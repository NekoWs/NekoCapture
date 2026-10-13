using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;

namespace FrameCapture.Services;

/// <summary>捕获状态机。所有方法都在 UI 线程调用（AppHost 已 Dispatch）。</summary>
public sealed class CaptureController : IDisposable
{
    public enum Phase { Idle, SelectingScope, Capturing, Editing }

    private readonly SettingsService _settings;
    private readonly LiveCapture _live = new();
    private WgcLiveCapture? _wgc;
    private FrameSpool? _spool;
    private RegionPickerWindow? _picker;
    private RegionOutlineWindow? _outline;
    private EditorWindow? _editor;
    private System.Diagnostics.Stopwatch? _captureClock;
    private int _captureFps;   // 本次捕获实际帧率（跟随刷新率时=刷新率），随 seq 交给编辑器算时间轴（R119）
    private ScreenRegion? _pendingRegion;
    /// <summary>点按总是先显示选区（QQ 式）：本次选区确认后立即截一帧静态图。</summary>
    private bool _captureAfterSelect;
    /// <summary>R120 B2：QQ 式「选区录制」——本次选区确认后立即对该区域起录。</summary>
    private bool _recordAfterSelect;
    /// <summary>R157：本条手势的「截图后」（引擎 BeginBinding 设，截图发起时捕获进 <see cref="_sessionAfter"/>）。空=默认弹提示。</summary>
    private string _pendingAfter = "";
    /// <summary>R157：本次截图会话的「截图后」（发起时从 <see cref="_pendingAfter"/> 捕获，完成时分派）。
    /// 单独存一份是为防选区拖框期间触发的别的手势把 _pendingAfter 覆盖掉。</summary>
    private string _sessionAfter = "";
    /// <summary>R157：引擎在跑某条手势动作前告知本条的「截图后」（空=默认）。仅记录，真正生效在截图发起时捕获。</summary>
    public void SetPendingAfterCapture(string? v) => _pendingAfter = string.IsNullOrEmpty(v) ? "" : v;
    /// <summary>选区完成后到真正抓取/起录之间的留白(ms)。选区窗已 WDA_EXCLUDEFROMCAPTURE，抓取本就拍不到它；
    /// 这里只留极短一拍让选区窗 Close() 走完（防 WDA 万一失效时把选区界面截进画面）。旧值 220ms 明显拖慢截图。</summary>
    private const int PickerExitMs = 40;
    private System.Windows.Threading.DispatcherTimer? _recBadgeTimer; // R43：起录后延迟点亮 REC 徽标（避免快速点按闪徽标）
    private const int RecBadgeDelayMs = 250; // R126 B2：旧 TapThresholdMs 退役，徽标延迟用固定值（录制已是确认动作）
    // R78：保留最近一次静态截图帧，供托盘「编辑上次截图」在托盘找回编辑器
    private readonly LastStill _lastStill = new();
    // R124：录制产物留在内存（不落盘、不自动开编辑器）。停止录制只暂存，进编辑器由 edit.lastvideo 动作显式触发。
    // 新录制完成会释放上一条（含 spool 文件）；从未进编辑器就再次录制/清空 → 上一条丢弃（用户明确要的"关掉就丢"）。
    private CapturedSequence? _lastSequence;
    // R88：滚动截图——选区后自动检测滚动并拼接（目标窗保持焦点，用户只管滚）
    private bool _scrollAfterSelect;
    private Overlays.ScrollHintWindow? _scrollBar;
    private byte[]? _scrollAcc;
    private int _scrollW, _scrollH, _scrollSegs;
    private RectI _scrollRegion;
    private string _scrollDevice = "";
    private System.Windows.Threading.DispatcherTimer? _scrollTimer;
    private byte[]? _scrollPrevB;   // 上一 tick 抓帧，用于「滚动已停稳」判定（避免拼接滚动中途的过渡帧）

    // R91：延时截图——独立托盘动作，倒计时后截当前范围（不碰点按/录制状态机）
    private Overlays.CountdownWindow? _delayWindow;
    private System.Windows.Threading.DispatcherTimer? _delayTimer;
    private int _delayLeft;
    private bool _delayOutlineShown;   // 倒计时期间是否显示了范围高亮（R119），收尾时只收自己显示的

    public Phase State { get; private set; } = Phase.Idle;

    public CaptureController(SettingsService settings)
    {
        _settings = settings;
        // 只订阅一次：避免多次捕获累积 handler，导致同一帧重复入仓
        _live.FrameSampled += f => _spool?.Add(f);
        _recBadgeTimer = new System.Windows.Threading.DispatcherTimer();
        _recBadgeTimer.Tick += (_, _) =>
        {
            _recBadgeTimer!.Stop();
            if (State == Phase.Capturing)
            {
                _outline?.StartRecBadge();
                if (!_settings.Current.RecHintShown) _settings.Mutate(s => s.RecHintShown = true); // 教过一次就收
            }
        };
    }

    public void Initialize()
    {
        _outlineOn = _settings.Current.AlwaysShowRegionOutline;
        if (_outlineOn) ShowOutlineIfStored();
        // R161 修复「常显捕获区域不生效」：此前开关只在下一次捕获收尾时被消费——拨动瞬间毫无反应，
        // 用户自然认为无效。订阅设置变更做边沿检测：开→立刻显示已存范围轮廓，关→立刻收起。
        _settings.Changed += () =>
        {
            bool on = _settings.Current.AlwaysShowRegionOutline;
            if (on == _outlineOn) return;
            _outlineOn = on;
            if (on) ShowOutlineIfStored(); else HideOutline();
        };
    }

    bool _outlineOn;   // 上次已应用的「常显轮廓」开关态（Changed 边沿检测用）

