using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;

namespace FrameCapture.Services;

// 本 partial 职责：静态截图与识别文字的触发入口、剪贴板贴图、已存范围与上次产物的存取。
/// <summary>
/// <see cref="CaptureController"/> 的静图/贴图分部：单帧产物的出口集中在这里，
/// 选区拖框与点按直出两条路径都由 <see cref="DispatchStill"/> 收尾。
/// </summary>
public sealed partial class CaptureController : IDisposable
{
    /// <summary>「选区录制」——选一块区域，选完立即对该区域起录。</summary>
    public void BeginRecordSelection()
    {
        BeginChain(new[] { GestureActions.Select, GestureActions.RecordStart }, Array.Empty<string>());
    }

    /// <summary>录制中停止并保留产物，空闲时开始选区录制。</summary>
    public void ToggleRecordSelection()
    {
        if (State is Phase.Starting or Phase.Capturing)
        {
            Trace.Log("record-toggle→stop");
            _ = StopRecordingAsync();
            return;
        }
        BeginRecordSelection();
    }

    /// <summary>干净利落的全屏即时截图——直接抓光标所在屏幕全屏当下这一帧，无选区、无点按窗、
    /// 无录制管线、无延时、抓取前不弹任何窗（不抢焦点）。专治游戏「一丢焦点就暂停」：按下即定格此刻画面。
    /// 本动作<b>永远抓光标屏全屏</b>，已存范围与它无关；要按已存范围截用 <see cref="CaptureStoredStill"/>。</summary>
    public async void CaptureInstantStill() => await CaptureStandaloneAsync(false);

    /// <summary>对已存区域或光标屏截静图，保存最近产物并显示浮窗。</summary>
    public async void CaptureStoredStill() => await CaptureStandaloneAsync(true);

    private async Task CaptureStandaloneAsync(bool stored)
    {
        if (_disposed || _updateReserved || State != Phase.Idle || _chainTask is { IsCompleted: false }) return;
        State = Phase.CapturingStill;
        using var activity = BackgroundWork.Hold();
        try
        {
            var region = stored ? StoredRegion() ?? FullMonitorAtCursor() : FullMonitorAtCursor();
            var frame = await BackgroundWork.Run(() => LiveCapture.Grab(region.Bounds));
            if (_disposed || frame == null) return;
            State = Phase.Idle;
            DispatchStill(frame, region.Bounds, region.DeviceName);
        }
        catch (Exception ex) { Trace.Log("standalone still: " + ex); }
        finally { if (!_disposed && State == Phase.CapturingStill) State = Phase.Idle; }
    }

    /// <summary>手势「识别文字」——对已存范围（无则光标屏全屏）抓一帧做 OCR；这条手势刚截好一帧时认那一帧。
    /// 结果按 <see cref="OcrService.Route"/> 分流：
    /// 结果窗 / 复制+轻确认 / 无引擎提示 / 空提示（反馈统一走可截图审查的 OcrResultWindow）。
    /// 后台任务持有只读帧直到识别完成；无语言包按无引擎路由处理。</summary>
    public async void OcrStoredStill()
    {
        using var activity = BackgroundWork.Hold();
        await RecognizeStoredStillAsync();
    }

