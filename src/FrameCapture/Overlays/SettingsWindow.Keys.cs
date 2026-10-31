using System.Windows;
using System.Windows.Controls;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>按命令分组编辑键位，支持多绑定、冲突提示与恢复默认。</summary>
public sealed partial class SettingsWindow : Window
{
    private void BuildEditorKeys(StackPanel panel)
    {
        var refresh = new List<Action>();
        void RefreshAll() { foreach (var update in refresh) update(); }
        string ConflictNote(EditorCommand command)
        {
            var hits = new List<string>();
            foreach (var (chord, ids) in EditorCommands.Conflicts(_settings.Current))
                if (ids.Contains(command.Id))
                {
                    var names = ids.Where(id => id != command.Id).Select(id => EditorCommands.Find(id) is { } other ? Loc.T(other.Name) : id);
                    hits.Add(Loc.F(K.Settings.SharesAKeyWith,
                        string.Join(Loc.T(K.Settings.ListSeparator), names.Select(name => Loc.F(K.Settings.QuotedName, name))), chord));
                }
            return string.Join(Loc.T(K.Settings.Semicolon), hits.Distinct());
        }
        foreach (var group in EditorCommands.Groups)
        {
            var commands = EditorCommands.All.Where(command => command.Group == group).ToList();
            if (commands.Count == 0) continue;
            var stack = new StackPanel();
            stack.Children.Add(SectionHeader(Loc.T(group)));
            foreach (var command in commands)
            {
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var title = new Grid();
                title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                title.Children.Add(new TextBlock
                {
                    Text = Loc.T(command.Name), FontSize = 13, Foreground = Theme.TextBrush,
                    TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                });
                label.Children.Add(title);
                var note = new TextBlock
                {
                    FontSize = 11, Foreground = Theme.DangerBrush, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
                };
                label.Children.Add(note);
                var boxes = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var reset = Fluent.UiKeyAction("\uE72C", Loc.T(K.Settings.Reset), () =>
                {
                    _settings.Mutate(settings => settings.EditorKeys.Remove(command.Id)); RefreshAll();
                });
                var add = Fluent.UiKeyAction("\uE710", Loc.T(K.Hotkeys.AddBinding), () => RenderRow(true));
                Grid.SetColumn(reset, 1); title.Children.Add(reset);
                actions.Children.Add(add);
                stack.Children.Add(Fluent.UiKeyRow(label, boxes, actions));

                void Write(int index, string chord)
                {
                    _settings.Mutate(settings =>
                    {
                        var bindings = EditorCommands.BindingsFor(settings, command);
                        if (chord.Length == 0) { if (index < bindings.Count) bindings.RemoveAt(index); }
                        else if (index < bindings.Count) bindings[index] = chord;
                        else bindings.Add(chord);
                        EditorCommands.WriteBindings(settings, command, bindings);
                    });
                    RefreshAll();
                }
                ChordCaptureBox Box(string chord = "")
                {
                    var box = Fluent.UiKeyBinding(chord);
                    box.Margin = new Thickness(0, 2, 4, 2); return box;
                }
                void RenderRow(bool pending)
                {
                    var settings = _settings.Current;
                    var bindings = EditorCommands.BindingsFor(settings, command);
                    boxes.Children.Clear();
                    for (int i = 0; i < bindings.Count; i++)
                    {
                        int index = i;
                        var box = Box(bindings[i]);
                        box.Picked += chord => Write(index, chord);
                        boxes.Children.Add(box);
                    }
                    if (pending)
                    {
                        var fresh = Box();
                        fresh.Picked += chord => { if (chord.Length > 0) Write(bindings.Count, chord); else RefreshAll(); };
                        fresh.Cancelled += RefreshAll;
                        boxes.Children.Add(fresh); fresh.BeginCapture();
                    }
                    else if (bindings.Count == 0)
                    {
                        var empty = Box();
                        empty.EmptyHint = Loc.T(K.Common.NoKey);
                        empty.Picked += chord => { if (chord.Length > 0) Write(0, chord); };
                        boxes.Children.Add(empty);
                    }
                    bool customized = EditorCommands.IsCustomized(settings, command);
                    reset.Opacity = customized ? 1 : 0;
                    reset.IsEnabled = reset.IsHitTestVisible = customized;
                    note.Text = ConflictNote(command);
                    note.Visibility = note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                refresh.Add(() => RenderRow(false)); RenderRow(false);
            }
            Cardify(stack); panel.Children.Add(stack);
        }
        var resetAll = Fluent.Ui(new Button
        {
            Content = Loc.T(K.Settings.RestoreAllDefaultKeys), Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 4, 0, 2),
        });
        resetAll.Click += (_, _) => { _settings.Mutate(settings => settings.EditorKeys.Clear()); RefreshAll(); };
        panel.Children.Add(resetAll);
    }
}
