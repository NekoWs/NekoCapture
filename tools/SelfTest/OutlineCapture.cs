using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NekoCapture.Capture;
using NekoCapture.Core;
using NekoCapture.Overlays;

static partial class SelfTest
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    private static void OutlineCaptureChecks()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var settings = SettingsService.Shared ?? new SettingsService();
        bool original = settings.Current.CaptureSelf;
        try
        {
            foreach (bool self in new[] { false, true })
            {
                settings.Current.CaptureSelf = self;
                var outline = new RegionOutlineWindow();
                try
                {
                    var hwnd = new WindowInteropHelper(outline).Handle;
                    Chk($"outline-affinity-follows-capture-self-{self}",
                        GetWindowDisplayAffinity(hwnd, out uint affinity) && affinity == (self ? 0u : 0x11u));
                    outline.SetRegion(new RectI(-4000, -4000, 64, 48));
                    Chk($"outline-placement-does-not-show-window-{self}",
                        !outline.IsVisible && !NekoCapture.Native.Win32.IsWindowVisible(hwnd));
                    outline.Flash();
                    var border = (Border)typeof(RegionOutlineWindow).GetField("_border", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(outline)!;
                    var dpi = VisualTreeHelper.GetDpi(outline);
                    Chk($"outline-flash-stays-outside-region-{self}",
                        Math.Abs(border.BorderThickness.Left * dpi.DpiScaleX - 2) < 0.001
                        && Math.Abs(border.BorderThickness.Top * dpi.DpiScaleY - 2) < 0.001);
                    outline.SetBorderVisible(false); outline.Flash();
                    Chk($"outline-flash-respects-hidden-border-{self}", border.BorderThickness == new Thickness(0));
                }
                finally { outline.Close(); }
            }
            var stroke = typeof(RegionOutlineWindow).GetMethod("StrokeThickness", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 })
            {
                var border = new Border
                {
                    BorderBrush = Brushes.Blue,
                    BorderThickness = (Thickness)stroke.Invoke(null, new object[] { new DpiScale(scale, scale) })!,
                    Background = Brushes.Transparent, UseLayoutRounding = false, SnapsToDevicePixels = true,
                };
                var size = new Size(68 / scale, 52 / scale);
                border.Measure(size); border.Arrange(new Rect(size)); border.UpdateLayout();
                var frame = new RenderTargetBitmap(68, 52, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                frame.Render(border);
                byte[] pixels = new byte[68 * 52 * 4]; frame.CopyPixels(pixels, 68 * 4, 0);
                bool clean = true, visible = false;
                for (int y = 0; y < 52; y++)
                for (int x = 0; x < 68; x++)
                {
                    byte alpha = pixels[(y * 68 + x) * 4 + 3];
                    if (x >= 2 && x < 66 && y >= 2 && y < 50) clean &= alpha == 0;
                    else visible |= alpha > 0;
                }
                Chk($"outline-raster-keeps-capture-pixels-clean-at-{scale}", clean && visible);
            }
        }
        catch (Exception ex) { Chk("outline-capture-contract", false, ex.ToString()); }
        finally { settings.Current.CaptureSelf = original; }
    }
}
