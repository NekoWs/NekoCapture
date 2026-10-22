using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FrameCapture.Capture;
using FrameCapture.Native;

namespace FrameCapture.Core;

/// <summary>MSAA 命中结果：边界（物理像素）+ 角色 + 名字。没有 UIA 元素，因此不支持上钻。</summary>
public sealed class MsaaHit
{
    /// <summary>边界（物理像素、虚拟桌面绝对坐标）。</summary>
    public RectI Rect;
    /// <summary>MSAA 角色码（<c>ROLE_SYSTEM_*</c>）。</summary>
    public int Role;
    /// <summary>无障碍名字，可能为空。</summary>
    public string Name = "";
    /// <summary>子节点数；&gt; 0 说明这是一个容器，可以再往下钻一层。</summary>
    public int ChildCount;
}

/// <summary>
/// 用 MSAA（<c>IAccessible</c>）在**指定窗口内部**做命中测试。
///
/// 为什么要它：选区遮罩铺在屏幕最上层，UIA 的 <c>ElementFromPoint</c> 是全局命中，
/// 永远命中遮罩自己——网页与 Electron 的内容树又只走那条路，于是浏览器里根本吸不到控件。
/// <c>IAccessible::accHitTest</c> 是问「这扇窗在这一点上是什么」，不经过全局命中，
/// 遮罩在不在上面都无所谓。实测 Chrome 上直接返回「Ask anything…」输入框 1018×126、
/// 另一个按钮 149×60——两路各自量到的矩形一样。
///
/// 角色码 0x2B 是按钮、0x1E 是链接、0x2A 是文本、0x28 是图形……判定规则见 <see cref="IsSemantic"/>。
/// </summary>
public static class MsaaSnap
{
    /// <summary><c>OBJID_CLIENT</c>：取窗口的客户区对象，也就是内容树根。</summary>
    const uint ObjidClient = 0xFFFFFFFC;
    /// <summary>一次命中的时间预算（含 Chromium 冷启动建树）。</summary>
    const int BudgetMs = 400;
    /// <summary>控件的最小边长（物理像素）。</summary>
    const int MinSide = 6;

    /// <summary>探针用：把每次 MSAA 命中尝试打出来。生产路径留 null，零开销。</summary>
    public static Action<string>? DebugLog { get; set; }

    // 已经唤醒过无障碍树的窗口：Chromium 在第一次被问到时才建树，唤醒只做一次
    static readonly HashSet<IntPtr> _warmed = new();

    /// <summary>
    /// 唤醒目标窗口的无障碍树。Chromium 与 Electron 只在被询问时才建无障碍树，
    /// 而选区遮罩让 UIA 的全局命中永远落在遮罩自己身上，目标窗口一次都没被问过——
    /// 于是 MSAA 那边只能看到一具空壳（无名整窗 Pane）。这里显式按句柄问一次。
    /// </summary>
    /// <param name="hwnd">目标窗口。</param>
    static void Warm(IntPtr hwnd)
    {
        lock (_warmed)
        {
            if (!_warmed.Add(hwnd)) return;
        }
        // 放后台：唤醒要走跨进程无障碍握手，几十毫秒，不能卡住鼠标移动。
        // 两条路都要走：只发 UIA 那一路时，Chromium 常常连渲染器子窗口都还没建出来，
        // 于是候选里只剩 D3D 合成层、顶层又只给一个无名整窗 Pane——看起来就像根本没有内容。
        _ = Task.Run(() =>
        {
            try
            {
                var iid = IidAccessible;
                _ = AccessibleObjectFromWindow(hwnd, ObjidClient, ref iid, out object _);
                _ = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                // 握手之后它才会建渲染器子窗口与内容树，给它一点时间
                Thread.Sleep(120);
                DebugLog?.Invoke($"  msaa-warm hwnd={hwnd} rendererAfterWarm={FindRenderer(hwnd)}");
            }
            catch { }
        });
    }
    // 同一点位的悬停高频重复：按 (x, y) 缓存
    static int _cacheX = int.MinValue, _cacheY;
    static IntPtr _cacheHwnd;
    static MsaaHit? _cache;

