using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;

namespace FrameCapture.Services;

/// <summary>BGRA 帧与 WPF BitmapSource 互转 + PNG 编码。</summary>
public static partial class BgraImage
{
    /// <summary>
    /// BGRA 帧 → WPF <see cref="BitmapSource"/>：Bgra32、96×96 DPI、行距 <c>W*4</c>（无 padding），返回已 Freeze 的对象。
    /// </summary>
    /// <param name="f">源帧。<c>Bgra.Length</c> 须等于 <c>W*H*4</c>，否则 WPF 侧抛参数异常。</param>
    /// <returns>冻结位图，可跨线程共享与长期持有；像素是创建时的一次拷贝，之后改帧字节不影响它。</returns>
    public static BitmapSource ToSource(CapturedFrame f)
    {
        var src = BitmapSource.Create(f.W, f.H, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.W * 4);
        src.Freeze();
        return src;
    }

    /// <summary>
    /// BitmapSource → BGRA 帧：从剪贴板图片等外部源建帧。统一转 Bgra32 再 CopyPixels，
    /// 与 <see cref="ToSource"/> 同格式 → 往返无损（SelfTest 有往返用例）。
    /// </summary>
    /// <param name="src">任意外部位图源，可以是未冻结的、也可以是任意 <c>PixelFormats</c>。</param>
    /// <returns>新帧，<c>TimeMs</c> 恒为 0（外部源没有捕获时刻概念）；null 或零尺寸返回 null。</returns>
    public static CapturedFrame? FromSource(BitmapSource? src)
    {
        if (src == null || src.PixelWidth <= 0 || src.PixelHeight <= 0) return null;
        var conv = src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var bgra = new byte[stride * h];
        conv.CopyPixels(bgra, stride, 0);
        return new CapturedFrame { Bgra = bgra, W = w, H = h, TimeMs = 0 };
    }

