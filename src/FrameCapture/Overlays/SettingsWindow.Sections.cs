using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>截图、录制与系统子页的设置行。</summary>
public sealed partial class SettingsWindow
{
    private void BuildSelectionSettings(StackPanel panel)
    {
        var snapDwellRow = SnapDwellRow();
        snapDwellRow.IsEnabled = _settings.Current.SnapContentArea;
        snapDwellRow.Opacity = snapDwellRow.IsEnabled ? 1 : 0.5;
        panel.Children.Add(ToggleRow("\uE740", Loc.T(K.Settings.SnapToControls), Loc.T(K.Settings.HoveringSnapsToASpecific),
            _settings.Current.SnapContentArea, v =>
            {
                _settings.Mutate(s => s.SnapContentArea = v);
                snapDwellRow.IsEnabled = v; snapDwellRow.Opacity = v ? 1 : 0.5;
            }));
        panel.Children.Add(snapDwellRow);
        panel.Children.Add(EditableSelectionRow());
        panel.Children.Add(ToggleRow("\uE7C4", Loc.T(K.Settings.RightClickCancelsPicker), Loc.T(K.Settings.RightClickCancelsPickerTip),
            _settings.Current.PickerRightClickCancel, v => _settings.Mutate(s => s.PickerRightClickCancel = v)));
        var shadow = Fluent.Revealable(ToggleRow("\uE7B3", Loc.T(K.Settings.IncludeTheShadowInWindow), Loc.T(K.Settings.WithTheSwitchAbove),
            _settings.Current.WindowCaptureShadow, v => _settings.Mutate(s => s.WindowCaptureShadow = v)));
        shadow.Margin = new Thickness(24, 0, 0, 0);
        if (_settings.Current.WindowCaptureEnabled) Fluent.RevealOpen(shadow);
        panel.Children.Add(ToggleRow("\uE7C8", Loc.T(K.Settings.ClickAWindowToCapture), Loc.T(K.Settings.ClickingAWindowThePicker),
            _settings.Current.WindowCaptureEnabled, v =>
            {
                _settings.Mutate(s => s.WindowCaptureEnabled = v);
                if (v) Fluent.Reveal(shadow); else Fluent.Conceal(shadow);
            }));
        panel.Children.Add(shadow);
        var snapLast = Fluent.Revealable(ToggleRow("\uE7C4", Loc.T(K.Settings.SnapLastRegion), Loc.T(K.Settings.SnapLastRegionHint),
            _settings.Current.SnapLastRegion, v => _settings.Mutate(s => s.SnapLastRegion = v)));
        snapLast.Margin = new Thickness(24, 0, 0, 0);
        if (_settings.Current.ShowLastRegion) Fluent.RevealOpen(snapLast);
        panel.Children.Add(ToggleRow("\uE7C4", Loc.T(K.Settings.ShowTheLastSelection), Loc.T(K.Settings.AfterTheSelectionHotkey),
            _settings.Current.ShowLastRegion, v =>
            {
                _settings.Mutate(s => s.ShowLastRegion = v);
                if (v) Fluent.Reveal(snapLast); else Fluent.Conceal(snapLast);
            }));
        panel.Children.Add(snapLast);
        panel.Children.Add(ToggleRow("\uE721", Loc.T(K.Settings.SelectionMagnifier), Loc.T(K.Settings.WhileYouDragOrHover),
            _settings.Current.PickerMagnifier, v => _settings.Mutate(s => s.PickerMagnifier = v)));
        var pregrabRow = SliderRow("\uE7C8", Loc.T(K.Settings.WindowsPreCapturedOnFreeze), Loc.T(K.Settings.WhenBothFreezeTheScreen),
            0, 16, 1, _settings.Current.FreezePregrabMaxWindows,
            v => v <= 0 ? Loc.T(K.Settings.None) : Loc.F(K.Settings.Windows, v), v => _settings.Mutate(s => s.FreezePregrabMaxWindows = (int)v));
        void SetPregrabVisible(bool vis)
        {
            if (vis)
            {
                pregrabRow.Visibility = Visibility.Visible;
                pregrabRow.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0,
                    new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                var fade = new DoubleAnimation(0.0,
                    new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.ExitMs)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                fade.Completed += (_, _) => pregrabRow.Visibility = Visibility.Collapsed;
                pregrabRow.BeginAnimation(OpacityProperty, fade);
            }
        }
        if (!_settings.Current.FreezeOnSelect) { pregrabRow.Opacity = 0; pregrabRow.Visibility = Visibility.Collapsed; }
        panel.Children.Add(ToggleRow("\uE7C4", Loc.T(K.Settings.FreezeTheScreenWhileSelecting), Loc.T(K.Settings.TheInstantTheSelectionHotkey),
            _settings.Current.FreezeOnSelect, v => { _settings.Mutate(s => s.FreezeOnSelect = v); SetPregrabVisible(v); }));
        panel.Children.Add(pregrabRow);
        panel.Children.Add(ColorFormatRow());

        panel.Children.Add(DelayRow());
        Cardify(panel);
    }

