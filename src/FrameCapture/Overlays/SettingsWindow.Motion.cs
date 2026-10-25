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
    /// <summary>悬停时浮出底色的填充：<paramref name="rest"/> 是静止底色，<paramref name="hover"/> 是
    /// 指针进来时的底色，指针离开回到静止底色。默认静止全透明——给没有底色的元素用；
    /// 已经在层次台阶上的行传进 <see cref="Theme.RowBg"/> 与 <see cref="Theme.RowHover"/>，
    /// 悬停就在台阶上再走一档，而不是把底色抹掉再混一层白。</summary>
    /// <param name="bd">目标框；<c>Background</c> 被换成本方法持有的可动画画刷。</param>
    /// <param name="rest">静止底色，省略即全透明。</param>
    /// <param name="hover">悬停底色，省略即 <see cref="Theme.Hover"/>。</param>
    static void HoverFill(Border bd, Color? rest = null, Color? hover = null)
    {
        var restColor = rest ?? Colors.Transparent;
        var hoverColor = hover ?? Theme.Hover;
        // 按压色 = 底上再压一层半透明白：暗色更亮一档、亮色更暗一档，方向跟着主题走。
        // 静止全透明的幽灵按钮没有底色可压，就拿悬停色当底，否则按下去什么变化都没有。
        var pressColor = Fluent.Mix(restColor.A > 0 ? restColor : hoverColor, Theme.Press);
        var bg = new SolidColorBrush(restColor);
        bd.Background = bg;
        bool inside = false;
        void TintBg(Color to, int ms) => bg.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms))));
        bd.MouseEnter += (_, _) => { inside = true; TintBg(hoverColor, Fluent.Motion.HoverMs); };
        bd.MouseLeave += (_, _) => { inside = false; TintBg(restColor, Fluent.Motion.ExitMs); };
        bd.PreviewMouseLeftButtonDown += (_, _) => TintBg(pressColor, Fluent.Motion.PressMs);
        // 抬起回到指针所在的这一档：还在框里就停悬停色，已经滑出去才回静止色。
        bd.MouseLeftButtonUp += (_, _) => TintBg(inside ? hoverColor : restColor, Fluent.Motion.QuickMs);
    }

    /// <summary>逐行错位的间隔：取分层错位间隔的三分之一，四五行走完仍落在一次入场时长里。</summary>
    /// <value>约 37 毫秒。</value>
    static TimeSpan RowStep => TimeSpan.FromMilliseconds(Fluent.Motion.StaggerMs / 3);

    /// <summary>展开正文容器：容器先长到实测高度，长到一半时内容自上而下逐条淡入。
    /// 两件事同时开跑会糊成一次整体位移——盒子先就位，内容才有落点。</summary>
    /// <param name="host">正文容器；动画期间靠 MaxHeight 限高，走完后放开。</param>
    /// <param name="rows">容器里的行面板，按可见顺序逐行错位。</param>
    /// <param name="width">容器可用的排版宽度，用来量出目标高度。</param>
    static void PlayExpand(Border host, Panel rows, double width)
    {
        ClearFade(host);
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
        // 半拍后开始落位：等容器完全长够再逐条淡入，整段会拖成明显的两拍
        int i = 0;
        foreach (UIElement c in rows.Children)
        {
            if (c.Visibility != Visibility.Visible) continue;
            SlideIn(c, TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs * 0.5 + i * RowStep.TotalMilliseconds));
            i++;
        }
    }

    /// <summary>收起一个容器或一行：内容先逐条淡出，容器再收口，走完后置为不可见。</summary>
    /// <param name="el">要收起的元素。</param>
    /// <param name="delayMs">起跳前的等待毫秒数：同一批里后收的行等在这一行前面。</param>
    static void PlayCollapse(FrameworkElement el, double delayMs = 0)
    {
        if (el.Visibility != Visibility.Visible)
        {
            el.BeginAnimation(MaxHeightProperty, null);
            el.MaxHeight = 0;
            return;
        }
        double from = el.ActualHeight > 1 ? el.ActualHeight : 1;
        // 收口与内容淡出同时开跑：内容先退完再收，中间多出一拍空等，看着像卡了一下
        FadeOutRows(el);
        var shrink = new DoubleAnimation(from, 0, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.ExitMs)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
        };
        shrink.Completed += (_, _) =>
        {
            el.BeginAnimation(MaxHeightProperty, null);
            el.MaxHeight = 0;
            el.Visibility = Visibility.Collapsed;
        };
        el.BeginAnimation(MaxHeightProperty, shrink);
    }

    /// <summary>把容器里的可见行逐条淡出，倒着退（最后一条先走），与展开的先后成镜像。</summary>
    /// <param name="el">容器：正文框或一行。</param>
    /// <returns>整段淡出占用的毫秒数，收口动画据此错开；容器里没有行面板时为 0。</returns>
    static double FadeOutRows(FrameworkElement el)
    {
        Panel? rows = (el as Border)?.Child as Panel ?? el as Panel;
        if (rows == null) return 0;
        double step = RowStep.TotalMilliseconds / 2;
        int i = 0;
        foreach (UIElement c in rows.Children)
            if (c.Visibility == Visibility.Visible) i++;
        int at = i - 1;
        var d = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        foreach (UIElement c in rows.Children)
        {
            if (c.Visibility != Visibility.Visible) continue;
            // 倒着退：最后一条先走，与展开的先后成镜像；退的同时往下滑一点，方向与入场相反
            var begin = TimeSpan.FromMilliseconds(at * step);
            c.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, d) { BeginTime = begin, EasingFunction = ease });
            var tt = c.RenderTransform as TranslateTransform ?? new TranslateTransform();
            c.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 6, d) { BeginTime = begin, EasingFunction = ease });
            at--;
        }
        return i <= 1 ? 0 : (i - 1) * step;
    }

    /// <summary>清掉淡出留下的透明度动画。收起时逐条淡出的行，再次露出时若不清，
    /// 那条动画的终值会把这一行一直按在透明上。</summary>
    /// <param name="el">要清干净的容器。</param>
    static void ClearFade(FrameworkElement el)
    {
        el.BeginAnimation(UIElement.OpacityProperty, null);
        Panel? rows = (el as Border)?.Child as Panel ?? el as Panel;
        if (rows == null) return;
        foreach (UIElement c in rows.Children) c.BeginAnimation(UIElement.OpacityProperty, null);
    }

    /// <summary>把单独一行就地长出来：高度从 0 到实测高度，随后滑入。</summary>
    /// <param name="row">要露出的行。</param>
    /// <param name="width">排版宽度。</param>
    /// <param name="delayMs">起跳前的等待毫秒数：同一批里后露出的行据此排到前一行之后。</param>
    static void RevealRow(FrameworkElement row, double width, double delayMs = 0)
    {
        ClearFade(row);
        row.BeginAnimation(MaxHeightProperty, null);
        row.MaxHeight = double.PositiveInfinity;
        row.Visibility = Visibility.Visible;
        row.Measure(new Size(Math.Max(240, width), double.PositiveInfinity));
        double h = Math.Ceiling(row.DesiredSize.Height);
        var grow = new DoubleAnimation(0, h, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = TimeSpan.FromMilliseconds(delayMs) };
        grow.Completed += (_, _) =>
        {
            row.BeginAnimation(MaxHeightProperty, null);
            row.MaxHeight = double.PositiveInfinity;
        };
        row.MaxHeight = 0;
        row.BeginAnimation(MaxHeightProperty, grow);
        SlideIn(row, TimeSpan.FromMilliseconds(delayMs + Fluent.Motion.EnterMs * 0.5));
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
