using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>系统内置 OCR 与可选 small 模型的选择和下载。</summary>
public sealed partial class SettingsWindow
{
    void BuildOcrSettings(StackPanel panel)
    {
        var row = ChipRow("\uE8C1", Loc.T(K.Ocr.EngineDescription), Loc.T(K.Ocr.Engine));
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Right };
        Fluent.StyleCombo(combo);
        combo.Items.Add(new ComboBoxItem { Content = Loc.T(K.Ocr.SystemEngine), Tag = OcrService.SystemEngine });
        var small = new ComboBoxItem { Content = Loc.T(K.Ocr.SmallEngine), Tag = OcrService.SmallEngine };
        combo.Items.Add(small);
        combo.SelectedIndex = _settings.Current.OcrEngine == OcrService.SmallEngine ? 1 : 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string engine }) _settings.Mutate(s => s.OcrEngine = engine);
        };
        Grid.SetColumn(combo, 2); row.Children.Add(combo); panel.Children.Add(row);
        var component = ChipRow("\uE8C1", Loc.T(K.Ocr.DownloadSize), Loc.T(K.Ocr.Component));
        var download = Fluent.RoundButton(Loc.T(K.Common.DownloadAndInstall), Theme.Accent, Fluent.OnAccentColor(Theme.Accent), 12, true);
        Grid.SetColumn(download, 2); component.Children.Add(download);
        var status = new TextBlock { FontSize = 11.5, Foreground = Theme.TextDimBrush,
            Margin = new Thickness(32, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
        var fill = new Border { CornerRadius = new CornerRadius(10), Background = Theme.AccentBrush,
            RenderTransform = new ScaleTransform(0, 1), RenderTransformOrigin = new Point(0, 0.5) };
        var progress = new Border { Height = 4, Background = Theme.TrackBgBrush, Child = fill,
            CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new Thickness(32, 0, 0, 8) };
        var content = new StackPanel(); content.Children.Add(component); content.Children.Add(status); content.Children.Add(progress);
        panel.Children.Add(content);
        void Refresh()
        {
            bool available = OcrSmallComponent.Available, busy = OcrSmallComponent.Busy;
            var snapshot = OcrSmallComponent.CurrentProgress;
            small.IsEnabled = available;
            component.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            download.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            download.Content = Loc.T(busy ? K.Common.Cancel : K.Common.DownloadAndInstall);
            progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ((ScaleTransform)fill.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(OcrSmallComponent.Progress, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
            status.Text = available ? Loc.T(K.Ocr.Installed) : snapshot.Phase switch
            {
                OcrComponentPhase.Connecting => Loc.T(K.Settings.ComponentConnecting),
                OcrComponentPhase.Downloading => Loc.F(K.Settings.ComponentDownloading, (int)(OcrSmallComponent.Progress * 100),
                    (snapshot.Bytes / 1048576d).ToString("0.0"), (OcrSmallComponent.DownloadBytes / 1048576d).ToString("0.0"),
                    snapshot.Speed >= 1048576 ? $"{snapshot.Speed / 1048576:0.0} MiB/s" : $"{snapshot.Speed / 1024:0} KiB/s"),
                OcrComponentPhase.Verifying => Loc.T(K.Ocr.Verifying),
                OcrComponentPhase.Installing => Loc.T(K.Ocr.Installing),
                OcrComponentPhase.Cancelled => Loc.T(K.Common.ComponentCancelled),
                OcrComponentPhase.Failed => Loc.T(K.Ocr.InstallFailed),
                _ => Loc.T(K.Ocr.DownloadSize),
            };
        }
        void Changed()
        {
            if (Dispatcher.HasShutdownStarted) return;
            if (Dispatcher.CheckAccess()) Refresh();
            else Dispatcher.BeginInvoke(new Action(Refresh));
        }
        download.Click += async (_, _) =>
        {
            if (OcrSmallComponent.Busy) OcrSmallComponent.Cancel();
            else await OcrSmallComponent.InstallAsync();
        };
        content.Loaded += (_, _) => { OcrSmallComponent.Changed += Changed; Refresh(); };
        content.Unloaded += (_, _) => OcrSmallComponent.Changed -= Changed;
        Refresh();
    }
}
