using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;

namespace FrameCapture.Services;

// 本 partial 职责：滚动截图的步进拼接循环，与延时截图的倒计时状态机。
/// <summary>滚动拼接与倒计时使用独立会话，静图经统一出口登记并继续动作链。</summary>
public sealed partial class CaptureController : IDisposable
{
    #region 滚动截图
    /// <summary>托盘/入口调用：复用选区流程选一块要滚的区域，选完进入步进拼接循环。</summary>
    public void BeginScrollCapture()
    {
        BeginChain(new[] { GestureActions.Select, GestureActions.ScrollCapture }, Array.Empty<string>());
    }

    private int _scrollGeneration, _delayGeneration;
    private Task? _scrollStepTask;
    private bool _scrollStarting, _scrollFinishing, _delayFiring;

    private async Task StartScrollLoopAsync(RectI region, string device)
    {
        int generation = ++_scrollGeneration;
        var completion = _scrollCompletion;
        _scrollStarting = true;
        try
        {
            var frame = await BackgroundWork.Run(() => LiveCapture.Grab(region));
            if (_disposed || generation != _scrollGeneration || completion?.Task.IsCompleted == true) return;
            if (frame == null) { completion?.TrySetResult(false); return; }
            _scrollResume = null;
            ResetScrollSession(frame.Bgra, frame.W, frame.H, region, device);
            ShowScrollStage(frame.Bgra, region, device);
        }
        catch (Exception ex) { Trace.Log("scroll start: " + ex); completion?.TrySetResult(false); }
        finally { if (generation == _scrollGeneration) _scrollStarting = false; }
    }

    /// <summary>把一次滚动会话摆成起点：基准像素 + 区域 + 设备，自动滚动的各档状态清零。</summary>
    /// <param name="acc">已拼好的像素（新起一次就是基准帧，接着上次就是那张长图）。</param>
    /// <param name="w">基准图像宽（物理像素）。</param>
    /// <param name="h">基准图像高（物理像素）。</param>
    /// <param name="region">要滚的屏幕区域。</param>
    /// <param name="device">该区域所在显示器的设备名。</param>
    void ResetScrollSession(byte[] acc, int w, int h, RectI region, string device)
    {
        _scrollFinishing = false;
        _scrollAcc = acc; _scrollW = w; _scrollH = h; _scrollRegion = region; _scrollDevice = device; _scrollSegs = 1;
        _scrollPrevB = null; _scrollAuto = false;
        _autoWaitSettle = false; _autoNoNew = 0; _autoNotch = ScrollAutoPolicy.NotchStart; _autoStuckTicks = 0;
    }

    /// <summary>三层浮层自下而上（取景层 → 预览卡 → 控制条）加 280ms 步进计时器。
    /// 新起一次与接着上次都走这里：两条路径的浮层行为必须一模一样。</summary>
    /// <param name="acc">预览卡第一眼要放的基准像素，与 <see cref="ResetScrollSession"/> 收下的同一份。</param>
    /// <param name="region">要滚的屏幕区域，浮层就铺在它上面。</param>
    /// <param name="device">该区域所在显示器的设备名。</param>
    void ShowScrollStage(byte[] acc, RectI region, string device)
    {
        // 同刻 Show，入场按 StaggerMs 依次错开：三件事一起出现读起来是「卡了一下」，依次落位才读得出层次
        _scrollStage = new Overlays.ScrollStageWindow(region, device);
        _scrollStage.Show();
        _scrollPreview = new Overlays.ScrollPreviewWindow(region, device, Core.Fluent.Motion.StaggerMs);
        _scrollPreview.SetImage(acc, _scrollW, _scrollH);   // 基准帧就上屏：进流程第一眼就有预览
        _scrollPreview.Show();
        _scrollBar = new Overlays.ScrollHintWindow(region, device, Core.Fluent.Motion.StaggerMs * 2)
        {
            OnDone = ScrollDone, OnCancel = ScrollCancel, OnToggleAutoScroll = ScrollToggleAuto,
        };
        _scrollBar.SetProgress(1, _scrollH);
        _scrollBar.Show();   // ShowActivated=false：不抢焦点，目标窗继续接收滚动
        // 会话期间全局吞 Esc 取消；标志绑定控制条生命周期，Closed 兜底清零，绝不泄漏成"全局 Esc 失灵"
        Services.HotkeyManager.ScrollActive = true;
        _scrollBar.Closed += (_, _) => Services.HotkeyManager.ScrollActive = false;
        Overlays.ScrollHintWindow.Current = _scrollBar;
        _scrollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _scrollTimer.Tick += (_, _) => ScrollTick();
        _scrollTimer.Start();
    }

