using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace FrameCapture.Encoding;

/// <summary>按可执行文件身份缓存编码器能力，兼容 LGPL 和用户安装的 FFmpeg。</summary>
internal static class FfmpegCapabilities
{
    private static readonly ConcurrentDictionary<string, bool> Cache = new();

    /// <summary>优先使用外部构建的 libx264；缺少时使用 h264_mf。</summary>
    /// <param name="path">FFmpeg 的绝对路径。</param>
    /// <returns>需要使用 Media Foundation 时为 true。</returns>
    internal static bool UsesMediaFoundation(string path)
    {
        var info = new FileInfo(path);
        string key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Cache.GetOrAdd(key, _ => Probe(path));
    }

    private static bool Probe(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path, "-hide_banner -encoders")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("FFmpeg capability probe failed to start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("FFmpeg capability probe timed out");
        }
        string text = output.GetAwaiter().GetResult();
        error.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("FFmpeg capability probe failed");
        if (Regex.IsMatch(text, @"(?m)^\s*V\S*\s+libx264\s")) return false;
        if (Regex.IsMatch(text, @"(?m)^\s*V\S*\s+h264_mf\s")) return true;
        throw new InvalidOperationException("FFmpeg requires libx264 or h264_mf for MP4 export");
    }
}
