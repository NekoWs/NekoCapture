namespace NekoCapture.Core;

/// <summary>
/// 文案 · 触发方式的后缀：按下、松手、轻点…。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，目录与取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>触发方式的后缀：按下、松手、轻点…。</summary>
    public static partial class Triggers
    {
        public static readonly Str CaptureMode = new("triggers.captureMode", "默认捕获方式", "Default capture mode");
        public static readonly Str CaptureModeHint = new("triggers.captureModeHint", "选区打开时默认选择截图或录制，仍可使用模式按钮切换", "Choose capture or recording when selection opens; mode buttons remain available.");
        public static readonly Str ModeSwitchHint = new("triggers.modeSwitchHint", "在选区上方显示截图与录制的切换按钮", "Show capture and recording buttons above the selection.");
        public static readonly Str TargetHint = new("triggers.targetHint", "这个动作要执行的共享行为", "The shared behavior this action runs.");
        public static readonly Str FailureHint = new("triggers.failureHint", "这个动作失败后，仍执行后面的动作", "Continue with the next action if this action fails.");
        public static readonly Str CancelHint = new("triggers.cancelHint", "取消这个动作后，仍执行后面的动作", "Continue with the next action after cancelling this action.");
        public static readonly Str AtStart = new("triggers.atStart", "行为开始时", "At the start");
        public static readonly Str AfterStillChoice = new("triggers.afterStillChoice", "确认截图后", "After choosing capture");
        public static readonly Str AfterRecordChoice = new("triggers.afterRecordChoice", "确认录制后", "After choosing recording");
        public static readonly Str AfterRecording = new("triggers.afterRecording", "录制停止后", "After recording stops");
        public static readonly Str AfterAction = new("triggers.afterAction", "{0}完成后", "After {0} completes");
        public static readonly Str InBehavior = new("triggers.inBehavior", "执行“{0}”时", "While running “{0}”");
        public static readonly Str ClipboardImage = new("triggers.clipboardImage", "读取剪贴板图片", "Read clipboard image");
        public static readonly Str SearchTray = new("triggers.searchTray", "托盘 通知区域 单击 双击 右键菜单 菜单项", "tray notification area click double-click context menu");
        public static readonly Str Source = new("triggers.source", "来源", "Source");
        public static readonly Str Keyboard = new("triggers.keyboard", "按键触发", "Key trigger");
        public static readonly Str TrayClick = new("triggers.trayClick", "单击托盘", "Tray click");
        public static readonly Str TrayDoubleClick = new("triggers.trayDoubleClick", "双击托盘", "Tray double-click");
        public static readonly Str TrayMenu = new("triggers.trayMenu", "托盘菜单项", "Tray menu item");
        public static readonly Str Parameters = new("triggers.parameters", "触发可调整项", "Trigger options");
        public static readonly Str ParametersHint = new("triggers.parametersHint", "勾选的设置会显示在触发详情中，其他设置统一使用行为设置", "Selected options appear in trigger details. Other options use the behavior settings.");
        public static readonly Str ContinueFailure = new("triggers.continueFailure", "失败时继续执行", "Continue on failure");
        public static readonly Str ContinueCancel = new("triggers.continueCancel", "取消时继续执行", "Continue on cancellation");
        public static readonly Str PinClipboardHint = new("triggers.pinClipboardHint", "将剪贴板图片作为本次产物", "Use the clipboard image as this run’s result");
        public static readonly Str TriggerClick = new("triggers.triggerClick", "单击", "Click");
        public static readonly Str TriggerDoubleclick = new("triggers.triggerDoubleclick", "双击", "Double-click");
        public static readonly Str TriggerHold = new("triggers.triggerHold", "按住", "Hold");
        public static readonly Str TriggerHoldrelease = new("triggers.triggerHoldrelease", "松手", "Hold release");
        public static readonly Str TriggerKeydown = new("triggers.triggerKeydown", "按下", "Press");
        public static readonly Str TriggerKeyup = new("triggers.triggerKeyup", "松开", "Release");
        public static readonly Str TriggerLongpress = new("triggers.triggerLongpress", "长按", "Long press");
        public static readonly Str TriggerTap = new("triggers.triggerTap", "轻点", "Tap");
        public static readonly Str TriggerTripleclick = new("triggers.triggerTripleclick", "三击", "Triple-click");
    }
}
