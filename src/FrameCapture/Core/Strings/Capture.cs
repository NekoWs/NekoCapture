namespace FrameCapture.Core;

/// <summary>
/// 文案 · 捕获过程与各种浮窗。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>捕获过程与各种浮窗的文案。</summary>
    public static partial class Capture
    {
        public static readonly Str RecordingSession = new("capture.recordingSession", "录制 {0}", "Recording {0}");
        public static readonly Str CopySelection = new("capture.copySelection", "复制所选", "Copy selection");
        public static readonly Str FitImage = new("capture.fitImage", "适合窗口", "Fit to window");
        public static readonly Str SelectTextOnImage = new("capture.selectTextOnImage", "在图片上拖选文字", "Drag over text to select");
        public static readonly Str SelectedCharacters = new("capture.selectedCharacters", "已选 {0} 字", "{0} characters selected");
        public static readonly Str RecordReady = new("capture.recordReady", "准备录制", "Ready to record");
        public static readonly Str RecordActive = new("capture.recordActive", "正在录制", "Recording in progress");
        public static readonly Str RecordStartKey = new("capture.recordStartKey", "开始录制 · {0}", "Start recording · {0}");
        public static readonly Str RecordCancelKey = new("capture.recordCancelKey", "取消录制 · {0}", "Cancel recording · {0}");
        public static readonly Str RecordSelectHint = new("capture.recordSelectHint", "拖框选择录制范围 · {0} 取消", "Drag to select the recording region · {0} to cancel");
        public static readonly Str RecordAdjustHint = new("capture.recordAdjustHint", "拖边角调整录制范围 · {0} 确认 · {1} 取消", "Adjust the recording region · {0} to confirm · {1} to cancel");
        /// <summary>调整选区期间的提示条：怎么改、怎么确认。</summary>
    public static readonly Str AdjustHint = new(
        "capture.adjustHint",
        "拖边角改大小 · 拖动中间移动 · Enter 确认 · Esc 取消",
        "Drag an edge or corner to resize, drag inside to move, Enter to confirm, Esc to cancel");

    /// <summary>勾按钮的悬停提示。</summary>
    public static readonly Str ConfirmSelection = new(
        "capture.confirmSelection",
        "确认选区",
        "Confirm the selection");


    public static readonly Str AutoScroll = new("capture.autoScroll", "自动滚动", "Auto-Scroll");
        public static readonly Str AutoScrollingStops = new(
            "capture.autoScrollingStops",
            "自动滚动中 · 到底自动停止",
            "Auto-scrolling · stops at the bottom");
        public static readonly Str AutoScrollingTheCursorMoved = new(
            "capture.autoScrollingTheCursorMoved",
            "自动滚动中：光标已移入选区，停止或结束后移回原位",
            "Auto-scrolling: the cursor moved into the region and returns when you stop or finish");
        public static readonly Str BottomOfTheContentReached = new(
            "capture.bottomOfTheContentReached",
            "已到内容底部，点「完成」出图",
            "Bottom of the content reached — choose Done to stitch");
        public static readonly Str Copied = new("capture.copied", "已复制 ", "Copied ");

        public static readonly Str CopyAll = new("capture.copyAll", "复制全部", "Copy all");
        public static readonly Str DragToSelectHover = new(
            "capture.dragToSelectHover",
            "拖框选择 · 悬停点击吸附窗口 · Enter 确认上次选区 · Esc 取消",
            "Drag to select · hover and click to snap to a window · Enter reuses the last region · Esc cancels");
        public static readonly Str DragToSelectHover2 = new(
            "capture.dragToSelectHover2",
            "拖框选择 · 悬停点击吸附窗口 · Esc 取消",
            "Drag to select · hover and click to snap to a window · Esc cancels");
        public static readonly Str FramecaptureIsReady = new(
            "capture.framecaptureIsReady",
            "FrameCapture 已就绪",
            "FrameCapture is ready");
        public static readonly Str FullScreen = new("capture.fullScreen", "全屏", "Full screen");
        public static readonly Str NoOcrLanguagePack = new("capture.noOcrLanguagePack", "未安装 OCR 语言包，请在 Windows 语言选项中添加文字识别", "No OCR language pack. Add optical character recognition in Windows language options.");
        public static readonly Str NoTextRecognized = new("capture.noTextRecognized", "未识别到文字", "No text recognized");
        public static readonly Str RecognizedCharactersCopied = new(
            "capture.recognizedCharactersCopied",
            "已识别 {0} 字 · 已复制",
            "Recognized {0} characters · copied");
        public static readonly Str Region = new("capture.region", "范围 {0}×{1}", "Region {0}×{1}");
        public static readonly Str ScreenshotTaken = new("capture.screenshotTaken", "已截图", "Screenshot taken");
        public static readonly Str ScrolledTooFastToFind = new(
            "capture.scrolledTooFastToFind",
            "滚动太快、找不到重叠——请少滚一点",
            "Scrolled too fast to find an overlap — scroll a little at a time");
        public static readonly Str ScrollTheTargetWindow = new(
            "capture.scrollTheTargetWindow",
            "滚动目标窗口，自动拼接 · 点「完成」结束",
            "Scroll the target window to stitch automatically · press Done to finish");
        public static readonly Str SegmentsStitchedPxTall = new(
            "capture.segmentsStitchedPxTall",
            "已拼接 {0} 段 · 高 {1} px",
            "{0} segments stitched · {1} px tall");
        public static readonly Str SegmentStitched = new("capture.segmentStitched", "已拼接 1 段", "1 segment stitched");
        public static readonly Str SavedFileName = new("capture.savedFileName", "已保存 {0}", "Saved {0}");
        public static readonly Str SavingVideo = new("capture.savingVideo", "正在保存视频…", "Saving video…");
        public static readonly Str StopAutoScroll = new("capture.stopAutoScroll", "停止自动滚动", "Stop Auto-Scroll");
        public static readonly Str TheViewKeepsChangingSo = new(
            "capture.theViewKeepsChangingSo",
            "画面一直在变化、判定不了停稳，自动滚动已停止",
            "The view keeps changing, so settling can't be detected; auto-scroll stopped");
        public static readonly Str ThisEnvironmentCanTInject = new(
            "capture.thisEnvironmentCanTInject",
            "此环境无法注入滚轮，请手动滚动",
            "This environment can't inject the wheel — scroll by hand");
        public static readonly Str ThisWindowDoesnTAccept = new(
            "capture.thisWindowDoesnTAccept",
            "这个窗口不接受滚轮注入，请手动滚动",
            "This window doesn't accept wheel injection — scroll by hand");
        public static readonly Str UseTheseSecondsToOpen = new(
            "capture.useTheseSecondsToOpen",
            "趁这几秒打开要截的菜单 / 悬停提示…",
            "Use these seconds to open the menu or tooltip you want to capture…");
        public static readonly Str Working = new("capture.working", "处理中…", "Working…");
    }
}