    /// <summary>
    /// 帧 → PNG 文件。父目录不存在时自动创建；同名文件被覆盖（不做唯一化，唯一命名由调用方负责）。
    /// </summary>
    /// <param name="f">源帧。</param>
    /// <param name="path">目标文件路径，扩展名由调用方决定——PNG 编码由内容决定，与扩展名无关。</param>
    /// <remarks>磁盘/权限异常直接抛出，由调用方（自动保存、编辑器导出）各自决定是记 trace 还是提示用户。</remarks>
    public static void SavePng(CapturedFrame f, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(ToSource(f)));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>帧 → PNG 字节（保留 alpha）。用于剪贴板 "PNG" 格式（窗口截图透明）。</summary>
    /// <param name="f">源帧。</param>
    /// <returns>完整 PNG 文件字节（含文件头），可直接作为流内容写出；alpha 通道原样保留。</returns>
    public static byte[] PngBytes(CapturedFrame f)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(ToSource(f)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>把带 alpha 的帧拍平到纯色底（输出 alpha=255）。给只认 CF_DIB（无 alpha）的应用做兜底，
    /// 避免透明区在它们眼里变黑。不透明帧原样返回。</summary>
    /// <param name="f">源帧。</param>
    /// <param name="r">底色红通道，0–255。三个通道按 <b>RGB</b> 顺序给出，函数内部自行落到 BGRA 字节序。</param>
    /// <param name="g">底色绿通道，0–255。</param>
    /// <param name="b">底色蓝通道，0–255。</param>
    /// <returns>全不透明新帧（沿用 <c>f</c> 的 <c>W/H/TimeMs</c>）；源帧本就无半透明像素时返回 <c>f</c> 本身，不拷贝。</returns>
    public static CapturedFrame FlattenOver(CapturedFrame f, byte r, byte g, byte b)
    {
        var src = f.Bgra;
        bool anyAlpha = false;
        for (int i = 3; i < src.Length; i += 4) if (src[i] != 255) { anyAlpha = true; break; }
        if (!anyAlpha) return f;
        var dst = new byte[src.Length];
        for (int i = 0; i < src.Length; i += 4)
        {
            double a = src[i + 3] / 255.0;
            dst[i] = (byte)(src[i] * a + b * (1 - a));
            dst[i + 1] = (byte)(src[i + 1] * a + g * (1 - a));
            dst[i + 2] = (byte)(src[i + 2] * a + r * (1 - a));
            dst[i + 3] = 255;
        }
        return new CapturedFrame { Bgra = dst, W = f.W, H = f.H, TimeMs = f.TimeMs };
    }

    /// <summary>从帧里裁出 (x,y,w,h) 子矩形（越界自动钳进画布；裁空返回 null）。逐行拷贝。
    /// 定格截图用它从全屏定格帧里裁出选区。纯函数供单测。</summary>
    /// <param name="src">源帧，null 或零尺寸返回 null。</param>
    /// <param name="x">子矩形左上角 X，源帧像素坐标，允许为负（与画布求交后收缩）。</param>
    /// <param name="y">子矩形左上角 Y，源帧像素坐标，允许为负。</param>
    /// <param name="w">子矩形宽，像素；小于 1 返回 null。</param>
    /// <param name="h">子矩形高，像素；小于 1 返回 null。</param>
    /// <returns>裁出新帧，尺寸为<b>交集</b>而非请求尺寸（贴边时会更小），<c>TimeMs</c> 归 0；交集为空返回 null。</returns>
    public static CapturedFrame? Crop(CapturedFrame? src, int x, int y, int w, int h)
    {
        if (src == null || src.W <= 0 || src.H <= 0 || w < 1 || h < 1) return null;
        // 请求矩形 [x,x+w)×[y,y+h) 与画布 [0,W)×[0,H) 求交（负偏移正确收缩，不向右多取）
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(src.W, x + w), y1 = Math.Min(src.H, y + h);
        int cw = x1 - x0, ch = y1 - y0;
        if (cw < 1 || ch < 1) return null;
        var dst = new byte[(long)cw * ch * 4];
        for (int r = 0; r < ch; r++)
            Buffer.BlockCopy(src.Bgra, (int)(((long)(y0 + r) * src.W + x0) * 4), dst, (int)((long)r * cw * 4), cw * 4);
        return new CapturedFrame { Bgra = dst, W = cw, H = ch, TimeMs = 0 };
    }

    /// <summary>
    /// 马赛克：区域内逐 block 均值填充，就地修改。坐标越界自动钳进画布；纯字节运算，
    /// 编辑器预览与烘焙共用同一函数 → 所见即所得（SelfTest 有用例）。
    /// </summary>
    /// <param name="bgra">画布本体，就地改写（行主序 BGRA，行距 <c>w*4</c>，长度须为 <c>w*h*4</c>）。</param>
    /// <param name="w">画布宽，像素。</param>
    /// <param name="h">画布高，像素。</param>
    /// <param name="x">马赛克区左上角 X，画布像素坐标；负值钳到 0。</param>
    /// <param name="y">马赛克区左上角 Y，画布像素坐标；负值钳到 0。</param>
    /// <param name="rw">马赛克区宽，超出画布部分被裁掉；钳后为 0 则整帧不变。</param>
    /// <param name="rh">马赛克区高，同上。</param>
    /// <param name="block">块边长，像素；小于 1 按 1 处理（等价于不模糊）。边缘不足一块的按实际剩余像素求均值。</param>
    public static void MosaicInPlace(byte[] bgra, int w, int h, int x, int y, int rw, int rh, int block)
    {
        if (block < 1) block = 1;
        int right = (int)Math.Clamp((long)x + Math.Max(0, rw), 0, w);
        int bottom = (int)Math.Clamp((long)y + Math.Max(0, rh), 0, h);
        x = Math.Clamp(x, 0, Math.Max(0, w)); y = Math.Clamp(y, 0, Math.Max(0, h));
        rw = Math.Max(0, right - x); rh = Math.Max(0, bottom - y);
        for (int by = y; by < y + rh; by += block)
        for (int bx = x; bx < x + rw; bx += block)
        {
            int bw2 = Math.Min(block, x + rw - bx), bh2 = Math.Min(block, y + rh - by);
            long sr = 0, sg = 0, sb = 0, sa = 0; int n = 0;
            for (int py = by; py < by + bh2; py++)
            for (int px = bx; px < bx + bw2; px++)
            {
                int i = (py * w + px) * 4;
                sb += bgra[i]; sg += bgra[i + 1]; sr += bgra[i + 2]; sa += bgra[i + 3]; n++;
            }
            byte b = (byte)(sb / n), g = (byte)(sg / n), r = (byte)(sr / n), a = (byte)(sa / n);
            for (int py = by; py < by + bh2; py++)
            for (int px = bx; px < bx + bw2; px++)
            {
                int i = (py * w + px) * 4;
                bgra[i] = b; bgra[i + 1] = g; bgra[i + 2] = r; bgra[i + 3] = a;
            }
        }
    }

    /// <summary>视觉暂留合成：多帧加权叠加（累积值逐帧衰减后按 max 融合，故后帧更亮）。</summary>
    /// <param name="frames">按时间顺序的帧序列。<b>只按下标稀疏取帧</b>，不整段遍历——
    /// 调用方可传惰性 / 磁盘暂存列表，长录制不会因此全量进内存。</param>
    /// <param name="outputFrames">参与合成的采样帧数，须 ≥ 2（实际参与数不超过 <c>frames.Count</c>）。</param>
    /// <returns>最后一张合成帧：尺寸取 <c>frames[0]</c>，<c>TimeMs</c> 取该采样帧。
    /// 尺寸与首帧不一致的采样帧被跳过；签名非空，调用方须保证采样帧同尺寸。</returns>
    /// <exception cref="ArgumentException"><c>frames</c> 为空。</exception>
    public static CapturedFrame Afterimage(IReadOnlyList<CapturedFrame> frames, int outputFrames = 5)
    {
        if (frames.Count == 0) throw new ArgumentException("empty");
        int w = frames[0].W, h = frames[0].H;
        var acc = new float[w * h * 4];
        int emitted = 0;
        var samples = SampleIndices(frames.Count, outputFrames);
        CapturedFrame? lastOut = null;
        foreach (var idx in samples)
        {
            var f = frames[idx];
            if (f.W != w || f.H != h) continue;
            // 累积值先衰减，后帧因此压过前帧
            for (int i = 0; i < acc.Length; i++) acc[i] *= 0.82f;
            // 本帧像素以 max 融合
            var src = f.Bgra;
            for (int i = 0; i < acc.Length; i++)
                if (src[i] > acc[i]) acc[i] = src[i];
            var dst = new byte[acc.Length];
            for (int i = 0; i < acc.Length; i++) dst[i] = (byte)Math.Clamp((int)acc[i], 0, 255);
            lastOut = new CapturedFrame { Bgra = dst, W = w, H = h, TimeMs = f.TimeMs };
            emitted++;
        }
        return lastOut!;
    }

    /// <summary>
    /// 在 <c>0..count-1</c> 上均匀取 <c>samples</c> 个下标，<b>首尾必取</b>（合成图必然包含最后一帧）。
    /// </summary>
    /// <param name="count">总帧数，可为 0（返回空表）。</param>
    /// <param name="samples">期望采样数。<c>count ≤ samples</c> 时全取；否则须 ≥ 2，否则分母为 0。</param>
    /// <returns>升序下标表；整数除法保证不重复、不越界。</returns>
    public static List<int> SampleIndices(int count, int samples)
    {
        if (count <= samples) return Enumerable.Range(0, count).ToList();
        var list = new List<int>();
        for (int i = 0; i < samples; i++)
            list.Add((int)((long)i * (count - 1) / (samples - 1)));
        return list;
    }
}
