using System.IO;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Encoding;

namespace FrameCapture.Services;

/// <summary>
/// 捕获产物自动保存副本（R63）：截图存 PNG、录制按「录制格式」存 MP4/WebM（未装 ffmpeg 回退 GIF）到本地目录，剪贴板照常写入。
/// 关闭时零副作用；开启后任何写盘失败只记 trace，绝不影响剪贴板主流程（捕获永远成功）。
/// </summary>
public static class SaveService
{
    /// <summary>解析保存目录：设置指定路径优先（自动建目录），空或非法回退 图片\FrameCapture。</summary>
    public static string ResolveFolder()
    {
        var custom = SettingsService.Shared?.Current.AutoSavePath;
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try { Directory.CreateDirectory(custom); return custom; }
            catch { /* 非法/不可写路径 → 回退默认，绝不因设置炸掉捕获 */ }
        }
        var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FrameCapture");
        try { Directory.CreateDirectory(def); } catch { }
        return def;
    }

    /// <summary>时间戳命名，同秒冲突自动加 (2)(3)…；纯逻辑，可单测。</summary>
    public static string UniqueName(string dir, string stem, string ext)
    {
        var name = $"{stem}.{ext}";
        var full = Path.Combine(dir, name);
        if (!File.Exists(full)) return name;
        for (int n = 2; n < 1000; n++)
        {
            name = $"{stem} ({n}).{ext}";
            if (!File.Exists(Path.Combine(dir, name))) return name;
        }
        return $"{stem} ({Guid.NewGuid():N}).{ext}";
    }

    static string Stamp() => $"FrameCapture {DateTime.Now:yyyy-MM-dd HHmmss}";

    /// <summary>
    /// 编辑器提交：按帧数选自然格式存副本——单帧截图存 PNG、多帧录制按「录制格式」设置
    /// （MP4/WebM 且装了 ffmpeg → 后台编码，不阻塞编辑器关闭；否则 GIF）。均为已烘焙标注/裁剪的最终产物。
    /// </summary>
    public static void MaybeSaveArtifact(CapturedSequence seq, byte[] gif)
    {
        if (SettingsService.Shared?.Current.AutoSaveCopy != true) return;
        try
        {
            var dir = ResolveFolder();
            if (seq.Frames.Count == 1)
            {
                var path = Path.Combine(dir, UniqueName(dir, Stamp(), "png"));
                BgraImage.SavePng(seq.Frames[0], path);
                Trace.Log($"autosave png {path} {seq.Frames[0].W}x{seq.Frames[0].H}");
                return;
            }

            var fmt = SettingsService.Shared.Current.RecordFormat;
            string? ff = fmt == RecordFormat.Gif ? null : FfmpegLocator.Find();
            if (ff == null)
            {
                var gpath = Path.Combine(dir, UniqueName(dir, Stamp(), "gif"));
                File.WriteAllBytes(gpath, gif);
                Trace.Log($"autosave gif {gpath} {gif.Length}B {seq.Frames.Count}f");
                return;
            }

            // 视频：后台编码（编辑器已可关闭，文件稍后出现）。先写临时文件成功再拷进目标名，
            // 保证目录里只出现完整可播产物（应用中途退出不会留下半截文件）。失败 → GIF 兜底不丢产物。
            var vfmt = fmt == RecordFormat.Mp4 ? VideoFormat.Mp4 : VideoFormat.WebM;
            string ext = vfmt == VideoFormat.Mp4 ? "mp4" : "webm";
            var vpath = Path.Combine(dir, UniqueName(dir, Stamp(), ext));
            var frames = seq.Frames; // 已烘焙、内存态，闭包持有，独立于 spool
            System.Threading.Tasks.Task.Run(() =>
            {
                string tmp = Path.Combine(Path.GetTempPath(), $"fc_autosave_{Guid.NewGuid():N}.{ext}");
                try
                {
                    VideoEncoder.Encode(frames, ff, tmp, vfmt, VideoEncoder.FpsFromFrames(frames));
                    File.Copy(tmp, vpath, overwrite: true);
                    Trace.Log($"autosave {ext} {vpath} {new FileInfo(vpath).Length}B {frames.Count}f");
                }
                catch (Exception ex)
                {
                    try { File.WriteAllBytes(Path.ChangeExtension(vpath, ".gif"), gif); Trace.Log($"autosave gif fallback ({ext} 失败): {ex.Message}"); }
                    catch (Exception ex2) { Trace.Log($"autosave failed: {ex2.Message}"); }
                }
                finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
            });
        }
        catch (Exception ex) { Trace.Log($"autosave failed: {ex.Message}"); }
    }

    /// <summary>点按截图：存 PNG 副本（原始帧，无标注）。</summary>
    public static void MaybeSavePng(CapturedFrame frame)
    {
        if (SettingsService.Shared?.Current.AutoSaveCopy != true) return;
        try
        {
            var dir = ResolveFolder();
            var path = Path.Combine(dir, UniqueName(dir, Stamp(), "png"));
            BgraImage.SavePng(frame, path);
            Trace.Log($"autosave png {path} {frame.W}x{frame.H}");
        }
        catch (Exception ex) { Trace.Log($"autosave png failed: {ex.Message}"); }
    }
}
