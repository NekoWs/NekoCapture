namespace NekoCapture.Core;

/// <summary>
/// 文案 · 快捷键页与录键框。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>快捷键页与录键框的文案。</summary>
    public static partial class Hotkeys
    {
        public static readonly Str PickerNudge = new("hotkeys.pickerNudge", "选区微调", "Selection fine adjustment");
        public static readonly Str AddBinding = new("hotkeys.addBinding", "添加键位", "Add binding");
        public static readonly Str NudgeLeft = new("hotkeys.nudgeLeft", "向左微调", "Nudge left");
        public static readonly Str NudgeRight = new("hotkeys.nudgeRight", "向右微调", "Nudge right");
        public static readonly Str NudgeUp = new("hotkeys.nudgeUp", "向上微调", "Nudge up");
        public static readonly Str NudgeDown = new("hotkeys.nudgeDown", "向下微调", "Nudge down");
        public static readonly Str PickerNudgeTip = new("hotkeys.pickerNudgeTip", "点按移动鼠标 1 像素，长按连续移动", "Tap to move the pointer by 1 pixel; hold to keep moving.");
        public static readonly Str PresetBehaviors = new("hotkeys.presetBehaviors", "预设行为", "Behavior presets");
        public static readonly Str PresetDual = new("hotkeys.presetDual", "双快捷键", "Two shortcuts");
        public static readonly Str PresetSeparate = new("hotkeys.presetSeparate", "选区与捕获", "Select, then capture");
        public static readonly Str PresetSingle = new("hotkeys.presetSingle", "单快捷键", "One shortcut");
        public static readonly Str PresetCustom = new("hotkeys.presetCustom", "自定义", "Custom");
        public static readonly Str PresetHint = new("hotkeys.presetHint", "应用方案替换快捷键与行为，修改后存入自定义", "Apply to replace shortcuts and behaviors; edits are saved to Custom.");
        public static readonly Str PresetDualDesc = new("hotkeys.presetDualDesc", "两个快捷键分别进入截图与录制", "Two keys open capture or recording.");
        public static readonly Str PresetSeparateDesc = new("hotkeys.presetSeparateDesc", "保存并复用选区，另一键控制捕获", "Reuse a region; tap or hold the other key.");
        public static readonly Str PresetSingleDesc = new("hotkeys.presetSingleDesc", "同一入口，截图与录制由按钮切换", "One key opens selection; buttons choose the mode.");
        public static readonly Str PresetCustomDesc = new("hotkeys.presetCustomDesc", "保存你的快捷键与行为，随时恢复", "Keep your own shortcuts and behaviors.");
        public static readonly Str PresetTapCapture = new("hotkeys.presetTapCapture", "轻点截图", "Tap to capture");
        public static readonly Str PresetHoldRecord = new("hotkeys.presetHoldRecord", "按住录制", "Hold to record");
        public static readonly Str PresetReleaseStop = new("hotkeys.presetReleaseStop", "松开停止", "Release to stop");
        public static readonly Str PresetGestures = new("hotkeys.presetGestures", "轻点截图 · 按住录制 · 松开停止", "Tap to capture · hold to record · release to stop");
        public static readonly Str PresetModeChoice = new("hotkeys.presetModeChoice", "按钮选择截图或录制", "Buttons choose capture or recording");
        public static readonly Str PresetCustomSummary = new("hotkeys.presetCustomSummary", "{0} 条快捷键 · {1} 个行为", "Shortcuts {0} · Behaviors {1}");
        public static readonly Str ChainTitle = new("hotkeys.chainTitle", "动作链", "Action chain");
        public static readonly Str FollowsSwitch = new("hotkeys.followsSwitch", "模式出口", "Mode exit");
        public static readonly Str SwitchStepHint = new("hotkeys.switchStepHint", "模式按钮决定截图或录制分支", "The mode switch chooses the capture or recording branch.");
        public static readonly Str EditResult = new("hotkeys.editResult", "编辑捕获结果", "Edit capture result");
        public static readonly Str EditResultDesc = new("hotkeys.editResultDesc", "编辑本次捕获的最新产物", "Edit the latest result from this run.");
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
        public static readonly Str AddsOnePressAKey = new("hotkeys.addsOnePressAKey", "添加按键并选择行为入口", "Add a key and choose a behavior entry.");
        public static readonly Str AnotherHotkeyAlreadyUsesThis = new("hotkeys.anotherHotkeyAlreadyUsesThis", "按键与触发方式重复，仅首条生效", "Duplicate key and trigger; only the first binding runs.");
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
        public static readonly Str OffStepsTheWholeHotkey = new("hotkeys.offStepsTheWholeHotkey", "关闭后保留配置，按键交给当前应用", "Keep settings when disabled; keys pass to the active app.");
        public static readonly Str OneKeyCombinationCanCarry = new("hotkeys.oneKeyCombinationCanCarry", "同一按键可配置轻点、长按等手势", "Assign tap, hold, and other gestures to the same key.");
        public static readonly Str OnFailure = new("hotkeys.onFailure", "失败后", "After failure");
        public static readonly Str PressANewKeyEsc = new(
            "hotkeys.pressANewKeyEsc",
            "按新键 · Esc 解绑",
            "Press a new key · Esc clears it");
        public static readonly Str RunsInOrderDragToReorder = new("hotkeys.runsInOrderDragToReorder", "拖动排序，点击加号添加；录制等待停止", "Drag to reorder; use plus to add. Recording waits for stopping.");
        public static readonly Str Shortcut = new("hotkeys.shortcut", "组合键", "Shortcut");
        public static readonly Str Trigger = new("hotkeys.trigger", "触发方式", "Trigger");
        public static readonly Str UiKeys = new("hotkeys.uiKeys", "界面键位", "Interface keys");
    }
}