    private void BuildCaptureFeedback(StackPanel panel)
    {
        panel.Children.Add(ToggleRow("\uE71A", Loc.T(K.Settings.AlwaysShowTheCaptureOutline), Loc.T(K.Settings.KeepsTheCurrentCaptureRegion),
            _settings.Current.AlwaysShowRegionOutline, v => _settings.Mutate(s => s.AlwaysShowRegionOutline = v)));
        panel.Children.Add(OutlineFadeRow());
        panel.Children.Add(ToastRow());
        panel.Children.Add(CaptureSoundRow());

        
        Cardify(panel);
    }

    private void BuildRecordingQuality(StackPanel panel)
    {
        var fpsRow = SliderRow("\uEC4A", Loc.T(K.Settings.RecordingFrameRate), Loc.T(K.Settings.FramesCapturedPerSecondWhile),
            AppSettings.FpsMin, AppSettings.FpsMax, 5, _settings.Current.Fps,
            v => $"{v:0} fps", v => _settings.Mutate(s => s.Fps = (int)v));
        var fpsFold = Fluent.Revealable(fpsRow);
        if (!_settings.Current.FollowMonitorRefresh) Fluent.RevealOpen(fpsFold);
        panel.Children.Add(fpsFold);
        int hz = Core.DisplayRefresh.RateOf(Capture.Monitors.All().FirstOrDefault(m => m.IsPrimary)?.DeviceName);
        panel.Children.Add(ToggleRow("\uE7F4", Loc.T(K.Settings.RecordAtFullMonitorRefresh),
            (hz > 0 ? Loc.F(K.Settings.TheRecordingFrameRateThen2, hz, hz)
                    : Loc.T(K.Settings.TheRecordingFrameRateThen))
            + Loc.T(K.Settings.HighRefreshPanelsAtHz),
            _settings.Current.FollowMonitorRefresh,
            v =>
            {
                _settings.Mutate(s => s.FollowMonitorRefresh = v);
                if (v) Fluent.Conceal(fpsFold); else Fluent.Reveal(fpsFold);
            }));
        panel.Children.Add(ToggleRow("\uE7C8", Loc.T(K.Settings.RecordWindowOnly), Loc.T(K.Settings.WhenYouClickAWindow),
            _settings.Current.RecordWindowClean, v => _settings.Mutate(s => s.RecordWindowClean = v)));
        panel.Children.Add(ToggleRow("\uE8B9", Loc.T(K.Settings.CaptureTheAppItself), Loc.T(K.Settings.WhenOnTheEditorTheRecord),
            _settings.Current.CaptureSelf, value =>
            {
                _settings.Mutate(settings => settings.CaptureSelf = value);
                foreach (Window window in Application.Current.Windows) ApplyCaptureAffinity(window);
            }));
        Cardify(panel);
    }

    private void BuildRecordingFeedback(StackPanel panel)
    {
        panel.Children.Add(ToggleRow("\uE7C4", Loc.T(K.Settings.ShowTheRegionBorderWhile), Loc.T(K.Settings.DuringARecordingTheReal),
            _settings.Current.ShowRecordingBorder, v => _settings.Mutate(s => s.ShowRecordingBorder = v)));
        panel.Children.Add(ToggleRow("\uE8B0", Loc.T(K.Settings.IncludeTheMousePointer), Loc.T(K.Settings.TheCursorAppears),
            _settings.Current.RecordIncludeCursor, v => _settings.Mutate(s => s.RecordIncludeCursor = v)));

        
        Cardify(panel);
    }

}
