using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Encoding;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

// 结果输出：确定提交 + 导出/保存（GIF/MP4/WebM/PNG）+ 胶片条 + 钉图 + OCR + 轻提示/忙态
public sealed partial class EditorWindow : Window
{

    readonly List<Button> _okBtns = new();
    bool _encoding;
    CancellationTokenSource? _commitCts;

    /// <summary>确定：后台烘焙+编码 GIF（HUD 进度，Esc 可取消）→ 剪贴板（文件+位图+自定义格式）。</summary>
    void Commit()
    {
        int idx = Math.Clamp(_index, 0, _seq.Frames.Count - 1);
        // R161：不再自动生成 Agent 胶片条（设置项已移除）——改为工具栏「生成胶片条」按钮显式触发。
        StartEncode(null, null, (seq, gif) =>
        {
            Trace.Log($"gif encoded bytes={gif!.Length} frames={seq.Frames.Count}");
            ClipboardService.SetCapture(seq, gif, idx);
            FrameCapture.Services.SaveService.MaybeSaveArtifact(seq, gif); // R63 自动保存副本：单帧 PNG / 多帧按 RecordFormat（R103，视频后台编码；关=零副作用）
            FrameCapture.Core.Fluent.CloseWithFade(this);
        });
    }

    /// <summary>R65：把当前帧（含标注+裁剪，WYSIWYG）烘焙成一张位图帧，用于钉到屏幕。
    /// 单帧同步烘焙（快，无需后台任务）；返回帧 + 其对应的物理屏幕矩形（裁剪后随之收缩）。</summary>
    (CapturedFrame? frame, RectI region) BakeCurrentFrame()
    {
        int idx = Math.Clamp(_index, 0, _seq.Frames.Count - 1);
        var frame = _seq.Frames[idx];
        var anns = new List<FrameCapture.Drawing.Annotation>(_anns.Items);
        var frames = new List<CapturedFrame> { frame };
        var outAnns = anns;
        var region = _seq.Region;
        if (_crop.IsCropped)
        {
            int fw = frame.W, fh = frame.H;
            var (cx, cy, cw, ch) = FrameCapture.Core.CropState.ToPixelOf(_crop.Current, fw, fh);
            frames = FrameCapture.Drawing.Bake.Crop(frames, cx, cy, cw, ch);
            outAnns = FrameCapture.Core.CropRemap.Remap(anns, (double)cx / fw, (double)cy / fh, (double)cw / fw, (double)ch / fh);
            region = new RectI(region.X + cx, region.Y + cy, cw, ch);
        }
        var baked = FrameCapture.Drawing.Bake.Apply(frames, outAnns);
        return (baked.Count > 0 ? baked[0] : null, region);
    }

    /// <summary>R65：钉到屏幕——烘焙当前帧并在原捕获位置弹出一张贴图（可多张并存，Snipaste 式）。</summary>
    void PinCurrentFrame()
    {
        var (f, region) = BakeCurrentFrame();
        if (f == null) return;
        var w = new PinWindow(f, region);
        w.Show();
        FrameCapture.Core.Trace.Log($"pin frame {f.W}x{f.H} at {region.X},{region.Y}");
    }

