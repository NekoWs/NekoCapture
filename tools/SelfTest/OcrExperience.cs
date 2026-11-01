using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NekoCapture.Capture;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

static partial class SelfTest
{
    static CapturedFrame OcrSample(string text, double size, bool dark, bool lowContrast = false)
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(dark ? new SolidColorBrush(Color.FromRgb(32, 34, 38)) : Brushes.White, null, new Rect(0, 0, 720, 110));
            context.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), size, dark ? Brushes.White : lowContrast ? Brushes.LightGray : Brushes.Black, 1), new Point(18, 30));
        }
        var image = new RenderTargetBitmap(720, 110, 96, 96, PixelFormats.Pbgra32); image.Render(drawing);
        return BgraImage.FromSource(image)!;
    }

    static OcrOutcome OcrPreviewFixture()
    {
        var frame = OcrSample("在图片上选择文字  NekoCapture 12345", 24, false);
        var line = new OcrLineInfo("在图片上选择文字 NekoCapture 12345", new OcrWordInfo[] {
            new("在图片上选择文字", new Rect(18, 34, 192, 28), 0),
            new("NekoCapture", new Rect(225, 34, 170, 28), 9),
            new("12345", new Rect(403, 34, 72, 28), 22) }, 0, new Point());
        return new(OcrStatus.Success, line.Text) { Lines = new[] { line }, Image = BgraImage.ToSource(frame) };
    }

    static void OcrExperienceChecks()
    {
        try
        {
            var samples = new[] {
                ("small-zh", "截图与录制 识别文字 清晰准确", 10.0, false, false),
                ("small-en", "NekoCapture screenshot 12345 clipboard", 9.0, false, false),
                ("dark-zh", "截图与录制 识别文字 清晰准确", 11.0, true, false),
                ("faint-zh", "截图与录制 识别文字 清晰准确", 12.0, false, true) };
            int beforeErrors = 0, afterErrors = 0;
            foreach (var (name, expected, size, dark, faint) in samples)
            {
                var frame = OcrSample(expected, size, dark, faint);
                string baseline = Task.Run(async () =>
                {
                    var engine = OcrEngine.TryCreateFromUserProfileLanguages();
                    if (engine == null) return "";
                    using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(frame.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, frame.W, frame.H);
                    return (await engine.RecognizeAsync(bitmap)).Text;
                }).GetAwaiter().GetResult();
                var result = OcrService.RecognizeAsync(frame).GetAwaiter().GetResult();
                int before = OcrDistance(expected, baseline), after = OcrDistance(expected, result.Text);
                beforeErrors += before; afterErrors += after;
                Console.WriteLine($"OCR {name}: before={before}, after={after}; text={result.Text}");
                Chk("ocr-" + name + "-has-positioned-text", result.Status == OcrStatus.Success && result.Lines.Count > 0
                    && result.Image is { IsFrozen: true } && result.Lines.SelectMany(line => line.Words).All(word =>
                        word.Bounds.X >= 0 && word.Bounds.Y >= 0 && word.Bounds.Right <= frame.W && word.Bounds.Bottom <= frame.H));
            }
            Chk("ocr-enhancement-reduces-sample-errors", afterErrors < beforeErrors, $"before={beforeErrors}, after={afterErrors}");
            OcrLongImageChecks();
            var transparent = OcrSample("Hidden text", 24, false);
            for (int i = 3; i < transparent.Bgra.Length; i += 4) transparent.Bgra[i] = 0;
            Chk("ocr-transparent-pixels-have-no-hidden-text", OcrService.RecognizeAsync(transparent).GetAwaiter().GetResult().Status == OcrStatus.Empty);
            OcrSelectionChecks();
        }
        catch (Exception error) { Chk("ocr-experience", false, error.ToString()); }
    }

    static int OcrDistance(string expected, string actual)
    {
        string Clean(string value) => new(value.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray());
        expected = Clean(expected); actual = Clean(actual);
        var row = Enumerable.Range(0, actual.Length + 1).ToArray();
        for (int i = 1; i <= expected.Length; i++)
        {
            int prior = row[0]; row[0] = i;
            for (int j = 1; j <= actual.Length; j++)
            {
                int old = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), prior + (expected[i - 1] == actual[j - 1] ? 0 : 1));
                prior = old;
            }
        }
        return row[^1];
    }

    static void OcrLongImageChecks()
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 3400, 160));
            foreach (var item in new[] { (40, "SCREENSHOT"), (1230, "CLIPBOARD"), (2500, "NekoCapture") })
                context.DrawText(new FormattedText(item.Item2, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 24, Brushes.Black, 1), new Point(item.Item1, 40));
        }
        var image = new RenderTargetBitmap(3400, 160, 96, 96, PixelFormats.Pbgra32); image.Render(drawing);
        var result = OcrService.RecognizeAsync(BgraImage.FromSource(image)!).GetAwaiter().GetResult();
        foreach (string word in new[] { "SCREENSHOT", "CLIPBOARD", "NekoCapture" })
            Chk("ocr-wide-image-" + word, result.Text.Split(word).Length == 2, result.Text);
        Chk("ocr-wide-image-maps-last-word", result.Lines.SelectMany(line => line.Words)
            .Any(word => word.Text.Contains("NekoCapture") && word.Bounds.X > 2450 && word.Bounds.X < 2550));
    }

    static void OcrSelectionChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var document = OcrPreviewFixture();
        var ctor = typeof(OcrResultWindow).GetConstructor(flags, null, new[] { typeof(OcrOutcome?), typeof(string) }, null)!;
        var window = (Window)ctor.Invoke(new object?[] { document, null });
        try
        {
            window.Left = -20000; window.Top = -20000; window.ShowActivated = false; window.Show(); PumpMs(Fluent.Motion.EnterMs + 80);
            var view = typeof(OcrResultWindow).GetField("_view", flags)!.GetValue(window)!;
            var type = view.GetType();
            type.GetMethod("SelectRange", flags)!.Invoke(view, new object[] { 2, 8 });
            Chk("ocr-selects-partial-text-on-image", (string)type.GetProperty("SelectedText", flags)!.GetValue(view)! == document.Text[2..8]);
            type.GetProperty("Zoom", flags)!.SetValue(view, 2.0);
            Chk("ocr-zoom-keeps-selection-and-image-coordinates", ((FrameworkElement)view).Width == 1440
                && (string)type.GetProperty("SelectedText", flags)!.GetValue(view)! == document.Text[2..8]);
            type.GetMethod("SelectAll", flags)!.Invoke(view, null);
            Chk("ocr-select-all-preserves-reading-order", (string)type.GetProperty("SelectedText", flags)!.GetValue(view)! == document.Text);
            var hit = type.GetMethod("Hit", flags)!.Invoke(view, new object[] { new Point(19, 48), true });
            Chk("ocr-hit-test-maps-original-image-position", hit != null);
            var second = new OcrLineInfo("第二行", new[] { new OcrWordInfo("第二行", new Rect(18, 76, 60, 24), 0) }, 0, new Point());
            var multiline = document with { Text = document.Text + "\n" + second.Text, Lines = new[] { document.Lines[0], second } };
            var other = type.GetConstructor(flags, null, new[] { typeof(OcrOutcome) }, null)!.Invoke(new object[] { multiline });
            type.GetMethod("SelectRange", flags)!.Invoke(other, new object[] { 22, multiline.Text.Length });
            Chk("ocr-cross-line-selection-retains-newline", (string)type.GetProperty("SelectedText", flags)!.GetValue(other)! == "12345\n第二行");
            var rotatedLine = document.Lines[0] with { Angle = 12, Center = new Point(360, 55) };
            var rotated = type.GetConstructor(flags, null, new[] { typeof(OcrOutcome) }, null)!
                .Invoke(new object[] { document with { Lines = new[] { rotatedLine } } });
            var point = new RotateTransform(12, 360, 55).Transform(new Point(19, 48));
            Chk("ocr-rotated-text-hit-test-matches-overlay", type.GetMethod("Hit", flags)!.Invoke(rotated, new object[] { point, true }) != null);
            type.GetProperty("Zoom", flags)!.SetValue(view, 1.0);
            type.GetMethod("SelectRange", flags)!.Invoke(view, new object[] { 0, 8 });
            var root = (FrameworkElement)window.Content; root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(Environment.CurrentDirectory, "test-out", "ocr-experience-preview.png")); encoder.Save(file);
        }
        finally { window.Close(); }
    }
}
