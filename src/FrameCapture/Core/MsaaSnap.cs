using System.Runtime.InteropServices;
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

    // 同一点位的悬停高频重复：按 (x, y) 缓存
    static int _cacheX = int.MinValue, _cacheY;
    static MsaaHit? _cache;

    /// <summary>在指定窗口内部命中光标下的控件。</summary>
    /// <param name="hwnd">目标窗口（已排除自己的选区遮罩）。</param>
    /// <param name="physX">光标横坐标（物理像素）。</param>
    /// <param name="physY">光标纵坐标（物理像素）。</param>
    /// <returns>可用命中；窗口没有暴露无障碍、或命中点上是整页/匿名容器时为 null。</returns>
    public static MsaaHit? At(IntPtr hwnd, int physX, int physY)
    {
        if (hwnd == IntPtr.Zero) return null;
        if (physX == _cacheX && physY == _cacheY) return _cache;

        MsaaHit? result = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var iid = IidAccessible;
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

        _cacheX = physX; _cacheY = physY; _cache = result;
        return result;
    }

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
            or RoleRadioButton or RoleComboBox or RoleTabItem or RoleTab => true,
        // 其余角色：有名字就当控件，没名字不要
        _ => !string.IsNullOrWhiteSpace(name),
    };

    // ROLE_SYSTEM_* 里我们用得到的几个（oleacc.h）
    const int RoleDocument = 0x0F;
    const int RoleWindow = 0x09;
    const int RoleClient = 0x0A;
    const int RolePaneUnnamed = 0x10;
    const int RoleGrouping = 0x14;
    const int RoleTab = 0x12;
    const int RoleRow = 0x13;
    const int RoleCell = 0x1D;
    const int RoleLink = 0x1E;
    const int RoleListItem = 0x22;
    const int RoleOutlineItem = 0x23;
    const int RoleTabItem = 0x25;
    const int RoleGraphic = 0x28;
    const int RoleStaticText = 0x29;
    const int RoleText = 0x2A;
    const int RolePushButton = 0x2B;
    const int RoleCheckButton = 0x2C;
    const int RoleRadioButton = 0x2D;
    const int RoleComboBox = 0x2E;
    const int RoleMenuItem = 0x0C;

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
