using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

// 本 partial 职责：设置窗里「展开 / 收起」这一类高度动画——容器长出高度，内容逐行滑入；
// 以及成排条目那种「静止透明、悬停才浮底色」的填充。
/// <summary>
/// 设置窗的高度动画：容器从 0 长到实测高度，里面的行按固定间隔自上而下逐个滑入，收起时反向收回。
/// 时长与错位间隔全部取 <see cref="Fluent.Motion"/> 令牌。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>静止时全透明、指针进来才浮出底色的填充。成排的条目各自垫一块灰底会把列表切成格子，
    /// 底色于是只在悬停时出现。<see cref="Fluent.ChipHover"/> 走的是「与基色混合」，
    /// 透明基色混出来仍是透明，这里直接淡到 <see cref="Theme.Hover"/> 本身。</summary>
    /// <param name="bd">目标框；<c>Background</c> 被换成本方法持有的可动画画刷。</param>
    static void HoverFill(Border bd)
    {
        var bg = new SolidColorBrush(Colors.Transparent);
        bd.Background = bg;
        void Tint(Color to) => bg.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.HoverMs))));
        bd.MouseEnter += (_, _) => Tint(Theme.Hover);
        bd.MouseLeave += (_, _) => Tint(Colors.Transparent);
    }

    /// <summary>逐行错位的间隔：取分层错位间隔的三分之一，四五行走完仍落在一次入场时长里。</summary>
    /// <value>约 37 毫秒。</value>
    static TimeSpan RowStep => TimeSpan.FromMilliseconds(Fluent.Motion.StaggerMs / 3);

    /// <summary>展开正文容器：高度从 0 长到实测高度，容器里可见的行自上而下逐个滑入。</summary>
    /// <param name="host">正文容器；动画期间靠 MaxHeight 限高，走完后放开。</param>
    /// <param name="rows">容器里的行面板，按可见顺序逐行错位。</param>
    /// <param name="width">容器可用的排版宽度，用来量出目标高度。</param>
    static void PlayExpand(Border host, Panel rows, double width)
    {
        host.BeginAnimation(MaxHeightProperty, null);
        host.MaxHeight = double.PositiveInfinity;
        host.Visibility = Visibility.Visible;
        host.Measure(new Size(Math.Max(240, width), double.PositiveInfinity));
        double h = Math.Ceiling(host.DesiredSize.Height);
        var grow = new DoubleAnimation(0, h, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        // 走完必须放开限高：正文里就地铺开候选动作时高度会长，留着 MaxHeight 会把新铺的芯片裁掉。
        grow.Completed += (_, _) =>
        {
            host.BeginAnimation(MaxHeightProperty, null);
            host.MaxHeight = double.PositiveInfinity;
        };
        host.MaxHeight = 0;
        host.BeginAnimation(MaxHeightProperty, grow);
        int i = 0;
        foreach (UIElement c in rows.Children)
        {
            if (c.Visibility != Visibility.Visible) continue;
            SlideIn(c, TimeSpan.FromMilliseconds(i * RowStep.TotalMilliseconds));
            i++;
        }
    }

    /// <summary>收起一个容器或一行：高度收回 0，走完后置为不可见。</summary>
    /// <param name="el">要收起的元素。</param>
    static void PlayCollapse(FrameworkElement el)
    {
        if (el.Visibility != Visibility.Visible)
        {
            el.BeginAnimation(MaxHeightProperty, null);
            el.MaxHeight = 0;
            return;
        }
        double from = el.ActualHeight > 1 ? el.ActualHeight : 1;
        var shrink = new DoubleAnimation(from, 0, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.ExitMs)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        shrink.Completed += (_, _) =>
        {
            el.BeginAnimation(MaxHeightProperty, null);
            el.MaxHeight = 0;
            el.Visibility = Visibility.Collapsed;
        };
        el.BeginAnimation(MaxHeightProperty, shrink);
    }

    /// <summary>把单独一行就地长出来：高度从 0 到实测高度，同时滑入。</summary>
    /// <param name="row">要露出的行。</param>
    /// <param name="width">排版宽度。</param>
    static void RevealRow(FrameworkElement row, double width)
    {
        row.BeginAnimation(MaxHeightProperty, null);
        row.MaxHeight = double.PositiveInfinity;
        row.Visibility = Visibility.Visible;
        row.Measure(new Size(Math.Max(240, width), double.PositiveInfinity));
        double h = Math.Ceiling(row.DesiredSize.Height);
        var grow = new DoubleAnimation(0, h, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        grow.Completed += (_, _) =>
        {
            row.BeginAnimation(MaxHeightProperty, null);
            row.MaxHeight = double.PositiveInfinity;
        };
        row.MaxHeight = 0;
        row.BeginAnimation(MaxHeightProperty, grow);
        SlideIn(row, TimeSpan.Zero);
    }

    /// <summary>一行滑入：从上方 8 像素落到原位并淡入。</summary>
    /// <param name="el">要滑入的元素。</param>
    /// <param name="begin">入场起点，逐行错开。</param>
    static void SlideIn(UIElement el, TimeSpan begin)
    {
        if (el.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform();
            el.RenderTransform = tt;
        }
        el.Opacity = 0;
        tt.Y = -8;
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs))) { BeginTime = begin });
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
        { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
