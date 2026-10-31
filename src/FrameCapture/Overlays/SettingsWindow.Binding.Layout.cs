using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

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
        var box = NewChordBox(binding); _hotBoxes[binding.Id] = box;
        box.Margin = new Thickness(6, 0, 0, 0);
        void PaintEnabled() => box.Opacity = binding.Enabled ? 1 : 0.55;
        PaintEnabled();
        var keys = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        keys.Children.Add(DeleteIconButton(binding)); keys.Children.Add(EnableSwitch(binding, PaintEnabled)); keys.Children.Add(box);
        rows.Children.Add(LabeledRow(Loc.T(K.Hotkeys.Shortcut), keys, Loc.T(K.Hotkeys.ClickThenPressAKey)));
        var trigger = HotkeyBody(binding, includeEntry: false); trigger.Margin = new Thickness(0, 3, 0, 0);
        if (trigger.Child is Panel triggerRows)
            foreach (var row in triggerRows.Children.OfType<Grid>()) AlignBindingValue(row);
        rows.Children.Add(trigger);
        var card = AboutCard(rows); card.Tag = "settings-binding-input";
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
        var entry = BehaviorEntryRow(binding.EntryId, id =>
        {
            binding.EntryId = id;
            if (id.Length == 0) { binding.Actions = binding.OnFail = Array.Empty<string>(); binding.OnComplete = null; }
            CommitHotkeys();
        });
        if (entry is Grid entryRow) AlignBindingValue(entryRow);
        rows.Children.Add(entry);
        var mode = LabeledRow(Loc.T(K.Behaviors.EntryMode), ModeCombo(binding.EntryMode, true, value =>
        { binding.EntryMode = value; CommitHotkeys(); }), Loc.T(K.Behaviors.EntryModeHint));
        mode.Tag = "settings-binding-entry-mode";
        if (mode is Grid modeRow) AlignBindingValue(modeRow);
        rows.Children.Add(mode);
        var open = SettingsEntry(Loc.T(K.Hotkeys.ChainTitle), Loc.T(K.SettingsNavigation.ExecuteHint), "\uE7A8", () =>
        {
            if (_settings.Current.Gestures.Graph.Flow(binding.EntryId) == null) NavigateSettings("behaviors", true);
            else OpenSettingsFlow(binding.EntryId);
        });
        open.Background = Brushes.Transparent; open.BorderThickness = new Thickness(0);
        open.Effect = null;
        open.Padding = new Thickness(2, 10, 2, 2); open.Margin = new Thickness(0, 5, 0, 0);
        rows.Children.Add(open);
        var card = AboutCard(rows); card.Tag = "settings-binding-behavior";
        return card;
    }

    private static void AlignBindingValue(Grid row)
    {
        foreach (var element in row.Children.OfType<FrameworkElement>().Where(element => Grid.GetColumn(element) == 1))
            element.HorizontalAlignment = HorizontalAlignment.Right;
    }
}
