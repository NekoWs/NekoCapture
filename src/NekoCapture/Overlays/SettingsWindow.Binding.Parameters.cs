using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>触发入口按动作覆盖参数，编辑与共享行为相互独立。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement BindingParameterOverrides(GestureBindingDto binding)
    {
        var root = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var cfg = _settings.Current.Gestures;
        BehaviorNode[] Nodes() => BehaviorParameters.Reachable(cfg.BindingGraph(binding), binding.EntryId).ToArray();
        if (Nodes().Length == 0) return root;
        bool CanAdd() => Nodes().Any(node => BehaviorParameters.For(node).Any(parameter =>
            !binding.Overrides.TryGetValue(node.Id, out var values) || !values.ContainsKey(parameter.Id)));
        void Render()
        {
            root.Children.Clear();
            var nodes = Nodes();
            var paths = BehaviorParameters.Paths(cfg.BindingGraph(binding), binding.EntryId);
            Button? add = null;
            root.Children.Add(SectionHeader(Loc.T(K.Triggers.Parameters)));
            foreach (var node in nodes.Where(node => binding.Overrides.ContainsKey(node.Id)))
            {
                var values = binding.Overrides[node.Id];
                var rows = new StackPanel();
                var reset = Fluent.IconButton("\uE711", Loc.T(K.Triggers.ResetOverride), 28, 12, Theme.TextDimBrush);
                reset.Click += (_, _) => { binding.Overrides.Remove(node.Id); CommitHotkeys(); Refresh(); };
                var heading = LabeledRow(paths.GetValueOrDefault(node.Id, GestureActions.LabelOf(node.Action)), reset, Loc.T(K.Triggers.ParametersHint));
                if (heading is Grid headingRow) AlignBindingValue(headingRow);
                rows.Children.Add(heading);
                foreach (var parameter in BehaviorParameters.For(node).Where(parameter => values.ContainsKey(parameter.Id)))
                {
                    JsonElement Read() => values.TryGetValue(parameter.Id, out var value)
                        && (parameter.Kind == BehaviorParameterKind.Behavior ? value.ValueKind == JsonValueKind.String
                            : value.ValueKind is JsonValueKind.True or JsonValueKind.False) ? value : BehaviorParameters.Read(node, parameter.Id);
                    void Write(object value)
                    {
                        values[parameter.Id] = JsonSerializer.SerializeToElement(value); CommitHotkeys();
                        if (add != null) add.IsEnabled = CanAdd();
                    }
                    if (parameter.Kind == BehaviorParameterKind.Behavior)
                        rows.Children.Add(BehaviorEntryRow(Read().GetString() ?? "", value => Write(value)));
                    else if (parameter.Kind == BehaviorParameterKind.CaptureMode)
                        rows.Children.Add(LabeledRow(Loc.T(parameter.Label), ModeCombo(Read().GetBoolean() ? BehaviorPorts.Record : BehaviorPorts.Still,
                            false, value => Write(value == BehaviorPorts.Record)), Loc.T(K.Triggers.ParametersHint)));
                    else rows.Children.Add(ToggleRow("\uE7A8", Loc.T(parameter.Label), Loc.T(K.Triggers.ParametersHint), Read().GetBoolean(), value => Write(value)));
                    if (parameter.Kind != BehaviorParameterKind.Toggle && rows.Children[rows.Children.Count - 1] is Grid parameterRow) AlignBindingValue(parameterRow);
                    ((FrameworkElement)rows.Children[rows.Children.Count - 1]).Tag = "settings-binding-parameter:" + node.Id + ":" + parameter.Id;
                }
                var card = new Border { Child = rows, Background = Theme.RowBgBrush, CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 2, 10, 6), Margin = new Thickness(0, 4, 0, 6) };
                root.Children.Add(card);
            }
            add = Fluent.RoundButton(Loc.T(K.Triggers.AddOverride), Theme.RowBg, Theme.Text, 12, false);
            add.Tag = "settings-binding-add-override"; add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                var menu = Fluent.Ui(new ContextMenu { Resources = Fluent.MenuResources(), PlacementTarget = add });
                var labels = BehaviorParameters.Paths(cfg.BindingGraph(binding), binding.EntryId);
                foreach (var node in Nodes())
                {
                    var item = new MenuItem { Header = new TextBlock { Text = labels.GetValueOrDefault(node.Id, GestureActions.LabelOf(node.Action)),
                        MaxWidth = 380, TextWrapping = TextWrapping.Wrap, Foreground = Theme.TextBrush } };
                    foreach (var parameter in BehaviorParameters.For(node))
                    {
                        if (binding.Overrides.TryGetValue(node.Id, out var existing) && existing.ContainsKey(parameter.Id)) continue;
                        var option = new MenuItem { Header = Loc.T(parameter.Label), Tag = parameter.Id };
                        option.Click += (_, _) =>
                        {
                            if (!binding.Overrides.TryGetValue(node.Id, out var values)) binding.Overrides[node.Id] = values = new();
                            values[parameter.Id] = BehaviorParameters.Read(node, parameter.Id);
                            menu.IsOpen = false; CommitHotkeys(); Refresh();
                        };
                        item.Items.Add(option);
                    }
                    if (item.Items.Count > 0) menu.Items.Add(item);
                }
                add.ContextMenu = menu;
                if (menu.Items.Count > 0) menu.IsOpen = true;
            };
            add.IsEnabled = CanAdd(); root.Children.Add(add);
        }
        void Refresh() => TransitionContent(root, Render);
        Render(); return root;
    }
}
