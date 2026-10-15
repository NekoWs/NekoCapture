namespace FrameCapture.Core;

/// <summary>
/// 文案 · 快捷键页与录键框。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>快捷键页与录键框的文案。</summary>
    public static partial class Hotkeys
    {
        public static readonly Str AddAHotkey = new("hotkeys.addAHotkey", "添加快捷键", "Add a hotkey");
        public static readonly Str AddsOnePressAKey = new(
            "hotkeys.addsOnePressAKey",
            "新增一条：先给它按一个组合键，再挑要执行的动作",
            "Adds one: press a key combination for it first, then pick what it should do.");
        public static readonly Str AnotherHotkeyAlreadyUsesThis = new(
            "hotkeys.anotherHotkeyAlreadyUsesThis",
            "同一个组合键与触发方式上挂着多条快捷键，只有排在前面那条会触发",
            "Another hotkey already uses this shortcut and trigger — only the first one in the list fires.");
        public static readonly Str ClickThenPressAKey = new(
            "hotkeys.clickThenPressAKey",
            "点击按键",
            "Click, then press a key");
        public static readonly Str ClickToSet = new("hotkeys.clickToSet", "点击设置", "Click to set");
        public static readonly Str Comma = new("hotkeys.comma", "，", ",");
        public static readonly Str DefaultNothing = new("hotkeys.defaultNothing", "默认 · 什么都不做", "Default · nothing");
        public static readonly Str DefaultToast = new("hotkeys.defaultToast", "默认 · 弹提示", "Default · toast");
        public static readonly Str EnableHotkeys = new("hotkeys.enableHotkeys", "启用快捷键", "Enable hotkeys");
        public static readonly Str LeaveEmptyToNameIt = new(
            "hotkeys.leaveEmptyToNameIt",
            "留空则按序列自动取名",
            "Leave empty to name it from the sequence");
        public static readonly Str Name = new("hotkeys.name", "名字", "Name");
        public static readonly Str OffStepsTheWholeHotkey = new(
            "hotkeys.offStepsTheWholeHotkey",
            "关掉后快捷键整体不参与：不吞键、不触发，按键照常交给当前窗口",
            "Off steps the whole hotkey layer back: no swallowed keys, no triggering — every keypress goes to the window in front.");
        public static readonly Str OnCompletion = new("hotkeys.onCompletion", "完成后", "On completion");
        public static readonly Str OneKeyCombinationCanCarry = new(
            "hotkeys.oneKeyCombinationCanCarry",
            "同一个组合键可以配多条不同触发：组合键即按即触发，轻点与按住用来让一个键分出两件事",
            "One key combination can carry several triggers: a shortcut fires the moment it is pressed, while tap and hold split one key into two jobs.");
        public static readonly Str OnFailure = new("hotkeys.onFailure", "失败时", "On failure");
        public static readonly Str OnTrigger = new("hotkeys.onTrigger", "触发时", "On trigger");
        public static readonly Str PressANewKeyEsc = new(
            "hotkeys.pressANewKeyEsc",
            "按新键 · Esc 解绑",
            "Press a new key · Esc clears it");
        public static readonly Str RunsInOrderClick = new(
            "hotkeys.runsInOrderClick",
            "按顺序执行；点芯片换这一步，点加号在后面加一步",
            "Runs in order. Click a chip to change that step, click the plus sign to add one after it.");
        public static readonly Str Shortcut = new("hotkeys.shortcut", "组合键", "Shortcut");
        public static readonly Str TheNameThisHotkeyShows = new(
            "hotkeys.theNameThisHotkeyShows",
            "这条快捷键在列表里的名字；留空时按它执行的动作自动显示",
            "The name this hotkey shows in the list. Left empty, it follows the actions it runs.");
        public static readonly Str Trigger = new("hotkeys.trigger", "触发方式", "Trigger");
    }
}
