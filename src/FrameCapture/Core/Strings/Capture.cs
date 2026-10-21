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
        public static readonly Str Characters = new("capture.characters", "{0} 字", "{0} characters");
        public static readonly Str Copied = new("capture.copied", "已复制 ", "Copied ");

        public static readonly Str CopyAll = new("capture.copyAll", "复制全部", "Copy all");
        public static readonly Str Done = new("capture.done", "完成", "Done");
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
        public static readonly Str NoOcrLanguagePack = new(
            "capture.noOcrLanguagePack",
            "未安装 OCR 语言包，请到 设置 › 时间和语言 › 语音 添加",
            "No OCR language pack is installed — add one in Settings › Time & Language › Speech");
        public static readonly Str NoTextRecognized = new("capture.noTextRecognized", "未识别到文字", "No text recognized");
        public static readonly Str Preview = new("capture.preview", "预览", "Preview");
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
