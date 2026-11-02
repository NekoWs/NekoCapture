using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>触发开放项的直接编辑与逐项继承，提交时保留控件实例。</summary>
public sealed partial class SettingsWindow
{
    private (FrameworkElement Row, Action Sync) BindingParameterRow(GestureBindingDto binding, BehaviorNode node,
        BehaviorParameter parameter, Action structureChanged)
    {
        bool syncing = false;
        JsonElement Read()
        {
            if (binding.Overrides.TryGetValue(node.Id, out var values) && values.TryGetValue(parameter.Id, out var value)
                && (parameter.Kind == BehaviorParameterKind.Behavior ? value.ValueKind == JsonValueKind.String
                    : value.ValueKind is JsonValueKind.True or JsonValueKind.False)) return value;
            return BehaviorParameters.Read(node, parameter.Id);
        }
        Action sync = () => { };
        void Write(object value)
        {
            if (syncing) return;
            if (!binding.Overrides.TryGetValue(node.Id, out var values)) binding.Overrides[node.Id] = values = new();
            values[parameter.Id] = JsonSerializer.SerializeToElement(value);
            CommitHotkeys(); sync();
            if (parameter.Kind == BehaviorParameterKind.Behavior) structureChanged();
        }
        FrameworkElement field;
        Action syncValue;
        if (parameter.Kind == BehaviorParameterKind.Toggle)
        {
            field = ToggleRow("", Loc.T(parameter.Label), Loc.T(parameter.Hint), Read().GetBoolean(), value => Write(value), out var setValue);
            var toggle = (Grid)field;
            toggle.Children.RemoveAt(0); toggle.ColumnDefinitions[0].Width = new GridLength(0);
            syncValue = () => setValue(Read().GetBoolean());
        }
        else
        {
            ComboBox combo;
            if (parameter.Kind == BehaviorParameterKind.CaptureMode)
                combo = ModeCombo(Read().GetBoolean() ? BehaviorPorts.Record : BehaviorPorts.Still, false,
                    value => Write(value == BehaviorPorts.Record));
            else
            {
                var entry = (Grid)BehaviorEntryRow(Read().GetString() ?? "", value => Write(value));
                combo = entry.Children.OfType<ComboBox>().Single(); entry.Children.Remove(combo);
            }
            combo.VerticalAlignment = VerticalAlignment.Center;
            field = LabeledRow(Loc.T(parameter.Label), combo, Loc.T(parameter.Hint));
            AlignBindingValue((Grid)field);
            syncValue = () =>
            {
                string selected = parameter.Kind == BehaviorParameterKind.CaptureMode
                    ? Read().GetBoolean() ? BehaviorPorts.Record : BehaviorPorts.Still : Read().GetString() ?? "";
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == selected) ?? combo.Items[0];
            };
        }
        field.Margin = new Thickness(0); field.MinHeight = 48;
        var reset = Fluent.IconButton("\uE72C", Loc.T(K.Behaviors.NodeDefault), 28, 12, Theme.TextDimBrush);
        reset.Margin = new Thickness(12, 0, 0, 0);
        reset.Tag = "settings-binding-reset:" + node.Id + ":" + parameter.Id;
        var row = new Grid { Tag = "settings-binding-parameter:" + node.Id + ":" + parameter.Id };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(reset, 1); row.Children.Add(field); row.Children.Add(reset);
        sync = () =>
        {
            syncing = true;
            try { syncValue(); } finally { syncing = false; }
            reset.IsEnabled = binding.Overrides.TryGetValue(node.Id, out var values) && values.ContainsKey(parameter.Id);
            reset.Opacity = reset.IsEnabled ? 1 : 0.3;
        };
        reset.Click += (_, _) =>
        {
            if (binding.Overrides.TryGetValue(node.Id, out var values))
            { values.Remove(parameter.Id); if (values.Count == 0) binding.Overrides.Remove(node.Id); }
            CommitHotkeys(); sync();
            if (parameter.Kind == BehaviorParameterKind.Behavior) structureChanged();
        };
        sync(); return (row, sync);
    }
}
