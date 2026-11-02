using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NekoCapture.Core;
using NekoCapture.Overlays;

static partial class SelfTest
{
    private static void TriggerMenuUiChecks(SettingsWindow window, SettingsService settings, Action<string> snapshot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SettingsWindow);
        var bodies = (Dictionary<string, StackPanel>)type.GetField("_settingsBodies", flags)!.GetValue(window)!;
        type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object[] { "hotkeys", false, true }); PumpMs(50);
        var cfg = settings.Current.Gestures;
        int bindingsBefore = cfg.Bindings.Count, flowsBefore = cfg.Graph.Flows.Count;
        var create = Descendants(bodies["hotkeys"]).OfType<Button>().Single(button => button.Tag as string == "settings-add-menu-item");
        create.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); PumpMs(Fluent.Motion.QuickMs + Fluent.Motion.EnterMs + 80);
        var binding = cfg.Bindings.Last(); var page = bodies["binding:" + binding.Id];
        Chk("tray-config-create-opens-independent-menu", cfg.Bindings.Count == bindingsBefore + 1 && cfg.Graph.Flows.Count == flowsBefore + 1
            && binding.Source == TriggerSources.TrayMenu && !Descendants(page).OfType<ChordCaptureBox>().Any()
            && cfg.Bindings.Where(other => other.Id != binding.Id).All(other => other.EntryId != binding.EntryId));
        var rename = Descendants(page).OfType<Border>().Single(control => control.ToolTip as string == Loc.T(K.Hotkeys.RenameThisHotkey));
        rename.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        PumpMs(30);
        var name = Descendants(page).OfType<TextBox>().Single();
        name.Text = "My capture"; name.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        PumpMs(30);
        Chk("tray-config-name-is-editable", binding.Name == "My capture" && AllText(page).Any(text => text.Text == binding.Name));
        var open = Descendants(page).OfType<Button>().Single(button => button.Tag as string == "settings-menu-icon-open");
        void Expand() { open.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); PumpMs(Fluent.Motion.EnterMs + 80); }
        void Choose(string id)
        {
            Descendants(page).OfType<Button>().Single(button => button.Tag as string == "settings-menu-icon-choice:" + id)
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); PumpMs(Fluent.Motion.EnterMs + 80);
        }
        Expand();
        var icons = Descendants(page).OfType<Button>().Where(button => (button.Tag as string)?.StartsWith("settings-menu-icon-choice:") == true).ToArray();
        Chk("tray-config-icon-picker-is-visual-and-labelled", icons.Length == TrayMenuIcons.All.Count + 1
            && icons.All(button => !string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(button)))
            && icons.All(button => button.ActualWidth >= 40 && button.ActualHeight >= 40));
        snapshot("tray-menu-icon-choices");
        Choose("record");
        Chk("tray-config-icon-selection-keeps-control", binding.MenuIcon == "record" && Descendants(page).Contains(open)
            && AllText(open).Any(text => text.Text == "\uE714"));
        Expand(); Choose("");
        Chk("tray-config-restores-automatic-icon", binding.MenuIcon == "" && AllText(open).Any(text => text.Text == Loc.T(K.Triggers.AutoIcon)));
        Expand(); Choose("star"); snapshot("tray-menu-custom-details");
        var source = Descendants(page).OfType<ComboBox>().Single(combo => combo.Tag as string == "settings-trigger-source");
        source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == TriggerSources.Keyboard);
        PumpMs(Fluent.Motion.EnterMs + 80);
        Chk("tray-config-key-source-hides-menu-only-controls", !Descendants(page).OfType<Button>().Any(button => button.Tag as string == "settings-menu-icon-open"));
        source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == TriggerSources.TrayMenu);
        PumpMs(Fluent.Motion.EnterMs + 80);
        Chk("tray-config-source-roundtrip-keeps-custom-icon", binding.MenuIcon == "star"
            && Descendants(page).OfType<Button>().Any(button => button.Tag as string == "settings-menu-icon-open"));
        type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object[] { "hotkeys", false, true }); PumpMs(50);
        var list = bodies["hotkeys"];
        Chk("tray-config-overview-shows-menu-icon-and-name", AllText(list).Any(text => text.Text == "My capture")
            && AllText(list).Any(text => text.Text == "\uE734"));
        var delete = Descendants(list).OfType<Border>().Single(control => control.Tag as string == "settings-hotkey-delete:" + binding.Id);
        delete.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        PumpMs(Fluent.Motion.StateMs + 80);
        Chk("tray-config-custom-menu-can-be-deleted", cfg.Bindings.Count == bindingsBefore && cfg.Bindings.All(other => other.Id != binding.Id));
        settings.Mutate(_ => cfg.Graph.Flows.RemoveAll(flow => flow.Id == binding.EntryId));
    }
}
