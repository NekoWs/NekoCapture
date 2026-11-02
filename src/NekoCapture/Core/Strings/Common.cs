namespace NekoCapture.Core;

/// <summary>
/// 文案 · 两个界面以上共用的文案。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>两个界面以上共用的文案的文案。</summary>
    public static partial class Common
    {
        public static readonly Str Copy = new("pin.copy", "复制", "Copy");
        public static readonly Str Save = new("editor.saveOutput", "保存", "Save");
        public static readonly Str Edit = new("commands.groupEditing", "编辑", "Editing");
        public static readonly Str LabelWithContext = new("palette.editingColor", "{0} · {1}", "{0} · {1}");
        public static readonly Str RecordStartLabel = new("actions.recordStartLabel", "开始录制", "Start Recording");
        public static readonly Str ClipboardUnavailable = new("common.clipboardUnavailable", "剪贴板不可用，请重试", "Clipboard unavailable. Try again.");
        public static readonly Str CopyingClipboard = new("common.copyingClipboard", "正在写入剪贴板…", "Writing to clipboard…");
        public static readonly Str Done = new("capture.done", "完成", "Done");
        public static readonly Str Milliseconds = new("common.milliseconds", "毫秒", "ms");
        public static readonly Str RunAsAdministrator = new("tray.runAsAdministrator", "以管理员身份运行", "Run As Administrator");
        public static readonly Str StartupTaskCancelled = new("settings.startupTaskCancelled", "已取消修改", "Change cancelled");
        public static readonly Str StartupTaskFailed = new("settings.startupTaskFailed", "自启设置未完成，请重试", "Could not update startup settings. Try again.");
        public static readonly Str VideoComponent = new("codecsetup.title", "视频组件", "Video component");
        public static readonly Str VideoComponentDescription = new("codecsetup.description", "支持 MP4 与透明 WebM 导出", "Enable MP4 and transparent WebM export");
        public static readonly Str DownloadAndInstall = new("codecsetup.download", "下载并安装", "Download and install");
        public static readonly Str ComponentVerifying = new("codecsetup.verifying", "正在校验与测试编码", "Verifying and testing encoders");
        public static readonly Str ComponentCancelled = new("codecsetup.stopped", "已取消安装", "Installation cancelled");
        public static readonly Str CancelSelection = new("capture.cancelSelection", "取消选区", "Cancel the selection");
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
        public static readonly Str Preview = new("common.preview", "预览", "Preview");
        public static readonly Str SelectAll = new("common.selectAll", "全选", "Select all");
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
