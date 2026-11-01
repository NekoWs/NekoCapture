using System.Windows;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>启动通知的显示时长。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement WelcomeDurationRow()
    {
        var field = DurationField("settings-duration-welcome", new[]
        {
            (3.0, Str.Empty), (5.0, Str.Empty), (10.0, Str.Empty), (15.0, Str.Empty), (30.0, Str.Empty),
        }, () => _settings.Current.WelcomeToastSeconds, 60, -1, "", Loc.T(K.Settings.WelcomeDurationHint),
            value => _settings.Mutate(settings => settings.WelcomeToastSeconds = Math.Clamp(value, 1, 60)));
        return DurationRow("\uE916", Loc.T(K.Settings.WelcomeDurationHint), Loc.T(K.Settings.WelcomeDuration), field);
    }
}
