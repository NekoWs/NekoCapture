using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NekoCapture.Core;
using NekoCapture.Overlays;

static partial class SelfTest
{
    private static void TriggerUiChecks(SettingsWindow window, SettingsService settings, Action<string> snapshot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(SettingsWindow).GetField(name, flags)!.GetValue(window);
        void Open(GestureBindingDto binding)
        { typeof(SettingsWindow).GetMethod("OpenSettingsBinding", flags)!.Invoke(window, new object[] { binding }); PumpMs(Fluent.Motion.QuickMs + Fluent.Motion.EnterMs + 80); }
        var clipboard = settings.Current.Gestures.Bindings.First(b => b.Source == TriggerSources.TrayMenu && b.EntryId == TrayDefaults.Clipboard);
        Open(clipboard);
        var bodies = (Dictionary<string, StackPanel>)Field("_settingsBodies")!;
        var page = bodies["binding:" + clipboard.Id];
        var source = Descendants(page).OfType<ComboBox>().Single(c => c.Tag as string == "settings-trigger-source");
        Chk("trigger-ui-exposes-all-input-sources", source.Items.Count == 4);
        Chk("trigger-ui-menu-hides-key-recording", !Descendants(page).OfType<ChordCaptureBox>().Any());
        source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == TriggerSources.Keyboard);
        PumpMs(Fluent.Motion.EnterMs + 80);
        Chk("trigger-ui-changing-source-reveals-key-input", clipboard.Source == TriggerSources.Keyboard
            && Descendants(page).OfType<ChordCaptureBox>().Any() && Descendants(page).Contains(source));
        source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == TriggerSources.TrayMenu);
        PumpMs(Fluent.Motion.EnterMs + 80);
        var add = Descendants(page).OfType<Button>().Single(button => button.Tag as string == "settings-binding-add-override");
        add.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        PumpMs(Fluent.Motion.EnterMs + 80);
        var actions = Descendants(page).OfType<ComboBox>().Single(c => c.Tag as string == "settings-binding-action-picker");
        var settingsPicker = Descendants(page).OfType<ComboBox>().Single(c => c.Tag as string == "settings-binding-setting-picker");
        Chk("trigger-ui-picker-is-inline-and-readable", add.ContextMenu == null && actions.Items.Count > 0
            && actions.Items.OfType<ComboBoxItem>().All(item => item.Content is string text && !text.Contains("›")));
        Chk("trigger-ui-picker-does-not-change-settings-before-confirm", clipboard.Overrides.Count == 0);
        var firstAction = actions.SelectedItem;
        actions.SelectedIndex = actions.Items.Count - 1; PumpMs(30);
        Chk("trigger-ui-picker-keeps-setting-control-on-action-change", Descendants(page).Contains(settingsPicker) && settingsPicker.Items.Count > 0);
        actions.SelectedItem = firstAction;
        settingsPicker.SelectedItem = settingsPicker.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == nameof(BehaviorNode.ContinueOnFailure));
        snapshot("trigger-setting-picker");
        var confirm = Descendants(page).OfType<Button>().Single(button => button.Tag as string == "settings-binding-confirm-override");
        confirm.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); PumpMs(Fluent.Motion.EnterMs + 80);
        string node = settings.Current.Gestures.Graph.Flow(clipboard.EntryId)!.Steps[0];
        Chk("trigger-ui-overrides-non-selection-parameter", clipboard.Overrides[node].ContainsKey(nameof(BehaviorNode.ContinueOnFailure))
            && !AllText(page).Any(text => text.Text == Loc.T(K.Triggers.CaptureMode)));
        snapshot("trigger-menu-details");
        var reset = Descendants(page).OfType<Button>().Single(button => Equals(button.ToolTip, Loc.T(K.Triggers.ResetOverride)));
        reset.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); PumpMs(Fluent.Motion.EnterMs + 80);
        Chk("trigger-ui-removes-local-overrides", clipboard.Overrides.Count == 0);
        typeof(SettingsWindow).GetMethod("NavigateSettings", flags)!.Invoke(window, new object[] { "hotkeys", false, true });
        PumpMs(50); snapshot("triggers-overview");
    }
}
