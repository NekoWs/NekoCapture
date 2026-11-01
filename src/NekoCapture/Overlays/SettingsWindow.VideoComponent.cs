using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NekoCapture.Core;
using NekoCapture.Encoding;
using NekoCapture.Services;

namespace NekoCapture.Overlays;

/// <summary>所选视频格式的按需下载提示与跨分页共享的安装进度。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement VideoComponentRow(VideoComponentInstall? session = null)
    {
        session ??= VideoComponentInstall.Shared;
        var package = session.Package;
        var row = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var detail = new TextBlock { FontSize = 12, Foreground = Theme.TextDimBrush, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Tag = "video-component-status" };
        var button = Fluent.RoundButton(Loc.T(K.Common.DownloadAndInstall), Theme.Accent, Fluent.OnAccentColor(Theme.Accent), 12, true);
        Grid.SetColumn(button, 1); row.Children.Add(detail); row.Children.Add(button);
        var fill = new Border { CornerRadius = new CornerRadius(10), Background = Theme.AccentBrush,
            RenderTransform = new ScaleTransform(0, 1), RenderTransformOrigin = new Point(0, 0.5) };
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(10), Background = Theme.TrackBgBrush,
            Margin = new Thickness(0, 0, 0, 8), ClipToBounds = true, Child = fill, Tag = "video-component-progress" };
        var outer = new StackPanel { Tag = "video-component-" + package.Id };
        outer.Children.Add(row); outer.Children.Add(track);
        double shownProgress = -1;
        void Refresh()
        {
            bool available = session.State == VideoComponentInstall.InstallState.Available;
            bool busy = session.Busy;
            var progress = session.Progress;
            button.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            button.Content = Loc.T(busy ? K.Common.Cancel : K.Common.DownloadAndInstall);
            button.Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent));
            button.Background = Theme.AccentBrush;
            detail.Foreground = available ? Theme.SuccessBrush : Theme.TextDimBrush;
            detail.Text = session.State switch
            {
                VideoComponentInstall.InstallState.Available => Loc.F(K.VideoComponent.Ready, package.Label),
                VideoComponentInstall.InstallState.Verifying => Loc.T(K.Common.ComponentVerifying),
                VideoComponentInstall.InstallState.Failed => Loc.T(K.Settings.ComponentInstallFailed),
                VideoComponentInstall.InstallState.Cancelled => Loc.T(K.Common.ComponentCancelled),
                VideoComponentInstall.InstallState.Downloading when progress.Bytes == 0 => Loc.T(K.Settings.ComponentConnecting),
                VideoComponentInstall.InstallState.Downloading => Loc.F(K.Settings.ComponentDownloading,
                    (int)progress.Percent, (progress.Bytes / 1048576d).ToString("0.0"),
                    (package.DownloadBytes / 1048576d).ToString("0.0"),
                    progress.Speed < 1048576 ? $"{progress.Speed / 1024:0} KiB/s" : $"{progress.Speed / 1048576:0.0} MiB/s"),
                _ => Loc.F(K.VideoComponent.NeedsDownload, package.Label, (package.DownloadBytes / 1048576d).ToString("0.0")),
            };
            track.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            double amount = progress.Verifying || progress.Bytes == 0 ? 1 : progress.Percent / 100;
            fill.Background = Theme.AccentBrush; track.Background = Theme.TrackBgBrush;
            fill.Opacity = progress.Verifying || progress.Bytes == 0 ? 0.45 : 1;
            if (amount != shownProgress)
            {
                shownProgress = amount;
                ((ScaleTransform)fill.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(amount, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
            }
        }
        button.Click += async (_, _) => { if (session.Busy) session.Cancel(); else await session.StartAsync(); };
        outer.Loaded += (_, _) => { session.Changed += Refresh; Theme.Changed += Refresh; session.Refresh(); };
        outer.Unloaded += (_, _) => { session.Changed -= Refresh; Theme.Changed -= Refresh; };
        Refresh(); return outer;
    }

    private StackPanel VideoComponentPrompt(Func<RecordFormat> selected, out Action refresh,
        VideoComponentInstall? mp4 = null, VideoComponentInstall? webm = null)
    {
        mp4 ??= VideoComponentInstall.For(VideoFormat.Mp4); webm ??= VideoComponentInstall.For(VideoFormat.WebM);
        var host = new StackPanel { Margin = new Thickness(32, 0, 0, 0), Tag = "video-component-prompt" };
        VideoComponentInstall? shown = null;
        void Refresh(bool animate)
        {
            var format = selected();
            var next = format == RecordFormat.Gif ? null : format == RecordFormat.Mp4 ? mp4 : webm;
            if (next?.State == VideoComponentInstall.InstallState.Available) next = null;
            if (ReferenceEquals(shown, next)) return;
            shown = next;
            void Render() { host.Children.Clear(); if (shown != null) host.Children.Add(VideoComponentRow(shown)); }
            if (animate && host.IsVisible) TransitionContent(host, Render); else Render();
        }
        void Changed()
        {
            if (Dispatcher.HasShutdownStarted) return;
            if (Dispatcher.CheckAccess()) Refresh(true); else Dispatcher.BeginInvoke(new Action(() => Refresh(true)));
        }
        host.Loaded += (_, _) => { mp4.Changed += Changed; webm.Changed += Changed; mp4.Refresh(); webm.Refresh(); Refresh(false); };
        host.Unloaded += (_, _) => { mp4.Changed -= Changed; webm.Changed -= Changed; };
        refresh = () => Refresh(true);
        Refresh(false); return host;
    }
}
