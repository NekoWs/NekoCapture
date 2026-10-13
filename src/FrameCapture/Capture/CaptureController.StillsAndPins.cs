using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;

namespace FrameCapture.Services;

// 本 partial 职责：静态截图与识别文字的触发入口、剪贴板贴图、已存范围与上次产物的存取。
public sealed partial class CaptureController : IDisposable
{
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

}
