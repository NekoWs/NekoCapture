using System;
using System.IO;

namespace FrameCapture.Encoding;

/// <summary>独立在线组件的固定来源、完整性约束和用户级安装位置。</summary>
public static class FfmpegComponentInfo
{
    /// <summary>由发布者保留两年的月末构建。</summary>
    public const string Release = "autobuild-2026-09-30-13-08";
    /// <summary>LGPL shared x64 组件归档名称。</summary>
    public const string Archive = "ffmpeg-n8.1.3-9-g29e619e767-win64-lgpl-shared-8.1.zip";
    /// <summary>固定构建的下载地址；不跟随 latest。</summary>
    public const string DownloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/" + Release + "/" + Archive;
    /// <summary>由发布者 GitHub Release 资产摘要核对的归档 SHA256。</summary>
    public const string Sha256 = "3e47bda1607740550141e37c0e49d1e5182b34699f15adfd137ee266d346811a";
    /// <summary>下载大小，单位字节；同时限制下载流的最大长度。</summary>
    public const long DownloadBytes = 80733362;
    /// <summary>构建配方和依赖目录的来源。</summary>
    public const string BuildUrl = "https://github.com/BtbN/FFmpeg-Builds/tree/" + Release;
    /// <summary>FFmpeg 对应源码提交。</summary>
    public const string SourceUrl = "https://github.com/FFmpeg/FFmpeg/tree/29e619e767";

    /// <summary>当前用户组件根目录；系统未提供用户数据路径时为空。</summary>
    public static string? InstallDirectory
    {
        get
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrWhiteSpace(local) ? null : Path.Combine(local, "FrameCapture", "Components", "FFmpeg");
        }
    }

    /// <summary>独立组件的 FFmpeg 进程入口。</summary>
    public static string? ExecutablePath => InstallDirectory is { } dir ? Path.Combine(dir, "bin", "ffmpeg.exe") : null;
}
