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

    /// <summary>选区确认后进入步进拼接循环：先抓基准帧，之后每 280ms 抓一帧按重叠行数追加——
    /// 滚动途中就在长，不必等手停下来。停稳帧走完整口径，滚动途中的帧只认全匹配的高置信重叠。
    /// 基准帧抓不到（区域已被关掉）就直接放弃，不进入循环也不留任何浮层——取景层与预览卡在基准帧之后才创建。</summary>
    void StartScrollLoop(RectI region, string device)
    {
        var f = LiveCapture.Grab(region);
        if (f == null) { Trace.Log("scroll: first grab null"); _scrollCompletion?.TrySetResult(false); return; }
        _scrollResume = null;   // 新起一次，手里那张就不该再挂在托盘上
        ResetScrollSession(f.Bgra, f.W, f.H, region, device);
        ShowScrollStage(f.Bgra, region, device);
        Trace.Log($"scroll loop start {region.W}x{region.H}");
    }

    /// <summary>把一次滚动会话摆成起点：基准像素 + 区域 + 设备，自动滚动的各档状态清零。</summary>
    /// <param name="acc">已拼好的像素（新起一次就是基准帧，接着上次就是那张长图）。</param>
    /// <param name="w">基准图像宽（物理像素）。</param>
    /// <param name="h">基准图像高（物理像素）。</param>
    /// <param name="region">要滚的屏幕区域。</param>
    /// <param name="device">该区域所在显示器的设备名。</param>
    void ResetScrollSession(byte[] acc, int w, int h, RectI region, string device)
    {
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
        if (!_scrollAuto || _scrollBar == null) return;
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

    void ScrollTick()
    {
        if (_scrollAcc == null || _scrollBar == null) return;
        var b = LiveCapture.Grab(_scrollRegion);
        if (b == null) return;
        // 稳定判定：本帧与上一帧一致（上一帧底部完全包含本帧）才算滚动停稳。停稳与否决定找重叠的口径：
        // 停稳帧走完整口径（容差趟、兜底照常），滚动途中的帧只认「有信息行全匹配」的高置信重叠。
        bool stable = _scrollPrevB != null &&
            ScrollStitch.FindOverlap(_scrollPrevB, _scrollW, _scrollPrevB.Length / (_scrollW * 4), b.Bgra, b.H) >= b.H;
        _scrollPrevB = b.Bgra;
        if (!stable && _scrollAuto && _autoWaitSettle && ++_autoStuckTicks >= SettleMaxTicks)
        {
            // 注进去的那一步迟迟没有下文：页面一直在动（视频、闪烁光标、惯性滚动）又一帧都拼不上时，沉默下去等于卡死
            StopAuto(Loc.T(K.Capture.TheViewKeepsChangingSo));
            return;
        }
        if (stable) { _autoWaitSettle = false; _autoStuckTicks = 0; }   // 已停稳：注过的那一步落定
        int ov = stable
            ? ScrollStitch.FindOverlap(_scrollAcc, _scrollW, _scrollH, b.Bgra, b.H)
            : ScrollStitch.FindOverlapExact(_scrollAcc, _scrollW, _scrollH, b.Bgra, b.H);
        if (ov >= b.H) { if (stable) AutoObserve(ScrollAutoObservation.NoNewContent); return; }   // 画面没再滚（本帧已含于累积底部）
        // 「滚太快」只有停稳帧才够格下结论：滚动途中凑不出全匹配是常态，一路提示会响成一片
        if (ov <= 0)
        {
            if (stable) { _scrollBar.FlashHint(Loc.T(K.Capture.ScrolledTooFastToFind)); AutoObserve(ScrollAutoObservation.NoOverlap); }
            return;
        }
        int newRows = b.H - ov;
        // 停稳帧 3 行起拼；滚动途中 12 行起拼——Append 每次重抄整张长图，为几行蠕动重抄会让长图越滚越卡。
        // acc 没长，newRows 就一直在攒，慢滚照样会在攒够那一拍拼上。
        if (newRows < (stable ? 3 : 12)) { if (stable) AutoObserve(ScrollAutoObservation.TinyStep); return; }
        _scrollAcc = ScrollStitch.Append(_scrollAcc, _scrollW, _scrollH, b.Bgra, b.H, ov);
        _scrollH = _scrollAcc.Length / (_scrollW * 4);
        _scrollSegs++;
        _scrollBar.SetProgress(_scrollSegs, _scrollH);
        _scrollPreview?.SetImage(_scrollAcc, _scrollW, _scrollH);
        _autoStuckTicks = 0;   // 拼上了就是进展，停稳等待从头计
        Trace.Log($"scroll append ov={ov} new={newRows} h={_scrollH} stable={stable}");
        AutoObserve(ScrollAutoObservation.Appended);
    }

    /// <summary>收起取景层与预览卡并清字段。完成与取消两条退出路径都走这里——漏一条就是关不掉的压暗遮罩。</summary>
    void CloseScrollStageAndPreview()
    {
        var stage = _scrollStage; _scrollStage = null;
        var prev = _scrollPreview; _scrollPreview = null;
        stage?.CloseSelf();
        prev?.CloseSelf();
    }

    void ScrollDone()
    {
        Services.HotkeyManager.ScrollActive = false;
        _scrollTimer?.Stop(); _scrollTimer = null;
        RestoreAutoCursor();   // 注轮借走的光标在出图前就得还回去，acc 为空提前返回也覆盖
        CloseScrollStageAndPreview();
        var acc = _scrollAcc; var bar = _scrollBar;
        _scrollAcc = null; _scrollBar = null; _scrollPrevB = null; _scrollAuto = false;
        if (acc == null) { bar?.CloseSelf(); return; }
        var frame = new CapturedFrame { Bgra = acc, W = _scrollW, H = _scrollH, TimeMs = 0 };
        var reg = _scrollRegion; var dev = _scrollDevice; int segs = _scrollSegs; _scrollSegs = 0;
        bar?.CloseSelf();
        // 基准帧只留一张：新的一张落下前先把上一张删掉，否则每滚一次就多一个文件。
        Core.TempFiles.Drop(_scrollResume?.Png);
        var pth = Core.TempFiles.PathOf($"scroll-{DateTime.Now:HHmmss}.png");
        try { BgraImage.SavePng(frame, pth); } catch { }
        // 完成不等于收工：滚早了头是这条路径最常见的失手。刚落盘的这张 PNG 就是「继续滚动」的基准帧，
        // 像素不进内存——长图动辄上百 MB，而它本来就已经在磁盘上了。
        _scrollResume = System.IO.File.Exists(pth) ? (pth, reg, dev) : null;
        DispatchStill(frame, reg, dev);
        _scrollCompletion?.TrySetResult(true);
    }

    /// <summary>取消滚动拼接：停表、还原注轮借走的光标、收起取景层与预览卡、关控制条、丢弃已拼接的内容（不落盘、不进剪贴板）。
    /// 退出必须清 <c>HotkeyManager.ScrollActive</c>，否则全局 Esc 会被一直吞掉（控制条 <c>Closed</c> 另有兜底）。</summary>
    public void ScrollCancel()
    {
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
    public void ResumeScrollCapture()
    {
        var r = _scrollResume;
        if (r == null) { Trace.Log("scroll resume: nothing to continue"); return; }
        var (png, region, device) = r.Value;
        _scrollResume = null;   // 无论接不接得起来，这一次都已经被用掉了
        byte[] acc; int w, h;
        try
        {
            var src = new System.Windows.Media.Imaging.BitmapImage();
            using (var fs = System.IO.File.OpenRead(png))
            {
                src.BeginInit();
                src.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                src.StreamSource = fs;
                src.EndInit();
            }
            src.Freeze();
            var f = BgraImage.FromSource(src);
            if (f == null) throw new System.IO.InvalidDataException("空图");
            (acc, w, h) = (f.Bgra, f.W, f.H);
        }
        catch (Exception e) { Trace.Log($"scroll resume: 读不回 {png} — {e.Message}"); Core.TempFiles.Drop(png); return; }
        if (State == Phase.SelectingScope || _scrollBar != null) { Trace.Log("scroll resume: 已有会话在跑"); return; }
        ResetScrollSession(acc, w, h, region, device);
        ShowScrollStage(acc, region, device);
        Trace.Log($"scroll resume {w}x{h} from {png}");
    }
    #endregion

    #region 延时截图
    /// <summary>托盘/入口调用：倒计时 N 秒后截「当前范围」（已存范围，否则光标所在屏全屏）。
    /// 独立于点按/录制状态机——只借完成路径，不碰 Phase，避免与按住录制的模式交互。</summary>
    public void BeginDelayedCapture()
    {
        if (_delayWindow != null) { Trace.Log("delay: already running, ignore"); return; }
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
        HideDelayOutline();
        _delayTimer?.Stop(); _delayTimer = null;
        var win = _delayWindow; _delayWindow = null;
        win?.CloseSelf();
        Trace.Log("delay cancel");
        _delayCompletion?.TrySetResult(false);
    }

    void DelayFire(ScreenRegion region)
    {
        HideDelayOutline();   // 收起倒计时范围高亮（轮廓本就不入镜，这里只是收尾）
        var reg = region.Bounds; var dev = region.DeviceName;
        var f = LiveCapture.Grab(reg);
        if (f == null) { Trace.Log("delay: grab null"); _delayCompletion?.TrySetResult(false); return; }
        DispatchStill(f, reg, dev);
        _delayCompletion?.TrySetResult(true);
    }
    #endregion


}
