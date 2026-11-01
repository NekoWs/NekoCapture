using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using NekoCapture.Core;
using NekoCapture.Runtime;

namespace NekoCapture.Services;

/// <summary>托盘图标的多尺寸 RGBA 编码与常驻进程缓存。</summary>
public sealed partial class TrayService
{
    private static string? _residentIconCacheKey;

    /// <summary>图标编码、任务栏尺寸与当前强调色组成的缓存身份。</summary>
    internal static string ResidentIconRevision
    {
        get
        {
            var size = TrayIconSource.PreferredSize();
            return $"png32-pixel-v2:{size.Width}x{size.Height}:{Theme.Accent}";
        }
    }

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
        var preferred = TrayIconSource.PreferredSize();
        var images = new List<(int Size, byte[] Png)>();
        var sizes = Enumerable.Range(4, 13).Select(i => i * 4).Append(preferred.Width).Append(preferred.Height).Distinct().Order();
        foreach (int size in sizes)
        {
            using var frame = DrawTrayIcon(size, paused);
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
        return new Icon(stream, preferred.Width, preferred.Height);
    }

    private static Bitmap DrawTrayIcon(int size, bool paused)
    {
        int stroke = Math.Max(1, (int)Math.Round(size * 0.0938)), margin = Math.Max(1, (int)Math.Round(size * 0.10));
        int arm = Math.Max(stroke + 1, (int)Math.Round(size * 0.29));
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        var accent = paused ? Color.FromArgb(255, 0x9A, 0xA0, 0xA6)
            : Color.FromArgb(255, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B);
        using var fill = new SolidBrush(accent);
        graphics.SmoothingMode = SmoothingMode.None;
        foreach (bool right in new[] { false, true })
        foreach (bool bottom in new[] { false, true })
        {
            int x = right ? size - margin - arm : margin, y = bottom ? size - margin - arm : margin;
            graphics.FillRectangle(fill, x, bottom ? size - margin - stroke : margin, arm, stroke);
            graphics.FillRectangle(fill, right ? size - margin - stroke : margin, y, stroke, arm);
        }
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int dot = Math.Max(3, (int)Math.Round(size * 0.293));
        float origin = (size - dot) / 2f;
        graphics.FillEllipse(fill, origin, origin, dot, dot);
        return bitmap;
    }
}
