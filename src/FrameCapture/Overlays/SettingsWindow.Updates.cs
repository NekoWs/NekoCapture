using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FrameCapture.Core;
using FrameCapture.Services;
using FrameCapture.Updates;

namespace FrameCapture.Overlays;

/// <summary>更新开关与关于页的检查、下载及安装状态。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement AutoUpdateOption() => ToggleRow("\uE946", Loc.T(K.Updates.AutoCheck), Loc.T(K.Updates.AutoCheckHint),
        _settings.Current.AutoCheckUpdates, async enabled => await _settings.MutateAsync(s => s.AutoCheckUpdates = enabled));

    private FrameworkElement AboutUpdateOptions()
    {
        var service = AppHost.Current.Updates;
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        var row = ChipRow("\uE946", Loc.T(K.Updates.Check), Loc.T(K.Updates.Software));
        var check = AboutButton(K.Updates.Check);
        Grid.SetColumn(check, 2); row.Children.Add(check); panel.Children.Add(row);
        var result = new TextBlock
        {
            Foreground = Theme.TextBrush, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(32, 8, 4, 4), Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite);
        var progress = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Height = 4, BorderThickness = new Thickness(0),
            Foreground = Theme.AccentBrush, Background = Theme.TrackBgBrush,
            Margin = new Thickness(32, 6, 4, 8), Visibility = Visibility.Collapsed,
        };
        var skip = new CheckBox { Content = Loc.T(K.Updates.Skip), Foreground = Theme.TextBrush, Margin = new Thickness(32, 8, 0, 4) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        var cancel = AboutButton(K.Common.Cancel);
        var update = AboutButton(K.Updates.Update, accent: true);
        cancel.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(cancel); actions.Children.Add(update);
        panel.Children.Add(result); panel.Children.Add(progress); panel.Children.Add(skip); panel.Children.Add(actions);
        string? offer = null;
        UpdateStage? prior = null;
        bool attached = false;

        void Render()
        {
            var stage = service?.Stage ?? UpdateStage.Idle;
            var release = service?.Release;
            bool transferring = stage is UpdateStage.Downloading or UpdateStage.Waiting or UpdateStage.Installing or UpdateStage.Cancelling;
            bool available = release != null && stage is UpdateStage.Available or UpdateStage.Failed;
            if (offer != release?.Version) { offer = release?.Version; skip.IsChecked = false; }
            check.Content = Loc.T(stage == UpdateStage.Checking ? K.Updates.Checking : K.Updates.Check);
            check.IsEnabled = service?.Busy != true;
            result.Text = stage switch
            {
                UpdateStage.Checking => "",
                UpdateStage.Current => Loc.T(K.Updates.Current),
                UpdateStage.Available => Loc.F(K.Updates.Available, release!.Version),
                UpdateStage.CheckFailed => Loc.T(K.Updates.CheckFailed),
                UpdateStage.Downloading => Loc.F(K.Updates.Progress, (service?.Downloaded ?? 0) / 1048576.0, release!.Length / 1048576.0),
                UpdateStage.Waiting => Loc.T(K.Updates.Waiting),
                UpdateStage.Installing => Loc.T(K.Updates.Installing),
                UpdateStage.Cancelling => Loc.T(K.Updates.Cancelling),
                UpdateStage.Cancelled => Loc.T(K.Updates.Cancelled),
                UpdateStage.Skipped => Loc.T(K.Updates.Skipped),
                UpdateStage.Failed => Loc.T(K.Updates.Failed),
                _ => "",
            };
            result.Visibility = result.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            progress.Visibility = transferring || stage == UpdateStage.Checking ? Visibility.Visible : Visibility.Collapsed;
            progress.IsIndeterminate = stage != UpdateStage.Downloading;
            progress.Value = release is { Length: > 0 } ? 100.0 * (service?.Downloaded ?? 0) / release.Length : 0;
            skip.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            actions.Visibility = available || transferring ? Visibility.Visible : Visibility.Collapsed;
            update.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            update.Content = Loc.T(stage == UpdateStage.Failed ? K.Updates.Retry : K.Updates.Update);
            cancel.IsEnabled = stage is not (UpdateStage.Installing or UpdateStage.Cancelling);
            if (prior != stage && result.IsVisible)
                result.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
            prior = stage;
        }

        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            check.Content = Loc.T(K.Updates.Checking);
            result.Visibility = Visibility.Collapsed; progress.Visibility = Visibility.Visible;
            progress.IsIndeterminate = true;
            await Dispatcher.Yield(DispatcherPriority.Render);
            try
            {
                if (service != null) await service.CheckAsync();
                else { result.Text = Loc.T(K.Updates.CheckFailed); result.Visibility = Visibility.Visible; }
            }
            finally { if (service != null) Render(); else { check.IsEnabled = true; check.Content = Loc.T(K.Updates.Check); progress.Visibility = Visibility.Collapsed; } }
        };
        cancel.Click += (_, _) => service?.DismissUpdate(skip.IsChecked == true);
        update.Click += (_, _) => service?.InstallUpdate();
        void SyncAttachment()
        {
            bool visible = panel.IsVisible && panel.IsLoaded;
            if (service != null && visible != attached)
            {
                attached = visible;
                if (visible) { service.Changed += Render; service.AttachInlineView(); }
                else { service.Changed -= Render; service.DetachInlineView(); }
            }
            if (visible) Render();
        }
        panel.Loaded += (_, _) => SyncAttachment();
        panel.IsVisibleChanged += (_, _) => SyncAttachment();
        panel.Unloaded += (_, _) =>
        {
            if (!attached || service == null) return;
            attached = false; service.Changed -= Render; service.DetachInlineView();
        };
        Render();
        return panel;
    }
}
