using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NekoCapture.Core;

/// <summary>数值候选采用连续数字带滚动，列位置与输入框宽度同步收束。</summary>
public static partial class Fluent
{
    private sealed class NumberRoll
    {
        /// <summary>当前动画代数。</summary>
        internal int Generation;
        /// <summary>动画期间隐藏文字的输入框。</summary>
        internal TextBox? Text;
        /// <summary>滚轮覆盖层。</summary>
        internal Canvas? Layer;
        /// <summary>选择过程已接管宽度同步。</summary>
        internal bool Prepared;
        /// <summary>滚轮正在播放。</summary>
        internal bool Active;
        /// <summary>选择前的实际宽度。</summary>
        internal double StartWidth;
        /// <summary>数值落定后的宽度。</summary>
        internal double TargetWidth;
    }

    private static readonly ConditionalWeakTable<ComboBox, NumberRoll> NumberRolls = new();
    private static readonly Brush NumberWheelMask = CreateNumberWheelMask();

    /// <summary>在原生选择同步前保留可见画面与宽度，候选提交期间不显示新文本。</summary>
    /// <param name="combo">正在选择候选数值的输入框。</param>
    internal static void PrepareComboNumber(ComboBox combo)
    {
        var state = NumberRolls.GetValue(combo, _ => new NumberRoll());
        if (state.Prepared) return;
        state.StartWidth = combo.ActualWidth;
        state.TargetWidth = combo.ActualWidth;
        state.Prepared = true;
        if (!combo.IsLoaded || !combo.IsVisible
            || combo.Template.FindName("PART_EditableTextBox", combo) is not TextBox text
            || combo.Template.FindName("numberRoll", combo) is not Canvas layer
            || text.Parent is not FrameworkElement viewport
            || ComboRollSnapshot(viewport) is not { } snapshot) return;
        state.Generation++;
        layer.BeginAnimation(UIElement.OpacityProperty, null);
        layer.Children.Clear();
        layer.Children.Add(new Image { Source = snapshot, Width = viewport.ActualWidth, Height = viewport.ActualHeight });
        state.Text = text; state.Layer = layer; state.Active = false;
        text.Opacity = 0; layer.Visibility = Visibility.Visible;
        combo.BeginAnimation(FrameworkElement.WidthProperty, null);
        combo.SetCurrentValue(FrameworkElement.WidthProperty, state.StartWidth);
    }

    private static bool HoldComboNumberWidth(ComboBox combo, double target)
    {
        if (!NumberRolls.TryGetValue(combo, out var state) || (!state.Prepared && !state.Active)) return false;
        state.TargetWidth = target;
        return true;
    }

    private static void StopComboNumber(ComboBox combo, bool restoreText = true)
    {
        if (!NumberRolls.TryGetValue(combo, out var state)) return;
        state.Generation++;
        if (restoreText && state.Text != null) state.Text.Opacity = 1;
        if (state.Layer != null)
        {
            state.Layer.BeginAnimation(UIElement.OpacityProperty, null);
            state.Layer.Children.Clear(); state.Layer.Visibility = Visibility.Collapsed;
        }
        if (state.Prepared || state.Active)
        {
            combo.BeginAnimation(FrameworkElement.WidthProperty, null);
            if (state.TargetWidth > 0) combo.SetCurrentValue(FrameworkElement.WidthProperty, state.TargetWidth);
        }
        state.Prepared = state.Active = false;
        state.Text = null; state.Layer = null;
    }

