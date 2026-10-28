using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>设置内容替换的交叉淡入、方向位移与高度收束。</summary>
public sealed partial class SettingsWindow
{
    static readonly ConditionalWeakTable<Panel, ContentMotion> ContentMotions = new();

    sealed class ContentMotion
    {
        /// <summary>当前内容替换的代数。</summary>
        public int Generation;
        /// <summary>退场内容的只读快照。</summary>
        public SnapshotAdorner? Outgoing;
    }

    static BitmapSource? CaptureSettingsVisual(FrameworkElement source)
    {
        var dpi = VisualTreeHelper.GetDpi(source);
        int width = (int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY);
        if (width <= 0 || height <= 0 || (long)width * height > 8_000_000) return null;
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var bounds = new Rect(source.RenderSize);
        var offset = VisualTreeHelper.GetOffset(source);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(source) { ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(offset.X, offset.Y, bounds.Width, bounds.Height),
                Stretch = Stretch.Fill, AutoLayoutContent = false }, null, bounds);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    static ContentMotion ContentMotionFor(Panel host) => ContentMotions.GetValue(host, panel =>
        {
            var motion = new ContentMotion();
            panel.Unloaded += (_, _) =>
            {
                motion.Generation++; motion.Outgoing?.Remove(); motion.Outgoing = null;
                panel.BeginAnimation(HeightProperty, null); panel.Height = double.NaN;
            };
            panel.IsVisibleChanged += (_, _) =>
            {
                if (panel.IsVisible) return;
                motion.Generation++; motion.Outgoing?.Remove(); motion.Outgoing = null;
                panel.BeginAnimation(HeightProperty, null); panel.Height = double.NaN;
            };
            return motion;
        });

    static void TransitionContent(Panel host, Action render, int direction = 1)
    {
        if (!host.IsLoaded || !host.IsVisible || host.ActualWidth <= 0) { render(); return; }
        var state = ContentMotionFor(host);
        int generation = ++state.Generation;
        state.Outgoing?.Remove(); state.Outgoing = null;
        double oldHeight = host.ActualHeight, width = host.ActualWidth;
        var previous = CaptureSettingsVisual(host);
        host.BeginAnimation(HeightProperty, null); host.Height = double.NaN;
        render();
        host.Measure(new Size(Math.Max(1, width + host.Margin.Left + host.Margin.Right), double.PositiveInfinity));
        double targetHeight = Math.Max(0, host.DesiredSize.Height - host.Margin.Top - host.Margin.Bottom);
        host.ClipToBounds = true;
        if (previous != null)
            state.Outgoing = DepartingVisual(host, previous, new Rect(0, 0, width, oldHeight), false, -direction * 10);
        var duration = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs));
        var height = new DoubleAnimation(oldHeight, targetHeight, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        height.Completed += (_, _) =>
        {
            if (state.Generation != generation) return;
            host.BeginAnimation(HeightProperty, null); host.Height = double.NaN;
            state.Outgoing?.Remove(); state.Outgoing = null;
        };
        host.BeginAnimation(HeightProperty, height);
        Fluent.CascadeIn(host.Children.OfType<FrameworkElement>().Where(child => child is not TabControl).ToArray(),
            risePx: 0, riseX: direction * 12, budgetMs: Fluent.Motion.EnterMs);
    }

    static void CancelContentTransitions(DependencyObject root)
    {
        if (root is Panel panel && ContentMotions.TryGetValue(panel, out var state))
        {
            state.Generation++; state.Outgoing?.Remove(); state.Outgoing = null;
            panel.BeginAnimation(HeightProperty, null); panel.Height = double.NaN;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            CancelContentTransitions(VisualTreeHelper.GetChild(root, i));
    }

    static SnapshotAdorner? DepartingVisual(FrameworkElement host, BitmapSource source, Rect bounds, bool shrink, double offset = 0)
    {
        var layer = AdornerLayer.GetAdornerLayer(host);
        if (layer == null) return null;
        var ghost = new SnapshotAdorner(host, source, bounds, layer);
        layer.Add(ghost); ghost.Dismiss(shrink, offset);
        return ghost;
    }

    sealed class SnapshotAdorner : Adorner
    {
        readonly Image _image;
        readonly Rect _bounds;
        readonly AdornerLayer _layer;
        readonly FrameworkElement _host;
        bool _removed;

        /// <summary>建立不接收输入的退场快照。</summary>
        /// <param name="host">快照所在的布局容器。</param>
        /// <param name="source">冻结的图像。</param>
        /// <param name="bounds">相对于容器的位置与大小。</param>
        /// <param name="layer">持有快照的装饰层。</param>
        public SnapshotAdorner(FrameworkElement host, BitmapSource source, Rect bounds, AdornerLayer layer) : base(host)
        {
            _bounds = bounds; _layer = layer; _host = host;
            IsHitTestVisible = false; IsClipEnabled = true; ClipToBounds = true;
            _image = new Image { Source = source, Width = bounds.Width, Height = bounds.Height,
                Stretch = Stretch.Fill, RenderTransformOrigin = new Point(0.5, 0.5) };
            AddVisualChild(_image);
            host.Unloaded += OnHostUnloaded;
            host.IsVisibleChanged += OnHostVisibilityChanged;
        }

        void OnHostUnloaded(object sender, RoutedEventArgs e) => Remove();
        void OnHostVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!_host.IsVisible) Remove();
        }

        /// <summary>移除快照并解除宿主订阅。</summary>
        public void Remove()
        {
            if (_removed) return;
            _removed = true;
            _host.Unloaded -= OnHostUnloaded;
            _host.IsVisibleChanged -= OnHostVisibilityChanged;
            _layer.Remove(this);
            _image.Source = null;
        }

        /// <summary>淡出快照，按需缩小或横向退出。</summary>
        /// <param name="shrink">是否缩小卡片。</param>
        /// <param name="offset">横向退场距离。</param>
        public void Dismiss(bool shrink, double offset)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.ExitMs));
            var fade = new DoubleAnimation(0, duration);
            fade.Completed += (_, _) => Remove();
            _image.BeginAnimation(OpacityProperty, fade);
            var scale = new ScaleTransform(1, 1);
            var shift = new TranslateTransform();
            _image.RenderTransform = new TransformGroup { Children = { scale, shift } };
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offset, duration));
            if (!shrink) return;
            var small = new DoubleAnimation(0.8, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, small);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, small);
        }

        /// <inheritdoc/>
        protected override int VisualChildrenCount => 1;
        /// <inheritdoc/>
        protected override Visual GetVisualChild(int index) => _image;
        /// <inheritdoc/>
        protected override Size MeasureOverride(Size constraint) { _image.Measure(_bounds.Size); return AdornedElement.RenderSize; }
        /// <inheritdoc/>
        protected override Size ArrangeOverride(Size finalSize) { _image.Arrange(_bounds); return finalSize; }
    }
}