    // ---------------- 设置范围 ----------------

    private IntPtr _prevFg; // 触发捕获/选区前的前台窗，关闭编辑器后归还焦点

    /// <summary>把窗口推到前台并拿键盘焦点。后台进程直发 SetForegroundWindow 会被前台锁拒绝
    /// （注入按键也不授予前台权限），用 AttachThreadInput 经典链绕开；最后兜底 SwitchToThisWindow。</summary>
    private static void ForegroundNow(System.IntPtr h)
    {
        if (h == System.IntPtr.Zero) return;
        Native.Win32.BringWindowToTop(h);
        if (Native.Win32.SetForegroundWindow(h)) return;
        IntPtr fg = Native.Win32.GetForegroundWindow();
        uint fgTid = Native.Win32.GetWindowThreadProcessId(fg, out _);
        uint curTid = Native.Win32.GetCurrentThreadId();
        if (fgTid != 0 && fgTid != curTid && Native.Win32.AttachThreadInput(curTid, fgTid, true))
        {
            try
            {
                Native.Win32.SetForegroundWindow(h);
                Native.Win32.BringWindowToTop(h);
            }
            finally { Native.Win32.AttachThreadInput(curTid, fgTid, false); }
        }
        if (Native.Win32.GetForegroundWindow() != h)
        {
            Native.Win32.SwitchToThisWindow(h, true);
            if (Native.Win32.GetForegroundWindow() != h)
                FrameCapture.Core.Trace.Log($"foreground denied hwnd={h}");
        }
    }

    private static readonly bool TestNoForeground =
        Environment.GetEnvironmentVariable("FC_TEST_NO_FG") == "1"; // 测试专用：不抢前台（前台机制单独取证）

