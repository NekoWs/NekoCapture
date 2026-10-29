using System.Diagnostics;
using FrameCapture.Native;
using FrameCapture.Core;

namespace FrameCapture.Capture;

/// <summary>
/// 基于 GDI BitBlt 的区域实时捕获（DPI 感知下为物理像素，兼容性最好）。
/// 独立线程按 fps 拉帧，回调携带已裁剪的 BGRA 帧。
/// </summary>
public sealed class LiveCapture : IDisposable
{
    private Thread? _loop;
    private volatile bool _running;
    private readonly Stopwatch _clock = new();
    private double _intervalMs = 33;
    private CapturedFrame? _latest;
    private readonly object _gate = new();
    private bool _disposed;
    private RectI _region; // 虚拟桌面物理坐标

    /// <summary>采样回调（捕获线程）。帧为区域裁剪后的 BGRA。</summary>
    /// <value>在名为 LiveCapture 的后台线程上同步调用，回调方不得做耗时操作，需要 UI 副作用要自己投递。</value>
    public event Action<CapturedFrame>? FrameSampled;

    /// <summary>捕获线程因写帧失败而中止时触发（在捕获线程上）。磁盘写满、文件句柄失效都会走到这里。
    /// 订阅方据此收尾；已采到的帧不丢弃，由订阅方决定怎么交付。</summary>
    /// <value>在捕获线程上同步调用，需要 UI 副作用的订阅方自己投递；订阅方抛出的异常被吞掉。</value>
    public event Action<Exception>? Faulted;

    /// <summary>目标显示器，<see cref="Start"/> 时记下，<see cref="Stop"/> 之后仍保留最后一次的值。</summary>
    /// <value>从未启动过时为 null。</value>
    public MonitorInfo? TargetMonitor { get; private set; }
    /// <summary>当前捕获区域。</summary>
    /// <value><see cref="Start"/> 传入的原值（虚拟桌面物理坐标），不做裁剪也不做对齐。</value>
    public RectI Region => _region;

    /// <summary>最近一帧（点按单帧用），线程安全。</summary>
    public CapturedFrame? Latest
    {
        get { lock (_gate) return _latest; }
    }

    /// <summary>启动捕获线程。内部先 <see cref="Stop"/> 上一次，重复调用安全。</summary>
    /// <param name="monitor">目标显示器，仅作记录；实际抓取完全按 <paramref name="region"/> 的虚拟桌面坐标进行。</param>
    /// <param name="region">虚拟桌面物理像素区域，可跨屏。宽或高小于 2 时循环每轮都取不到帧。</param>
    /// <param name="fps">目标帧率，先钳到 AppSettings 的上下限，再换算成双精度采样间隔。</param>
    public void Start(MonitorInfo monitor, RectI region, int fps)
    {
        Stop();
        TargetMonitor = monitor;
        _region = region;
        _intervalMs = FpsThrottle.IntervalMs(Math.Clamp(fps, AppSettings.FpsMin, AppSettings.FpsMax));
        lock (_gate) _latest = null;

        _running = true;
        _clock.Restart();
        _loop = new Thread(Loop) { IsBackground = true, Name = "LiveCapture", Priority = ThreadPriority.AboveNormal };
        _loop.Start();
    }

    private void Loop()
    {
        FrameCapture.Core.Trace.Log($"loop start region={_region} interval={_intervalMs}");
        long grabbed = 0, failed = 0;
        long nextDue = 0;
        while (_running)
        {
            long now = _clock.ElapsedMilliseconds;
            if (now < nextDue)
            {
                Thread.Sleep((int)Math.Min(8, nextDue - now));
                continue;
            }
            nextDue = _clock.ElapsedMilliseconds + (long)Math.Round(_intervalMs);

            var f = Grab(_region);
            if (f != null)
            {
                grabbed++;
                f.TimeMs = _clock.ElapsedMilliseconds;
                lock (_gate) _latest = f;
                try { FrameSampled?.Invoke(f); }
                catch (Exception ex)
                {
                    // 写帧失败若不拦，后台线程异常会终止整个进程，录到现在的帧全丢
                    FrameCapture.Core.Trace.Log($"loop fault {ex.GetType().Name}: {ex.Message}");
                    _running = false;
                    try { Faulted?.Invoke(ex); } catch { }
                }
            }
            else failed++;
            if ((grabbed + failed) % 30 == 1)
                FrameCapture.Core.Trace.Log($"loop ok={grabbed} fail={failed}");
        }
        FrameCapture.Core.Trace.Log($"loop exit ok={grabbed} fail={failed}");
    }

