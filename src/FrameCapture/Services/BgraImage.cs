using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;

namespace FrameCapture.Services;

/// <summary>BGRA 帧与 WPF BitmapSource 互转 + PNG 编码。</summary>
public static class BgraImage
{
    public static BitmapSource ToSource(CapturedFrame f)
    {
        var src = BitmapSource.Create(f.W, f.H, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.W * 4);
        src.Freeze();
        return src;
    }

    /// <summary>
    /// BitmapSource → BGRA 帧：从剪贴板图片等外部源建帧。统一转 Bgra32 再 CopyPixels，
    /// 与 ToSource 同格式 → 往返无损（SelfTest ）。空/零尺寸返回 null。
    /// </summary>
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

    public static void SavePng(CapturedFrame f, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(ToSource(f)));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>帧 → PNG 字节（保留 alpha）。用于剪贴板 "PNG" 格式（窗口截图透明）。</summary>
    public static byte[] PngBytes(CapturedFrame f)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(ToSource(f)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>把带 alpha 的帧拍平到纯色底（alpha=255）。给只认 CF_DIB（无 alpha）的应用做兜底，
    /// 避免透明区在它们眼里变黑。不透明帧原样返回。</summary>
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
    /// 编辑器预览与烘焙共用同一函数 → 所见即所得（SelfTest ）。
    /// </summary>
    public static void MosaicInPlace(byte[] bgra, int w, int h, int x, int y, int rw, int rh, int block)
    {
        if (block < 1) block = 1;
        x = Math.Clamp(x, 0, Math.Max(0, w)); y = Math.Clamp(y, 0, Math.Max(0, h));
        rw = Math.Clamp(rw, 0, w - x); rh = Math.Clamp(rh, 0, h - y);
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

    /// <summary>视觉暂留合成：多帧加权叠加（后帧更亮）。</summary>
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
            // 旧像素衰减
            for (int i = 0; i < acc.Length; i++) acc[i] *= 0.82f;
            // 新像素以 max 融合
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

    public static List<int> SampleIndices(int count, int samples)
    {
        if (count <= samples) return Enumerable.Range(0, count).ToList();
        var list = new List<int>();
        for (int i = 0; i < samples; i++)
            list.Add((int)((long)i * (count - 1) / (samples - 1)));
        return list;
    }
}
