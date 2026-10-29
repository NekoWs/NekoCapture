using System.IO;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Encoding;

namespace FrameCapture.Services;

/// <summary>
/// 捕获产物自动保存副本：截图存 PNG、录制按「录制格式」存 MP4/WebM（未装 ffmpeg 回退 GIF）到本地目录，剪贴板照常写入。
/// 关闭时零副作用；开启后任何写盘失败只记 trace，绝不影响剪贴板主流程（捕获永远成功）。
/// </summary>
public static partial class SaveService
{
    /// <summary>将静图保存为 PNG，与自动保存开关无关。</summary>
    /// <param name="frame">只读 BGRA 帧。</param>
    /// <returns>文件是否完整保存。</returns>
    public static bool SavePng(CapturedFrame frame)
    {
        try
        {
            var dir = ResolveFolder();
            BgraImage.SavePng(frame, Path.Combine(dir, UniqueName(dir, Stamp(), "png")));
            return true;
        }
        catch (Exception ex) { Trace.Log($"save png failed: {ex.Message}"); return false; }
    }

    /// <summary>解析保存目录：设置指定路径优先（自动建目录），空或非法回退 图片\FrameCapture。</summary>
    /// <returns>绝对路径，目录已尽力创建。设置里的路径建不出来（非法字符 / 无写权限）时
    /// 直接回退默认目录——绝不因设置炸掉捕获；默认目录本身建不出来也照样返回，写盘失败由调用方吞掉。</returns>
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
    /// <param name="dir">用于探测同名冲突的目录，须已存在；本方法不创建目录。</param>
    /// <param name="stem">主名，不含扩展名分隔点。</param>
    /// <param name="ext">扩展名，<b>不带</b>点（如 <c>png</c>）。</param>
    /// <returns>仅文件名（不含目录）。依次试 <c>stem.ext</c>、<c>stem (2).ext</c> … 直到 <c>stem (999)</c>；
    /// 全撞上则退 GUID 主名，保证同一秒内连拍也不会互相覆盖。</returns>
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
    /// <param name="seq">编辑器提交的序列。<c>Frames</c> 可能是磁盘暂存的惰性列表，本方法只在需要时按索引取帧，
    /// 不整段物化。视频分支持有独立烘焙帧租约，任务完成时释放。</param>
    /// <param name="gif">已编码好的 GIF 字节：无 ffmpeg 时它就是产物，视频编码失败时是兜底产物。调用方必然备妥。</param>
    public static void MaybeSaveArtifact(CapturedSequence seq, byte[] gif)
    {
        if (SettingsService.Shared?.Current.AutoSaveCopy != true) return;
        try
        {
            var dir = ResolveFolder();
            if (seq.Frames.Count == 1)
            {
                var path = Path.Combine(dir, UniqueName(dir, Stamp(), "png"));
                var frame = seq.Frames[0];
                BgraImage.SavePng(frame, path);
                Trace.Log($"autosave png {path} {frame.W}x{frame.H}");
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
            // MP4 没有透明通道：喂帧前按用户选的「边框填充色」拍平；WebM 自己带 alpha，这个颜色对它不起作用。
            var matte = Core.HexColor.Parse(SettingsService.Shared.Current.VideoMatteColor);
            string ext = vfmt == VideoFormat.Mp4 ? "mp4" : "webm";
            var vpath = Path.Combine(dir, UniqueName(dir, Stamp(), ext));
            var hint = SavingHint.Start(Path.GetFileName(vpath));
            var frames = BakedFrameList.Retain(seq).Frames;
            BackgroundWork.Run(() =>
            {
                using var reader = frames as BakedFrameList;
                string tmp = Path.Combine(dir, $".fc-autosave-{Guid.NewGuid():N}.{ext}");
                bool ok = false;
                try
                {
                    VideoEncoder.Encode(frames, ff, tmp, vfmt, VideoEncoder.FpsFromFrames(frames),
                        matte: matte);
                    File.Move(tmp, vpath, overwrite: true);
                    Trace.Log($"autosave {ext} {vpath} {new FileInfo(vpath).Length}B {frames.Count}f");
                    ok = true;
                }
                catch (Exception ex)
                {
                    try { File.WriteAllBytes(Path.ChangeExtension(vpath, ".gif"), gif); Trace.Log($"autosave gif fallback ({ext} 失败): {ex.Message}"); }
                    catch (Exception ex2) { Trace.Log($"autosave failed: {ex2.Message}"); }
                }
                finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
                return ok;
            }).ContinueWith(t =>
            {
                hint.Done(t.IsCompletedSuccessfully && t.Result);
                // 编码任务结束后请求优化回收，不主动裁剪工作集。
                Memory.Reclaim();
            }, TaskScheduler.Default);
        }
        catch (Exception ex) { Trace.Log($"autosave failed: {ex.Message}"); }
    }

    /// <summary>点按截图：存 PNG 副本（原始帧，无标注）。</summary>
    /// <param name="frame">待存的原始帧，BGRA 长度须为 <c>W*H*4</c>。本方法只读不改。</param>
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

    /// <summary>
    /// 后台编码的轻提示：只在慢的那次开口。半秒内写完不吭声——文件本来就在眼前出现，
    /// 弹一条只是打断；超过半秒人已经开始找文件，先说「正在保存视频…」，写完换成「已保存 文件名」。
    /// 编码失败不报「已保存」：兜底 GIF 会静默落盘，产物没丢，也不该拿成功的话骗人。
    /// </summary>
    sealed class SavingHint
    {
        /// <summary>等多久没写完才开口（ms）。短于这个时长，文件出现得比读完一条提示还快。</summary>
        const int PatienceMs = 600;

        readonly string _name;
        System.Windows.Threading.DispatcherTimer? _timer;
        bool _shown;

        SavingHint(string name)
        {
            _name = name;
            if (System.Windows.Application.Current is null) return;   // 没有 UI 的宿主：整条提示静默
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PatienceMs) };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                _shown = true;
                Overlays.NoticeToast.Show(Loc.T(K.Capture.SavingVideo), _name, seconds: 8);
            };
            _timer.Start();
        }

        /// <summary>开始计时，须在 UI 线程调用——提示本身也在那里画。</summary>
        /// <param name="name">报给用户的文件名，不含目录。</param>
        /// <returns>提示句柄；编码结束时交给 <see cref="Done"/>。</returns>
        public static SavingHint Start(string name) => new(name);

        /// <summary>编码收尾：没来得及弹的收回；已经弹出去的，成功就换成「已保存」，失败就让那条自己到期。</summary>
        /// <param name="ok">产物是否已完整落到目标文件。兜底 GIF 不算成功。</param>
        public void Done(bool ok)
        {
            var app = System.Windows.Application.Current;
            if (app is null) { _timer?.Stop(); return; }
            app.Dispatcher.BeginInvoke(() =>
            {
                _timer?.Stop();
                if (_shown && ok) Overlays.NoticeToast.Show(Loc.F(K.Capture.SavedFileName, _name), seconds: 3);
            });
        }
    }
}
