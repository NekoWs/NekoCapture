namespace FrameCapture.Core;

/// <summary>
/// 文案 · 动作词汇表的短名与它做什么。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，目录与取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>动作词汇表的短名与它做什么。</summary>
    public static partial class Actions
    {
        public static readonly Str CaptureFrameDesc = new("actions.captureFrameDesc", "按下立即抓光标所在屏幕的当下这一帧，不走选区、不带延时、不抢焦点，适合游戏", "Grabs the exact frame on the screen under the cursor the moment you press: no selection, no delay, no focus steal — usable in games");
        public static readonly Str CaptureFrameLabel = new("actions.captureFrameLabel", "全屏即时截图", "Instant Full-screen Capture");
        public static readonly Str CaptureStoredDesc = new("actions.captureStoredDesc", "对当前范围截一张静图；跟在选区后面就是刚圈好的那块", "Takes a still of the current region; after a Selection step that means the region you just drew");
        public static readonly Str CopyLastDesc = new("actions.copyLastDesc", "把上一次截图写进剪贴板", "Writes the last still image to the clipboard");
        public static readonly Str CopyLastLabel = new("actions.copyLastLabel", "复制到剪贴板", "Copy to Clipboard");
        public static readonly Str DelayedCaptureDesc = new("actions.delayedCaptureDesc", "倒数数秒后截当前范围，用来抓一点就消失的菜单和悬停", "Counts down a few seconds, then captures the current region — for menus and tooltips that vanish the moment you touch them");
        public static readonly Str EditLastStillDesc = new("actions.editLastStillDesc", "打开标注编辑器编辑上一次静图", "Opens the annotation editor on the last still image");
        public static readonly Str EditLastVideoDesc = new("actions.editLastVideoDesc", "打开视频编辑器编辑上一次录制，常配在完成后，让停止录制后再进编辑器", "Opens the video editor on the last recording; usually set under On completion so you land in the editor once recording stops");
        public static readonly Str EditLastVideoLabel = new("actions.editLastVideoLabel", "打开编辑器", "Open Editor");
        public static readonly Str HelpOpenDesc = new("actions.helpOpenDesc", "打开使用说明窗口", "Opens the usage guide window");
        public static readonly Str HelpOpenLabel = new("actions.helpOpenLabel", "打开帮助", "Open Help");
        public static readonly Str NotifyToastDesc = new("actions.notifyToastDesc", "把上一次截图以浮窗弹出来，点它可以进编辑器", "Shows the last still again as a toast; clicking it opens the editor");
        public static readonly Str NotifyToastLabel = new("actions.notifyToastLabel", "提示", "Notify");
        public static readonly Str OcrSelectionDesc = new("actions.ocrSelectionDesc", "对当前范围做 OCR：开「识别后显示结果窗」则弹可编辑结果窗，否则直接复制文本", "Runs OCR on the current region: with Show the result window after recognizing text on, an editable result window appears, otherwise the text is copied directly");
        public static readonly Str PinSelectionDesc = new("actions.pinSelectionDesc", "把当前范围钉在屏幕上", "Pins the current region on screen");
        public static readonly Str RecordStartDesc = new("actions.recordStartDesc", "对当前范围开始录制，直到收到停止", "Records the current region until a stop arrives");
        public static readonly Str RecordStartLabel = new("actions.recordStartLabel", "开始录制", "Start Recording");
        public static readonly Str RecordStopDesc = new("actions.recordStopDesc", "停止录制，产物留在内存，不自动进编辑器", "Stops recording and keeps the result in memory; it does not open the editor on its own");
        public static readonly Str RecordStopLabel = new("actions.recordStopLabel", "停止录制", "Stop Recording");
        public static readonly Str RecordToggleDesc = new("actions.recordToggleDesc", "空闲→选区起录；录制中→停止，不自动进编辑器", "Idle → pick a region and start recording; recording → stop. It does not open the editor on its own");
        public static readonly Str RecordToggleLabel = new("actions.recordToggleLabel", "开始/停止录制", "Start/Stop Recording");
        public static readonly Str ScopeSelectDesc = new("actions.scopeSelectDesc", "拖框选一块区域，作为这条序列后面动作的范围", "Drag a box to pick the region the rest of this sequence works on");
        public static readonly Str ScopeSelectLabel = new("actions.scopeSelectLabel", "选区", "Selection");
        public static readonly Str ScrollCaptureDesc = new("actions.scrollCaptureDesc", "对当前范围做滚动长截图", "Takes a scrolling long capture of the current region");
        public static readonly Str SettingsOpenDesc = new("actions.settingsOpenDesc", "打开设置窗口", "Opens the settings window");
        public static readonly Str SettingsOpenLabel = new("actions.settingsOpenLabel", "打开设置", "Open Settings");
    }
}