    async Task<bool> RecognizeStoredStillAsync()
    {
        if (State != Phase.Idle) return false;
        // 刚截好的那一帧就是用户看见的那一帧：定格画面、窗口本体的透明边都在里面，重抓会换成另一个时刻。
        var context = _chainContext;
        CapturedFrame? shot = context?.Still;
        var region = StoredRegion() ?? FullMonitorAtCursor();
        var f = shot ?? await BackgroundWork.Run(() => LiveCapture.Grab(region.Bounds));
        if (f == null) { Trace.Log("ocr-still: grab null"); return false; }
        try
        {
            bool preview = _settings.Current.OcrShowResult;
            var outcome = await OcrService.RecognizeAsync(f, preview);
            var text = OcrService.Normalize(outcome.Text);
            if (_disposed || _updateReserved) return false;
            if (context != null) context.Text = text;
            switch (OcrService.Route(outcome.Status, preview))
            {
                case OcrRoute.ResultWindow:
                    OcrResultWindow.Show(outcome);
                    Trace.Log($"ocr-still ok {text.Length}");
                    break;
                case OcrRoute.CopyThenConfirm:
                    if (!await Encoding.ClipboardService.SetTextAsync(text)) return false;
                    OcrResultWindow.ShowMessage(OcrService.Feedback(OcrStatus.Success, text.Length));
                    Trace.Log($"ocr-still copied {text.Length}");
                    break;
                case OcrRoute.NoEngineHint:
                    OcrResultWindow.ShowMessage(OcrService.Feedback(OcrStatus.NoEngine, 0));
                    Trace.Log("ocr-still no-engine");
                    break;
                default:
                    OcrResultWindow.ShowMessage(OcrService.Feedback(OcrStatus.Empty, 0));
                    Trace.Log("ocr-still empty");
                    break;
            }
            return outcome.Status == OcrStatus.Success;
        }
        catch (Exception ex)
        {
            Trace.Log($"ocr-still failed: {ex.Message}");
            OcrResultWindow.ShowMessage(Loc.T(K.Common.RecognitionFailed));
            return false;
        }
    }
    #region 从剪贴板贴图
    /// <summary>托盘/入口调用：后台读取剪贴板图片并钉成贴图（粘贴即贴图）——
    /// 复制任意图片（截图/图片文件/网页图）后一键钉屏参考。剪贴板无图片返回 false（调用方给反馈）。
    /// 落点：光标所在屏、以光标为中心，越界钳进屏内。须在 UI 线程调用（托盘菜单点击即 UI 线程）。</summary>
    /// <returns>true = 已钉出贴图；false = 剪贴板里没有可用图片（含读取抛异常），此时不创建任何窗口。</returns>
    public async Task<bool> PinFromClipboardAsync()
    {
        if (_updateReserved) return false;
        var f = await Encoding.ClipboardService.ReadImageAsync();
        if (_disposed || _updateReserved) return false;
        if (f == null) { Trace.Log("pin-clip: no image"); return false; }
        Native.Win32.GetCursorPos(out var pt);
        var mon = Capture.Monitors.Nearest(pt.X, pt.Y);
        int x = Math.Max(mon.Bounds.X, Math.Min(pt.X - f.W / 2, mon.Bounds.X + mon.Bounds.W - f.W));
        int y = Math.Max(mon.Bounds.Y, Math.Min(pt.Y - f.H / 2, mon.Bounds.Y + mon.Bounds.H - f.H));
        var bitmap = await BackgroundWork.Run(() => BgraImage.ToSource(f));
        if (_disposed || _updateReserved) return false;
        var w = new Overlays.PinWindow(f, new Capture.RectI(x, y, f.W, f.H), initialBitmap: bitmap);
        w.Show();
        Trace.Log($"pin-clip {f.W}x{f.H} at {x},{y}");
        return true;
    }

    /// <summary>截图后=贴图：把刚截的单帧钉在捕获区域原位（原地钉，可多张并存）。
    /// activateOnShow=false：不抢焦点，用户可继续在原应用操作，点贴图才激活。须在 UI 线程调用。</summary>
    async Task<bool> PinFrameAtAsync(CapturedFrame f, RectI reg)
    {
        var bitmap = await BackgroundWork.Run(() => BgraImage.ToSource(f));
        if (_disposed || _updateReserved) return false;
        var w = new Overlays.PinWindow(f, reg, activateOnShow: false, initialBitmap: bitmap);
        w.Show();
        Trace.Log($"pin-at {f.W}x{f.H} @ {reg.X},{reg.Y}");
        return true;
    }

    /// <summary>已存的捕获范围（物理像素、虚拟桌面绝对坐标），每次选区确认时写进设置。
    /// 以下情形返回 null：从未设过范围、范围所在显示器已不存在、宽或高不足 16px、
    /// 范围中心已落在该显示器之外（显示器布局变了）。</summary>
    /// <returns>可直接用于捕获的范围；null = 无可用范围，调用方应回退到光标屏全屏。</returns>
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

    /// <summary>清掉已存范围（显示器名与矩形一并归零），并收起常显轮廓——范围没了，轮廓也就无处可画。</summary>
    public async void ClearStoredRegion() => await ClearStoredRegionAsync();

    private async Task ClearStoredRegionAsync()
    {
        var save = _settings.MutateAsync(s => { s.RegionMonitorId = null; s.RegionX = s.RegionY = s.RegionW = s.RegionH = 0; });
        HideOutline();
        await save;
    }

    /// <summary>截屏浮窗「点击继续标注」：以刚截的帧开单帧编辑器。焦点归还目标=点击前的前台窗。</summary>
    async void OpenEditorForFrame(CapturedFrame f, RectI region, string device)
        => await OpenEditorForFrameAsync(f, region, device);

    private async Task<bool> OpenEditorForFrameAsync(CapturedFrame f, RectI region, string device)
    {
        if (_disposed || _updateReserved || State != Phase.Idle) return false; // 编辑器/录制/选取占用中：浮窗点击不重入
        var fg = Native.Win32.GetForegroundWindow();
        _prevFg = fg != IntPtr.Zero && Native.Win32.GetWindowThreadProcessId(fg, out uint fgp) != Native.Win32.GetCurrentThreadId()
            ? fg : IntPtr.Zero;
        var monitor = Monitors.ByDevice(device);
        return await OpenEditorAsync(new CapturedSequence
        {
            Frames = new List<CapturedFrame> { f },
            Region = region,
            MonitorBounds = monitor?.Bounds ?? region,
            MonitorDevice = device,
        });
    }

