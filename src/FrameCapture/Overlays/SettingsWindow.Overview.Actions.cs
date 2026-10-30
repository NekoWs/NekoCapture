using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>概要列表保留改名、删除、启用与触发提示。</summary>
public sealed partial class SettingsWindow
{
    private ContentControl SettingsOverviewName(Func<string> read, Action<string> save, Str? trigger = null)
    {
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
        void ShowName()
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new TextBlock { Text = read(), FontSize = 13, Foreground = Theme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(text);
            string suffix = trigger is { } label ? Loc.T(label) : "";
            var hint = new TextBlock { Text = suffix, FontSize = 12, Foreground = Theme.TextDimBrush,
                Margin = new Thickness(suffix.Length > 0 ? 8 : 0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hint, 1); row.Children.Add(hint);
            var rename = RenameIconButton(() => host.Content = RenameBox(read(), value => { save(value.Trim()); ShowName(); }, ShowName));
            Grid.SetColumn(rename, 2); row.Children.Add(rename); host.Content = row;
        }
        ShowName(); return host;
    }

    private static StackPanel SettingsOverviewActions(Button entry)
    {
        var grid = (Grid)entry.Content;
        grid.ColumnDefinitions.Insert(2, new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(grid.Children[^1], 3);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(actions, 2); grid.Children.Add(actions); return actions;
    }

    private FrameworkElement BuildHotkeyOverviewEntry(GestureBindingDto binding)
    {
        var cfg = _settings.Current.Gestures;
        var flow = cfg.Graph.Flow(binding.EntryId);
        string summary = flow == null ? Loc.T(K.SettingsNavigation.ExecuteHint)
            : string.Join(" → ", flow.Steps.Select(id => cfg.Graph.Node(id)).Where(node => node != null).Select(node => GestureActions.LabelOf(node!.Action)));
        var entry = SettingsEntry(HotkeyDisplayName(binding), summary, "\uE765", () => OpenSettingsBinding(binding));
        var label = (StackPanel)((Grid)entry.Content).Children[1]; label.Children.RemoveAt(0);
        var name = SettingsOverviewName(() => HotkeyDisplayName(binding), value =>
        {
            binding.Name = value; CommitHotkeys();
            System.Windows.Automation.AutomationProperties.SetName(entry, HotkeyDisplayName(binding));
        }, GestureConfig.TriggerLabel(binding.Trigger));
        label.Children.Insert(0, name);
        var actions = SettingsOverviewActions(entry);
        FrameworkElement caps = null!;
        void PaintEnabled()
        {
            name.Opacity = label.Children[1].Opacity = caps.Opacity = binding.Enabled ? 1 : 0.55;
        }
        var delete = DeleteIconButton(binding); delete.Tag = "settings-hotkey-delete:" + binding.Id;
        actions.Children.Add(delete); actions.Children.Add(EnableSwitch(binding, PaintEnabled));
        void RepaintCaps()
        {
            if (caps != null) actions.Children.Remove(caps);
            caps = Keycaps.Row(binding.Chord, new KeycapTheme { Face = Theme.RowBg, Text = Theme.Text,
                Stroke = Theme.StrokeSoft, Dim = Theme.TextDim, Size = 10 });
            caps.VerticalAlignment = VerticalAlignment.Center; actions.Children.Add(caps); PaintEnabled();
        }
        RepaintCaps();
        entry.Loaded += (_, _) => { Theme.Changed -= RepaintCaps; Theme.Changed += RepaintCaps; RepaintCaps(); };
        entry.Unloaded += (_, _) => Theme.Changed -= RepaintCaps;
        var stroke = new SolidColorBrush(Theme.StrokeSoft); entry.BorderBrush = stroke;
        void PaintConflict()
        {
            bool conflict = _settings.Current.Gestures.ConflictIds().Contains(binding.Id);
            entry.ToolTip = conflict ? Loc.T(K.Hotkeys.AnotherHotkeyAlreadyUsesThis) : null;
            stroke.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(conflict ? Theme.Danger : Theme.StrokeSoft,
                TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
        }
        _hotClashSync.Add(PaintConflict); PaintConflict(); _hotRows[binding.Id] = entry;
        return entry;
    }

    private Button BuildBehaviorOverviewEntry(BehaviorFlow flow)
    {
        var cfg = _settings.Current.Gestures;
        var entry = SettingsEntry(cfg.FlowName(flow), Loc.T(K.SettingsNavigation.ExecuteHint), "\uE7A8", () => OpenSettingsFlow(flow.Id));
        var label = (StackPanel)((Grid)entry.Content).Children[1]; label.Children.RemoveAt(0);
        label.Children.Insert(0, SettingsOverviewName(() => cfg.FlowName(flow), value =>
        {
            flow.Name = value; CommitBehaviorGraph(); RebuildHotkeys();
            System.Windows.Automation.AutomationProperties.SetName(entry, cfg.FlowName(flow));
        }));
        var actions = SettingsOverviewActions(entry);
        var delete = RenameIconButton(() =>
        {
            cfg.Graph.Flows.Remove(flow); _behaviorPath.Clear(); _behaviorFlow = "";
            CommitBehaviorGraph(); RebuildHotkeys(); RenderBehaviors();
            if (_settingsBodies.TryGetValue("behaviors", out var list))
                TransitionContent(list, () => { list.Children.Clear(); BuildSharedBehaviorList(list); });
        });
        ((TextBlock)delete.Child).Text = "\uE74D"; delete.ToolTip = Loc.T(K.Behaviors.Delete);
        delete.Tag = "settings-behavior-delete:" + flow.Id; actions.Children.Add(delete);
        return entry;
    }
}
