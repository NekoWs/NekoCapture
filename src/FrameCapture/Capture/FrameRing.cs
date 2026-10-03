namespace FrameCapture.Capture;

/// <summary>带内存预算的帧缓冲。超预算时优先丢弃最老的帧（保留最近画面）。</summary>
public sealed class FrameRing
{
    private readonly List<CapturedFrame> _frames = new();
    private long _bytes;
    private readonly long _budget;

    public FrameRing(long? budgetBytes = null) => _budget = budgetBytes ?? TestBudget();

    /// <summary>测试专用（与 FC_TEST_NO_FG 同约定）：FC_TEST_RING_BUDGET_MB 把帧预算钉到给定 MB，
    /// 令「⚠内存预算已丢 N 帧」HUD 边界态可确定性触达并做像素取证（UIAUDIT D9）。用户环境永不置位。</summary>
    private static long TestBudget()
    {
        if (long.TryParse(Environment.GetEnvironmentVariable("FC_TEST_RING_BUDGET_MB"), out var mb) && mb > 0) return mb << 20;
        return AdaptiveBudget();
    }

    /// <summary>按当前可用物理内存自适应：avail×40%，夹在 [512MB, 2.5GB]。</summary>
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
    public int Dropped { get; private set; }

    public IReadOnlyList<CapturedFrame> Frames => _frames;

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

    public List<CapturedFrame> Snapshot() => new(_frames);
}