    /// <summary>「自动滚动」按钮：开＝把光标移进选区中心、由 ScrollTick 按步进注轮；关＝还原光标并交还滚动控制权。
    /// 滚轮投给光标所在窗口，光标不在选区内就注不到目标窗，所以开启必须先把光标挪进去。</summary>
    void ScrollToggleAuto()
    {
        if (_scrollBar == null) return;
        if (_scrollAuto)
        {
            _scrollAuto = false; _autoWaitSettle = false; _autoNoNew = 0;
            _scrollBar.SetAutoScrolling(false);
            RestoreAutoCursor();
            Trace.Log("scroll auto off");
            return;
        }
        if (!Native.Win32.InputLayoutOk) { _scrollBar.FlashHint(Loc.T(K.Capture.ThisEnvironmentCanTInject)); return; }
        // 只在开启这一刻存一次原位置，整个自动滚动过程中光标就停在那儿不动；取不到原位置就不还原，免得把光标甩到 (0,0)
        _autoCursorSaved = Native.Win32.GetCursorPos(out var home);
        if (_autoCursorSaved) _autoCursorHome = home;
        Native.Win32.SetCursorPos(_scrollRegion.X + _scrollRegion.W / 2, _scrollRegion.Y + _scrollRegion.H / 2);
        _scrollAuto = true; _autoNoNew = 0; _autoNotch = ScrollAutoPolicy.NotchStart;
        _scrollBar.SetAutoScrolling(true);
        _scrollBar.FlashHint(Loc.T(K.Capture.AutoScrollingTheCursorMoved));
        AutoStep();   // 立刻发第一步，不等下一个空拍
        Trace.Log($"scroll auto on notch={_autoNotch}");
    }

    /// <summary>注入一步滚轮并进入「等这一步落定」：这一步落定后 ScrollTick 才继续判定，等待期间既不发轮也不拼接。</summary>
    void AutoStep()
    {
        _autoWaitSettle = true;
        _autoStuckTicks = 0;
        // 注入被拒（目标窗以更高完整性级别运行等）时画面永远不会动，再等下去只会报出"已到底"这种假结论，当场说明并退出
        if (!Native.Win32.Wheel(_autoNotch)) StopAuto(Loc.T(K.Capture.ThisWindowDoesnTAccept));
    }

    /// <summary>退出自动滚动：会话本身继续，只停注轮。光标留在选区内，想接着手滚一段再出图也顺手。</summary>
    /// <param name="why">上屏的一句原因，同时进 trace。</param>
    void StopAuto(string why)
    {
        _scrollAuto = false;
        _scrollBar?.SetAutoScrolling(false);   // 先复位按钮文案再刷提示，顺序反了会被 SetAutoScrolling 覆盖
        _scrollBar?.FlashHint(why);
        Trace.Log($"scroll auto stop: {why}");
    }

