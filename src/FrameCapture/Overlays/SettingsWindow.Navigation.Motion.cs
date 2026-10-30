using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>路径栏在页面退场期间保留占位，换页后平滑展开或收起。</summary>
public sealed partial class SettingsWindow
{
    private int _settingsCrumbGeneration;

    private void ExitSettingsBreadcrumbs()
    {
        _settingsCrumbGeneration++;
        if (_settingsCrumbHost.Visibility != Visibility.Visible) return;
        double height = _settingsCrumbHost.ActualHeight;
        _settingsCrumbHost.BeginAnimation(HeightProperty, null); _settingsCrumbHost.Height = height;
        _settingsCrumbHost.IsHitTestVisible = false;
        var shift = _settingsCrumbs.RenderTransform as TranslateTransform ?? new TranslateTransform();
        _settingsCrumbs.RenderTransform = shift;
        var duration = TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs);
        _settingsCrumbs.BeginAnimation(OpacityProperty, new DoubleAnimation(_settingsCrumbs.Opacity, 0, duration));
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(shift.Y, -4, duration));
    }

    private void AnimateSettingsBreadcrumbs(bool visible, double fromHeight, bool animate)
    {
        int generation = ++_settingsCrumbGeneration;
        _settingsCrumbHost.BeginAnimation(HeightProperty, null);
        _settingsCrumbs.BeginAnimation(OpacityProperty, null);
        _settingsCrumbs.RenderTransform = null;
        _settingsCrumbs.Opacity = 1;
        _settingsCrumbHost.IsHitTestVisible = visible;
        if (!animate)
        {
            _settingsCrumbHost.Height = double.NaN;
            _settingsCrumbHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        _settingsCrumbHost.Visibility = Visibility.Visible;
        double width = Math.Max(1, _settingsScroller.ActualWidth - 40);
        _settingsCrumbs.Measure(new Size(width, double.PositiveInfinity));
        double targetHeight = visible ? _settingsCrumbs.DesiredSize.Height : 0;
        _settingsCrumbHost.Height = fromHeight;
        var duration = TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs);
        var height = new DoubleAnimation(fromHeight, targetHeight, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        height.Completed += (_, _) =>
        {
            if (generation != _settingsCrumbGeneration) return;
            _settingsCrumbHost.BeginAnimation(HeightProperty, null);
            _settingsCrumbHost.Height = double.NaN;
            _settingsCrumbHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        };
        _settingsCrumbHost.BeginAnimation(HeightProperty, height);
        if (!visible) { _settingsCrumbs.Opacity = 0; return; }
        var shift = new TranslateTransform(0, 5); _settingsCrumbs.RenderTransform = shift;
        _settingsCrumbs.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, 0, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
