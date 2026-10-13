using System.IO;
using FrameCapture.Capture;

namespace FrameCapture.Encoding;

/// <summary>
/// GIF89a 编码器：逐帧 median-cut 调色板 + LZW + Netscape 循环扩展 + 百分秒延迟。
/// 纯托管实现，无外部依赖。
/// </summary>
public static class GifEncoder
{
    /// <summary>
    /// frames 为 BGRA 帧；delaysMs 每帧显示时长；loop=true 无限循环。
    /// progress 报告已完成帧数；ct 可取消（抛 OperationCanceledException）。多帧自动并行。
    /// </summary>
    /// <param name="frames">BGRA 帧序列。<b>逻辑屏幕描述符只写首帧的宽高</b>，因此全部帧必须同宽同高。</param>
    /// <param name="delaysMs">与 frames 一一对应的显示时长（毫秒）；厘秒量化可能删帧，见 Core.GifTiming。</param>
    /// <param name="loop">true = 写 Netscape 扩展无限循环；false = 播一遍停住。</param>
    /// <param name="progress">报告已完成帧数（计数，不是百分比），可为 null。</param>
    /// <param name="ct">取消令牌；取消时抛 <see cref="OperationCanceledException"/>，已编码结果整个丢弃。</param>
    /// <returns>完整 GIF89a 字节流，可直接落盘或进剪贴板。</returns>
    /// <exception cref="ArgumentException">frames 为空。</exception>
    public static byte[] Encode(IReadOnlyList<CapturedFrame> frames, IReadOnlyList<int> delaysMs, bool loop = true,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (frames.Count == 0) throw new ArgumentException("no frames");
        // 厘秒重采样：60fps 直存会整体提速 1.2×；重采样保总时长（Core.GifTiming 纯逻辑，断言）
        var (keep, qDelays) = FrameCapture.Core.GifTiming.Resample(delaysMs);
        if (keep.Count < frames.Count)
        {
            var kf = new List<CapturedFrame>(keep.Count);
            foreach (var i in keep) kf.Add(frames[i]);
            frames = kf;
        }
        delaysMs = qDelays; // 即使不丢帧（30fps 的 33→30/40 交替）也要用量化后的延迟
        int w = frames[0].W, h = frames[0].H;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        void B(byte v) => bw.Write(v);
        void W16(int v) => bw.Write((ushort)v);
        void LE32(int v) => bw.Write(v);

        bw.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
        W16(w); W16(h);
        B(0); B(0); B(0); // 无全局色表

        if (loop)
        {
            B(0x21); B(0xFF); B(11);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            B(3); B(1); W16(0); B(0);
        }

        var parts = EncodeFrames(frames, delaysMs, progress, ct);
        foreach (var part in parts) bw.Write(part);

        B(0x3B);
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>逐帧独立编码（可并行）：每帧产出一段完整 GIF 记录（GCE+描述符+色表+LZW）。</summary>
    static byte[][] EncodeFrames(IReadOnlyList<CapturedFrame> frames, IReadOnlyList<int> delaysMs,
        IProgress<int>? progress, CancellationToken ct)
    {
        int n = frames.Count;
        var parts = new byte[n][];
        int done = 0;
        if (n > 3 && Environment.ProcessorCount > 1)
        {
            System.Threading.Tasks.Parallel.For(0, n,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
                fi =>
                {
                    ct.ThrowIfCancellationRequested();
                    parts[fi] = EncodeOne(frames[fi], delaysMs[fi]);
                    int d = System.Threading.Interlocked.Increment(ref done);
                    progress?.Report(d);
                });
        }
        else
        {
            for (int fi = 0; fi < n; fi++)
            {
                ct.ThrowIfCancellationRequested();
                parts[fi] = EncodeOne(frames[fi], delaysMs[fi]);
                progress?.Report(++done);
            }
        }
        return parts;
    }

    static byte[] EncodeOne(CapturedFrame frame, int delayMs)
    {
        int w = frame.W, h = frame.H;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        void B(byte v) => bw.Write(v);
        void W16(int v) => bw.Write((ushort)v);
        {
            int delay = Math.Clamp(delayMs, 10, 655_350) / 10;
            if (delay == 1) delay = 2; // 避免某些播放器把 1cs 当 0

            // GCE
            B(0x21); B(0xF9); B(4);
            B(0x08); // packed: disposal=1（保留上一帧）
            W16(delay);
            B(0x00); // transparency flag=0
            B(0x00); // GCE 块终止符

            var (indices, palette, entryBits) = Quantize(frame.Bgra, w, h);

            // Image Descriptor + 局部色表
            B(0x2C);
            W16(0); W16(0); W16(w); W16(h);
            int gctFlag = 0x80 | (entryBits - 1);
            B((byte)gctFlag);
            int tableSize = 1 << entryBits;
            for (int i = 0; i < tableSize; i++)
            {
                B(palette[i * 3]); B(palette[i * 3 + 1]); B(palette[i * 3 + 2]);
            }

            // LZW
            B((byte)entryBits);
            LzwCompress(indices, entryBits, bw);
        }
        bw.Flush();
        return ms.ToArray();
    }

    #region 量化：median-cut → ≤256 色

    static (byte[] Indices, byte[] Palette, int EntryBits) Quantize(byte[] bgra, int w, int h)
    {
        // 1) 收集样本颜色（大步长抽样）。6bit 归约键：5bit 会把暗渐变灰与暗杂色并进同桶，
        // 桶均值被拉离灰轴 → 渐变端头整段同档（抓到）
        int total = w * h;
        int step = Math.Max(1, total / 60_000);
        var counts = new Dictionary<int, int>();
        for (int p = 0; p < total; p += step)
        {
            int o = p * 4;
            int key = (bgra[o + 2] >> 2 << 12) | (bgra[o + 1] >> 2 << 6) | (bgra[o] >> 2);
            if (counts.TryGetValue(key, out int c)) counts[key] = c + 1; else counts[key] = 1;
        }

        // 2) median-cut 分桶（按 跨度×像素数 加权选分裂桶——纯 span 优先会让杂色垄断调色板，
        // 大面积渐变被挤成两三档，抖动也救不回顶部行）
        var boxes = new List<ColorBox>();
        var all = new List<(int Key, int Count)>(counts.Select(kv => (kv.Key, kv.Value)));
        boxes.Add(new ColorBox(all));
        while (boxes.Count < 256)
        {
            ColorBox? best = null; long bestScore = -1; int bestAxis = 0;
            foreach (var box in boxes)
            {
                if (box.Colors.Count < 2) continue;
                int span = box.Span(out int axis);
                long score = (long)span * box.TotalCount;
                if (score > bestScore) { best = box; bestScore = score; bestAxis = axis; }
            }
            if (best == null || bestScore <= 0) break;
            var sorted = best.Colors.OrderBy(x => AxisKey(x.Key, bestAxis)).ToList();
            // 按像素数取中点（而非颜色条数中点）：分裂后两桶像素量均衡，符合 median-cut 本义
            long half = best.TotalCount / 2, acc = 0; int mid = 0;
            for (int i = 0; i < sorted.Count; i++) { acc += sorted[i].Count; if (acc >= half) { mid = i + 1; break; } }
            mid = Math.Clamp(mid, 1, sorted.Count - 1);
            var a = new ColorBox(sorted.GetRange(0, mid));
            var b = new ColorBox(sorted.GetRange(mid, sorted.Count - mid));
            boxes.Remove(best);
            boxes.Add(a); boxes.Add(b);
            if (boxes.Count >= 256) break;
        }

        // 3) 调色板（桶均值）
        int n = boxes.Count;
        // 补齐到 2 的幂
        int entryBits = 1; while ((1 << entryBits) < n) entryBits++;
        entryBits = Math.Max(entryBits, 2);
        int tableSize = 1 << entryBits;
        var palette = new byte[tableSize * 3];
        for (int i = 0; i < n; i++)
        {
            long r = 0, g = 0, b2 = 0, wt = 0;
            foreach (var (key, count) in boxes[i].Colors)
            {
                r += (long)(((key >> 12 & 63) << 2) + 1) * count;
                g += (long)(((key >> 6 & 63) << 2) + 1) * count;
                b2 += (long)(((key & 63) << 2) + 1) * count;
                wt += count;
            }
            if (wt == 0) wt = 1;
            palette[i * 3] = (byte)(r / wt); // GIF 调色板 = RGB 顺序
            palette[i * 3 + 1] = (byte)(g / wt);
            palette[i * 3 + 2] = (byte)(b2 / wt);
        }

        // 4) 像素 → 最近调色板色 + Floyd–Steinberg 误差扩散（蛇形扫描）。
        // 无抖动的中位切割在暗渐变上出明显带状纹（逐帧审查 GIF 产物发现）；
        // 误差全为 0 的平坦区直接跳过传播，文字边缘不引入噪点。
        // 7bit 键缓存：6bit 的 4 宽桶会把误差反馈量化成 4px 周期竖纹（真实帧 3x 放大抓到）
        var cache = new int[2097152];
        Array.Fill(cache, -1);
        var indices = new byte[total];
        var errBuf = ErrBuf(total);
        for (int y = 0; y < h; y++)
        {
            bool ltr = (y & 1) == 0;
            int sx = ltr ? 1 : -1;
            int rowBase = y * w;
            for (int xi = 0; xi < w; xi++)
            {
                int x = ltr ? xi : w - 1 - xi;
                int p = rowBase + x, o = p * 4, pe = p * 3;
                int r = Clamp255(bgra[o + 2] + (int)Math.Round(errBuf[pe]));
                int g = Clamp255(bgra[o + 1] + (int)Math.Round(errBuf[pe + 1]));
                int b3 = Clamp255(bgra[o] + (int)Math.Round(errBuf[pe + 2]));
                int key = (r >> 1 << 14) | (g >> 1 << 7) | (b3 >> 1);
                int idx = cache[key];
                if (idx < 0)
                {
                    int bestI = 0, bestD = int.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        int dr = palette[i * 3] - r, dg = palette[i * 3 + 1] - g, db = palette[i * 3 + 2] - b3;
                        int d = dr * dr + dg * dg + db * db;
                        if (d < bestD) { bestD = d; bestI = i; }
                    }
                    idx = bestI;
                    cache[key] = idx;
                }
                int er = r - palette[idx * 3], eg = g - palette[idx * 3 + 1], eb = b3 - palette[idx * 3 + 2];
                indices[p] = (byte)idx;
                // 微误差（三通道绝对值和 ≤2）不传播：源噪声/近平坦区保持平坦，不生成可见噪点；
                // 真渐变的误差会累积到阈值以上继续驱动抖动
                int aer = er < 0 ? -er : er, aeg = eg < 0 ? -eg : eg, aeb = eb < 0 ? -eb : eb;
                if (aer + aeg + aeb <= 2) continue;
                if (x + sx >= 0 && x + sx < w)
                {
                    int q = (p + sx) * 3;
                    errBuf[q] += er * 7f / 16f; errBuf[q + 1] += eg * 7f / 16f; errBuf[q + 2] += eb * 7f / 16f;
                }
                if (y + 1 < h)
                {
                    int q = (p + w) * 3;
                    errBuf[q] += er * 5f / 16f; errBuf[q + 1] += eg * 5f / 16f; errBuf[q + 2] += eb * 5f / 16f;
                    if (x - sx >= 0 && x - sx < w)
                    {
                        int q2 = (p + w - sx) * 3;
                        errBuf[q2] += er * 1f / 16f; errBuf[q2 + 1] += eg * 1f / 16f; errBuf[q2 + 2] += eb * 1f / 16f;
                    }
                }
            }
        }
        return (indices, palette, entryBits);

        static int AxisKey(int key, int axis) => axis == 0 ? (key >> 12) : axis == 1 ? (key >> 6 & 63) : (key & 63);
        static int Clamp255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
    }

