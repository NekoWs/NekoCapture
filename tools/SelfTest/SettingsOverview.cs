using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NekoCapture.Core;
using NekoCapture.Overlays;

static partial class SelfTest
{
    private static void SettingsOverviewChecks(SettingsWindow window, SettingsService settings)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(SettingsWindow);
        void Go(string id) { type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object[] { id, false, true }); PumpMs(50); }
        var bodies = (Dictionary<string, StackPanel>)type.GetField("_settingsBodies", flags)!.GetValue(window)!;
        Go("hotkeys");
        var binding = settings.Current.Gestures.Bindings[0];
        var list = bodies["hotkeys"];
        var delete = Descendants(list).OfType<Border>().Single(item => item.Tag as string == "settings-hotkey-delete:" + binding.Id);
        Chk("settings-shortcut-delete-is-visible-in-overview", delete.IsVisible && delete.ActualWidth >= 24 && delete.Focusable);
        Chk("settings-shortcut-rename-remains-in-overview", Descendants(list).OfType<Border>()
            .Any(item => item.IsVisible && item.ToolTip as string == Loc.T(K.Hotkeys.RenameThisHotkey) && item.Focusable));
        void Press(FrameworkElement control)
        {
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
            control.RaiseEvent(down);
            Chk("settings-inner-control-owns-pointer-press", down.Handled && Mouse.Captured is not Button);
            control.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        }
        var enabled = Descendants(list).OfType<Border>().Single(item => item.Tag as string == "settings-hotkey-enabled:" + binding.Id);
        bool wasEnabled = binding.Enabled;
        Press(enabled);
        Chk("settings-shortcut-switch-click-updates-binding", binding.Enabled != wasEnabled);
        Chk("settings-shortcut-switch-does-not-open-detail", (string)type.GetField("_currentSettingsPage", flags)!.GetValue(window)! == "hotkeys");
        int before = settings.Current.Gestures.Bindings.Count;
        Press(delete);
        PumpMs(Fluent.Motion.StateMs + 80);
        Chk("settings-shortcut-overview-delete-removes-binding", settings.Current.Gestures.Bindings.Count == before - 1
            && settings.Current.Gestures.Bindings.All(item => item.Id != binding.Id));
        Chk("settings-shortcut-delete-does-not-open-detail", (string)type.GetField("_currentSettingsPage", flags)!.GetValue(window)! == "hotkeys");
        Go("behaviors");
        Chk("settings-behavior-delete-remains-in-overview", Descendants(bodies["behaviors"]).OfType<Border>()
            .Any(item => item.IsVisible && item.Tag is string tag && tag.StartsWith("settings-behavior-delete:", StringComparison.Ordinal)));
    }
}