    /// <summary>抓取虚拟桌面坐标 region 的一帧 BGRA（任意线程可调）。
    /// 走 GetDC(NULL) + BitBlt(SRCCOPY | CAPTUREBLT)：CAPTUREBLT 让分层窗口（指针、部分置顶窗）也进画面。
    /// 暂存位图按尺寸缓存复用：全屏抓取里新建一块十几 MB 的 DIB 要花十几毫秒，而它每帧都被整块覆盖。
    /// 缓存只为少建位图，返回值仍是新分配的帧，调用方独占自己的像素。</summary>
    /// <param name="region">物理像素、虚拟桌面绝对坐标；宽或高小于 2 直接返回 null。</param>
    /// <returns>新分配的 BGRA 帧，top-down、无 padding；<c>TimeMs</c> 恒为 0——单帧抓取没有计时基准。
    /// 取屏 DC 失败静默返回 null；建 DIBSection、建兼容 DC、BitBlt 失败各记一条 trace（前者与 BitBlt 带 Win32 错误码）。</returns>
    public static CapturedFrame? Grab(RectI region)
    {
        if (region.W < 2 || region.H < 2) return null;
        IntPtr hsrc = Win32.GetDC(IntPtr.Zero);
        if (hsrc == IntPtr.Zero) return null;
        try
        {
            lock (StageLock)
            {
                if (_stageW != region.W || _stageH != region.H || _stageBmp == IntPtr.Zero)
                {
                    ReleaseStage();
                    var bmi = new Win32.BITMAPINFO
                    {
                        biSize = Win32.SIZEOF_BITMAPINFOHEADER,
                        biWidth = region.W,
                        biHeight = -region.H, // top-down
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = Win32.BI_RGB,
                    };
                    _stageBmp = Win32.CreateDIBSection(hsrc, ref bmi, 0, out _stageBits, IntPtr.Zero, 0);
                    if (_stageBmp == IntPtr.Zero || _stageBits == IntPtr.Zero)
                    {
                        FrameCapture.Core.Trace.Log("grab: DIBSection=0 err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                        ReleaseStage();
                        return null;
                    }
                    _stageDc = Win32.CreateCompatibleDC(hsrc);
                    if (_stageDc == IntPtr.Zero)
                    {
                        FrameCapture.Core.Trace.Log("grab: CompatDC=0");
                        ReleaseStage();
                        return null;
                    }
                    _stageW = region.W; _stageH = region.H;
                }

                IntPtr old = Win32.SelectObject(_stageDc, _stageBmp);
                try
                {
                    if (!Win32.BitBlt(_stageDc, 0, 0, region.W, region.H, hsrc, region.X, region.Y,
                            Win32.SRCCOPY | Win32.CAPTUREBLT))
                    {
                        FrameCapture.Core.Trace.Log("grab: BitBlt fail err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                        return null;
                    }
                    Win32.GdiFlush();
                }
                finally
                {
                    if (old != IntPtr.Zero) Win32.SelectObject(_stageDc, old);
                }
                // 马上被整块覆盖：不初始化，省掉十几 MB 的零填充
                var dst = System.GC.AllocateUninitializedArray<byte>((int)((long)region.W * region.H * 4));
                System.Runtime.InteropServices.Marshal.Copy(_stageBits, dst, 0, dst.Length);
                return new CapturedFrame { Bgra = dst, W = region.W, H = region.H, TimeMs = 0 };
            }
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, hsrc);
        }
    }

    // 暂存位图：只在 Grab 内部用，按尺寸缓存
    static readonly object StageLock = new();
    static IntPtr _stageDc, _stageBmp, _stageBits;
    static int _stageW, _stageH;

    /// <summary>释放暂存位图与兼容 DC。</summary>
    static void ReleaseStage()
    {
        if (_stageDc != IntPtr.Zero) { Win32.DeleteDC(_stageDc); _stageDc = IntPtr.Zero; }
        if (_stageBmp != IntPtr.Zero) { Win32.DeleteObject(_stageBmp); _stageBmp = IntPtr.Zero; }
        _stageBits = IntPtr.Zero;
        _stageW = _stageH = 0;
    }

    /// <summary>空闲时丢弃最近帧并尽力释放共享 GDI 暂存区，不等待其他截图线程。</summary>
    internal void ReleaseIdleBuffers()
    {
        if (_running) return;
        lock (_gate) _latest = null;
        if (!Monitor.TryEnter(StageLock)) return;
        try { ReleaseStage(); } finally { Monitor.Exit(StageLock); }
    }

    /// <summary>停止捕获：置停止位后 Join 等待循环退出，上限 500ms——UI 线程调用时最坏会等这么久。</summary>
    public void Stop()
    {
        _running = false;
        _loop?.Join(500);
        _loop = null;
    }

    /// <summary>停止线程并丢掉最近一帧。重复调用安全。</summary>
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        Stop();
        lock (_gate) _latest = null;
    }
}