    // 误差缓冲按帧复用（LOH 分配，逐帧 new 会造成 GC 抖动）
    [ThreadStatic] private static float[] _errBuf;
    private static float[] ErrBuf(int total)
    {
        var b = _errBuf;
        if (b == null || b.Length < total * 3) { b = new float[total * 3]; _errBuf = b; }
        else Array.Clear(b, 0, total * 3);
        return b;
    }

    sealed class ColorBox
    {
        public List<(int Key, int Count)> Colors { get; }
        public long TotalCount { get; }
        /// <summary>建一个色桶，顺带累计该桶覆盖的样本像素总数。</summary>
        /// <param name="colors">桶内条目：6bit 归约色键 + 该键的样本像素数。</param>
        public ColorBox(List<(int Key, int Count)> colors)
        {
            Colors = colors;
            long t = 0; foreach (var (_, c) in colors) t += c;
            TotalCount = t;
        }
        /// <summary>取该桶在三通道上跨度最大的一轴及其跨度，供 median-cut 决定往哪切。</summary>
        /// <param name="axis">输出分裂轴：0=R、1=G、2=B，对应 6bit 归约键的通道序。</param>
        /// <returns>该轴跨度（0–63）。并列时按 R、G、B 的优先序取轴，同一输入结果确定。</returns>
        public int Span(out int axis)
        {
            int maxR = 0, minR = 63, maxG = 0, minG = 63, maxB = 0, minB = 63;
            foreach (var (key, _) in Colors)
            {
                int r = key >> 12 & 63, g = key >> 6 & 63, b = key & 63;
                if (r > maxR) maxR = r; if (r < minR) minR = r;
                if (g > maxG) maxG = g; if (g < minG) minG = g;
                if (b > maxB) maxB = b; if (b < minB) minB = b;
            }
            int spanR = maxR - minR, spanG = maxG - minG, spanB = maxB - minB;
            if (spanR >= spanG && spanR >= spanB) { axis = 0; return spanR; }
            if (spanG >= spanB) { axis = 1; return spanG; }
            axis = 2; return spanB;
        }
    }
    #endregion

