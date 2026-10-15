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
    private ScreenRegion? _pendingRegion;
    /// <summary>点按总是先显示选区：本次选区确认后立即截一帧静态图。</summary>
    private bool _captureAfterSelect;
    /// <summary>「选区录制」——本次选区确认后立即对该区域起录。</summary>
    private bool _recordAfterSelect;
    /// <summary>本条手势的「截图后」（引擎 BeginBinding 设，截图发起时捕获进 <see cref="_sessionAfter"/>）。空=默认弹提示。</summary>
    private string _pendingAfter = "";
    /// <summary>本次截图会话的「截图后」（发起时从 <see cref="_pendingAfter"/> 捕获，完成时分派）。
    /// 单独存一份是为防选区拖框期间触发的别的手势把 _pendingAfter 覆盖掉。</summary>
    private string _sessionAfter = "";
    /// <summary>本条手势的「复制到剪贴板」，发起时捕获进 <see cref="_sessionCopy"/>。</summary>
    private bool _pendingCopy = true;
    /// <summary>本次截图会话要不要写剪贴板。与落点各自独立：贴图、进编辑器都可以不动剪贴板。</summary>
    private bool _sessionCopy = true;
    /// <summary>引擎在跑某条手势动作前告知本条的「截图后」与「复制到剪贴板」。仅记录，真正生效在截图发起时捕获。</summary>
    /// <param name="v">落点名；null 与空串等价，都按「默认弹提示」处理。</param>
    /// <param name="copy">这张图完成后要不要写进剪贴板。</param>
    public void SetPendingAfterCapture(string? v, bool copy = true)
    {
        _pendingAfter = string.IsNullOrEmpty(v) ? "" : v;
        _pendingCopy = copy;
    }
    /// <summary>选区完成后到真正抓取/起录之间的留白(ms)。选区窗已 WDA_EXCLUDEFROMCAPTURE，抓取本就拍不到它；
    /// 这里只留极短一拍让选区窗 Close 走完（防 WDA 万一失效时把选区界面截进画面）。再长就是纯等，用户能感觉到截图变慢。</summary>
    private const int PickerExitMs = 40;
    private System.Windows.Threading.DispatcherTimer? _recBadgeTimer; // 起录后延迟点亮 REC 徽标（避免快速点按闪徽标）
    private const int RecBadgeDelayMs = 250; // 固定一拍躲开快速点按的闪徽标；录制本身已是确认动作，时长判定归手势引擎
    // 保留最近一次静态截图帧，供托盘「编辑上次截图」在托盘找回编辑器
    private readonly LastStill _lastStill = new();
    // 录制产物留在内存（不落盘、不自动开编辑器）。停止录制只暂存，进编辑器由 edit.lastvideo 动作显式触发。
    // 新录制完成会释放上一条（含 spool 文件）；从未进编辑器就再次录制/清空 → 上一条丢弃（用户明确要的"关掉就丢"）。
    private CapturedSequence? _lastSequence;
    // 滚动截图——选区后自动检测滚动并拼接（目标窗保持焦点，用户只管滚）
    private bool _scrollAfterSelect;
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
            if (State == Phase.Capturing) FinishCapture();
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

    /// <summary>
    /// 打开全屏选区窗（热键与托盘共用）。录制中忽略；选取中再按一次只把选取层顶到最前，不重建选区窗。
    /// 结果回调按 <c>_scrollAfterSelect</c> / <c>_recordAfterSelect</c> / <c>_captureAfterSelect</c> 分流到
    /// 滚动拼接、选区起录、单帧静图，都不成立时只记录范围。调用方在返回后置位这些意图标志即可——回调必然晚于返回。
    /// </summary>
    public void BeginScopeSelection()
    {
        // 防状态卡死：选取窗被异常途径关闭（Alt+F4 等）时复位
        if (State == Phase.SelectingScope && _picker == null) State = Phase.Idle;
        var scopeIntent = Core.CaptureGuards.OnScopeHotkey(
            State == Phase.Capturing, State == Phase.SelectingScope, _picker != null);
        if (scopeIntent == Core.CaptureGuards.ScopeIntent.Ignore) return;
        if (scopeIntent == Core.CaptureGuards.ScopeIntent.RefocusPicker)
        {
            // 再按一次热键：把选取层重新顶到所有窗口最前（游戏等霸屏窗口会盖住它）
            _picker!.Show(); // 只顶 Z 序，不抢焦点（原因见本方法末尾）
            return;
        }
        CloseEditor();

        var cursor = Win32Wrap.GetCursorPosition();
        var monitor = Monitors.Nearest(cursor.X, cursor.Y);

        State = Phase.SelectingScope;
        // 进入全屏选区前，收起一切残留半透明浮窗（截图提示浮窗 / 轮廓），否则它们会和选区窗叠着闪几下。
        Overlays.CaptureToast.DismissCurrent();
        HideOutline();
        var swOpen = System.Diagnostics.Stopwatch.StartNew();
        // ShowLastRegion 关=干净屏选区（不传 existing 矩形），此时 Enter「确认上次选区」随之无意义
        _picker = new RegionPickerWindow(monitor, existing: _settings.Current.ShowLastRegion ? StoredRegion()?.Bounds : null,
            snapContent: _settings.Current.SnapContentArea, magnifier: _settings.Current.PickerMagnifier,
            colorFormat: Core.ColorFormat.Parse(_settings.Current.ColorCopyFormat), windowCapture: _settings.Current.WindowCaptureEnabled,
            freeze: _settings.Current.FreezeOnSelect, pregrabMax: _settings.Current.FreezePregrabMaxWindows,
            windowShadow: _settings.Current.WindowCaptureShadow);
        Trace.Log($"picker ctor done dt={swOpen.ElapsedMilliseconds}ms magnifier={_settings.Current.PickerMagnifier}");
        _picker.Result += (region, hwnd) =>
        {
            var pk = _picker; // 定格帧/预抓缓存挂在选区窗上，先留引用（下面 _picker 置空、窗随即关闭）
            _picker = null;
            State = Phase.Idle;
            bool stillAfterSelect = _settings.Current.AutoCaptureOnSelect || _captureAfterSelect;
            _captureAfterSelect = false;
            if (region is null) return;
            _settings.Mutate(s =>
            {
                s.RegionMonitorId = monitor.DeviceName;
                s.RegionX = region.Value.X; s.RegionY = region.Value.Y;
                s.RegionW = region.Value.W; s.RegionH = region.Value.H;
            });
            if (_scrollAfterSelect)
            {
                // 滚动截图：选区即进入步进拼接循环（选区窗已 WDA 排除，只留一拍让 Close 走完，别把选区界面截进首帧）
                _scrollAfterSelect = false;
                HideOutline();
                var sreg = region.Value; var sdev = monitor.DeviceName;
                var st = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PickerExitMs) };
                st.Tick += (_, _) => { st.Stop(); StartScrollLoop(sreg, sdev); };
                st.Start();
                return;
            }
            if (_recordAfterSelect)
            {
                // 选区录制：区域已由上方 Mutate 存好（RegionX/Y/W/H），选区窗已 WDA 排除，只留一拍让 Close 走完即对该区域起录。
                _recordAfterSelect = false;
                HideOutline();
                var rt = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PickerExitMs) };
                rt.Tick += (_, _) => { rt.Stop(); BeginCaptureCore(); };
                rt.Start();
                return;
            }
            if (stillAfterSelect)
            {
                // 选区完成即截一帧**静态图**（PNG + 剪贴板），不进 GIF 管线、不开编辑器。
                // 选区窗已 WDA_EXCLUDEFROMCAPTURE，抓取拍不到它；只留一拍让 Close 走完（防 WDA 万一失效）。
                HideOutline();
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PickerExitMs) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    // /点吸附窗口 → 真·窗口截图（带 alpha）；定格+窗口截图时优先用定格瞬间预抓的干净画面。
                    // 区域截图：定格开 → 裁定格帧（所见即所得，不重抓）；定格关 → 实时重抓。
                    CapturedFrame? f = null;
                    RectI capReg = region.Value;
                    bool winCap = false;
                    if (hwnd != IntPtr.Zero && _settings.Current.WindowCaptureEnabled)
                    {
                        if (pk != null && pk.TryGetCachedWindow(hwnd, out var cf, out var cr))
                        { f = cf; capReg = cr; winCap = true; } // 定格瞬间预抓的干净窗口（定格+干净圆角+投影）
                        else
                        {
                            try
                            {
                                var (wf, wreg) = WindowCapture.Grab(hwnd, _settings.Current.WindowCaptureShadow);
                                f = wf; capReg = wreg; winCap = true;
                            }
                            catch (Exception ex) { Trace.Log($"winCap fail→region: {ex.Message}"); f = null; }
                        }
                    }
                    if (f == null && _settings.Current.FreezeOnSelect && pk != null && pk.FrozenFrame is { } ff)
                    {
                        // 裁自定格帧；跨屏窗口超出本屏部分裁掉，capReg 同步收缩到实际裁出的矩形（轮廓不超框）
                        int ox = pk.FrozenOrigin.X, oy = pk.FrozenOrigin.Y, rx = region.Value.X - ox, ry = region.Value.Y - oy;
                        f = BgraImage.Crop(ff, rx, ry, region.Value.W, region.Value.H);
                        if (f != null) capReg = new RectI(ox + Math.Clamp(rx, 0, ff.W), oy + Math.Clamp(ry, 0, ff.H), f.W, f.H);
                        else f = LiveCapture.Grab(region.Value); // 定格帧没到位（极快点选）→ 实时兜底
                    }
                    if (f == null) f = LiveCapture.Grab(region.Value);
                    if (f != null) DispatchStill(f, capReg, monitor.DeviceName, windowCapture: winCap);
                    if (_settings.Current.AlwaysShowRegionOutline) ShowOutline(capReg);
                    else { FlashOutline(capReg); _outline!.FadeAway(_settings.Current.OutlineFadeSeconds); }
                };
                t.Start();
            }
            else if (_settings.Current.AlwaysShowRegionOutline) ShowOutline(region.Value);
            else
            {
                // 选区确认反馈：亮起→强调停顿→柔和退场，非常显模式下这是唯一的"范围已定"信号
                FlashOutline(region.Value);
                _outline!.FadeAway(_settings.Current.OutlineFadeSeconds);
            }
        };
        _picker.Show();
        // 选区窗不调 ForceForeground：它是非激活窗，抢前台会把焦点从目标窗（如浏览器）夺走，
        // Z 序/前台一变，选区里就截到别的软件。Topmost + ReassertTop 已保证视觉置顶，键走全局钩子代收。
        Trace.Log($"picker Show returned dt={swOpen.ElapsedMilliseconds}ms");
    }
    #endregion

    #region 捕获

    /// <summary>真正起录的核心（region = 已存范围，无则光标所在显示器全屏）。
    /// 由手势「开始录制」（<see cref="StartRecording"/>）与选区录制复用；点按/长按的判定在手势引擎，这里只认「起录」。
    /// 起录失败（GDI 也起不来）时回滚 spool 文件并留在 <see cref="Phase.Idle"/>。</summary>
    void BeginCaptureCore()
    {
        HideOutline(); // 轮廓绝不能入镜：仅真正起录时隐藏

        ScreenRegion region = StoredRegion() ?? FullMonitorAtCursor();
        _pendingRegion = region;
        _prevFg = Native.Win32.GetForegroundWindow();
        // 前台是自家窗口（如从托盘再次触发）则无需归还，避免焦点乒乓
        if (_prevFg != IntPtr.Zero && Native.Win32.GetWindowThreadProcessId(_prevFg, out uint fgp) == Native.Win32.GetCurrentThreadId())
            _prevFg = IntPtr.Zero;

        _spool = new FrameSpool();  // 帧逐帧落盘，不驻留内存（容量护栏见 FrameSpool）
        _spool.DiskLow += free => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // 捕获线程触发 → 回 UI 线程停录；已录内容照常进编辑器，绝不静默丢帧
            Trace.Log($"disk low {free >> 20}MB → auto-stop");
            if (State == Phase.Capturing) FinishCapture();
        });
        var monitor = Monitors.ByDevice(region.DeviceName) ?? Monitors.Nearest(region.Bounds.X, region.Bounds.Y);

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
            try
            {
                _wgc = new WgcLiveCapture();
                _wgc.FrameSampled += f => _spool?.Add(f);
                _wgc.Start(monitor, region.Bounds, fps, _settings.Current.RecordIncludeCursor);
                wgcOk = true;
                Trace.Log("capture backend=wgc");
            }
            catch (Exception ex)
            {
                _wgc?.Dispose();
                _wgc = null;
                Trace.Log("wgc init failed: " + ex.Message.Split('\n')[0] + " → 回退 GDI");
            }
        }
        if (!wgcOk)
        {
            try
            {
                _live.Start(monitor, region.Bounds, fps);
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
        // 系统 WGC 提示边框已关闭，改用自家按范围绘制的轮廓做"正在录制"提示。
        // 该窗以 WDA_EXCLUDEFROMCAPTURE 排除，不会进产物；关掉开关即完全不提示。
        if (_settings.Current.ShowRecordingBorder)
        {
            ShowCaptureScope(region.Bounds);
            // 起录后延迟点亮 REC 徽标——"正在录"从隐性约定变成看得见的状态
            _recBadgeTimer!.Interval = TimeSpan.FromMilliseconds(RecBadgeDelayMs);
            _recBadgeTimer.Start();
        }
    }

    /// <summary>手势「开始录制」——直接起录（不经点按/长按状态机）。非空闲态一律拒绝且无副作用。</summary>
    /// <returns>true = 已进入 <see cref="Phase.Capturing"/>。
    /// false = 未起录：当前态不允许，或 WGC 与 GDI 后端都启动失败；调用方可据此顺延。</returns>
    public bool StartRecording()
    {
        if (State != Phase.Idle) return false;
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
            var reg = _pendingRegion?.Bounds ?? default;
            if (reg.W > 0 && reg.H > 0)
            {
                try
                {
                    var one = LiveCapture.Grab(reg);
                    if (one != null) { frames = new List<CapturedFrame> { one }; Trace.Log("zero-frame safety net (gdi grab)"); }
                }
                catch { }
            }
            if (frames.Count == 0) { RetireCaptureOutline(); spoolList?.Dispose(); State = Phase.Idle; return; }
        }

        // 点按/长按的判定在手势引擎（Tap→截图动作、Hold/HoldRelease→录制）；
        // 走到这里就是「真录制」收尾：产物一律按录制走，没有「点按→单帧」分支。
        var region = _pendingRegion ?? new ScreenRegion("", frames[0].W > 0 ? new RectI(0, 0, frames[0].W, frames[0].H) : default);
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
            // 否则会把「完成后序列」投递的 edit.lastvideo 提前跑（此刻 State 还没置 Idle、_lastSequence 还没存）。
            _outline?.SetRecProcessing();
            try { System.Windows.Application.Current?.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render); } catch { }
            _lastSequence = null;
            OpenEditor(seq);
        }
        else { _lastSequence = seq; State = Phase.Idle; }
        RetireCaptureOutline();
    }

    /// <summary>录制/截图收尾时退场范围提示：停徽标、复位边框，再按「常显轮廓」设置决定保留常显还是淡出。
    /// 从 FinishCapture 开头移到各产物就绪的出口，使录制路径能在编辑器构造前挂住「处理中…」。
    /// 决策见 CaptureGuards.OnCaptureFinished：常显模式且录的是已存范围 → 带回常显轮廓；否则淡出。</summary>
    void RetireCaptureOutline()
    {
        _outline?.StopRecBadge();
        _outline?.SetBorderVisible(true); // 免按住录制的"只徽标"模式复位，别把无边框带给下次
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
            // 焦点归还给捕获前正在使用的窗口
            try { if (_prevFg != IntPtr.Zero && Native.Win32.IsWindow(_prevFg)) ForegroundNow(_prevFg); } catch { }
            _prevFg = IntPtr.Zero;
            // 编辑器位图 LRU（封顶约 600MB）此刻已成垃圾，但 .NET 不把页交还 OS、工作集虚高。
            // 等窗口拆完（ApplicationIdle）再主动全代 GC + 裁工作集，等价用户手动 MemReduct，回到干净基线。
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                () => Memory.Reclaim(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        _editor.Show();
        ForceForeground(_editor);
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
