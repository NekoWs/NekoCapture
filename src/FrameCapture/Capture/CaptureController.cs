using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;

namespace FrameCapture.Services;

/// <summary>捕获状态机。所有方法都在 UI 线程调用（AppHost 已 Dispatch）。</summary>
public sealed partial class CaptureController : IDisposable
{
    /// <summary>
    /// 捕获状态机阶段，<b>唯一</b>写入口是 <see cref="CaptureController"/> 自身，入口方法先读它再决定放行。
    /// <c>Idle</c>=任何捕获入口都可发起、单帧完成路径可开编辑器；<c>SelectingScope</c>=选区窗等待拖框或取消；
    /// <c>Capturing</c>=帧持续入仓，只有 <c>FinishCapture</c> 能离开；<c>Editing</c>=编辑器持有产物所有权（含 spool 文件）。
    /// </summary>
    public enum Phase { Idle, SelectingScope, Capturing, Editing }

    private readonly SettingsService _settings;
    private readonly LiveCapture _live = new();
    private WgcLiveCapture? _wgc;
    private FrameSpool? _spool;
    private RegionPickerWindow? _picker;
    private RegionOutlineWindow? _outline;
    private EditorWindow? _editor;
    private System.Diagnostics.Stopwatch? _captureClock;
    private int _captureFps;   // 本次捕获实际帧率（跟随刷新率时=刷新率），随 seq 交给编辑器算时间轴
    /// <summary>本次捕获的范围与锁定状态：选区录制时由选区窗交来（可能锁定到某个窗口本体），
    /// 热键直接起录时按存好的范围或光标所在屏现造一个。起录时改写成本次实际捕获范围，供收尾开编辑器用。</summary>
    private Selection? _pendingSel;
    /// <summary>录制状态由选区旁的面板显示。</summary>
    private bool _panelRecording;
    /// <summary>选区完成后到真正抓取/起录之间的留白(ms)。选区窗已 WDA_EXCLUDEFROMCAPTURE，抓取本就拍不到它；
    /// 这里只留极短一拍让选区窗 Close 走完（防 WDA 万一失效时把选区界面截进画面）。再长就是纯等，用户能感觉到截图变慢。</summary>
    private const int PickerExitMs = 40;
    private System.Windows.Threading.DispatcherTimer? _recBadgeTimer; // 起录后延迟点亮 REC 徽标（避免快速点按闪徽标）
    private const int RecBadgeDelayMs = 250; // 固定一拍躲开快速点按的闪徽标；录制本身已是确认动作，时长判定归手势引擎
    // 保留最近一次静态截图帧，供托盘「编辑上次截图」在托盘找回编辑器
    private readonly LastStill _lastStill = new();
    // 录制产物按需读取磁盘帧，进入编辑器由 edit.lastvideo 动作触发。
    private CapturedSequence? _lastSequence;
    // 滚动截图——选区后自动检测滚动并拼接（目标窗保持焦点，用户只管滚）
    private Overlays.ScrollHintWindow? _scrollBar;
    private Overlays.ScrollStageWindow? _scrollStage;       // 取景层：范围外压暗 + 范围描边，鼠标穿透
    private Overlays.ScrollPreviewWindow? _scrollPreview;   // 选区右侧的已拼接长图实时预览
    private bool _scrollAuto;   // 「自动滚动」开关：控制条按钮翻转它，注轮节奏由 ScrollTick 驱动
    private byte[]? _scrollAcc;
    private int _scrollW, _scrollH, _scrollSegs;
    private RectI _scrollRegion;
    private string _scrollDevice = "";
    private System.Windows.Threading.DispatcherTimer? _scrollTimer;
    private byte[]? _scrollPrevB;   // 上一 tick 抓帧，用于「滚动已停稳」判定（避免拼接滚动中途的过渡帧）
    /// <summary>点「完成」之后留在手里的半张长图：完成时落盘的那张 PNG，加上那块区域与所在显示器。
    /// 滚早了头、滚少了尾巴是这条路径最常见的失手，托盘「继续上次滚动」据此接着拼而不是重来。
    /// 像素不留在内存里——长图动辄上百 MB，而它本来就已经在磁盘上了。</summary>
    private (string Png, RectI Region, string Device)? _scrollResume;
    // 自动滚动用 SendInput 注真实滚轮，节奏挂在同一个 ScrollTick 上：发一步 → 等这一步被拼上 → 再发下一步
    private bool _autoWaitSettle;   // 已注入一步、正等它落定；落定前不做拼接判定
    private int _autoNoNew;         // 连续「停稳且画面没变」次数，数满判到底
    private int _autoNotch = ScrollAutoPolicy.NotchStart;   // 当前步长（滚轮格数）
    private int _autoStuckTicks;    // 已注一步却迟迟等不到停稳的连续拍数，数满判定这页滚不动
    private const int SettleMaxTicks = 12;   // 停稳等待上限，×280ms ≈ 3.4s：页面一直在动时不能无限沉默
    private bool _autoCursorSaved;  // 光标是否为注轮移进了选区，退出路径据此还原
    private Native.Win32.POINT _autoCursorHome;   // 注轮前的光标位置，物理像素

