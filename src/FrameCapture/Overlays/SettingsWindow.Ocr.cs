using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>OCR 引擎选择与所选模型的按需下载提示。</summary>
public sealed partial class SettingsWindow
{
    void BuildOcrSettings(StackPanel panel)
    {
        var row = ChipRow("\uE8C1", Loc.T(K.Ocr.EngineDescription), Loc.T(K.Ocr.Engine));
        var combo = Fluent.Ui(new ComboBox { HorizontalAlignment = HorizontalAlignment.Right });
        combo.Items.Add(new ComboBoxItem { Content = Loc.T(K.Ocr.SystemEngine), Tag = OcrService.SystemEngine });
        foreach (var model in OcrModelComponent.All)
            combo.Items.Add(new ComboBoxItem { Content = Loc.T(model.Label), Tag = model.Engine });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == _settings.Current.OcrEngine)
            ?? combo.Items[0];
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var uninstall = Fluent.IconButton("\uE74D", Loc.T(K.Ocr.Uninstall), 28);
        uninstall.Tag = "ocr-model-uninstall";
        uninstall.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetShowOnDisabled(uninstall, true);
        var deleteMotion = new OcrDeleteMotion(uninstall);
        controls.Children.Add(deleteMotion.Host); controls.Children.Add(combo);
        Grid.SetColumn(controls, 2); row.Children.Add(controls); panel.Children.Add(row);
        var prompt = new StackPanel { Margin = new Thickness(32, 0, 0, 0), Tag = "ocr-model-download" };
        panel.Children.Add(prompt);
        OcrModelComponent? shown = null;
        TextBlock? status = null;
        Button? download = null;
        Border? progress = null, fill = null;
        double displayedProgress = -1;

        void UpdateStatus(OcrModelComponent model)
        {
            if (status == null || download == null || progress == null || fill == null) return;
            var snapshot = model.CurrentProgress;
            bool busy = model.Busy;
            bool removing = snapshot.Phase == OcrComponentPhase.Uninstalling;
            download.Visibility = model.Available ? Visibility.Collapsed : Visibility.Visible;
            download.IsEnabled = !removing;
            download.Content = Loc.T(busy && !removing ? K.Common.Cancel : K.Common.DownloadAndInstall);
            progress.Visibility = busy && !removing ? Visibility.Visible : Visibility.Collapsed;
            double fraction = Math.Clamp(snapshot.Bytes / (double)model.DownloadBytes, 0, 1);
            if (fraction != displayedProgress)
            {
                displayedProgress = fraction;
                ((ScaleTransform)fill.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(fraction, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
            }
            status.Text = snapshot.Phase switch
            {
                OcrComponentPhase.Connecting => Loc.T(K.Settings.ComponentConnecting),
                OcrComponentPhase.Downloading => Loc.F(K.Settings.ComponentDownloading, (int)(fraction * 100),
                    (snapshot.Bytes / 1048576d).ToString("0.0"), (model.DownloadBytes / 1048576d).ToString("0.0"),
                    snapshot.Speed >= 1048576 ? $"{snapshot.Speed / 1048576:0.0} MiB/s" : $"{snapshot.Speed / 1024:0} KiB/s"),
                OcrComponentPhase.Verifying => Loc.T(K.Ocr.Verifying),
                OcrComponentPhase.Installing => Loc.T(K.Ocr.Installing),
                OcrComponentPhase.Cancelled => Loc.T(K.Common.ComponentCancelled),
                OcrComponentPhase.Failed => Loc.T(K.Ocr.InstallFailed),
                OcrComponentPhase.Uninstalling => Loc.T(K.Ocr.Uninstalling),
                OcrComponentPhase.UninstallFailed => Loc.T(K.Ocr.UninstallFailed),
                _ => Loc.F(K.Ocr.NeedsDownload, Loc.T(model.Label), (model.DownloadBytes / 1048576d).ToString("0")),
            };
        }

        void RenderPrompt(OcrModelComponent? model)
        {
            prompt.Children.Clear(); status = null; download = null; progress = fill = null; displayedProgress = -1;
            if (model == null) return;
            var line = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            status = new TextBlock { FontSize = 12, Foreground = Theme.TextDimBrush, VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
            download = Fluent.RoundButton(Loc.T(K.Common.DownloadAndInstall), Theme.Accent, Fluent.OnAccentColor(Theme.Accent), 12, true);
            download.Click += async (_, _) => { if (model.Busy) model.Cancel(); else await model.InstallAsync(); };
            Grid.SetColumn(download, 1); line.Children.Add(status); line.Children.Add(download); prompt.Children.Add(line);
            fill = new Border { CornerRadius = new CornerRadius(10), Background = Theme.AccentBrush,
                RenderTransform = new ScaleTransform(0, 1), RenderTransformOrigin = new Point(0, 0.5) };
            progress = new Border { Height = 4, Background = Theme.TrackBgBrush, Child = fill,
                CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 8) };
            prompt.Children.Add(progress); UpdateStatus(model);
        }

        void Refresh(bool animate)
        {
            var selected = OcrModelComponent.For(_settings.Current.OcrEngine);
            deleteMotion.SetVisible(selected != null, animate);
            uninstall.IsEnabled = selected is { HasFiles: true, Busy: false, InUse: false };
            uninstall.Opacity = uninstall.IsEnabled ? 1 : 0.45;
            uninstall.ToolTip = Loc.T(selected?.InUse == true ? K.Ocr.ModelInUse
                : selected?.CurrentProgress.Phase == OcrComponentPhase.Uninstalling ? K.Ocr.Uninstalling : K.Ocr.Uninstall);
            var needed = selected != null && (!selected.Available || selected.CurrentProgress.Phase == OcrComponentPhase.UninstallFailed)
                ? selected : null;
            if (ReferenceEquals(shown, needed)) { if (needed != null) UpdateStatus(needed); return; }
            shown = needed;
            if (animate) TransitionContent(prompt, () => RenderPrompt(needed));
            else RenderPrompt(needed);
        }

        void Changed()
        {
            if (Dispatcher.HasShutdownStarted) return;
            if (Dispatcher.CheckAccess()) Refresh(animate: true);
            else Dispatcher.BeginInvoke(new Action(() => Refresh(animate: true)));
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string engine } || engine == _settings.Current.OcrEngine) return;
            _settings.Mutate(s => s.OcrEngine = engine); Refresh(animate: true);
        };
        uninstall.Click += async (_, _) =>
        {
            if (OcrModelComponent.For(_settings.Current.OcrEngine) is { } model) await model.UninstallAsync();
        };
        prompt.Loaded += (_, _) => { foreach (var model in OcrModelComponent.All) model.Changed += Changed; Refresh(animate: false); };
        prompt.Unloaded += (_, _) => { foreach (var model in OcrModelComponent.All) model.Changed -= Changed; };
        Refresh(animate: false);
    }
}