    /// <summary>在指定窗口内部命中光标下的控件。</summary>
    /// <param name="hwnd">目标窗口（已排除自己的选区遮罩）。</param>
    /// <param name="physX">光标横坐标（物理像素）。</param>
    /// <param name="physY">光标纵坐标（物理像素）。</param>
    /// <returns>可用命中；窗口没有暴露无障碍、或命中点上是整页/匿名容器时为 null。</returns>
    public static MsaaHit? At(IntPtr hwnd, int physX, int physY)
    {
        if (hwnd == IntPtr.Zero) return null;
        if (physX == _cacheX && physY == _cacheY && hwnd == _cacheHwnd) return _cache;

        MsaaHit? result = null;
        Warm(hwnd);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var iid = IidAccessible;
            // 先问顶层窗口；Chromium 与 Electron 的网页内容挂在渲染器子窗口下，
            // 顶层只给一个无名整窗 Pane，所以拿不到说得出口的控件时换子窗口再问一次。
            if (AccessibleObjectFromWindow(hwnd, ObjidClient, ref iid, out object rootObj) == 0 && rootObj is IAccessible root)
            {
                if (root.accHitTest(physX, physY, out object child) == 0 && child != null)
                {
                    result = FromVariant(root, child);
                    // 命中的是容器（比如无名工具栏）：往下再钻一层，挑面积最小的语义孩子
                    for (int depth = 0; depth < 2 && result != null && result.ChildCount > 0
                           && !IsSemantic(result.Role, result.Name) && sw.ElapsedMilliseconds < BudgetMs; depth++)
                    {
                        var deeper = Descend(root, child, result, physX, physY);
                        if (deeper is not { } d) break;
                        result = d.Hit;
                        child = d.Child;
                    }
                }
            }
        }
        catch { result = null; } // 目标进程没暴露 MSAA / 提权进程 / 对象已死，一律当作拿不到

        // 顶层没给出说得出口的控件：换「包含光标点的子窗口」逐层再问，取面积最小的语义命中
        if (result is null || !IsSemantic(result.Role, result.Name))
        {
            long bestArea = result is null ? long.MaxValue : (long)result.Rect.W * result.Rect.H;
            var kids = ChildrenAt(hwnd, physX, physY);
            var renderer = FindRenderer(hwnd);
            DebugLog?.Invoke($"  msaa-children count={kids.Count} renderer={renderer}");
            if (renderer != IntPtr.Zero && !kids.Contains(renderer)) kids.Insert(0, renderer);
            foreach (var childHwnd in kids)
            {
                try
                {
                    var iid2 = IidAccessible;
                    if (AccessibleObjectFromWindow(childHwnd, ObjidClient, ref iid2, out object o2) != 0 || o2 is not IAccessible sub) continue;
                    if (sub.accHitTest(physX, physY, out object c2) != 0 || c2 == null) continue;
                    var h2 = FromVariant(sub, c2);
                    if (h2 == null) continue;
                    DebugLog?.Invoke($"  msaa-child hwnd={childHwnd} role={h2.Role} name='{h2.Name}' " +
                        $"{h2.Rect.X},{h2.Rect.Y} {h2.Rect.W}x{h2.Rect.H} semantic={IsSemantic(h2.Role, h2.Name)}");
                    // 取面积最小、且说得出口的那个：中间层（D3D 合成窗口）常给出有名的大容器，不能先到先得
                    if (!IsSemantic(h2.Role, h2.Name)) continue;
                    long area = (long)h2.Rect.W * h2.Rect.H;
                    if (area < bestArea) { bestArea = area; result = h2; }
                }
                catch { }
            }
        }

