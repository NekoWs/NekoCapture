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
    private FrameRing? _ring;
    private System.Windows.Threading.DispatcherTimer? _maxTimer;
    private RegionPickerWindow? _picker;
    private RegionOutlineWindow? _outline;
    private EditorWindow? _editor;
    private System.Diagnostics.Stopwatch? _captureClock;
    private ScreenRegion? _pendingRegion;
    /// <summary>点按总是先显示选区（QQ 式）：本次选区确认后立即截一帧静态图。</summary>
    private bool _captureAfterSelect;
    private System.Windows.Threading.DispatcherTimer? _recBadgeTimer; // R43：按住过阈值→REC 徽标

    public Phase State { get; private set; } = Phase.Idle;

    public CaptureController(SettingsService settings)
    {
        _settings = settings;
        // 只订阅一次：避免多次捕获累积 handler，导致同一帧重复入 ring
        _live.FrameSampled += f => _ring?.Add(f);
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
        if (_settings.Current.AlwaysShowRegionOutline)
            ShowOutlineIfStored();
    }

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
            _picker!.Show(); ForceForeground(_picker);
            return;
        }
        CloseEditor();

        var cursor = Win32Wrap.GetCursorPosition();
        var monitor = Monitors.Nearest(cursor.X, cursor.Y);

        State = Phase.SelectingScope;
        // 首次教学（R43）：用户还没录过制时，在选区提示里点破"按住=录制"，起录一次后不再打扰
        string? teachTip = _settings.Current.RecHintShown ? null
            : $"提示：按住 {_settings.Current.CaptureHotkey} 不放 = 录制 GIF（点按 = 截图）";
        _picker = new RegionPickerWindow(monitor, existing: StoredRegion()?.Bounds, snapContent: _settings.Current.SnapContentArea, teachTip: teachTip);
        _picker.Result += region =>
        {
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
            if (stillAfterSelect)
            {
                // 选区完成即截一帧**静态图**（PNG + 剪贴板），不进 GIF 管线、不开编辑器。
                // 延迟一拍让选取窗/遮罩完全退场，避免把选区界面截进画面。
                HideOutline();
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    var f = LiveCapture.Grab(region.Value);
                    if (f != null)
                    {
                        var pth = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FrameCapture", $"capture-{DateTime.Now:HHmmss}.png");
                        try { FrameCapture.Services.BgraImage.SavePng(f, pth); } catch { }
                        FrameCapture.Encoding.ClipboardService.SetFrame(f);
                        FrameCapture.Core.Trace.Log($"still saved {pth} {f.W}x{f.H}");
                        // 拍完即见（R42）：缩略浮窗确认"已复制"，点击原地进编辑器标注，不抢焦点
                        var reg = region.Value;
                        Overlays.CaptureToast.Show(f, reg, monitor.DeviceName, () => OpenEditorForFrame(f, reg, monitor.DeviceName));
                    }
                    if (_settings.Current.AlwaysShowRegionOutline) ShowOutline(region.Value);
                    else { FlashOutline(region.Value); _outline!.FadeAway(1.1); }
                };
                t.Start();
            }
            else if (_settings.Current.AlwaysShowRegionOutline) ShowOutline(region.Value);
            else
            {
                // 选区确认反馈：亮起→强调停顿→柔和退场（FlashOutline/FadeAway 此前是死代码）
                FlashOutline(region.Value);
                _outline!.FadeAway(1.1);
            }
        };
        _picker.Show();
        ForceForeground(_picker);
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

    /// <summary>按下捕获快捷键。region = 已存范围或光标所在显示器全屏。</summary>
    public void BeginCapture()
    {
        FrameCapture.Core.Trace.Log($"BeginCapture state={State}");
        var intent = Core.CaptureGuards.OnCaptureHotkey(
            State == Phase.Capturing, State == Phase.SelectingScope, State == Phase.Editing);
        if (intent != Core.CaptureGuards.CaptureIntent.StartCapture)
        {
            // 非起录路径一律不碰轮廓：旧版在此无条件 HideOutline，导致"编辑器开着再按捕获键"
            // 把常显范围轮廓永久弄没（任何提前 return 的分支同样误隐藏）。
            if (intent == Core.CaptureGuards.CaptureIntent.RefocusEditor) _editor?.Activate();
            return;
        }
        HideOutline(); // 轮廓绝不能入镜：仅真正起录时隐藏

        ScreenRegion region = StoredRegion() ?? FullMonitorAtCursor();
        _pendingRegion = region;
        _prevFg = Native.Win32.GetForegroundWindow();
        // 前台是自家窗口（如从托盘再次触发）则无需归还，避免焦点乒乓
        if (_prevFg != System.IntPtr.Zero && Native.Win32.GetWindowThreadProcessId(_prevFg, out uint fgp) == Native.Win32.GetCurrentThreadId())
            _prevFg = System.IntPtr.Zero;

        _ring = new FrameRing();  // 预算按可用内存自适应
        var monitor = Monitors.ByDevice(region.DeviceName) ?? Monitors.Nearest(region.Bounds.X, region.Bounds.Y);

        _captureClock = System.Diagnostics.Stopwatch.StartNew();

        var fps = _settings.Current.Fps;
        bool wgcOk = false;
        if (_settings.Current.UseWgc)
        {
            try
            {
                _wgc = new WgcLiveCapture();
                _wgc.FrameSampled += f => _ring?.Add(f);
                _wgc.Start(monitor, region.Bounds, fps);
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
                _ring = null;
                return;
            }
        }

        State = Phase.Capturing;
        // 系统 WGC 提示边框已关闭，改用自家按范围绘制的轮廓做"正在录制"提示。
        // 该窗以 WDA_EXCLUDEFROMCAPTURE 排除，不会进产物；关掉开关即完全不提示。
        if (_settings.Current.ShowRecordingBorder)
        {
            ShowCaptureScope(region.Bounds);
            // R43：按住超过点按阈值才点亮 REC 徽标——"还在按=正在录"从隐性约定变成看得见的状态
            _recBadgeTimer!.Interval = TimeSpan.FromMilliseconds(_settings.Current.TapThresholdMs + 60);
            _recBadgeTimer.Start();
        }

        int max = _settings.Current.MaxDurationMs;
        _maxTimer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(max) };
        _maxTimer.Tick += (_, _) =>
        {
            _maxTimer?.Stop();
            if (State == Phase.Capturing) FinishCapture();
        };
        _maxTimer.Start();
    }

    /// <summary>放开捕获快捷键。holdMs=钩子测得的真实按住时长（-1=无钩子来源，退回本地时钟）。</summary>
    public void EndCapture(long holdMs = -1)
    {
        if (State != Phase.Capturing) return;
        FinishCapture(holdMs);
    }

    private void FinishCapture(long holdMs = -1)
    {
        FrameCapture.Core.Trace.Log("FinishCapture");
        _recBadgeTimer?.Stop();
        _outline?.StopRecBadge();
        _maxTimer?.Stop();
        _maxTimer = null;
        _live.Stop();
        if (_wgc != null) { try { _wgc.Stop(); _wgc.Dispose(); } catch { } _wgc = null; }
        // 录制范围提示的去向（决策见 CaptureGuards.OnCaptureFinished）：
        //  常显模式且录的就是已存范围 → 把常显轮廓带回来。旧版在这里无条件 FadeAway，
        //  注释却写「由后续分支重新点亮」——实际没有任何分支点亮，于是常显模式下一次录制
        //  就把轮廓弄没；且若「录制时显示范围边框」关着，录制期间根本没上屏，
        //  更不能用「当时可见吗」当前提。其余情况 → 立即淡出，不要拖到编辑器上。
        if (Core.CaptureGuards.OnCaptureFinished(_settings.Current.AlwaysShowRegionOutline, StoredRegion() != null)
            == Core.CaptureGuards.AfterCaptureOutline.KeepPersistent)
        {
            ShowOutlineIfStored();
        }
        else if (_outline != null && _outline.IsVisible)
        {
            _outline.FadeAway(0.25);
        }



        double durMs = _captureClock?.ElapsedMilliseconds ?? 0;
        // 点按/长按判定用钩子事件时间戳：本地时钟会把首次 WGC 初始化耗时算成"按住"（R42 实测：
        // 新实例首次点按被误判长按直接进编辑器）。时钟只兜底无钩子来源（托盘/最长时限自动停）。
        if (holdMs >= 0) durMs = holdMs;
        _captureClock = null;
        var frames = _ring?.Snapshot() ?? new List<CapturedFrame>();
        int dropped = _ring?.Dropped ?? 0;
        _ring = null;
        if (dropped > 0) FrameCapture.Core.Trace.Log($"ring dropped {dropped} frames (memory budget)");
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
            if (frames.Count == 0) { State = Phase.Idle; return; }
        }

        // 点按 → 单帧
        if (_settings.Current.TapThresholdMs > 0 && durMs < _settings.Current.TapThresholdMs)
        {
            // QQ 式手感：点按总是先显示选区，选完立即截图（不开编辑器）
            if (_settings.Current.TapAlwaysShowsRegion)
            {
                FrameCapture.Core.Trace.Log("tap→picker (always-show-region)");
                State = Phase.Idle;
                BeginScopeSelection();
                _captureAfterSelect = true;
                return;
            }
            frames = new List<CapturedFrame> { frames[^1] };
        }

        var region = _pendingRegion ?? new ScreenRegion("", frames[0].W > 0 ? new RectI(0, 0, frames[0].W, frames[0].H) : default);
        var monitor = Monitors.ByDevice(region.DeviceName);
        var seq = new CapturedSequence
        {
            Frames = frames,
            Region = region.Bounds,
            MonitorBounds = monitor?.Bounds ?? new RectI(0, 0, frames[0].W, frames[0].H),
            MonitorDevice = region.DeviceName,
            DroppedFrames = dropped,
        };

        OpenEditor(seq);
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
            // 焦点归还给捕获前正在使用的窗口
            try { if (_prevFg != System.IntPtr.Zero && Native.Win32.IsWindow(_prevFg)) ForegroundNow(_prevFg); } catch { }
            _prevFg = System.IntPtr.Zero;
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
