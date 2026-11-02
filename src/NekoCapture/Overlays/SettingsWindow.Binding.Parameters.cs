using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>按动作呈现当前触发的单独设置，页内选择要添加的设置项。</summary>
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
            var locations = BehaviorParameters.Locations(cfg.BindingGraph(binding), binding.EntryId);
            root.Children.Add(SectionHeader(Loc.T(K.Triggers.Parameters)));
            root.Children.Add(UiNote(Loc.T(K.Triggers.ParametersHint)));
            foreach (var node in Nodes().Where(node => binding.Overrides.ContainsKey(node.Id)))
            {
                var values = binding.Overrides[node.Id];
                var rows = new StackPanel();
                var reset = Fluent.IconButton("\uE711", Loc.T(K.Triggers.ResetOverride), 28, 12, Theme.TextDimBrush);
                reset.Click += (_, _) => { binding.Overrides.Remove(node.Id); CommitHotkeys(); Refresh(); };
                var heading = LabeledRow(GestureActions.LabelOf(node.Action), reset, locations.GetValueOrDefault(node.Id, ""));
                if (heading is Grid headingRow) AlignBindingValue(headingRow);
                rows.Children.Add(heading);
                if (locations.TryGetValue(node.Id, out string? location) && location != Loc.T(K.Triggers.AtStart)) rows.Children.Add(UiNote(location));
                foreach (var parameter in BehaviorParameters.For(node).Where(parameter => values.ContainsKey(parameter.Id)))
                {
                    JsonElement Read() => values.TryGetValue(parameter.Id, out var value)
                        && (parameter.Kind == BehaviorParameterKind.Behavior ? value.ValueKind == JsonValueKind.String
                            : value.ValueKind is JsonValueKind.True or JsonValueKind.False) ? value : BehaviorParameters.Read(node, parameter.Id);
                    void Write(object value) { values[parameter.Id] = JsonSerializer.SerializeToElement(value); CommitHotkeys(); }
                    if (parameter.Kind == BehaviorParameterKind.Behavior)
                        rows.Children.Add(BehaviorEntryRow(Read().GetString() ?? "", value => Write(value)));
                    else if (parameter.Kind == BehaviorParameterKind.CaptureMode)
                        rows.Children.Add(LabeledRow(Loc.T(parameter.Label), ModeCombo(Read().GetBoolean() ? BehaviorPorts.Record : BehaviorPorts.Still,
                            false, value => Write(value == BehaviorPorts.Record)), Loc.T(parameter.Hint)));
                    else rows.Children.Add(ToggleRow("\uE7A8", Loc.T(parameter.Label), Loc.T(parameter.Hint), Read().GetBoolean(), value => Write(value)));
                    if (parameter.Kind != BehaviorParameterKind.Toggle && rows.Children[rows.Children.Count - 1] is Grid parameterRow) AlignBindingValue(parameterRow);
                    ((FrameworkElement)rows.Children[rows.Children.Count - 1]).Tag = "settings-binding-parameter:" + node.Id + ":" + parameter.Id;
                }
                root.Children.Add(new Border { Child = rows, Background = Theme.RowBgBrush, CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 2, 10, 6), Margin = new Thickness(0, 4, 0, 6) });
            }
            var add = Fluent.RoundButton(Loc.T(K.Triggers.AddOverride), Theme.RowBg, Theme.Text, 12, false);
            add.Tag = "settings-binding-add-override"; add.HorizontalAlignment = HorizontalAlignment.Left;
            var chooser = new StackPanel { Tag = "settings-binding-override-chooser" };
            bool choosing = false;
            add.Click += (_, _) =>
            {
                choosing = !choosing;
                add.Content = Loc.T(choosing ? K.Common.Cancel : K.Triggers.AddOverride);
                TransitionContent(chooser, () =>
                {
                    chooser.Children.Clear();
                    if (choosing) chooser.Children.Add(BindingParameterPicker(binding, (node, parameter) =>
                    {
                        if (!binding.Overrides.TryGetValue(node.Id, out var values)) binding.Overrides[node.Id] = values = new();
                        values[parameter.Id] = BehaviorParameters.Read(node, parameter.Id);
                        CommitHotkeys(); Refresh();
                    }));
                });
            };
            add.IsEnabled = CanAdd(); root.Children.Add(add); root.Children.Add(chooser);
        }
        void Refresh() => TransitionContent(root, Render, stableChildren: 2);
        Render(); return root;
    }
}
