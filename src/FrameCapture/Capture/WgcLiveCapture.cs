using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

using FrameCapture.Core;

namespace FrameCapture.Capture;

/// <summary>
/// 基于 Windows.Graphics.Capture（WGC）的显示器实时捕获。
/// 失败（无 D3D11/策略禁用等）时由调用方捕获异常回退 GDI（LiveCapture）。
/// API 面与 LiveCapture 对齐：Start / Stop / FrameSampled。
/// </summary>
public sealed class WgcLiveCapture : IDisposable
{
    /// <summary>约每 1000/fps 毫秒一帧（帧到达节流）。</summary>
    /// <value>在 WGC 的 FrameArrived 回调线程上同步调用，且调用期间一直持着 <c>_frameGate</c>——回调里做耗时工作会直接丢帧。</value>
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
    private volatile bool _running;
    private System.Diagnostics.Stopwatch _clock = new();
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private IntPtr _deviceAbi; // IDirect3DDevice 的 IUnknown 引用
    private RectI _region;
    private RectI _monOrigin;
    private Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice? _device;
    private double _intervalMs = 33;
    private int _targetFps = 30;
    private bool _includeCursor = true;
    private bool _keepAll;
    private long _lastKeptMs = -1;
    private readonly object _frameGate = new();
    private long _frameLost;
    private long _delivered;   // 诊断：TryGetNextFrame 成功数（= WGC 投递率，与拷贝无关）
    private long _keptThrottle; // 诊断：过节流数（= 采样率）
    /// <summary>取帧线程最后一次异常（null=正常）。测试/诊断用。</summary>
    /// <value>异常完整文本；只在拷贝/解码抛异常时写入，下一次异常覆盖它。</value>
    public string? LastLoopError { get; private set; }
    /// <summary>TryGetNextFrame 空返回次数。</summary>
    /// <value>回调比投递慢时的背压表现，不等于产物丢帧（产物丢帧看节流）。线程安全读。</value>
    public long FramesMissed => _frameLost;
    /// <summary>TryGetNextFrame 成功取到的帧数。</summary>
    /// <value>反映 WGC 的真实投递率，与是否过节流、是否拷贝成功无关。线程安全读。</value>
    public long Delivered => Interlocked.Read(ref _delivered);
    /// <summary>通过帧率节流、真正交给 <see cref="FrameSampled"/> 的帧数。</summary>
    /// <value>即实际采样率；与 <see cref="Delivered"/> 的差就是被节流丢掉的部分。线程安全读。</value>
    public long KeptThrottle => Interlocked.Read(ref _keptThrottle);

    /// <summary>启动捕获。region 为虚拟桌面物理像素区域（必须位于 monitor 内）。
    /// WinRT 初始化放在独立 MTA 线程：WPF UI(STA) 线程上直接激活会死锁。</summary>
    /// <param name="monitor">目标显示器。WGC 只能整显示器捕获，这里用它的 Bounds 当帧原点，再从中裁出 region。</param>
    /// <param name="region">虚拟桌面物理像素区域，须完全落在 <paramref name="monitor"/>.Bounds 内；
    /// 越界不在本方法报错，而是首帧拷贝时抛异常并停掉本后端。</param>
    /// <param name="fps">目标帧率，先钳到 AppSettings 上下限；不低于显示器刷新率时全保不丢帧。</param>
    /// <param name="includeCursor">帧里是否画鼠标指针；系统不支持该开关时按系统默认。</param>
    /// <exception cref="TimeoutException">初始化线程 5s 内没有返回。</exception>
    public void Start(MonitorInfo monitor, RectI region, int fps, bool includeCursor = true)
    {
        Stop();
        _region = region;
        _monOrigin = monitor.Bounds;
        _targetFps = Math.Clamp(fps, AppSettings.FpsMin, AppSettings.FpsMax);
        _includeCursor = includeCursor;
        _intervalMs = FpsThrottle.IntervalMs(_targetFps);
        _lastKeptMs = -1;

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
        try { _session.IsCursorCaptureEnabled = _includeCursor; } catch { } // 系统不支持该开关时抛，忽略即保持系统默认

        // Windows 11 的系统级捕获提示边框（#FFD11C，绕整个被捕获显示器）：
        // 它只按"被捕获对象"画，而 WGC 只能整显示器捕获 —— 于是永远全屏，
        // 与用户选的录制范围无关，且长时间录制期间一直亮着。
        // 关闭它，改由 RegionOutlineWindow 按真实范围显示轮廓（WDA_EXCLUDEFROMCAPTURE 保证不入镜）。
        // 需要系统/用户授权（Win10 或未授权会抛），失败即保持系统默认行为。
        if (!TrySuppressSystemBorder()) { /* 已记录原因 */ }

        // 事件驱动取帧：FrameArrived 按显示器真实刷新节奏回调，不受 Sleep 粒度（~15ms）限制——
        // 自己按间隔轮询会把帧率天花板压在 60fps。目标帧率≥刷新率时全保（录满），否则按时间节流丢帧。
        int refresh = DisplayRefresh.RateOf(monitor.DeviceName);
        _keepAll = refresh > 0 && _targetFps >= refresh;
        FrameCapture.Core.Trace.Log($"wgc fps={_targetFps} refresh={refresh} keepAll={_keepAll} interval={_intervalMs:F2}ms");
        _pool.FrameArrived += OnFrameArrived;

        _session.StartCapture();

        _running = true;
        _clock = System.Diagnostics.Stopwatch.StartNew();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (!_running) return;
        // 串行化：拷贝耗时可能超过帧间隔，重叠回调在锁上排队，池(3 缓冲)自然背压丢帧。
        lock (_frameGate)
        {
            if (!_running) return;
            using var frame = sender.TryGetNextFrame();
            if (frame is null) { Interlocked.Increment(ref _frameLost); return; }
            Interlocked.Increment(ref _delivered);

            long now = _clock.ElapsedMilliseconds;
            if (!FpsThrottle.ShouldKeep(now, _lastKeptMs, _intervalMs, _keepAll))
                return; // 未到采样时刻：丢弃（frame 随 using 释放）
            _lastKeptMs = now;
            Interlocked.Increment(ref _keptThrottle);

            try { CopyAndEmit(frame); }
            catch (Exception ex)
            {
                LastLoopError = ex.ToString();
                FrameCapture.Core.Trace.Log($"wgc frame: {ex.Message.Split('\n')[0]}");
                _running = false; // 触发回退语义：停止本后端
            }
        }
    }

