using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using NekoCapture.Capture;

namespace NekoCapture.Core;

/// <summary>
/// 按窗口句柄取无障碍树、再按坐标挑控件——**不经过全局命中**。
///
/// 为什么需要这一层：选区遮罩铺在屏幕最上层，UIA 的 <c>FromPoint</c> 是全局命中，
/// 一直被遮罩挡住。而有些应用（Electron 类）的内容只走 UIA、MSAA 那边是空壳，
/// 于是它们在选区里永远吸不到。它们的内容其实挂在自己渲染器子窗口的树里，
/// 从那个句柄 <c>FromHandle</c> 平铺查后代就能拿到——实测 DeepSeek Harness 上
/// 一根到底 466 个元素，「会话条目」「输入框」都在里面。
///
/// 一根树要 90~150 ms，所以按窗口缓存一份元素矩形快照（约 1.2 秒过期），
/// 每次悬停只在快照里按坐标筛一遍（几百到两千个矩形，1~3 ms）。
/// </summary>
public static class UiaSnap
{
    /// <summary>快照有效期：超过就后台重建，重建期间继续用缓存快照，不卡鼠标。</summary>
    static readonly TimeSpan Ttl = TimeSpan.FromMilliseconds(1200);   // lint-motion:ignore 缓存过期时间，不是动效
    /// <summary>一份快照最多收多少个元素：再多的树，元素本身也太碎，吸了没意义。</summary>
    const int MaxItems = 4000;
    /// <summary>控件的最小边长（物理像素）。</summary>
    const int MinSide = 6;

    /// <summary>快照里的一条：矩形、控件类型、名字。</summary>
    public readonly struct Item
    {
        /// <summary>边界（物理像素、虚拟桌面坐标）。</summary>
        public RectI Rect { get; init; }
        /// <summary>UIA 控件类型。</summary>
        public ControlType Type { get; init; }
        /// <summary>无障碍名字，可能为空。</summary>
        public string Name { get; init; }
        /// <summary>支持后续父容器查询的元素对象。</summary>
        public AutomationElement? Element { get; init; }
    }

    sealed class Snapshot
    {
        public List<Item> Items = new();
        public DateTime At = DateTime.MinValue;
        public bool Building;
    }

    static readonly Dictionary<IntPtr, Snapshot> Cache = new();
    static readonly object Lock = new();

    /// <summary>按窗口取光标下的控件（读缓存，不阻塞）。</summary>
    /// <param name="hwnd">目标窗口（通常是渲染器子窗口）。</param>
    /// <param name="physX">光标横坐标（物理像素）。</param>
    /// <param name="physY">光标纵坐标（物理像素）。</param>
    /// <returns>命中项；快照还没建好或该点上没有语义元素时为 null。</returns>
    public static Item? At(IntPtr hwnd, int physX, int physY)
    {
        if (hwnd == IntPtr.Zero) return null;
        Snapshot? snap;
        lock (Lock)
        {
            if (!Cache.TryGetValue(hwnd, out snap)) { snap = new Snapshot(); Cache[hwnd] = snap; }
        }
        // 第一次就同步建：异步建的话首个悬停必然落空，用户会以为「吸不到」。
        // 一次 150ms 左右，只发生在某个窗口第一次被吸附时。
        if (snap.Items.Count == 0)
        {
            if (snap.Building) WaitBuild(snap, 400);
            if (snap.Items.Count == 0) BuildSync(hwnd, snap);
        }
        var pick = Best(snap.Items, physX, physY);
        // 过期就后台刷新：这次仍用缓存数据，鼠标不会被卡住
        if (!snap.Building && DateTime.UtcNow - snap.At > Ttl) Build(hwnd, snap);
        return pick;
    }

    /// <summary>提前建快照（开选区时对光标下的窗口调一次，之后悬停直接可用）。</summary>
    /// <param name="hwnd">目标窗口。</param>
    public static void Warm(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        Snapshot? snap;
        lock (Lock)
        {
            if (!Cache.TryGetValue(hwnd, out snap)) { snap = new Snapshot(); Cache[hwnd] = snap; }
        }
        lock (Lock)
            if (!snap.Building && DateTime.UtcNow - snap.At > Ttl) Build(hwnd, snap);
    }

