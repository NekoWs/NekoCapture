using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;

namespace FrameCapture.Services;

// 本 partial 职责：滚动截图的步进拼接循环，与延时截图的倒计时状态机。
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

    /// <summary>QQ 式「选区截图」——选一块区域，选完立即截静态图（不受 AutoCaptureOnSelect 影响）。</summary>

    void StartScrollLoop(RectI region, string device)
    {
        var f = LiveCapture.Grab(region);
        if (f == null) { FrameCapture.Core.Trace.Log("scroll: first grab null"); return; }
        _scrollAcc = f.Bgra; _scrollW = f.W; _scrollH = f.H; _scrollRegion = region; _scrollDevice = device; _scrollSegs = 1;
        _scrollPrevB = null;
        _scrollBar = new Overlays.ScrollHintWindow(region, device) { OnDone = ScrollDone, OnCancel = ScrollCancel };
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