    /// <summary>把一张 PNG 读成单帧，供调试入口当捕获产物用。</summary>
    /// <param name="path">PNG 路径。读不到或解码失败只记日志，不打扰用户。</param>
    /// <returns>解好的帧；路径无效、图为空或解码失败返回 null。</returns>
    static CapturedFrame? FrameFromPng(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Trace.Log($"png: 找不到 {path}"); return null; }
        try
        {
            var decoded = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bgra = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            if (w <= 0 || h <= 0) { Trace.Log($"png: 空图 {path}"); return null; }
            var px = new byte[w * 4 * h];
            bgra.CopyPixels(px, w * 4, 0);
            return new CapturedFrame { Bgra = px, W = w, H = h };
        }
        catch (Exception ex) { Trace.Log("png fail: " + ex); return null; }
    }

    /// <summary>主屏，取不到时退到任意一台；调试入口用它给合成帧编一个合理的捕获位置。</summary>
    /// <returns>主显示器信息；一块屏都没有时返回 null。</returns>
    static MonitorInfo? PrimaryMonitor()
        => Monitors.All().FirstOrDefault(m => m.IsPrimary) ?? Monitors.All().FirstOrDefault();

    /// <summary>调试/e2e：把一张 PNG 当作刚截好的静图开进编辑器，版面审查不必走真实捕获链路。</summary>
    /// <param name="path">PNG 路径。</param>
    public async void OpenEditorWithPng(string path)
    {
        if (_updateReserved || State != Phase.Idle) return; // 编辑器/录制/选取占用中：不重入
        var f = await BackgroundWork.Run(() => FrameFromPng(path));
        if (_disposed || _updateReserved || State != Phase.Idle) return;
        if (f == null) return;
        var mon = PrimaryMonitor();
        var bounds = mon?.Bounds ?? new RectI(0, 0, f.W, f.H);
        await OpenEditorAsync(new CapturedSequence
        {
            Frames = new List<CapturedFrame> { f },
            Region = new RectI(bounds.X, bounds.Y, f.W, f.H),
            MonitorBounds = bounds,
            MonitorDevice = mon?.DeviceName ?? "",
        });
    }

    /// <summary>调试/e2e：把一张 PNG 钉成贴图，贴图窗口的版面审查不必走真实捕获链路。</summary>
    /// <param name="path">PNG 路径。钉在主屏正中，与真实「原地贴图」同一出口。</param>
    public async void PinWithPng(string path)
    {
        var f = await BackgroundWork.Run(() => FrameFromPng(path));
        if (_disposed || _updateReserved || State != Phase.Idle) return;
        if (f == null) return;
        var b = PrimaryMonitor()?.Bounds ?? new RectI(0, 0, f.W, f.H);
        await PinFrameAtAsync(f, new RectI(b.X + (b.W - f.W) / 2, b.Y + (b.H - f.H) / 2, f.W, f.H));
    }

    /// <summary>登记静图，应用自动保存、提示音与范围反馈；链内更新上下文，独立截图显示浮窗。</summary>
    /// <param name="f">只读共享的 BGRA 帧。</param>
    /// <param name="reg">物理像素，虚拟桌面绝对坐标。</param>
    /// <param name="device">显示器设备名。</param>
    /// <param name="windowCapture">是否保留窗口透明通道。</param>
    /// <param name="context">接收产物的动作上下文；null 表示独立截图。</param>
    void DispatchStill(CapturedFrame f, RectI reg, string device, bool windowCapture = false, RunContext? context = null)
    {
        _lastStill.Set(f, reg, device, windowCapture);
        SaveService.MaybeSavePng(f);
        if (_settings.Current.CaptureSound) CaptureSound.Play(_settings.Current.CustomSoundPath);
        if (_settings.Current.AlwaysShowRegionOutline) ShowOutline(reg);
        else if (_settings.Current.OutlineFadeSeconds > 0)
        {
            FlashOutline(reg);
            _outline!.FadeAway(_settings.Current.OutlineFadeSeconds);
        }
        if (context != null)
        {
            context.Still = f;
            context.LatestProduct = RunValue.Still;
            context.Text = null;
            context.StillRegion = reg;
            context.StillDevice = device;
            context.WindowCapture = windowCapture;
            return;
        }
        _ = ShowLastStillToastAsync();
    }

    /// <summary>是否有可重新编辑的最近静态截图。</summary>
    /// <value>true = 托盘「编辑上次截图」应出现；false = 还没截过图，或缓存已被清。</value>
    public bool HasLastStill => !_lastStill.Empty;

    /// <summary>托盘「编辑上次截图」：确认浮窗已消失时，用最近一次静态截图重开编辑器。</summary>
    public async void EditLastStill() => await EditLastStillAsync();

    private async Task<bool> EditLastStillAsync()
    {
        if (_disposed || _updateReserved || State != Phase.Idle) return false;
        var snapshot = await _lastStill.ReadAsync();
        if (_disposed || _updateReserved || State != Phase.Idle || snapshot == null) return false;
        return await OpenEditorForFrameAsync(snapshot.Frame, snapshot.Region, snapshot.Device);
    }

    /// <summary>手势「贴图」——对当前范围（无则光标屏全屏）抓一帧钉在原地；这条手势刚截好一帧时钉那一帧。
    /// 与托盘的「粘贴即贴图」各走各的：这里钉的是屏幕上的范围，不看剪贴板。</summary>
    public async void PinStoredStill()
    {
        if (_updateReserved || State != Phase.Idle) return;
        // 刚截好的那一帧就在手上：钉它。对范围重抓会丢掉窗口截图的透明通道与投影，
        // 而且选区窗刚关闭，重抓拿到的是选区之后的画面。
        if (_chainContext?.Still is { } just)
        {
            await PinFrameAtAsync(just, _chainContext.StillRegion);
            Trace.Log($"pin-run {just.W}x{just.H}");
            return;
        }
        var region = StoredRegion() ?? FullMonitorAtCursor();
        var f = await BackgroundWork.Run(() => LiveCapture.Grab(region.Bounds));
        if (_disposed || _updateReserved || State != Phase.Idle) return;
        if (f == null) { Trace.Log("pin-stored: grab null"); return; }
        await PinFrameAtAsync(f, region.Bounds);
        Trace.Log($"pin-stored {f.W}x{f.H}");
    }

    /// <summary>手势「提示」——把上一次截图再以浮窗弹一次，点它仍可进编辑器。没有上次截图时什么都不做。</summary>
    public async void ShowLastStillToast() => await ShowLastStillToastAsync();

    private async Task<bool> ShowLastStillToastAsync()
    {
        var snapshot = await _lastStill.ReadAsync();
        if (_disposed || _updateReserved || snapshot == null) return false;
        var f = snapshot.Frame; var reg = snapshot.Region; var dev = snapshot.Device;
        Overlays.CaptureToast.Show(f, reg, dev, () => OpenEditorForFrame(f, reg, dev), async () =>
        {
            if (!await Encoding.ClipboardService.SetFrameAsync(f, snapshot.WindowCapture))
                Overlays.NoticeToast.Show(Loc.T(K.Common.ClipboardUnavailable));
        });
        return true;
    }

    /// <summary>手势「复制到剪贴板」——把上一次截图写进剪贴板。没有上次截图时什么都不做。
    /// 上次那张是窗口截图时必须带上 PNG 形态：CF_DIB 不带 alpha，透明边会被读成黑色。</summary>
    /// <returns>剪贴板是否完整写入。</returns>
    public async Task<bool> CopyLastStillAsync()
    {
        var snapshot = await _lastStill.ReadAsync();
        if (_disposed || _updateReserved || snapshot == null) return false;
        var f = snapshot.Frame;
        bool result = await Encoding.ClipboardService.SetFrameAsync(f, includePng: snapshot.WindowCapture);
        Trace.Log($"copy-last still {f.W}x{f.H} success={result}");
        return result;
    }

    /// <summary>是否有留在内存、可进编辑器的上次录制。</summary>
    /// <value>true = 『打开编辑器（上次录制）』会成功（还要当前空闲）；false = 没有留存产物，或已交给编辑器。</value>
    public bool HasLastVideo => _lastSequence != null;

    /// <summary>手势『打开编辑器（上次录制）』——用留在内存的上次录制打开视频编辑器。
    /// 所有权移交编辑器（_lastSequence 置空，编辑器关闭时自行释放 spool）。空闲且确有产物才开。</summary>
    /// <returns>true = 已打开编辑器并移交产物所有权；false = 非空闲态或没有留存产物，未产生任何副作用。</returns>
    public async Task<bool> EditLastVideoAsync()
    {
        if (State != Phase.Idle || _lastSequence == null) return false;
        var s = _lastSequence;
        _lastSequence = null;
        return await OpenEditorAsync(s);
    }

    ScreenRegion FullMonitorAtCursor()
    {
        var cursor = Win32Wrap.GetCursorPosition();
        var m = Monitors.Nearest(cursor.X, cursor.Y);
        return new ScreenRegion(m.DeviceName, m.Bounds);
    }
    #endregion

}
