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

    public static void SavePng(CapturedFrame f, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(ToSource(f)));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>
    /// 马赛克（R47）：区域内逐 block 均值填充，就地修改。坐标越界自动钳进画布；纯字节运算，
    /// 编辑器预览与烘焙共用同一函数 → 所见即所得（SelfTest T91）。
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