    /// <summary>只查询已发布的矩形快照，不等待建树或调用无障碍提供程序。</summary>
    /// <param name="hwnd">目标窗口。</param>
    /// <param name="physX">物理横坐标。</param>
    /// <param name="physY">物理纵坐标。</param>
    /// <returns>缓存中的命中；尚无快照时为空。</returns>
    internal static Item? CachedAt(IntPtr hwnd, int physX, int physY)
    {
        List<Item> items;
        lock (Lock)
        {
            if (!Cache.TryGetValue(hwnd, out var snapshot)) return null;
            items = snapshot.Items;
        }
        return Best(items, physX, physY);
    }

    /// <summary>纯逻辑：在快照里挑「包含该点、面积最小、且说得出口」的元素。</summary>
    /// <param name="items">元素列表。</param>
    /// <param name="physX">光标横坐标。</param>
    /// <param name="physY">光标纵坐标。</param>
    /// <returns>命中的元素矩形与类型；没有合适元素时为 null。</returns>
    public static Item? Best(List<Item> items, int physX, int physY)
    {
        Item? best = null;
        long bestArea = long.MaxValue;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var r = it.Rect;
            if (r.W < MinSide || r.H < MinSide) continue;
            if (!r.Contains(physX, physY)) continue;
            if (!ElementSnap.IsSemantic(it.Type, it.Name)) continue;
            long area = (long)r.W * r.H;
            if (area < bestArea) { bestArea = area; best = it; }
        }
        return best;
    }

    /// <summary>把目标窗口的后代元素抓成一份矩形列表。</summary>
    /// <param name="hwnd">目标窗口。</param>
    /// <returns>元素列表；UIA 抛异常时为空列表。</returns>
    static List<Item> Collect(IntPtr hwnd)
    {
        var items = new List<Item>();
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root == null) return items;
            var request = new CacheRequest();
            request.Add(AutomationElement.BoundingRectangleProperty);
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.NameProperty);
            AutomationElementCollection all;
            using (request.Activate())
                all = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
            int n = Math.Min(all.Count, MaxItems);
            for (int i = 0; i < n; i++)
            {
                var e = all[i];
                try
                {
                    var cached = e.Cached;
                    var rc = cached.BoundingRectangle;
                    if (rc == System.Windows.Rect.Empty || rc.Width <= 0 || rc.Height <= 0) continue;
                    items.Add(new Item
                    {
                        Rect = new RectI((int)rc.X, (int)rc.Y, (int)Math.Round(rc.Width), (int)Math.Round(rc.Height)),
                        Type = cached.ControlType,
                        Name = cached.Name ?? "", Element = e,
                    });
                }
                catch { }
            }
        }
        catch { }
        return items;
    }
    /// <summary>等一次在途的后台建树收尾。</summary>
    /// <param name="snap">快照对象。</param>
    /// <param name="ms">最多等多久。</param>
    static void WaitBuild(Snapshot snap, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (snap.Building && sw.ElapsedMilliseconds < ms) Thread.Sleep(10);
    }

    /// <summary>同步建快照：只在某个窗口第一次被吸附时走一次。</summary>
    /// <param name="hwnd">目标窗口。</param>
    /// <param name="snap">要填的快照对象。</param>
    static void BuildSync(IntPtr hwnd, Snapshot snap)
    {
        snap.Building = true;
        var items = Collect(hwnd);
        lock (Lock)
        {
            snap.Items = items;
            snap.At = DateTime.UtcNow;
            snap.Building = false;
        }
    }
    /// <summary>后台建快照：一根树要 90~150 ms，不能放 UI 线程。</summary>
    /// <param name="hwnd">目标窗口。</param>
    /// <param name="snap">要填的快照对象。</param>
    static void Build(IntPtr hwnd, Snapshot snap)
    {
        snap.Building = true;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var items = Collect(hwnd);
            lock (Lock)
            {
                snap.Items = items;
                snap.At = DateTime.UtcNow;
                snap.Building = false;
            }
            Trace.Log($"uia snap hwnd={hwnd} items={items.Count} dt={sw.ElapsedMilliseconds}ms");
        });
    }
}