        _cacheX = physX; _cacheY = physY; _cacheHwnd = hwnd; _cache = result;
        return result;
    }

    /// <summary>吸附候选窗口：先渲染器（按类名直找），再是包含该点的子窗口。</summary>
    /// <param name="parent">顶层窗口。</param>
    /// <param name="x">物理横坐标。</param>
    /// <param name="y">物理纵坐标。</param>
    /// <returns>候选窗口，按优先级排列；可能为空。</returns>
    internal static List<IntPtr> Candidates(IntPtr parent, int x, int y)
    {
        var list = new List<IntPtr>();
        var renderer = FindRenderer(parent);
        if (renderer != IntPtr.Zero) list.Add(renderer);
        foreach (var child in ChildrenAt(parent, x, y))
            if (!list.Contains(child)) list.Add(child);
        return list;
    }
    /// <summary>在整棵子树里按类名找 Chromium 渲染器窗口。</summary>
    /// <param name="parent">顶层窗口。</param>
    /// <returns>渲染器窗口句柄；没有时为 <c>IntPtr.Zero</c>。</returns>
    static IntPtr FindRenderer(IntPtr parent)
    {
        IntPtr found = IntPtr.Zero;
        try
        {
            EnumChildWindows(parent, (h, _) =>
            {
                if (!IsRenderer(h)) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
        }
        catch { }
        return found;
    }
    /// <summary>是不是 Chromium 系的渲染器窗口——网页内容挂在它下面。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <returns>类名含 <c>Chrome_RenderWidgetHost</c> 时返回 true。</returns>
    static bool IsRenderer(IntPtr hwnd)
    {
        try
        {
            var sb = new System.Text.StringBuilder(128);
            GetClassNameW(hwnd, sb, 128);
            return sb.ToString().Contains("Chrome_RenderWidgetHost", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder name, int max);
    /// <summary>
    /// 问出「这个点上真正属于目标窗口的那一层窗口」。遮罩铺在最上层，直接 WindowFromPoint
    /// 只会拿到遮罩自己；把自己临时设为命中测试透明再问，就能拿到目标窗口的渲染器层。
    /// 只翻一个扩展样式位、问完立刻翻回来，不动可见性也不触发重绘。
    /// </summary>
    /// <param name="overlay">自己的遮罩窗口；<c>IntPtr.Zero</c> 表示不处理。</param>
    /// <param name="x">物理横坐标。</param>
    /// <param name="y">物理纵坐标。</param>
    /// <returns>该点最上层的窗口；全部失败时为 <c>IntPtr.Zero</c>。</returns>
    internal static IntPtr TopWindowUnderOverlay(IntPtr overlay, int x, int y)
    {
        int saved = 0;
        bool flipped = false;
        try
        {
            if (overlay != IntPtr.Zero)
            {
                saved = GetWindowLongW(overlay, GwlExStyle);
                if ((saved & WsExTransparent) == 0)
                {
                    SetWindowLongW(overlay, GwlExStyle, saved | WsExTransparent);
                    flipped = true;
                }
            }
            var pt = new Pt { X = x, Y = y };
            return WindowFromPoint(pt);
        }
        catch { return IntPtr.Zero; }
        finally
        {
            if (flipped)
            {
                try { SetWindowLongW(overlay, GwlExStyle, saved); } catch { }
            }
        }
    }

    /// <summary>
    /// 遮罩临时设为命中测试透明后，用 UIA 的全局命中拿光标下的元素。
    /// 有些应用只暴露 UIA、不暴露 MSAA（Electron 类），MSAA 那边只能给空壳，
    /// 而 UIA 的全局命中又被最上层的遮罩挡住——趁遮罩透明的那一瞬间问，才能看到它。
    /// </summary>
    /// <param name="overlay">自己的遮罩窗口。</param>
    /// <param name="x">物理横坐标。</param>
    /// <param name="y">物理纵坐标。</param>
    /// <returns>光标下的元素；拿不到或落在自己界面上时为 null。</returns>
    internal static System.Windows.Automation.AutomationElement? FromPointThroughOverlay(IntPtr overlay, int x, int y)
    {
        // 光标下的自家窗口不止一扇：选区遮罩之外还有放大镜、提示条这些各占一扇窗，
        // 只把遮罩设成透明，UIA 就会撞到放大镜上——所以本进程的窗口全部临时透明。
        var saved = new List<(IntPtr Hwnd, int Style)>();
        try
        {
            foreach (var h in OwnWindows(overlay))
            {
                int st = GetWindowLongW(h, GwlExStyle);
                if ((st & WsExTransparent) != 0) continue;
                if (SetWindowLongW(h, GwlExStyle, st | WsExTransparent) == 0 && st == 0) continue;
                saved.Add((h, st));
            }
            var el = System.Windows.Automation.AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (el == null) return null;
            if (el.Current.ProcessId == Environment.ProcessId) return null;
            return el;
        }
        catch { return null; }
        finally
        {
            foreach (var (h, st) in saved)
            {
                try { SetWindowLongW(h, GwlExStyle, st); } catch { }
            }
        }
    }

    /// <summary>本进程所有可见的顶层窗口。</summary>
    /// <param name="also">额外包含的窗口（可能还没进枚举）。</param>
    /// <returns>窗口句柄列表。</returns>
    static List<IntPtr> OwnWindows(IntPtr also)
    {
        var list = new List<IntPtr>();
        if (also != IntPtr.Zero) list.Add(also);
        try
        {
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h)) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != (uint)Environment.ProcessId) return true;
                if (!list.Contains(h)) list.Add(h);
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return list;
    }

    delegate bool EnumWinProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWinProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    const int GwlExStyle = -20;
    const int WsExTransparent = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    struct Pt { public int X, Y; }

    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Pt p);
    [DllImport("user32.dll")] static extern int GetWindowLongW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLongW(IntPtr hwnd, int index, int value);
    /// <summary>包含该点的子窗口，按面积从小到大排——网页内容在渲染器那一层，
    /// 而更小的中间层（D3D 合成窗口）没有无障碍对象，所以要逐层试而不是只取最小的一层。</summary>
    /// <param name="parent">父窗口。</param>
    /// <param name="x">物理横坐标。</param>
    /// <param name="y">物理纵坐标。</param>
    /// <returns>候选子窗口，面积升序；一个都没有时为空列表。</returns>
    static List<IntPtr> ChildrenAt(IntPtr parent, int x, int y)
    {
        var list = new List<(IntPtr Hwnd, long Area)>();
        try
        {
            EnumChildWindows(parent, (h, _) =>
            {
                if (!GetWindowRect(h, out var r)) return true;
                if (x < r.L || x >= r.R || y < r.T || y >= r.B) return true;
                list.Add((h, (long)(r.R - r.L) * (r.B - r.T)));
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        // 渲染器窗口优先：网页内容挂在它下面；D3D 中间层面积更小但没有内容
        list.Sort((a, b) =>
        {
            bool ra = IsRenderer(a.Hwnd), rb = IsRenderer(b.Hwnd);
            if (ra != rb) return ra ? -1 : 1;
            return a.Area.CompareTo(b.Area);
        });
        var result = new List<IntPtr>(list.Count);
        foreach (var item in list) result.Add(item.Hwnd);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinRect { public int L, T, R, B; }

    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out WinRect r);

    /// <summary>角色与名字算不算「说得出口的控件」。</summary>
    /// <param name="role">MSAA 角色码。</param>
    /// <param name="name">无障碍名字，可能为空。</param>
    /// <returns>语义角色、或带名字的非整页容器返回 true。</returns>
    public static bool IsSemantic(int role, string? name) => role switch
    {
        // 整页与窗口本身：吸上去等于整窗，不算控件
        RoleDocument or RoleWindow or RoleClient => false,   // 整页与窗口本身：吸上去等于整窗
        // 说得出口的角色
        RolePushButton or RoleLink or RoleText or RoleStaticText or RoleListItem or RoleOutlineItem
            or RoleGraphic or RoleGrouping or RoleCell or RoleRow or RoleMenuItem or RoleCheckButton
            or RoleRadioButton or RoleComboBox or RolePageTab or RoleOutline or RoleTable or RoleList
            or RoleSlider or RoleSpinButton or RoleSplitButton => true,
        // 其余角色：有名字就当控件，没名字不要
        _ => !string.IsNullOrWhiteSpace(name),
    };

    // ROLE_SYSTEM_*（取值见 oleacc.h）
    const int RoleWindow = 0x09;
    const int RoleClient = 0x0A;
    const int RoleMenuItem = 0x0C;
    const int RoleDocument = 0x0F;
    const int RoleGrouping = 0x14;
    const int RoleTable = 0x18;
    const int RoleRow = 0x1C;
    const int RoleCell = 0x1D;
    const int RoleLink = 0x1E;
    const int RoleList = 0x21;
    const int RoleListItem = 0x22;
    const int RoleOutline = 0x23;
    const int RoleOutlineItem = 0x24;
    const int RolePageTab = 0x25;
    const int RoleGraphic = 0x28;
    const int RoleStaticText = 0x29;
    const int RoleText = 0x2A;
    const int RolePushButton = 0x2B;
    const int RoleCheckButton = 0x2C;
    const int RoleRadioButton = 0x2D;
    const int RoleComboBox = 0x2E;
    const int RoleSlider = 0x33;
    const int RoleSpinButton = 0x34;
    const int RoleSplitButton = 0x3E;

    static readonly Guid IidAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    /// <summary>把 <c>accHitTest</c> 的返回值整理成命中结果。</summary>
    /// <param name="root">窗口的无障碍根对象。</param>
    /// <param name="child"><c>accHitTest</c> 给出的子项（int 子 id 或对象）。</param>
    /// <returns>命中结果；取不到位置或尺寸过小时为 null。</returns>
    static MsaaHit? FromVariant(IAccessible root, object child)
    {
        try
        {
            int id = child is int ci ? ci : 0;
            var arg = (object)id;
            if (child is not int)
            {
                // accHitTest 直接给了对象：用它自己的根节点做查询
                if (child is not IAccessible sub) return null;
                return ReadFrom(sub, 0);
            }
            return ReadFrom(root, id);
        }
        catch { return null; }
    }

    /// <summary>读一个对象的角色、名字、位置与子节点数。</summary>
    /// <param name="acc">对象。</param>
    /// <param name="child">子 id；0 表示对象自身。</param>
    /// <returns>命中结果；位置取不到时为 null。</returns>
    static MsaaHit? ReadFrom(IAccessible acc, int child)
    {
        try
        {
            var arg = (object)child;
            string name = "";
            try { acc.get_accName(arg, out name); } catch { }
            int role = 0;
            try { if (acc.get_accRole(arg, out object r) == 0 && r != null) role = Convert.ToInt32(r); } catch { }
            acc.accLocation(out int l, out int t, out int w, out int h, arg);
            if (w < MinSide || h < MinSide) return null;
            int kids = 0;
            if (child == 0) { try { acc.get_accChildCount(out kids); } catch { } }
            return new MsaaHit { Rect = new RectI(l, t, w, h), Role = role, Name = name ?? "", ChildCount = kids };
        }
        catch { return null; }
    }

    /// <summary>容器里再往下钻一层：挑包含光标、面积最小的语义孩子。</summary>
    /// <param name="root">窗口的无障碍根对象。</param>
    /// <param name="child">当前命中项。</param>
    /// <param name="parent">当前命中结果。</param>
    /// <param name="physX">光标横坐标。</param>
    /// <param name="physY">光标纵坐标。</param>
    /// <returns>更小的一层命中；找不到比父级更小的语义孩子时为 null。</returns>
    static (MsaaHit Hit, object Child)? Descend(IAccessible root, object child, MsaaHit parent, int physX, int physY)
    {
        try
        {
            if (child is not int) return null;
            int kids = parent.ChildCount;
            if (kids <= 0 || kids > 600) return null;
            MsaaHit? best = null;
            object bestChild = null!;
            long bestArea = (long)parent.Rect.W * parent.Rect.H;
            for (int id = 1; id <= kids; id++)
            {
                var h = ReadFrom(root, id);
                if (h == null || !h.Rect.Contains(physX, physY)) continue;
                long area = (long)h.Rect.W * h.Rect.H;
                if (area >= bestArea) continue;
                if (!IsSemantic(h.Role, h.Name)) continue;
                best = h;
                bestChild = id;
                bestArea = area;
            }
            return best == null ? null : (best, bestChild);
        }
        catch { return null; }
    }

    /// <summary>把窗口的 MSAA 根对象交出来（供排查用）。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="root">取到的根对象。</param>
    /// <returns>成功返回 true。</returns>
    internal static bool TryRoot(IntPtr hwnd, out IAccessible root)
    {
        root = null!;
        try
        {
            var iid = IidAccessible;
            if (AccessibleObjectFromWindow(hwnd, ObjidClient, ref iid, out object o) != 0) return false;
            root = (IAccessible)o;
            return true;
        }
        catch { return false; }
    }

    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint idObject, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    /// <summary>
    /// MSAA 的 <c>IAccessible</c>。必须按 vtable 顺序把 21 个方法全声明出来——
    /// <c>accHitTest</c> 是 vtable 方法，用 IDispatch 声明它只会得到 DISP_E_MEMBERNOTFOUND。
    /// </summary>
    [ComImport, Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    internal interface IAccessible
    {
        [PreserveSig] int get_accParent([MarshalAs(UnmanagedType.IDispatch)] out object ppdispParent);
        [PreserveSig] int get_accChildCount(out int pcountChildren);
        [PreserveSig] int get_accChild([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.IDispatch)] out object ppdispChild);
        [PreserveSig] int get_accName([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszName);
        [PreserveSig] int get_accValue([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszValue);
        [PreserveSig] int get_accDescription([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszDescription);
        [PreserveSig] int get_accRole([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.Struct)] out object pvarRole);
        [PreserveSig] int get_accState([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.Struct)] out object pvarState);
        [PreserveSig] int get_accHelp([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszHelp);
        [PreserveSig] int get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string pszHelpFile, [MarshalAs(UnmanagedType.Struct)] object varChild, out int pidTopic);
        [PreserveSig] int get_accKeyboardShortcut([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszKeyboardShortcut);
        [PreserveSig] int get_accFocus([MarshalAs(UnmanagedType.Struct)] out object pvarChild);
        [PreserveSig] int get_accSelection([MarshalAs(UnmanagedType.Struct)] out object pvarChildren);
        [PreserveSig] int get_accDefaultAction([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] out string pszDefaultAction);
        [PreserveSig] int accSelect(int flagsSelect, [MarshalAs(UnmanagedType.Struct)] object varChild);
        [PreserveSig] int accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, [MarshalAs(UnmanagedType.Struct)] object varChild);
        [PreserveSig] int accNavigate(int navDir, [MarshalAs(UnmanagedType.Struct)] object varStart, [MarshalAs(UnmanagedType.Struct)] out object pvarEndUpAt);
        [PreserveSig] int accHitTest(int xLeft, int yTop, [MarshalAs(UnmanagedType.Struct)] out object pvarChild);
        [PreserveSig] int accDoDefaultAction([MarshalAs(UnmanagedType.Struct)] object varChild);
        [PreserveSig] int put_accName([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] string szName);
        [PreserveSig] int put_accValue([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] string szValue);
    }
}
