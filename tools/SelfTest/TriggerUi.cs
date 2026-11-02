using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NekoCapture.Core;
using NekoCapture.Overlays;

static partial class SelfTest
{
    private static void TriggerUiChecks(SettingsWindow window, SettingsService settings, Action<string> snapshot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(SettingsWindow).GetField(name, flags)!.GetValue(window);
        void Call(string name, object argument)
        { typeof(SettingsWindow).GetMethod(name, flags)!.Invoke(window, new[] { argument }); PumpMs(Fluent.Motion.QuickMs + Fluent.Motion.EnterMs + 80); }
        void Open(GestureBindingDto binding) => Call("OpenSettingsBinding", binding);
        var cfg = settings.Current.Gestures;
        var clipboard = cfg.Bindings.First(b => b.Source == TriggerSources.TrayMenu && b.EntryId == TrayDefaults.Clipboard);
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
        Chk("trigger-ui-shows-no-selection-settings-for-clipboard", !AllText(page).Any(text => text.Text == Loc.T(K.Triggers.CaptureMode)));
        Chk("trigger-ui-has-no-parameter-picker", !Descendants(page).OfType<FrameworkElement>()
            .Any(element => element.Tag as string is "settings-binding-action-picker" or "settings-binding-setting-picker" or "settings-binding-add-override"));

        Call("OpenSettingsFlow", clipboard.EntryId);
        var flowPage = bodies["flow"];
        var expose = Descendants(flowPage).OfType<Button>().Single(button => button.Tag as string == "settings-action-exposure");
        expose.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); PumpMs(Fluent.Motion.EnterMs + 80);
        var failure = Descendants(flowPage).OfType<CheckBox>().Single(check => check.Tag as string == "settings-action-expose:ContinueOnFailure");
        failure.IsChecked = true;
        string nodeId = cfg.Graph.Flow(clipboard.EntryId)!.Steps[0];
        var node = cfg.Graph.Node(nodeId)!;
        Chk("trigger-ui-action-controls-exposed-options", node.ExposedParameters!.Contains(nameof(BehaviorNode.ContinueOnFailure)));
        snapshot("action-exposed-options");

        Open(clipboard);
        string tag = "settings-binding-parameter:" + nodeId + ":ContinueOnFailure";
        var parameterRow = Descendants(page).OfType<Grid>().Single(row => row.Tag as string == tag);
        var toggle = Descendants(parameterRow).OfType<Grid>().Single(row => row.ToolTip as string == Loc.T(K.Triggers.FailureHint));
        var reset = Descendants(parameterRow).OfType<Button>().Single();
        Chk("trigger-ui-exposed-option-inherits-without-writing", clipboard.Overrides.Count == 0 && !reset.IsEnabled && toggle.MinHeight >= 44);
        toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        Chk("trigger-ui-edits-non-selection-parameter", cfg.BindingGraph(clipboard).Node(nodeId)!.ContinueOnFailure
            && !node.ContinueOnFailure && reset.IsEnabled && Descendants(page).Contains(toggle));
        Chk("trigger-ui-local-option-does-not-change-other-trigger", cfg.Bindings.Where(binding => binding.Id != clipboard.Id)
            .All(binding => !binding.Overrides.ContainsKey(nodeId)));
        reset.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Chk("trigger-ui-reset-keeps-field-and-restores-inheritance", clipboard.Overrides.Count == 0 && !reset.IsEnabled && Descendants(page).Contains(toggle));
        toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        Chk("trigger-ui-reset-synchronizes-toggle", cfg.BindingGraph(clipboard).Node(nodeId)!.ContinueOnFailure);
        snapshot("trigger-menu-details");