    /// <summary>按数值增减方向转动各位数字，连续经过中间数字后停稳。</summary>
    /// <param name="combo">已提交数值的可输入下拉框。</param>
    /// <param name="previous">选择前的数值文本。</param>
    /// <param name="current">已选中的数值文本。</param>
    internal static void RollComboNumber(ComboBox combo, string previous, string current)
    {
        if (previous == current || !combo.IsLoaded || !combo.IsVisible
            || !decimal.TryParse(previous, NumberStyles.Number, CultureInfo.InvariantCulture, out var oldValue)
            || !decimal.TryParse(current, NumberStyles.Number, CultureInfo.InvariantCulture, out var newValue)
            || oldValue < 0 || newValue < 0)
        { StopComboNumber(combo); return; }
        if (combo.Template.FindName("PART_EditableTextBox", combo) is not TextBox text
            || combo.Template.FindName("numberRoll", combo) is not Canvas layer || text.ActualHeight <= 0)
        { StopComboNumber(combo); return; }
        PrepareComboNumber(combo);
        FitComboWidth(combo);
        var state = NumberRolls.GetValue(combo, _ => new NumberRoll());
        double startWidth = state.StartWidth, targetWidth = state.TargetWidth;
        StopComboNumber(combo, restoreText: false);
        text.ScrollToHorizontalOffset(0); combo.UpdateLayout(); text.UpdateLayout();
        var oldLayout = LayoutNumber(text, previous, native: false);
        var newLayout = LayoutNumber(text, current, native: true);
        int first = Math.Max(oldLayout.LastInteger, newLayout.LastInteger);
        int last = Math.Min(oldLayout.LastFraction, newLayout.LastFraction);
        int places = first - last + 1;
        int totalMs = Motion.NumberRollMs + Math.Max(0, places - 1) * Motion.NumberStaggerMs;
        var duration = TimeSpan.FromMilliseconds(totalMs);
        state.Text = text; state.Layer = layer; state.Active = true;
        state.StartWidth = startWidth; state.TargetWidth = targetWidth;
        int generation = state.Generation;
        int direction = newValue >= oldValue ? 1 : -1;
        for (int place = first; place >= last; place--)
        {
            var oldGlyph = oldLayout.At(place); var newGlyph = newLayout.At(place);
            bool had = oldLayout.Digits.ContainsKey(place), has = newLayout.Digits.ContainsKey(place);
            var cell = NumberCell(text, layer, oldGlyph, newGlyph, had, has, duration);
            cell.Tag = "number-reel:" + place;
            int steps = NumberWheelSteps(oldValue, newValue, oldGlyph.Value - '0', newGlyph.Value - '0', place, direction);
            var tape = new Canvas(); cell.Children.Add(tape);
            double step = Math.Ceiling(NumberLabel(text, "0").DesiredSize.Height + 2);
            Canvas.SetTop(tape, (cell.Height - step) / 2);
            int start = steps == 0 ? 0 : -1, end = steps == 0 ? 0 : steps + 1;
            for (int row = start; row <= end; row++)
            {
                int digit = ((oldGlyph.Value - '0' + direction * row) % 10 + 10) % 10;
                var label = NumberLabel(text, digit.ToString(CultureInfo.InvariantCulture));
                label.HorizontalAlignment = HorizontalAlignment.Left;
                var slot = new Border { Width = cell.Width, Height = step, Child = label };
                label.VerticalAlignment = VerticalAlignment.Center;
                Canvas.SetTop(slot, direction * row * step); tape.Children.Add(slot);
            }
            if (steps == 0) continue;
            var shift = new TranslateTransform(); tape.RenderTransform = shift;
            var roll = new DoubleAnimation(0, -direction * steps * step, TimeSpan.FromMilliseconds(Motion.NumberRollMs))
            {
                BeginTime = TimeSpan.FromMilliseconds((first - place) * Motion.NumberStaggerMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            shift.BeginAnimation(TranslateTransform.YProperty, roll);
        }
        if (oldLayout.HasDecimal || newLayout.HasDecimal)
        {
            var cell = NumberCell(text, layer, oldLayout.Dot, newLayout.Dot, oldLayout.HasDecimal, newLayout.HasDecimal, duration);
            var label = NumberLabel(text, "."); label.VerticalAlignment = VerticalAlignment.Center; cell.Children.Add(label);
        }
        text.Opacity = 0; layer.Visibility = Visibility.Visible;
        combo.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(startWidth, targetWidth, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        var finish = new DoubleAnimation(1, 1, duration);
        finish.Completed += (_, _) => { if (state.Generation == generation) StopComboNumber(combo); };
        layer.BeginAnimation(UIElement.OpacityProperty, finish);
    }

    private static Grid NumberCell(TextBox text, Canvas layer, NumberGlyph before, NumberGlyph after,
        bool existed, bool exists, TimeSpan duration)
    {
        double height = Math.Min(text.ActualHeight, Math.Ceiling(text.FontSize * 1.5));
        var cell = new Grid { Width = Math.Max(before.Width, after.Width), Height = height,
            ClipToBounds = true, OpacityMask = NumberWheelMask, Opacity = existed ? 1 : 0 };
        Canvas.SetTop(cell, (text.ActualHeight - height) / 2);
        Canvas.SetLeft(cell, before.X); layer.Children.Add(cell);
        cell.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(before.X, after.X, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        if (existed != exists) cell.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(existed ? 1 : 0, exists ? 1 : 0, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        return cell;
    }

    private static int NumberWheelSteps(decimal previous, decimal current, int before, int after, int place, int direction)
    {
        decimal scale = 1;
        for (int i = 0; i < Math.Abs(place); i++) scale = place >= 0 ? scale * 10 : scale / 10;
        decimal distance = Math.Abs(decimal.Floor(current / scale) - decimal.Floor(previous / scale));
        int steps = direction > 0 ? (after - before + 10) % 10 : (before - after + 10) % 10;
        return distance >= 10 ? steps + 10 : steps;
    }

    private static Brush CreateNumberWheelMask()
    {
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.16));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.84));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        mask.Freeze(); return mask;
    }
}
