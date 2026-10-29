using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Encoding;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>
/// EditorWindow partial：结果输出——确定提交 + 导出/保存（GIF/MP4/WebM/PNG）+ 胶片条 + 钉图 + OCR + 轻提示/忙态。
/// </summary>
public sealed partial class EditorWindow : Window
{

    readonly List<Button> _okBtns = new();
    bool _encoding;
    CancellationTokenSource? _commitCts;

    /// <summary>确定：后台烘焙+编码 GIF（HUD 进度，Esc 可取消）→ 剪贴板（文件+位图+自定义格式）。</summary>
    void Commit()
    {
        int idx = Math.Clamp(_index, 0, _seq.Frames.Count - 1);
        // 胶片条只由工具栏「生成胶片条」按钮显式触发，确定路径不自动生成。
        StartEncode(null, null, (seq, gif) =>
        {
            Trace.Log($"gif encoded bytes={gif!.Length} frames={seq.Frames.Count}");
            if (!ManagedOutput)
            {
                ClipboardService.SetCapture(seq, gif, idx);
            }
            SaveService.MaybeSaveArtifact(seq, gif);
            OutputReady?.Invoke(seq);
            Fluent.CloseWithFade(this);
        });
    }

    /// <summary>把当前帧（含标注+裁剪，WYSIWYG）烘焙成一张位图帧，用于钉到屏幕。
    /// 单帧同步烘焙（快，无需后台任务）；返回帧 + 其对应的物理屏幕矩形（裁剪后随之收缩）。</summary>
    (CapturedFrame? frame, RectI region) BakeCurrentFrame()
    {
        int idx = Math.Clamp(_index, 0, _seq.Frames.Count - 1);
        var frame = _seq.Frames[idx];
        var anns = new List<Drawing.Annotation>(_anns.Items);
        var frames = new List<CapturedFrame> { frame };
        var outAnns = anns;
        var region = _seq.Region;
        if (_crop.IsCropped)
        {
            int fw = frame.W, fh = frame.H;
            var (cx, cy, cw, ch) = CropState.ToPixelOf(_crop.Current, fw, fh);
            frames = Drawing.Bake.Crop(frames, cx, cy, cw, ch);
            outAnns = CropRemap.Remap(anns, (double)cx / fw, (double)cy / fh, (double)cw / fw, (double)ch / fh);
            region = new RectI(region.X + cx, region.Y + cy, cw, ch);
        }
        var baked = Drawing.Bake.Apply(frames, outAnns);
        return (baked.Count > 0 ? baked[0] : null, region);
    }

    /// <summary>钉到屏幕——烘焙当前帧并在原捕获位置弹出一张贴图（可多张并存）。</summary>
    void PinCurrentFrame()
    {
        var (f, region) = BakeCurrentFrame();
        if (f == null) return;
        var w = new PinWindow(f, region);
        w.Show();
        Trace.Log($"pin frame {f.W}x{f.H} at {region.X},{region.Y}");
    }

