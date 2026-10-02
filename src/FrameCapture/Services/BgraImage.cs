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
