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
        public static readonly Str ChainTitle = new("hotkeys.chainTitle", "动作链", "Action chain");
        public static readonly Str FollowsSwitch = new("hotkeys.followsSwitch", "跟随开关", "Follows switch");
        public static readonly Str SwitchStepHint = new("hotkeys.switchStepHint", "选区上方开关控制随后第一个捕获步骤，其余步骤按顺序执行", "The selection switch controls the next capture step; all other steps run in order");
        public static readonly Str EditResult = new("hotkeys.editResult", "编辑捕获结果", "Edit capture result");
        public static readonly Str EditResultDesc = new("hotkeys.editResultDesc", "编辑本链最近产出的静图或录制，不使用其他运行的产物", "Edit the latest still or recording from this chain, without using a result from another run");
        public static readonly Str StoredScope = new("hotkeys.storedScope", "使用当前范围", "Use current region");
        public static readonly Str StoredScopeDesc = new("hotkeys.storedScopeDesc", "读取已存范围，没有时使用光标所在屏幕", "Use the stored region, or the screen under the cursor if none is stored");
        public static readonly Str ChainEmptyHint = new("hotkeys.chainEmptyHint", "添加步骤后按顺序执行", "Add steps to run in order");
        public static readonly Str AddStep = new("hotkeys.addStep", "添加步骤", "Add step");
        public static readonly Str MoveLeft = new("hotkeys.moveLeft", "向前移动", "Move earlier");
        public static readonly Str MoveRight = new("hotkeys.moveRight", "向后移动", "Move later");
        public static readonly Str GrabWindow = new("hotkeys.grabWindow", "窗口截图", "Capture window");
        public static readonly Str GrabWindowDesc = new("hotkeys.grabWindowDesc", "截取选区命中的窗口本体", "Capture the window targeted by the selection");
        public static readonly Str SavePng = new("hotkeys.savePng", "保存 PNG", "Save PNG");
        public static readonly Str SavePngDesc = new("hotkeys.savePngDesc", "将本链静图保存到保存目录", "Save this chain's still image to the capture folder");
        public static readonly Str StepValues = new("hotkeys.stepValues", "需要 {0} · 产出 {1}", "Needs {0} · produces {1}");
        public static readonly Str MissingValues = new("hotkeys.missingValues", "缺少 {0}", "Missing {0}");
        public static readonly Str ValueRegion = new("hotkeys.valueRegion", "区域", "Region");
        public static readonly Str ValueStill = new("hotkeys.valueStill", "静图", "Still");
        public static readonly Str ValueVideo = new("hotkeys.valueVideo", "视频", "Video");
        public static readonly Str ValueRecording = new("hotkeys.valueRecording", "录制中", "Recording");
        public static readonly Str ValueNone = new("hotkeys.valueNone", "无", "None");
        public static readonly Str AddAHotkey = new("hotkeys.addAHotkey", "添加快捷键", "Add a hotkey");
        public static readonly Str AddsOnePressAKey = new(
            "hotkeys.addsOnePressAKey",
            "新增一条：先给它按一个组合键，再挑要执行的动作",
            "Add one: press a key combination first, then pick what it should do.");
        public static readonly Str AnotherHotkeyAlreadyUsesThis = new(
            "hotkeys.anotherHotkeyAlreadyUsesThis",
            "同一个组合键与触发方式上挂着多条快捷键，只有排在前面那条会触发",
            "Another hotkey already uses this shortcut and trigger — only the first one in the list fires.");
        public static readonly Str ClickThenPressAKey = new(
            "hotkeys.clickThenPressAKey",
            "点击按键",
            "Click, then press a key");
        public static readonly Str ClickToSet = new("hotkeys.clickToSet", "点击设置", "Click to set");
        public static readonly Str Comma = new("hotkeys.comma", "，", ", ");
        public static readonly Str DefaultNothing = new("hotkeys.defaultNothing", "默认 · 什么都不做", "Default · nothing");
        public static readonly Str EnableHotkeys = new("hotkeys.enableHotkeys", "启用快捷键", "Enable hotkeys");
        public static readonly Str NewPresetName = new("hotkeys.newPresetName", "新配置", "New preset");
        public static readonly Str RenameThisHotkey = new("hotkeys.renameThisHotkey", "改名", "Rename");
        public static readonly Str OffStepsTheWholeHotkey = new(
            "hotkeys.offStepsTheWholeHotkey",
            "关掉后快捷键整体不参与：不吞键、不触发，按键照常交给当前窗口",
            "Off, the whole hotkey layer steps back: no swallowed keys, no triggering — every keypress goes to the window in front.");
        public static readonly Str OneKeyCombinationCanCarry = new(
            "hotkeys.oneKeyCombinationCanCarry",
            "同一个组合键可以配多条不同触发：组合键即按即触发，轻点与按住用来让一个键分出两件事",
            "One key combination can carry several triggers: a shortcut fires the moment it is pressed, while tap and hold split one key into two jobs.");
        public static readonly Str OnFailure = new("hotkeys.onFailure", "失败时", "On failure");
        public static readonly Str PressANewKeyEsc = new(
            "hotkeys.pressANewKeyEsc",
            "按新键 · Esc 解绑",
            "Press a new key · Esc clears it");
        public static readonly Str RunsInOrderDragToReorder = new(
            "hotkeys.runsInOrderDragToReorder",
            "按顺序执行；箭头或拖动调整顺序，加号添加步骤。录制步骤等待停止后继续",
            "Runs in order. Use arrows or drag to reorder; use plus to add a step. Recording waits until stopped.");
        public static readonly Str Shortcut = new("hotkeys.shortcut", "组合键", "Shortcut");
        public static readonly Str Trigger = new("hotkeys.trigger", "触发方式", "Trigger");
        public static readonly Str UiKeys = new("hotkeys.uiKeys", "界面键位", "Interface keys");
    }
}
