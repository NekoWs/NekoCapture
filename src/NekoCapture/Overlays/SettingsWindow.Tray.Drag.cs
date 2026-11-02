using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using NekoCapture.Core;
using NekoCapture.Services;

namespace NekoCapture.Overlays;

/// <summary>托盘菜单行的拖动排序：按住行体拖动、邻行让位、松手带弹性落位。</summary>
public sealed partial class SettingsWindow
{
    /// <summary>拖起时的放大倍率。</summary>
    private const double TrayLiftScale = 1.03;

    private Border? _trayDragRow;
    private Point _trayDragFrom;
    private int _trayDragTarget = -1;
    private bool _trayDragging;
    private double _trayDragOffset;

    /// <summary>按住一行：只记起点，不抢鼠标——图标、下拉与删除按钮都还要能点。</summary>
    /// <param name="row">按下的那一行。</param>
    /// <param name="e">鼠标参数。</param>
    private void BeginTrayDrag(Border row, MouseButtonEventArgs e)
    {
        if (_trayMenuList == null || IsInteractiveSource(e.OriginalSource as DependencyObject, row)) return;
        _trayDragRow = row;
        _trayDragging = false;
        _trayDragOffset = 0;
        _trayDragTarget = _trayMenuList.Children.IndexOf(row);
        _trayDragFrom = e.GetPosition(_trayMenuList);
    }

    /// <summary>指针从按钮、下拉、输入框里出来的不算拖动：那些控件自己要吃这一下。</summary>
    /// <param name="node">命中的元素。</param>
    /// <param name="row">这一行的容器，走到它就停。</param>
    /// <returns>落在交互控件里返回 true。</returns>
    private static bool IsInteractiveSource(DependencyObject? node, DependencyObject row)
    {
        for (var at = node; at != null && !ReferenceEquals(at, row); at = VisualTreeHelper.GetParent(at))
            if (at is ButtonBase or ComboBox or TextBox or PasswordBox or ScrollBar) return true;
        return false;
    }

    /// <summary>拖动中：被拖的行跟着指针走，其余行按目标槽位让位，位移带动画。</summary>
    /// <param name="e">鼠标参数。</param>
    private void MoveTrayDrag(MouseEventArgs e)
    {
        if (_trayDragRow == null || _trayMenuList == null || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(_trayMenuList);
        if (!_trayDragging)
        {
            if (Math.Abs(at.Y - _trayDragFrom.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            LiftTrayRow(_trayDragRow);
        }
        _trayDragOffset = at.Y - _trayDragFrom.Y;
        var (shift, _) = RowTransforms(_trayDragRow);
        // 上一回落位动画可能还攥着这条位移：先交还属性再写本地值，行不会在手里跳一下
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        shift.Y = _trayDragOffset;
        int target = TrayDropIndex(at.Y, _trayDragRow);
        if (target == _trayDragTarget) return;
        _trayDragTarget = target;
        ShiftTrayRows();
    }

    /// <summary>起拖：把行抬起来——投影、放大、压在邻行之上，只在越过阈值那一次做。</summary>
    /// <param name="row">被拖的行。</param>
    private void LiftTrayRow(Border row)
    {
        _trayDragging = true;
        row.CaptureMouse();
        row.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 5, Opacity = 0.45, Color = Colors.Black };
        row.Opacity = 0.96;
        Panel.SetZIndex(row, 20);
        var (_, scale) = RowTransforms(row);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, ScaleTo(TrayLiftScale));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleTo(TrayLiftScale));
    }

    /// <summary>相邻两行在行容器里的间距：行高加上下各 2 像素的外边距。</summary>
    /// <param name="row">量谁的高度；给 null 时只算外边距。</param>
    /// <returns>一步的像素数。</returns>
    private static double TrayStepOf(FrameworkElement? row) => Math.Max(1, (row?.ActualHeight ?? 0) + 4);

    /// <summary>目标槽位：指针落在第几行的高度范围内就落到第几格。</summary>
    /// <param name="y">相对行容器的指针纵坐标。</param>
    /// <param name="row">被拖的那一行，用来取行距。</param>
    /// <returns>目标下标。</returns>
    private int TrayDropIndex(double y, FrameworkElement row)
    {
        if (_trayMenuList == null || _trayMenuList.Children.Count == 0) return 0;
        int index = (int)Math.Floor(Math.Max(0, y) / TrayStepOf(row));
        return Math.Clamp(index, 0, _trayMenuList.Children.Count - 1);
    }

