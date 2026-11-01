using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NekoCapture.Core;
using NekoCapture.Runtime;
using NekoCapture.Services;

static partial class SelfTest
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", EntryPoint = "DestroyIcon")]
    private static extern bool TrayDestroyIcon(IntPtr icon);

    private static void TrayIconChecks()
    {
        string? priorDirectory = Environment.GetEnvironmentVariable("NC_SETTINGS_DIR");
        string priorAccent = Theme.Accent.ToString(); bool dark = Theme.IsDark;
        string directory = Path.Combine(SettingsService.Dir, "tray-icons");
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("NC_SETTINGS_DIR", directory);
        var make = typeof(TrayService).GetMethod("MakeIcon", BindingFlags.Static | BindingFlags.NonPublic)!;
        Icon Make(bool paused) => (Icon)make.Invoke(null, new object[] { paused })!;
        bool Blue(Color pixel) => Math.Abs(pixel.R - 76) <= 3 && Math.Abs(pixel.G - 194) <= 3 && pixel.B >= 252;
        try
        {
            Theme.Apply("dark", "#4CC2FF");
            string path = Path.Combine(directory, "roundtrip.ico");
            using (var icon = Make(false)) using (var output = File.Create(path)) icon.Save(output);
            byte[] data = File.ReadAllBytes(path);
            int frames = BitConverter.ToUInt16(data, 4);
            Chk("tray-icon-saves-multiple-32-bit-png-frames", frames >= 13 && Enumerable.Range(0, frames).All(i =>
            {
                int entry = 6 + i * 16, offset = BitConverter.ToInt32(data, entry + 12);
                return BitConverter.ToUInt16(data, entry + 6) == 32 && data[offset] == 137 && data[offset + 1] == 80;
            }));
            foreach (int size in Enumerable.Range(4, 13).Select(i => i * 4))
            {
                using var icon = new Icon(path, size, size);
                using var bitmap = icon.ToBitmap();
                Chk("tray-icon-roundtrip-preserves-color-" + size, bitmap.Width == size && Blue(bitmap.GetPixel(size / 2, size / 2)));
                Chk("tray-icon-roundtrip-preserves-alpha-" + size, bitmap.GetPixel(0, 0).A == 0
                    && Enumerable.Range(0, size * size).Any(i => bitmap.GetPixel(i % size, i / size).A is > 0 and < 255));
                int margin = Math.Max(1, (int)Math.Round(size * 0.1)), stroke = Math.Max(1, (int)Math.Round(size * 0.0938));
                Chk("tray-icon-straight-edge-is-pixel-aligned-" + size, bitmap.GetPixel(margin + stroke, margin).A == 255
                    && bitmap.GetPixel(margin + stroke, margin - 1).A == 0);
                bitmap.Save(Path.Combine(directory, "working-" + size + ".png"));
            }
            var preferred = TrayIconSource.PreferredSize();
            IntPtr context = SetThreadDpiAwarenessContext(new IntPtr(-1));
            IntPtr small = TrayIconSource.LoadFile(path);
            try
            {
                using var borrowed = Icon.FromHandle(small);
                using var bitmap = borrowed.ToBitmap();
                Chk("tray-icon-native-loader-preserves-color", Blue(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2)));
                Chk("tray-icon-native-loader-uses-physical-taskbar-size", bitmap.Width == preferred.Width && bitmap.Height == preferred.Height,
                    $"loaded={bitmap.Width}x{bitmap.Height} taskbar={preferred.Width}x{preferred.Height}");
                bitmap.Save(Path.Combine(directory, "native-small.png"));
            }
            finally { if (small != IntPtr.Zero) TrayDestroyIcon(small); if (context != IntPtr.Zero) SetThreadDpiAwarenessContext(context); }
            using (var paused = Make(true))
            using (var bitmap = paused.ToBitmap())
            {
                var pixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
                Chk("tray-icon-paused-stays-gray", pixel.R == 154 && pixel.G == 160 && pixel.B == 166);
            }
            TrayService.SaveResidentIcons();
            string normal = Path.Combine(directory, "resident-tray.ico"), pausedPath = Path.Combine(directory, "resident-tray-paused.ico");
            DateTime stamp = File.GetLastWriteTimeUtc(normal);
            TrayService.SaveResidentIcons();
            Chk("tray-icon-cache-reuses-unchanged-pair", File.GetLastWriteTimeUtc(normal) == stamp);
            File.Delete(pausedPath); TrayService.SaveResidentIcons();
            Chk("tray-icon-cache-restores-missing-paused-file", File.Exists(pausedPath));

            string other = Path.Combine(directory, "other"); Directory.CreateDirectory(other);
            using (var stale = Make(true))
                foreach (string name in new[] { "resident-tray.ico", "resident-tray-paused.ico" })
                    using (var output = File.Create(Path.Combine(other, name))) stale.Save(output);
            Environment.SetEnvironmentVariable("NC_SETTINGS_DIR", other); TrayService.SaveResidentIcons();
            using (var icon = new Icon(Path.Combine(other, "resident-tray.ico")))
            using (var bitmap = icon.ToBitmap())
                Chk("tray-icon-cache-is-scoped-to-settings-directory", Blue(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2)));

            Environment.SetEnvironmentVariable("NC_SETTINGS_DIR", directory);
            var settings = new SettingsService();
            var write = typeof(ResidentBridge).GetMethod("WriteManifest", BindingFlags.Static | BindingFlags.NonPublic)!;
            write.Invoke(null, new object[] { settings }); var first = ResidentProtocol.ReadManifest()!;
            Theme.Apply("dark", "#E66B42");
            write.Invoke(null, new object[] { settings }); var second = ResidentProtocol.ReadManifest()!;
            Chk("tray-icon-color-change-invalidates-resident-manifest", first.IconRevision != second.IconRevision
                && second.IconRevision == TrayService.ResidentIconRevision && first.SourceTicks == second.SourceTicks);
        }
        catch (Exception error) { Chk("tray-icon-exception", false, error.ToString()); }
        finally
        {
            Environment.SetEnvironmentVariable("NC_SETTINGS_DIR", priorDirectory);
            Theme.Apply(dark ? "dark" : "light", priorAccent);
        }
    }
}
