using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace FrameCapture.Encoding;

/// <summary>校验按格式独立下载的 GPLv3 精简组件，在用户目录完成编码自检后提交安装。</summary>
internal static class ComponentInstaller
{
    /// <summary>只下载并安装指定格式的编码组件。</summary>
    /// <param name="package">固定组件描述。</param>
    /// <param name="progress">下载与校验进度。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>组件提交后的完成任务。</returns>
    internal static async Task InstallAsync(FfmpegPackage package, IProgress<ComponentProgress> progress, CancellationToken ct)
    {
        string target = package.InstallDirectory
            ?? throw new InvalidOperationException("LocalApplicationData is unavailable");
        CheckAncestors(target);
        string parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        using var installLock = new FileStream(Path.Combine(parent, ".ffmpeg-" + package.Id + "-install.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string work = Path.Combine(parent, ".ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string archive = Path.Combine(work, "download.zip");
            await ComponentDownloader.DownloadAsync(archive, progress, ct, sources: package.DownloadUrls, expectedBytes: package.DownloadBytes, expectedHash: package.Sha256).ConfigureAwait(false);
            progress.Report(new ComponentProgress(package.DownloadBytes, 0, true, package.DownloadBytes));
            await Task.Run(() => InstallArchive(archive, target, work, ct, package), ct).ConfigureAwait(false);
        }
        finally { TryRemove(work); }
    }

    /// <summary>校验归档、测试编码后原子替换组件；替换失败恢复已安装目录。</summary>
    /// <param name="archive">固定来源的下载归档。</param>
    /// <param name="target">组件安装绝对路径。</param>
    /// <param name="work">同卷隔离工作目录。</param>
    /// <param name="ct">校验与提交前的取消令牌。</param>
    /// <param name="package">固定格式描述；缺省使用 MP4 组件。</param>
    internal static void InstallArchive(string archive, string target, string work, CancellationToken ct, FfmpegPackage? package = null)
    {
        package ??= FfmpegComponentInfo.Mp4;
        CheckAncestors(target);
        CheckAncestors(work);
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(archive))
        {
            string digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (stream.Length != package.DownloadBytes ||
                !string.Equals(digest, package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("FFmpeg SHA256 mismatch");
        }
        ct.ThrowIfCancellationRequested();
        string content = Path.Combine(work, "content");
        Extract(archive, content, ct, package);
        string exe = Path.Combine(content, "bin", "ffmpeg.exe");
        string version = Run(exe, "-version", ct);
        string license = Run(exe, "-L", ct);
        if (version.Contains("--enable-nonfree") || !version.Contains("--enable-version3") ||
            !license.Contains("GNU General Public License"))
            throw new InvalidDataException("FFmpeg must be a redistributable GPLv3 build");
        string codecs = Run(exe, "-hide_banner -encoders", ct);
        string encoder = package.Id == "mp4" ? "libx264" : "libvpx-vp9";
        if (!codecs.Contains(" " + encoder + " ")) throw new InvalidDataException("FFmpeg lacks required encoder");
        string raw = Path.Combine(work, "probe.bgra");
        byte[] pixels = new byte[64 * 48 * 4 * 2];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = 255; pixels[i + 3] = 128; }
        File.WriteAllBytes(raw, pixels);
        string output = Path.Combine(work, "probe." + package.Id);
        string input = "-hide_banner -loglevel error -y -f rawvideo -pix_fmt bgra -s 64x48 -framerate 10 -i \"" + raw + "\" -frames:v 2 ";
        string options = package.Id == "mp4"
            ? "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -movflags +faststart"
            : "-c:v libvpx-vp9 -pix_fmt yuva420p -auto-alt-ref 0 -b:v 0 -crf 40";
        Run(exe, input + options + " -threads 2 \"" + output + "\"", ct);
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
            throw new InvalidDataException("FFmpeg encoder smoke test produced no output");
        File.WriteAllText(Path.Combine(content, "BUILD.txt"), version + "\n" + license + "\n" +
            package.DownloadUrl + "\nSHA256 " + package.Sha256 + "\n" +
            FfmpegComponentInfo.SourceUrl + "\n" + FfmpegComponentInfo.BuildUrl, System.Text.Encoding.UTF8);
        ct.ThrowIfCancellationRequested();
        Commit(content, target);
    }

    /// <summary>只提取运行所需文件及许可证；所有条目都检查边界。</summary>
    /// <param name="archive">ZIP 归档路径。</param>
    /// <param name="destination">全新的隔离目录。</param>
    /// <param name="ct">每个文件之间的取消令牌。</param>
    /// <param name="package">固定格式描述；缺省使用 MP4 组件。</param>
    internal static void Extract(string archive, string destination, CancellationToken ct, FfmpegPackage? package = null)
    {
        package ??= FfmpegComponentInfo.Mp4;
        Directory.CreateDirectory(destination);
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string prefix = Path.GetFileNameWithoutExtension(package.Archive) + "/";
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
            if (expanded > 32L * 1024 * 1024) throw new InvalidDataException("FFmpeg archive exceeds extraction limit");
            bool keep = relative == "bin/ffmpeg.exe" || relative == "LICENSE.txt" || relative == "SOURCE.txt"
                || relative.StartsWith("licenses/", StringComparison.Ordinal);
            if (!keep || relative.EndsWith("/", StringComparison.Ordinal)) continue;
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
