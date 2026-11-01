// 换色幕布：深浅档互换时整窗盖一层换色前的窗口底，淡出后自摘。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NekoCapture.Core;

/// <summary>Fluent 换色幕布的 partial：盖一层退场侧的窗口底，整片淡出到已经就位的新调色板。
/// 全部成员必须在 UI 线程调用——它们挂在窗口的视觉树上并启动动画。</summary>
public static partial class Fluent
{
    /// <summary>换色幕布：整窗盖一层换色那一刻的窗口底，按 <see cref="Motion.ThemeMs"/> 淡出后自摘。
    /// 深浅互换时几十处颜色在同一帧里同时跳，眼睛抓不到落点；盖一层换色前的窗口底再整片淡开，
    /// 新调色板在幕布后面就位，淡出期间看到的是同一块面在换色，而不是零散地这里黑一块那里白一块。
    /// 幕布走 Adorner 层：不进布局，不会把 DockPanel 的填充位抢走，也不会挤动任何一行。</summary>
    /// <param name="w">盖幕布的窗口。</param>
    /// <param name="from">幕布颜色：换色那一刻画面上的窗口底。</param>
    public static void MaskFade(Window w, Color from)
    {
        if (w.Content is not UIElement el) return;
        var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(el);
        if (layer == null) return;
        var ad = new MaskAdorner(el, from);
        layer.Add(ad);
        var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(Motion.ThemeMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        fade.Completed += (_, _) => layer.Remove(ad);
        ad.Surface.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>换色幕布的载体：铺满被装饰元素的一层纯色，不吃命中。
    /// 窗口表面把客户端区域外的绘制裁掉，幕布铺到窗口边界为止，不会漏到圆角外面。</summary>
    sealed class MaskAdorner : System.Windows.Documents.Adorner
    {
        readonly Border _b;

        /// <param name="el">被盖住的元素（窗口的内容根）。</param>
        /// <param name="c">幕布颜色。</param>
        public MaskAdorner(UIElement el, Color c) : base(el)
        {
            IsHitTestVisible = false;
            _b = new Border { Background = new SolidColorBrush(c) };
            AddVisualChild(_b);
        }

        /// <summary>幕布本体：淡出动画挂在这块面上。</summary>
        public UIElement Surface => _b;

        /// <summary>只有一个视觉子级：那块纯色 Border。</summary>
        /// <returns>恒为 1。</returns>
        protected override int VisualChildrenCount => 1;

        /// <summary>取第 index 个视觉子级。</summary>
        /// <param name="index">子级下标，只有 0。</param>
        /// <returns>幕布那块 Border。</returns>
        protected override Visual GetVisualChild(int index) => _b;

        /// <summary>按被装饰元素的实际尺寸量幕布。</summary>
        /// <param name="constraint">装饰层给的建议尺寸。</param>
        /// <returns>被装饰元素的渲染尺寸。</returns>
        protected override Size MeasureOverride(Size constraint)
        {
            _b.Measure(constraint);
            return AdornedElement.RenderSize;
        }

        /// <summary>把幕布铺满整个装饰区。</summary>
        /// <param name="finalSize">装饰区尺寸。</param>
        /// <returns>原样返回。</returns>
        protected override Size ArrangeOverride(Size finalSize)
        {
            _b.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
