using System;
using System.Collections.Generic;
using System.IO;

namespace FrameCapture.Encoding;

/// <summary>按导出格式拆分的固定视频组件与对应源码。</summary>
public static class FfmpegComponentInfo
{
    /// <summary>MP4 的 H.264 精简组件。</summary>
    public static FfmpegPackage Mp4 { get; } = new("mp4", 1329264, "443c25e4999e306ce11496dbe6cb7606eb17e6790fd039f0948cd427caf2560e");
    /// <summary>WebM 的 VP9 与透明通道精简组件。</summary>
    public static FfmpegPackage WebM { get; } = new("webm", 1488073, "a4a0a65edbb36690815c7182a8c61993c90263c797409d021e7faa5d7fe3b8b7");
    /// <summary>固定组件资产所在的发布标签。</summary>
    public const string Release = "v0.9.33";
    /// <summary>完整对应源码与构建配方；与二进制位于同一发布目录。</summary>
    public const string SourceUrl = "https://github.com/NekoWs/FrameCapture/releases/download/" + Release + "/FrameCapture-video-sources-1.tar.gz";
    /// <summary>可审阅的精简构建配方。</summary>
    public const string BuildUrl = "https://github.com/NekoWs/FrameCapture/tree/main/scripts/video-components";
    /// <summary>默认下载组件的归档名称。</summary>
    public static string Archive => Mp4.Archive;
    /// <summary>默认组件的固定下载地址。</summary>
    public static string DownloadUrl => Mp4.DownloadUrl;
    /// <summary>默认组件的竞速下载来源。</summary>
    public static IReadOnlyList<string> DownloadUrls => Mp4.DownloadUrls;
    /// <summary>默认组件的固定完整摘要。</summary>
    public static string Sha256 => Mp4.Sha256;
    /// <summary>默认组件的归档大小。</summary>
    public static long DownloadBytes => Mp4.DownloadBytes;
    /// <summary>当前用户的视频组件根目录，兼容完整组件布局。</summary>
    public static string? InstallDirectory
    {
        get
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrWhiteSpace(local) ? null : Path.Combine(local, "FrameCapture", "Components", "FFmpeg");
        }
    }
    /// <summary>完整组件布局的进程入口。</summary>
    public static string? ExecutablePath => InstallDirectory is { } dir ? Path.Combine(dir, "bin", "ffmpeg.exe") : null;
}

/// <summary>单种视频格式所需的精简 FFmpeg 归档与独立安装目录。</summary>
public sealed class FfmpegPackage
{
    /// <summary>创建固定版本、大小与摘要的组件描述。</summary>
    /// <param name="id">导出格式标识。</param>
    /// <param name="bytes">归档的完整字节数。</param>
    /// <param name="hash">归档 SHA256。</param>
    internal FfmpegPackage(string id, long bytes, string hash) { Id = id; DownloadBytes = bytes; Sha256 = hash; }
    /// <summary>组件对应的导出格式标识。</summary>
    public string Id { get; }
    /// <summary>组件的用户可见格式名。</summary>
    public string Label => Id == "mp4" ? "MP4" : "WebM";
    /// <summary>归档字节数，作为下载上限与进度分母。</summary>
    public long DownloadBytes { get; }
    /// <summary>内置固定的归档 SHA256。</summary>
    public string Sha256 { get; }
    /// <summary>精简组件归档名称。</summary>
    public string Archive => "FrameCapture-video-" + Id + "-1-win-x64.zip";
    /// <summary>固定归档的上游地址。</summary>
    public string DownloadUrl => "https://github.com/NekoWs/FrameCapture/releases/download/" + FfmpegComponentInfo.Release + "/" + Archive;
    /// <summary>国内加速来源与可信上游；内容共同受固定摘要约束。</summary>
    public IReadOnlyList<string> DownloadUrls => Array.AsReadOnly(new[] { "https://gh-proxy.com/" + DownloadUrl, "https://gh-proxy.org/" + DownloadUrl, DownloadUrl });
    /// <summary>当前用户、当前格式的独立安装目录。</summary>
    public string? InstallDirectory => FfmpegComponentInfo.InstallDirectory is { } dir ? Path.Combine(dir, Id) : null;
    /// <summary>当前格式的独立编码进程入口。</summary>
    public string? ExecutablePath => InstallDirectory is { } dir ? Path.Combine(dir, "bin", "ffmpeg.exe") : null;
}
