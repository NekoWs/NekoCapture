namespace FrameCapture.Core;

/// <summary>界面文案目录。</summary>
public static partial class K
{
    /// <summary>关于页面的产品信息与链接。</summary>
    public static class About
    {
        public static readonly Str Title = new("about.title", "关于", "About");
        public static readonly Str Product = new("about.product", "FrameCapture", "FrameCapture");
        public static readonly Str Version = new("about.version", "版本 {0}", "Version {0}");
        public static readonly Str Description = new("about.description", "截图、录制、OCR 与标注", "Capture, record, recognize text, and annotate");
        public static readonly Str Source = new("about.source", "源代码", "Source code");
        public static readonly Str Project = new("about.project", "项目", "Project");
        public static readonly Str OpenSourceLicense = new("about.openSourceLicense", "开源许可", "Open-source license");
        public static readonly Str License = new("about.license", "GPL-3.0", "GPL-3.0");
        public static readonly Str LinkFailed = new("about.linkFailed", "无法打开链接，请重试", "Unable to open the link. Try again.");
    }
}
