// 换色遮罩：深浅档互换时从圆心推开的一层，推开的是换色前拍下的那一屏。
using System;
using System.Windows;
using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>Fluent 换色遮罩的 partial：趁画面还是旧调色板拍一屏，盖回窗口，再从圆心把它挖开。
/// 全部成员必须在 UI 线程调用——它们挂在窗口的视觉树和渲染节拍上。</summary>
public static partial class Fluent
{

    /// <summary>换色扩散的圆心，落在窗口内容根的坐标系；不写就从遮罩区的中心起。
    /// 点主题按钮的那一刻由按钮把自己的位置写进来，同一次换色里开着的每一扇窗都从这一点推开，
    /// 落完由写下它的那一方清掉。</summary>
    public static Point? RippleOrigin { get; set; }

    /// <summary>换色前拍下的那一屏，连同它垫底的不透明色。内容根自己可能是半透明的
    /// （窗口底由 Mica 那层透出来），所以退场层是「底色 + 快照」两张叠着，而不是一张快照。</summary>
    public sealed class RippleScene
    {
        /// <summary>换色前的画面。</summary>
        public ImageSource Shot { get; }

        /// <summary>照片底下垫的那层不透明色，取换色前的窗口底。</summary>
        public Color Backing { get; }

        /// <param name="shot">换色前的画面。</param>
        /// <param name="backing">垫在照片底下的不透明色。</param>
        public RippleScene(ImageSource shot, Color backing) { Shot = shot; Backing = backing; }
    }

    /// <summary>换色前那一屏。落新调色板前拍一张写进来，遮罩就把它盖在窗口上、从圆心挖开：
    /// 圆里直接是换好色的界面，圆外还是换色前的那一屏，边界走过就是换色走过。
    /// 没写时遮罩退回一层纯色——照样从圆心推开，只是圆里暂时看不到新界面。</summary>
    public static RippleScene? RippleView { get; set; }

    /// <summary>拍一扇窗此刻的画面，供换色遮罩当退场层。半透明的部分由退场层底下的那层底色补实。</summary>
    /// <param name="w">要拍的窗口。</param>
    /// <returns>窗口内容根的位图快照；量不到尺寸或渲染不成时为 null，遮罩自己退回纯色。</returns>
    public static ImageSource? Snapshot(Window w)
    {
        if (w.Content is not FrameworkElement el) return null;
        if (el.ActualWidth < 1 || el.ActualHeight < 1) return null;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(el);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(el.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(el.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerDip, dpi.PixelsPerDip, PixelFormats.Pbgra32);
            rtb.Render(el);
            rtb.Freeze();
            return rtb;
        }
        catch (Exception) { return null; }   // 窗口没上屏、显存拿不出这块面：当没拍到
    }

    /// <summary>换色遮罩：从圆心向外推开一层，铺满整窗即摘。
    /// 明暗互换时几十处颜色在同一帧里同时跳，眼睛抓不到落点；改成从点下去的那颗按钮向外铺，
    /// 边界扫过的地方就是换色的落点。有换色前那一屏时推开的是它，圆里露出的直接是新界面；
    /// 没有时推开的是一层进场侧的底色。
    /// 遮罩走 Adorner 层：不进布局，不会把 DockPanel 的填充位抢走，也不会挤动任何一行。</summary>
    /// <param name="w">盖遮罩的窗口。</param>
    /// <param name="to">遮罩颜色：换色之后的窗口底，只在没有那一屏时用得上。</param>
    public static void MaskRipple(Window w, Color to)
    {
        if (w.Content is not UIElement el) return;
        var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(el);
        if (layer == null) return;
        var ad = new MaskAdorner(el, to, RippleView, RippleOrigin ?? new Point(-1, -1));
        layer.Add(ad);
        ad.Done += () => layer.Remove(ad);
        ad.Start();
    }

    /// <summary>换色遮罩的载体：一块被挖了圆洞的面，铺满整窗报一次 Done，不吃命中。
    /// 有换色前那一屏时，洞里之外垫一层旧窗口底再压上那张照片，几何是「整窗矩形 挖掉 一个越来越大的圆」；
    /// 没有时是一枚填着进场侧底色的单位圆，靠缩放长大。
    /// 两种都由同一个逐帧推进的时钟驱动：挖洞用的几何要一帧一帧重画，动画对象接不住。</summary>
    sealed class MaskAdorner : System.Windows.Documents.Adorner
    {
        readonly FrameworkElement _face;
        readonly System.Windows.Shapes.Path? _disc;
        readonly ScaleTransform? _scale;
        readonly bool _reveal;
        readonly Point _origin;
        Point _o;
        double _reach;
        DateTime _t0;
        bool _fired;

        /// <summary>铺满整窗时触发一次，挂遮罩的一方靠它把自己摘掉。</summary>
        public event Action? Done;

