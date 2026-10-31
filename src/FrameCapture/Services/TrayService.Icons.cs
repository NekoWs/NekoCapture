using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using FrameCapture.Core;

namespace FrameCapture.Services;

/// <summary>托盘图标的多尺寸 RGBA 编码与常驻进程缓存。</summary>
public sealed partial class TrayService
{
    private static string? _residentIconCacheKey;

    /// <summary>图标编码格式与当前强调色组成的缓存身份。</summary>
    internal static string ResidentIconRevision => "png32-v1:" + Theme.Accent;

    /// <summary>向当前配置目录写入正常与暂停图标，保留真彩色和透明边缘。</summary>
    internal static void SaveResidentIcons()
    {
        string directory = Path.GetFullPath(SettingsService.Dir);
        string[] paths = [Path.Combine(directory, "resident-tray.ico"), Path.Combine(directory, "resident-tray-paused.ico")];
        string key = directory + "|" + ResidentIconRevision;
        if (_residentIconCacheKey == key && paths.All(File.Exists)) return;
        Directory.CreateDirectory(directory);
        for (int i = 0; i < paths.Length; i++)
        {
            string temporary = paths[i] + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var icon = MakeIcon(paused: i != 0))
                using (var file = File.Create(temporary)) icon.Save(file);
                File.Move(temporary, paths[i], overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        _residentIconCacheKey = key;
    }

    private static Icon MakeIcon(bool paused)
    {
        using var bitmap = DrawTrayIcon(paused);
        var images = new List<(int Size, byte[] Png)>();
        foreach (int size in new[] { 16, 20, 24, 32, 40, 48, 64 })
        {
            using var frame = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(frame))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(bitmap, new Rectangle(0, 0, size, size));
            }
            using var png = new MemoryStream();
            frame.Save(png, ImageFormat.Png);
            images.Add((size, png.ToArray()));
        }

        // 从完整 ICO 数据创建 Icon，Save 可直接保留 PNG 图层及其 32 位透明度。
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)images.Count);
            int offset = 6 + images.Count * 16;
            foreach (var image in images)
            {
                writer.Write((byte)image.Size); writer.Write((byte)image.Size);
                writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(image.Png.Length); writer.Write(offset);
                offset += image.Png.Length;
            }
            foreach (var image in images) writer.Write(image.Png);
        }
        stream.Position = 0;
        return new Icon(stream, 64, 64);
    }

    private static Bitmap DrawTrayIcon(bool paused)
    {
        const int canvas = 64;
        const float inset = 0.1465f * canvas, arm = 0.2441f * canvas, stroke = 0.0938f * canvas, dot = 0.2930f * canvas;
        var bitmap = new Bitmap(canvas, canvas, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var accent = paused ? Color.FromArgb(255, 0x9A, 0xA0, 0xA6)
            : Color.FromArgb(255, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B);
        using var pen = new Pen(accent, stroke) { StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Round };
        float lo = inset, hi = canvas - inset, mid = canvas / 2f;
        foreach (var (x, y, dx, dy) in new[] { (lo, lo, 1f, 1f), (hi, lo, -1f, 1f), (lo, hi, 1f, -1f), (hi, hi, -1f, -1f) })
        {
            using var path = new GraphicsPath();
            path.AddLines([new PointF(x + dx * arm, y), new PointF(x, y), new PointF(x, y + dy * arm)]);
            graphics.DrawPath(pen, path);
        }
        using var fill = new SolidBrush(accent);
        graphics.FillEllipse(fill, mid - dot / 2, mid - dot / 2, dot, dot);
        return bitmap;
    }
}