    /// <summary>预览底部轻提示：从下方 6px 抬起淡入，约 1.8s 后淡出并收起。OCR/复制等即时反馈用。
    /// 提示条已在显示中时只换文字并续期，不重播入场——重播会把刚看清的一行字压回全透明再抬一次。</summary>
    void ShowSnack(string msg)
    {
        if (_snack == null || _snackText == null) return;
        _snackText.Text = msg;
        if (!(_snack.Visibility == Visibility.Visible && _snack.Opacity > 0.5))
        {
            var dur = new System.Windows.Duration(TimeSpan.FromMilliseconds(Fluent.Motion.HoverMs));
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            _snack.Visibility = Visibility.Visible;
            _snackShift = new System.Windows.Media.TranslateTransform(0, 6);
            _snack.RenderTransform = _snackShift;
            _snack.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            _snackShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, dur) { EasingFunction = ease });
        }
        int gen = ++_snackGen;   // 本次显示的世代，淡出回调只收自己那一代
        if (_snackTimer == null)
        {
            _snackTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            _snackTimer.Tick += (_, _) =>
            {
                _snackTimer!.Stop();
                int closing = _snackGen;
                var fade = new System.Windows.Media.Animation.DoubleAnimation(
                    0.0, TimeSpan.FromMilliseconds(Fluent.Motion.DissolveMs))
                { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
                // 淡出走完才收起：留着 Visible 的透明浮层会让下一次显示被当成「仍在显示中」而只换文字不动画
                fade.Completed += (_, __) =>
                {
                    if (_snack == null || closing != _snackGen) return;   // 期间又提示过一次，交给那一次收尾
                    _snack.RenderTransform = null;
                    _snackShift = null;
                    _snack.Visibility = Visibility.Collapsed;
                };
                _snack!.BeginAnimation(OpacityProperty, fade);
            };
        }
        _snackTimer.Stop();
        _snackTimer.Start();
    }
    /// <summary>识别当前帧文字（Windows 内置 OCR）→ 纯文本写剪贴板 → 轻提示。
    /// 帧像素在首个 await 前同步拷贝，故与关窗/释放 spool 无竞态；无语言包优雅降级不抛。</summary>
    async void OcrCurrentFrame()
    {
        if (_ocrBusy || _seq.Frames.Count == 0) return;
        using var activity = BackgroundWork.Hold();
        _ocrBusy = true;
        ShowSnack(Loc.T(K.Common.RecognizeText2));
        try
        {
            var frame = Cur();
            var outcome = await OcrService.RecognizeAsync(frame);
            if (_userClosed) return; // 识别期间关窗：跳过提示
            switch (outcome.Status)
            {
                case OcrStatus.Success:
                    var text = OcrService.Normalize(outcome.Text);
                    // 预览优先：开结果窗时以「预览选取」为主，不自动写剪贴板（要哪段选哪段 / 点复制全部）；
                    // 关结果窗才走快速路径——直接复制 + 轻提示。
                    if (_settings.Current.OcrShowResult)
                        OcrResultWindow.Show(text);
                    else
                    {
                        try { Clipboard.SetText(text); } catch { }
                        ShowSnack(OcrService.Feedback(OcrStatus.Success, text.Length));
                    }
                    Trace.Log($"ocr ok {text.Length} chars");
                    break;
                case OcrStatus.NoEngine:
                    ShowSnack(OcrService.Feedback(OcrStatus.NoEngine, 0));
                    Trace.Log("ocr no-engine");
                    break;
                default:
                    ShowSnack(OcrService.Feedback(OcrStatus.Empty, 0));
                    Trace.Log("ocr empty");
                    break;
            }
        }
        catch (Exception ex)
        {
            Trace.Log($"ocr failed: {ex.Message}");
            if (!_userClosed) ShowSnack(Loc.T(K.Common.RecognitionFailed));
        }
        finally { _ocrBusy = false; }
    }

    /// <summary>确定按钮忙态：编码期间淡到 0.55 半透明（带过渡动画），结束恢复 1.0。</summary>
    void SetOkBusy(bool busy)
    {
        foreach (var b in _okBtns)
        {
            b.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(busy ? 0.55 : 1.0,
                new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.HoverMs)))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        }
    }

    /// <summary>
    /// 导出录制。videoFmt==null → GIF（onDone 收字节）；否则 ffmpeg 编码到 outPath（onDone 收 null）。
    /// 子集/裁剪/烘焙两路共用，只在末步分派编码器。
    /// </summary>
    void StartEncode(VideoFormat? videoFmt, string? outPath, Action<CapturedSequence, byte[]?> onDone)
    {
        if (_encoding) return;
        try
        {
            var (a, b) = TrimRange;
            // 导出子集：spool 用 Slice（共享文件、只切索引，不整块物化）；内存表用 GetRange。
            // 后续 Bake 会按需物化裁剪范围——导出体量由时间轴裁剪范围决定，与总录制时长解耦。
            IReadOnlyList<CapturedFrame> subset = _seq.Frames is FrameSpoolList spl
                ? spl.Slice(a, b - a + 1)
                : _seq.Frames.Skip(a).Take(b - a + 1).ToList();
            var anns = new List<Drawing.Annotation>(_anns.Items);
            var region = _seq.Region; var monBounds = _seq.MonitorBounds; var monDev = _seq.MonitorDevice;
            int fps = _fps;
            var cropSnap = _crop.Current; bool cropOn = _crop.IsCropped; // UI 线程快照，后台只读
            string verb = videoFmt == null ? "GIF" : videoFmt == VideoFormat.Mp4 ? "MP4" : "WebM";

            _encoding = true;
            SetOkBusy(true);
            var cancellation = _commitCts = new CancellationTokenSource();
            var ct = cancellation.Token;
            _hud.Text = Loc.F(K.Editor.Exporting, verb);
            WithFrameReaderAsync(subset, () =>
            {
                // 隔离测试可强制触发编码失败，校验失败反馈。
                if (Environment.GetEnvironmentVariable("FC_TEST_ENCODE_FAIL") == "1")
                    throw new InvalidOperationException("forced encode failure (FC_TEST_ENCODE_FAIL=1)");
                // 裁剪=视图状态：导出瞬间一次性折算（像素级等价：先裁剪后烘焙 ≡ 先烘焙后裁剪）
                var frames = subset; var outAnns = anns; var outRegion = region;
                if (cropOn && frames.Count > 0)
                {
                    int fw = frames[0].W, fh = frames[0].H;
                    var (cx, cy, cw, ch) = CropState.ToPixelOf(cropSnap, fw, fh);
                    frames = Drawing.Bake.Crop(frames, cx, cy, cw, ch);
                    outAnns = CropRemap.Remap(anns, (double)cx / fw, (double)cy / fh, (double)cw / fw, (double)ch / fh);
                    outRegion = new RectI(region.X + cx, region.Y + cy, cw, ch);
                }
                var baked = Drawing.Bake.Apply(frames, outAnns);
                var seq = new CapturedSequence
                {
                    Frames = baked,
                    Region = outRegion,
                    MonitorBounds = monBounds,
                    MonitorDevice = monDev,
                };
                var prog = new Progress<int>(d => Dispatcher.BeginInvoke(() => _hud.Text = Loc.F(K.Editor.Exporting2, verb, d, baked.Count)));
                if (videoFmt == null)
                {
                    byte[] gif = GifEncoder.Encode(baked, ComputeDelays(baked, fps), true, prog, ct);
                    return (seq, gif: (byte[]?)gif);
                }
                string ff = FfmpegLocator.Find()
                    ?? throw new InvalidOperationException("未找到 ffmpeg，无法导出视频");
                // 用帧时间戳推实际帧率：静态段少帧 → 低 fps，但总时长与录制一致（播放速度正确）
                double vfps = VideoEncoder.FpsFromFrames(baked);
                // MP4 没有透明通道：喂帧前按「边框填充色」拍平；WebM 带 alpha，这个颜色对它不起作用。
                var matte = Core.HexColor.Parse(_settings.Current.VideoMatteColor);
                VideoEncoder.Encode(baked, ff, outPath!, videoFmt.Value, vfps, prog, ct, matte);
                return (seq, gif: (byte[]?)null);
            }, ct).ContinueWith(t =>
            {
                if (ReferenceEquals(_commitCts, cancellation)) _commitCts = null;
                cancellation.Dispose();
                _encoding = false;
                if (_userClosed) { _ = t.Exception; return; }
                SetOkBusy(false);
                if (t.IsCanceled || (t.Exception?.InnerException is OperationCanceledException))
                {
                    Trace.Log("encode cancelled");
                    Show(_index); // 恢复 HUD
                    return;
                }
                if (t.IsFaulted)
                {
                    Trace.Log($"encode failed: {t.Exception}");
                    _hud.Text = t.Exception?.InnerException is InvalidOperationException ? Loc.T(K.Editor.FfmpegNotFound) : Loc.T(K.Editor.ExportFailed);
                    OutputFailed?.Invoke();
                    return;
                }
                onDone(t.Result.seq, t.Result.gif);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch (Exception ex)
        {
            _encoding = false;
            Trace.Log($"encode start failed: {ex}");
            OutputFailed?.Invoke();
        }
    }

    private static async Task<T> WithFrameReaderAsync<T>(IReadOnlyList<CapturedFrame> frames, Func<T> work, CancellationToken cancellation)
    {
        using var reader = frames as FrameSpoolList;
        return await BackgroundWork.Run(work, cancellation).ConfigureAwait(false);
    }

    static List<int> ComputeDelays(IReadOnlyList<CapturedFrame> frames, int fps)
    {
        int defaultDelay = 1000 / fps;
        var delays = new List<int>(frames.Count);
        double total = frames.Count > 0 ? FrameMetadata.TimeAt(frames, frames.Count - 1) : 0;
        for (int i = 0; i < frames.Count; i++)
        {
            double t0 = FrameMetadata.TimeAt(frames, i);
            double t1 = i + 1 < frames.Count ? FrameMetadata.TimeAt(frames, i + 1) : total;
            int d = (int)(t1 - t0);
            // 环形缓冲丢帧会造成巨大时间洞；洞两侧用默认间隔，避免播放停顿
            delays.Add(d > 0 && d < 250 ? Math.Clamp(d, 10, 200) : defaultDelay);
        }
        return delays;
    }


    /// <summary>生成胶片条——小窗问「抽多少帧 / 每 N 帧取一次」（参数记忆），
    /// 确定后按时间轴裁剪范围取子集、折算裁剪与标注（与导出同口径），后台烘焙抽样帧，
    /// 渲染成一张逐帧图直接复制剪贴板（不落盘）。生成中复用 _encoding 互斥锁防并发导出。</summary>
    async void GenerateFilmStrip()
    {
        if (_encoding || !_seq.Frames.Any()) return;
        using var activity = BackgroundWork.Hold();
        var (a, b) = TrimRange;
        int count = b - a + 1;
        // UI 线程快照（后台只读）：弹窗预览与成片读同一批帧，抽样口径一致
        IReadOnlyList<CapturedFrame> subset = _seq.Frames is FrameSpoolList spl
            ? spl.Slice(a, count)
            : _seq.Frames.Skip(a).Take(count).ToList();
        var anns = new List<Drawing.Annotation>(_anns.Items);
        var cropSnap = _crop.Current; bool cropOn = _crop.IsCropped;

        using var reader = subset as FrameSpoolList;
        _encoding = true; SetOkBusy(true);
        try
        {
            var dlg = new FilmStripDialog(this, count, _settings.Current.AgentStripFrames, (k, token) =>
            {
                var preview = AgentStrip.SampleIndices(count, k, 1);
                return AgentStrip.RenderMapped(i => subset[preview[i]], preview,
                    FilmStripDialog.PreviewCell, decimate: true, cancellation: token);
            });
            bool confirmed = dlg.ShowDialog() == true;
            await dlg.PreviewFinished;
            if (!confirmed || _userClosed) return;
            int frames = dlg.Frames;
            _settings.Mutate(s => s.AgentStripFrames = frames);
            var picks = AgentStrip.SampleIndices(count, frames, 1);
            using var cancellation = new CancellationTokenSource();
            _commitCts = cancellation;
            ShowSnack(Loc.T(K.Editor.BuildingFilmStrip));
            var img = await BackgroundWork.Run(() => AgentStrip.RenderMapped(
                i => BakeForStrip(new[] { subset[picks[i]] }, anns, cropSnap, cropOn)[0], picks,
                cancellation: cancellation.Token), cancellation.Token);
            if (_userClosed) return;
            try { Clipboard.SetImage(img); ShowSnack(Loc.F(K.Editor.FilmStripCopiedFrames, picks.Count)); }
            catch { ShowSnack(Loc.T(K.Editor.TheClipboardIsBusySo)); }
            Trace.Log($"filmstrip {picks.Count}/{count} frames");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Trace.Log("filmstrip: " + ex.Message);
            if (!_userClosed) ShowSnack(Loc.T(K.Editor.FilmStripFailed));
        }
        finally { _commitCts = null; _encoding = false; if (!_userClosed) SetOkBusy(false); }
    }

    /// <summary>按时间轴裁剪与标注烘焙给定帧，与导出同口径；只烘焙要上格的那几帧，长录制也秒级。</summary>
    /// <param name="fs">抽样出来的帧，UI 线程快照，后台只读。</param>
    /// <param name="anns">当前标注；开了裁剪时按裁剪框折算坐标。</param>
    /// <param name="crop">裁剪框（归一化），仅在 <paramref name="cropOn"/> 为真时生效。</param>
    /// <param name="cropOn">是否开了裁剪。</param>
    /// <returns>烘焙完成的帧，与 <paramref name="fs"/> 一一对应，可直接交给 <see cref="AgentStrip.Render"/>.</returns>
    static IReadOnlyList<CapturedFrame> BakeForStrip(
        IReadOnlyList<CapturedFrame> fs, List<Drawing.Annotation> anns, CropState.Norm crop, bool cropOn)
    {
        IReadOnlyList<CapturedFrame> frames = fs;
        var outAnns = anns;
        if (cropOn && frames.Count > 0)
        {
            int fw = frames[0].W, fh = frames[0].H;
            var (cx, cy, cw, ch) = CropState.ToPixelOf(crop, fw, fh);
            frames = Drawing.Bake.Crop(frames, cx, cy, cw, ch);
            outAnns = CropRemap.Remap(anns, (double)cx / fw, (double)cy / fh, (double)cw / fw, (double)ch / fh);
        }
        return Drawing.Bake.Apply(frames, outAnns);
    }

    void SaveFrame()
    {
        var dlg = NewSaveDialog(Loc.T(K.Common.PngImagePng), $"frame-{_times[_index] / 1000.0:0.0}.png");
        if (dlg.ShowDialog(this) != true) return;
        var frame = ExportFrame(_index);
        BgraImage.SavePng(frame, EnsureExt(dlg.FileName, ".png"));
        OutputReady?.Invoke(new CapturedSequence { Frames = new[] { frame }, Region = _seq.Region,
            MonitorBounds = _seq.MonitorBounds, MonitorDevice = _seq.MonitorDevice });
    }

    void SaveRecording()
    {
        if (_encoding) return;
        // 测试专用（与 FC_TEST_ENCODE_FAIL 同约定）：置 FC_TEST_EXPORT=<路径> 则跳过存盘对话框，
        // 直接按扩展名导出，使「录制→编辑器→ffmpeg 落盘」全链路可确定性 e2e + ffprobe 核对。正常用户环境永不置位。
        string? testExport = Environment.GetEnvironmentVariable("FC_TEST_EXPORT");
        if (!string.IsNullOrEmpty(testExport)) { DispatchSave(testExport); return; }
        var def = _settings.Current.RecordFormat;
        string defName = def switch
        {
            RecordFormat.Mp4 => "capture.mp4",
            RecordFormat.WebM => "capture.webm",
            _ => "capture.gif",
        };
        var dlg = NewSaveDialog(Loc.T(K.Editor.Mp4VideoMp4WebmVideo), defName);
        // 类型下拉默认选中当前「录制格式」，与默认文件名扩展一致（否则总停在首项 MP4，与 capture.gif 打架）
        dlg.FilterIndex = def switch { RecordFormat.Mp4 => 1, RecordFormat.WebM => 2, _ => 3 };
        if (dlg.ShowDialog(this) != true) return;
        DispatchSave(dlg.FileName);
    }

    /// <summary>按扩展名分派导出：.mp4/.webm 走 ffmpeg，其余走 GIF。</summary>
    void DispatchSave(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".mp4" or ".webm")
        {
            if (FfmpegLocator.Find() == null)
            {
                _hud.Text = Loc.T(K.Editor.FfmpegNotFoundSoVideo);
                return;
            }
            var fmt = ext == ".mp4" ? VideoFormat.Mp4 : VideoFormat.WebM;
            path = EnsureExt(path, ext);
            StartEncode(fmt, path, (output, _) =>
            {
                _hud.Text = Loc.F(K.Editor.Exported, System.IO.Path.GetFileName(path));
                Trace.Log($"video saved {path}");
                OutputReady?.Invoke(output);
            });
        }
        else
        {
            path = EnsureExt(path, ".gif");
            StartEncode(null, null, (output, gif) =>
            {
                try { File.WriteAllBytes(path, gif!); Trace.Log($"gif saved {path} {gif!.Length}B"); OutputReady?.Invoke(output); }
                catch (Exception ex) { Trace.Log($"gif save failed: {ex.Message}"); _hud.Text = Loc.T(K.Common.SaveFailed); OutputFailed?.Invoke(); }
                Show(_index);
            });
        }
    }

    void SaveAfterimage()
    {
        var dlg = NewSaveDialog(Loc.T(K.Common.PngImagePng), "afterimage.png");
        if (dlg.ShowDialog(this) != true) return;
        // 残影只稀疏采样 5 帧：用惰性裁剪投影，绝不全段物化（长录制全量转内存必 OOM）
        IReadOnlyList<CapturedFrame> src = _seq.Frames;
        if (_crop.IsCropped && _seq.Frames.Count > 0 && _frameW > 0)
        {
            var (cx, cy, cw, ch) = _crop.ToPixel(_frameW, _frameH);
            src = new LazyCropList(_seq.Frames, cx, cy, cw, ch);
        }
        var combined = BgraImage.Afterimage(src, 5);
        // 合成图同样带标注：裁剪坐标已随裁剪折算，标注做同一重映射后烘焙
        var anns = _anns.Items.ToList();
        if (_crop.IsCropped && combined.W > 0 && _frameW > 0)
        {
            var (cx, cy, cw, ch) = _crop.ToPixel(_frameW, _frameH);
            anns = CropRemap.Remap(anns, (double)cx / _frameW, (double)cy / _frameH, (double)cw / _frameW, (double)ch / _frameH);
        }
        var baked = Drawing.Bake.Apply(new[] { combined }, anns)[0];
        BgraImage.SavePng(baked, EnsureExt(dlg.FileName, ".png"));
    }

    /// <summary>惰性裁剪投影：按需裁剪单帧，不整段物化（残影稀疏采样用）。</summary>
    sealed class LazyCropList : IReadOnlyList<CapturedFrame>
    {
        readonly IReadOnlyList<CapturedFrame> _src; readonly int _x, _y, _w, _h;
        /// <summary>记录源列表与裁剪矩形：不复制帧、不改源列表。</summary>
        /// <param name="s">源帧列表：只读引用，枚举期间须保持可读。</param>
        /// <param name="x">裁剪矩形左边，源帧内物理像素列。</param>
        /// <param name="y">裁剪矩形顶边，源帧内物理像素行。</param>
        /// <param name="w">裁剪矩形宽，物理像素，须为正。</param>
        /// <param name="h">裁剪矩形高，物理像素，须为正。</param>
        public LazyCropList(IReadOnlyList<CapturedFrame> s, int x, int y, int w, int h) { _src = s; _x = x; _y = y; _w = w; _h = h; }
        public int Count => _src.Count;
        /// <summary>索引器：访问到哪帧才裁哪帧，不整段物化。</summary>
        public CapturedFrame this[int i] => Drawing.Bake.CropFrame(_src[i], _x, _y, _w, _h);
        /// <summary>按索引顺序枚举，逐帧现裁。</summary>
        /// <returns>逐个产出裁剪帧的枚举器；裁剪结果不缓存。</returns>
        public IEnumerator<CapturedFrame> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    static string EnsureExt(string path, string ext)
        => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? path : path + ext;

    static Microsoft.Win32.SaveFileDialog NewSaveDialog(string filter, string defaultName)
    {
        // 默认目录与「自动保存副本」保持一致：尊重用户自定义 AutoSavePath，非法/未设则回退 Pictures\FrameCapture
        string dir = SaveService.ResolveFolder();
        return new() { Filter = filter, FileName = defaultName, InitialDirectory = dir };
    }
}