    private void CopyAndEmit(Direct3D11CaptureFrame frame)
    {
        // 实测：无拷贝时 WGC 投递仍 ~47fps（见 PROGRESS），拷贝不是瓶颈，故不做 staging 优化。
        CopyViaSoftwareBitmap(frame);
    }

    private void Emit(byte[] bgra)
    {
        double t = _clock.ElapsedMilliseconds;
        FrameSampled?.Invoke(new CapturedFrame { Bgra = bgra, W = _region.W, H = _region.H, TimeMs = t });
    }

    private void CopyViaSoftwareBitmap(Direct3D11CaptureFrame frame)
    {
        using var sb = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).GetAwaiter().GetResult();
        if (sb.BitmapPixelFormat != Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8)
            throw new NotSupportedException($"pixel format {sb.BitmapPixelFormat}");

        int fw = sb.PixelWidth, fh = sb.PixelHeight;
        // region 为虚拟桌面绝对坐标，转监视器内坐标（帧覆盖整个监视器）
        int rx = _region.X - _monOrigin.X, ry = _region.Y - _monOrigin.Y;
        if (rx < 0 || ry < 0 || rx + _region.W > fw || ry + _region.H > fh)
            throw new NotSupportedException($"region {_region} outside frame {fw}x{fh}");

        int stride = fw * 4; // SoftwareBitmap(Bgra8) 单平面连续
        var bgra = new byte[_region.W * 4 * _region.H];
        using var bb = sb.LockBuffer(Windows.Graphics.Imaging.BitmapBufferAccessMode.Read);
        using var reference = bb.CreateReference();
        IntPtr data = WgcNative.ReferenceBytes(reference);
        int rowBytes = _region.W * 4;
        for (int y = 0; y < _region.H; y++)
            Marshal.Copy(data + (ry + y) * stride + rx * 4, bgra, y * rowBytes, rowBytes);

        Emit(bgra);
    }

    /// <summary>系统提示边框是否已被关闭。</summary>
    /// <value>null = 本系统没有 IGraphicsCaptureSession3（Win10 等），从未尝试；
    /// true = 已关闭，录制期间那条绕整屏的黄色边框不会出现；false = 系统拒绝，保持默认。</value>
    public bool? SystemBorderSuppressed { get; private set; }

    /// <summary>
    /// 尝试关闭 Windows.Graphics.Capture 的系统提示边框。
    /// 这是 Windows 11 的隐私保护特性，关闭需要能力声明 + 用户授权；
    /// 任一条件不满足（Win10 无该接口、未授权被静默忽略）都只记日志并保持系统默认，绝不影响捕获本身。
    /// </summary>
    private bool TrySuppressSystemBorder()
    {
        if (_session is null) return false;
        try
        {
            bool actual = WgcNative.SetBorderRequired(_session, false);
            if (actual)
            {
                // 官方文档：未获 Borderless 授权时 put 会"成功"但值被忽略 —— 回读仍为 true。
                FrameCapture.Core.Trace.Log("wgc system border kept: OS ignored IsBorderRequired=false (no Borderless grant)");
                return false;
            }
            SystemBorderSuppressed = true;
            FrameCapture.Core.Trace.Log("wgc system border suppressed (IsBorderRequired=false, readback confirms)");
            return true;
        }
        catch (Exception ex)
        {
            FrameCapture.Core.Trace.Log(
                "wgc system border kept: " + ex.GetType().Name + " hr=0x" + (ex.HResult & 0xFFFFFFFF).ToString("X8")
                + " " + ex.Message.Split('\n')[0]);
            return false;
        }
    }

    /// <summary>停止捕获并退订、释放池/会话/设备。可重复调用。</summary>
    public void Stop()
    {
        _running = false;
        // 先等在途的 FrameArrived 拷贝结束（持 _frameGate），再退订并释放池——避免回调正用 surface 时被 Dispose。
        lock (_frameGate)
        {
            lock (_gate)
            {
                if (_pool != null) { try { _pool.FrameArrived -= OnFrameArrived; } catch { } }
                try { _session?.Dispose(); } catch { }
                _session = null;
                try { _pool?.Dispose(); } catch { }
                _pool = null;
                _item = null;
                _device = null;
                if (_deviceAbi != IntPtr.Zero) { Marshal.Release(_deviceAbi); _deviceAbi = IntPtr.Zero; }
            }
        }
    }

    /// <summary>等价于 <see cref="Stop"/>：本类不持有非托管资源之外的东西，也没有终值器。</summary>
    public void Dispose() => Stop();
}
