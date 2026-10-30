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
        public static readonly Str MediumEngine = new("ocr.mediumEngine", "PaddleOCR medium", "PaddleOCR medium");
        public static readonly Str NeedsDownload = new("ocr.needsDownload", "{0} 需要下载 · {1} MiB", "Download required for {0} · {1} MiB");
        public static readonly Str Verifying = new("ocr.verifying", "正在校验模型…", "Verifying models…");
        public static readonly Str Installing = new("ocr.installing", "正在安装模型…", "Installing models…");
        public static readonly Str InstallFailed = new("ocr.installFailed", "模型安装失败，请重试", "Model installation failed. Try again.");
        public static readonly Str Missing = new("ocr.missing", "请先在编辑器设置中下载所选 OCR 模型", "Download the selected OCR model in Editor settings first.");
    }
}
