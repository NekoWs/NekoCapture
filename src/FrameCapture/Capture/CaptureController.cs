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
    private ScreenRegion? _pendingRegion;
    /// <summary>点按总是先显示选区（QQ 式）：本次选区确认后立即截一帧静态图。</summary>
    private bool _captureAfterSelect;
    private System.Windows.Threading.DispatcherTimer? _recBadgeTimer; // R43：按住过阈值→REC 徽标
    // R60：连按两下=免按住录制（纯逻辑在 Core.ToggleRecord，这里只做引擎/徽标接线）
    private Core.ToggleRecord.St _tr = Core.ToggleRecord.St.Idle;
    private System.Windows.Threading.DispatcherTimer? _tapWindowTimer;
    private System.Windows.Threading.DispatcherTimer? _stopWindowTimer;
    // R78：保留最近一次静态截图帧，供托盘「编辑上次截图」在托盘找回编辑器
    private readonly LastStill _lastStill = new();
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
        // 点按窗到期：没等到第二下 → 走原「点按=截图」分支（用 tapMs-1 作合成按住时长）
        _tapWindowTimer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(Core.ToggleRecord.WindowMs) };
        _tapWindowTimer.Tick += (_, _) =>
        {
            _tapWindowTimer!.Stop();
            if (_tr != Core.ToggleRecord.St.PendingTap) return;
            _tr = Core.ToggleRecord.St.Idle;
            FrameCapture.Core.Trace.Log("tap window elapsed → tap");
            FinishCapture(Math.Max(0, _settings.Current.TapThresholdMs - 1));
        };
        // 结束窗到期：录制中只按了一下 → 无操作，回到录制态
        _stopWindowTimer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(Core.ToggleRecord.WindowMs) };
        _stopWindowTimer.Tick += (_, _) =>
        {
            _stopWindowTimer!.Stop();
            if (_tr == Core.ToggleRecord.St.StopArmed) _tr = Core.ToggleRecord.St.Toggle;
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
            : $"提示：按住或连按两下 {_settings.Current.CaptureHotkey} = 录制 GIF（点按 = 截图）";
        // 旧选区可关（R55 设置项）：关=干净屏选区，Enter 确认上次选区随之无意义
        _picker = new RegionPickerWindow(monitor, existing: _settings.Current.ShowLastRegion ? StoredRegion()?.Bounds : null,
            snapContent: _settings.Current.SnapContentArea, teachTip: teachTip, magnifier: _settings.Current.PickerMagnifier,
            colorFormat: Core.ColorFormat.Parse(_settings.Current.ColorCopyFormat));
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
            if (_scrollAfterSelect)
            {
                // 滚动截图：选区即进入步进拼接循环（延迟一拍让选取层退场，别把选区界面截进首帧）
                _scrollAfterSelect = false;
                HideOutline();
                var sreg = region.Value; var sdev = monitor.DeviceName;
                var st = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                st.Tick += (_, _) => { st.Stop(); StartScrollLoop(sreg, sdev); };
                st.Start();
                return;
            }
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
                        FrameCapture.Services.SaveService.MaybeSavePng(f); // R63 自动保存副本（关=零副作用）
                        FrameCapture.Core.Trace.Log($"still saved {pth} {f.W}x{f.H}");
                        if (_settings.Current.CaptureSound) FrameCapture.Core.CaptureSound.Play(); // R86 提示音（关=零副作用）
                        // 截图后行为（R83）：弹提示浮窗 / 直接进编辑器 / 只复制——三态覆盖不同人群
                        var reg = region.Value;
                        switch (FrameCapture.Core.AfterCapture.Resolve(_settings.Current.AfterCapture))
                        {
                            case FrameCapture.Core.AfterCaptureAction.Editor:
                                OpenEditorForFrame(f, reg, monitor.DeviceName);
                                break;
                            case FrameCapture.Core.AfterCaptureAction.Copy:
                                break; // 只复制（上面已 SetFrame），下方轮廓闪一下作反馈
                            case FrameCapture.Core.AfterCaptureAction.Pin:
                                PinFrameAt(f, reg); // 截图后直接原地钉屏（Snipaste 式）
                                break;
                            default:
                                // 拍完即见（R42）：缩略浮窗确认"已复制"，点击原地进编辑器标注，不抢焦点
                                Overlays.CaptureToast.Show(f, reg, monitor.DeviceName, () => OpenEditorForFrame(f, reg, monitor.DeviceName));
                                break;
                        }
                        _lastStill.Set(f, reg, monitor.DeviceName); // R78：浮窗消失后仍可经托盘找回编辑器
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

    // ---------- R88 滚动截图 ----------
    /// <summary>托盘/入口调用：复用选区流程选一块要滚的区域，选完进入步进拼接循环。</summary>
    public void BeginScrollCapture()
    {
        FrameCapture.Core.Trace.Log("scroll capture→picker");
        BeginScopeSelection();
        _scrollAfterSelect = true;
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
        if (_settings.Current.CaptureSound) FrameCapture.Core.CaptureSound.Play();
        FrameCapture.Core.Trace.Log($"scroll done {frame.W}x{frame.H} segs={segs} {pth}");
        switch (FrameCapture.Core.AfterCapture.Resolve(_settings.Current.AfterCapture))
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
        _delayTimer?.Stop(); _delayTimer = null;
        var win = _delayWindow; _delayWindow = null;
        win?.CloseSelf();
        FrameCapture.Core.Trace.Log("delay cancel");
    }

    void DelayFire(ScreenRegion region)
    {
        var reg = region.Bounds; var dev = region.DeviceName;
        var f = LiveCapture.Grab(reg);
        if (f == null) { FrameCapture.Core.Trace.Log("delay: grab null"); return; }
        FrameCapture.Encoding.ClipboardService.SetFrame(f);
        FrameCapture.Services.SaveService.MaybeSavePng(f);
        if (_settings.Current.CaptureSound) FrameCapture.Core.CaptureSound.Play();
        FrameCapture.Core.Trace.Log($"delay fire {f.W}x{f.H}");
        switch (FrameCapture.Core.AfterCapture.Resolve(_settings.Current.AfterCapture))
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
        else { FlashOutline(reg); _outline!.FadeAway(1.1); }
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

    /// <summary>按下捕获快捷键。region = 已存范围或光标所在显示器全屏。</summary>
    public void BeginCapture()
    {
        FrameCapture.Core.Trace.Log($"BeginCapture state={State} tr={_tr}");
        // R60 状态机优先：点按窗内的第二下 → 免按住录制；录制中的第一下 → 装填结束窗；结束窗内的第二下 → 结束
        var step = Core.ToggleRecord.Next(_tr, Core.ToggleRecord.Ev.Down, 0,
            _settings.Current.TapThresholdMs, _settings.Current.DoubleTapRecord);
        switch (step.Action)
        {
            case Core.ToggleRecord.Act.EnterToggle:
                _tr = step.Next; EnterToggleRecording(); return;
            case Core.ToggleRecord.Act.ArmStop:
                _tr = step.Next; _stopWindowTimer!.Start(); return;
            case Core.ToggleRecord.Act.FinishToggleStop:
                _tr = step.Next; FrameCapture.Core.Trace.Log("toggle record stop"); FinishCapture(-1); return;
        }
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
        _tr = Core.ToggleRecord.St.Hold;
        // 系统 WGC 提示边框已关闭，改用自家按范围绘制的轮廓做"正在录制"提示。
        // 该窗以 WDA_EXCLUDEFROMCAPTURE 排除，不会进产物；关掉开关即完全不提示。
        if (_settings.Current.ShowRecordingBorder)
        {
            ShowCaptureScope(region.Bounds);
            // R43：按住超过点按阈值才点亮 REC 徽标——"还在按=正在录"从隐性约定变成看得见的状态
            _recBadgeTimer!.Interval = TimeSpan.FromMilliseconds(_settings.Current.TapThresholdMs + 60);
            _recBadgeTimer.Start();
        }
    }

    /// <summary>放开捕获快捷键。holdMs=钩子测得的真实按住时长（-1=无钩子来源，退回本地时钟）。</summary>
    public void EndCapture(long holdMs = -1)
    {
        if (State != Phase.Capturing) return;
        var step = Core.ToggleRecord.Next(_tr, Core.ToggleRecord.Ev.Up, holdMs,
            _settings.Current.TapThresholdMs, _settings.Current.DoubleTapRecord);
        _tr = step.Next;
        switch (step.Action)
        {
            case Core.ToggleRecord.Act.None:
                if (_tr == Core.ToggleRecord.St.PendingTap)
                {
                    // 短按松手：引擎继续跑（零间隙等第二下），徽标计时收起来——还没确定在录
                    _recBadgeTimer?.Stop();
                    _tapWindowTimer!.Start();
                }
                // 免按住录制中松手 / 结束窗内松手：都不停（结束只认"再连按两下"）
                return;
            case Core.ToggleRecord.Act.FinishHold:
                FinishCapture(holdMs);
                return;
        }
    }

    /// <summary>R60：点按窗内等来第二下 → 免按住录制。引擎本来就在跑（零间隙），这里只把状态"说清楚"：
    /// REC 徽标立即常驻并写明结束方式；用户关了录制边框时挂"只徽标不画框"的提示窗。</summary>
    private void EnterToggleRecording()
    {
        FrameCapture.Core.Trace.Log("toggle record start");
        _recBadgeTimer?.Stop();
        var bounds = _pendingRegion?.Bounds ?? default;
        if (_settings.Current.ShowRecordingBorder)
        {
            ShowCaptureScope(bounds);
            _outline?.SetBorderVisible(true);
        }
        else if (_outline != null && bounds.W > 0)
        {
            _outline.SetRegion(bounds);
            _outline.SetBorderVisible(false);
            _outline.ShowNoActivate();
        }
        _outline?.StartRecBadge(toggle: true);
        if (!_settings.Current.RecHintShown) _settings.Mutate(s => s.RecHintShown = true);
    }

    private void FinishCapture(long holdMs = -1)
    {
        FrameCapture.Core.Trace.Log("FinishCapture");
        _tapWindowTimer?.Stop();
        _stopWindowTimer?.Stop();
        _tr = Core.ToggleRecord.St.Idle;
        _recBadgeTimer?.Stop();
        _outline?.StopRecBadge();
        _outline?.SetBorderVisible(true); // 免按住录制的"只徽标"模式复位，别把无边框带给下次
        _live.Stop();
        long wgcMissed = 0, wgcDelivered = 0, wgcKept = 0;
        if (_wgc != null) { wgcMissed = _wgc.FramesMissed; wgcDelivered = _wgc.Delivered; wgcKept = _wgc.KeptThrottle; try { _wgc.Stop(); _wgc.Dispose(); } catch { } _wgc = null; }
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
            if (frames.Count == 0) { spoolList?.Dispose(); State = Phase.Idle; return; }
        }

        // 点按 → 单帧
        if (_settings.Current.TapThresholdMs > 0 && durMs < _settings.Current.TapThresholdMs)
        {
            // QQ 式手感：点按总是先显示选区，选完立即截图（不开编辑器）
            if (_settings.Current.TapAlwaysShowsRegion)
            {
                FrameCapture.Core.Trace.Log("tap→picker (always-show-region)");
                spoolList?.Dispose();
                State = Phase.Idle;
                BeginScopeSelection();
                _captureAfterSelect = true;
                return;
            }
            // 只留最后一帧进编辑器：spool 文件随之释放（单帧已读入内存）
            var last = frames[^1];
            frames = new List<CapturedFrame> { last };
            spoolList?.Dispose();
        }

        var region = _pendingRegion ?? new ScreenRegion("", frames[0].W > 0 ? new RectI(0, 0, frames[0].W, frames[0].H) : default);
        var monitor = Monitors.ByDevice(region.DeviceName);
        var seq = new CapturedSequence
        {
            Frames = frames,
            Region = region.Bounds,
            MonitorBounds = monitor?.Bounds ?? new RectI(0, 0, frames[0].W, frames[0].H),
            MonitorDevice = region.DeviceName,
            DroppedFrames = 0, // R61：磁盘暂存不再丢帧
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
            // 磁盘暂存帧列表随编辑器释放（删 spool 文件）
            (seq.Frames as System.IDisposable)?.Dispose();
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

    /// <summary>是否有可重新编辑的最近静态截图（R78，决定托盘项是否出现）。</summary>
    public bool HasLastStill => !_lastStill.Empty;

    /// <summary>托盘「编辑上次截图」：确认浮窗已消失时，用最近一次静态截图重开编辑器（R78）。</summary>
    public void EditLastStill()
    {
        if (!_lastStill.TryGet(out var f, out var reg, out var dev)) return;
        OpenEditorForFrame(f, reg, dev);
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
