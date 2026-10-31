using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FrameCapture.Core;

/// <summary>设置键位列表的平面行、紧凑录键框与行内操作。</summary>
public static partial class Fluent
{
    /// <summary>创建无常驻描边的键位行，名称与按键分列，悬停和键盘焦点使用轻量底色。</summary>
    /// <param name="label">名称及可选冲突提示。</param>
    /// <param name="keys">一个或多个可换行的键位。</param>
    /// <param name="actions">可选的添加与重置操作。</param>
    /// <returns>统一高度、内边距及圆角的键位行。</returns>
    public static Border UiKeyRow(FrameworkElement label, FrameworkElement keys, FrameworkElement? actions = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.VerticalAlignment = keys.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 20, 0);
        grid.Children.Add(label); grid.Children.Add(keys); Grid.SetColumn(keys, 1);
        if (actions != null)
        {
            actions.VerticalAlignment = VerticalAlignment.Center;
            actions.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(actions, 2); grid.Children.Add(actions);
        }
        var fill = new SolidColorBrush(Colors.Transparent);
        var row = new Border
        {
            Child = grid, MinHeight = 44, Padding = new Thickness(12, 4, 8, 4),
            CornerRadius = new CornerRadius(10), Background = fill, Margin = new Thickness(0, 1, 0, 1),
        };
        void Paint(bool animate)
        {
            Color target = row.IsMouseOver || row.IsKeyboardFocusWithin ? Theme.Hover : Colors.Transparent;
            if (animate) fill.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(target, TimeSpan.FromMilliseconds(Motion.HoverMs)));
            else { fill.BeginAnimation(SolidColorBrush.ColorProperty, null); fill.Color = target; }
        }
        void Refresh() => Paint(false);
        row.MouseEnter += (_, _) => Paint(true); row.MouseLeave += (_, _) => Paint(true);
        row.IsKeyboardFocusWithinChanged += (_, _) => Paint(true);
        row.Loaded += (_, _) => { Theme.Changed -= Refresh; Theme.Changed += Refresh; Refresh(); };
        row.Unloaded += (_, _) => Theme.Changed -= Refresh;
        row.SizeChanged += (_, _) => keys.MaxWidth = Math.Max(100, row.ActualWidth * 0.58 - (actions?.ActualWidth ?? 0));
        return row;
    }

    /// <summary>创建仅显示键帽、悬停及录入时显示轮廓的紧凑录键框。</summary>
    /// <param name="chord">当前键位串。</param>
    /// <returns>用于设置键位列表的录键框。</returns>
    public static ChordCaptureBox UiKeyBinding(string chord = "") => new() { Inline = true, Chord = chord };

    /// <summary>创建键位行的标准图标操作，支持键盘聚焦。</summary>
    /// <param name="glyph">已有的 Segoe Fluent Icons 字形。</param>
    /// <param name="tip">操作名称及悬停提示。</param>
    /// <param name="action">点击操作。</param>
    /// <returns>统一尺寸的行内图标按钮。</returns>
    public static Button UiKeyAction(string glyph, string tip, Action action)
    {
        var button = IconButton(glyph, tip, 28, 13, Theme.TextDimBrush);
        button.Focusable = true; button.Margin = new Thickness(1, 0, 1, 0);
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => action();
        return button;
    }
}
