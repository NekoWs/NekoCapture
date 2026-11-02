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
        public static readonly Str ClipboardImage = new("triggers.clipboardImage", "读取剪贴板图片", "Read clipboard image");
        public static readonly Str SearchTray = new("triggers.searchTray", "托盘 通知区域 单击 双击 右键菜单 菜单项", "tray notification area click double-click context menu");
        public static readonly Str Source = new("triggers.source", "来源", "Source");
        public static readonly Str Keyboard = new("triggers.keyboard", "按键触发", "Key trigger");
        public static readonly Str TrayClick = new("triggers.trayClick", "单击托盘", "Tray click");
        public static readonly Str TrayDoubleClick = new("triggers.trayDoubleClick", "双击托盘", "Tray double-click");
        public static readonly Str TrayMenu = new("triggers.trayMenu", "托盘菜单项", "Tray menu item");
        public static readonly Str Parameters = new("triggers.parameters", "动作参数", "Action parameters");
        public static readonly Str ParametersHint = new("triggers.parametersHint", "只覆盖本次调用，不改变共享行为", "Override this invocation without changing the shared behavior");
        public static readonly Str AddOverride = new("triggers.addOverride", "自定义动作参数", "Customize action parameters");
        public static readonly Str ResetOverride = new("triggers.resetOverride", "恢复行为默认值", "Use behavior defaults");
        public static readonly Str ContinueFailure = new("triggers.continueFailure", "失败后继续", "Continue after failure");
        public static readonly Str ContinueCancel = new("triggers.continueCancel", "取消后继续", "Continue after cancellation");
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
