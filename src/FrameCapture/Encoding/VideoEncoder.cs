using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using FrameCapture.Capture;
using FrameCapture.Services;

namespace FrameCapture.Encoding;

/// <summary>视频输出容器；MP4 使用 H.264，WebM 使用 VP9 并按需保留透明。</summary>
public enum VideoFormat { Mp4 = 0, WebM = 1 }

/// <summary>
/// 定位独立安装的 FFmpeg 组件或系统 FFmpeg；不通过主程序加载 FFmpeg DLL。
/// </summary>
public static class FfmpegLocator
{
    /// <summary>找本机 ffmpeg.exe。</summary>
    /// <returns>可执行文件绝对路径；null = 本机没有 ffmpeg，调用方须回退 GIF 并提示安装。</returns>
    public static string? Find()
    {
        var component = FfmpegComponentInfo.ExecutablePath;
        if (component != null && File.Exists(component)) return component;
        // 1) PATH（用户自行装到 PATH 的情形）
        string pathVar;
        try { pathVar = Environment.GetEnvironmentVariable("PATH") ?? ""; }
        catch { pathVar = ""; }
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var cand = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (Path.IsPathFullyQualified(cand) && File.Exists(cand)) return Path.GetFullPath(cand);
            }
            catch { /* 非法路径片段跳过 */ }
        }
        // 2) 常见安装位置（winget Gyan.FFmpeg、choco、手动装 Program Files）
        foreach (var cand in CommonPaths())
            try { if (File.Exists(cand)) return cand; } catch { }
        return null;
    }

    private static IEnumerable<string> CommonPaths()
    {
        // 系统盘不一定是 C:，写死盘符会漏掉装在别的卷上的 ffmpeg
        foreach (var pf in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
            if (!string.IsNullOrEmpty(pf))
                yield return Path.Combine(pf, @"FFmpeg\bin\ffmpeg.exe");
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
        {
            yield return Path.Combine(local, @"Microsoft\WinGet\Links\ffmpeg.exe");
            yield return Path.Combine(local, @"Programs\ffmpeg\bin\ffmpeg.exe");
        }
    }
}

/// <summary>
/// 将 BGRA 帧通过标准输入传给独立 FFmpeg 进程，输出 MP4 或 WebM。
/// </summary>
public static class VideoEncoder
{
    /// <summary>由帧时间戳推平均帧率（保证总时长正确：静态段少帧 → 低 fps，但总时长不变）。</summary>
    /// <param name="frames">帧序列，取首末帧 TimeMs 之差作总时长。</param>
    /// <returns>平均帧率，钳制在 1–240。不足 2 帧或时间戳非递增时回退 30。</returns>
    public static double FpsFromFrames(IReadOnlyList<CapturedFrame> frames)
    {
        if (frames.Count < 2) return 30;
        double dur = FrameMetadata.TimeAt(frames, frames.Count - 1) - FrameMetadata.TimeAt(frames, 0);
        if (dur <= 0) return 30;
        return Math.Clamp((frames.Count - 1) / (dur / 1000.0), 1, 240);
    }

    /// <summary>这组帧里有没有要保留的透明。抽首/中/末三帧看 alpha 通道：
    /// 窗口录制的透明从第一帧起就在（圆角与投影全程都在），区域录制全程不透明。</summary>
    /// <param name="frames">待编码帧。</param>
    /// <returns>true = 至少一帧有 alpha 不满的像素。</returns>
    public static bool CarriesAlpha(IReadOnlyList<CapturedFrame> frames)
    {
        if (frames.Count == 0) return false;
        foreach (int i in new[] { 0, frames.Count / 2, frames.Count - 1 })
        {
            var bgra = frames[i].Bgra;
            for (int o = 3; o < bgra.Length; o += 4)
                if (bgra[o] < 250) return true;
        }
        return false;
    }

    /// <summary>拼 ffmpeg 命令行（纯函数，便于单测）。rawvideo BGRA 从 stdin 进，按 fmt 选编码器。
    /// yuv420p 要求宽高为偶数，而用户选区可能是奇数（如 641×401）→ 奇数时插一条向下取偶的 scale
    /// 滤镜（641→640、401→400），否则 libx264/vp9 直接报 "width not divisible by 2" 编码失败。</summary>
    /// <param name="w">输入帧宽（像素），奇数时触发补偶 scale 滤镜。</param>
    /// <param name="h">输入帧高（像素），奇数时同样触发。</param>
    /// <param name="fps">帧率，按 "0.###" 且不变文化格式化，避免区域小数点变成逗号。</param>
    /// <param name="fmt">容器与编码器选择。</param>
    /// <param name="outPath">输出文件绝对路径，带引号拼在末尾。</param>
    /// <param name="alpha">true = 产物要带透明通道：只有 WebM 认，vp9 走 yuva420p 并关掉 alt-ref
    /// （开着时参考帧会把 alpha 抹平）。MP4 的 h264 没有透明通道，这条参数救不了，
    /// 透明由调用方在喂帧前按用户选的填充色拍平。</param>
    /// <param name="mediaFoundation">MP4 使用 Windows Media Foundation H.264 软件编码。</param>
    /// <returns>ffmpeg 的参数串（不含可执行文件名），可直接给 ProcessStartInfo.Arguments。</returns>
    public static string BuildArgs(int w, int h, double fps, VideoFormat fmt, string outPath, bool alpha = false,
        bool mediaFoundation = false)
    {
        string fpsS = fps.ToString("0.###", CultureInfo.InvariantCulture);
        string codec = fmt == VideoFormat.Mp4
            ? mediaFoundation
                ? $"-c:v h264_mf -hw_encoding 0 -pix_fmt nv12 -b:v {Math.Clamp((long)(w * (double)h * fps * 0.15), 4_000_000, 100_000_000)} -movflags +faststart"
                : "-c:v libx264 -pix_fmt yuv420p -preset ultrafast -movflags +faststart"
            : alpha
                ? "-c:v libvpx-vp9 -pix_fmt yuva420p -auto-alt-ref 0 -b:v 0 -crf 40"
                : "-c:v libvpx-vp9 -pix_fmt yuv420p -b:v 0 -crf 40";
        string vf = (w % 2 != 0 || h % 2 != 0)
            ? " -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\""
            : "";
        return $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt bgra -s {w}x{h} -framerate {fpsS} -i pipe:0{vf} {codec} \"{outPath}\"";
    }

