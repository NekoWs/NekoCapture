namespace FrameCapture.Core;

/// <summary>独立视频组件安装器文案。</summary>
public static partial class K
{
    /// <summary>视频组件的下载、安装与来源说明。</summary>
    public static class CodecSetup
    {
        public static readonly Str Notice = new("codecsetup.notice", "下载 MP4 与 WebM 精简组件，共 {0} MiB。仅安装到当前用户，不修改系统 PATH。", "Download MP4 and WebM components, {0} MiB total. Installs for this user without changing PATH.");
        public static readonly Str Downloading = new("codecsetup.downloading", "正在下载 {0}%", "Downloading {0}%");
        public static readonly Str Ready = new("codecsetup.ready", "组件已安装，可导出 MP4 与 WebM", "Component installed. MP4 and WebM export are available.");
        public static readonly Str Failed = new("codecsetup.failed", "安装未完成，请检查网络或关闭正在导出的视频后重试。", "Installation failed. Check your connection or finish video exports, then retry.");
        public static readonly Str Source = new("codecsetup.source", "FFmpeg 源码", "FFmpeg source");
        public static readonly Str Build = new("codecsetup.build", "构建与依赖", "Build and dependencies");
        public static readonly Str License = new("codecsetup.license", "GPLv3 许可证", "GPLv3 license");
    }
}
