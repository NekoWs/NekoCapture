using System.Collections.Concurrent;
using System.Diagnostics;
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
    static void OcrDownloadChecks()
    {
        ComponentDownloadChecks();
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(OcrService).Assembly.GetType("FrameCapture.Services.OcrSmallComponent")!;
        T Field<T>(string name) => (T)type.GetProperty(name, statics)!.GetValue(null)!;
        var hostProperty = typeof(AppHost).GetProperty("Settings")!;
        var sharedProperty = typeof(SettingsService).GetProperty("Shared")!;
        var previous = hostProperty.GetValue(AppHost.Current); var shared = SettingsService.Shared;
        SettingsWindow? window = null;
        var states = new ConcurrentQueue<(string Phase, long Bytes, double Speed)>();
        Action changed = () =>
        {
            var snapshot = Field<object>("CurrentProgress"); var snapshotType = snapshot.GetType();
            states.Enqueue((snapshotType.GetProperty("Phase")!.GetValue(snapshot)!.ToString()!,
                (long)snapshotType.GetProperty("Bytes")!.GetValue(snapshot)!, (double)snapshotType.GetProperty("Speed")!.GetValue(snapshot)!));
        };
        var eventInfo = type.GetEvent("Changed", statics)!;
        try
        {
            var service = new SettingsService();
            hostProperty.SetValue(AppHost.Current, service);
            window = new SettingsWindow { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            typeof(SettingsWindow).GetField("_restoreCat", instance)!.SetValue(window, 5);
            typeof(SettingsWindow).GetMethod("Build", instance)!.Invoke(window, null);
            window.Show(); PumpMs(Fluent.Motion.SpringMs + 80);
            Chk("ocr-models-are-never-auto-downloaded", !Field<bool>("Busy") && !Field<bool>("Available")
                && !Directory.Exists(Path.Combine(SettingsService.Dir, "components")) && service.Current.OcrEngine == OcrService.SystemEngine);
            var objects = new List<DependencyObject>(); CollectTyped((DependencyObject)window.Content, typeof(ComboBox), objects);
            var combo = objects.OfType<ComboBox>().Single(control => control.Items.OfType<ComboBoxItem>()
                .Any(item => item.Tag as string == OcrService.SmallEngine));
            Chk("ocr-missing-model-keeps-small-disabled", !((ComboBoxItem)combo.Items[1]).IsEnabled);
            objects.Clear(); CollectTyped((DependencyObject)window.Content, typeof(Button), objects);
            var download = objects.OfType<Button>().Single(button => button.IsVisible
                && button.Content as string == Loc.T(K.Common.DownloadAndInstall));
            eventInfo.GetAddMethod(true)!.Invoke(null, new object[] { changed });
            download.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Chk("ocr-manual-download-starts-cancellable-session", Field<bool>("Busy") && download.Content as string == Loc.T(K.Common.Cancel));
            objects.Clear(); CollectTyped((DependencyObject)window.Content, typeof(TextBlock), objects);
            var labels = objects.OfType<TextBlock>().ToArray();
            bool statsShown = false;
            var timer = Stopwatch.StartNew();
            while (Field<bool>("Busy") && timer.ElapsedMilliseconds < 50000)
            {
                PumpMs(20);
                statsShown |= labels.Any(label => label.IsVisible && label.Text.Contains("MiB") && label.Text.Contains("/s"));
            }
            PumpMs(100);
            Chk("ocr-model-download-finishes-and-enables-small", Field<bool>("Available") && ((ComboBoxItem)combo.Items[1]).IsEnabled,
                string.Join(",", states.Select(state => state.Phase).Distinct()));
            Chk("ocr-model-download-reports-bytes-and-speed", states.Any(state => state.Bytes > 0 && state.Speed > 0) && statsShown);
            Chk("ocr-model-download-exposes-verifying-and-installing", states.Any(state => state.Phase == "Verifying")
                && states.Any(state => state.Phase == "Installing"));
            Chk("ocr-downloading-never-changes-engine-preference", service.Current.OcrEngine == OcrService.SystemEngine);
            Console.WriteLine($"OCR DOWNLOAD elapsed={timer.ElapsedMilliseconds} ms, bytes={states.Max(state => state.Bytes)}");
            SaveOcrSmallVisual((FrameworkElement)window.Content, "download-installed.png");
        }
        catch (Exception error) { Chk("ocr-download-ui", false, error.ToString()); }
        finally
        {
            if (Field<bool>("Busy")) type.GetMethod("Cancel", statics)!.Invoke(null, null);
            eventInfo.GetRemoveMethod(true)!.Invoke(null, new object[] { changed });
            window?.Close(); hostProperty.SetValue(AppHost.Current, previous); sharedProperty.SetValue(null, shared);
        }
    }
}
