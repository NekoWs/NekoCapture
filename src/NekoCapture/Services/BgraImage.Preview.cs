using NekoCapture.Capture;

namespace NekoCapture.Services;

/// <summary>按目标尺寸生成独立缩略像素，不保留全尺寸位图。</summary>
public static partial class BgraImage
{
    /// <summary>按比例缩小预览，每个目标像素至多采样 16 个源像素，并正确混合透明通道。</summary>
    /// <param name="frame">只读源帧。</param>
    /// <param name="maxWidth">目标最大宽度，像素。</param>
    /// <param name="maxHeight">目标最大高度，像素。</param>
    /// <returns>独立的小尺寸 BGRA 帧；不放大源画面。</returns>
    internal static CapturedFrame Preview(CapturedFrame frame, int maxWidth, int maxHeight)
    {
        double scale = Math.Min(1, Math.Min((double)Math.Max(1, maxWidth) / frame.W, (double)Math.Max(1, maxHeight) / frame.H));
        int width = Math.Max(1, (int)Math.Round(frame.W * scale)), height = Math.Max(1, (int)Math.Round(frame.H * scale));
        byte[] pixels = new byte[width * height * 4];
        int nx = Math.Clamp(frame.W / width, 1, 4), ny = Math.Clamp(frame.H / height, 1, 4), samples = nx * ny;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int alpha = 0, blue = 0, green = 0, red = 0;
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int sy = Math.Min(frame.H - 1, (int)((y + (j + 0.5) / ny) * frame.H / height));
                int sx = Math.Min(frame.W - 1, (int)((x + (i + 0.5) / nx) * frame.W / width));
                int source = (sy * frame.W + sx) * 4, a = frame.Bgra[source + 3];
                alpha += a; blue += frame.Bgra[source] * a; green += frame.Bgra[source + 1] * a; red += frame.Bgra[source + 2] * a;
            }
            int target = (y * width + x) * 4;
            pixels[target + 3] = (byte)((alpha + samples / 2) / samples);
            if (alpha == 0) continue;
            pixels[target] = (byte)((blue + alpha / 2) / alpha);
            pixels[target + 1] = (byte)((green + alpha / 2) / alpha);
            pixels[target + 2] = (byte)((red + alpha / 2) / alpha);
        }
        return new CapturedFrame { W = width, H = height, Bgra = pixels, TimeMs = frame.TimeMs };
    }
}