    private static void ForceForeground(System.Windows.Window w)
    {
        try
        {
            if (TestNoForeground) return;
            w.Activate();
            var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            ForegroundNow(h);
            // 开档 1 秒内有限重试：部分游戏/应用会在窗口弹出后立刻抢回前台；
            // 之后不再强抢（Topmost 仍在视觉上置顶，用户可点击获取焦点），避免焦点战。
            int tries = 3;
            var rt = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            rt.Tick += (_, _) =>
            {
                rt.Stop();
                if (!w.IsLoaded) return;
                if (Native.Win32.GetForegroundWindow() != h)
                {
                    FrameCapture.Core.Trace.Log("foreground re-steal detected, retry");
                    ForegroundNow(h);
                }
                if (--tries > 0 && w.IsLoaded)
                {
                    rt.Start();
                }
                // 重试次数用尽即止
            };
            rt.Start();
        }
        catch { }
    }

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
            _picker!.Show(); // R160：不再 ForceForeground（选区窗不抢焦点，见本方法末尾注释）
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
        // 首次教学（R43）：用户还没录过制时，在选区提示里点破"按住=录制"，起录一次后不再打扰
        string? teachTip = _settings.Current.RecHintShown ? null
            : "提示：按住 = 录制 GIF（点按 = 截图）";
        // 旧选区可关（R55 设置项）：关=干净屏选区，Enter 确认上次选区随之无意义
        _picker = new RegionPickerWindow(monitor, existing: _settings.Current.ShowLastRegion ? StoredRegion()?.Bounds : null,
            snapContent: _settings.Current.SnapContentArea, teachTip: teachTip, magnifier: _settings.Current.PickerMagnifier,
            colorFormat: Core.ColorFormat.Parse(_settings.Current.ColorCopyFormat), windowCapture: _settings.Current.WindowCaptureEnabled,
            freeze: _settings.Current.FreezeOnSelect, pregrabMax: _settings.Current.FreezePregrabMaxWindows,
            windowShadow: _settings.Current.WindowCaptureShadow);
        FrameCapture.Core.Trace.Log($"picker ctor done dt={swOpen.ElapsedMilliseconds}ms magnifier={_settings.Current.PickerMagnifier}");
        _picker.Result += (region, hwnd) =>
        {
            var pk = _picker; // R166：定格帧/预抓缓存挂在选区窗上，先留引用（下面 _picker 置空、窗随即关闭）
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
                // 滚动截图：选区即进入步进拼接循环（选区窗已 WDA 排除，只留一拍让 Close() 走完，别把选区界面截进首帧）
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
                // QQ 式选区录制：区域已由上方 Mutate 存好（RegionX/Y/W/H），选区窗已 WDA 排除，只留一拍让 Close() 走完即对该区域起录。
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
                // 选区窗已 WDA_EXCLUDEFROMCAPTURE，抓取拍不到它；只留一拍让 Close() 走完（防 WDA 万一失效）。
                HideOutline();
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PickerExitMs) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    // R165/R166：点吸附窗口 → 真·窗口截图（带 alpha）；定格+窗口截图时优先用定格瞬间预抓的干净画面。
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
                            catch (Exception ex) { FrameCapture.Core.Trace.Log($"winCap fail→region: {ex.Message}"); f = null; }
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
                // 选区确认反馈：亮起→强调停顿→柔和退场（FlashOutline/FadeAway 此前是死代码）
                FlashOutline(region.Value);
                _outline!.FadeAway(_settings.Current.OutlineFadeSeconds);
            }
        };
        _picker.Show();
        // R160：不再 ForceForeground——选区窗是非激活窗，抢前台会把焦点从目标窗（如浏览器）夺走，
        // Z 序/前台一变，选区里就截到别的软件。Topmost + ReassertTop 已保证视觉置顶，键走全局钩子代收。
        FrameCapture.Core.Trace.Log($"picker Show returned dt={swOpen.ElapsedMilliseconds}ms");
    }

    // ---------- R88 滚动截图 ----------
    /// <summary>托盘/入口调用：复用选区流程选一块要滚的区域，选完进入步进拼接循环。</summary>
    public void BeginScrollCapture()
    {
        FrameCapture.Core.Trace.Log("scroll capture→picker");
        _sessionAfter = _pendingAfter; _pendingAfter = "";   // R157：捕获本条手势的截图后
        BeginScopeSelection();
        _scrollAfterSelect = true;
    }

    /// <summary>R120 B2：QQ 式「选区截图」——选一块区域，选完立即截静态图（不受 AutoCaptureOnSelect 影响）。</summary>
    public void BeginCaptureSelection()
    {
        FrameCapture.Core.Trace.Log("capture-selection→picker");
        _sessionAfter = _pendingAfter; _pendingAfter = "";   // R157：捕获本条手势的截图后
        BeginScopeSelection();
        _captureAfterSelect = true;
    }

    /// <summary>R120 B2：QQ 式「选区录制」——选一块区域，选完立即对该区域起录。</summary>
    public void BeginRecordSelection()
    {
        FrameCapture.Core.Trace.Log("record-selection→picker");
        BeginScopeSelection();
        _recordAfterSelect = true;
    }

    /// <summary>R120 B2 / R124：录制中→停止（产物留内存，不进编辑器）；否则→选区起录。QQ 式「同键开始/结束」。
    /// 进编辑器请在「完成后序列」加『打开编辑器（上次录制）』——动作本身不再默认开编辑器。</summary>
    public void ToggleRecordSelection()
    {
        if (State == Phase.Capturing)
        {
            FrameCapture.Core.Trace.Log("record-toggle→stop");
            FinishCapture(-1, openEditor: false);
            return;
        }
        BeginRecordSelection();
    }

    /// <summary>R120 B2 / R123 A2：干净利落的全屏即时截图——直接抓光标所在屏幕全屏当下这一帧，无选区、无点按窗、
    /// 无录制管线、无延时、抓取前不弹任何窗（不抢焦点）。专治游戏「一丢焦点就暂停」：按下即定格此刻画面。
    /// （此前会优先抓"上次存的范围"，导致明明要全屏却截到残留选区——A2 改为永远全屏，语义清晰。）</summary>
    public void CaptureInstantStill()
    {
        if (State != Phase.Idle) return;
        _sessionAfter = _pendingAfter; _pendingAfter = "";   // R157：捕获本条手势的截图后
        var region = FullMonitorAtCursor();
        var f = LiveCapture.Grab(region.Bounds);
        if (f == null) { FrameCapture.Core.Trace.Log("instant still: grab null"); return; }
        FrameCapture.Core.Trace.Log($"instant still grab {f.W}x{f.H}");
        DispatchStill(f, region.Bounds, region.DeviceName);
    }

    /// <summary>R126 B2：托盘「立即捕获（按当前范围）」——对已存范围（无则光标屏全屏）截一帧静态图，
    /// 走统一「截图后」分派。替代旧 BeginCapture+600ms+EndCapture 的绕路（那会产出一段 600ms GIF，名不副实）。</summary>
    public void CaptureStoredStill()
    {
        if (State != Phase.Idle) return;
        _sessionAfter = _pendingAfter; _pendingAfter = "";   // R157：捕获本条手势的截图后（托盘触发时 _pendingAfter 已空=默认弹提示）
        var region = StoredRegion() ?? FullMonitorAtCursor();
        var f = LiveCapture.Grab(region.Bounds);
        if (f == null) { FrameCapture.Core.Trace.Log("stored still: grab null"); return; }
        FrameCapture.Core.Trace.Log($"stored still grab {f.W}x{f.H}");
        DispatchStill(f, region.Bounds, region.DeviceName);
    }

    /// <summary>R148：手势「识别文字」——对已存范围（无则光标屏全屏）抓一帧做 OCR，结果按 <see cref="OcrService.Route"/> 分流：
    /// 结果窗 / 复制+轻确认 / 无引擎提示 / 空提示（反馈统一走可截图审查的 OcrResultWindow）。
    /// 帧像素在首个 await 前同步拷入 SoftwareBitmap，与关窗/释放无竞态；无语言包优雅降级不抛。</summary>
    public async void OcrStoredStill()
    {
        if (State != Phase.Idle) return;
        var region = StoredRegion() ?? FullMonitorAtCursor();
        var f = LiveCapture.Grab(region.Bounds);
        if (f == null) { FrameCapture.Core.Trace.Log("ocr-still: grab null"); return; }
        try
        {
            var outcome = await OcrService.RecognizeAsync(f);
            var text = OcrService.Normalize(outcome.Text);
            switch (OcrService.Route(outcome.Status, _settings.Current.OcrShowResult))
            {
                case OcrRoute.ResultWindow:
                    OcrResultWindow.Show(text);
                    FrameCapture.Core.Trace.Log($"ocr-still ok {text.Length}");
                    break;
                case OcrRoute.CopyThenConfirm:
                    try { System.Windows.Clipboard.SetText(text); } catch { }
                    OcrResultWindow.ShowMessage(OcrService.Feedback(OcrStatus.Success, text.Length));
                    FrameCapture.Core.Trace.Log($"ocr-still copied {text.Length}");
                    break;
                case OcrRoute.NoEngineHint:
                    OcrResultWindow.ShowMessage(OcrService.Feedback(OcrStatus.NoEngine, 0));
                    FrameCapture.Core.Trace.Log("ocr-still no-engine");
                    break;
                default:
                    OcrResultWindow.ShowMessage(OcrService.Feedback(OcrStatus.Empty, 0));
                    FrameCapture.Core.Trace.Log("ocr-still empty");
                    break;
            }
        }
        catch (Exception ex)
        {
            FrameCapture.Core.Trace.Log($"ocr-still failed: {ex.Message}");
            OcrResultWindow.ShowMessage("识别失败");
        }
    }

    void StartScrollLoop(RectI region, string device)
    {
        var f = LiveCapture.Grab(region);
        if (f == null) { FrameCapture.Core.Trace.Log("scroll: first grab null"); return; }
        _scrollAcc = f.Bgra; _scrollW = f.W; _scrollH = f.H; _scrollRegion = region; _scrollDevice = device; _scrollSegs = 1;
        _scrollPrevB = null;
        _scrollBar = new Overlays.ScrollHintWindow(region, device) { OnDone = ScrollDone, OnCancel = ScrollCancel };
        _scrollBar.SetProgress(1, _scrollH);
        _scrollBar.Show();   // ShowActivated=false：不抢焦点，目标窗继续接收滚动
        // R90：会话期间全局吞 Esc 取消；标志绑定控制条生命周期，Closed 兜底清零，绝不泄漏成"全局 Esc 失灵"
        Services.HotkeyManager.ScrollActive = true;
        _scrollBar.Closed += (_, _) => Services.HotkeyManager.ScrollActive = false;
        Overlays.ScrollHintWindow.Current = _scrollBar;
        _scrollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _scrollTimer.Tick += (_, _) => ScrollTick();
        _scrollTimer.Start();
        FrameCapture.Core.Trace.Log($"scroll loop start {region.W}x{region.H}");
    }

    void ScrollTick()
    {
        if (_scrollAcc == null || _scrollBar == null) return;
        var b = LiveCapture.Grab(_scrollRegion);
        if (b == null) return;
        // 稳定判定：本帧与上一帧一致（上一帧底部完全包含本帧）才算滚动停稳，避免拼接滚动中途的过渡帧
        bool stable = _scrollPrevB != null &&
            FrameCapture.Core.ScrollStitch.FindOverlap(_scrollPrevB, _scrollW, _scrollPrevB.Length / (_scrollW * 4), b.Bgra, b.H) >= b.H;
        _scrollPrevB = b.Bgra;
        if (!stable) return;
        int ov = FrameCapture.Core.ScrollStitch.FindOverlap(_scrollAcc, _scrollW, _scrollH, b.Bgra, b.H);
        if (ov >= b.H) return;   // 画面没再滚（本帧已含于累积底部）
        if (ov <= 0) { _scrollBar.FlashHint("滚动太快、找不到重叠——请少滚一点"); return; }   // 滚过头
        int newRows = b.H - ov;
        if (newRows < 3) return; // 抖动级微滚，忽略
        _scrollAcc = FrameCapture.Core.ScrollStitch.Append(_scrollAcc, _scrollW, _scrollH, b.Bgra, b.H, ov);
        _scrollH = _scrollAcc.Length / (_scrollW * 4);
        _scrollSegs++;
        _scrollBar.SetProgress(_scrollSegs, _scrollH);
        FrameCapture.Core.Trace.Log($"scroll append ov={ov} new={newRows} h={_scrollH}");
    }

    void ScrollDone()
    {
        Services.HotkeyManager.ScrollActive = false;
        _scrollTimer?.Stop(); _scrollTimer = null;
        var acc = _scrollAcc; var bar = _scrollBar;
        _scrollAcc = null; _scrollBar = null; _scrollPrevB = null;
        if (acc == null) { bar?.CloseSelf(); return; }
        var frame = new CapturedFrame { Bgra = acc, W = _scrollW, H = _scrollH, TimeMs = 0 };
        var reg = _scrollRegion; var dev = _scrollDevice; int segs = _scrollSegs; _scrollSegs = 0;
        bar?.CloseSelf();
        var pth = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FrameCapture", $"scroll-{DateTime.Now:HHmmss}.png");
        try { FrameCapture.Services.BgraImage.SavePng(frame, pth); } catch { }
        FrameCapture.Encoding.ClipboardService.SetFrame(frame);
        FrameCapture.Services.SaveService.MaybeSavePng(frame);
        if (_settings.Current.CaptureSound) FrameCapture.Core.CaptureSound.Play(_settings.Current.CustomSoundPath);
        FrameCapture.Core.Trace.Log($"scroll done {frame.W}x{frame.H} segs={segs} {pth}");
        switch (FrameCapture.Core.AfterCapture.Resolve(_sessionAfter))
        {
            case FrameCapture.Core.AfterCaptureAction.Editor:
                OpenEditorForFrame(frame, reg, dev);
                break;
            case FrameCapture.Core.AfterCaptureAction.Copy:
                break;
            case FrameCapture.Core.AfterCaptureAction.Pin:
                PinFrameAt(frame, reg);
                break;
            default:
                Overlays.CaptureToast.Show(frame, reg, dev, () => OpenEditorForFrame(frame, reg, dev));
                break;
        }
        _lastStill.Set(frame, reg, dev);
    }

    public void ScrollCancel()
    {
        Services.HotkeyManager.ScrollActive = false;
        _scrollTimer?.Stop(); _scrollTimer = null;
        var bar = _scrollBar; _scrollBar = null; _scrollAcc = null; _scrollPrevB = null; _scrollSegs = 0;
        bar?.CloseSelf();
        FrameCapture.Core.Trace.Log("scroll cancel");
    }

    // ---------- R91 延时截图 ----------
    /// <summary>托盘/入口调用：倒计时 N 秒后截「当前范围」（已存范围，否则光标所在屏全屏）。
    /// 独立于点按/录制状态机——只借完成路径，不碰 Phase，避免与按住录制的模式交互。</summary>
    public void BeginDelayedCapture()
    {
        if (_delayWindow != null) { FrameCapture.Core.Trace.Log("delay: already running, ignore"); return; }
        _sessionAfter = _pendingAfter; _pendingAfter = "";   // R157：捕获本条手势的截图后
        int secs = Math.Clamp(_settings.Current.DelayCaptureSeconds, 0, 60);
        var region = StoredRegion() ?? FullMonitorAtCursor();
        string label = StoredRegion() != null ? $"范围 {region.Bounds.W}×{region.Bounds.H}" : "全屏";
        FrameCapture.Core.Trace.Log($"delay capture start {secs}s {region.Bounds.W}x{region.Bounds.H}");
        if (secs <= 0) { DelayFire(region); return; }   // 0 秒＝立即截，不弹倒计时
        _delayLeft = secs;
        _delayWindow = new Overlays.CountdownWindow(label, region.DeviceName) { OnCancel = DelayCancel };
        _delayWindow.SetNumber(_delayLeft);
        _delayWindow.Show();   // ShowActivated=false：不抢焦点，用户才能打开要截的菜单
        Overlays.CountdownWindow.Current = _delayWindow;
        _delayTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _delayTimer.Tick += (_, _) => DelayTick(region);
        _delayTimer.Start();
        // 倒计时期间高亮将要截取的范围（轮廓窗 WDA_EXCLUDEFROMCAPTURE，绝不入镜），让用户看清"截的是这块"（R119）。
        // 全屏范围无需高亮（整屏即范围，描边贴屏幕外缘看不见）；仅对已存选区显示。
        if (StoredRegion() != null)
        {
            _outline ??= new RegionOutlineWindow();
            _outline.SetRegion(region.Bounds);
            _outline.ShowNoActivate();
            _outline.Flash();
            _delayOutlineShown = true;
        }
    }

    /// <summary>收起倒计时范围高亮（仅当本次延时显示过，避免误关常驻轮廓）。</summary>
    void HideDelayOutline()
    {
        if (!_delayOutlineShown) return;
        _delayOutlineShown = false;
        _outline?.HideSilently();
    }

    void DelayTick(ScreenRegion region)
    {
        _delayLeft--;
        if (_delayLeft > 0) { _delayWindow?.SetNumber(_delayLeft); return; }
        var win = _delayWindow;
        _delayTimer?.Stop(); _delayTimer = null;
        _delayWindow = null;
        win?.CloseSelf();
        DelayFire(region);
    }

    void DelayCancel()
    {
        HideDelayOutline();
        _delayTimer?.Stop(); _delayTimer = null;
        var win = _delayWindow; _delayWindow = null;
        win?.CloseSelf();
        FrameCapture.Core.Trace.Log("delay cancel");
    }

    void DelayFire(ScreenRegion region)
    {
        HideDelayOutline();   // 收起倒计时范围高亮（轮廓本就不入镜，这里只是收尾）
        var reg = region.Bounds; var dev = region.DeviceName;
        var f = LiveCapture.Grab(reg);
        if (f == null) { FrameCapture.Core.Trace.Log("delay: grab null"); return; }
        FrameCapture.Encoding.ClipboardService.SetFrame(f);
        FrameCapture.Services.SaveService.MaybeSavePng(f);
        if (_settings.Current.CaptureSound) FrameCapture.Core.CaptureSound.Play(_settings.Current.CustomSoundPath);
        FrameCapture.Core.Trace.Log($"delay fire {f.W}x{f.H}");
        switch (FrameCapture.Core.AfterCapture.Resolve(_sessionAfter))
        {
            case FrameCapture.Core.AfterCaptureAction.Editor:
                OpenEditorForFrame(f, reg, dev);
                break;
            case FrameCapture.Core.AfterCaptureAction.Copy:
                break;
            case FrameCapture.Core.AfterCaptureAction.Pin:
                PinFrameAt(f, reg);
                break;
            default:
                Overlays.CaptureToast.Show(f, reg, dev, () => OpenEditorForFrame(f, reg, dev));
                break;
        }
        _lastStill.Set(f, reg, dev);
        // 范围闪一下作"截好了"的反馈（只复制模式没有浮窗，这一下尤其重要）
        if (_settings.Current.AlwaysShowRegionOutline) ShowOutline(reg);
        else { FlashOutline(reg); _outline!.FadeAway(_settings.Current.OutlineFadeSeconds); }
    }

    // ---------- R92 从剪贴板贴图 ----------
    /// <summary>托盘/入口调用：把剪贴板里的图片钉成贴图（Snipaste 式"粘贴即贴图"）——
    /// 复制任意图片（截图/图片文件/网页图）后一键钉屏参考。剪贴板无图片返回 false（调用方给反馈）。
    /// 落点：光标所在屏、以光标为中心，越界钳进屏内。须在 UI 线程调用（托盘菜单点击即 UI 线程）。</summary>
    public bool PinFromClipboard()
    {
        System.Windows.Media.Imaging.BitmapSource? img = null;
        try { if (System.Windows.Clipboard.ContainsImage()) img = System.Windows.Clipboard.GetImage(); }
        catch (Exception ex) { FrameCapture.Core.Trace.Log($"pin-clip: clipboard err {ex.GetType().Name}"); }
        var f = FrameCapture.Services.BgraImage.FromSource(img);
        if (f == null) { FrameCapture.Core.Trace.Log("pin-clip: no image"); return false; }
        Native.Win32.GetCursorPos(out var pt);
        var mon = Capture.Monitors.Nearest(pt.X, pt.Y);
        int x = Math.Max(mon.Bounds.X, Math.Min(pt.X - f.W / 2, mon.Bounds.X + mon.Bounds.W - f.W));
        int y = Math.Max(mon.Bounds.Y, Math.Min(pt.Y - f.H / 2, mon.Bounds.Y + mon.Bounds.H - f.H));
        var w = new Overlays.PinWindow(f, new Capture.RectI(x, y, f.W, f.H));
        w.Show();
        FrameCapture.Core.Trace.Log($"pin-clip {f.W}x{f.H} at {x},{y}");
        return true;
    }

    /// <summary>截图后=贴图（R117）：把刚截的单帧钉在捕获区域原位（Snipaste 式原地钉，可多张并存）。
    /// activateOnShow=false：不抢焦点，用户可继续在原应用操作，点贴图才激活（R118）。须在 UI 线程调用。</summary>
    void PinFrameAt(CapturedFrame f, RectI reg)
    {
        var w = new Overlays.PinWindow(f, reg, activateOnShow: false);
        w.Show();
        FrameCapture.Core.Trace.Log($"pin-at {f.W}x{f.H} @ {reg.X},{reg.Y}");
    }

    public ScreenRegion? StoredRegion()
    {
        var s = _settings.Current;
        if (string.IsNullOrEmpty(s.RegionMonitorId)) return null;
        var m = Monitors.ByDevice(s.RegionMonitorId);
        if (m == null) return null;
        var r = new RectI(s.RegionX, s.RegionY, s.RegionW, s.RegionH);
        if (r.W < 16 || r.H < 16) return null;
        if (!m.Bounds.Contains(r.X + r.W / 2, r.Y + r.H / 2)) return null; // 显示器布局变了
        return new ScreenRegion(m.DeviceName, r);
    }

    public void ClearStoredRegion()
    {
        _settings.Mutate(s => { s.RegionMonitorId = null; s.RegionX = s.RegionY = s.RegionW = s.RegionH = 0; });
        HideOutline();
    }

    // ---------------- 捕获 ----------------

    /// <summary>R120 B2：真正起录的核心（region = 已存范围或光标所在显示器全屏）。
    /// 由手势「开始录制」（<see cref="StartRecording"/>）与选区录制复用。R126 B2：旧 BeginCapture 点按/双击状态机已退役。</summary>
    void BeginCaptureCore()
    {
        HideOutline(); // 轮廓绝不能入镜：仅真正起录时隐藏

        ScreenRegion region = StoredRegion() ?? FullMonitorAtCursor();
        _pendingRegion = region;
        _prevFg = Native.Win32.GetForegroundWindow();
        // 前台是自家窗口（如从托盘再次触发）则无需归还，避免焦点乒乓
        if (_prevFg != System.IntPtr.Zero && Native.Win32.GetWindowThreadProcessId(_prevFg, out uint fgp) == Native.Win32.GetCurrentThreadId())
            _prevFg = System.IntPtr.Zero;

        _spool = new FrameSpool();  // R61：帧落盘（时长不再设上限），磁盘护栏触发就地停录
        _spool.DiskLow += free => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // 捕获线程触发 → 回 UI 线程停录；已录内容照常进编辑器，绝不静默丢帧
            FrameCapture.Core.Trace.Log($"disk low {free >> 20}MB → auto-stop");
            if (State == Phase.Capturing) FinishCapture();
        });
        var monitor = Monitors.ByDevice(region.DeviceName) ?? Monitors.Nearest(region.Bounds.X, region.Bounds.Y);

        _captureClock = System.Diagnostics.Stopwatch.StartNew();

        var fps = _settings.Current.Fps;
        // 跟随显示器刷新率（R100）：开启且检测成功 → 录满该显示器刷新率（如 160Hz→160fps）；否则用手动值。
        if (_settings.Current.FollowMonitorRefresh)
        {
            int hz = Core.DisplayRefresh.RateOf(monitor.DeviceName);
            if (hz > 0) fps = Math.Clamp(hz, Core.AppSettings.FpsMin, Core.AppSettings.FpsMax);
        }
        _captureFps = fps;   // 随 seq 交给编辑器，时间轴按实际帧率而非手动值（R119）
        bool wgcOk = false;
        if (_settings.Current.UseWgc)
        {
            try
            {
                _wgc = new WgcLiveCapture();
                _wgc.FrameSampled += f => _spool?.Add(f);
                _wgc.Start(monitor, region.Bounds, fps, _settings.Current.RecordIncludeCursor);
                wgcOk = true;
                FrameCapture.Core.Trace.Log("capture backend=wgc");
            }
            catch (Exception ex)
            {
                _wgc?.Dispose();
                _wgc = null;
                FrameCapture.Core.Trace.Log("wgc init failed: " + ex.Message.Split('\n')[0] + " → 回退 GDI");
            }
        }
        if (!wgcOk)
        {
            try
            {
                _live.Start(monitor, region.Bounds, fps);
                FrameCapture.Core.Trace.Log("capture backend=gdi");
            }
            catch
            {
                _spool?.Abandon();
                _spool = null;
                return;
            }
        }

        // R61：录制时长上限移除——录到用户停为止；护栏是磁盘（FrameSpool.DiskLow 自动停）。
        State = Phase.Capturing;
        // 系统 WGC 提示边框已关闭，改用自家按范围绘制的轮廓做"正在录制"提示。
        // 该窗以 WDA_EXCLUDEFROMCAPTURE 排除，不会进产物；关掉开关即完全不提示。
        if (_settings.Current.ShowRecordingBorder)
        {
            ShowCaptureScope(region.Bounds);
            // R43：起录后延迟点亮 REC 徽标——"正在录"从隐性约定变成看得见的状态
            _recBadgeTimer!.Interval = TimeSpan.FromMilliseconds(RecBadgeDelayMs);
            _recBadgeTimer.Start();
        }
    }

    /// <summary>R120 B2：手势「开始录制」——直接起录（不走点按/双击状态机）。非空闲态不动并返回 false（供失败顺延）。</summary>
    public bool StartRecording()
    {
        if (State != Phase.Idle) return false;
        BeginCaptureCore();
        return State == Phase.Capturing;
    }

    /// <summary>R120 B2 / R124：手势「停止录制」——停止录制、产物留内存（不进编辑器；未在录返回 false）。</summary>
    public bool StopRecording()
    {
        if (State != Phase.Capturing) return false;
        FinishCapture(-1, openEditor: false);
        return true;
    }

    private void FinishCapture(long holdMs = -1, bool openEditor = true)
    {
        FrameCapture.Core.Trace.Log("FinishCapture");
        _recBadgeTimer?.Stop();
        // 徽标与轮廓的退场延后到产物就绪（见 RetireCaptureOutline）：录制路径期间先挂「处理中…」，
        // 免得连按两下结束后、编辑器构造那段 UI 冻结期界面空着像没反应（R119）。
        _live.Stop();
        long wgcMissed = 0, wgcDelivered = 0, wgcKept = 0;
        if (_wgc != null) { wgcMissed = _wgc.FramesMissed; wgcDelivered = _wgc.Delivered; wgcKept = _wgc.KeptThrottle; try { _wgc.Stop(); _wgc.Dispose(); } catch { } _wgc = null; }

        double durMs = _captureClock?.ElapsedMilliseconds ?? 0;
        // 点按/长按判定用钩子事件时间戳：本地时钟会把首次 WGC 初始化耗时算成"按住"（R42 实测：
        // 新实例首次点按被误判长按直接进编辑器）。时钟只兜底无钩子来源（托盘触发/磁盘护栏自动停）。
        if (holdMs >= 0) durMs = holdMs;
        _captureClock = null;
        var spoolList = _spool != null && _spool.Count > 0 ? _spool.OpenReader() : null;
        _spool = null;
        IReadOnlyList<CapturedFrame> frames = spoolList ?? (IReadOnlyList<CapturedFrame>)new List<CapturedFrame>();
        if (durMs > 50)
            FrameCapture.Core.Trace.Log($"wgc stats kept={frames.Count} delivered={wgcDelivered} keptThrottle={wgcKept} missed={wgcMissed} dur={durMs:F0}ms fps={frames.Count / (durMs / 1000.0):F1}");
        if (frames.Count == 0)
        {
            // WGC 脏区语义在极短/全静止场景理论可为 0 帧：GDI 补抓一帧兜底，保证“点按必有产物”
            var reg = _pendingRegion?.Bounds ?? default;
            if (reg.W > 0 && reg.H > 0)
            {
                try
                {
                    var one = LiveCapture.Grab(reg);
                    if (one != null) { frames = new List<CapturedFrame> { one }; FrameCapture.Core.Trace.Log("zero-frame safety net (gdi grab)"); }
                }
                catch { }
            }
            if (frames.Count == 0) { RetireCaptureOutline(); spoolList?.Dispose(); State = Phase.Idle; return; }
        }

        // R126 B2：点按/长按判定已上移到手势引擎（Tap→截图动作、Hold/HoldRelease→录制）。
        // FinishCapture 现在只处理「真录制」收尾：产物一律按录制走，不再有「点按→单帧」分支。
        var region = _pendingRegion ?? new ScreenRegion("", frames[0].W > 0 ? new RectI(0, 0, frames[0].W, frames[0].H) : default);
        var monitor = Monitors.ByDevice(region.DeviceName);
        var seq = new CapturedSequence
        {
            Frames = frames,
            Region = region.Bounds,
            MonitorBounds = monitor?.Bounds ?? new RectI(0, 0, frames[0].W, frames[0].H),
            MonitorDevice = region.DeviceName,
            DroppedFrames = 0, // R61：磁盘暂存不再丢帧
            Fps = _captureFps, // 实际帧率（跟随刷新率时=刷新率），编辑器时间轴/时长据此而非手动值（R119）
        };

        // R124：产物先暂存内存。开编辑器时所有权归编辑器（_lastSequence 置空，免下次误回收）；
        // 不开编辑器时记为「上次录制」供 edit.lastvideo 取用，并释放上一条从未进编辑器的产物（含 spool 文件）。
        if (!ReferenceEquals(_lastSequence, seq)) (_lastSequence?.Frames as System.IDisposable)?.Dispose();
        if (openEditor)
        {
            // 编辑器构造 + 首帧解码是 UI 线程同步重活会冻结界面：先把 REC 徽标转「处理中…」并强制刷一帧，
            // 让用户在冻结期看到反馈（R119）。⚠ 这句 Dispatcher.Invoke 会泵动消息队列——只在开编辑器分支做：
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
    /// 从 FinishCapture 开头移到各产物就绪的出口，使录制路径能在编辑器构造前挂住「处理中…」（R119）。
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
        FrameCapture.Core.Trace.Log($"open editor frames={seq.Frames.Count}");
        _editor = new EditorWindow(_settings, seq);
        _editor.ClosedByUser += () =>
        {
            _editor = null;
            State = Phase.Idle;
            // 磁盘暂存帧列表随编辑器释放（删 spool 文件）
            (seq.Frames as System.IDisposable)?.Dispose();
            // 焦点归还给捕获前正在使用的窗口
            try { if (_prevFg != System.IntPtr.Zero && Native.Win32.IsWindow(_prevFg)) ForegroundNow(_prevFg); } catch { }
            _prevFg = System.IntPtr.Zero;
            // R120 A1：编辑器位图 LRU（封顶约 600MB）此刻已成垃圾，但 .NET 不把页交还 OS、工作集虚高。
            // 等窗口拆完（ApplicationIdle）再主动全代 GC + 裁工作集，等价用户手动 MemReduct，回到干净基线。
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                () => FrameCapture.Core.Memory.Reclaim(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        _editor.Show();
        ForceForeground(_editor);
    }

    /// <summary>截屏浮窗「点击继续标注」：以刚截的帧开单帧编辑器（R42）。焦点归还目标=点击前的前台窗。</summary>
    void OpenEditorForFrame(CapturedFrame f, RectI region, string device)
    {
        if (State != Phase.Idle) return; // 编辑器/录制/选取占用中：浮窗点击不重入
        var fg = Native.Win32.GetForegroundWindow();
        _prevFg = fg != System.IntPtr.Zero && Native.Win32.GetWindowThreadProcessId(fg, out uint fgp) != Native.Win32.GetCurrentThreadId()
            ? fg : System.IntPtr.Zero;
        var monitor = Monitors.ByDevice(device);
        OpenEditor(new CapturedSequence
        {
            Frames = new List<CapturedFrame> { f },
            Region = region,
            MonitorBounds = monitor?.Bounds ?? region,
            MonitorDevice = device,
        });
    }

    /// <summary>单帧静图完成后的统一处理：复制 + 自动保存副本 + 提示音 + 「截图后」四选一 + 记录上次截图。
    /// 选区拖框路径与点按直出路径共用，保证两条路径行为一致（R119 修复：此前点按直出绕过本分派，
    /// 导致「截图后」设置与提示音对直出截图全部失效，永远进编辑器）。</summary>
    void DispatchStill(CapturedFrame f, RectI reg, string device, bool windowCapture = false)
    {
        var pth = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FrameCapture", $"capture-{DateTime.Now:HHmmss}.png");
        try { FrameCapture.Services.BgraImage.SavePng(f, pth); } catch { }
        FrameCapture.Encoding.ClipboardService.SetFrame(f, includePng: windowCapture);
        FrameCapture.Services.SaveService.MaybeSavePng(f); // R63 自动保存副本（关=零副作用）
        FrameCapture.Core.Trace.Log($"still saved {pth} {f.W}x{f.H}");
        if (_settings.Current.CaptureSound) FrameCapture.Core.CaptureSound.Play(_settings.Current.CustomSoundPath); // R86 提示音（关=零副作用）
        // 截图后行为（R83）：弹提示浮窗 / 直接进编辑器 / 只复制 / 贴图——四态覆盖不同人群
        switch (FrameCapture.Core.AfterCapture.Resolve(_sessionAfter))
        {
            case FrameCapture.Core.AfterCaptureAction.Editor:
                OpenEditorForFrame(f, reg, device);
                break;
            case FrameCapture.Core.AfterCaptureAction.Copy:
                break; // 只复制（上面已 SetFrame），下方轮廓闪一下作反馈
            case FrameCapture.Core.AfterCaptureAction.Pin:
                PinFrameAt(f, reg); // 截图后直接原地钉屏（Snipaste 式）
                break;
            default:
                // 拍完即见（R42）：缩略浮窗确认"已复制"，点击原地进编辑器标注，不抢焦点
                Overlays.CaptureToast.Show(f, reg, device, () => OpenEditorForFrame(f, reg, device));
                break;
        }
        _lastStill.Set(f, reg, device); // R78：浮窗消失后仍可经托盘找回编辑器
    }

    /// <summary>是否有可重新编辑的最近静态截图（R78，决定托盘项是否出现）。</summary>
    public bool HasLastStill => !_lastStill.Empty;

    /// <summary>托盘「编辑上次截图」：确认浮窗已消失时，用最近一次静态截图重开编辑器（R78）。</summary>
    public void EditLastStill()
    {
        if (!_lastStill.TryGet(out var f, out var reg, out var dev)) return;
        OpenEditorForFrame(f, reg, dev);
    }

    /// <summary>是否有留在内存、可进编辑器的上次录制（R124）。</summary>
    public bool HasLastVideo => _lastSequence != null;

    /// <summary>R124：手势『打开编辑器（上次录制）』——用留在内存的上次录制打开视频编辑器。
    /// 所有权移交编辑器（_lastSequence 置空，编辑器关闭时自行释放 spool）。空闲且确有产物才开。</summary>
    public bool EditLastVideo()
    {
        if (State != Phase.Idle || _lastSequence == null) return false;
        var s = _lastSequence;
        _lastSequence = null;
        OpenEditor(s);
        return true;
    }

    ScreenRegion FullMonitorAtCursor()
    {
        var cursor = Win32Wrap.GetCursorPosition();
        var m = Monitors.Nearest(cursor.X, cursor.Y);
        return new ScreenRegion(m.DeviceName, m.Bounds);
    }

    // ---------------- 轮廓 ----------------

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
        FrameCapture.Core.Trace.Log($"outline: persistent show {region}");
    }

    void FlashOutline(RectI region)
    {
        _outline ??= new RegionOutlineWindow();
        _outline.SetRegion(region);
        _outline.ShowNoActivate();
        _outline.Flash();
        FrameCapture.Core.Trace.Log($"outline: flash {region}");
    }

    void HideOutline()
    {
        _outline?.HideSilently();
        FrameCapture.Core.Trace.Log("outline: hide");
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
        FrameCapture.Core.Trace.Log("recording scope outline shown req=" + region);
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

    public void Dispose()
    {
        _lastStill.Clear(); // R78：释放保留帧（最大可达数十 MB）
        (_lastSequence?.Frames as System.IDisposable)?.Dispose(); // R124：留在内存、从未进编辑器的录制产物
        _lastSequence = null;
        _live.Dispose();
    }
}

static class Win32Wrap
{
    public static (int X, int Y) GetCursorPosition()
    {
        Native.Win32.GetCursorPos(out var p);
        return (p.X, p.Y);
    }
}
