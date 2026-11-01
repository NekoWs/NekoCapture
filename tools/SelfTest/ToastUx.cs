using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NekoCapture.Core;
using NekoCapture.Overlays;

static partial class SelfTest
{
    private static void ToastUxChecks()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Fluent.ApplyGlobalStyles();
        var settings = new SettingsService();
        var defaults = new AppSettings();
        Chk("welcome-default-dismiss-duration", defaults.WelcomeToastSeconds == 5);
        defaults.WelcomeToastSeconds = 0; defaults.Normalize();
        Chk("welcome-duration-cannot-be-persistent", defaults.WelcomeToastSeconds == 1);
        defaults.WelcomeToastSeconds = double.NaN; defaults.Normalize();
        Chk("welcome-invalid-duration-falls-back", defaults.WelcomeToastSeconds == 5);
        settings.Current.WelcomeToastSeconds = 2.5;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var welcome = (WelcomeToast)Activator.CreateInstance(typeof(WelcomeToast), flags, null, new object[] { "F6", "Ctrl+Alt+R" }, null)!;
        try
        {
            var timer = (DispatcherTimer)typeof(WelcomeToast).GetField("_dismiss", flags)!.GetValue(welcome)!;
            Chk("welcome-duration-uses-configuration", timer.Interval == TimeSpan.FromSeconds(2.5));
            welcome.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            Chk("welcome-leaving-restarts-countdown", timer.IsEnabled);
            welcome.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            Chk("welcome-hover-pauses-countdown", !timer.IsEnabled);
        }
        finally { welcome.Close(); }
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme, settings.Current.Accent);
            var toast = new UpdateToast("0.9.35", "0.9.36", () => { }, _ => { });
            try
            {
                var card = (Border)toast.Content;
                var area = SystemParameters.WorkArea;
                toast.Measure(card.DesiredSize); toast.Arrange(new Rect(card.DesiredSize));
                typeof(Window).GetMethod("OnContentRendered", flags)!.Invoke(toast, new object[] { EventArgs.Empty });
                PumpMs(40);
                Chk("update-toast-measures-before-position-" + theme,
                    Math.Abs(toast.Top + card.DesiredSize.Height + 12 - area.Bottom) < 1
                    && Math.Abs(toast.Left + card.DesiredSize.Width + 12 - area.Right) < 1,
                    $"window={toast.ActualWidth},{toast.ActualHeight} card={card.DesiredSize} position={toast.Left},{toast.Top}");
                card.Arrange(new Rect(card.DesiredSize)); card.UpdateLayout();
                var image = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                image.Render(card);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = System.IO.File.Create(System.IO.Path.Combine(SettingsService.Dir, "update-" + theme + ".png"));
                encoder.Save(file);
            }
            finally { toast.Close(); }
        }
    }
}
