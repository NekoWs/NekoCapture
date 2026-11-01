using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;

static partial class SelfTest
{
    static void OcrSmallChecks()
    {
        try
        {
            Chk("ocr-default-stays-system", new AppSettings().OcrEngine == OcrService.SystemEngine);
            var config = new AppSettings { OcrEngine = "unavailable-engine" }; config.Normalize();
            Chk("ocr-unknown-setting-uses-system", config.OcrEngine == OcrService.SystemEngine);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var component = typeof(OcrService).Assembly.GetType("NekoCapture.Services.OcrModelComponent")!;
            var session = component.GetProperty("Small", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            ((Task)component.GetMethod("InstallAsync", flags)!.Invoke(session, null)!).GetAwaiter().GetResult();
            bool available = (bool)component.GetProperty("Available", flags)!.GetValue(session)!;
            Chk("ocr-small-component-installs-verified-models", available);
            if (!available) return;
            int systemErrors = 0, smallErrors = 0;
            OcrOutcome? preview = null;
            foreach (var (name, expected, size, dark, faint) in new[] {
                ("small-zh", "截图与录制 识别文字 清晰准确", 10.0, false, false),
                ("small-en", "NekoCapture screenshot 12345 clipboard", 9.0, false, false),
                ("dark-zh", "截图与录制 识别文字 清晰准确", 11.0, true, false),
                ("faint-zh", "截图与录制 识别文字 清晰准确", 12.0, false, true) })
            {
                var frame = OcrSample(expected, size, dark, faint);
                var system = OcrService.RecognizeAsync(frame, false, OcrService.SystemEngine).GetAwaiter().GetResult();
                var watch = Stopwatch.StartNew();
                var small = OcrService.RecognizeAsync(frame, true, OcrService.SmallEngine).GetAwaiter().GetResult();
                if (small.Status == OcrStatus.Success) preview ??= small;
                int before = OcrDistance(expected, system.Text), after = OcrDistance(expected, small.Text);
                systemErrors += before; smallErrors += after;
                Console.WriteLine($"SMALL {name}: system={before}, small={after}, ms={watch.ElapsedMilliseconds}; {small.Text}");
                Chk("ocr-small-" + name + "-returns-positioned-text", small.Status == OcrStatus.Success && small.Image is { IsFrozen: true }
                    && small.Lines.Count > 0 && small.Lines.SelectMany(line => line.Words).All(word => word.CharacterBounds?.Count
                        == StringInfo.ParseCombiningCharacters(word.Text).Length));
                if (small.Status == OcrStatus.Success)
                {
                    var viewType = typeof(OcrResultWindow).Assembly.GetType("NekoCapture.Overlays.OcrImageView")!;
                    var view = viewType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(OcrOutcome) }, null)!
                        .Invoke(new object[] { small });
                    viewType.GetMethod("SelectAll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
                    Chk("ocr-small-" + name + "-selection-keeps-recognized-text",
                        (string)viewType.GetProperty("SelectedText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)! == small.Text);
                }
            }
            Console.WriteLine($"SMALL SAMPLE ERRORS system={systemErrors}, small={smallErrors}");
            Chk("ocr-small-worker-exits-after-request", Process.GetProcessesByName("NekoCapture.OcrWorker").Length == 0);
            Chk("ocr-small-missing-component-has-specific-feedback", OcrService.Route(OcrStatus.ComponentMissing, true) == OcrRoute.NoEngineHint
                && OcrService.Feedback(OcrStatus.ComponentMissing, 0) == Loc.T(K.Ocr.Missing));
            if (preview is { } result) OcrSmallUiChecks(result);
        }
        catch (Exception error) { Chk("ocr-small-integration", false, error.ToString()); }
    }

    static void OcrSmallUiChecks(OcrOutcome result)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var hostProperty = typeof(AppHost).GetProperty("Settings")!;
        var sharedProperty = typeof(SettingsService).GetProperty("Shared")!;
        var previous = hostProperty.GetValue(AppHost.Current); var shared = SettingsService.Shared;
        SettingsWindow? window = null;
        Window? preview = null;
        try
        {
            var service = new SettingsService(); service.Current.OcrEngine = OcrService.SystemEngine;
            hostProperty.SetValue(AppHost.Current, service);
            window = new SettingsWindow { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            typeof(SettingsWindow).GetField("_restoreCat", flags)!.SetValue(window, 5);
            typeof(SettingsWindow).GetMethod("Build", flags)!.Invoke(window, null);
            window.Show(); PumpMs(Fluent.Motion.EnterMs + 60);
            var objects = new List<DependencyObject>(); CollectTyped((DependencyObject)window.Content, typeof(ComboBox), objects);
            var combo = objects.OfType<ComboBox>().Single(control => control.Items.OfType<ComboBoxItem>().Any(item => item.Tag as string == OcrService.SmallEngine));
            Chk("ocr-settings-default-selects-system", (combo.SelectedItem as ComboBoxItem)?.Tag as string == OcrService.SystemEngine);
            combo.SelectedIndex = 1;
            Chk("ocr-settings-small-selection-is-persisted", service.Current.OcrEngine == OcrService.SmallEngine && ((ComboBoxItem)combo.Items[1]).IsEnabled);
            combo.SelectedIndex = 0;
            Chk("ocr-settings-can-return-to-system", service.Current.OcrEngine == OcrService.SystemEngine);
            var ctor = typeof(OcrResultWindow).GetConstructor(flags, null, new[] { typeof(OcrOutcome?), typeof(string) }, null)!;
            preview = (Window)ctor.Invoke(new object?[] { result, null });
            preview.Left = -20000; preview.Top = -20000; preview.ShowActivated = false; preview.Show(); PumpMs(Fluent.Motion.EnterMs + 60);
            var view = typeof(OcrResultWindow).GetField("_view", flags)!.GetValue(preview)!;
            view.GetType().GetMethod("SelectAll", flags)!.Invoke(view, null);
            SaveOcrSmallVisual((FrameworkElement)preview.Content, "preview.png");
            SaveOcrSmallVisual((FrameworkElement)window.Content, "settings.png");
        }
        finally
        {
            preview?.Close(); window?.Close(); hostProperty.SetValue(AppHost.Current, previous); sharedProperty.SetValue(null, shared);
        }
    }

    static void SaveOcrSmallVisual(FrameworkElement content, string name)
    {
        content.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(Environment.CurrentDirectory, "test-out", "ocr-small", name)); encoder.Save(file);
    }
}
