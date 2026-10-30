using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

using FrameCapture.Core;

namespace FrameCapture.Capture;

/// <summary>
/// 基于 Windows.Graphics.Capture（WGC）的实时捕获，两种模式：
/// <see cref="Start"/> 按显示器整帧捕获再裁出区域；<see cref="StartWindow"/> 按窗口本体捕获，
/// 帧即窗口（含 DWM 边框与 Win11 圆角），别的窗口盖上来不会进画面。
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
    // 窗口模式：帧即窗口本体，没有区域裁剪。画布取首帧尺寸并全程固定——
    // GIF 的逻辑屏幕描述符只写首帧宽高，中途改尺寸会让产物错位。
    private bool _windowMode;
    private int _canvasW, _canvasH;
    // 窗口模式的合成投影：边距与静图窗口截图同源，投影本体按首帧轮廓算一次、整段复用。
    // 逐帧重算实测 60ms 一帧（30fps 预算 33ms），而窗口轮廓在一段录制里基本不变。
    private int _shadowMargin;
    private byte[]? _shadowOverlay;
    private SizeInt32 _poolSize;
    private string _refreshDevice = "";
    /// <summary>窗口模式首帧定下的画布尺寸；未起或区域模式为 0。</summary>
    /// <value>(宽, 高) 物理像素。产物里每一帧都是这个尺寸。</value>
    public (int W, int H) CanvasSize => (_canvasW, _canvasH);
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
    private long _emptyFrames;  // 诊断：窗口模式收到的 0 宽高空帧数
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
    /// <summary>窗口模式下收到的 0 宽高空帧数。</summary>
    /// <value>这类帧不画进产物也不定画布；它一直涨说明目标窗始终没画出内容。线程安全读。</value>
    public long EmptyFrames => Interlocked.Read(ref _emptyFrames);

    /// <summary>启动捕获。region 为虚拟桌面物理像素区域（必须位于 monitor 内）。
    /// WinRT 初始化放在独立 MTA 线程：WPF UI(STA) 线程上直接激活会死锁。</summary>
    /// <param name="monitor">目标显示器。WGC 只能整显示器捕获，这里用它的 Bounds 当帧原点，再从中裁出 region。</param>
    /// <param name="region">虚拟桌面物理像素区域，须完全落在 <paramref name="monitor"/>.Bounds 内；
    /// 越界不在本方法报错，而是首帧拷贝时抛异常并停掉本后端。</param>
    /// <param name="fps">目标帧率，先钳到 AppSettings 上下限；不低于显示器刷新率时全保不丢帧。</param>
    /// <param name="includeCursor">帧里是否画鼠标指针；系统不支持该开关时按系统默认。</param>
    /// <exception cref="TimeoutException">初始化线程 5s 内没有返回。该线程是后台线程，被弃在 WinRT 调用里也不会拖住进程退出。</exception>
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
            finally { Wake.Set(done); }
        }) { IsBackground = true, Name = "WgcInit" }; // 后台线程：初始化卡在 WinRT 里时也不能拖住进程退出
        initThread.Start();
        if (!done.Wait(5000))
            throw new TimeoutException("wgc init timeout");
        if (initErr != null)
            throw initErr;
    }

    /// <summary>按窗口本体起录。帧就是这一个窗口：别的窗口盖在它上面、它被挪到屏幕另一处，画面里都只有它。
    /// WinRT 初始化放在独立 MTA 线程：WPF UI(STA) 线程上直接激活会死锁。</summary>
    /// <param name="hwnd">目标窗口句柄，须非零。窗口销毁后本后端停止产帧。</param>
    /// <param name="monitor">窗口所在显示器，只用来取刷新率决定「录满还是节流」。</param>
    /// <param name="fps">目标帧率，先钳到 AppSettings 上下限。</param>
    /// <param name="includeCursor">帧里是否画鼠标指针；系统不支持该开关时按系统默认。</param>
    /// <param name="includeShadow">true = 产物四周合成投影（与静图窗口截图同一套算法、同一个边距），
    /// 画布因此向四周各外扩一个边距；false = 画布紧贴窗口本体，不外扩一像素。</param>
    /// <exception cref="ArgumentException"><paramref name="hwnd"/> 为 0。</exception>
    /// <exception cref="TimeoutException">初始化线程 5s 内没有返回。</exception>
    public void StartWindow(IntPtr hwnd, MonitorInfo monitor, int fps, bool includeCursor = true, bool includeShadow = false)
    {
        Stop();
        if (hwnd == IntPtr.Zero) throw new ArgumentException("hwnd=0");
        _windowMode = true;
        _targetFps = Math.Clamp(fps, AppSettings.FpsMin, AppSettings.FpsMax);
        _includeCursor = includeCursor;
        _intervalMs = FpsThrottle.IntervalMs(_targetFps);
        _lastKeptMs = -1;
        _canvasW = _canvasH = 0; // 首帧定画布
        _shadowMargin = includeShadow ? WindowCapture.ShadowMargin(hwnd) : 0;
        _refreshDevice = monitor.DeviceName;

        Exception? initErr = null;
        using var done = new ManualResetEventSlim();
        var initThread = new Thread(() =>
        {
            try { InitWindow(hwnd); }
            catch (Exception e) { initErr = e; }
            finally { Wake.Set(done); }
        }) { IsBackground = true, Name = "WgcInit" }; // 后台线程：初始化卡在 WinRT 里时也不能拖住进程退出
        initThread.Start();
        if (!done.Wait(5000))
            throw new TimeoutException("wgc init timeout");
        if (initErr != null)
            throw initErr;
    }

    private void InitWindow(IntPtr hwnd)
    {
        _deviceAbi = WgcNative.CreateDirect3DDevice();
        _item = WgcNative.CreateItemForWindow(hwnd);

        var insp = new WinRT.IInspectable(WinRT.ComWrappersSupport.GetObjectReferenceForInterface(_deviceAbi));
        _device = WinRT.CastExtensions.As<Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice>(insp);
        _poolSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _poolSize);
        _session = _pool.CreateCaptureSession(_item);
        try { _session.IsCursorCaptureEnabled = _includeCursor; } catch { } // 系统不支持该开关时抛，忽略即保持系统默认
        if (!TrySuppressSystemBorder()) { /* 已记录原因 */ }

        int refresh = DisplayRefresh.RateOf(_refreshDevice);
        _keepAll = refresh > 0 && _targetFps >= refresh;
        Trace.Log($"wgc window fps={_targetFps} refresh={refresh} keepAll={_keepAll} item={_poolSize.Width}x{_poolSize.Height}");
        _pool.FrameArrived += OnFrameArrived;

        _session.StartCapture();

        _running = true;
        _clock = System.Diagnostics.Stopwatch.StartNew();
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
        _poolSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _poolSize);
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
        Trace.Log($"wgc fps={_targetFps} refresh={refresh} keepAll={_keepAll} interval={_intervalMs:F2}ms");
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

            SizeInt32? resized = null;
            try { resized = CopyAndEmit(frame); }
            catch (Exception ex)
            {
                LastLoopError = ex.ToString();
                Trace.Log($"wgc frame: {ex.Message.Split('\n')[0]}");
                _running = false; // 触发回退语义：停止本后端
            }

            // 窗口被拉伸/最大化：帧尺寸变了，池按新尺寸重建，否则新尺寸的帧进不来。
            // 本帧已按画布裁切进产物，产物尺寸全程一致。
            if (resized is { } sz) RecreatePool(sz);
        }
    }

    private SizeInt32? CopyAndEmit(Direct3D11CaptureFrame frame)
    {
        return CopyViaSoftwareBitmap(frame);
    }

    private void Emit(byte[] bgra, int w, int h)
    {
        // 产物契约是 Bgra 长度恒等于 W*H*4：0 宽高或长度对不上的帧不进事件，
        // 否则按 W*H 取像素的下游（暂存、编码、遮挡判定）一律越界。
        if (w < 1 || h < 1 || bgra.Length != (long)w * h * 4) { Interlocked.Increment(ref _emptyFrames); return; }
        double t = _clock.ElapsedMilliseconds;
        FrameSampled?.Invoke(new CapturedFrame { Bgra = bgra, W = w, H = h, TimeMs = t });
    }

    private SizeInt32? CopyViaSoftwareBitmap(Direct3D11CaptureFrame frame)
    {
        using var sb = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).GetAwaiter().GetResult();
        if (sb.BitmapPixelFormat != Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8)
            throw new NotSupportedException($"pixel format {sb.BitmapPixelFormat}");

        int fw = sb.PixelWidth, fh = sb.PixelHeight;
        using var bb = sb.LockBuffer(Windows.Graphics.Imaging.BitmapBufferAccessMode.Read);
        using var reference = bb.CreateReference();
        IntPtr data = WgcNative.ReferenceBytes(reference);

        if (_windowMode)
        {
            // 窗口正在创建或重排时 WGC 会递来 0 宽高的空帧：它不是画面，
            // 既不能拿它定画布，也不能进产物——按 W*H 取像素的下游一律越界。
            if (fw < 1 || fh < 1) { Interlocked.Increment(ref _emptyFrames); return null; }
            CopyWindowFrame(data, fw, fh);
            return fw == _poolSize.Width && fh == _poolSize.Height ? null : new SizeInt32(fw, fh);
        }

        // region 为虚拟桌面绝对坐标，转监视器内坐标（帧覆盖整个监视器）
        int rx = _region.X - _monOrigin.X, ry = _region.Y - _monOrigin.Y;
        if (rx < 0 || ry < 0 || rx + _region.W > fw || ry + _region.H > fh)
            throw new NotSupportedException($"region {_region} outside frame {fw}x{fh}");

        int stride = fw * 4; // SoftwareBitmap(Bgra8) 单平面连续
        var bgra = new byte[_region.W * 4 * _region.H];
        int rowBytes = _region.W * 4;
        for (int y = 0; y < _region.H; y++)
            Marshal.Copy(data + (ry + y) * stride + rx * 4, bgra, y * rowBytes, rowBytes);

        Emit(bgra, _region.W, _region.H);
        return null;
    }

    /// <summary>窗口模式：整帧拷进首帧定下的画布（窗口拉大就裁掉超出部分，缩小则留一圈透明），
    /// 开投影时先把合成好的投影铺在底、本体盖上去。透明处保持透明：GIF 与 WebM 带透明通道，
    /// 不支持透明的容器由编码端按用户选的填充色拍平，不在取帧这一步替用户决定底色。</summary>
    /// <param name="data">帧缓冲首字节指针，归 WGC 所有、只读。</param>
    /// <param name="fw">帧宽（像素），随窗口尺寸变。</param>
    /// <param name="fh">帧高（像素）。</param>
    private void CopyWindowFrame(IntPtr data, int fw, int fh)
    {
        int m = _shadowMargin;
        if (_canvasW == 0) { _canvasW = fw + 2 * m; _canvasH = fh + 2 * m; }
        int cw = _canvasW, ch = _canvasH;
        // Stop 把画布清零与取帧线程并发：清零那一瞬间这一帧已经没有落脚处，丢掉比产出一张 0×0 帧好
        if (cw < 1 || ch < 1) { Interlocked.Increment(ref _emptyFrames); return; }
        int copyW = Math.Min(fw, cw - 2 * m), copyH = Math.Min(fh, ch - 2 * m);
        if (copyW < 1 || copyH < 1) { Interlocked.Increment(ref _emptyFrames); return; }

        var bgra = new byte[cw * 4 * ch];
        if (m > 0)
        {
            // 投影按首帧轮廓合成一次就够：窗口轮廓在一段录制里不变，逐帧重算实测 60ms 一帧。
            _shadowOverlay ??= BuildShadow(cw, ch, data, fw, copyW, copyH, m);
            Buffer.BlockCopy(_shadowOverlay, 0, bgra, 0, bgra.Length);
        }
        int rowBytes = copyW * 4;
        for (int y = 0; y < copyH; y++)
            Marshal.Copy(data + y * fw * 4, bgra, (y + m) * cw * 4 + m * 4, rowBytes);
        Emit(bgra, cw, ch);
    }

    /// <summary>按这一帧的 alpha 轮廓合成投影底图：画布大小，本体落点处留空，由调用方把本体盖上去。</summary>
    /// <param name="cw">画布宽（像素），已含两侧边距。</param>
    /// <param name="ch">画布高（像素），已含两侧边距。</param>
    /// <param name="data">帧缓冲首字节指针，归 WGC 所有、只读。</param>
    /// <param name="fw">帧宽（像素），即源行跨距。</param>
    /// <param name="bw">参与合成的本体宽（像素），画布容不下时为裁切后的宽。</param>
    /// <param name="bh">参与合成的本体高（像素）。</param>
    /// <param name="m">每侧投影边距（物理像素）。</param>
    /// <returns>画布大小的 BGRA：只有投影处有半透明黑，其余全透明。</returns>
    static byte[] BuildShadow(int cw, int ch, IntPtr data, int fw, int bw, int bh, int m)
    {
        var body = new byte[bw * 4 * bh];
        for (int y = 0; y < bh; y++)
            Marshal.Copy(data + y * fw * 4, body, y * bw * 4, bw * 4);
        var dst = new byte[cw * 4 * ch];
        var (radius, offsetY) = WindowCapture.ShadowShape(m);
        WindowCapture.SyntheticShadow(dst, cw, ch, body, bw, bh, m, m, radius,
            WindowCapture.ShadowStrength, offsetY);
        return dst;
    }

    /// <summary>按新帧尺寸重建帧池；画布不变，产物尺寸因此不受窗口缩放影响。</summary>
    /// <param name="size">新的帧尺寸（物理像素），来自实际投递的帧。</param>
    private void RecreatePool(SizeInt32 size)
    {
        try
        {
            _pool?.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, size);
            _poolSize = size;
            Trace.Log($"wgc window pool={size.Width}x{size.Height} canvas={_canvasW}x{_canvasH}");
        }
        catch (Exception ex) { Trace.Log("wgc pool recreate: " + ex.Message.Split('\n')[0]); }
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
                Trace.Log("wgc system border kept: OS ignored IsBorderRequired=false (no Borderless grant)");
                return false;
            }
            SystemBorderSuppressed = true;
            Trace.Log("wgc system border suppressed (IsBorderRequired=false, readback confirms)");
            return true;
        }
        catch (Exception ex)
        {
            Trace.Log(
                "wgc system border kept: " + ex.GetType().Name + " hr=0x" + (ex.HResult & 0xFFFFFFFF).ToString("X8")
                + " " + ex.Message.Split('\n')[0]);
            return false;
        }
    }

    /// <summary>停止捕获并退订、释放池/会话/设备。可重复调用。</summary>
    public void Stop()
    {
        _running = false;
        _windowMode = false;
        _canvasW = _canvasH = 0;
        _shadowMargin = 0;
        _shadowOverlay = null;   // 画布随下一次起录重定，这张投影底图也就没用了
        _poolSize = default;
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
