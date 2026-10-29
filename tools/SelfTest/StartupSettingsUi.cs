using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    static void StartupSettingsUiChecks(SettingsWindow window, SettingsService settings)
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        bool originalStartup = settings.Current.RunAtStartup, originalAdmin = settings.Current.RunAsAdmin;
        var completion = new TaskCompletionSource<StartupResult>();
        var requested = new List<bool>();
        Func<bool, bool, Task<StartupResult>> apply = (enable, elevated) => { requested.Add(enable); return completion.Task; };
        var panel = (StackPanel)typeof(SettingsWindow).GetMethod("StartupOptions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, new object[] { apply })!;
        var fixture = new Window { Content = panel, Width = 500, Height = 160, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false };
        void Click() => panel.Children[0].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        try
        {
            fixture.Show(); PumpMs(30);
            Click();
            Chk("startup-settings-waits-for-authorization-before-saving", !panel.IsEnabled
                && settings.Current.RunAtStartup == originalStartup && settings.Current.RunAsAdmin == originalAdmin);
            completion.SetResult(StartupResult.Cancelled); PumpMs(40);
            Chk("startup-settings-cancel-restores-switch", panel.IsEnabled && ((TextBlock)panel.Children[2]).Text == Loc.T(K.Common.StartupTaskCancelled));
            completion = new TaskCompletionSource<StartupResult>();
            Click(); completion.SetResult(StartupResult.Failed); PumpMs(40);
            Chk("startup-settings-failure-restores-switch-and-explains", panel.IsEnabled && requested.Count == 2
                && requested[0] == requested[1] && ((TextBlock)panel.Children[2]).Text == Loc.T(K.Common.StartupTaskFailed)
                && settings.Current.RunAtStartup == originalStartup && settings.Current.RunAsAdmin == originalAdmin);
        }
        finally { fixture.Close(); SynchronizationContext.SetSynchronizationContext(context); }
    }
}
