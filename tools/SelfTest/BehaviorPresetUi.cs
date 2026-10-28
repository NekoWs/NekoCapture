using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameCapture.Core;
using FrameCapture.Overlays;

static partial class SelfTest
{
    static void BehaviorPresetUiChecks(SettingsWindow window, SettingsService service)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var saved = service.Current.Clone();
        try
        {
            service.Mutate(settings => settings.Gestures = new GestureConfig());
            var cards = (Dictionary<string, Button>)typeof(SettingsWindow).GetField("_presetCards", flags)!.GetValue(window)!;
            Chk("preset-ui-has-three-schemes-and-custom", cards.Keys.Order().SequenceEqual(BehaviorPresets.All.Order()));
            var presetBody = ((Grid)cards[BehaviorPresets.Dual].Parent).Parent as StackPanel;
            var hotkeyPage = presetBody?.Parent as StackPanel;
            Chk("preset-ui-below-master-toggle", hotkeyPage != null && hotkeyPage.Children.IndexOf(presetBody!) == 1);
            service.Mutate(settings => settings.Gestures.Bindings[0].Chord = "F9");
            void Pick(string id) => cards[id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pick(BehaviorPresets.Dual); PumpMs(Fluent.Motion.EnterMs + 50);
            Chk("preset-ui-click-replaces-scheme", service.Current.Gestures.ActivePreset == BehaviorPresets.Dual
                && service.Current.Gestures.Bindings.Count == 2 && service.Current.Gestures.Graph.Flows.Count == 1);
            Pick(BehaviorPresets.Separate); PumpMs(Fluent.Motion.EnterMs + 50);
            var boxes = (Dictionary<string, ChordCaptureBox>)typeof(SettingsWindow).GetField("_hotBoxes", flags)!.GetValue(window)!;
            Chk("preset-ui-refreshes-binding-editors", boxes.Count == 4 && service.Current.Gestures.ToBindings().Count == 4);
            service.Mutate(settings => settings.Gestures.Graph.Nodes.First(node => node.Action == GestureActions.SelectOnly).ShowModeSwitch = true);
            Chk("preset-ui-graph-edit-selects-custom", service.Current.Gestures.ActivePreset == BehaviorPresets.Custom
                && ReferenceEquals(cards[BehaviorPresets.Custom].BorderBrush, Theme.AccentBrush));
            Pick(BehaviorPresets.Single); Pick(BehaviorPresets.Custom); PumpMs(Fluent.Motion.SpringMs + 100);
            Chk("preset-ui-custom-restores-edited-scheme", service.Current.Gestures.Bindings.Count == 4
                && service.Current.Gestures.Graph.Nodes.First(node => node.Action == GestureActions.SelectOnly).ShowModeSwitch);
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
