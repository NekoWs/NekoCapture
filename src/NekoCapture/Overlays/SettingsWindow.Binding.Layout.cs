using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>快捷键详情分别呈现名称、键位、触发方式和共享行为。</summary>
public sealed partial class SettingsWindow
{
    private Border BuildBindingInputCard(GestureBindingDto binding)
    {
        var rows = new StackPanel();
        rows.Children.Add(SectionHeader(Loc.T(K.SettingsNavigation.KeysAndTrigger)));
        var nameHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 240 };
        void ShowName()
        {
            var name = new TextBlock { Text = HotkeyDisplayName(binding), Foreground = Theme.TextBrush, FontSize = 13,
                MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(name);
            line.Children.Add(RenameIconButton(() =>
            {
                nameHost.Content = RenameBox(HotkeyDisplayName(binding), value =>
                { binding.Name = value.Trim(); CommitHotkeys(); ShowName(); }, ShowName);
            }));
            nameHost.Content = line;
        }
        ShowName();
        rows.Children.Add(LabeledRow(Loc.T(K.SettingsNavigation.BindingName), nameHost, Loc.T(K.Hotkeys.RenameThisHotkey)));
        var input = new StackPanel();
        void RenderInput()
        {
            input.Children.Clear(); _hotBoxes.Remove(binding.Id);
            if (binding.Source != TriggerSources.Keyboard) return;
            var box = NewChordBox(binding); _hotBoxes[binding.Id] = box;
            input.Children.Add(LabeledRow(Loc.T(K.Hotkeys.Shortcut), box, Loc.T(K.Hotkeys.ClickThenPressAKey)));
            var trigger = HotkeyBody(binding, includeEntry: false); trigger.Margin = new Thickness(0, 3, 0, 0);
            if (trigger.Child is Panel triggerRows)
                foreach (var row in triggerRows.Children.OfType<Grid>()) AlignBindingValue(row);
            input.Children.Add(trigger);
        }
        var source = Fluent.Ui(new ComboBox { Tag = "settings-trigger-source", HorizontalAlignment = HorizontalAlignment.Right });
        foreach (string id in TriggerSources.All) source.Items.Add(new ComboBoxItem { Content = Loc.T(TriggerSources.Label(id)), Tag = id });
        source.SelectedItem = source.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == binding.Source) ?? source.Items[0];
        source.SelectionChanged += (_, _) =>
        {
            if (source.SelectedItem is not ComboBoxItem { Tag: string id } || id == binding.Source) return;
            binding.Source = id; CommitHotkeys(); TransitionContent(input, RenderInput);
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(DeleteIconButton(binding)); controls.Children.Add(EnableSwitch(binding, () => input.Opacity = binding.Enabled ? 1 : 0.55));
        controls.Children.Add(source); rows.Children.Add(LabeledRow(Loc.T(K.Triggers.Source), controls, Loc.T(K.Triggers.Source)));
        rows.Children.Add(input); RenderInput(); input.Opacity = binding.Enabled ? 1 : 0.55;
        var card = AboutCard(rows); card.Tag = "settings-binding-input"; card.Padding = new Thickness(20);
        var stroke = new SolidColorBrush(Theme.Stroke); card.BorderBrush = stroke;
        void PaintConflict()
        {
            bool conflict = _settings.Current.Gestures.ConflictIds().Contains(binding.Id);
            card.ToolTip = conflict ? Loc.T(K.Hotkeys.AnotherHotkeyAlreadyUsesThis) : null;
            stroke.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(conflict ? Theme.Danger : Theme.Stroke,
                TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
        }
        _hotClashSync.Add(PaintConflict); PaintConflict(); _hotRows[binding.Id] = card;
        return card;
    }

    private Border BuildBindingBehaviorCard(GestureBindingDto binding)
    {
        var rows = new StackPanel();
        rows.Children.Add(SectionHeader(Loc.T(K.SettingsNavigation.Execute)));
        var overrides = new StackPanel();
        void RenderOverrides() { overrides.Children.Clear(); overrides.Children.Add(BindingParameterOverrides(binding)); }
        var entry = BehaviorEntryRow(binding.EntryId, id =>
        {
            binding.EntryId = id; binding.Overrides.Clear();
            if (id.Length == 0) { binding.Actions = binding.OnFail = Array.Empty<string>(); binding.OnComplete = null; }
            CommitHotkeys(); TransitionContent(overrides, RenderOverrides);
        });
        if (entry is Grid entryRow) AlignBindingValue(entryRow);
        rows.Children.Add(entry);
        RenderOverrides(); rows.Children.Add(overrides);
        var open = SettingsEntry(Loc.T(K.Hotkeys.ChainTitle), Loc.T(K.SettingsNavigation.ExecuteHint), "\uE7A8", () =>
        {
            if (_settings.Current.Gestures.Graph.Flow(binding.EntryId) == null) NavigateSettings("behaviors", true);
            else OpenSettingsFlow(binding.EntryId);
        });
        open.Background = Brushes.Transparent; open.BorderThickness = new Thickness(0);
        open.Effect = null;
        open.Padding = new Thickness(2, 10, 2, 2); open.Margin = new Thickness(0, 5, 0, 0);
        rows.Children.Add(open);
        var card = AboutCard(rows); card.Tag = "settings-binding-behavior"; card.Padding = new Thickness(20);
        return card;
    }

    private static void AlignBindingValue(Grid row)
    {
        foreach (var element in row.Children.OfType<FrameworkElement>().Where(element => Grid.GetColumn(element) == 1))
            element.HorizontalAlignment = HorizontalAlignment.Right;
    }
}