    /// <summary>
    /// 编码 frames 到 outPath。progress 报告已写入帧数；ct 取消则杀 ffmpeg 并删半成品。
    /// 帧尺寸须一致（裁剪/烘焙后已保证）。stderr 异步排空，避免管道写满死锁。
    /// <b>任何失败路径都会杀掉 ffmpeg 进程并删掉半成品</b>，不留半个文件。
    /// </summary>
    /// <param name="frames">待编码帧，每一帧的字节数都必须够 w*h*4。</param>
    /// <param name="ffmpegPath">ffmpeg.exe 绝对路径，取 <see cref="FfmpegLocator.Find"/> 的结果。</param>
    /// <param name="outPath">输出文件路径；已存在的同名文件会被 ffmpeg 以 -y 覆盖。</param>
    /// <param name="fmt">容器与编码器。</param>
    /// <param name="fps">帧率，钳制到 1–240。</param>
    /// <param name="progress">报告已写入帧数（计数，不是百分比），可为 null。</param>
    /// <param name="ct">取消令牌；取消即按失败路径处理并抛 <see cref="OperationCanceledException"/>。</param>
    /// <param name="matte">MP4 这类无透明通道容器的填充色：帧里有透明时，喂给 ffmpeg 前先拍平到这个颜色
    /// （只取 RGB，alpha 忽略）。WebM 与 GIF 自带透明通道，这个参数对它们无作用。</param>
    /// <exception cref="ArgumentException">帧序列为空，或首帧尺寸非法、某帧缓冲不足。</exception>
    /// <exception cref="FileNotFoundException">ffmpegPath 指向的文件不存在。</exception>
    /// <exception cref="InvalidOperationException">ffmpeg 启动失败、退出码非 0 或未产出文件。</exception>
    /// <exception cref="TimeoutException">关闭 stdin 后 120s 内 ffmpeg 未退出。</exception>
    public static void Encode(IReadOnlyList<CapturedFrame> frames, string ffmpegPath, string outPath,
        VideoFormat fmt, double fps, IProgress<int>? progress = null, CancellationToken ct = default,
        Color? matte = null)
    {
        if (frames.Count == 0) throw new ArgumentException("no frames");
        if (!File.Exists(ffmpegPath)) throw new FileNotFoundException("ffmpeg not found", ffmpegPath);
        int w = frames[0].W, h = frames[0].H;
        if (w <= 0 || h <= 0) throw new ArgumentException($"bad size {w}x{h}");
        fps = Math.Clamp(fps, 1, 240);

        bool hasAlpha = CarriesAlpha(frames);
        bool alpha = fmt == VideoFormat.WebM && hasAlpha;
        bool flatten = fmt == VideoFormat.Mp4 && hasAlpha && matte is { } mt;

        bool mediaFoundation = fmt == VideoFormat.Mp4 && FfmpegCapabilities.UsesMediaFoundation(ffmpegPath);
        var psi = new ProcessStartInfo(ffmpegPath, BuildArgs(w, h, fps, fmt, outPath, alpha, mediaFoundation))
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process? p = null;
        using var cancellation = ct.Register(() =>
        {
            try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); } catch { }
        });
        try
        {
            p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg start returned null");
            ct.ThrowIfCancellationRequested();
            // 先挂上 stderr 异步读取：ffmpeg 写日志时若 stderr 缓冲满会阻塞，与 stdin 写互锁 → 死锁。
            Task<string> errTask = p.StandardError.ReadToEndAsync();

            var stdin = p.StandardInput.BaseStream;
            int frameBytes = w * 4 * h;
            for (int i = 0; i < frames.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                byte[] bgra = flatten
                    ? BgraImage.FlattenOver(frames[i], mt.R, mt.G, mt.B).Bgra
                    : frames[i].Bgra;
                if (bgra.Length < frameBytes)
                    throw new InvalidOperationException($"frame {i} too small: {bgra.Length} < {frameBytes}");
                stdin.Write(bgra, 0, frameBytes);
                progress?.Report(i + 1);
            }
            stdin.Flush();
            stdin.Close();

            if (!p.WaitForExit(120000))
                throw new TimeoutException("ffmpeg 未在 120s 内退出");
            ct.ThrowIfCancellationRequested();
            string err = errTask.GetAwaiter().GetResult();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg 退出码 {p.ExitCode}：{Tail(err)}");
            if (!File.Exists(outPath) || new FileInfo(outPath).Length == 0)
                throw new InvalidOperationException("ffmpeg 未产出文件");
        }
        catch
        {
            try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); } catch { }
            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
            throw;
        }
        finally { p?.Dispose(); }
    }

    private static string Tail(string s)
    {
        s = (s ?? "").Trim();
        return s.Length <= 400 ? s : "…" + s[^400..];
    }
}
