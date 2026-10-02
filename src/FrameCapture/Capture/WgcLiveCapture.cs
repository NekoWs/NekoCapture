using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace FrameCapture.Capture;

/// <summary>
/// 基于 Windows.Graphics.Capture（WGC）的显示器实时捕获。
/// 失败（无 D3D11/策略禁用等）时由调用方捕获异常回退 GDI（LiveCapture）。
/// API 面与 LiveCapture 对齐：Start / Stop / FrameSampled。
/// </summary>
public sealed class WgcLiveCapture : IDisposable
{
    /// <summary>约每 1000/fps 毫秒一帧（帧到达节流）。</summary>
    public event Action<CapturedFrame>? FrameSampled;

    static WgcLiveCapture()
    {
        // apphost 默认探测上下文无法解析 Microsoft.Windows.SDK.NET 投影程序集（SelfTest 等普通 exe 可以）：
        // 使用方类型首次 JIT 前，从输出目录显式 LoadFrom 预加载一次。
        try
        {
            var p = System.IO.Path.Combine(AppContext.BaseDirectory, "Microsoft.Windows.SDK.NET.dll");
            if (System.IO.File.Exists(p)) System.Reflection.Assembly.LoadFrom(p);
        }
        catch { }
    }

    private readonly object _gate = new();
    private Thread? _loop;
    private volatile bool _running;
    private System.Diagnostics.Stopwatch _clock = new();
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private IntPtr _deviceAbi; // IDirect3DDevice 的 IUnknown 引用
    private RectI _region;
    private RectI _monOrigin;
    private Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice? _device;
    private int _intervalMs = 33;
    private long _frameLost;
    /// <summary>取帧线程最后一次异常（null=正常）。测试/诊断用。</summary>
    public string? LastLoopError { get; private set; }
    /// <summary>TryGetNextFrame 空返回次数。</summary>
    public long FramesMissed => _frameLost;

    /// <summary>启动捕获。region 为虚拟桌面物理像素区域（必须位于 monitor 内）。
    /// WinRT 初始化放在独立 MTA 线程：WPF UI(STA) 线程上直接激活会死锁。</summary>
    public void Start(MonitorInfo monitor, RectI region, int fps)
    {
        Stop();
        _region = region;
        _monOrigin = monitor.Bounds;
        _intervalMs = Math.Max(16, 1000 / Math.Clamp(fps, 5, 120));

        Exception? initErr = null;
        using var done = new ManualResetEventSlim();
        var initThread = new Thread(() =>
        {
            try { Init(monitor); }
            catch (Exception e) { initErr = e; }
            finally { done.Set(); }
        }) { IsBackground = false, Name = "WgcInit" };
        initThread.Start();
        if (!done.Wait(5000))
            throw new TimeoutException("wgc init timeout");
        if (initErr != null)
            throw initErr;
    }

    private void Init(MonitorInfo monitor)
    {
        IntPtr hmon = monitor.ResolveHMonitor();
        if (hmon == IntPtr.Zero) throw new InvalidOperationException("hmonitor=0");

        // 1) D3D11 设备 → WinRT IDirect3DDevice
        _deviceAbi = WgcNative.CreateDirect3DDevice();

        // 2) 显示器捕获项
        _item = WgcNative.CreateItemForMonitor(hmon);

        var insp = new WinRT.IInspectable(WinRT.ComWrappersSupport.GetObjectReferenceForInterface(_deviceAbi));
        _device = WinRT.CastExtensions.As<Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice>(insp);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _item.Size);
        _session = _pool.CreateCaptureSession(_item);
        try { _session.IsCursorCaptureEnabled = true; } catch { } // 旧构建无此属性则忽略
        _session.StartCapture();

        _running = true;
        _clock = System.Diagnostics.Stopwatch.StartNew();
        _loop = new Thread(Loop) { IsBackground = true, Name = "WgcLiveCapture", Priority = ThreadPriority.AboveNormal };
        _loop.Start();
    }

    private void Loop()
    {
        long nextDue = 0;
        while (_running)
        {
            int wait = Math.Max(1, _intervalMs - (int)(_clock.ElapsedMilliseconds - nextDue));
            if (wait > _intervalMs) wait = _intervalMs;
            if (!_clock.IsRunning) break;
            Thread.Sleep(wait);
            if (!_running) break;
            nextDue = _clock.ElapsedMilliseconds;

            try
            {
                using var frame = _pool?.TryGetNextFrame();
                if (frame is null) { Interlocked.Increment(ref _frameLost); continue; }

                using var sb = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).GetAwaiter().GetResult();
                if (sb.BitmapPixelFormat != Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8)
                    throw new NotSupportedException($"pixel format {sb.BitmapPixelFormat}");

                int fw = sb.PixelWidth, fh = sb.PixelHeight;
                // region 为虚拟桌面绝对坐标，转监视器内坐标（帧覆盖整个监视器）
                int rx = _region.X - _monOrigin.X, ry = _region.Y - _monOrigin.Y;
                if (rx < 0 || ry < 0 || rx + _region.W > fw || ry + _region.H > fh)
                    throw new NotSupportedException($"region {_region} outside frame {fw}x{fh}");

                int stride = fw * 4; // SoftwareBitmap(Bgra8) 单平面连续
                var full = new byte[stride * fh];
                using var bb = sb.LockBuffer(Windows.Graphics.Imaging.BitmapBufferAccessMode.Read);
                using var reference = bb.CreateReference();
                IntPtr data = WgcNative.ReferenceBytes(reference);
                Marshal.Copy(data, full, 0, full.Length);

                var bgra = new byte[_region.W * 4 * _region.H];
                for (int y = 0; y < _region.H; y++)
                    Buffer.BlockCopy(full, (ry + y) * stride + rx * 4, bgra, y * _region.W * 4, _region.W * 4);

                double t = _clock.ElapsedMilliseconds;
                FrameSampled?.Invoke(new CapturedFrame
                {
                    Bgra = bgra,
                    W = _region.W,
                    H = _region.H,
                    TimeMs = t,
                });
            }
            catch (Exception ex)
            {
                LastLoopError = ex.ToString();
                FrameCapture.Core.Trace.Log($"wgc frame: {ex.Message.Split('\n')[0]}");
                _running = false; // 触发回退语义：停止本后端
            }
        }
    }

    public void Stop()
    {
        _running = false;
        try { _loop?.Join(500); } catch { }
        _loop = null;
        lock (_gate)
        {
            try { _session?.Dispose(); } catch { }
            _session = null;
            try { _pool?.Dispose(); } catch { }
            _pool = null;
            _item = null;
            _device = null;
            if (_deviceAbi != IntPtr.Zero) { Marshal.Release(_deviceAbi); _deviceAbi = IntPtr.Zero; }
        }
    }

    public void Dispose() => Stop();
}
