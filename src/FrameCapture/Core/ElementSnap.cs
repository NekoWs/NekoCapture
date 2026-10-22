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
    /// <summary>UIA 元素；MSAA 命中时为空，此时不支持 Tab 上钻。</summary>
    public AutomationElement? Element;
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
    /// <summary>从目标窗口往下走时最多几层。</summary>
    const int MaxDepth = 16;
    /// <summary>往下走时一层里最多看几个兄弟节点。</summary>
    const int MaxChildren = 400;
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

    // 「这扇窗往下走也拿不到东西」的记账：Chromium 系的窗口连续几次走空之后就跳过它。
    // 每次走都要惊动它的无障碍树（实测会让窗口卡一下、画面黑一瞬），空走一次都不该白花。
    static readonly Dictionary<IntPtr, int> _walkMiss = new();
    /// <summary>同一窗口连续走空几次之后跳过。</summary>
    const int WalkMissLimit = 3;
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
            // 一步命中先试：命中不属于自己时它最快。但选区窗是一张铺满全屏的最顶层遮罩，
            // 开着的时候这里命中的永远是遮罩自己（会吸到底部提示文字那种东西）——
            // 所以一旦发现命中是自己的，就回到「从目标窗口往下走」：枚举窗口时已经跳过了自己。
            var quick = AutomationElement.FromPoint(new Point(physX, physY));
            if (quick != null && !Ours(quick, excludeHwnd))
                result = Pick(quick, physX, physY, sw);

            bool foundTarget = TryTopmostAt(physX, physY, excludeHwnd, out IntPtr targetHwnd);
            Trace.Log($"elemsnap at {physX},{physY} quickOurs={quick != null} target={targetHwnd} got={foundTarget}");
            if (result == null && foundTarget)
            {
                // MSAA 在目标窗口内部命中：这一条不经过全局命中，遮罩在不在最上层都无所谓。
                // Chrome / Electron 的网页内容只走这条路才拿得到（实测与 UIA 无遮罩时结果一致）。
                // 自己进程的窗口：从窗口往下走。这条路对 Chromium 没用（会卡），但自家 WPF 窗口走得通，
                // 而 MSAA 命中没有 UIA 元素、Tab 上钻会断，所以自家的窗仍走 UIA。
                if (IsOurWindow(targetHwnd))
                {
                    result = Walk(targetHwnd, physX, physY, sw);
                    Trace.Log($"elemsnap ours-walk {(result == null ? "null" : result.Rect.ToString())}");
                }
                else
                {
                    // 逐个候选去问，谁先给出说得出口的控件就用谁：
                    // 1) 遮罩下真实的窗口层（Chromium 的网页内容在这一层）
                    // 2) 顶层窗口本身（原生应用在这一层）
                    // 3) 按类名直接找的渲染器窗口
                    // 4) 其余子窗口
                    // 不靠"是不是它的后代"来判断——跨进程的祖先关系与子窗口枚举都可能不可靠。
                    var candidates = new List<IntPtr> { targetHwnd };
                    foreach (var extra in MsaaSnap.Candidates(targetHwnd, physX, physY)) candidates.Add(extra);
                    foreach (var cand in candidates)
                    {
                        // 自己界面的窗口永不参与：那会吸到遮罩自己的提示文字
                        if (cand == IntPtr.Zero || cand == excludeHwnd || IsOurWindow(cand)) continue;
                        var h = MsaaSnap.At(cand, physX, physY);
                        Trace.Log($"elemsnap msaa-try hwnd={cand} -> {(h == null ? "null" : $"role={h.Role} name='{h.Name}' {h.Rect} semantic={MsaaSnap.IsSemantic(h.Role, h.Name)}")}");
                        if (h != null && h.Rect.W >= MinSide && h.Rect.H >= MinSide
                            && MsaaSnap.IsSemantic(h.Role, h.Name) && h.Rect.Contains(physX, physY))
                        {
                            result = new ElementHit { Rect = h.Rect, Element = null };
                            break;
                        }
                    }
                }
            }
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
        var walker = TreeWalker.RawViewWalker;
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

    /// <summary>探针用：把「往下走」每一层选中的节点打出来。生产路径留 null，零开销。</summary>
    public static Action<string>? DebugLog { get; set; }
    /// <summary>这扇窗是不是本进程的。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <returns>属于本进程返回 true。</returns>
    static bool IsOurWindow(IntPtr hwnd)
    {
        try
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return pid == (uint)Environment.ProcessId;
        }
        catch { return false; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    /// <summary>这枚元素是不是我们自己的界面（选区遮罩、弹层）。自己的界面永远不该被吸附。</summary>
    /// <param name="el">UIA 元素。</param>
    /// <param name="excludeHwnd">要排除的窗口句柄，<c>IntPtr.Zero</c> 表示不排除。</param>
    static bool Ours(AutomationElement el, IntPtr excludeHwnd)
    {
        try
        {
            if (el.Current.ProcessId == Environment.ProcessId) return true;
            if (excludeHwnd == IntPtr.Zero) return false;
            IntPtr h = (IntPtr)el.Current.NativeWindowHandle;
            return h != IntPtr.Zero && h == excludeHwnd;
        }
        catch { return false; }
    }

    /// <summary>从目标窗口往下走，找包含光标点的最小控件。
    /// 这条路绕不开：选区遮罩在最顶层，全局命中拿不到遮罩下面的东西。
    /// 走不完预算时不死心——把路径上最后一个「说得出口」的节点交出去，总比给 null 强。</summary>
    /// <param name="hwnd">光标所在窗口（已排除自己的）。</param>
    /// <param name="physX">光标横坐标（物理像素）。</param>
    /// <param name="physY">光标纵坐标（物理像素）。</param>
    /// <param name="sw">本次命中的计时器。</param>
    /// <returns>可用命中；整棵树里都没有说得出口的元素时为 null。</returns>
    static ElementHit? Walk(IntPtr hwnd, int physX, int physY, Stopwatch sw)
    {
        AutomationElement root;
        try { root = AutomationElement.FromHandle(hwnd); } catch { return null; }
        var rootRect = FromUia(root);
        if (rootRect == null || !rootRect.Value.Contains(physX, physY)) return null;

        var walker = TreeWalker.RawViewWalker;
        var cur = root;
        RectI curRect = rootRect.Value;
        ElementHit? lastSemantic = null;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            if (sw.ElapsedMilliseconds > BudgetMs) break;
            AutomationElement? best = null;
            RectI bestRect = default;
            long bestArea = Area(curRect);
            // 用 FindAll 取子节点而不是 TreeWalker：Chrome 的部分节点上 GetFirstChild 直接返回 null
            // （实测走一层就断），FindAll 走的是另一条列举路径，能把内容子树列出来。
            AutomationElementCollection kids;
            try { kids = cur.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition); } catch { break; }
            int seen = 0;
            foreach (AutomationElement child in kids)
            {
                if (++seen > MaxChildren) break;
                var cr = FromUia(child);
                if (DebugLog != null && seen <= 12)
                    DebugLog($"      child {TypeName(child)} name=「{NameOf(child)}」 rect={(cr == null ? "空" : cr.Value.X + "," + cr.Value.Y + " " + cr.Value.W + "x" + cr.Value.H)}");
                if (cr != null && Usable(cr.Value, physX, physY) && Area(cr.Value) < bestArea)
                {
                    best = child;
                    bestRect = cr.Value;
                    bestArea = Area(cr.Value);
                }
            }
            if (best == null) break;
            cur = best;
            curRect = bestRect;
            if (IsSemantic(cur)) lastSemantic = new ElementHit { Rect = curRect, Element = cur };
            DebugLog?.Invoke($"    walk[{depth}] {TypeName(cur)} name='{NameOf(cur)}' rect={curRect.X},{curRect.Y} {curRect.W}x{curRect.H} semantic={IsSemantic(cur)}");
        }
        // 走到的那一格可能是无名容器：退回路径上最近的那个语义节点
        return lastSemantic;
    }

    /// <summary>从命中的控件向上钻一层（Tab 键：控件 → 父容器 → … → 整窗）。</summary>
    /// <param name="hit">上一次命中结果，其 Element 决定起点。</param>
    /// <returns>父容器的新命中；已到根元素、父级边界取不到或 UIA 抛异常时为 null，调用方据此停止上钻。</returns>
    public static ElementHit? Parent(ElementHit hit)
    {
        try
        {
            if (hit.Element == null) return null;   // MSAA 命中没有父级可上钻
        var p = TreeWalker.RawViewWalker.GetParent(hit.Element);
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

    /// <summary>取控件类型名；UIA 抛异常时返回问号。</summary>
    /// <param name="el">UIA 元素。</param>
    /// <returns>类型名。</returns>
    static string TypeName(AutomationElement el)
    {
        try { return el.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); } catch { return "?"; }
    }

    /// <summary>取无障碍名字；UIA 抛异常时返回空串。</summary>
    /// <param name="el">UIA 元素。</param>
    /// <returns>名字。</returns>
    static string NameOf(AutomationElement el)
    {
        try { return el.Current.Name ?? ""; } catch { return ""; }
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