    /// <summary>其余行按「被拖的行落到目标位之后」的位移滑开，位移本身带动画。</summary>
    private void ShiftTrayRows()
    {
        if (_trayMenuList == null || _trayDragRow == null) return;
        int from = _trayMenuList.Children.IndexOf(_trayDragRow);
        double step = TrayStepOf(_trayDragRow);
        for (int i = 0; i < _trayMenuList.Children.Count; i++)
        {
            if (_trayMenuList.Children[i] is not FrameworkElement row || ReferenceEquals(row, _trayDragRow)) continue;
            double want = 0;
            if (from < _trayDragTarget && i > from && i <= _trayDragTarget) want = -step;
            else if (from > _trayDragTarget && i >= _trayDragTarget && i < from) want = step;
            var (shift, _) = RowTransforms(row);
            double at = shift.Y;
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = at;
            if (Math.Abs(at - want) < 0.5) continue;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(want,
                new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    /// <summary>松手：先按目标槽位换好顺序，再给每行补一个「原位置减新位置」的位移并让它弹回 0，
    /// 被拖的那一行带过冲，其余行同步归零。元素本身已经落在新位置，落位那一刻不会跳。</summary>
    /// <param name="e">鼠标参数。</param>
    private void EndTrayDrag(MouseButtonEventArgs e)
    {
        var row = _trayDragRow;
        bool dragged = _trayDragging;
        int target = _trayDragTarget;
        _trayDragRow = null;
        _trayDragging = false;
        _trayDragTarget = -1;
        _trayDragOffset = 0;
        if (row == null || _trayMenuList == null) return;
        if (row.IsMouseCaptured) row.ReleaseMouseCapture();
        row.Effect = null;
        row.Opacity = 1;
        if (!dragged) { row.RenderTransform = null; return; }
        if (target < 0) target = _trayMenuList.Children.IndexOf(row);
        var before = TrayPositions();
        var ordered = TrayOrder(target, row);
        ApplyTrayOrder(ordered);
        PersistTrayOrder(ordered);
        _trayMenuList.UpdateLayout();
        foreach (var item in ordered)
        {
            if (!before.TryGetValue(item, out var was)) continue;
            var (shift, scale) = RowTransforms(item);
            double dy = was.Y + shift.Y - item.TranslatePoint(new Point(0, 0), _trayMenuList).Y;
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = dy;
            bool landed = ReferenceEquals(item, row);
            int ms = landed ? Fluent.Motion.SpringMs : Fluent.Motion.StateMs;
            var settle = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(ms)))
            {
                EasingFunction = landed
                    ? new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
                    : new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            if (landed) settle.Completed += (_, _) => ResetTrayRows();
            shift.BeginAnimation(TranslateTransform.YProperty, settle);
            if (!landed) continue;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, ScaleTo(1));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleTo(1));
        }
    }

    /// <summary>记下每行此刻在行容器里的位置，含已经加上去的位移。</summary>
    /// <returns>行 → 位置。</returns>
    private Dictionary<FrameworkElement, Point> TrayPositions()
    {
        var positions = new Dictionary<FrameworkElement, Point>();
        if (_trayMenuList == null) return positions;
        foreach (var item in _trayMenuList.Children.OfType<FrameworkElement>())
            positions[item] = item.TranslatePoint(new Point(0, 0), _trayMenuList);
        return positions;
    }

    /// <summary>鼠标被别人拿走（切窗、弹窗抢焦点）：把行放回原位，顺序保持不动。</summary>
    /// <param name="row">被拖的那一行。</param>
    private void AbortTrayDrag(Border row)
    {
        if (!ReferenceEquals(_trayDragRow, row)) return;
        _trayDragRow = null;
        _trayDragging = false;
        _trayDragTarget = -1;
        _trayDragOffset = 0;
        ResetTrayRows();
    }

    /// <summary>清掉拖动留下的投影、层级、透明度与位移/缩放；行回到静止的样子。</summary>
    private void ResetTrayRows()
    {
        if (_trayMenuList == null) return;
        foreach (UIElement child in _trayMenuList.Children)
        {
            if (child is not FrameworkElement row) continue;
            Panel.SetZIndex(row, 0);
            row.Opacity = 1;
            row.Effect = null;
            row.RenderTransform = null;
        }
    }

    /// <summary>按目标槽位排出新的行序列：被拖的那一行插在第 <paramref name="target"/> 格。</summary>
    /// <param name="target">目标下标。</param>
    /// <param name="row">落位的那一行。</param>
    /// <returns>新的顺序。</returns>
    private List<FrameworkElement> TrayOrder(int target, FrameworkElement row)
    {
        var ordered = _trayMenuList == null
            ? new List<FrameworkElement>()
            : _trayMenuList.Children.OfType<FrameworkElement>().Where(item => !ReferenceEquals(item, row)).ToList();
        ordered.Insert(Math.Clamp(target, 0, ordered.Count), row);
        return ordered;
    }

    /// <summary>按新顺序重排子元素：同一批控件换个次序，不重建。</summary>
    /// <param name="ordered">新的顺序。</param>
    private void ApplyTrayOrder(IReadOnlyList<FrameworkElement> ordered)
    {
        if (_trayMenuList == null || ordered.Count != _trayMenuList.Children.Count) return;
        foreach (var item in ordered)
            if (!_trayMenuList.Children.Contains(item)) return;
        _trayMenuList.Children.Clear();
        foreach (var item in ordered) _trayMenuList.Children.Add(item);
    }

    /// <summary>把新顺序写回设置：托盘项按视觉顺序排在前面，其余来源保持原位。</summary>
    /// <param name="ordered">新的视觉顺序。</param>
    private void PersistTrayOrder(IReadOnlyList<FrameworkElement> ordered)
    {
        var ids = ordered.Select(item => (item.Tag as string ?? "").Replace(TrayRowTag, "")).ToList();
        _settings.Mutate(settings =>
        {
            var all = settings.Gestures.Bindings;
            var moved = ids.Select(id => all.FirstOrDefault(item => item.Id == id))
                .Where(item => item != null).Select(item => item!).ToList();
            var rest = all.Where(item => item.Source != TriggerSources.TrayMenu).ToList();
            all.Clear(); all.AddRange(moved); all.AddRange(rest);
        });
        CommitHotkeys();
    }
}
