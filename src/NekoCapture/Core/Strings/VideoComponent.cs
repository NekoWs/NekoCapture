namespace NekoCapture.Core;

/// <summary>按需视频组件的格式与安装提示。</summary>
public static partial class K
{
    /// <summary>MP4 与 WebM 精简组件的可见状态。</summary>
    public static class VideoComponent
    {
        public static readonly Str NeedsDownload = new("videocomponent.needsDownload", "{0} 组件需下载 · {1} MiB", "Download the {0} component · {1} MiB");
        public static readonly Str Ready = new("videocomponent.ready", "{0} 组件已就绪", "{0} component ready");
    }
}