    /// <summary>预览底部轻提示：淡入显示 msg，约 1.8s 后淡出。OCR/复制等即时反馈用（R69）。</summary>
    void ShowSnack(string msg)
    {
        if (_snack == null || _snackText == null) return;
        _snackText.Text = msg;
        _snack.Visibility = Visibility.Visible;
        _snack.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            1.0, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.HoverMs))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        if (_snackTimer == null)
        {
            _snackTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            _snackTimer.Tick += (_, _) =>
            {
                _snackTimer!.Stop();
                _snack!.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
                    0.0, TimeSpan.FromMilliseconds(320))
                { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } });
            };
        }
        _snackTimer.Stop();
        _snackTimer.Start();
    }

    /// <summary>R69：识别当前帧文字（Windows 内置 OCR）→ 纯文本写剪贴板 → 轻提示。
    /// 帧像素在首个 await 前同步拷贝，故与关窗/释放 spool 无竞态；无语言包优雅降级不抛。</summary>
    async void OcrCurrentFrame()
    {
        if (_ocrBusy || _seq.Frames.Count == 0) return;
        _ocrBusy = true;
        ShowSnack("识别文字…");
        try
        {
            var frame = _seq.Frames[_index];
            var outcome = await FrameCapture.Core.OcrService.RecognizeAsync(frame);
            if (_userClosed) return; // 识别期间关窗：不再提示
            switch (outcome.Status)
            {
                case FrameCapture.Core.OcrStatus.Success:
                    var text = FrameCapture.Core.OcrService.Normalize(outcome.Text);
                    // R120 D2 预览优先：开结果窗时以「预览选取」为主，不自动写剪贴板（要哪段选哪段 / 点复制全部）；
                    // 关结果窗才走快速路径——直接复制 + 轻提示。
                    if (_settings.Current.OcrShowResult)
                        OcrResultWindow.Show(text);
                    else
                    {
                        try { System.Windows.Clipboard.SetText(text); } catch { }
                        ShowSnack(FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.Success, text.Length));
                    }
                    FrameCapture.Core.Trace.Log($"ocr ok {text.Length} chars");
                    break;
                case FrameCapture.Core.OcrStatus.NoEngine:
                    ShowSnack(FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.NoEngine, 0));
                    FrameCapture.Core.Trace.Log("ocr no-engine");
                    break;
                default:
                    ShowSnack(FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.Empty, 0));
                    FrameCapture.Core.Trace.Log("ocr empty");
                    break;
            }
        }
        catch (Exception ex)
        {
            FrameCapture.Core.Trace.Log($"ocr failed: {ex.Message}");
            if (!_userClosed) ShowSnack("识别失败");
        }
        finally { _ocrBusy = false; }
    }

    /// <summary>时间轴范围+标注的后台烘焙与 GIF 编码（HUD 进度，编码中 Esc 取消）；完成后回 UI 线程执行 onDone。</summary>
    void SetOkBusy(bool busy)
    {
        foreach (var b in _okBtns)
        {
            b.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(busy ? 0.55 : 1.0,
                new System.Windows.Duration(TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.HoverMs)))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        }
    }

    /// <summary>
    /// 导出录制。videoFmt==null → GIF（onDone 收字节）；否则 ffmpeg 编码到 outPath（onDone 收 null）。
    /// 子集/裁剪/烘焙两路共用，只在末步分派编码器。
    /// </summary>
    void StartEncode(FrameCapture.Encoding.VideoFormat? videoFmt, string? outPath, Action<CapturedSequence, byte[]?> onDone)
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
            var anns = new List<FrameCapture.Drawing.Annotation>(_anns.Items);
            var region = _seq.Region; var monBounds = _seq.MonitorBounds; var monDev = _seq.MonitorDevice;
            int fps = _fps;
            var cropSnap = _crop.Current; bool cropOn = _crop.IsCropped; // UI 线程快照，后台只读
            string verb = videoFmt == null ? "GIF" : videoFmt == FrameCapture.Encoding.VideoFormat.Mp4 ? "MP4" : "WebM";

            _encoding = true;
            SetOkBusy(true);
            _commitCts = new CancellationTokenSource();
            var ct = _commitCts.Token;
            _hud.Text = $"导出 {verb}…";
            System.Threading.Tasks.Task.Run(() =>
            {
                // 测试专用（与 FC_TEST_NO_FG 同约定）：FC_TEST_ENCODE_FAIL=1 令编码任务必然抛错，
                // 使"导出失败"HUD 终态可确定性触达并做双主题像素取证（UIAUDIT D9）。正常用户环境永不置位。
                if (System.Environment.GetEnvironmentVariable("FC_TEST_ENCODE_FAIL") == "1")
                    throw new System.InvalidOperationException("forced encode failure (FC_TEST_ENCODE_FAIL=1)");
                // 裁剪=视图状态：导出瞬间一次性折算（T78c 像素级等价：先裁剪后烘焙 ≡ 先烘焙后裁剪）
                var frames = subset; var outAnns = anns; var outRegion = region;
                if (cropOn && frames.Count > 0)
                {
                    int fw = frames[0].W, fh = frames[0].H;
                    var (cx, cy, cw, ch) = FrameCapture.Core.CropState.ToPixelOf(cropSnap, fw, fh);
                    frames = FrameCapture.Drawing.Bake.Crop(frames, cx, cy, cw, ch);
                    outAnns = FrameCapture.Core.CropRemap.Remap(anns, (double)cx / fw, (double)cy / fh, (double)cw / fw, (double)ch / fh);
                    outRegion = new RectI(region.X + cx, region.Y + cy, cw, ch);
                }
                var baked = FrameCapture.Drawing.Bake.Apply(frames, outAnns);
                var seq = new CapturedSequence
                {
                    Frames = baked,
                    Region = outRegion,
                    MonitorBounds = monBounds,
                    MonitorDevice = monDev,
                };
                var prog = new Progress<int>(d => Dispatcher.BeginInvoke(() => _hud.Text = $"导出 {verb}… {d}/{baked.Count}"));
                if (videoFmt == null)
                {
                    byte[] gif = GifEncoder.Encode(baked, ComputeDelays(baked, fps), true, prog, ct);
                    return (seq, gif: (byte[]?)gif);
                }
                string ff = FrameCapture.Encoding.FfmpegLocator.Find()
                    ?? throw new System.InvalidOperationException("未找到 ffmpeg，无法导出视频");
                // 用帧时间戳推实际帧率：静态段少帧 → 低 fps，但总时长与录制一致（播放速度正确）
                double vfps = FrameCapture.Encoding.VideoEncoder.FpsFromFrames(baked);
                FrameCapture.Encoding.VideoEncoder.Encode(baked, ff, outPath!, videoFmt.Value, vfps, prog, ct);
                return (seq, gif: (byte[]?)null);
            }, ct).ContinueWith(t =>
            {
                _encoding = false;
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
                    _hud.Text = t.Exception?.InnerException is System.InvalidOperationException ? "未找到 ffmpeg" : "导出失败";
                    return;
                }
                onDone(t.Result.seq, t.Result.gif);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch (Exception ex)
        {
            _encoding = false;
            Trace.Log($"encode start failed: {ex}");
        }
    }

    static List<int> ComputeDelays(IReadOnlyList<CapturedFrame> frames, int fps)
    {
        int defaultDelay = 1000 / fps;
        var delays = new List<int>(frames.Count);
        double total = frames.Count > 0 ? frames[^1].TimeMs : 0;
        for (int i = 0; i < frames.Count; i++)
        {
            double t0 = frames[i].TimeMs;
            double t1 = i + 1 < frames.Count ? frames[i + 1].TimeMs : total;
            int d = (int)(t1 - t0);
            // 环形缓冲丢帧会造成巨大时间洞；洞两侧用默认间隔，避免播放停顿
            delays.Add(d > 0 && d < 250 ? Math.Clamp(d, 10, 200) : defaultDelay);
        }
        return delays;
    }


    /// <summary>R161：生成胶片条——小窗问「抽多少帧 / 每 N 帧取一次」（参数记忆），
    /// 确定后按时间轴裁剪范围取子集、折算裁剪与标注（与导出同口径），后台烘焙抽样帧，
    /// 渲染成一张逐帧图直接复制剪贴板（不落盘）。生成中复用 _encoding 互斥锁防并发导出。</summary>
    async void GenerateFilmStrip()
    {
        if (_encoding || !_seq.Frames.Any()) return;
        var (a, b) = TrimRange;
        int count = b - a + 1;
        var dlg = new FilmStripDialog(this, _settings.Current.AgentStripFrames, _settings.Current.AgentStripEveryN, count) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        int frames = dlg.Frames, every = dlg.EveryN;
        _settings.Mutate(s => { s.AgentStripFrames = frames; s.AgentStripEveryN = every; });   // 小窗参数持久化，下次沿用

        // UI 线程快照（后台只读）：抽样索引先算好，只烘焙要用的那几帧，长录制也秒级
        IReadOnlyList<CapturedFrame> subset = _seq.Frames is FrameSpoolList spl
            ? spl.Slice(a, count)
            : _seq.Frames.Skip(a).Take(count).ToList();
        var idxs = FrameCapture.Encoding.AgentStrip.SampleIndices(count, frames, every);
        var sampled = idxs.Select(i => subset[i]).ToList();
        var anns = new List<FrameCapture.Drawing.Annotation>(_anns.Items);
        var cropSnap = _crop.Current; bool cropOn = _crop.IsCropped;

        _encoding = true; SetOkBusy(true);
        ShowSnack("生成胶片条…");
        try
        {
            var baked = await System.Threading.Tasks.Task.Run(() =>
            {
                IReadOnlyList<CapturedFrame> fs = sampled; var outAnns = anns;
                if (cropOn && fs.Count > 0)
                {
                    int fw = fs[0].W, fh = fs[0].H;
                    var (cx, cy, cw, ch) = FrameCapture.Core.CropState.ToPixelOf(cropSnap, fw, fh);
                    fs = FrameCapture.Drawing.Bake.Crop(fs, cx, cy, cw, ch);
                    outAnns = FrameCapture.Core.CropRemap.Remap(anns, (double)cx / fw, (double)cy / fh, (double)cw / fw, (double)ch / fh);
                }
                return FrameCapture.Drawing.Bake.Apply(fs, outAnns);
            });
            var img = FrameCapture.Encoding.AgentStrip.Render(baked, idxs);
            try { System.Windows.Clipboard.SetImage(img); ShowSnack($"胶片条已复制（{idxs.Count} 帧）"); }
            catch { ShowSnack("剪贴板被占用，生成失败"); }
            Trace.Log($"filmstrip {idxs.Count}/{count} frames every={every}");
        }
        catch (Exception ex)
        {
            Trace.Log("filmstrip: " + ex.Message);
            ShowSnack("生成胶片条失败");
        }
        finally { _encoding = false; SetOkBusy(false); }
    }

    void SaveFrame()
    {
        var dlg = NewSaveDialog("PNG 图片|*.png", $"frame-{_times[_index] / 1000.0:0.0}.png");
        if (dlg.ShowDialog(this) != true) return;
        BgraImage.SavePng(ExportFrame(_index), EnsureExt(dlg.FileName, ".png"));
    }

    void SaveRecording()
    {
        if (_encoding) return;
        // 测试专用（与 FC_TEST_ENCODE_FAIL 同约定）：置 FC_TEST_EXPORT=<路径> 则跳过存盘对话框，
        // 直接按扩展名导出，使「录制→编辑器→ffmpeg 落盘」全链路可确定性 e2e + ffprobe 核对。正常用户环境永不置位。
        string? testExport = System.Environment.GetEnvironmentVariable("FC_TEST_EXPORT");
        if (!string.IsNullOrEmpty(testExport)) { DispatchSave(testExport); return; }
        var def = _settings.Current.RecordFormat;
        string defName = def switch
        {
            RecordFormat.Mp4 => "capture.mp4",
            RecordFormat.WebM => "capture.webm",
            _ => "capture.gif",
        };
        var dlg = NewSaveDialog("MP4 视频|*.mp4|WebM 视频|*.webm|GIF 动图|*.gif", defName);
        // 类型下拉默认选中当前「录制格式」，与默认文件名扩展一致（否则总停在首项 MP4，与 capture.gif 打架）
        dlg.FilterIndex = def switch { RecordFormat.Mp4 => 1, RecordFormat.WebM => 2, _ => 3 };
        if (dlg.ShowDialog(this) != true) return;
        DispatchSave(dlg.FileName);
    }

    /// <summary>按扩展名分派导出：.mp4/.webm 走 ffmpeg，其余走 GIF。</summary>
    void DispatchSave(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".mp4" or ".webm")
        {
            if (FrameCapture.Encoding.FfmpegLocator.Find() == null)
            {
                _hud.Text = "未找到 ffmpeg，无法导出视频 — 安装方法见帮助（按 F1）";
                return;
            }
            var fmt = ext == ".mp4" ? FrameCapture.Encoding.VideoFormat.Mp4 : FrameCapture.Encoding.VideoFormat.WebM;
            path = EnsureExt(path, ext);
            StartEncode(fmt, path, (_, _) =>
            {
                _hud.Text = $"已导出 {System.IO.Path.GetFileName(path)}";
                Trace.Log($"video saved {path}");
            });
        }
        else
        {
            path = EnsureExt(path, ".gif");
            StartEncode(null, null, (_, gif) =>
            {
                try { File.WriteAllBytes(path, gif!); Trace.Log($"gif saved {path} {gif!.Length}B"); }
                catch (Exception ex) { Trace.Log($"gif save failed: {ex.Message}"); _hud.Text = "保存失败"; }
                Show(_index);
            });
        }
    }

    void SaveAfterimage()
    {
        var dlg = NewSaveDialog("PNG 图片|*.png", "afterimage.png");
        if (dlg.ShowDialog(this) != true) return;
        // 残影只稀疏采样 5 帧：用惰性裁剪投影，绝不全段物化（长录制全量转内存必 OOM）
        IReadOnlyList<CapturedFrame> src = _seq.Frames;
        if (_crop.IsCropped && _seq.Frames.Count > 0 && _frameW > 0)
        {
            var (cx, cy, cw, ch) = _crop.ToPixel(_frameW, _frameH);
            src = new LazyCropList(_seq.Frames, cx, cy, cw, ch);
        }
        var combined = BgraImage.Afterimage(src, 5);
        // 合成图同样带标注（R46）：裁剪坐标已随裁剪折算，标注做同一重映射后烘焙
        var anns = _anns.Items.ToList();
        if (_crop.IsCropped && combined.W > 0 && _frameW > 0)
        {
            var (cx, cy, cw, ch) = _crop.ToPixel(_frameW, _frameH);
            anns = FrameCapture.Core.CropRemap.Remap(anns, (double)cx / _frameW, (double)cy / _frameH, (double)cw / _frameW, (double)ch / _frameH);
        }
        var baked = FrameCapture.Drawing.Bake.Apply(new[] { combined }, anns)[0];
        BgraImage.SavePng(baked, EnsureExt(dlg.FileName, ".png"));
    }

    /// <summary>惰性裁剪投影：按需裁剪单帧，不整段物化（残影稀疏采样用）。</summary>
    sealed class LazyCropList : IReadOnlyList<CapturedFrame>
    {
        readonly IReadOnlyList<CapturedFrame> _src; readonly int _x, _y, _w, _h;
        public LazyCropList(IReadOnlyList<CapturedFrame> s, int x, int y, int w, int h) { _src = s; _x = x; _y = y; _w = w; _h = h; }
        public int Count => _src.Count;
        public CapturedFrame this[int i] => FrameCapture.Drawing.Bake.CropFrame(_src[i], _x, _y, _w, _h);
        public IEnumerator<CapturedFrame> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    static string EnsureExt(string path, string ext)
        => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? path : path + ext;

    static Microsoft.Win32.SaveFileDialog NewSaveDialog(string filter, string defaultName)
    {
        // 默认目录与「自动保存副本」保持一致：尊重用户自定义 AutoSavePath，非法/未设则回退 Pictures\FrameCapture
        string dir = FrameCapture.Services.SaveService.ResolveFolder();
        return new() { Filter = filter, FileName = defaultName, InitialDirectory = dir };
    }
}