        /// <param name="el">被盖住的元素（窗口的内容根）。</param>
        /// <param name="c">遮罩颜色，只在没有那一屏时用得上。</param>
        /// <param name="view">换色前那一屏；null 表示没有。</param>
        /// <param name="origin">圆心；横坐标为负表示取遮罩区中心。</param>
        public MaskAdorner(UIElement el, Color c, RippleScene? view, Point origin) : base(el)
        {
            IsHitTestVisible = false;
            _origin = origin;
            _reveal = view != null;
            if (view is { } v)
            {
                // 照片自己可能透光，底下垫一层换色前的窗口底：这一层必须实心，否则新界面从缝里全透出来
                var photo = new System.Windows.Controls.Image
                {
                    Source = v.Shot,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false,
                };
                var mat = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(v.Backing),
                    IsHitTestVisible = false,
                };
                var grid = new System.Windows.Controls.Grid { IsHitTestVisible = false };
                grid.Children.Add(mat);
                grid.Children.Add(photo);
                _face = grid;
            }
            else
            {
                _scale = new ScaleTransform(0, 0);
                _disc = new System.Windows.Shapes.Path
                {
                    Fill = new SolidColorBrush(c),
                    Data = new EllipseGeometry(new Point(0, 0), 1, 1),
                    RenderTransform = _scale,
                    IsHitTestVisible = false,
                };
                _face = _disc;
            }
            AddVisualChild(_face);
        }

        /// <summary>定好圆心与终点半径，开始逐帧推进。</summary>
        public void Start()
        {
            var s = AdornedElement.RenderSize;
            if (s.Width < 1 || s.Height < 1) { Fire(); return; }
            _o = _origin.X < 0 ? new Point(s.Width / 2, s.Height / 2) : _origin;
            // 半径取到最远的角：边界扫过最后一个角的那一刻，遮罩正好整片退场，看不到收边
            foreach (var c in new[] { new Point(0, 0), new Point(s.Width, 0), new Point(0, s.Height), new Point(s.Width, s.Height) })
                _reach = Math.Max(_reach, Math.Sqrt((c.X - _o.X) * (c.X - _o.X) + (c.Y - _o.Y) * (c.Y - _o.Y)));
            if (_reach <= 0) { Fire(); return; }
            if (_reveal) _face.Clip = Hole(0.5);
            else
            {
                // 圆心既是几何的中心也是缩放的锚点：两者重合，圆才原地长大而不是边长边漂
                _disc!.Data = new EllipseGeometry(_o, 1, 1);
                _disc.RenderTransformOrigin = new Point(_o.X / s.Width, _o.Y / s.Height);
            }
            _t0 = DateTime.UtcNow;
            CompositionTarget.Rendering += OnTick;
        }

        /// <summary>一帧：按 cubic-out 推进半径，把洞再挖大一圈。</summary>
        void OnTick(object? sender, EventArgs e)
        {
            double t = (DateTime.UtcNow - _t0).TotalMilliseconds / Motion.RippleMs;
            if (t >= 1) { Finish(); Fire(); return; }
            double p = 1 - Math.Pow(1 - t, 3);   // cubic-out：起步快、收尾缓，边界走过看得出方向又不至于拖
            double r = Math.Max(0.5, p * _reach);
            if (_reveal) _face.Clip = Hole(r);
            else { _scale!.ScaleX = r; _scale.ScaleY = r; }
        }

        /// <summary>整窗矩形挖掉一个圆：圆里露出换好色的界面，圆外还是换色前的那一屏。</summary>
        /// <param name="r">这一帧的圆半径。</param>
        /// <returns>矩形减去圆的几何（Exclude：圆那部分不在裁剪范围内）。</returns>
        Geometry Hole(double r)
        {
            var s = AdornedElement.RenderSize;
            return new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(new Point(0, 0), s)),
                new EllipseGeometry(_o, r, r));
        }

        /// <summary>停表：圆铺满整窗的那一刻把遮罩清空，摘掉时不留残影。</summary>
        void Finish()
        {
            CompositionTarget.Rendering -= OnTick;
            if (_reveal) _face.Clip = new RectangleGeometry(new Rect(0, 0, 0, 0));
            else { _scale!.ScaleX = _reach; _scale.ScaleY = _reach; }
        }

        /// <summary>报一次 Done；重复调用只报第一次，摘遮罩那一步不重跑。</summary>
        void Fire()
        {
            if (_fired) return;
            _fired = true;
            CompositionTarget.Rendering -= OnTick;
            Done?.Invoke();
        }

        /// <summary>只有一个视觉子级：那块被挖洞的面（或那枚纯色圆盘）。</summary>
        /// <returns>恒为 1。</returns>
        protected override int VisualChildrenCount => 1;

        /// <summary>取第 index 个视觉子级。</summary>
        /// <param name="index">子级下标，只有 0。</param>
        /// <returns>遮罩那层面。</returns>
        protected override Visual GetVisualChild(int index) => _face;

        /// <summary>按被装饰元素的实际尺寸量遮罩。</summary>
        /// <param name="constraint">装饰层给的建议尺寸。</param>
        /// <returns>被装饰元素的渲染尺寸。</returns>
        protected override Size MeasureOverride(Size constraint)
        {
            _face.Measure(constraint);
            return AdornedElement.RenderSize;
        }

        /// <summary>把遮罩铺满整个装饰区，圆心位置按这块面的实际宽高换算。</summary>
        /// <param name="finalSize">装饰区尺寸。</param>
        /// <returns>原样返回。</returns>
        protected override Size ArrangeOverride(Size finalSize)
        {
            _face.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }

}