        Call("OpenSettingsFlow", clipboard.EntryId);
        expose = Descendants(flowPage).OfType<Button>().Single(button => button.Tag as string == "settings-action-exposure");
        expose.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); PumpMs(Fluent.Motion.EnterMs + 80);
        failure = Descendants(flowPage).OfType<CheckBox>().Single(check => check.Tag as string == "settings-action-expose:ContinueOnFailure");
        failure.IsChecked = false; Open(clipboard);
        Chk("trigger-ui-hiding-option-cleans-and-removes-field", clipboard.Overrides.Count == 0
            && !Descendants(page).OfType<FrameworkElement>().Any(element => element.Tag as string == tag));
        var capture = cfg.Bindings.First(binding => binding.Source == TriggerSources.Keyboard);
        Open(capture); snapshot("trigger-direct-options-light");
        Theme.Apply("dark", settings.Current.Accent); PumpMs(Fluent.Motion.ThemeMs + 80); snapshot("trigger-direct-options-dark");
        var capturePage = bodies["binding:" + capture.Id];
        Chk("trigger-ui-details-have-consistent-padding", capturePage.Children.OfType<Border>().All(card => card.Padding == new Thickness(20)));
        var modeRow = Descendants(capturePage).OfType<Grid>().Single(row => (row.Tag as string)?.EndsWith(":RecordMode") == true);
        var modeCombo = Descendants(modeRow).OfType<ComboBox>().Single();
        var comboSurface = (Border)modeCombo.Template.FindName("root", modeCombo);
        Chk("trigger-ui-values-align-without-stretching", modeCombo.ActualHeight < 40 && modeRow.ActualHeight >= 44);
        Chk("trigger-ui-combo-updates-palette-in-place", ((System.Windows.Media.SolidColorBrush)comboSurface.Background).Color == Theme.CardBg);
        TriggerTargetOptionsChecks(window, settings);
        typeof(SettingsWindow).GetMethod("NavigateSettings", flags)!.Invoke(window, new object[] { "hotkeys", false, true });
        PumpMs(50); snapshot("triggers-overview");
    }

    private static void TriggerTargetOptionsChecks(SettingsWindow window, SettingsService settings)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var cfg = settings.Current.Gestures; var graph = cfg.Graph;
        var first = graph.Add(GestureActions.Select); var second = graph.Add(GestureActions.SaveResult);
        second.ExposedParameters = new() { nameof(BehaviorNode.ContinueOnFailure) };
        var firstFlow = new BehaviorFlow { Name = "First", Steps = new() { first.Id } };
        var secondFlow = new BehaviorFlow { Name = "Second", Steps = new() { second.Id } };
        var call = graph.Add(GestureActions.CallBehavior); call.TargetId = firstFlow.Id;
        var entry = new BehaviorFlow { Name = "Call", Steps = new() { call.Id } };
        graph.Flows.AddRange(new[] { firstFlow, secondFlow, entry });
        var binding = new GestureBindingDto { EntryId = entry.Id, Source = TriggerSources.TrayMenu }; cfg.Bindings.Add(binding);
        typeof(SettingsWindow).GetMethod("OpenSettingsBinding", flags)!.Invoke(window, new object[] { binding });
        PumpMs(Fluent.Motion.QuickMs + Fluent.Motion.EnterMs + 80);
        var bodies = (Dictionary<string, StackPanel>)typeof(SettingsWindow).GetField("_settingsBodies", flags)!.GetValue(window)!;
        var page = bodies["binding:" + binding.Id];
        string callTag = "settings-binding-parameter:" + call.Id + ":TargetId";
        var callRow = Descendants(page).OfType<Grid>().Single(row => row.Tag as string == callTag);
        var target = Descendants(callRow).OfType<ComboBox>().Single();
        target.SelectedItem = target.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == secondFlow.Id);
        PumpMs(Fluent.Motion.EnterMs + 80);
        bool HasField(string id) => Descendants(page).OfType<FrameworkElement>().Any(element => (element.Tag as string)?.StartsWith("settings-binding-parameter:" + id + ":") == true);
        Chk("trigger-ui-target-switch-refreshes-reachable-options", !HasField(first.Id) && HasField(second.Id));
        Chk("trigger-ui-target-switch-retains-committing-combo", Descendants(page).Contains(target)
            && graph.Node(call.Id)!.TargetId == firstFlow.Id && cfg.BindingGraph(binding).Node(call.Id)!.TargetId == secondFlow.Id);
        Descendants(callRow).OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        PumpMs(Fluent.Motion.EnterMs + 80);
        Chk("trigger-ui-target-reset-restores-reachable-options", HasField(first.Id) && !HasField(second.Id)
            && Descendants(page).Contains(target) && ((ComboBoxItem)target.SelectedItem).Tag as string == firstFlow.Id);
        cfg.Bindings.Remove(binding); graph.Flows.RemoveAll(flow => flow == entry || flow == firstFlow || flow == secondFlow); graph.Collect();
    }
}
