using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

// 本 partial 职责：快捷键列表的行级增删动画——删一行先淡出右滑，其余行补上空缺；
// 加一行先占位淡入，其余行让开。
/// <summary>
/// 快捷键列表的增删过渡：沿用芯片行的位移做法（记下布局前的位置、改完布局再让每行从布局前的位置
/// 滑到新位置），所以这里不做高度动画，被删的那一行退回布局之后，下面的行自然被带走。
/// 这种「列表项移除后，下方内容带动画补上空位」的做法通常叫 <b>FLIP 补位动画</b>
/// （First–Last–Invert–Play）：先记位置、改布局、再按差值倒推起点播放。
/// </summary>
public sealed partial class SettingsWindow
{
    /// <summary>按绑定 Id 找回那一行的可视容器，供删行时做动画。</summary>
    /// <value>由 <see cref="HotkeyRow"/> 登记；整表重建时清空。</value>
    private readonly Dictionary<string, FrameworkElement> _hotRows = new();

    /// <summary>记下每行当前的纵向位置，供改完布局后算补位位移。</summary>
    /// <returns>行 → 相对列表面板的 Y 坐标。</returns>
    private Dictionary<FrameworkElement, double> SnapshotRows()
    {
        var map = new Dictionary<FrameworkElement, double>();
        if (_hotList == null) return map;
        foreach (UIElement c in _hotList.Children)
            if (c is FrameworkElement fe) map[fe] = fe.TranslatePoint(new Point(0, 0), _hotList).Y;
        return map;
    }

    /// <summary>改完布局后，让每行从记下的布局前的位置滑到当前位置；状态间隔与缓动与芯片行一致。</summary>
    /// <param name="before">改布局前记下的行位置。</param>
    /// <param name="skip">不做补位动画的行（正在淡出的那一行）。</param>
    private void PlayReflow(Dictionary<FrameworkElement, double> before, FrameworkElement? skip = null)
    {
        if (_hotList == null) return;
        var span = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (UIElement c in _hotList.Children)
        {
            if (c is not FrameworkElement fe || ReferenceEquals(fe, skip)) continue;
            double now = fe.TranslatePoint(new Point(0, 0), _hotList).Y;
            double dy = (before.TryGetValue(fe, out var old) ? old : now) - now;
            if (Math.Abs(dy) < 0.5) continue;
            var tt = fe.RenderTransform as TranslateTransform ?? new TranslateTransform();
            fe.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, span) { EasingFunction = ease });
        }
    }

    /// <summary>删掉一条快捷键：这一行一边淡出右滑，一边把高度收到 0——下面的行被布局连着顶上来，收完再退出列表。
    /// 先滑走、再补位是两拍：滑走那段时间下面几行纹丝不动，收尾那一下看着就是一个跳。</summary>
    /// <param name="b">要删的绑定。</param>
    void DetachHotkeyAnimated(GestureBindingDto b)
    {
        if (_hotList == null) return;
        if (!_hotRows.TryGetValue(b.Id, out var row) || row.Parent is not Panel host)
        {
            RemoveHotkeyNow(b);
            return;
        }

        row.IsHitTestVisible = false;
        var tt = row.RenderTransform as TranslateTransform ?? new TranslateTransform();
        row.RenderTransform = tt;
        var span = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        // 展开着的行连正文一起收：限高由外层说话，这里只钉住行本身的高度
        double h = row.ActualHeight;
        var margin = row.Margin;
        row.ClipToBounds = true;
        var shrink = new DoubleAnimation(h, 0, span) { EasingFunction = ease };
        shrink.Completed += (_, _) =>
        {
            host.Children.Remove(row);
            _hotList?.UpdateLayout();
            RemoveHotkeyNow(b);
        };
        row.BeginAnimation(FrameworkElement.HeightProperty, shrink);
        row.BeginAnimation(FrameworkElement.MarginProperty,
            new ThicknessAnimation(margin, new Thickness(0), span) { EasingFunction = ease });
        row.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, span) { EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 24, span) { EasingFunction = ease });
    }

    /// <summary>把这条绑定从配置里摘掉并热重载键位；不改动界面。</summary>
    /// <param name="b">要摘掉的绑定。</param>
    void RemoveHotkeyNow(GestureBindingDto b)
    {
        _settings.Mutate(s => s.Gestures.Bindings.RemoveAll(x => ReferenceEquals(x, b)));
        AppHost.Current.InstallHotkeys();
        if (_hotOpen == b.Id) _hotOpen = "";
        _hotBoxes.Remove(b.Id);
        _hotRows.Remove(b.Id);
    }

    /// <summary>把一条新行插进列表（排在「添加快捷键」前面）：新行原地淡入，其余行向下让开。</summary>
    /// <param name="b">新绑定。</param>
    /// <returns>新行的容器。</returns>
    FrameworkElement InsertHotkeyAnimated(GestureBindingDto b)
    {
        var row = HotkeyRow(b);
        if (_hotList == null) return row;
        var before = SnapshotRows();
        int at = Math.Max(0, _hotList.Children.Count - 1);
        _hotList.Children.Insert(at, row);
        _hotList.UpdateLayout();

        // 新行从上方 8 px 落到原位并淡入；它占的高度立刻生效，其余行由 PlayReflow 滑下去
        row.Opacity = 0;
        var tt = new TranslateTransform(0, -8);
        row.RenderTransform = tt;
        row.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs))));
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        PlayReflow(before, row);
        return row;
    }
}
