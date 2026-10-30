using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace FrameCapture.Core;

/// <summary>非编辑下拉框选中文字的按项目顺序滚动。</summary>
public static partial class Fluent
{
    static readonly ConditionalWeakTable<ComboBox, ComboRollState> ComboRolls = new();

    sealed class ComboRollState
    {
        /// <summary>最近选中的项目下标。</summary>
        public int Index = -1;
        /// <summary>用于丢弃已中断动画回调的代数。</summary>
        public int Generation;
        /// <summary>最近选中的项目。</summary>
        public object? Item;
        /// <summary>动画期间隐藏的原生显示内容。</summary>
        public ContentPresenter? Label;
        /// <summary>当前滚动内容的承载层。</summary>
        public Canvas? Strip;
    }

    static void ResetComboSelection(ComboBox combo)
    {
        if (!ComboRolls.TryGetValue(combo, out var state)) return;
        StopComboRoll(state);
        state.Index = combo.SelectedIndex; state.Item = combo.SelectedItem;
    }

    static void StopComboRoll(ComboRollState state)
    {
        state.Generation++;
        if (state.Label != null) state.Label.Visibility = Visibility.Visible;
        if (state.Strip != null)
        {
            state.Strip.BeginAnimation(UIElement.OpacityProperty, null);
            state.Strip.Children.Clear(); state.Strip.Visibility = Visibility.Collapsed;
            state.Strip.RenderTransform = null;
        }
        state.Label = null; state.Strip = null;
    }

    static void WireComboSelection(ComboBox combo)
    {
        var state = ComboRolls.GetValue(combo, _ => new ComboRollState { Index = combo.SelectedIndex, Item = combo.SelectedItem });
        combo.Loaded += (_, _) => ResetComboSelection(combo);
        combo.Unloaded += (_, _) => ResetComboSelection(combo);
        combo.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != combo) return;
            int old = state.Index; var oldItem = state.Item;
            state.Index = combo.SelectedIndex; state.Item = combo.SelectedItem;
            if (combo.IsEditable || !combo.IsLoaded || !combo.IsVisible || old < 0 || state.Index < 0
                || old == state.Index || ReferenceEquals(oldItem, state.Item)) { StopComboRoll(state); return; }
            RollComboSelection(combo, state, old, oldItem);
        };
    }

    static void RollComboSelection(ComboBox combo, ComboRollState state, int previous, object? oldItem)
    {
        if (combo.Template.FindName("selectionViewport", combo) is not Grid viewport || viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0
            || combo.Template.FindName("selectionLabel", combo) is not ContentPresenter label
            || combo.Template.FindName("selectionRoll", combo) is not Canvas strip) { StopComboRoll(state); return; }
        double width = viewport.ActualWidth, height = viewport.ActualHeight;
        BitmapSource? first = state.Strip != null ? ComboRollSnapshot(viewport) : ComboChoiceSnapshot(combo, oldItem, width, height);
        StopComboRoll(state);
        if (first == null) return;
        int distance = state.Index - previous, steps = Math.Min(Math.Abs(distance), 12), direction = Math.Sign(distance);
        var rows = new List<BitmapSource> { first };
        for (int i = 1; i <= steps; i++)
        {
            int index = previous + (int)Math.Round(distance * i / (double)steps);
            if (index < 0 || index >= combo.Items.Count) return;
            var bitmap = ComboChoiceSnapshot(combo, combo.Items[index], width, height);
            if (bitmap == null) return;
            rows.Add(bitmap);
        }
        state.Label = label; state.Strip = strip;
        int generation = state.Generation;
        label.Visibility = Visibility.Hidden; strip.Visibility = Visibility.Visible;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = new Image { Source = rows[i], Width = width, Height = height, Stretch = Stretch.Fill };
            Canvas.SetTop(row, direction * i * height); strip.Children.Add(row);
        }
        var shift = new TranslateTransform(); strip.RenderTransform = shift;
        var animation = new DoubleAnimation(0, -direction * steps * height, TimeSpan.FromMilliseconds(Motion.EnterMs))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) => { if (state.Generation == generation) StopComboRoll(state); };
        shift.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    static BitmapSource? ComboChoiceSnapshot(ComboBox combo, object? item, double width, double height)
    {
        var choice = item as ComboBoxItem;
        object? content = choice?.Content ?? item;
        if (content is TextBlock text) content = text.Text;
        if (content is UIElement) return null;
        var presenter = new ContentPresenter { Content = content,
            ContentTemplate = choice?.ContentTemplate ?? combo.ItemTemplate ?? combo.SelectionBoxItemTemplate,
            ContentStringFormat = choice?.ContentStringFormat ?? combo.ItemStringFormat, FlowDirection = combo.FlowDirection };
        TextElement.SetForeground(presenter, combo.Foreground); TextElement.SetFontFamily(presenter, combo.FontFamily);
        TextElement.SetFontSize(presenter, combo.FontSize); TextElement.SetFontWeight(presenter, combo.FontWeight);
        TextElement.SetFontStyle(presenter, combo.FontStyle);
        presenter.Measure(new Size(width, height));
        double textHeight = Math.Min(height, presenter.DesiredSize.Height);
        var host = new Grid { Width = width, Height = height, ClipToBounds = true };
        presenter.VerticalAlignment = VerticalAlignment.Center; presenter.Height = textHeight; host.Children.Add(presenter);
        host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
        return ComboRollSnapshot(host, VisualTreeHelper.GetDpi(combo));
    }

    static BitmapSource? ComboRollSnapshot(FrameworkElement source, DpiScale? scale = null)
    {
        source.UpdateLayout();
        var dpi = scale ?? VisualTreeHelper.GetDpi(source);
        int width = (int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY);
        if (width <= 0 || height <= 0 || (long)width * height > 1_000_000) return null;
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); var offset = VisualTreeHelper.GetOffset(source);
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(source) { ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(offset.X, offset.Y, source.ActualWidth, source.ActualHeight), Stretch = Stretch.Fill, AutoLayoutContent = false },
                null, new Rect(0, 0, source.ActualWidth, source.ActualHeight));
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
