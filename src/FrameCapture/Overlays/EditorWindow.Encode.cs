using System.IO;
using System.Windows;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Encoding;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>录制逐帧导出与结果租约交接。</summary>
public sealed partial class EditorWindow : Window
{
    /// <summary>固定编辑状态后逐帧编码；文件输出先写同目录临时文件，成功后替换目标。</summary>
    async void StartEncode(VideoFormat? videoFmt, string? outPath, Func<CapturedSequence, byte[]?, Task> onDone)
    {
        if (_encoding) return;
        _encoding = true;
        SetOkBusy(true);
        using var cancellation = new CancellationTokenSource();
        _commitCts = cancellation;
        var ct = cancellation.Token;
        try
        {
            var (a, b) = TrimRange;
            RectI? crop = null;
            var region = _seq.Region;
            if (_crop.IsCropped)
            {
                var first = FrameMetadata.At(_seq.Frames, a);
                var (x, y, w, h) = CropState.ToPixelOf(_crop.Current, first.W, first.H);
                crop = new RectI(x, y, w, h);
                region = new RectI(region.X + x, region.Y + y, w, h);
            }
            using var frames = BakedFrameList.Create(_seq.Frames, a, b - a + 1, _anns.Items, crop);
            var sequence = new CapturedSequence
            {
                Frames = frames, Region = region, MonitorBounds = _seq.MonitorBounds,
                MonitorDevice = _seq.MonitorDevice, Fps = _fps, DroppedFrames = _seq.DroppedFrames,
            };
            var matte = HexColor.Parse(_settings.Current.VideoMatteColor);
            string verb = videoFmt == null ? "GIF" : videoFmt == VideoFormat.Mp4 ? "MP4" : "WebM";
            SetExportProgress(Loc.F(K.Editor.Exporting, verb));
            var progress = new ExportProgress(frames.Count, done => Dispatcher.BeginInvoke(() =>
            {
                if (!_userClosed && !ct.IsCancellationRequested && ReferenceEquals(_commitCts, cancellation))
                    SetExportProgress(verb, done, frames.Count);
            }, System.Windows.Threading.DispatcherPriority.Background));
            bool needsGif = !ManagedOutput || _settings.Current.AutoSaveCopy || outPath != null;
            byte[]? gif = await BackgroundWork.Run(() =>
            {
                if (Environment.GetEnvironmentVariable("FC_TEST_ENCODE_FAIL") == "1")
                    throw new InvalidOperationException("forced encode failure (FC_TEST_ENCODE_FAIL=1)");
                string? temporary = outPath == null ? null : Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(outPath))!,
                    $".fc-export-{Guid.NewGuid():N}{Path.GetExtension(outPath)}");
                try
                {
                    byte[]? bytes = null;
                    if (videoFmt == null)
                    {
                        if (!needsGif) return (byte[]?)null;
                        var delays = ComputeDelays(frames, _fps);
                        if (temporary == null) bytes = GifEncoder.Encode(frames, delays, true, progress, ct);
                        else
                        {
                            using var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                            GifEncoder.Write(file, frames, delays, true, progress, ct);
                        }
                    }
                    else
                    {
                        string ffmpeg = FfmpegLocator.Find(videoFmt.Value)
                            ?? throw new InvalidOperationException("ffmpeg not found");
                        VideoEncoder.Encode(frames, ffmpeg, temporary!, videoFmt.Value,
                            VideoEncoder.FpsFromFrames(frames), progress, ct, matte);
                    }
                    ct.ThrowIfCancellationRequested();
                    if (temporary != null) File.Move(temporary, outPath!, overwrite: true);
                    return bytes;
                }
                finally
                {
                    if (temporary != null) try { File.Delete(temporary); } catch { }
                }
            }, ct);
            if (!_userClosed) await onDone(sequence, gif);
        }
        catch (OperationCanceledException)
        {
            Trace.Log("encode cancelled");
            if (!_userClosed) Show(_index);
        }
        catch (Exception ex)
        {
            Trace.Log($"encode failed: {ex}");
            if (!_userClosed)
            {
                _hud.Text = Loc.T(K.Editor.ExportFailed);
                ShowSnack(Loc.T(K.Editor.ExportFailed));
                OutputFailed?.Invoke();
            }
        }
        finally
        {
            if (ReferenceEquals(_commitCts, cancellation)) _commitCts = null;
            _encoding = false;
            if (!_userClosed) SetOkBusy(false);
        }
    }

    async Task<CapturedFrame> BakeFrameAsync(int index)
    {
        RectI? crop = null;
        if (_crop.IsCropped)
        {
            var (x, y, w, h) = _crop.ToPixel(_frameW, _frameH);
            crop = new RectI(x, y, w, h);
        }
        using var frames = BakedFrameList.Create(_seq.Frames, index, 1, _anns.Items, crop);
        return await BackgroundWork.Run(() => frames[0]);
    }

    void PublishOutput(CapturedSequence sequence)
    {
        if (OutputReady is not { } handlers) return;
        foreach (Action<CapturedSequence> handler in handlers.GetInvocationList())
        {
            var retained = BakedFrameList.Retain(sequence);
            try { handler(retained); }
            catch
            {
                (retained.Frames as BakedFrameList)?.Dispose();
                throw;
            }
        }
    }

    sealed class ExportProgress(int count, Action<int> publish) : IProgress<int>
    {
        long _last;

        /// <summary>合并进度更新，避免长录像导出挤占界面消息队列。</summary>
        /// <param name="value">已处理到的帧数。</param>
        public void Report(int value)
        {
            long now = Environment.TickCount64;
            if (value < count && now - _last < 80) return;
            _last = now;
            publish(value);
        }
    }
}
