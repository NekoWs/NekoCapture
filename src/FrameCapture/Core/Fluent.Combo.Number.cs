using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FrameCapture.Core;

/// <summary>可输入下拉框选择数值时逐位滚动，直接输入保持静态。</summary>
public static partial class Fluent
{
    private sealed class NumberRoll
    {
        /// <summary>当前动画代数。</summary>
        internal int Generation;
        /// <summary>动画期间隐藏文字的输入框。</summary>
        internal TextBox? Text;
        /// <summary>逐位动画的覆盖层。</summary>
        internal Canvas? Layer;
    }

    private static readonly ConditionalWeakTable<ComboBox, NumberRoll> NumberRolls = new();

    private static void StopComboNumber(ComboBox combo)
    {
        if (!NumberRolls.TryGetValue(combo, out var state)) return;
        state.Generation++;
        if (state.Text != null) state.Text.Opacity = 1;
        if (state.Layer != null) { state.Layer.Children.Clear(); state.Layer.Visibility = Visibility.Collapsed; }
    }

    /// <summary>用每一位数字各自的变化方向显示选择结果。</summary>
    /// <param name="combo">已提交数值的可输入下拉框。</param>
    /// <param name="previous">选择前的数值文本。</param>
    /// <param name="current">已选中的数值文本。</param>
    internal static void RollComboNumber(ComboBox combo, string previous, string current)
    {
        StopComboNumber(combo);
        if (previous == current || !combo.IsLoaded || !combo.IsVisible) return;
        if (combo.Template.FindName("PART_EditableTextBox", combo) is not TextBox text
            || combo.Template.FindName("numberRoll", combo) is not Canvas layer || text.ActualHeight <= 0) return;
        var state = NumberRolls.GetValue(combo, _ => new NumberRoll());
        state.Text = text; state.Layer = layer;
        int generation = state.Generation;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = text.ActualHeight };
        Canvas.SetLeft(row, text.Padding.Left); layer.Children.Add(row);
        int count = Math.Max(previous.Length, current.Length);
        previous = previous.PadLeft(count); current = current.PadLeft(count);
        for (int i = 0; i < count; i++)
        {
            TextBlock Label(char value) => new()
            {
                Text = value.ToString(), FontFamily = text.FontFamily, FontSize = text.FontSize,
                FontWeight = text.FontWeight, Foreground = text.Foreground, VerticalAlignment = VerticalAlignment.Center,
            };
            var oldLabel = Label(previous[i]); var newLabel = Label(current[i]);
            oldLabel.Measure(new Size(double.PositiveInfinity, text.ActualHeight));
            newLabel.Measure(new Size(double.PositiveInfinity, text.ActualHeight));
            var cell = new Grid { Width = Math.Max(oldLabel.DesiredSize.Width, newLabel.DesiredSize.Width),
                Height = text.ActualHeight, ClipToBounds = true };
            row.Children.Add(cell); cell.Children.Add(newLabel);
            int direction = current[i].CompareTo(previous[i]) < 0 ? -1 : 1;
            if (previous[i] != current[i])
            {
                cell.Children.Add(oldLabel);
                var outgoing = new TranslateTransform(); var incoming = new TranslateTransform(0, -direction * text.ActualHeight);
                oldLabel.RenderTransform = outgoing; newLabel.RenderTransform = incoming;
                var duration = TimeSpan.FromMilliseconds(Motion.EnterMs);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                outgoing.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, direction * text.ActualHeight, duration) { EasingFunction = ease });
                incoming.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-direction * text.ActualHeight, 0, duration) { EasingFunction = ease });
            }
        }
        text.Opacity = 0; layer.Visibility = Visibility.Visible;
        var clock = new DoubleAnimation(1, 1, TimeSpan.FromMilliseconds(Motion.EnterMs));
        clock.Completed += (_, _) => { if (state.Generation == generation) StopComboNumber(combo); };
        layer.BeginAnimation(UIElement.OpacityProperty, clock);
    }
}