    #region LZW（GIF 变体：低位在前）

    /// <summary>
    /// GIF 变体 LZW：码字按<b>低位在前</b>压入位缓冲，每满 8 位吐一个字节，
    /// 字节再攒成最长 255 的子块并前置长度字节，末尾补 0 结束子块链。
    /// 位流方向反了解码器不会报错，只会读出满屏花码——方向属于格式契约，不是实现细节。
    /// </summary>
    static void LzwCompress(byte[] indices, int entryBits, BinaryWriter bw)
    {
        int minCodeSize = entryBits;
        int clearCode = 1 << minCodeSize;
        int eoiCode = clearCode + 1;
        int codeSize = minCodeSize + 1;
        int nextCode = eoiCode + 1;

        var block = new List<byte>(256);
        long bitBuf = 0;
        int bitCount = 0;
        void PutBits(int code, int bits)
        {
            bitBuf |= (long)code << bitCount;
            bitCount += bits;
            while (bitCount >= 8)
            {
                block.Add((byte)(bitBuf & 0xFF));
                bitBuf >>= 8;
                bitCount -= 8;
                if (block.Count == 255) { bw.Write((byte)255); bw.Write(block.ToArray()); block.Clear(); }
            }
        }
        void Flush()
        {
            if (bitCount > 0) { block.Add((byte)(bitBuf & 0xFF)); bitCount = 0; bitBuf = 0; }
            if (block.Count > 0) { bw.Write((byte)block.Count); bw.Write(block.ToArray()); block.Clear(); }
            bw.Write((byte)0); // 子块列表终止符
        }

        var dict = new Dictionary<int, int>(7000);
        void Reset()
        {
            dict.Clear();
            codeSize = minCodeSize + 1;
            nextCode = eoiCode + 1;
        }

        PutBits(clearCode, codeSize);
        Reset();

        if (indices.Length == 0) { PutBits(eoiCode, codeSize); Flush(); return; }
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int idx = indices[i];
            int combined = ((prefix << 8) | idx) & 0xFFFFFF;
            if (dict.TryGetValue(combined, out int found)) { prefix = found; continue; }
            PutBits(prefix, codeSize);
            if (nextCode < 4096)
            {
                dict[combined] = nextCode++;
                if (nextCode > (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                PutBits(clearCode, codeSize);
                Reset();
            }
            prefix = idx;
        }
        PutBits(prefix, codeSize);
        PutBits(eoiCode, codeSize);
        Flush();
    }
    #endregion

}
