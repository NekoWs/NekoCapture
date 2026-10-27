using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

// 本 partial 职责：动作步骤的移动按钮、数据契约徽标与空态。
/// <summary>动作步骤的移动与数据提示。</summary>
public sealed partial class SettingsWindow
{
    sealed partial class ChainEditor
    {
        TextBlock? _emptyHint;

        void PaintEmptyHint(bool empty)
        {
            if (_emptyHint == null)
            {
                _emptyHint = new TextBlock
                {
                    Text = Loc.T(K.Hotkeys.ChainEmptyHint), Foreground = Theme.TextDimBrush,
                    FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 3, 0, 0),
                };
                Root.Children.Add(_emptyHint);
            }
            _emptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        void DecorateChip(Border chip, IReadOnlyList<string> list, int index)
        {
            var body = (StackPanel)chip.Child;
            body.Children.Insert(0, MoveButton("\uE76B", K.Hotkeys.MoveLeft, index, -1, index > 0));
            body.Children.Add(MoveButton("\uE76C", K.Hotkeys.MoveRight, index, 1, index + 1 < list.Count));
            var info = GestureActions.InfoOf(list[index]);
            var missing = GestureActions.MissingInputs(list)[index];
            string values = Loc.F(K.Hotkeys.StepValues, ValueLabel(info.Needs), ValueLabel(info.Produces));
            if (missing != RunValue.None) values = Loc.F(K.Hotkeys.MissingValues, ValueLabel(missing)) + " · " + values;
            chip.ToolTip = values;
            if (info.Needs == RunValue.None && info.Produces == RunValue.None) return;
            body.Children.Insert(2, new TextBlock
            {
                Text = missing != RunValue.None ? Loc.F(K.Hotkeys.MissingValues, ValueLabel(missing))
                    : ValueLabel(info.Produces != RunValue.None ? info.Produces : info.Needs),
                Foreground = missing != RunValue.None ? Theme.AccentBrush : Theme.TextDimBrush,
                FontSize = 10, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center,
            });
        }

        Border MoveButton(string glyph, Str label, int index, int offset, bool enabled)
        {
            var button = new Border
            {
                Width = 24, Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand,
                ToolTip = Loc.T(label), Opacity = enabled ? 1 : 0.25, IsEnabled = enabled,
                Child = new TextBlock
                {
                    Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 10,
                    Foreground = Theme.TextDimBrush, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
            button.MouseLeftButtonDown += (_, e) => e.Handled = true;
            button.MouseLeftButtonUp += (_, e) => { Move(index, offset); e.Handled = true; };
            return button;
        }

        void Move(int index, int offset)
        {
            var list = _get().ToList();
            int target = index + offset;
            if (index < 0 || index >= list.Count || target < 0 || target >= list.Count) return;
            (list[index], list[target]) = (list[target], list[index]);
            _set(list.ToArray());
            Render();
        }

        static string ValueLabel(RunValue value)
        {
            if (value == RunValue.None) return Loc.T(K.Hotkeys.ValueNone);
            return string.Join(" / ", new[]
            {
                (RunValue.Region, K.Hotkeys.ValueRegion), (RunValue.Window, K.Commands.GroupWindow),
                (RunValue.Still, K.Hotkeys.ValueStill), (RunValue.Video, K.Hotkeys.ValueVideo),
                (RunValue.Recording, K.Hotkeys.ValueRecording),
            }.Where(v => (value & v.Item1) != 0).Select(v => Loc.T(v.Item2)));
        }
    }
}
