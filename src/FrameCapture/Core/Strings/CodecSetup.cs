namespace FrameCapture.Core;

/// <summary>独立视频组件安装器文案。</summary>
public static partial class K
{
    /// <summary>视频组件的下载、安装与来源说明。</summary>
    public static class CodecSetup
    {
        public static readonly Str Title = new("codecsetup.title", "视频组件", "Video component");
        public static readonly Str Description = new("codecsetup.description", "支持 MP4 与透明 WebM 导出", "Enable MP4 and transparent WebM export");
        public static readonly Str Download = new("codecsetup.download", "下载并安装", "Download and install");
        public static readonly Str Notice = new("codecsetup.notice", "从 BtbN 下载 FFmpeg LGPL 组件，约 {0} MiB。仅安装到当前用户，不修改系统 PATH。", "Download FFmpeg LGPL from BtbN, about {0} MiB. Installs for this user without changing PATH.");
        public static readonly Str Downloading = new("codecsetup.downloading", "正在下载 {0}%", "Downloading {0}%");
        public static readonly Str Verifying = new("codecsetup.verifying", "正在校验与测试编码", "Verifying and testing encoders");
        public static readonly Str Ready = new("codecsetup.ready", "组件已安装，可导出 MP4 与 WebM", "Component installed. MP4 and WebM export are available.");
        public static readonly Str Failed = new("codecsetup.failed", "安装未完成，请检查网络或关闭正在导出的视频后重试。", "Installation failed. Check your connection or finish video exports, then retry.");
        public static readonly Str Stopped = new("codecsetup.stopped", "已取消安装", "Installation cancelled");
        public static readonly Str Source = new("codecsetup.source", "FFmpeg 源码", "FFmpeg source");
        public static readonly Str Build = new("codecsetup.build", "构建与依赖", "Build and dependencies");
        public static readonly Str License = new("codecsetup.license", "LGPL 许可证", "LGPL license");
    }
}