    /// <summary>把本帧的拼接判定结果交给步进策略：该发下一步就发，判到底就停。手动滚动时不参与。</summary>
    /// <param name="obs">本帧观测：画面没变 / 找不到重叠 / 一步太小 / 已拼上。</param>
    void AutoObserve(ScrollAutoObservation obs)
    {
        if (_scrollFinishing || !_scrollAuto || _scrollBar == null) return;
        var d = ScrollAutoPolicy.Next(_autoWaitSettle, _autoNoNew, _autoNotch, obs);
        _autoNoNew = d.NoNew; _autoNotch = d.Notch; _autoWaitSettle = d.Waiting;
        if (d.Stop)
        {
            // 到底：只停注入、不停会话。光标留在选区内，用户想接着手动滚一段再出图也顺手
            StopAuto(Loc.T(K.Capture.BottomOfTheContentReached));
            return;
        }
        if (d.Inject) AutoStep();
    }

    /// <summary>把注轮期间移进选区的光标放回原位。没移过则无副作用，重复调用只还原一次。</summary>
    void RestoreAutoCursor()
    {
        if (!_autoCursorSaved) return;
        _autoCursorSaved = false;
        Native.Win32.SetCursorPos(_autoCursorHome.X, _autoCursorHome.Y);
        Trace.Log("scroll auto cursor restored");
    }

    private void ScrollTick()
    {
        if (_scrollFinishing || _scrollStepTask is { IsCompleted: false }) return;
        _scrollStepTask = ScrollTickAsync();
    }

    private async Task ScrollTickAsync()
    {
        if (_scrollAcc is not { } acc || _scrollBar is not { } bar) return;
        int generation = _scrollGeneration, width = _scrollW, height = _scrollH;
        var region = _scrollRegion; var previous = _scrollPrevB;
        try
        {
            var sample = await BackgroundWork.Run(() =>
            {
                var frame = LiveCapture.Grab(region);
                if (frame == null || frame.W != width) return (Frame: (CapturedFrame?)null, Stable: false, Overlap: 0, Appended: (byte[]?)null);
                bool stable = previous != null && ScrollStitch.FindOverlap(previous, width, previous.Length / (width * 4), frame.Bgra, frame.H) >= frame.H;
                int overlap = stable ? ScrollStitch.FindOverlap(acc, width, height, frame.Bgra, frame.H)
                    : ScrollStitch.FindOverlapExact(acc, width, height, frame.Bgra, frame.H);
                byte[]? appended = overlap > 0 && frame.H - overlap >= (stable ? 3 : 12)
                    ? ScrollStitch.Append(acc, width, height, frame.Bgra, frame.H, overlap) : null;
                return (Frame: (CapturedFrame?)frame, Stable: stable, Overlap: overlap, Appended: appended);
            });
            if (_disposed || generation != _scrollGeneration || !ReferenceEquals(bar, _scrollBar) || sample.Frame is not { } frame) return;
            _scrollPrevB = frame.Bgra;
            bool stable = sample.Stable; int overlap = sample.Overlap;
            if (!stable && _scrollAuto && _autoWaitSettle && ++_autoStuckTicks >= SettleMaxTicks)
            {
                StopAuto(Loc.T(K.Capture.TheViewKeepsChangingSo)); return;
            }
            if (stable) { _autoWaitSettle = false; _autoStuckTicks = 0; }
            if (overlap >= frame.H) { if (stable) AutoObserve(ScrollAutoObservation.NoNewContent); return; }
            if (overlap <= 0)
            {
                if (stable) { bar.FlashHint(Loc.T(K.Capture.ScrolledTooFastToFind)); AutoObserve(ScrollAutoObservation.NoOverlap); }
                return;
            }
            if (sample.Appended == null) { if (stable) AutoObserve(ScrollAutoObservation.TinyStep); return; }
            _scrollAcc = sample.Appended;
            _scrollH = _scrollAcc.Length / (width * 4); _scrollSegs++;
            bar.SetProgress(_scrollSegs, _scrollH);
            _scrollPreview?.SetImage(_scrollAcc, width, _scrollH);
            _autoStuckTicks = 0;
            if (!_scrollFinishing) AutoObserve(ScrollAutoObservation.Appended);
        }
        catch (Exception ex)
        {
            Trace.Log("scroll step: " + ex);
            if (generation == _scrollGeneration && !_disposed) ScrollCancel();
        }
    }

