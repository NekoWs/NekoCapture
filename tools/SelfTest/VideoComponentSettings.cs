using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrameCapture.Core;
using FrameCapture.Encoding;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    static void VideoComponentSettingsChecks(SettingsWindow settings)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        bool available = false;
        int starts = 0;
        IProgress<ComponentProgress>? report = null;
        CancellationToken token = default;
        var completion = new TaskCompletionSource();
        var session = new VideoComponentInstall(() => available ? "component.exe" : null, (progress, ct) =>
        { starts++; report = progress; token = ct; return completion.Task; });
        var method = typeof(SettingsWindow).GetMethod("VideoComponentRow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FrameworkElement Row() => (FrameworkElement)method.Invoke(settings, new object[] { session })!;
        var row = (StackPanel)Row();
        FrameworkElement Wrap(StackPanel value) => new Border
        { Background = Theme.CardBgBrush, Padding = new Thickness(12), Child = value };
        var fixture = new Window { Content = Wrap(row), Width = 500, Height = 160, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false };
        Button ButtonIn(StackPanel panel) => ((Grid)panel.Children[0]).Children.OfType<Button>().Single();
        TextBlock Status(StackPanel panel) => Descendants(panel).OfType<TextBlock>().Single(e => e.Tag as string == "video-component-status");
        FrameworkElement Track(StackPanel panel) => Descendants(panel).OfType<FrameworkElement>().Single(e => e.Tag as string == "video-component-progress");
        void Snapshot(string name)
        {
            var content = (FrameworkElement)fixture.Content;
            content.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)content.ActualWidth,
                (int)content.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            string dir = Path.Combine(Path.GetTempPath(), "component-ui");
            Directory.CreateDirectory(dir);
            using var output = File.Create(Path.Combine(dir, name + ".png"));
            encoder.Save(output);
        }
        try
        {
            fixture.Show(); PumpMs(30);
            Chk("video-component-settings-offers-install-when-missing", ButtonIn(row).IsVisible
                && Status(row).Text.Contains((session.Package.DownloadBytes / 1048576d).ToString("0.0")));
            Snapshot("missing");
            RecordFormat selected = RecordFormat.Gif;
            var webm = new VideoComponentInstall(() => null, (_, _) => Task.CompletedTask, FfmpegComponentInfo.WebM);
            var promptMethod = typeof(SettingsWindow).GetMethod("VideoComponentPrompt", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object?[] promptArgs = { (Func<RecordFormat>)(() => selected), null, session, webm };
            var prompt = (StackPanel)promptMethod.Invoke(settings, promptArgs)!;
            var refresh = (Action)promptArgs[1]!;
            Chk("video-component-gif-needs-no-download", prompt.Children.Count == 0);
            selected = RecordFormat.Mp4; refresh();
            Chk("video-component-mp4-offers-only-selected-package", prompt.Children.OfType<FrameworkElement>().Single().Tag as string == "video-component-mp4");
            selected = RecordFormat.WebM; refresh();
            Chk("video-component-webm-offers-only-selected-package", prompt.Children.OfType<FrameworkElement>().Single().Tag as string == "video-component-webm");
            selected = RecordFormat.Gif; refresh();
            Chk("video-component-gif-hides-prompt", prompt.Children.Count == 0);
            bool dark = Theme.IsDark;
            bool english = Loc.IsEnglish;
            try
            {
                Theme.Apply("light", Theme.Accent.ToString()); Loc.Apply(Loc.PrefEn);
                fixture.Content = null; row = (StackPanel)Row(); fixture.Content = Wrap(row); PumpMs(30);
                var action = ButtonIn(row);
                Chk("video-component-settings-light-english-is-readable", Fluent.ContrastRatio(
                    ((System.Windows.Media.SolidColorBrush)action.Foreground).Color,
                    ((System.Windows.Media.SolidColorBrush)action.Background).Color) >= 4.5
                    && action.TranslatePoint(new Point(), row).X + action.ActualWidth <= row.ActualWidth + 1);
                Snapshot("missing-light-en");
            }
            finally
            {
                Loc.Apply(english ? Loc.PrefEn : Loc.PrefZh); Theme.Apply(dark ? "dark" : "light", Theme.Accent.ToString());
                fixture.Content = null; row = (StackPanel)Row(); fixture.Content = Wrap(row); PumpMs(30);
            }
            ButtonIn(row).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            _ = session.StartAsync();
            Chk("video-component-settings-prevents-duplicate-download", starts == 1 && session.Busy
                && Equals(ButtonIn(row).Content, Loc.T(K.Common.Cancel)));
            report!.Report(new ComponentProgress((FfmpegComponentInfo.DownloadBytes + 1) / 2, 2097152)); PumpMs(50);
            Chk("video-component-settings-shows-size-speed-and-progress", Status(row).Text.Contains("50%")
                && Status(row).Text.Contains("2.0 MiB/s") && Track(row).Visibility == Visibility.Visible);
            Snapshot("progress");
            fixture.Content = null; PumpMs(20);
            row = (StackPanel)Row(); fixture.Content = Wrap(row); PumpMs(30);
            Chk("video-component-settings-reopen-preserves-download", session.Busy && starts == 1
                && Status(row).Text.Contains("50%"));
            report.Report(new ComponentProgress(FfmpegComponentInfo.DownloadBytes, 0, true)); PumpMs(30);
            Chk("video-component-settings-shows-verifying-before-ready",
                session.State == VideoComponentInstall.InstallState.Verifying
                && Status(row).Text == Loc.T(K.Common.ComponentVerifying));
            available = true; completion.SetResult(); PumpMs(50);
            Chk("video-component-settings-refreshes-after-install", session.State == VideoComponentInstall.InstallState.Available
                && !session.Busy && ButtonIn(row).Visibility == Visibility.Collapsed && Track(row).Visibility == Visibility.Collapsed);
            Snapshot("available");
            available = false; session.Refresh();
            completion = new TaskCompletionSource();
            ButtonIn(row).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ButtonIn(row).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Chk("video-component-settings-cancel-signals-transfer", token.IsCancellationRequested && starts == 2);
            completion.SetCanceled(); PumpMs(50);
            Chk("video-component-settings-can-retry-after-cancel", !session.Busy
                && session.State == VideoComponentInstall.InstallState.Cancelled && ButtonIn(row).Visibility == Visibility.Visible);
            completion = new TaskCompletionSource();
            ButtonIn(row).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            completion.SetException(new IOException("Simulated failure")); PumpMs(50);
            Chk("video-component-settings-error-keeps-retry-entry", !session.Busy && starts == 3
                && Status(row).Text == Loc.T(K.Settings.ComponentInstallFailed));
        }
        finally { fixture.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}
