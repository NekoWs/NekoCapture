namespace FrameCapture.Core;

/// <summary>OCR 引擎选择与可选模型组件的文案。</summary>
public static partial class K
{
    /// <summary>OCR 设置和组件状态。</summary>
    public static class Ocr
    {
        public static readonly Str Engine = new("ocr.engine", "OCR 引擎", "OCR engine");
        public static readonly Str EngineDescription = new("ocr.engineDescription", "选择文字识别引擎", "Choose a text recognition engine");
        public static readonly Str SystemEngine = new("ocr.systemEngine", "系统内置", "Windows OCR");
        public static readonly Str SmallEngine = new("ocr.smallEngine", "PaddleOCR small", "PaddleOCR small");
        public static readonly Str Component = new("ocr.component", "Small 模型", "Small model");
        public static readonly Str DownloadSize = new("ocr.downloadSize", "可选下载 · 约 26 MB", "Optional download · about 26 MB");
        public static readonly Str Installed = new("ocr.installed", "Small 模型已安装", "Small model installed");
        public static readonly Str Downloading = new("ocr.downloading", "下载模型 {0}%", "Downloading models {0}%");
        public static readonly Str InstallFailed = new("ocr.installFailed", "模型安装失败，请重试", "Model installation failed. Try again.");
        public static readonly Str Missing = new("ocr.missing", "请先在编辑器设置中下载 Small 模型", "Download the Small model in Editor settings first.");
    }
}
