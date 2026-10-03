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

    public Phase State { get; private set; } = Phase.Idle;

    public CaptureController(SettingsService settings)
    {
        _settings = settings;
        // 只订阅一次：避免多次捕获累积 handler，导致同一帧重复入 ring
        _live.FrameSampled += f => _ring?.Add(f);
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
        if (State == Phase.Capturing) return;
        // 防状态卡死：选取窗被异常途径关闭（Alt+F4 等）时复位
        if (State == Phase.SelectingScope && _picker == null) State = Phase.Idle;
        CloseEditor();
        if (State == Phase.SelectingScope)
        {
            // 再按一次热键：把选取层重新顶到所有窗口最前（游戏等霸屏窗口会盖住它）
            if (_picker != null) { _picker.Show(); ForceForeground(_picker); }
            return;
        }

        var cursor = Win32Wrap.GetCursorPosition();
        var monitor = Monitors.Nearest(cursor.X, cursor.Y);

        State = Phase.SelectingScope;
        _picker = new RegionPickerWindow(monitor, existing: StoredRegion()?.Bounds, snapContent: _settings.Current.SnapContentArea);
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
        HideOutline(); // 轮廓绝不能入镜
        if (State == Phase.Capturing) return;
        if (State == Phase.SelectingScope) return;
        if (State == Phase.Editing)
        {
            _editor?.Activate();
            return; // 编辑器已打开时不重入
        }

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
        // 绝不在此刻显示轮廓：WGC/GDI 抓的是整屏，蓝框一旦出现就会被录进画面

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

    /// <summary>放开捕获快捷键。</summary>
    public void EndCapture()
    {
        if (State != Phase.Capturing) return;
        FinishCapture();
    }

    private void FinishCapture()
    {
        FrameCapture.Core.Trace.Log("FinishCapture");
        _maxTimer?.Stop();
        _maxTimer = null;
        _live.Stop();
        if (_wgc != null) { try { _wgc.Stop(); _wgc.Dispose(); } catch { } _wgc = null; }

        double durMs = _captureClock?.ElapsedMilliseconds ?? 0;
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
    }

    void FlashOutline(RectI region)
    {
        _outline ??= new RegionOutlineWindow();
        _outline.SetRegion(region);
        _outline.ShowNoActivate();
        _outline.Flash();
    }

    void HideOutline()
    {
        _outline?.HideSilently();
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
