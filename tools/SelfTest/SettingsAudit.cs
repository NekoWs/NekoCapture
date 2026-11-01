using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using NekoCapture.Capture;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;

static partial class SelfTest
{
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static extern IntPtr SettingsAuditCursor(IntPtr instance, IntPtr name);

    private static void SettingsAuditChecks()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var settings = new SettingsService();
        var hostProperty = typeof(AppHost).GetProperty("Settings")!;
        object? previous = hostProperty.GetValue(AppHost.Current);
        try
        {
            var input = JsonSerializer.Deserialize<AppSettings>(
                """{"ConfigVersion":1,"EditorToolbarAnim":false,"Fps":45,"DelayCaptureSeconds":120,"RecordIncludeCursor":false,"RememberEditorTools":false}""")!;
            input.Normalize();
            string output = JsonSerializer.Serialize(input);
            Chk("settings-retired-toolbar-option-ignored", typeof(AppSettings).GetProperty("EditorToolbarAnim") == null
                && !output.Contains("EditorToolbarAnim", StringComparison.Ordinal));
            Chk("settings-retired-option-preserves-valid-values", input.Fps == 45 && !input.RecordIncludeCursor
                && !input.RememberEditorTools && input.DelayCaptureSeconds == 120);
            input.DelayCaptureSeconds = int.MaxValue; input.Normalize();
            Chk("settings-delay-clamps-to-supported-limit", input.DelayCaptureSeconds == AppSettings.DelayCaptureSecondsMax);
            input.DelayCaptureSeconds = -1; input.Normalize();
            Chk("settings-delay-preserves-immediate-capture", input.DelayCaptureSeconds == 0);
            settings.Current.DelayCaptureSeconds = 120;
            using (var controller = new CaptureController(settings))
            {
                controller.BeginDelayedCapture();
                Chk("settings-delay-runtime-honors-two-minutes",
                    (int)typeof(CaptureController).GetField("_delayLeft", flags)!.GetValue(controller)! == 120);
            }
            hostProperty.SetValue(AppHost.Current, settings);
            var window = new SettingsWindow();
            try
            {
                typeof(SettingsWindow).GetMethod("Build", flags)!.Invoke(window, null);
                bool retired = false;
                void Walk(DependencyObject node)
                {
                    if (node is System.Windows.Controls.TextBlock text && text.Text == "底部工具栏入场动效") retired = true;
                    for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                        Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
                }
                Walk((DependencyObject)window.Content);
                Chk("settings-editor-has-no-retired-toggle", !retired);
            }
            finally { window.Close(); }
            var paint = typeof(LiveCapture).GetMethod("PaintCursor", BindingFlags.NonPublic | BindingFlags.Static)!;
            IntPtr cursor = SettingsAuditCursor(IntPtr.Zero, new IntPtr(32512));
            Chk("settings-gdi-cursor-resource-available", cursor != IntPtr.Zero);
            using var bitmap = new System.Drawing.Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.Magenta);
            IntPtr dc = graphics.GetHdc();
            try
            {
                Chk("settings-gdi-cursor-uses-region-offset", (bool)paint.Invoke(null,
                    new object[] { dc, new RectI(1000, -500, 64, 64), cursor, 1016, -484 })!);
                Chk("settings-gdi-cursor-reuses-hotspot", (bool)paint.Invoke(null,
                    new object[] { dc, new RectI(1000, -500, 64, 64), cursor, 1016, -484 })!);
                NekoCapture.Native.Win32.GdiFlush();
            }
            finally { graphics.ReleaseHdc(dc); }
            int changed = 0;
            bool opaque = true;
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                var color = bitmap.GetPixel(x, y);
                opaque &= color.A == 255;
                if (color.R != 255 || color.G != 0 || color.B != 255) changed++;
            }
            Chk("settings-gdi-cursor-renders-into-frame", changed > 0);
            Chk("settings-gdi-cursor-does-not-replace-entire-frame", changed < 64 * 64 / 2);
            Chk("settings-gdi-cursor-keeps-opaque-frame", opaque);
        }
        catch (Exception ex) { Chk("settings-audit-contract", false, ex.ToString()); }
        finally { hostProperty.SetValue(AppHost.Current, previous); }
    }
}
