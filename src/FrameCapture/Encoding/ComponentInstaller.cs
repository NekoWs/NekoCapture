using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace FrameCapture.Encoding;

/// <summary>校验独立下载的 LGPL 组件，在用户目录完成编码自检后提交安装。</summary>
internal static class ComponentInstaller
{
    /// <summary>下载固定构建；取消或失败时保留已安装组件。</summary>
    /// <param name="progress">下载和校验进度。</param>
    /// <param name="ct">下载和安装的取消令牌。</param>
    /// <returns>组件成功提交后完成的任务。</returns>
    internal static async Task InstallAsync(IProgress<ComponentProgress> progress, CancellationToken ct)
    {
        string target = FfmpegComponentInfo.InstallDirectory
            ?? throw new InvalidOperationException("LocalApplicationData is unavailable");
        CheckAncestors(target);
        string parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        using var installLock = new FileStream(Path.Combine(parent, ".ffmpeg-install.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string work = Path.Combine(parent, ".ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string archive = Path.Combine(work, "download.zip");
            await ComponentDownloader.DownloadAsync(archive, progress, ct).ConfigureAwait(false);
            progress.Report(new ComponentProgress(FfmpegComponentInfo.DownloadBytes, 0, true));
            await Task.Run(() => InstallArchive(archive, target, work, ct), ct).ConfigureAwait(false);
        }
        finally { TryRemove(work); }
    }

    /// <summary>校验归档、测试编码后原子替换组件；替换失败恢复已安装目录。</summary>
    /// <param name="archive">固定来源的下载归档。</param>
    /// <param name="target">组件安装绝对路径。</param>
    /// <param name="work">同卷隔离工作目录。</param>
    /// <param name="ct">校验与提交前的取消令牌。</param>
    internal static void InstallArchive(string archive, string target, string work, CancellationToken ct)
    {
        CheckAncestors(target);
        CheckAncestors(work);
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(archive))
        {
            string digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (stream.Length != FfmpegComponentInfo.DownloadBytes ||
                !string.Equals(digest, FfmpegComponentInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("FFmpeg SHA256 mismatch");
        }
        ct.ThrowIfCancellationRequested();
        string content = Path.Combine(work, "content");
        Extract(archive, content, ct);
        string exe = Path.Combine(content, "bin", "ffmpeg.exe");
        string version = Run(exe, "-version", ct);
        string license = Run(exe, "-L", ct);
        if (version.Contains("--enable-gpl") || version.Contains("--enable-nonfree") ||
            !license.Contains("GNU Lesser General Public License"))
            throw new InvalidDataException("FFmpeg must be an LGPL build");
        string codecs = Run(exe, "-hide_banner -encoders", ct);
        if (!codecs.Contains(" h264_mf ") || !codecs.Contains(" libvpx-vp9 "))
            throw new InvalidDataException("FFmpeg lacks required encoders");
        string input = "-hide_banner -loglevel error -y -f lavfi -i color=c=red@0.5:s=64x48:r=10,format=bgra -frames:v 2 ";
        string mp4 = Path.Combine(work, "probe.mp4");
        string webm = Path.Combine(work, "probe.webm");
        Run(exe, input + "-c:v h264_mf -hw_encoding 0 -pix_fmt nv12 -b:v 4000000 -movflags +faststart \"" + mp4 + "\"", ct);
        Run(exe, input + "-c:v libvpx-vp9 -pix_fmt yuva420p -auto-alt-ref 0 -b:v 0 -crf 40 \"" + webm + "\"", ct);
        if (!File.Exists(mp4) || new FileInfo(mp4).Length == 0 || !File.Exists(webm) || new FileInfo(webm).Length == 0)
            throw new InvalidDataException("FFmpeg encoder smoke test produced no output");
        File.WriteAllText(Path.Combine(content, "BUILD.txt"), version + "\n" + license + "\n" +
            FfmpegComponentInfo.DownloadUrl + "\nSHA256 " + FfmpegComponentInfo.Sha256 + "\n" +
            FfmpegComponentInfo.SourceUrl + "\n" + FfmpegComponentInfo.BuildUrl, System.Text.Encoding.UTF8);
        ct.ThrowIfCancellationRequested();
        Commit(content, target);
    }

    /// <summary>只提取运行所需文件及许可证；所有条目都检查边界。</summary>
    /// <param name="archive">ZIP 归档路径。</param>
    /// <param name="destination">全新的隔离目录。</param>
    /// <param name="ct">每个文件之间的取消令牌。</param>
    internal static void Extract(string archive, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string prefix = Path.GetFileNameWithoutExtension(FfmpegComponentInfo.Archive) + "/";
        using var zip = ZipFile.OpenRead(archive);
        long expanded = 0;
        if (zip.Entries.Count > 1000) throw new InvalidDataException("Too many FFmpeg archive entries");
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Split('/').Any(p => p == ".." || p.Contains(":")) ||
                (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("Unsafe FFmpeg archive entry");
            string relative = name.Substring(prefix.Length);
            string full = Path.GetFullPath(Path.Combine(root, relative));
            if (relative.Length == 0) continue;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("FFmpeg archive escapes destination");
            expanded = checked(expanded + entry.Length);
            if (expanded > 250L * 1024 * 1024) throw new InvalidDataException("FFmpeg archive exceeds extraction limit");
            bool binary = relative.StartsWith("bin/", StringComparison.Ordinal) &&
                !relative.Substring(4).Contains("/") &&
                (relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || relative == "bin/ffmpeg.exe");
            if (!binary && relative != "LICENSE.txt") continue;
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full);
        }
        if (!File.Exists(Path.Combine(root, "LICENSE.txt")) || !File.Exists(Path.Combine(root, "bin", "ffmpeg.exe")))
            throw new InvalidDataException("FFmpeg archive lacks runtime or license");
    }

    /// <summary>用同卷目录交换提交，失败时恢复当前组件。</summary>
    /// <param name="content">校验后的组件目录。</param>
    /// <param name="target">最终安装位置。</param>
    internal static void Commit(string content, string target)
    {
        CheckAncestors(content);
        CheckAncestors(target);
        string backup = target + ".backup-" + Guid.NewGuid().ToString("N");
        bool moved = false;
        try
        {
            if (Directory.Exists(target)) { Directory.Move(target, backup); moved = true; }
            Directory.Move(content, target);
        }
        catch
        {
            if (moved && !Directory.Exists(target)) Directory.Move(backup, target);
            throw;
        }
        TryRemove(backup);
    }

    private static string Run(string exe, string arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var process = Process.Start(new ProcessStartInfo(exe, arguments)
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("FFmpeg failed to start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cancellation = ct.Register(() => { try { if (!process.HasExited) process.Kill(); } catch { } });
        if (!process.WaitForExit(30000))
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException("FFmpeg component test timed out");
        }
        ct.ThrowIfCancellationRequested();
        string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("FFmpeg component test failed: " + output);
        return output;
    }

    private static void CheckAncestors(string path)
    {
        if (!Path.IsPathRooted(path)) throw new InvalidDataException("Component path must be absolute");
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Component path contains a reparse point");
    }

    private static void TryRemove(string directory)
    {
        try
        {
            CheckAncestors(directory);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
