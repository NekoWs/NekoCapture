using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;
using FrameCapture.Encoding;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>录制设置中的视频组件入口和跨分页共享的安装进度。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement VideoComponentRow(VideoComponentInstall? session = null)
    {
        session ??= VideoComponentInstall.Shared;
        var row = ChipRow("\uE714", Loc.T(K.Common.VideoComponentDescription), Loc.T(K.Common.VideoComponent));
        var button = Fluent.RoundButton(Loc.T(K.Common.DownloadAndInstall), Theme.Accent, Fluent.InkOn(Theme.Accent, Theme.Text), 12, true);
        button.MinHeight = 32;
        button.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(button, 2);
        row.Children.Add(button);
        var detail = new TextBlock
        {
            FontSize = 11.5, Foreground = Theme.TextDimBrush, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(32, 2, 0, 5),
        };
        var track = new Border
        {
            Height = 4, CornerRadius = new CornerRadius(10), Background = Theme.TrackBgBrush,
            Margin = new Thickness(32, 0, 0, 6), ClipToBounds = true,
        };
        var fill = new Border
        {
            CornerRadius = new CornerRadius(10), Background = Theme.AccentBrush,
            RenderTransform = new ScaleTransform(0, 1), RenderTransformOrigin = new Point(0, 0.5),
        };
        track.Child = fill;
        var outer = new StackPanel();
        outer.Children.Add(row);
        outer.Children.Add(detail);
        outer.Children.Add(track);
        void Refresh()
        {
            bool available = session.State == VideoComponentInstall.InstallState.Available;
            bool busy = session.Busy;
            var progress = session.Progress;
            button.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            button.Content = busy ? Loc.T(K.Common.Cancel) : Loc.T(K.Common.DownloadAndInstall);
            button.Foreground = new SolidColorBrush(Fluent.InkOn(busy ? Theme.TrackBg : Theme.Accent, Theme.Text));
            button.Background = new SolidColorBrush(busy ? Theme.TrackBg : Theme.Accent);
            detail.Foreground = available ? Theme.SuccessBrush : Theme.TextDimBrush;
            detail.Text = session.State switch
            {
                VideoComponentInstall.InstallState.Available => Loc.T(K.Settings.FfmpegDetectedMp4AndWebm),
                VideoComponentInstall.InstallState.Verifying => Loc.T(K.Common.ComponentVerifying),
                VideoComponentInstall.InstallState.Failed => Loc.T(K.Settings.ComponentInstallFailed),
                VideoComponentInstall.InstallState.Cancelled => Loc.T(K.Common.ComponentCancelled),
                VideoComponentInstall.InstallState.Downloading when progress.Bytes == 0 => Loc.T(K.Settings.ComponentConnecting),
                VideoComponentInstall.InstallState.Downloading => Loc.F(K.Settings.ComponentDownloading,
                    (int)progress.Percent, (progress.Bytes / 1048576d).ToString("0.0"),
                    (FfmpegComponentInfo.DownloadBytes / 1048576d).ToString("0.0"),
                    progress.Speed < 1048576 ? $"{progress.Speed / 1024:0} KiB/s" : $"{progress.Speed / 1048576:0.0} MiB/s"),
                _ => Loc.F(K.Settings.ComponentNotInstalled, Math.Round(FfmpegComponentInfo.DownloadBytes / 1048576d)),
            };
            track.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            double amount = progress.Verifying || progress.Bytes == 0 ? 1 : progress.Percent / 100;
            fill.Opacity = progress.Verifying || progress.Bytes == 0 ? 0.45 : 1;
            ((ScaleTransform)fill.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(amount, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
        }
        button.Click += async (_, _) =>
        {
            if (session.Busy) session.Cancel();
            else await session.StartAsync();
        };
        outer.Loaded += (_, _) => { session.Changed += Refresh; Theme.Changed += Refresh; session.Refresh(); };
        outer.Unloaded += (_, _) => { session.Changed -= Refresh; Theme.Changed -= Refresh; };
        Refresh();
        return outer;
    }
}
