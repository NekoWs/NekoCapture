using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;

static partial class SelfTest
{
    static void StartupSettingsUiChecks(SettingsWindow window, SettingsService settings)
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        bool originalStartup = settings.Current.RunAtStartup, originalAdmin = settings.Current.RunAsAdmin;
        settings.Current.RunAsAdmin = true;
        bool actualAdmin = false;
        var completion = new TaskCompletionSource<StartupResult>();
        var requested = new List<(bool Startup, bool Admin)>();
        Func<bool, bool, Task<StartupResult>> apply = (enable, elevated) => { requested.Add((enable, elevated)); return completion.Task; };
        var panel = (StackPanel)typeof(SettingsWindow).GetMethod("StartupOptions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, new object[] { apply, (Func<bool>)(() => actualAdmin) })!;
        var fixture = new Window { Content = panel, Width = 500, Height = 160, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false };
        void Click(int row = 0) => panel.Children[row].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        bool AdminOn()
        {
            var track = ((Grid)panel.Children[1]).Children.OfType<Border>().Single();
            return ((TranslateTransform)((Ellipse)track.Child).RenderTransform).X > 10;
        }
        void Settle() => PumpMs((int)Fluent.Motion.StateMs + 50);
        try
        {
            fixture.Show(); Settle();
            Chk("startup-settings-actual-standard-overrides-saved-admin", !AdminOn());
            Click();
            Chk("startup-settings-waits-for-authorization-before-saving", !panel.IsEnabled
                && settings.Current.RunAtStartup == originalStartup && settings.Current.RunAsAdmin);
            completion.SetResult(StartupResult.Cancelled); PumpMs(40);
            Chk("startup-settings-cancel-restores-switch", panel.IsEnabled && ((TextBlock)panel.Children[2]).Text == Loc.T(K.Common.StartupTaskCancelled));
            completion = new TaskCompletionSource<StartupResult>();
            Click(); completion.SetResult(StartupResult.Failed); PumpMs(40);
            Chk("startup-settings-failure-restores-switch-and-explains", panel.IsEnabled && requested.Count == 2
                && requested[0] == requested[1] && ((TextBlock)panel.Children[2]).Text == Loc.T(K.Common.StartupTaskFailed)
                && settings.Current.RunAtStartup == originalStartup && settings.Current.RunAsAdmin);
            Chk("startup-settings-autostart-uses-actual-privileges", !requested[0].Admin);
            completion = new TaskCompletionSource<StartupResult>();
            Click(1); Settle();
            Chk("startup-settings-upgrade-waits-for-real-privilege", !panel.IsEnabled && !AdminOn() && requested[^1].Admin
                && ((TextBlock)panel.Children[2]).Text == Loc.T(K.Settings.AdministratorChanging));
            completion.SetResult(StartupResult.Failed); Settle();
            Chk("startup-settings-upgrade-failure-stays-standard", panel.IsEnabled && !AdminOn()
                && ((TextBlock)panel.Children[2]).Text == Loc.T(K.Settings.AdministratorChangeFailed));
            completion = new TaskCompletionSource<StartupResult>();
            Click(1); actualAdmin = true; settings.Current.RunAsAdmin = false;
            completion.SetResult(StartupResult.Off); Settle();
            Chk("startup-settings-actual-admin-overrides-saved-standard", AdminOn() && ((TextBlock)panel.Children[2]).Visibility == Visibility.Collapsed);
            completion = new TaskCompletionSource<StartupResult>();
            Click(1); Settle();
            Chk("startup-settings-downgrade-keeps-on-until-restart", !panel.IsEnabled && AdminOn() && !requested[^1].Admin);
            completion.SetResult(StartupResult.Cancelled); Settle();
            Chk("startup-settings-downgrade-cancel-stays-elevated", panel.IsEnabled && AdminOn());
            completion = new TaskCompletionSource<StartupResult>();
            Click(1); actualAdmin = false; completion.SetResult(StartupResult.Off); Settle();
            Chk("startup-settings-downgrade-success-shows-standard", panel.IsEnabled && !AdminOn());
        }
        finally
        {
            settings.Current.RunAtStartup = originalStartup; settings.Current.RunAsAdmin = originalAdmin;
            fixture.Close(); SynchronizationContext.SetSynchronizationContext(context);
        }
    }
}
