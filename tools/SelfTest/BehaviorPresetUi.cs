using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NekoCapture.Core;
using NekoCapture.Overlays;

static partial class SelfTest
{
    static void BehaviorPresetUiChecks(SettingsWindow window, SettingsService service)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var saved = service.Current.Clone();
        try
        {
            service.Mutate(settings => settings.Gestures = new GestureConfig());
            // 预设已经拆成独立页签：先真的进那一页，卡片才会建出来
            typeof(SettingsWindow).GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "presets", false, true });
            PumpMs(150);
            var cards = (Dictionary<string, Button>)typeof(SettingsWindow).GetField("_presetCards", flags)!.GetValue(window)!;
            Chk("preset-ui-has-three-schemes-and-custom", cards.Keys.Order().SequenceEqual(BehaviorPresets.All.Order()));
            // 预设已经是独立页签：卡片归「预设」页正文，不再挂在快捷键页总开关下面
            var presetBody = ((Grid)cards[BehaviorPresets.Dual].Parent).Parent as StackPanel;
            var bodies = (Dictionary<string, StackPanel>)typeof(SettingsWindow).GetField("_settingsBodies", flags)!.GetValue(window)!;
            Chk("preset-ui-on-own-page",
                presetBody != null && bodies.TryGetValue("presets", out var presetPanel)
                    && presetPanel.IsAncestorOf(presetBody),
                $"预设卡片在「预设」页正文里={presetBody != null && bodies.TryGetValue("presets", out var panel) && panel.IsAncestorOf(presetBody)}");
            if (service.Current.Gestures.Bindings.Count > 0)
                service.Mutate(settings => settings.Gestures.Bindings[0].Chord = "F9");
            // 快捷键页必须先建过：预设切换刷新的是那一页上已经存在的行与键位框
            typeof(SettingsWindow).GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "hotkeys", false, true });
            PumpMs(150);
            void Pick(string id) => cards[id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pick(BehaviorPresets.Dual); PumpMs(Fluent.Motion.EnterMs + 50);
            Chk("preset-ui-click-replaces-scheme", service.Current.Gestures.ActivePreset == BehaviorPresets.Dual
                && service.Current.Gestures.Bindings.Count(b => b.Source == TriggerSources.Keyboard) == 2 && service.Current.Gestures.Graph.Flows.Count(f => !f.Id.StartsWith("tray.")) == 1);
            // 触发详情页只链接共享行为入口，不再在行内铺「初始模式」
            var detail = (Border)typeof(SettingsWindow).GetMethod("BuildBindingBehaviorCard", flags)!
                .Invoke(window, new object[] { service.Current.Gestures.Bindings[0] })!;
            var labels = new List<DependencyObject>(); CollectTyped(detail, typeof(TextBlock), labels);
            Chk("hotkey-settings-only-link-behavior-entry",
                labels.OfType<TextBlock>().Any(t => t.Text == Loc.T(K.Behaviors.Entry))
                && labels.OfType<TextBlock>().All(t => t.Text != Loc.T(K.Behaviors.InitialMode)),
                $"详情页有共享行为入口={labels.OfType<TextBlock>().Any(t => t.Text == Loc.T(K.Behaviors.Entry))}");
            Pick(BehaviorPresets.Separate); PumpMs(Fluent.Motion.EnterMs + 50);
            // 键位录入搬到触发详情页之后，行上留下的是概览卡：切换预设刷新的是这批卡
            var rowsAfter = (Dictionary<string, FrameworkElement>)typeof(SettingsWindow).GetField("_hotRows", flags)!.GetValue(window)!;
            Chk("preset-ui-refreshes-binding-editors", rowsAfter.Count == 4 && service.Current.Gestures.ToBindings().Count == 4,
                $"概览卡 {rowsAfter.Count} 张，引擎绑定 {service.Current.Gestures.ToBindings().Count} 条");
            service.Mutate(settings => settings.Gestures.Graph.Nodes.First(node => node.Action == GestureActions.SelectOnly).ShowModeSwitch = true);
            Chk("preset-ui-graph-edit-selects-custom", service.Current.Gestures.ActivePreset == BehaviorPresets.Custom
                && System.Windows.Automation.AutomationProperties.GetItemStatus(cards[BehaviorPresets.Custom]) == Loc.T(K.Hotkeys.PresetCustom));
            Pick(BehaviorPresets.Single); Pick(BehaviorPresets.Custom); PumpMs(Fluent.Motion.SpringMs + 100);
            Chk("preset-ui-custom-restores-edited-scheme", service.Current.Gestures.Bindings.Count(b => b.Source == TriggerSources.Keyboard) == 4
                && service.Current.Gestures.Graph.Nodes.First(node => node.Action == GestureActions.SelectOnly).ShowModeSwitch);
            PresetFlickerChecks(window, cards);
            PresetWrappedLayoutChecks();
        }
        finally { service.Save(saved); }
    }

    static void PresetWrappedLayoutChecks()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        var left = new TextBlock { Text = "A paragraph that needs several lines in this narrow card.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
        var right = new TextBlock { Text = "Neighbor", FontSize = 12, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(right, 1); root.Children.Add(left); root.Children.Add(right);
        var window = new Window { Content = root, Width = 270, Height = 160, Left = -20000, Top = -20000,
            ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); PumpMs(50);
            var found = new List<DependencyObject> { left, right };
            var rows = new List<string>(); OverlapPass(rows, "preset-layout", root, found);
            Chk("layout-wrapped-card-text-stays-in-column", rows.Count == 0);
            left.TextWrapping = TextWrapping.NoWrap; root.UpdateLayout();
            rows.Clear(); OverlapPass(rows, "preset-layout", root, found);
            Chk("layout-unwrapped-overflow-is-still-detected", rows.Any(row => row.Contains("叠字")));
        }
        finally { window.Close(); }
    }
}
