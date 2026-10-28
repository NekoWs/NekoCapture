namespace FrameCapture.Core;

/// <summary>行为入口与事件分支文案。</summary>
public static partial class K
{
    /// <summary>行为页与图校验文案。</summary>
    public static partial class Behaviors
    {
        public static readonly Str Title = new("behaviors.title", "行为", "Behaviors");
        public static readonly Str CaptureFlow = new("behaviors.captureFlow", "捕获", "Capture");
        public static readonly Str EditStill = new("behaviors.editStill", "编辑截图", "Edit screenshot");
        public static readonly Str Add = new("behaviors.add", "新建行为", "New behavior");
        public static readonly Str Delete = new("behaviors.delete", "删除行为", "Delete behavior");
        public static readonly Str Entry = new("behaviors.entry", "进入行为", "Enter behavior");
        public static readonly Str InitialMode = new("behaviors.initialMode", "初始模式", "Initial mode");
        public static readonly Str NodeDefault = new("behaviors.nodeDefault", "使用行为设置", "Use behavior setting");
        public static readonly Str SelectOnly = new("behaviors.selectOnly", "仅选区", "Select region only");
        public static readonly Str SelectOnlyDesc = new("behaviors.selectOnlyDesc", "隐藏模式按钮，仅保存选区；确认后执行所配动作", "Hide the mode switch and store the region; run configured actions after confirmation");
        public static readonly Str ClearRegion = new("behaviors.clearRegion", "清除选区", "Clear stored region");
        public static readonly Str ClearRegionDesc = new("behaviors.clearRegionDesc", "清除持续保存的区域与范围轮廓", "Clear the stored region and its outline");
        public static readonly Str StoredRequired = new("behaviors.storedRequired", "使用已选区域", "Use selected region");
        public static readonly Str StoredRequiredDesc = new("behaviors.storedRequiredDesc", "读取持续保存的选区；没有可用选区时进入失败出口", "Read the stored region; use the failure exit if no region is available");
        public static readonly Str SaveResult = new("behaviors.saveResult", "保存捕获结果", "Save capture result");
        public static readonly Str SaveResultDesc = new("behaviors.saveResultDesc", "保存本次静图或录制，文件写完后继续后续动作", "Save this run's still or recording and continue after the file is complete");
        public static readonly Str Call = new("behaviors.call", "调用行为", "Call behavior");
        public static readonly Str CallDesc = new("behaviors.callDesc", "执行所选共享行为，完成后继续当前动作链", "Run a shared behavior and continue this chain after it completes");
        public static readonly Str Done = new("behaviors.done", "完成后", "After completion");
        public static readonly Str Cancel = new("behaviors.cancel", "取消后", "After cancellation");
        public static readonly Str Saved = new("behaviors.saved", "保存后", "After saving");
        public static readonly Str Closed = new("behaviors.closed", "关闭后", "After closing");
        public static readonly Str Stopped = new("behaviors.stopped", "停止后", "After stopping");
        public static readonly Str ShowSwitch = new("behaviors.showSwitch", "显示模式按钮", "Show mode switch");
        public static readonly Str Continue = new("behaviors.continue", "继续同级动作", "Continue sibling actions");
        public static readonly Str Description = new("behaviors.description", "拖动卡片排序，点击卡片编辑事件出口；路径可返回上一级", "Drag cards to reorder; click a card to edit its event exits and use the path to go back");
        public static readonly Str Invalid = new("behaviors.invalid", "行为图含有无效引用、事件出口或执行回路，请检查动作连接", "The behavior graph contains an invalid reference, event exit, or execution cycle");
        public static readonly Str Empty = new("behaviors.empty", "未连接行为", "No behavior connected");
        public static readonly Str HoldMs = new("behaviors.holdMs", "长按阈值", "Hold threshold");
        public static readonly Str ClickMs = new("behaviors.clickMs", "连击间隔", "Multi-click interval");
        public static readonly Str EditCurrent = new("behaviors.editCurrent", "编辑此行为", "Edit this behavior");
        public static readonly Str TimingHint = new("behaviors.timingHint", "以毫秒为单位；同一按键的轻点、长按与松开共用长按阈值", "Times are in milliseconds; tap, hold, and release on the same key share a hold threshold");
        public static readonly Str RecordingReady = new("behaviors.recordingReady", "录制完成", "Recording complete");
        public static readonly Str ActionDone = new("behaviors.actionDone", "行为已完成", "Behavior completed");
        public static readonly Str ActionFailed = new("behaviors.actionFailed", "行为执行失败", "Behavior failed");
        public static readonly Str ActionCancelled = new("behaviors.actionCancelled", "行为已取消", "Behavior cancelled");
    }
}
