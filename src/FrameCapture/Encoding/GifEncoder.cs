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
    public static byte[] Encode(IReadOnlyList<CapturedFrame> frames, IReadOnlyList<int> delaysMs, bool loop = true,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (frames.Count == 0) throw new ArgumentException("no frames");
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

    // ---------------- 量化：median-cut → ≤256 色 ----------------

    static (byte[] Indices, byte[] Palette, int EntryBits) Quantize(byte[] bgra, int w, int h)
    {
        // 1) 收集样本颜色（大步长抽样）
        int total = w * h;
        int step = Math.Max(1, total / 60_000);
        var samples = new List<int>(60_000);
        var counts = new Dictionary<int, int>();
        for (int p = 0; p < total; p += step)
        {
            int o = p * 4;
            // BGR → 5bit 归约键
            int key = (bgra[o + 2] >> 3 << 10) | (bgra[o + 1] >> 3 << 5) | (bgra[o] >> 3);
            if (counts.TryGetValue(key, out int c)) counts[key] = c + 1; else counts[key] = 1;
        }

        // 2) median-cut 分桶
        var boxes = new List<ColorBox>();
        var all = new List<(int Key, int Count)>(counts.Select(kv => (kv.Key, kv.Value)));
        boxes.Add(new ColorBox(all));
        while (boxes.Count < 256)
        {
            // 找最长通道且桶内颜色最多的 box
            ColorBox? best = null; int bestRange = -1, bestAxis = 0, bestCount = -1;
            foreach (var box in boxes)
            {
                if (box.Colors.Count < 2) continue;
                int span = box.Span(out int axis);
                if (span > bestRange || (span == bestRange && box.Colors.Count > bestCount))
                { best = box; bestRange = span; bestAxis = axis; bestCount = box.Colors.Count; }
            }
            if (best == null || bestRange <= 0) break;
            var sorted = best.Colors.OrderBy(x => AxisKey(x.Key, bestAxis)).ToList();
            int mid = sorted.Count / 2;
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
                r += (long)((key >> 10 & 31) << 3) * count;
                g += (long)((key >> 5 & 31) << 3) * count;
                b2 += (long)((key & 31) << 3) * count;
                wt += count;
            }
            if (wt == 0) wt = 1;
            palette[i * 3] = (byte)(r / wt); // GIF 调色板 = RGB 顺序
            palette[i * 3 + 1] = (byte)(g / wt);
            palette[i * 3 + 2] = (byte)(b2 / wt);
        }

        // 4) 像素 → 最近调色板色（带 5bit 键缓存）
        var cache = new int[32768];
        Array.Fill(cache, -1);
        var indices = new byte[total];
        for (int p = 0; p < total; p++)
        {
            int o = p * 4;
            int key = (bgra[o + 2] >> 3 << 10) | (bgra[o + 1] >> 3 << 5) | (bgra[o] >> 3);
            int idx = cache[key];
            if (idx < 0)
            {
                int r = (key >> 10 & 31) << 3, g = (key >> 5 & 31) << 3, b3 = (key & 31) << 3;
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
            indices[p] = (byte)idx;
        }
        return (indices, palette, entryBits);

        static int AxisKey(int key, int axis) => axis == 0 ? (key >> 10) : axis == 1 ? (key >> 5 & 31) : (key & 31);
    }

    sealed class ColorBox
    {
        public List<(int Key, int Count)> Colors { get; }
        public ColorBox(List<(int Key, int Count)> colors) => Colors = colors;
        public int Span(out int axis)
        {
            int maxR = 0, minR = 31, maxG = 0, minG = 31, maxB = 0, minB = 31;
            foreach (var (key, _) in Colors)
            {
                int r = key >> 10 & 31, g = key >> 5 & 31, b = key & 31;
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

    // ---------------- LZW（GIF 变体：低位在前） ----------------

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
}
