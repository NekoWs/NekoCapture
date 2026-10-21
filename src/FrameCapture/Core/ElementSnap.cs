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
/// 控件级吸附：用 UI Automation 命中窗口内具体控件的边界。
/// 命中方式是 <see cref="AutomationElement.FromPoint"/> 一步到位——从顶层窗口往下逐层试那种做法
/// 在浏览器上要试十几层、每层几十个兄弟节点，60 ms 预算走两层就见底，只能停在无名容器上。
/// 一步命中之后再做两件事：语义过滤（无名容器不算控件）与有界修补（容器里找得到说得出口的孩子就用孩子）。
/// 依赖目标应用暴露无障碍元素；Electron 与 Chromium 系实测都能命中，
/// 完全不暴露的应用（游戏、canvas 自绘）返回 null，由调用方交给视觉吸附或整窗吸附。
/// </summary>
public static class ElementSnap
{
    /// <summary>修补时最多再往下走几层（只在一步命中落到匿名容器上时才用）。</summary>
    const int RepairDepth = 3;
    /// <summary>修补时一层里最多看几个兄弟节点。</summary>
    const int RepairChildren = 240;
    /// <summary>一次命中的总时间预算：冷启动第一次查 Chromium 的无障碍树要 30 ms 上下，150 ms 留足余量。</summary>
    const int BudgetMs = 150;
    /// <summary>控件的最小边长（物理像素）：比它还小的多半是装饰线或字符级碎片。</summary>
    const int MinSide = 6;

    /// <summary>说得出口的控件类型：这些是用户心里想的「那个按钮 / 那个输入框 / 那张图」。</summary>
    static readonly HashSet<ControlType> SemanticTypes = new()
    {
        ControlType.Button, ControlType.Hyperlink, ControlType.Edit, ControlType.Text,
        ControlType.Image, ControlType.ListItem, ControlType.TreeItem, ControlType.DataItem,
        ControlType.TabItem, ControlType.MenuItem, ControlType.CheckBox, ControlType.RadioButton,
        ControlType.ComboBox, ControlType.Slider, ControlType.Spinner,
        // 注：Document 不在表里——它是网页根节点（整页），吸上去等于整页截图
    };

    // 同一点位的悬停高频重复：按 (x, y) 缓存一次完整命中
    private static int _cacheX = int.MinValue, _cacheY;
    private static ElementHit? _cache;
    private static bool _warmed;

    /// <summary>返回光标物理点所在的最深控件边界；没有可用控件时为 null。
    /// 「可用」= 语义类型、或带名字，且边界包含该点、边长不小于 <see cref="MinSide"/>。</summary>
    /// <param name="physX">光标横坐标，<b>物理像素</b>的虚拟桌面坐标，不是 DIP。</param>
    /// <param name="physY">光标纵坐标，同上。</param>
    /// <param name="excludeHwnd">要从候选窗口里排除的句柄（自己的选区窗），无则 <c>IntPtr.Zero</c>。</param>
    /// <returns>命中结果；同一坐标重复调用走缓存。命中的是匿名容器、或 UIA 抛异常时为 null。</returns>
    public static ElementHit? At(int physX, int physY, IntPtr excludeHwnd)
    {
        if (physX == _cacheX && physY == _cacheY && _cache != null) return _cache;
        if (physX == _cacheX && physY == _cacheY && _cache == null) return null;

        ElementHit? result = null;
        var sw = Stopwatch.StartNew();
        try
        {
            // 冷启动预热：Chromium 是按需建无障碍树的，开选区时先对光标所在窗口问一次，
            // 首次悬停才不会只拿到外壳。
            if (!_warmed)
            {
                _warmed = true;
                if (TryTopmostAt(physX, physY, excludeHwnd, out IntPtr warmHwnd))
                    _ = AutomationElement.FromHandle(warmHwnd);
            }
            var hit = AutomationElement.FromPoint(new Point(physX, physY));
            if (hit != null && !IsExcluded(hit, excludeHwnd))
                result = Pick(hit, physX, physY, sw);
        }
        catch { result = null; } // UIA 对提权进程/死句柄会抛 COMException

        _cacheX = physX; _cacheY = physY; _cache = result;
        return result;
    }

