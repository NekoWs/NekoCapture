using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using FrameCapture.Capture;
using FrameCapture.Native;

namespace FrameCapture.Core;

/// <summary>控件命中结果：边界（物理像素）+ UIA 元素句柄（供继续向上/下钻层级）。</summary>
public sealed class ElementHit
{
    public RectI Rect;
    public AutomationElement Element = null!;
}

/// <summary>
/// 控件级吸附：用 UI Automation 命中窗口内具体控件的边界
/// （输入框、文字、图片、分组容器等）。依赖目标应用暴露无障碍元素；
/// 不支持的应用（多数游戏/自绘 UI）会返回 null，调用方回退窗口级吸附。
/// </summary>
public static class ElementSnap
{
    private const int MaxDepth = 14;
    private const int MaxChildren = 400;
    private const int BudgetMs = 60;

    // 同一点位的悬停高频重复：按 (x, y) 缓存一次完整命中
    private static int _cacheX = int.MinValue, _cacheY;
    private static ElementHit? _cache;

    /// <summary>返回光标物理点所在的最深控件边界；无候选窗口或 UIA 不可用时为 null。</summary>
    /// <param name="physX">光标横坐标，<b>物理像素</b>的虚拟桌面坐标，不是 DIP。</param>
    /// <param name="physY">光标纵坐标，同上。</param>
    /// <param name="excludeHwnd">要从候选窗口里排除的句柄（自己的选区窗），无则 <c>IntPtr.Zero</c>。</param>
    /// <returns>命中结果；同一坐标重复调用走缓存。没有窗口含该点、或 UIA 抛异常时为 null。</returns>
    public static ElementHit? At(int physX, int physY, IntPtr excludeHwnd)
    {
        if (physX == _cacheX && physY == _cacheY && _cache != null)
            return _cache;
        if (!TryTopmostAt(physX, physY, excludeHwnd, out IntPtr hwnd))
            return null;

        ElementHit? result = null;
        try
        {
            var sw = Stopwatch.StartNew();
            var root = AutomationElement.FromHandle(hwnd);
            var walker = TreeWalker.ControlViewWalker;
            var cur = root;
            RectI curRect = FromUia(root) ?? new RectI(0, 0, 0, 0);
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (sw.ElapsedMilliseconds > BudgetMs) break;
                AutomationElement? best = null;
                RectI bestRect = default;
                long bestArea = Area(curRect);
                var child = walker.GetFirstChild(cur);
                int seen = 0;
                while (child != null && seen < MaxChildren)
                {
                    seen++;
                    var r = FromUia(child);
                    if (r != null && r.Value.W >= 3 && r.Value.H >= 3 &&
                        r.Value.Contains(physX, physY) && Area(r.Value) < bestArea)
                    {
                        best = child;
                        bestRect = r.Value;
                        bestArea = Area(r.Value);
                    }
                    child = walker.GetNextSibling(child);
                }
                if (best == null) break;
                cur = best;
                curRect = bestRect;
            }
            if (FromUia(root) is RectI rr && rr.Contains(physX, physY))
                result = new ElementHit { Rect = curRect, Element = cur };
        }
        catch { result = null; } // UIA 对提权进程/死句柄会抛 COMException

        _cacheX = physX; _cacheY = physY; _cache = result;
        return result;
    }

    /// <summary>从命中的控件向上钻一层（Tab 键：控件 → 父容器 → … → 整窗）。</summary>
    /// <param name="hit">上一次命中结果，其 Element 决定起点。</param>
    /// <returns>父容器的新命中；已到根元素、父级边界取不到或 UIA 抛异常时为 null，调用方据此停止上钻。</returns>
    public static ElementHit? Parent(ElementHit hit)
    {
        try
        {
            var p = TreeWalker.ControlViewWalker.GetParent(hit.Element);
            if (p == null) return null;
            var r = FromUia(p);
            if (r == null) return null;
            return new ElementHit { Rect = r.Value, Element = p };
        }
        catch { return null; }
    }

    private static bool TryTopmostAt(int x, int y, IntPtr excludeHwnd, out IntPtr hwnd)
    {
        foreach (var t in WindowEnum.ListTopmostCandidates(excludeHwnd))
        {
            if (t.Bounds.Contains(x, y)) { hwnd = t.Hwnd; return true; }
        }
        hwnd = IntPtr.Zero;
        return false;
    }

    private static RectI? FromUia(AutomationElement el)
    {
        try
        {
            var r = el.Current.BoundingRectangle;
            if (r == Rect.Empty || double.IsInfinity(r.Width) || double.IsInfinity(r.Height) ||
                r.Width <= 0 || r.Height <= 0)
                return null;
            return new RectI((int)r.X, (int)r.Y, (int)Math.Round(r.Width), (int)Math.Round(r.Height));
        }
        catch { return null; }
    }

    private static long Area(RectI r) => (long)r.W * r.H;
}