    /// <summary>收起取景层与预览卡并清字段。完成与取消两条退出路径都走这里——漏一条就是关不掉的压暗遮罩。</summary>
    void CloseScrollStageAndPreview()
    {
        var stage = _scrollStage; _scrollStage = null;
        var prev = _scrollPreview; _scrollPreview = null;
        stage?.CloseSelf();
        prev?.CloseSelf();
    }

    async void ScrollDone()
    {
        if (_scrollFinishing) return;
        int generation = _scrollGeneration;
        var completion = _scrollCompletion;
        _scrollFinishing = true;
        _scrollTimer?.Stop();
        try
        {
            if (_scrollStepTask != null) await _scrollStepTask;
            if (_disposed || generation != _scrollGeneration || _scrollBar == null) return;
            Services.HotkeyManager.ScrollActive = false;
            _scrollTimer = null;
            RestoreAutoCursor();
            CloseScrollStageAndPreview();
            var acc = _scrollAcc; var bar = _scrollBar;
            _scrollAcc = null; _scrollBar = null; _scrollPrevB = null; _scrollAuto = false;
            bar?.CloseSelf();
            if (acc == null) { completion?.TrySetResult(false); return; }
            var frame = new CapturedFrame { Bgra = acc, W = _scrollW, H = _scrollH, TimeMs = 0 };
            var region = _scrollRegion; var device = _scrollDevice;
            var context = _chainContext;
            _scrollSegs = 0;
            var previous = _scrollResume?.Png;
            string? saved = await BackgroundWork.Run(() =>
            {
                Core.TempFiles.Drop(previous);
                try
                {
                    var path = Core.TempFiles.PathOf($"scroll-{Guid.NewGuid():N}.png");
                    BgraImage.SavePng(frame, path);
                    return System.IO.File.Exists(path) ? path : null;
                }
                catch (Exception ex) { Trace.Log("scroll save: " + ex); return null; }
            });
            if (_disposed || generation != _scrollGeneration || completion?.Task.IsCompleted == true)
            {
                await BackgroundWork.Run(() => { Core.TempFiles.Drop(saved); return true; });
                return;
            }
            _scrollResume = saved != null ? (saved, region, device) : null;
            DispatchStill(frame, region, device, context: context);
            _scrollFinishing = false;
            completion?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            Trace.Log("scroll finish: " + ex);
            completion?.TrySetResult(false);
        }
        finally { if (generation == _scrollGeneration) _scrollFinishing = false; }
    }

    /// <summary>取消滚动拼接：停表、还原注轮借走的光标、收起取景层与预览卡、关控制条、丢弃已拼接的内容（不落盘、不进剪贴板）。
    /// 退出必须清 <c>HotkeyManager.ScrollActive</c>，否则全局 Esc 会被一直吞掉（控制条 <c>Closed</c> 另有兜底）。</summary>
    public void ScrollCancel()
    {
        _scrollGeneration++; _scrollStarting = false; _scrollFinishing = false;
        Services.HotkeyManager.ScrollActive = false;
        _scrollTimer?.Stop(); _scrollTimer = null;
        RestoreAutoCursor();
        CloseScrollStageAndPreview();
        var bar = _scrollBar; _scrollBar = null; _scrollAcc = null; _scrollPrevB = null; _scrollSegs = 0; _scrollAuto = false;
        bar?.CloseSelf();
        Trace.Log("scroll cancel");
        _scrollCompletion?.TrySetResult(false);
    }

    /// <summary>手里是否留着一张「完成」后没接着滚的长图。托盘据此决定给不给「继续上次滚动」。</summary>
    public bool HasScrollResume => _scrollResume != null;

