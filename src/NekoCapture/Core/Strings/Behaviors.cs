namespace NekoCapture.Core;

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
        public static readonly Str SelectOnlyDesc = new("behaviors.selectOnlyDesc", "保存并复用选区，隐藏模式按钮", "Store and reuse the region; hide the mode switch.");
        public static readonly Str ClearRegion = new("behaviors.clearRegion", "清除选区", "Clear stored region");
        public static readonly Str ClearRegionDesc = new("behaviors.clearRegionDesc", "清除持续保存的区域与范围轮廓", "Clear the stored region and its outline");
        public static readonly Str StoredRequired = new("behaviors.storedRequired", "使用已选区域", "Use selected region");
        public static readonly Str StoredRequiredDesc = new("behaviors.storedRequiredDesc", "复用已选区域；无选区时执行失败分支", "Reuse the selected region; run the failure branch if none exists.");
        public static readonly Str SaveResult = new("behaviors.saveResult", "保存捕获结果", "Save capture result");
        public static readonly Str SaveResultDesc = new("behaviors.saveResultDesc", "保存本次产物，完成后继续", "Save the current result, then continue.");
        public static readonly Str Call = new("behaviors.call", "调用行为", "Call behavior");
        public static readonly Str CallDesc = new("behaviors.callDesc", "执行所选行为，完成后继续", "Run the selected behavior, then continue.");
        public static readonly Str Done = new("behaviors.done", "完成后", "After completion");
        public static readonly Str Cancel = new("behaviors.cancel", "取消后", "After cancellation");
        public static readonly Str Saved = new("behaviors.saved", "保存后", "After saving");
        public static readonly Str Closed = new("behaviors.closed", "关闭后", "After closing");
        public static readonly Str Stopped = new("behaviors.stopped", "停止后", "After stopping");
        public static readonly Str Started = new("behaviors.started", "开始后", "After starting");
        public static readonly Str ShowSwitch = new("behaviors.showSwitch", "显示模式按钮", "Show mode switch");
        public static readonly Str Continue = new("behaviors.continue", "继续同级动作", "Continue sibling actions");
        public static readonly Str Description = new("behaviors.description", "拖动排序，点击配置分支", "Drag to reorder; click to configure branches");
        public static readonly Str Invalid = new("behaviors.invalid", "行为连接无效或存在循环", "Invalid behavior connection or cycle");
        public static readonly Str Empty = new("behaviors.empty", "未连接行为", "No behavior connected");
        public static readonly Str HoldMs = new("behaviors.holdMs", "长按阈值", "Hold threshold");
        public static readonly Str ClickMs = new("behaviors.clickMs", "连击间隔", "Multi-click interval");
        public static readonly Str TimingHint = new("behaviors.timingHint", "单位为毫秒；同键手势共用长按阈值", "Milliseconds; gestures on the same key share a hold threshold.");
        public static readonly Str RecordingReady = new("behaviors.recordingReady", "录制完成", "Recording complete");
        public static readonly Str ActionDone = new("behaviors.actionDone", "行为已完成", "Behavior completed");
        public static readonly Str ActionFailed = new("behaviors.actionFailed", "行为执行失败", "Behavior failed");
        public static readonly Str ActionCancelled = new("behaviors.actionCancelled", "行为已取消", "Behavior cancelled");
    }
}
