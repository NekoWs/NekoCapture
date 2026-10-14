using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GestureEditor;

/// <summary>
/// 面板内逐项错位入场：淡入 + 轻微上移，行与行之间错开 <see cref="Motion.StaggerMs"/>。
/// 本库不引用宿主 FrameCapture，故自带一份，语义与时长对齐宿主的分层入场——
/// 项数越多间隔自动收窄，整段压在预算内，几十行不会拖成秒级。
/// </summary>
static class Cascade
{
    /// <summary>本类造出的位移动换登记，清零时只摘自己造的那个，不动调用方合法挂着的变换。</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TranslateTransform, object> Shifts = new();

    /// <summary>让面板的每个子项依次淡入上移。</summary>
    /// <param name="body">承载行的面板。</param>
    /// <param name="risePx">起跳时的下移像素，落位时回到 0；幅度克制，只做「浮起来」的暗示。</param>
    /// <param name="itemMs">单项时长（ms）；≤0 用 <see cref="Motion.EnterMs"/>。</param>
    /// <param name="budgetMs">整段入场预算（ms）；≤0 用 <see cref="Motion.StaggerMs"/> × 3。</param>
    /// <param name="leadMs">整段起跳前的等待（ms），用于排在窗口入场之后。</param>
    public static void In(Panel body, double risePx = 6, int itemMs = -1, int budgetMs = -1, int leadMs = 0)
    {
        int n = body.Children.Count;
        if (n == 0) return;
        itemMs = itemMs > 0 ? itemMs : Motion.EnterMs;
        budgetMs = budgetMs > 0 ? budgetMs : Motion.StaggerMs * 3;
        // 错位间隔取令牌与「(预算 − 单项) ÷ 行距数」中较小者：项少时错位明显，项多时自动收窄
        double step = n > 1 ? Math.Max(0, Math.Min(Motion.StaggerMs, (double)(budgetMs - itemMs) / (n - 1))) : 0;
        var dur = new Duration(TimeSpan.FromMilliseconds(itemMs));
        int lead = Math.Max(0, leadMs);
        int k = 0;
        foreach (var c in body.Children)
        {
            k++;
            if (c is not FrameworkElement fe) continue;
            var begin = TimeSpan.FromMilliseconds(lead + step * (k - 1));
            fe.BeginAnimation(UIElement.OpacityProperty, null);
            fe.Opacity = 0;
            // From 显式给 0：lead 期间动画还没进入活动期，默认 FillBehavior 会把起始值顶住，
            // 属性从第一帧起归动画所有——后排因此停在 0，不会先整块可见再瞬间清空。
            var fade = new DoubleAnimation(0, 1, dur)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = begin };
            fade.Freeze();
            fe.BeginAnimation(UIElement.OpacityProperty, fade);
            if (risePx <= 0) continue;
            // 已有变换的行不覆盖：详情行日后可能自带缩放变换，覆盖会让缩放动画静默失效
            var prior = fe.RenderTransform;
            var tt = new TranslateTransform(0, risePx);
            Shifts.Add(tt, new object());
            TransformGroup? group = null;
            if (prior is null) fe.RenderTransform = tt;
            else
            {
                group = new TransformGroup();
                group.Children.Add(prior);
                group.Children.Add(tt);
                fe.RenderTransform = group;
            }
            // 位移归零后撤掉自己：留着 TranslateTransform 会让后续挂缩放的代码以为已有变换而跳过
            void ClearShift()
            {
                if (ReferenceEquals(fe.RenderTransform, tt)) { fe.RenderTransform = null; return; }
                if (group is not null && ReferenceEquals(fe.RenderTransform, group))
                {
                    group.Children.Remove(tt);
                    fe.RenderTransform = group.Children.Count == 1 ? group.Children[0] : group;
                }
            }
            var rise = new DoubleAnimation(0, dur)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = begin };
            rise.Completed += (_, __) => ClearShift();
            rise.Freeze();   // 回调必须赶在冻结前挂上：冻结后挂事件按「修改」处理
            tt.BeginAnimation(TranslateTransform.YProperty, rise);
        }
    }

    /// <summary>撤掉 <see cref="In"/> 挂上的动画与位移，恢复完全可见。
    /// 不走动画的重建路径必须调它，否则可能在飞的行会停在半透明。</summary>
    /// <param name="body">承载行的面板。</param>
    public static void Reset(Panel body)
    {
        foreach (var c in body.Children)
        {
            if (c is not FrameworkElement fe) continue;
            fe.BeginAnimation(UIElement.OpacityProperty, null);
            fe.Opacity = 1;
            if (fe.RenderTransform is TranslateTransform bare && Shifts.TryGetValue(bare, out _))
                fe.RenderTransform = null;
            else if (fe.RenderTransform is TransformGroup g)
            {
                for (int i = g.Children.Count - 1; i >= 0; i--)
                    if (g.Children[i] is TranslateTransform ct && Shifts.TryGetValue(ct, out _))
                        g.Children.RemoveAt(i);
                if (g.Children.Count == 1) fe.RenderTransform = g.Children[0];
            }
        }
    }
}
