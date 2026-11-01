using System.IO;
using NekoCapture.Capture;
using NekoCapture.Core;
using NekoCapture.Encoding;

namespace NekoCapture.Services;

/// <summary>行为图等待文件完整写入的保存出口。</summary>
public static partial class SaveService
{
    /// <summary>按录制格式保存序列；编码与临时文件完成后才报告成功。</summary>
    /// <param name="sequence">调用期间持续有效的帧序列。</param>
    /// <returns>目标文件是否完整保存。</returns>
    public static async Task<bool> SaveSequenceAsync(CapturedSequence sequence)
    {
        sequence = BakedFrameList.Retain(sequence);
        using var reader = sequence.Frames as BakedFrameList;
        var settings = SettingsService.Shared?.Current;
        var format = settings?.RecordFormat ?? RecordFormat.Gif;
        string? ffmpeg = format == RecordFormat.Gif ? null : await BackgroundWork.Run(() => FfmpegLocator.Find(format == RecordFormat.Mp4 ? VideoFormat.Mp4 : VideoFormat.WebM));
        string extension = ffmpeg == null ? "gif" : format == RecordFormat.Mp4 ? "mp4" : "webm";
        string dir = await BackgroundWork.Run(ResolveFolder);
        string path = await BackgroundWork.Run(() => Path.Combine(dir, UniqueName(dir, Stamp(), extension)));
        var matte = HexColor.Parse(settings?.VideoMatteColor ?? "#000000");
        return await BackgroundWork.Run(() =>
        {
            string temp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp." + extension);
            try
            {
                var frames = sequence.Frames;
                if (frames.Count == 0) return false;
                if (ffmpeg != null)
                    VideoEncoder.Encode(frames, ffmpeg, temp,
                        format == RecordFormat.Mp4 ? VideoFormat.Mp4 : VideoFormat.WebM,
                        VideoEncoder.FpsFromFrames(frames), matte: matte);
                else
                {
                    int fallback = 1000 / Math.Max(1, sequence.Fps);
                    var delays = Enumerable.Range(0, frames.Count).Select(i => i + 1 < frames.Count
                        ? Math.Max(10, (int)(FrameMetadata.TimeAt(frames, i + 1) - FrameMetadata.TimeAt(frames, i))) : Math.Max(10, fallback)).ToArray();
                    using var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    GifEncoder.Write(file, frames, delays);
                }
                File.Move(temp, path);
                return true;
            }
            catch (Exception ex) { Trace.Log($"save sequence: {ex.Message}"); return false; }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        });
    }
}