    // 延时截图——独立托盘动作，倒计时后截当前范围（不碰点按/录制状态机）
    private Overlays.CountdownWindow? _delayWindow;
    private System.Windows.Threading.DispatcherTimer? _delayTimer;
    private int _delayLeft;
    private bool _delayOutlineShown;   // 倒计时期间是否显示了范围高亮，收尾时只收自己显示的

    /// <summary>捕获控制器的当前阶段。</summary>
    public Phase State { get; private set; } = Phase.Idle;

    /// <summary>构造捕获控制器。必须在 UI 线程创建：内部全部使用 <c>DispatcherTimer</c>，且所有方法都按 UI 线程调用设计。</summary>
    /// <param name="settings">设置服务：控制器读它的快照、并往里写已存范围与首次教学标记，其生命周期由调用方持有。</param>
    public CaptureController(SettingsService settings)
    {
        _settings = settings;
        // 只订阅一次：避免多次捕获累积 handler，导致同一帧重复入仓
        _live.FrameSampled += f => _spool?.Add(f);
        // 捕获线程写帧失败（磁盘满、句柄失效）会中止捕获线程并在这里回调，同样只订阅一次
        _live.Faulted += _ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // 捕获线程触发 → 回 UI 线程停录；已录内容照常进编辑器，绝不静默丢帧
            if (State == Phase.Capturing) FinishCapture(openEditor: false);
        });
        _recBadgeTimer = new System.Windows.Threading.DispatcherTimer();
        _recBadgeTimer.Tick += (_, _) =>
        {
            _recBadgeTimer!.Stop();
            if (State == Phase.Capturing) _outline?.StartRecBadge();
        };
    }

    /// <summary>
    /// 启动时调用一次：按设置决定开机是否常显范围轮廓，并订阅设置变更做边沿检测。
    /// </summary>
    public void Initialize()
    {
        _outlineOn = _settings.Current.AlwaysShowRegionOutline;
        if (_outlineOn) ShowOutlineIfStored();
        // 开关的可见副作用必须在拨动瞬间生效：开→立刻显示已存范围轮廓，关→立刻收起。
        // 只在捕获收尾时消费该设置会让"拨了没反应"，用户只能判定开关是坏的。
        _settings.Changed += () =>
        {
            bool on = _settings.Current.AlwaysShowRegionOutline;
            if (on == _outlineOn) return;
            _outlineOn = on;
            if (on) ShowOutlineIfStored(); else HideOutline();
        };
    }

    bool _outlineOn;   // 上次已应用的「常显轮廓」开关态（Changed 边沿检测用）

    #region 设置范围

    private IntPtr _prevFg; // 触发捕获/选区前的前台窗，关闭编辑器后归还焦点

    /// <summary>选区截图这一帧从哪来。优先级：定格瞬间预抓的干净窗口 → 定格帧裁选区 → 实时抓。
    /// 定格开着时绝不实时抓窗口本体，也绝不实时抓矩形：屏幕上定着的就是开框那一帧，重抓会把所见即所得
    /// 变成所见非所得——预抓没排到那只窗，就退一步按矩形裁这一定格帧，拿到的仍是用户看见的画面。</summary>
    /// <param name="freeze">「选区时定格画面」是否开着。</param>
    /// <param name="windowLocked">选区是否吸附到了一只窗口。</param>
    /// <param name="cachedWindow">取定格瞬间预抓到这只窗口的干净画面；没缓存到返回 null。</param>
    /// <param name="liveWindow">实时抓这只窗口的本体；抓不到返回 null。</param>
    /// <param name="frozen">开框瞬间的整屏定格帧；没到位为 null。</param>
    /// <param name="frozenOrigin">定格帧覆盖的显示器范围，用于把选区换算进帧坐标。</param>
    /// <param name="region">选区矩形（物理像素、虚拟桌面绝对坐标）。</param>
    /// <param name="liveRegion">实时抓一块矩形。</param>
    /// <returns>取到的一帧、它对应的矩形，以及这一帧是否按窗口截的。</returns>
    static (CapturedFrame? frame, RectI reg, bool windowCapture) ChooseStillFrame(
        bool freeze, bool windowLocked,
        Func<(CapturedFrame f, RectI r)?> cachedWindow,
        Func<(CapturedFrame f, RectI r)?> liveWindow,
        CapturedFrame? frozen, RectI frozenOrigin, RectI region,
        Func<RectI, CapturedFrame?> liveRegion)
    {
        if (windowLocked)
        {
            var hit = cachedWindow();
            if (hit != null) return (hit.Value.f, hit.Value.r, true);
            if (!freeze)
            {
                var live = liveWindow();
                if (live != null) return (live.Value.f, live.Value.r, true);
            }
        }
        if (frozen != null)
        {
            // 跨屏窗口超出本屏的部分裁掉，矩形同步收缩到实际裁出的范围（轮廓不超框）
            int rx = region.X - frozenOrigin.X, ry = region.Y - frozenOrigin.Y;
            var cut = BgraImage.Crop(frozen, rx, ry, region.W, region.H);
            if (cut != null)
                return (cut, new RectI(frozenOrigin.X + Math.Clamp(rx, 0, frozen.W),
                    frozenOrigin.Y + Math.Clamp(ry, 0, frozen.H), cut.W, cut.H), false);
        }
        return (liveRegion(region), region, false);
    }

    /// <summary>打开选区，产物由选区开关决定。</summary>
    public void BeginScopeSelection() => BeginChain(new[] { GestureActions.Select }, Array.Empty<string>());

    void OpenChainPicker(RunContext context, bool initialRecordMode, bool showModeSwitch = true)
    {
        CloseEditor();
        var cursor = Win32Wrap.GetCursorPosition();
        var monitor = Monitors.Nearest(cursor.X, cursor.Y);
        State = Phase.SelectingScope;
        CaptureToast.DismissCurrent();
        HideOutline();
        var frozen = _settings.Current.FreezeOnSelect ? LiveCapture.Grab(monitor.Bounds) : null;
        _picker = new RegionPickerWindow(monitor,
            existing: _settings.Current.ShowLastRegion ? StoredRegion()?.Bounds : null,
            snapContent: _settings.Current.SnapContentArea, magnifier: _settings.Current.PickerMagnifier,
            colorFormat: Core.ColorFormat.Parse(_settings.Current.ColorCopyFormat),
            windowCapture: true, freeze: _settings.Current.FreezeOnSelect,
            pregrabMax: _settings.Current.FreezePregrabMaxWindows,
            windowShadow: _settings.Current.WindowCaptureShadow, frozenScreen: frozen,
            rightClickCancel: _settings.Current.PickerRightClickCancel, recordMode: initialRecordMode,
            showModeSwitch: showModeSwitch);
        _picker.Result += (region, hwnd) =>
        {
            var picker = _picker;
            _picker = null;
            State = Phase.Idle;
            if (region != null)
            {
                context.Selected = true;
                context.Region = region;
                context.DeviceName = monitor.DeviceName;
                context.Window = hwnd;
                context.RecordMode = picker?.RecordMode == true;
                context.Frozen = picker?.FrozenFrame;
                context.FrozenOrigin = picker?.FrozenOrigin ?? monitor.Bounds;
                context.FrozenWindow = null;
                if (picker != null && picker.TryGetCachedWindow(hwnd, out var frame, out var bounds))
                {
                    context.FrozenWindow = frame;
                    context.FrozenWindowRegion = bounds;
                }
                _settings.Mutate(s =>
                {
                    s.RegionMonitorId = monitor.DeviceName;
                    s.RegionX = region.Value.X; s.RegionY = region.Value.Y;
                    s.RegionW = region.Value.W; s.RegionH = region.Value.H;
                });
            }
            else context.Cancelled = true;
            _selectionCompletion?.TrySetResult(region != null);
        };
        _picker.Show();
        Trace.Log("chain picker open");
    }
    #endregion

    #region 捕获

    /// <summary>真正起录的核心（region = 已存范围，无则光标所在显示器全屏）。
    /// 由手势「开始录制」（<see cref="StartRecording"/>）与选区录制复用；点按/长按的判定在手势引擎，这里只认「起录」。
    /// 起录失败（GDI 也起不来）时回滚 spool 文件并留在 <see cref="Phase.Idle"/>。</summary>
    void BeginCaptureCore()
    {
        if (_disposed) return;
        // 轮廓绝不能入镜：快捷键起录时先把边框收掉；面板录制反之——那一圈正是用户要看的范围，留着
        if (!_panelRecording) HideOutline();

        // 选区录制带着选区交来的状态（可能锁定到某个窗口本体）；热键直接起录则按存好的范围现造一个。
        Selection sel = _pendingSel ?? (StoredRegion() ?? FullMonitorAtCursor()).ToSelection();
        _pendingSel = sel;
        _prevFg = Native.Win32.GetForegroundWindow();
        // 前台是自家窗口（如从托盘再次触发）则无需归还，避免焦点乒乓
        if (_prevFg != IntPtr.Zero && Native.Win32.GetWindowThreadProcessId(_prevFg, out uint fgp) == Native.Win32.GetCurrentThreadId())
            _prevFg = IntPtr.Zero;

        _spool = new FrameSpool();  // 帧逐帧落盘，不驻留内存（容量护栏见 FrameSpool）
        _spool.DiskLow += free => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // 捕获线程触发 → 回 UI 线程停录；已录内容照常进编辑器，绝不静默丢帧
            Trace.Log($"disk low {free >> 20}MB → auto-stop");
            if (State == Phase.Capturing) FinishCapture(openEditor: false);
        });
        var monitor = Monitors.ByDevice(sel.DeviceName) ?? Monitors.Nearest(sel.Bounds.X, sel.Bounds.Y);

        _captureClock = System.Diagnostics.Stopwatch.StartNew();

        var fps = _settings.Current.Fps;
        // 跟随显示器刷新率：开启且检测成功 → 录满该显示器刷新率（如 160Hz→160fps）；否则用手动值。
        if (_settings.Current.FollowMonitorRefresh)
        {
            int hz = Core.DisplayRefresh.RateOf(monitor.DeviceName);
            if (hz > 0) fps = Math.Clamp(hz, Core.AppSettings.FpsMin, Core.AppSettings.FpsMax);
        }
        _captureFps = fps;   // 随 seq 交给编辑器，时间轴按实际帧率而非手动值
        bool wgcOk = false;
        if (_settings.Current.UseWgc)
        {
            // 锁定到窗口就先录窗口本体；起不来就退回录这块矩形，用户至少拿到覆盖那片区域的成片。
            // try 包住调用本身：投影程序集取不到时，异常在编译被调方法的那一刻抛出，落在被调方法自己的 catch 之外。
            try
            {
                if (sel.LockedToWindow) wgcOk = TryStartWgc(sel, monitor, fps);
                if (!wgcOk) wgcOk = TryStartWgc(sel.WithWindowHonor(false), monitor, fps);
            }
            catch (Exception ex) { Trace.Log("wgc unavailable: " + ex.Message.Split('\n')[0] + " → 回退 GDI"); }
        }
        if (!wgcOk)
        {
            try
            {
                _live.Start(monitor, sel.Bounds, fps);
                Trace.Log("capture backend=gdi");
            }
            catch
            {
                _spool?.Abandon();
                _spool = null;
                return;
            }
        }

        // 无录制时长上限——录到用户停为止；护栏是磁盘（FrameSpool.DiskLow 自动停）。
        State = Phase.Capturing;
        Trace.Log("chain recording started");
        // 系统 WGC 提示边框已关闭，改用自家按范围绘制的轮廓做"正在录制"提示。
        // 该窗以 WDA_EXCLUDEFROMCAPTURE 排除，不会进产物；关掉开关即完全不提示。
        if (_settings.Current.ShowRecordingBorder)
        {
            ShowCaptureScope(sel.Bounds);
            // 起录后延迟点亮 REC 徽标——"正在录"从隐性约定变成看得见的状态
            // 面板录制时 REC 点与时长由上方面板说话，右上角那枚不亮
            if (!_panelRecording)
            {
                _recBadgeTimer!.Interval = TimeSpan.FromMilliseconds(RecBadgeDelayMs);
                _recBadgeTimer.Start();
            }
        }
    }

    /// <summary>
    /// 起 WGC 采集后端：句柄非零按窗口本体录，否则按这块矩形录。
    /// WinRT 类型只出现在本方法内，且本方法不被内联——起不来的异常因此不会污染 BeginCaptureCore 的编译，
    /// 调用方还能在 catch 里退回矩形 / GDI。
    /// </summary>
    /// <param name="sel">本次捕获的范围与锁定状态。</param>
    /// <param name="monitor">范围所在显示器。</param>
    /// <param name="fps">目标帧率。</param>
    /// <returns>true = WGC 已在产帧；false = 起不来，调用方按矩形或 GDI 继续。</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    bool TryStartWgc(Selection sel, MonitorInfo monitor, int fps)
    {
        bool byWindow = sel.LockedToWindow;
        WgcLiveCapture? w = null;
        try
        {
            w = new WgcLiveCapture();
            w.FrameSampled += f => _spool?.Add(f);
            if (byWindow)
                w.StartWindow(sel.Window, monitor, fps, _settings.Current.RecordIncludeCursor,
                    _settings.Current.WindowCaptureShadow);
            else w.Start(monitor, sel.Bounds, fps, _settings.Current.RecordIncludeCursor);
            _wgc = w;
            Trace.Log(byWindow ? "capture backend=wgc-window" : "capture backend=wgc");
            return true;
        }
        catch (Exception ex)
        {
            try { w?.Dispose(); } catch { }
            _wgc = null;
            Trace.Log((byWindow ? "wgc window init failed: " : "wgc init failed: ") + ex.Message.Split('\n')[0] // copyok
                + (byWindow ? " → 按区域录" : " → 回退 GDI")); // copyok
            return false;
        }
    }

    /// <summary>手势「开始录制」——直接起录（不经点按/长按状态机）。非空闲态一律拒绝且无副作用。</summary>
    /// <returns>true = 已进入 <see cref="Phase.Capturing"/>。
    /// false = 未起录：当前态不允许，或 WGC 与 GDI 后端都启动失败；调用方可据此顺延。</returns>
    public bool StartRecording()
    {
        if (_disposed || State != Phase.Idle) return false;
        BeginCaptureCore();
        return State == Phase.Capturing;
    }

    /// <summary>手势「停止录制」——停止录制，产物留在内存供『打开编辑器（上次录制）』取用，不进编辑器。</summary>
    /// <returns>true = 已收尾；false = 当前未在录，未产生任何副作用。</returns>
    public bool StopRecording()
    {
        if (State != Phase.Capturing) return false;
        FinishCapture(-1, openEditor: false);
        return true;
    }

    private void FinishCapture(long holdMs = -1, bool openEditor = true)
    {
        // 面板录制结束：上方面板与范围框一起收，标志清掉——下一次起录重新按新流程走
        Overlays.RecordPanelWindow.Dismiss();
        _panelRecording = false;
        Trace.Log("FinishCapture");
        _recBadgeTimer?.Stop();
        // 徽标与轮廓的退场延后到产物就绪（见 RetireCaptureOutline）：录制路径期间先挂「处理中…」，
        // 免得连按两下结束后、编辑器构造那段 UI 冻结期界面空着像没反应。
        _live.Stop();
        long wgcMissed = 0, wgcDelivered = 0, wgcKept = 0;
        if (_wgc != null) { wgcMissed = _wgc.FramesMissed; wgcDelivered = _wgc.Delivered; wgcKept = _wgc.KeptThrottle; try { _wgc.Stop(); _wgc.Dispose(); } catch { } _wgc = null; }

        double durMs = _captureClock?.ElapsedMilliseconds ?? 0;
        // durMs = 起录到收尾的墙钟时长（时钟在 BeginCaptureCore 起跳），既用于帧率统计，也用于「够长才自动开编辑器」。
        // holdMs 是给外部覆盖时长的入口（负值=不覆盖）；当前所有调用方都传 -1，时长一律来自这个时钟。
        if (holdMs >= 0) durMs = holdMs;
        _captureClock = null;
        var spoolList = _spool != null && _spool.Count > 0 ? _spool.OpenReader() : null;
        _spool = null;
        IReadOnlyList<CapturedFrame> frames = spoolList ?? (IReadOnlyList<CapturedFrame>)new List<CapturedFrame>();
        if (durMs > 50)
            Trace.Log($"wgc stats kept={frames.Count} delivered={wgcDelivered} keptThrottle={wgcKept} missed={wgcMissed} dur={durMs:F0}ms fps={frames.Count / (durMs / 1000.0):F1}");
        if (frames.Count == 0)
        {
            // WGC 脏区语义在极短/全静止场景理论可为 0 帧：GDI 补抓一帧兜底，保证“点按必有产物”
            var reg = _pendingSel?.Bounds ?? default;
            if (reg.W > 0 && reg.H > 0)
            {
                try
                {
                    var one = LiveCapture.Grab(reg);
                    if (one != null) { frames = new List<CapturedFrame> { one }; Trace.Log("zero-frame safety net (gdi grab)"); }
                }
                catch { }
            }
            if (frames.Count == 0) { RetireCaptureOutline(); spoolList?.Dispose(); State = Phase.Idle; _recordCompletion?.TrySetResult(false); return; }
        }

        // 点按/长按的判定在手势引擎（Tap→截图动作、Hold/HoldRelease→录制）；
        // 走到这里就是「真录制」收尾：产物一律按录制走，没有「点按→单帧」分支。
        var region = _pendingSel?.Region ?? new ScreenRegion("", frames[0].W > 0 ? new RectI(0, 0, frames[0].W, frames[0].H) : default);
        var monitor = Monitors.ByDevice(region.DeviceName);
        var seq = new CapturedSequence
        {
            Frames = frames,
            Region = region.Bounds,
            MonitorBounds = monitor?.Bounds ?? new RectI(0, 0, frames[0].W, frames[0].H),
            MonitorDevice = region.DeviceName,
            DroppedFrames = 0, // 逐帧落盘，录制路径恒为 0
            Fps = _captureFps, // 实际帧率（跟随刷新率时=刷新率），编辑器时间轴/时长据此而非手动值
        };

        // 产物先暂存内存。开编辑器时所有权归编辑器（_lastSequence 置空，免下次误回收）；
        // 不开编辑器时记为「上次录制」供 edit.lastvideo 取用，并释放上一条从未进编辑器的产物（含 spool 文件）。
        if (!ReferenceEquals(_lastSequence, seq)) (_lastSequence?.Frames as IDisposable)?.Dispose();
        if (openEditor)
        {
            // 编辑器构造 + 首帧解码是 UI 线程同步重活会冻结界面：先把 REC 徽标转「处理中…」并强制刷一帧，
            // 让用户在冻结期看到反馈。⚠ 这句 Dispatcher.Invoke 会泵动消息队列——只在开编辑器分支做：
            // 动作链的继续点位于状态与产物登记之后
            _outline?.SetRecProcessing();
            try { System.Windows.Application.Current?.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render); } catch { }
            _lastSequence = null;
            OpenEditor(seq);
        }
        else { _lastSequence = seq; State = Phase.Idle; }
        RetireCaptureOutline();
        // 产物与空闲状态就绪后再唤醒动作链
        _recordCompletion?.TrySetResult(true);
    }

    /// <summary>产物就绪后停录制徽标，按设置保留范围轮廓或淡出。</summary>
    void RetireCaptureOutline()
    {
        _outline?.StopRecBadge();
        _outline?.SetBorderVisible(true); // 免按住录制的徽标模式复位，别把无边框带给下次
        if (Core.CaptureGuards.OnCaptureFinished(_settings.Current.AlwaysShowRegionOutline, StoredRegion() != null)
            == Core.CaptureGuards.AfterCaptureOutline.KeepPersistent)
            ShowOutlineIfStored();
        else if (_outline != null && _outline.IsVisible)
            _outline.FadeAway(0.25);
    }

    /// <summary>打开编辑器并接管生命周期（关闭→Idle→焦点归还）。调用方负责 seq 组装。</summary>
    void OpenEditor(CapturedSequence seq)
    {
        State = Phase.Editing;
        Trace.Log($"open editor frames={seq.Frames.Count}");
        _editor = new EditorWindow(_settings, seq);
        _editor.ClosedByUser += () =>
        {
            _editor = null;
            State = Phase.Idle;
            // 磁盘暂存帧列表随编辑器释放（删 spool 文件）
            (seq.Frames as IDisposable)?.Dispose();
            // 焦点归还给捕获前正在使用的窗口（测试形态下不做：归还也是一次 SetForegroundWindow）
            try { if (!TestNoForeground && _prevFg != IntPtr.Zero && Native.Win32.IsWindow(_prevFg)) ForegroundNow(_prevFg); } catch { }
            _prevFg = IntPtr.Zero;
            // 窗口拆除后请求优化回收，不等待终结器或压缩整堆。
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                () => Memory.Reclaim(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        // Show 默认 ShowActivated=true，激活本身就是一次抢前台，绕开 ForceForeground 里的开关；
        // 测试形态下必须一起关掉，否则「不抢前台」只对 Activate 那半边成立。
        _editor.ShowActivated = !TestNoForeground;
        _editor.Show();
        ForceForeground(_editor);
    }

    /// <summary>把一次按键直投当前编辑器的分派表。编辑器窗未激活时 WPF 不会路由键盘，
    /// 测试通道经此进入与真实按键同一个入口。调用方是主实例的消息专用窗，
    /// 那条路只在 FC_TEST_NO_FG=1 的测试形态下受理命令。</summary>
    /// <param name="mods">修饰键状态。</param>
    /// <param name="key">WPF 键。</param>
    /// <returns>有编辑器在开并已分派返回真。</returns>
    public bool FeedEditorKey(System.Windows.Input.ModifierKeys mods, System.Windows.Input.Key key)
    {
        if (_editor == null) return false;
        _editor.FeedKeyForTest(mods, key);
        return true;
    }
    #endregion

    #region 轮廓

    void ShowOutlineIfStored()
    {
        var r = StoredRegion();
        if (r != null) ShowOutline(r.Bounds);
    }

    void ShowOutline(RectI region)
    {
        if (!_settings.Current.AlwaysShowRegionOutline) return;
        _outline ??= new RegionOutlineWindow();
        _outline.SetRegion(region);
        _outline.ShowNoActivate();
        Trace.Log($"outline: persistent show {region}");
    }

    void FlashOutline(RectI region)
    {
        _outline ??= new RegionOutlineWindow();
        _outline.SetRegion(region);
        _outline.ShowNoActivate();
        _outline.Flash();
        Trace.Log($"outline: flash {region}");
    }

    void HideOutline()
    {
        _outline?.HideSilently();
        Trace.Log("outline: hide");
    }

    /// <summary>
    /// 录制期间按**真实捕获范围**常亮范围边框（系统那条 WGC 全屏黄框已在后端关掉）。
    /// 轮廓窗走 WDA_EXCLUDEFROMCAPTURE，所以 WGC/GDI 整屏抓也拍不到它。
    /// </summary>
    void ShowCaptureScope(RectI region)
    {
        _outline ??= new RegionOutlineWindow();
        _outline.SetRegion(region);
        _outline.ShowNoActivate();
        _outline.Flash(); // 起录给一次强调，随后由动画回落常态
        Trace.Log("recording scope outline shown req=" + region);
    }

    void CloseEditor()
    {
        if (_editor != null)
        {
            var ed = _editor;
            _editor = null;
            State = Phase.Idle;
            ed.CloseByController();
        }
    }

    /// <summary>
    /// 释放控制器自己持有的产物与后端：上次静图、从未进编辑器的录制序列（含其 spool 文件）、GDI 捕获线程。
    /// 已交给编辑器的产物归编辑器释放，这里不碰。滚动会话还在跑时光标仍被注轮借走，这里负责还回去。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_chainContext != null) _chainContext.Cancelled = true;
        _selectionCompletion?.TrySetResult(false);
        _recordCompletion?.TrySetResult(false);
        _scrollCompletion?.TrySetResult(false);
        _delayCompletion?.TrySetResult(false);
        _editorEvent?.TrySetResult(BehaviorPorts.Cancel);
        _scrollTimer?.Stop();
        _delayTimer?.Stop();
        _recBadgeTimer?.Stop();
        _outline?.HideSilently(); _outline?.Close(); _outline = null;
        if (_picker != null) { _picker.Close(); _picker = null; }
        if (_recordCompletion != null) RecordPanelWindow.Dismiss();
        try { _wgc?.Stop(); _wgc?.Dispose(); } catch { }
        _wgc = null;
        _spool?.Abandon(); _spool = null;
        RestoreAutoCursor();
        _lastStill.Clear(); // 释放保留帧（最大可达数十 MB）
        (_lastSequence?.Frames as IDisposable)?.Dispose(); // 留在内存、从未进编辑器的录制产物
        _lastSequence = null;
        _live.Dispose();
    }
}

static class Win32Wrap
{
    /// <summary>取当前鼠标位置。</summary>
    /// <returns>物理像素、虚拟桌面绝对坐标（与 <see cref="RectI"/> 同一坐标系，非 DIP）。</returns>
    public static (int X, int Y) GetCursorPosition()
    {
        Native.Win32.GetCursorPos(out var p);
        return (p.X, p.Y);
    }
    #endregion

}
