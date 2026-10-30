using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    static void OcrUninstallChecks()
    {
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(OcrService).Assembly.GetType("FrameCapture.Services.OcrModelComponent")!;
        var small = type.GetProperty("Small", statics)!.GetValue(null)!;
        var medium = type.GetProperty("Medium", statics)!.GetValue(null)!;
        bool Field(object model, string name) => (bool)type.GetProperty(name, instance)!.GetValue(model)!;
        var hostProperty = typeof(AppHost).GetProperty("Settings")!;
        var sharedProperty = typeof(SettingsService).GetProperty("Shared")!;
        var previous = hostProperty.GetValue(AppHost.Current); var shared = SettingsService.Shared;
        SettingsWindow? window = null;
        try
        {
            string Fixture(string engine)
            {
                string revision = "ppocr-v6-" + engine + "-1.0.0";
                string directory = Path.Combine(SettingsService.Dir, "components", revision);
                Directory.CreateDirectory(directory);
                foreach (string file in new[] { "det.onnx", "rec.onnx", "cls.onnx", "dict.txt" }) File.WriteAllText(Path.Combine(directory, file), "fixture");
                File.WriteAllText(Path.Combine(directory, "ready"), revision);
                return directory;
            }
            string smallDirectory = Fixture(OcrService.SmallEngine), mediumDirectory = Fixture(OcrService.MediumEngine);
            var service = new SettingsService(); service.Current.OcrEngine = OcrService.SystemEngine;
            hostProperty.SetValue(AppHost.Current, service);
            window = new SettingsWindow { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            typeof(SettingsWindow).GetField("_restoreSettingsPage", instance)!.SetValue(window, "ocr");
            typeof(SettingsWindow).GetMethod("Build", instance)!.Invoke(window, null);
            window.Show(); PumpMs(Fluent.Motion.SpringMs + 80);
            var objects = new List<DependencyObject>(); CollectTyped((DependencyObject)window.Content, typeof(ComboBox), objects);
            var combo = objects.OfType<ComboBox>().Single(control => control.Items.OfType<ComboBoxItem>()
                .Any(item => item.Tag as string == OcrService.MediumEngine));
            objects.Clear(); CollectTyped((DependencyObject)window.Content, typeof(Button), objects);
            var uninstall = objects.OfType<Button>().Single(button => button.Tag as string == "ocr-model-uninstall");
            Chk("ocr-system-has-no-uninstall-button", uninstall.Visibility == Visibility.Collapsed);
            combo.SelectedIndex = 1; PumpMs(Fluent.Motion.SpringMs + 80); window.UpdateLayout();
            Chk("ocr-uninstall-button-precedes-model-combo", uninstall.IsVisible && uninstall.IsEnabled
                && uninstall.TranslatePoint(new Point(uninstall.ActualWidth, 0), window).X <= combo.TranslatePoint(new Point(), window).X);
            using (var lease = ((Task<IDisposable?>)type.GetMethod("AcquireRecognitionAsync", instance)!.Invoke(small, null)!).GetAwaiter().GetResult())
            {
                PumpMs(30);
                bool removed = ((Task<bool>)type.GetMethod("UninstallAsync", instance)!.Invoke(small, null)!).GetAwaiter().GetResult();
                Chk("ocr-recognition-lease-protects-model", lease != null && Field(small, "InUse") && !uninstall.IsEnabled
                    && !removed && Directory.Exists(smallDirectory));
            }
            PumpMs(30);
            using (var locked = new FileStream(Path.Combine(smallDirectory, "det.onnx"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                uninstall.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                while (Field(small, "Busy")) PumpMs(20);
                PumpMs(Fluent.Motion.SpringMs + 80);
                objects.Clear(); CollectTyped((DependencyObject)window.Content, typeof(TextBlock), objects);
                Chk("ocr-uninstall-error-is-localized-and-retryable", objects.OfType<TextBlock>().Any(label => label.IsVisible
                    && label.Text == Loc.T(K.Ocr.UninstallFailed)) && uninstall.IsEnabled && Field(small, "HasFiles"));
            }
            uninstall.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            while (Field(small, "Busy")) PumpMs(20);
            PumpMs(Fluent.Motion.SpringMs + 80);
            Chk("ocr-uninstall-removes-only-selected-model", !Directory.Exists(smallDirectory) && Directory.Exists(mediumDirectory)
                && !Field(small, "Available") && !uninstall.IsEnabled && service.Current.OcrEngine == OcrService.SmallEngine);
            objects.Clear(); CollectTyped((DependencyObject)window.Content, typeof(TextBlock), objects);
            Chk("ocr-uninstall-restores-download-prompt", objects.OfType<TextBlock>().Any(label => label.IsVisible
                && label.Text == Loc.F(K.Ocr.NeedsDownload, Loc.T(K.Ocr.SmallEngine), "26")));
            combo.SelectedIndex = 2; PumpMs(Fluent.Motion.SpringMs + 80);
            uninstall.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            while (Field(medium, "Busy")) PumpMs(20);
            Chk("ocr-medium-can-be-uninstalled", !Directory.Exists(mediumDirectory) && !Field(medium, "Available"));
            combo.SelectedIndex = 0; PumpMs(Fluent.Motion.SpringMs + 80);
            Chk("ocr-system-stays-available-after-model-removal", uninstall.Visibility == Visibility.Collapsed
                && service.Current.OcrEngine == OcrService.SystemEngine);
        }
        catch (Exception error) { Chk("ocr-uninstall-ui", false, error.ToString()); }
        finally { window?.Close(); hostProperty.SetValue(AppHost.Current, previous); sharedProperty.SetValue(null, shared); }
    }
}
