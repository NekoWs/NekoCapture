namespace FrameCapture.Core;

/// <summary>
/// 文案 · 两个界面以上共用的文案。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>两个界面以上共用的文案的文案。</summary>
    public static partial class Common
    {
        public static readonly Str Appearance = new("common.appearance", "外观", "Appearance");
        public static readonly Str Confirm = new("common.confirm", "确认", "Confirm");
    public static readonly Str Cancel = new("common.cancel", "取消", "Cancel");
        public static readonly Str Capture = new("common.capture", "截图", "Capture");
        public static readonly Str Close = new("common.close", "关闭", "Close");
        public static readonly Str Copied = new("common.copied", "已复制", "Copied");
        public static readonly Str DelayedCapture = new("common.delayedCapture", "延时截图", "Delayed Capture");
        public static readonly Str Editor = new("common.editor", "编辑器", "Editor");
        public static readonly Str NoKey = new("common.noKey", "未设键", "No key");
        public static readonly Str Palette = new("common.palette", "配色", "Palette");
        public static readonly Str Pin = new("common.pin", "贴图", "Pin");
        public static readonly Str PngImagePng = new("common.pngImagePng", "PNG 图片|*.png", "PNG image|*.png");
        public static readonly Str RecognitionFailed = new("common.recognitionFailed", "识别失败", "Recognition failed");
        public static readonly Str RecognizeText = new("common.recognizeText", "识别文字", "Recognize Text");
        public static readonly Str RecognizeText2 = new("common.recognizeText2", "识别文字…", "Recognize Text…");
        public static readonly Str Recording = new("common.recording", "录制", "Recording");
        public static readonly Str SaveFailed = new("common.saveFailed", "保存失败", "Save failed");
        public static readonly Str ScrollingCapture = new("common.scrollingCapture", "滚动截图", "Scrolling Capture");
        public static readonly Str Settings = new("common.settings", "设置", "Settings");
        public static readonly Str StartWithWindows = new("common.startWithWindows", "开机自启", "Start With Windows");
    }
}
