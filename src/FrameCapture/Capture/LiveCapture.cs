using System.Diagnostics;
using FrameCapture.Native;

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
    private int _intervalMs = 33;
    private CapturedFrame? _latest;
    private readonly object _gate = new();
    private bool _disposed;
    private RectI _region; // 虚拟桌面物理坐标

    /// <summary>采样回调（捕获线程）。帧为区域裁剪后的 BGRA。</summary>
    public event Action<CapturedFrame>? FrameSampled;

    public MonitorInfo? TargetMonitor { get; private set; }
    public RectI Region => _region;

    /// <summary>最近一帧（点按单帧用），线程安全。</summary>
    public CapturedFrame? Latest
    {
        get { lock (_gate) return _latest; }
    }

    /// <summary>启动捕获。region 为虚拟桌面物理像素区域。</summary>
    public void Start(MonitorInfo monitor, RectI region, int fps)
    {
        Stop();
        TargetMonitor = monitor;
        _region = region;
        _intervalMs = Math.Max(16, 1000 / Math.Clamp(fps, 5, 120));
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
            nextDue = _clock.ElapsedMilliseconds + _intervalMs;

            var f = Grab(_region);
            if (f != null)
            {
                grabbed++;
                f.TimeMs = _clock.ElapsedMilliseconds;
                lock (_gate) _latest = f;
                FrameSampled?.Invoke(f);
            }
            else failed++;
            if ((grabbed + failed) % 30 == 1)
                FrameCapture.Core.Trace.Log($"loop ok={grabbed} fail={failed}");
        }
        FrameCapture.Core.Trace.Log($"loop exit ok={grabbed} fail={failed}");
    }

    /// <summary>抓取虚拟桌面坐标 region 的一帧 BGRA（任意线程可调）。</summary>
    public static CapturedFrame? Grab(RectI region)
    {
        if (region.W < 2 || region.H < 2) return null;
        IntPtr hsrc = Win32.GetDC(IntPtr.Zero);
        if (hsrc == IntPtr.Zero) return null;
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            var bmi = new Win32.BITMAPINFO
            {
                biSize = Win32.SIZEOF_BITMAPINFOHEADER,
                biWidth = region.W,
                biHeight = -region.H, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Win32.BI_RGB,
            };
            IntPtr bits;
            hbmp = Win32.CreateDIBSection(hsrc, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (hbmp == IntPtr.Zero || bits == IntPtr.Zero)
            {
                FrameCapture.Core.Trace.Log("grab: DIBSection=0 err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                return null;
            }
            IntPtr mem = Win32.CreateCompatibleDC(hsrc);
            if (mem == IntPtr.Zero) { FrameCapture.Core.Trace.Log("grab: CompatDC=0"); return null; }
            IntPtr old = Win32.SelectObject(mem, hbmp);
            try
            {
                if (!Win32.BitBlt(mem, 0, 0, region.W, region.H, hsrc, region.X, region.Y,
                        Win32.SRCCOPY | Win32.CAPTUREBLT))
                {
                    FrameCapture.Core.Trace.Log("grab: BitBlt fail err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    return null;
                }
                Win32.GdiFlush();
                var dst = new byte[(long)region.W * region.H * 4];
                System.Runtime.InteropServices.Marshal.Copy(bits, dst, 0, dst.Length);
                return new CapturedFrame { Bgra = dst, W = region.W, H = region.H, TimeMs = 0 };
            }
            finally
            {
                if (old != IntPtr.Zero) Win32.SelectObject(mem, old);
                Win32.DeleteDC(mem);
            }
        }
        finally
        {
            if (hbmp != IntPtr.Zero) Win32.DeleteObject(hbmp);
            Win32.ReleaseDC(IntPtr.Zero, hsrc);
        }
    }

    public void Stop()
    {
        _running = false;
        _loop?.Join(500);
        _loop = null;
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        Stop();
        lock (_gate) _latest = null;
    }
}
