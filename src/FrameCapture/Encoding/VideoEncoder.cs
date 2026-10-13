using System.Diagnostics;
using System.Globalization;
using System.IO;
using FrameCapture.Capture;

namespace FrameCapture.Encoding;

/// <summary>输出容器与编码器：MP4 走 libx264，WebM 走 libvpx-vp9，两者都固定 yuv420p。</summary>
public enum VideoFormat { Mp4 = 0, WebM = 1 }

/// <summary>
/// 定位本机 ffmpeg。项目离线、不捆绑 ffmpeg（体积 + 许可），因此运行时探测：
/// 先扫 PATH，再查常见安装目录。找不到返回 null，调用方回退到 GIF 并提示用户安装。
/// </summary>
public static class FfmpegLocator
{
    /// <summary>找本机 ffmpeg.exe。</summary>
    /// <returns>可执行文件绝对路径；null = 本机没有 ffmpeg，调用方须回退 GIF 并提示安装。</returns>
    public static string? Find()
    {
        // 1) PATH（用户自行装到 PATH 的情形）
        string pathVar;
        try { pathVar = Environment.GetEnvironmentVariable("PATH") ?? ""; }
        catch { pathVar = ""; }
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var cand = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(cand)) return cand;
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
        yield return @"C:\Program Files\FFmpeg\bin\ffmpeg.exe";
        yield return @"C:\Program Files (x86)\FFmpeg\bin\ffmpeg.exe";
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
        {
            yield return Path.Combine(local, @"Microsoft\WinGet\Links\ffmpeg.exe");
            yield return Path.Combine(local, @"Programs\ffmpeg\bin\ffmpeg.exe");
        }
    }
}

/// <summary>
/// 把 BGRA 帧经 stdin 管道喂给 ffmpeg，产出 MP4(libx264) / WebM(libvpx-vp9)。
/// 高刷录制的正解：GIF 受格式限制封顶 ~50fps 且体积巨大，MP4/WebM 才能忠实落盘高帧率。
/// </summary>
public static class VideoEncoder
{
    /// <summary>由帧时间戳推平均帧率（保证总时长正确：静态段少帧 → 低 fps，但总时长不变）。</summary>
    /// <param name="frames">帧序列，取首末帧 TimeMs 之差作总时长。</param>
    /// <returns>平均帧率，钳制在 1–240。不足 2 帧或时间戳非递增时回退 30。</returns>
    public static double FpsFromFrames(IReadOnlyList<CapturedFrame> frames)
    {
        if (frames.Count < 2) return 30;
        double dur = frames[^1].TimeMs - frames[0].TimeMs;
        if (dur <= 0) return 30;
        return Math.Clamp((frames.Count - 1) / (dur / 1000.0), 1, 240);
    }

    /// <summary>拼 ffmpeg 命令行（纯函数，便于单测）。rawvideo BGRA 从 stdin 进，按 fmt 选编码器。
    /// yuv420p 要求宽高为偶数，而用户选区可能是奇数（如 641×401）→ 奇数时插一条向下取偶的 scale
    /// 滤镜（641→640、401→400），否则 libx264/vp9 直接报 "width not divisible by 2" 编码失败。</summary>
    /// <param name="w">输入帧宽（像素），奇数时触发补偶 scale 滤镜。</param>
    /// <param name="h">输入帧高（像素），奇数时同样触发。</param>
    /// <param name="fps">帧率，按 "0.###" 且不变文化格式化，避免区域小数点变成逗号。</param>
    /// <param name="fmt">容器与编码器选择。</param>
    /// <param name="outPath">输出文件绝对路径，带引号拼在末尾。</param>
    /// <returns>ffmpeg 的参数串（不含可执行文件名），可直接给 ProcessStartInfo.Arguments。</returns>
    public static string BuildArgs(int w, int h, double fps, VideoFormat fmt, string outPath)
    {
        string fpsS = fps.ToString("0.###", CultureInfo.InvariantCulture);
        string codec = fmt == VideoFormat.Mp4
            ? "-c:v libx264 -pix_fmt yuv420p -preset ultrafast -movflags +faststart"
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
    /// <exception cref="ArgumentException">帧序列为空，或首帧尺寸非法、某帧缓冲不足。</exception>
    /// <exception cref="FileNotFoundException">ffmpegPath 指向的文件不存在。</exception>
    /// <exception cref="InvalidOperationException">ffmpeg 启动失败、退出码非 0 或未产出文件。</exception>
    /// <exception cref="TimeoutException">关闭 stdin 后 120s 内 ffmpeg 未退出。</exception>
    public static void Encode(IReadOnlyList<CapturedFrame> frames, string ffmpegPath, string outPath,
        VideoFormat fmt, double fps, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (frames.Count == 0) throw new ArgumentException("no frames");
        if (!File.Exists(ffmpegPath)) throw new FileNotFoundException("ffmpeg not found", ffmpegPath);
        int w = frames[0].W, h = frames[0].H;
        if (w <= 0 || h <= 0) throw new ArgumentException($"bad size {w}x{h}");
        fps = Math.Clamp(fps, 1, 240);

        var psi = new ProcessStartInfo(ffmpegPath, BuildArgs(w, h, fps, fmt, outPath))
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process? p = null;
        try
        {
            p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg start returned null");
            // 先挂上 stderr 异步读取：ffmpeg 写日志时若 stderr 缓冲满会阻塞，与 stdin 写互锁 → 死锁。
            Task<string> errTask = p.StandardError.ReadToEndAsync();

            var stdin = p.StandardInput.BaseStream;
            int frameBytes = w * 4 * h;
            for (int i = 0; i < frames.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                byte[] bgra = frames[i].Bgra;
                if (bgra.Length < frameBytes)
                    throw new InvalidOperationException($"frame {i} too small: {bgra.Length} < {frameBytes}");
                stdin.Write(bgra, 0, frameBytes);
                progress?.Report(i + 1);
            }
            stdin.Flush();
            stdin.Close();

            string err = errTask.GetAwaiter().GetResult();
            if (!p.WaitForExit(120000))
                throw new TimeoutException("ffmpeg 未在 120s 内退出");
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
