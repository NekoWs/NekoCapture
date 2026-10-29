using System.Windows;
using System.Windows.Controls;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>自启和权限选项共用登记事务；失败或取消时恢复开关。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement StartupOptions(Func<bool, bool, Task<StartupResult>>? apply = null, Func<bool>? isElevated = null)
    {
        isElevated ??= () => StartupService.IsElevated;
        apply ??= (enable, elevated) => AppHost.Current.ConfigureStartupAsync(enable, elevated, "--settings --page system");
        var panel = new StackPanel();
        var status = UiNote("");
        status.Visibility = Visibility.Collapsed;
        Action<bool> setStartup = _ => { }, setAdmin = _ => { };
        void Sync()
        {
            setStartup(_settings.Current.RunAtStartup);
            setAdmin(isElevated());
        }
        async void Apply(bool enable, bool elevated, bool changePermission)
        {
            panel.IsEnabled = false;
            Sync();
            status.Text = changePermission ? Loc.T(K.Settings.AdministratorChanging) : "";
            status.Visibility = changePermission ? Visibility.Visible : Visibility.Collapsed;
            try
            {
                var result = await apply(enable, elevated);
                if (result is StartupResult.Cancelled or StartupResult.Failed)
                {
                    status.Text = Loc.T(result == StartupResult.Cancelled ? K.Common.StartupTaskCancelled
                        : changePermission ? K.Settings.AdministratorChangeFailed : K.Common.StartupTaskFailed);
                    status.Visibility = Visibility.Visible;
                }
                else status.Visibility = Visibility.Collapsed;
            }
            finally { Sync(); panel.IsEnabled = true; }
        }
        panel.Children.Add(ToggleRow("\uE7E8", Loc.T(K.Common.StartWithWindows), Loc.T(K.Settings.StartsFramecaptureWhenYouSign),
            _settings.Current.RunAtStartup, value => Apply(value, isElevated(), false), out setStartup));
        panel.Children.Add(ToggleRow("\uE7EF", Loc.T(K.Common.RunAsAdministrator), Loc.T(K.Settings.RunAsAdministratorTip),
            isElevated(), value => Apply(_settings.Current.RunAtStartup, value, true), out setAdmin));
        panel.Children.Add(status);
        panel.Loaded += (_, _) => { _settings.Changed += Sync; Sync(); };
        panel.Unloaded += (_, _) => _settings.Changed -= Sync;
        return panel;
    }
}
