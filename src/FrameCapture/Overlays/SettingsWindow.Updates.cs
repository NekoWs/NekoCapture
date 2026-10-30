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
        var panel = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Updates.Software), FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 12, 0), TextWrapping = TextWrapping.Wrap,
        });
        var check = AboutButton(K.Updates.Check, accent: true);
        Grid.SetColumn(check, 1); row.Children.Add(check); panel.Children.Add(row);
        var details = new StackPanel { Margin = new Thickness(2, 14, 2, 0) };
        var detailsHost = new Border { Child = details, ClipToBounds = true, Visibility = Visibility.Collapsed,
            Opacity = 0, Tag = "settings-update-result" };
        panel.Children.Add(detailsHost);
        var result = new TextBlock
        {
            Foreground = Theme.TextBrush, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite);
        var progress = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Height = 4, BorderThickness = new Thickness(0),
            Foreground = Theme.AccentBrush, Background = Theme.TrackBgBrush,
            Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed,
        };
        var amount = new TextBlock { Foreground = Theme.TextDimBrush, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var skip = new CheckBox
        {
            Content = new TextBlock { Text = Loc.T(K.Updates.Skip), TextWrapping = TextWrapping.Wrap },
            Foreground = Theme.TextBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(actions, 1); footer.Children.Add(skip); footer.Children.Add(actions);
        var cancel = AboutButton(K.Common.Cancel);
        var update = AboutButton(K.Updates.Update, accent: true);
        cancel.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(cancel); actions.Children.Add(update);
        details.Children.Add(result); details.Children.Add(progress); details.Children.Add(amount); details.Children.Add(footer);
        string? offer = null;
        UpdateStage? prior = null;
        bool attached = false;
        bool detailsShown = false;
        int detailsGeneration = 0;
        bool snapDetails = false;

        void ShowDetails(bool show)
        {
            if (detailsShown == show) return;
            detailsShown = show; int generation = ++detailsGeneration;
            double fromHeight = detailsHost.Visibility == Visibility.Visible ? detailsHost.ActualHeight : 0;
            double fromOpacity = detailsHost.Opacity;
            detailsHost.BeginAnimation(HeightProperty, null); detailsHost.BeginAnimation(OpacityProperty, null);
            if (!panel.IsVisible || snapDetails)
            {
                detailsHost.Height = double.NaN;
                detailsHost.Opacity = show ? 1 : 0;
                detailsHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            detailsHost.Visibility = Visibility.Visible;
            detailsHost.RenderTransform = null;
            details.Measure(new Size(Math.Max(1, panel.ActualWidth), double.PositiveInfinity));
            double targetHeight = show ? details.DesiredSize.Height : 0;
            detailsHost.Height = fromHeight;
            var duration = TimeSpan.FromMilliseconds(show ? Fluent.Motion.EnterMs : Fluent.Motion.ExitMs);
            var height = new DoubleAnimation(fromHeight, targetHeight, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            height.Completed += (_, _) =>
            {
                if (generation != detailsGeneration) return;
                detailsHost.BeginAnimation(HeightProperty, null); detailsHost.Height = double.NaN;
                detailsHost.BeginAnimation(OpacityProperty, null); detailsHost.Opacity = show ? 1 : 0;
                detailsHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            };
            detailsHost.BeginAnimation(HeightProperty, height);
            detailsHost.BeginAnimation(OpacityProperty, new DoubleAnimation(fromOpacity, show ? 1 : 0,
                TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs)));
        }

        void Render()
        {
            var stage = service?.Stage ?? UpdateStage.Idle;
            var release = service?.Release;
            bool transferring = stage is UpdateStage.Downloading or UpdateStage.Waiting or UpdateStage.Installing or UpdateStage.Cancelling;
            bool available = release != null && stage is UpdateStage.Available or UpdateStage.Failed;
            if (offer != release?.Version) { offer = release?.Version; skip.IsChecked = false; }
            check.Content = Loc.T(stage == UpdateStage.Checking ? K.Updates.Checking : K.Updates.Check);
            check.IsEnabled = service?.Busy != true;
            if (stage == UpdateStage.Idle)
            {
                ShowDetails(false); prior = stage;
                return;
            }
            void ApplyDetails()
            {
                result.Text = stage switch
                {
                    UpdateStage.Checking => "",
                    UpdateStage.Current => Loc.T(K.Updates.Current),
                    UpdateStage.Available => Loc.F(K.Updates.Available, release!.Version),
                    UpdateStage.CheckFailed => Loc.T(K.Updates.CheckFailed),
                    UpdateStage.Downloading => Loc.T(K.Updates.Downloading),
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
                amount.Text = stage == UpdateStage.Downloading
                    ? Loc.F(K.Updates.Progress, (service?.Downloaded ?? 0) / 1048576.0, release!.Length / 1048576.0) : "";
                amount.Visibility = amount.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                skip.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
                footer.Visibility = available || transferring ? Visibility.Visible : Visibility.Collapsed;
                update.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
                update.Content = Loc.T(stage == UpdateStage.Failed ? K.Updates.Retry : K.Updates.Update);
                cancel.IsEnabled = stage is not (UpdateStage.Installing or UpdateStage.Cancelling);
            }
            if (prior != stage && detailsHost.IsVisible && detailsShown) TransitionContent(details, ApplyDetails, direction: 0);
            else { if (!detailsShown) CancelContentTransitions(details); ApplyDetails(); }
            ShowDetails(true);
            prior = stage;
        }

        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            check.Content = Loc.T(K.Updates.Checking);
            result.Visibility = Visibility.Collapsed; progress.Visibility = Visibility.Visible; ShowDetails(true);
            footer.Visibility = Visibility.Collapsed; amount.Visibility = Visibility.Collapsed;
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
            bool entering = visible && !attached;
            if (service != null && visible != attached)
            {
                attached = visible;
                if (visible) { service.Changed += Render; service.AttachInlineView(); }
                else { service.Changed -= Render; service.DetachInlineView(); }
            }
            if (visible)
            {
                snapDetails = entering;
                try { Render(); }
                finally { snapDetails = false; }
            }
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
