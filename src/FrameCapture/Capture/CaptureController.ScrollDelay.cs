using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;

namespace FrameCapture.Services;

// 本 partial 职责：滚动截图的步进拼接循环，与延时截图的倒计时状态机。
/// <summary>
/// <see cref="CaptureController"/> 的滚动/延时分部：两条路径全程不改 <c>Phase</c>，
/// 各自用独立字段持有会话；产物按 <c>AfterCapture</c> 就地分派，不经 <see cref="DispatchStill"/>。
/// </summary>
public sealed partial class CaptureController : IDisposable
{
    #region 滚动截图
    /// <summary>托盘/入口调用：复用选区流程选一块要滚的区域，选完进入步进拼接循环。</summary>
    public void BeginScrollCapture()
    {
        FrameCapture.Core.Trace.Log("scroll capture→picker");
        _sessionAfter = _pendingAfter; _pendingAfter = "";   // 捕获本条手势的截图后
        BeginScopeSelection();
        _scrollAfterSelect = true;
    }

    /// <summary>选区确认后进入步进拼接循环：先抓基准帧，之后每 280ms 抓一帧，等滚动停稳再按重叠行数追加。
    /// 基准帧抓不到（区域已被关掉）就直接放弃，不进入循环也不留任何浮层——取景层与预览卡在基准帧之后才创建。</summary>
    void StartScrollLoop(RectI region, string device)
    {
        var f = LiveCapture.Grab(region);
        if (f == null) { FrameCapture.Core.Trace.Log("scroll: first grab null"); return; }
        _scrollAcc = f.Bgra; _scrollW = f.W; _scrollH = f.H; _scrollRegion = region; _scrollDevice = device; _scrollSegs = 1;
        _scrollPrevB = null; _scrollAuto = false;
        _autoWaitSettle = false; _autoNoNew = 0; _autoNotch = ScrollAutoPolicy.NotchStart;
        // 三层自下而上：取景层 → 预览卡 → 控制条。三者同为 Topmost，Show 顺序决定 Z 序，控制条必须在最上
        _scrollStage = new Overlays.ScrollStageWindow(region, device);
        _scrollStage.Show();
        _scrollPreview = new Overlays.ScrollPreviewWindow(region, device);
        _scrollPreview.SetImage(_scrollAcc, _scrollW, _scrollH);   // 基准帧就上屏：进流程第一眼就有预览
        _scrollPreview.Show();
        _scrollBar = new Overlays.ScrollHintWindow(region, device)
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
        FrameCapture.Core.Trace.Log($"scroll loop start {region.W}x{region.H}");
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
            FrameCapture.Core.Trace.Log("scroll auto off");
            return;
        }
        if (!Native.Win32.InputLayoutOk) { _scrollBar.FlashHint("此环境无法注入滚轮，请手动滚动"); return; }
        // 只在开启这一刻存一次原位置，整个自动滚动过程中光标就停在那儿不动；取不到原位置就不还原，免得把光标甩到 (0,0)
        _autoCursorSaved = Native.Win32.GetCursorPos(out var home);
        if (_autoCursorSaved) _autoCursorHome = home;
        Native.Win32.SetCursorPos(_scrollRegion.X + _scrollRegion.W / 2, _scrollRegion.Y + _scrollRegion.H / 2);
        _scrollAuto = true; _autoNoNew = 0; _autoNotch = ScrollAutoPolicy.NotchStart;
        _scrollBar.SetAutoScrolling(true);
        _scrollBar.FlashHint("自动滚动中：光标已移入选区，停止或结束后移回原位");
        AutoStep();   // 立刻发第一步，不等下一个空拍
        FrameCapture.Core.Trace.Log($"scroll auto on notch={_autoNotch}");
    }

    /// <summary>注入一步滚轮并进入「等这一步落定」：这一步落定后 ScrollTick 才继续判定，等待期间既不发轮也不拼接。</summary>
    void AutoStep()
    {
        _autoWaitSettle = true;
        if (!Native.Win32.Wheel(_autoNotch)) FrameCapture.Core.Trace.Log($"scroll wheel refused notch={_autoNotch}");
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
            _scrollAuto = false;
            _scrollBar.SetAutoScrolling(false);   // 先复位按钮文案，再刷提示，顺序反了会被 SetAutoScrolling 覆盖
            _scrollBar.FlashHint("已到内容底部，点「完成」出图");
            FrameCapture.Core.Trace.Log("scroll auto stop: bottom");
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
        FrameCapture.Core.Trace.Log("scroll auto cursor restored");
    }

    void ScrollTick()
    {
        if (_scrollAcc == null || _scrollBar == null) return;
        var b = LiveCapture.Grab(_scrollRegion);
        if (b == null) return;
        // 稳定判定：本帧与上一帧一致（上一帧底部完全包含本帧）才算滚动停稳，避免拼接滚动中途的过渡帧
        // 自动滚动注的那一步没落定，画面必然还在动，同样卡在这里返回——这一步不拼上就不发下一步
        bool stable = _scrollPrevB != null &&
            FrameCapture.Core.ScrollStitch.FindOverlap(_scrollPrevB, _scrollW, _scrollPrevB.Length / (_scrollW * 4), b.Bgra, b.H) >= b.H;
        _scrollPrevB = b.Bgra;
        if (!stable) return;
        _autoWaitSettle = false;   // 已停稳：注过的那一步落定，本帧可用于拼接判定
        int ov = FrameCapture.Core.ScrollStitch.FindOverlap(_scrollAcc, _scrollW, _scrollH, b.Bgra, b.H);
        if (ov >= b.H) { AutoObserve(ScrollAutoObservation.NoNewContent); return; }   // 画面没再滚（本帧已含于累积底部）
        if (ov <= 0) { _scrollBar.FlashHint("滚动太快、找不到重叠——请少滚一点"); AutoObserve(ScrollAutoObservation.NoOverlap); return; }   // 滚过头
        int newRows = b.H - ov;
        if (newRows < 3) { AutoObserve(ScrollAutoObservation.TinyStep); return; } // 抖动级微滚，忽略
        _scrollAcc = FrameCapture.Core.ScrollStitch.Append(_scrollAcc, _scrollW, _scrollH, b.Bgra, b.H, ov);
        _scrollH = _scrollAcc.Length / (_scrollW * 4);
        _scrollSegs++;
        _scrollBar.SetProgress(_scrollSegs, _scrollH);
        _scrollPreview?.SetImage(_scrollAcc, _scrollW, _scrollH);
        FrameCapture.Core.Trace.Log($"scroll append ov={ov} new={newRows} h={_scrollH}");
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
        FrameCapture.Core.Trace.Log("scroll cancel");
    }
    #endregion

    #region 延时截图
    /// <summary>托盘/入口调用：倒计时 N 秒后截「当前范围」（已存范围，否则光标所在屏全屏）。
    /// 独立于点按/录制状态机——只借完成路径，不碰 Phase，避免与按住录制的模式交互。</summary>
    public void BeginDelayedCapture()
    {
        if (_delayWindow != null) { FrameCapture.Core.Trace.Log("delay: already running, ignore"); return; }
        _sessionAfter = _pendingAfter; _pendingAfter = "";   // 捕获本条手势的截图后
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
    #endregion


}