    /// <summary>托盘「继续上次滚动」：把上次「完成」留下的那张长图当基准帧，重新进步进拼接循环。</summary>
    /// <remarks>页面在这期间被滚走也不会拼错：重叠找不到就是不追加，宁可少一段也不接出错位的长图。
    /// 那张 PNG 读不回来（临时目录被清、文件损坏）就当没有这次可继续，清掉并记 trace。</remarks>
    public async void ResumeScrollCapture()
    {
        if (_disposed || _updateReserved || State != Phase.Idle || _scrollStarting || _scrollBar != null || _chainTask is { IsCompleted: false }) return;
        if (_scrollResume is not { } resume) return;
        int generation = ++_scrollGeneration;
        _scrollStarting = true;
        _scrollResume = null;
        try
        {
            var frame = await BackgroundWork.Run(() => FrameFromPng(resume.Png));
            if (_disposed || generation != _scrollGeneration || State != Phase.Idle || _chainTask is { IsCompleted: false }) return;
            if (frame == null) return;
            ResetScrollSession(frame.Bgra, frame.W, frame.H, resume.Region, resume.Device);
            ShowScrollStage(frame.Bgra, resume.Region, resume.Device);
        }
        catch (Exception ex) { Trace.Log("scroll resume: " + ex); }
        finally { if (generation == _scrollGeneration) _scrollStarting = false; }
    }
    #endregion

    #region 延时截图
    /// <summary>托盘/入口调用：倒计时 N 秒后截「当前范围」（已存范围，否则光标所在屏全屏）。
    /// 独立于点按/录制状态机——只借完成路径，不碰 Phase，避免与按住录制的模式交互。</summary>
    public void BeginDelayedCapture()
    {
        if (_updateReserved) return;
        if (_delayWindow != null || _delayFiring) { Trace.Log("delay: already running, ignore"); return; }
        int secs = Math.Clamp(_settings.Current.DelayCaptureSeconds, 0, 60);
        var region = StoredRegion() ?? FullMonitorAtCursor();
        string label = StoredRegion() != null ? Loc.F(K.Capture.Region, region.Bounds.W, region.Bounds.H) : Loc.T(K.Capture.FullScreen);
        Trace.Log($"delay capture start {secs}s {region.Bounds.W}x{region.Bounds.H}");
        if (secs <= 0) { DelayFire(region); return; }   // 0 秒＝立即截，不弹倒计时
        _delayLeft = secs;
        _delayWindow = new Overlays.CountdownWindow(label, region.DeviceName, region.Bounds) { OnCancel = DelayCancel };
        _delayWindow.SetNumber(_delayLeft);
        _delayWindow.Show();   // ShowActivated=false：不抢焦点，用户才能打开要截的菜单
        Overlays.CountdownWindow.Current = _delayWindow;
        _delayTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _delayTimer.Tick += (_, _) => DelayTick(region);
        _delayTimer.Start();
        // 倒计时期间高亮将要截取的范围（轮廓窗 WDA_EXCLUDEFROMCAPTURE，绝不入镜），让用户看清"截的是这块"。
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
        _delayGeneration++; _delayFiring = false;
        HideDelayOutline();
        _delayTimer?.Stop(); _delayTimer = null;
        var win = _delayWindow; _delayWindow = null;
        win?.CloseSelf();
        Trace.Log("delay cancel");
        _delayCompletion?.TrySetResult(false);
    }

    private async void DelayFire(ScreenRegion region)
    {
        int generation = ++_delayGeneration;
        var completion = _delayCompletion;
        var context = _chainContext;
        _delayFiring = true;
        HideDelayOutline();
        try
        {
            var frame = await BackgroundWork.Run(() => LiveCapture.Grab(region.Bounds));
            if (_disposed || generation != _delayGeneration || completion?.Task.IsCompleted == true) return;
            if (frame == null) { completion?.TrySetResult(false); return; }
            DispatchStill(frame, region.Bounds, region.DeviceName, context: context);
            completion?.TrySetResult(true);
        }
        catch (Exception ex) { Trace.Log("delay capture: " + ex); completion?.TrySetResult(false); }
        finally { if (generation == _delayGeneration) _delayFiring = false; }
    }
    #endregion


}