    /// <summary>这枚元素是不是「说得出口」的控件：语义类型，或带非空名字。</summary>
    /// <param name="el">UIA 元素。</param>
    /// <returns>语义类型或带名字返回 true；匿名 Pane/Group/Document 之类返回 false。</returns>
    public static bool IsSemantic(AutomationElement el)
    {
        try { return IsSemantic(el.Current.ControlType, el.Current.Name); }
        catch { return false; }
    }

    /// <summary>判定的纯逻辑部分：给定类型与名字，这算不算「说得出口的控件」。
    /// 抽成纯函数是为了能直接测——真实元素造不出来，判定规则本身可以。</summary>
    /// <param name="ct">控件类型。</param>
    /// <param name="name">无障碍名字，可能为空。</param>
    /// <returns>语义类型或带非空名字返回 true。</returns>
    public static bool IsSemantic(ControlType ct, string? name) =>
        ct != ControlType.Document && (SemanticTypes.Contains(ct) || !string.IsNullOrWhiteSpace(name));

    /// <summary>从一次命中里挑出最终结果：已经在语义元素上就用它；落在匿名容器上则有界地往下找。</summary>
    /// <param name="hit">一步命中的元素。</param>
    /// <param name="physX">光标横坐标（物理像素）。</param>
    /// <param name="physY">光标纵坐标（物理像素）。</param>
    /// <param name="sw">本次命中的计时器，用来守住预算。</param>
    /// <returns>可用命中；没有可用控件时为 null。</returns>
    static ElementHit? Pick(AutomationElement hit, int physX, int physY, Stopwatch sw)
    {
        var r = FromUia(hit);
        if (r == null) return null;
        if (IsSemantic(hit) && Usable(r.Value, physX, physY))
            return new ElementHit { Rect = r.Value, Element = hit };

        // 匿名容器：往下找「包含光标点、且说得出口」的孩子，取面积最小的那个。
        var walker = TreeWalker.ControlViewWalker;
        var cur = hit;
        RectI curRect = r.Value;
        for (int depth = 0; depth < RepairDepth; depth++)
        {
            if (sw.ElapsedMilliseconds > BudgetMs) break;
            AutomationElement? best = null;
            RectI bestRect = default;
            long bestArea = Area(curRect);
            AutomationElement? child;
            try { child = walker.GetFirstChild(cur); } catch { break; }
            int seen = 0;
            while (child != null && seen < RepairChildren)
            {
                seen++;
                var cr = FromUia(child);
                if (cr != null && Usable(cr.Value, physX, physY) && Area(cr.Value) < bestArea && IsSemantic(child))
                {
                    best = child;
                    bestRect = cr.Value;
                    bestArea = Area(cr.Value);
                }
                try { child = walker.GetNextSibling(child); } catch { break; }
            }
            if (best == null) break;
            cur = best;
            curRect = bestRect;
        }
        return cur != hit && Usable(curRect, physX, physY)
            ? new ElementHit { Rect = curRect, Element = cur }
            : null;
    }

    /// <summary>这块矩形能不能当控件：包含光标点、边长够大。</summary>
    /// <param name="r">候选矩形（物理像素）。</param>
    /// <param name="physX">光标横坐标。</param>
    /// <param name="physY">光标纵坐标。</param>
    static bool Usable(RectI r, int physX, int physY) =>
        r.W >= MinSide && r.H >= MinSide && r.Contains(physX, physY);

    /// <summary>命中落在自己的选区窗上时不能用：否则吸到的是自己的遮罩。</summary>
    /// <param name="el">UIA 元素。</param>
    /// <param name="excludeHwnd">要排除的窗口句柄，<c>IntPtr.Zero</c> 表示不排除。</param>
    static bool IsExcluded(AutomationElement el, IntPtr excludeHwnd)
    {
        if (excludeHwnd == IntPtr.Zero) return false;
        try
        {
            IntPtr h = (IntPtr)el.Current.NativeWindowHandle;
            return h != IntPtr.Zero && h == excludeHwnd;
        }
        catch { return false; }
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
