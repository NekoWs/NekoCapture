namespace FrameCapture.Capture;

/// <summary>带内存预算的帧缓冲。超预算时优先丢弃最老的帧（保留最近画面）。
/// 驱逐规则：<b>索引 0 的首帧永不丢弃</b>，只从索引 1 起丢最老，因此任何时刻至少留 2 帧。
/// 生产录制路径逐帧落盘（<see cref="FrameSpool"/>），不经这里；本类的驱逐语义由 SelfTest 的预算用例锁定。</summary>
public sealed class FrameRing
{
    private readonly List<CapturedFrame> _frames = new();
    private long _bytes;
    private readonly long _budget;

    /// <summary>构造帧环。</summary>
    /// <param name="budgetBytes">内存预算（字节）。null = 取 <see cref="AdaptiveBudget"/>；
    /// 环境变量 <c>FC_TEST_RING_BUDGET_MB</c> 置位时优先于两者，用于把预算钉成确定值以复现边界态。</param>
    public FrameRing(long? budgetBytes = null) => _budget = budgetBytes ?? TestBudget();

    /// <summary>测试专用（与 FC_TEST_NO_FG 同约定）：FC_TEST_RING_BUDGET_MB 把帧预算钉到给定 MB，
    /// 令「⚠内存预算已丢 N 帧」HUD 边界态可确定性触达并做像素取证（UIAUDIT D9）。用户环境永不置位。</summary>
    private static long TestBudget()
    {
        if (long.TryParse(Environment.GetEnvironmentVariable("FC_TEST_RING_BUDGET_MB"), out var mb) && mb > 0) return mb << 20;
        return AdaptiveBudget();
    }

    /// <summary>按当前可用物理内存自适应：avail×40%，夹在 [512MB, 2.5GB]。</summary>
    /// <returns>预算字节。查询可用内存失败时返回 512MB 下限，绝不返回 0（否则每帧都会被当成超预算）。</returns>
    public static long AdaptiveBudget()
    {
        try
        {
            var st = new Native.Win32.MEMORYSTATUSEX();
            if (Native.Win32.GlobalMemoryStatusEx(st) && st.ullAvailPhys > 0)
                return Math.Clamp((long)(st.ullAvailPhys * 0.40), 512L << 20, 2560L << 20);
        }
        catch { }
        return 512L << 20;  // 兜底：查询失败
    }

    /// <summary>因超预算被丢弃的帧数（不含首帧保护）。</summary>
    /// <value>累计只增不减；0 = 从未触发预算。</value>
    public int Dropped { get; private set; }

    /// <summary>当前持有的帧，按到达顺序：索引 0 是首帧（永不驱逐），其余为最近画面。</summary>
    /// <value>实时视图而非快照——后续 <see cref="Add"/> 会就地增删，要稳定副本用 <see cref="Snapshot"/>。</value>
    public IReadOnlyList<CapturedFrame> Frames => _frames;

    /// <summary>追加一帧，随后按预算从索引 1 起驱逐最老的帧。</summary>
    /// <param name="f">新帧；其 <c>Bgra.Length</c> 计入预算，帧本体由本环持有直到被驱逐。</param>
    public void Add(CapturedFrame f)
    {
        _frames.Add(f);
        _bytes += f.Bgra.Length;
        while (_bytes > _budget && _frames.Count > 2)
        {
            // 丢最老（保留第 0 帧作首帧）
            var drop = _frames[1];
            _frames.RemoveAt(1);
            _bytes -= drop.Bgra.Length;
            Dropped++;
        }
    }

    /// <summary>取此刻持有帧的稳定副本。</summary>
    /// <returns>新建的可变列表，与环脱钩——之后的 <see cref="Add"/> 影响不到它，但列表里的帧对象仍与环共享同一份像素。</returns>
    public List<CapturedFrame> Snapshot() => new(_frames);
}
