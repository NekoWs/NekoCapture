using System.Windows;
using System.Windows.Controls;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>通过两个平面下拉框选择动作与设置项，确认后显示可编辑设置。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement BindingParameterPicker(GestureBindingDto binding, Action<BehaviorNode, BehaviorParameter> apply)
    {
        var graph = _settings.Current.Gestures.BindingGraph(binding);
        var locations = BehaviorParameters.Locations(graph, binding.EntryId);
        bool Available(BehaviorNode node, BehaviorParameter parameter) => !binding.Overrides.TryGetValue(node.Id, out var values) || !values.ContainsKey(parameter.Id);
        var nodes = BehaviorParameters.Reachable(graph, binding.EntryId).Where(node => BehaviorParameters.For(node).Any(parameter => Available(node, parameter))).ToArray();
        var rows = new StackPanel();
        var action = Fluent.Ui(new ComboBox { Tag = "settings-binding-action-picker", HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 320 });
        foreach (var node in nodes) action.Items.Add(new ComboBoxItem { Tag = node.Id,
            Content = Loc.F(K.Common.LabelWithContext, GestureActions.LabelOf(node.Action), locations.GetValueOrDefault(node.Id, "")) });
        var setting = Fluent.Ui(new ComboBox { Tag = "settings-binding-setting-picker", HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 280 });
        var description = new TextBlock { Foreground = Theme.TextDimBrush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 6, 2, 10) };
        var confirm = Fluent.RoundButton(Loc.T(K.Triggers.ConfirmOverride), Theme.Accent, Fluent.OnAccentColor(Theme.Accent), 12, false);
        confirm.Tag = "settings-binding-confirm-override"; confirm.HorizontalAlignment = HorizontalAlignment.Right;
        var reveal = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs) };
        reveal.Tick += (_, _) => { reveal.Stop(); if (confirm.IsVisible) confirm.BringIntoView(); };
        confirm.Loaded += (_, _) => reveal.Start();
        confirm.Unloaded += (_, _) => reveal.Stop();
        BehaviorNode? SelectedNode() => action.SelectedItem is ComboBoxItem { Tag: string id } ? graph.Node(id) : null;
        BehaviorParameter? SelectedParameter() => setting.SelectedItem is ComboBoxItem { Tag: string id } && SelectedNode() is { } node
            ? BehaviorParameters.For(node).FirstOrDefault(parameter => parameter.Id == id) : null;
        void Describe()
        {
            var parameter = SelectedParameter();
            description.Text = parameter == null ? "" : Loc.T(parameter.Hint);
            confirm.IsEnabled = parameter != null;
        }
        void Settings()
        {
            setting.Items.Clear();
            if (SelectedNode() is { } node)
                foreach (var parameter in BehaviorParameters.For(node).Where(parameter => Available(node, parameter)))
                    setting.Items.Add(new ComboBoxItem { Content = Loc.T(parameter.Label), Tag = parameter.Id });
            if (setting.Items.Count > 0) setting.SelectedIndex = 0;
            Describe();
        }
        action.SelectionChanged += (_, _) => Settings();
        setting.SelectionChanged += (_, _) => Describe();
        confirm.Click += (_, _) => { if (SelectedNode() is { } node && SelectedParameter() is { } parameter) apply(node, parameter); };
        if (action.Items.Count > 0) action.SelectedIndex = 0;
        var actionRow = LabeledRow(Loc.T(K.Triggers.ChooseAction), action, "");
        var settingRow = LabeledRow(Loc.T(K.Triggers.ChooseSetting), setting, "");
        if (actionRow is Grid actionGrid) AlignBindingValue(actionGrid);
        if (settingRow is Grid settingGrid) AlignBindingValue(settingGrid);
        rows.Children.Add(actionRow); rows.Children.Add(settingRow); rows.Children.Add(description); rows.Children.Add(confirm);
        return new Border { Child = rows, Background = Theme.RowBgBrush, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 4) };
    }
}
