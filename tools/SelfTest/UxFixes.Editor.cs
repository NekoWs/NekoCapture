using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Drawing;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    private static void UxEditorFixesChecks(SettingsService settings)
    {
        var pixels = new byte[64 * 64 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        { pixels[i] = (byte)(i / 4 % 64 * 3); pixels[i + 1] = 80; pixels[i + 2] = 30; pixels[i + 3] = 255; }
        var frame = new CapturedFrame { W = 64, H = 64, Bgra = pixels };
        var line = new Annotation { Kind = AnnKind.Line, Thickness = 90, Color = System.Drawing.Color.Red };
        line.Points.AddRange(new[] { (0.1f, 0.2f), (0.9f, 0.8f) });
        var mosaic = new Annotation { Kind = AnnKind.Mosaic, Thickness = 128 };
        mosaic.Points.AddRange(new[] { (0f, 0f), (1f, 1f) });
        var expected = Bake.Apply(new[] { frame }, new[] { line })[0].Bgra;
        BgraImage.MosaicInPlace(expected, 64, 64, 0, 0, 64, 64, MosaicBlock.Px(128, 64, 64));
        var actual = Bake.Apply(new[] { frame }, new[] { line, mosaic })[0];
        Chk("ux-mosaic-composes-earlier-strokes", actual.Bgra.SequenceEqual(expected));
        var reversed = Bake.Apply(new[] { frame }, new[] { mosaic, line })[0];
        Chk("ux-edit-order-changes-layering", !actual.Bgra.SequenceEqual(reversed.Bgra));
        var outside = new Annotation { Kind = AnnKind.Rect, Thickness = 45, Color = System.Drawing.Color.Red };
        outside.Points.AddRange(new[] { (-0.5f, -0.5f), (0.5f, 0.5f) });
        var clipped = Bake.Apply(new[] { frame }, new[] { outside })[0];
        Chk("ux-outside-rectangle-is-clipped-without-moving-edge", clipped.Bgra.Take(4).SequenceEqual(frame.Bgra.Take(4))
            && !clipped.Bgra.SequenceEqual(frame.Bgra));
        var bakedType = typeof(CapturedFrame).Assembly.GetType("FrameCapture.Capture.BakedFrameList")!;
        var lease = bakedType.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            new object[] { new[] { frame }, 0, 1, new[] { outside }, new RectI(16, 16, 32, 32) })!;
        try
        {
            var cropped = ((IReadOnlyList<CapturedFrame>)lease)[0];
            var reference = Bake.CropFrame(clipped, 16, 16, 32, 32);
            Chk("ux-cropped-export-retains-crossing-annotations", cropped.Bgra.SequenceEqual(reference.Bgra));
        }
        finally { ((IDisposable)lease).Dispose(); }
        var edgePixels = (byte[])pixels.Clone();
        BgraImage.MosaicInPlace(edgePixels, 64, 64, -32, -32, 40, 40, 4);
        Chk("ux-mosaic-outside-area-is-intersected", edgePixels.Skip((16 * 64 + 16) * 4).Take(4).SequenceEqual(pixels.Skip((16 * 64 + 16) * 4).Take(4)));
        var sequence = new CapturedSequence { Frames = new[] { frame }, Region = new RectI(0, 0, 64, 64),
            MonitorBounds = new RectI(0, 0, 1920, 1080), MonitorDevice = "", Fps = 60 };
        var editor = new EditorWindow(settings, sequence);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(EditorWindow);
        object? Field(string name) => type.GetField(name, flags)!.GetValue(editor);
        object? Call(string name, params object?[] args) => type.GetMethod(name, flags)!.Invoke(editor, args);
        try
        {
            ShowOffscreen(editor); PumpMs(100); editor.UpdateLayout();
            Chk("ux-editor-has-no-welcome-overlay", type.GetField("_guide", flags) == null && type.GetMethod("ShowGuide", flags) == null);
            var view = (Grid)Field("_viewGrid")!;
            var host = (Border)VisualTreeHelper.GetParent(view);
            Theme.Apply("light", settings.Current.Accent); PumpMs(Fluent.Motion.ThemeMs + 30);
            Chk("ux-editor-workspace-follows-theme", ReferenceEquals(host.Background, Theme.WindowBgSolidBrush)
                && ((SolidColorBrush)host.Background).Color == Theme.WindowBgSolid);
            var rect = (Rect)Field("_imgRect")!;
            var normalized = (Point)Call("Norm", new Point(rect.Left - 20, rect.Top - 20))!;
            Chk("ux-editor-retains-outside-coordinates", normalized.X < 0 && normalized.Y < 0);
            Chk("ux-editor-workspace-can-start-stroke-outside-image", view.InputHitTest(new Point(Math.Max(1, rect.Left / 2), rect.Top + rect.Height / 2)) != null);
            var canvas = (Canvas)Field("_canvas")!;
            Chk("ux-editor-clips-annotation-visuals-to-image", canvas.Clip != null && Math.Abs(canvas.Clip.Bounds.Width - rect.Width) < 0.1 && view.Clip == null);
            Call("CommitAnn", line); Call("CommitAnn", mosaic);
            var preview = (BitmapSource)Call("GetMosaicRect", 0, 128f, 0, 0, 64, 64, false, mosaic)!;
            var previewPixels = new byte[64 * 64 * 4]; preview.CopyPixels(previewPixels, 64 * 4, 0);
            Chk("ux-mosaic-preview-matches-export-layering", previewPixels.SequenceEqual(actual.Bgra));
            var themeType = typeof(Theme);
            themeType.GetField("_themeSetting", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, "system");
            themeType.GetMethod("RefreshSystemPreferences", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            bool systemDark = key?.GetValue("AppsUseLightTheme") is int mode && mode == 0;
            Chk("ux-theme-refresh-reads-current-system-preference", Theme.IsDark == systemDark);
        }
        finally { editor.Close(); }
        var monitor = Monitors.All().First();
        var picker = new RegionPickerWindow(monitor, new RectI(100, 100, 200, 100), snapLastRegion: true);
        try
        {
            var previous = typeof(RegionPickerWindow).GetMethod("PreviousRegionAt", flags)!;
            Chk("ux-last-region-can-be-selected", previous.Invoke(picker, new object[] { (150, 150) }) is RectI { W: 200, H: 100 });
            var snap = typeof(RegionPickerWindow).GetMethod("SnapPreviousPoint", flags)!;
            var position = ((int X, int Y))snap.Invoke(picker, new object[] { (102, 102) })!;
            Chk("ux-last-region-edges-snap", position == (100, 100));
        }
        finally { picker.Close(); }
    }
}
