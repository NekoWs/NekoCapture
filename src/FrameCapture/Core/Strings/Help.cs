namespace FrameCapture.Core;

/// <summary>
/// 文案 · 使用指南窗。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>使用指南窗的文案。</summary>
    public static partial class Help
    {
        public static readonly Str AfterCapture = new("help.afterCapture", "截图后", "After capture");
        public static readonly Str AfterCaptureIsSetPer = new(
            "help.afterCaptureIsSetPer",
            "「截图后」在「设置 · 快捷键」每条快捷键的完成后一栏设置；托盘还能「编辑上次截图」。",
            "After capture is set per hotkey under After on Settings · Shortcuts; the tray also offers Edit last capture.");
        public static readonly Str AfterRecording = new("help.afterRecording", "录完", "After recording");
        public static readonly Str AnyRectangleTheWheelResizes = new(
            "help.anyRectangleTheWheelResizes",
            "选任意矩形；滚轮微调、方向键移动",
            "Any rectangle; the wheel resizes and the arrows move it");
        public static readonly Str ArrowKeys = new("help.arrowKeys", "方向键", "Arrow keys");
        public static readonly Str CaptureExactlyWhatYouPoint = new(
            "help.captureExactlyWhatYouPoint",
            "指哪儿，截哪儿。",
            "Capture exactly what you point at.");
        public static readonly Str CaptureTheShotLands = new(
            "help.captureTheShotLands",
            "截图：截完自动进剪贴板",
            "Capture: the shot lands in the clipboard");
        public static readonly Str ChooseTheRegion = new("help.chooseTheRegion", "选捕获范围", "Choose the region");
        public static readonly Str ClearTheRegion = new("help.clearTheRegion", "清除范围", "Clear the region");
        public static readonly Str ClickItToOpen = new(
            "help.clickItToOpen",
            "点它进编辑器；常驻/1/2/5秒/自定义可调",
            "Click it to open the editor; stay, or 1/2/5 seconds, or custom");
        public static readonly Str CloseTheMenuSavesReads = new(
            "help.closeTheMenuSavesReads",
            "关闭 / 右键菜单存盘、识字、复制",
            "Close / the menu saves, reads text, copies");
        public static readonly Str ConfirmRegionCancel = new(
            "help.confirmRegionCancel",
            "确认范围 / 取消",
            "Confirm region / cancel");
        public static readonly Str CountsDownBeforeShootingSo = new(
            "help.countsDownBeforeShootingSo",
            "托盘触发后倒数几秒再截——摆好菜单/悬停再入镜，支持自定义秒数",
            "Counts down before shooting so menus and tooltips stay on screen; the seconds are adjustable");
        public static readonly Str CtrlHBlendsTheFrames = new(
            "help.ctrlHBlendsTheFrames",
            "Ctrl+H 视觉暂留合成图；「生成胶片条」抽帧拼成一张逐帧图直接复制，喂 AI 最好用。",
            "Ctrl+H blends the frames into one image; Film strip samples them into a single sheet you can copy straight into an AI prompt.");
        public static readonly Str CtrlWheel = new("help.ctrlWheel", "Ctrl+滚轮", "Ctrl+wheel");
        public static readonly Str DoneCopiesTheClipCancel = new(
            "help.doneCopiesTheClipCancel",
            "确定复制成片 / 取消",
            "Done copies the clip, cancel closes");
        public static readonly Str DoneCheck = new("help.doneCheck", "完成 ✓", "Done ✓");
        public static readonly Str Drag = new("help.drag", "拖动", "Drag");
        public static readonly Str DragABox = new("help.dragABox", "拖框", "Drag a box");
        public static readonly Str EditorDrawingClickTheCurrent = new(
            "help.editorDrawingClickTheCurrent",
            "编辑器 · 绘图 — 再点当前工具或 {0} 退出绘制",
            "Editor · Drawing — click the current tool again or press {0} to leave drawing");
        public static readonly Str EscRightClick = new("help.escRightClick", "Esc / 右键", "Esc / right-click");
        public static readonly Str EveryKeyIsADefault = new(
            "help.everyKeyIsADefault",
            "键位都是默认值，可在「设置 · 快捷键」里改成自己的习惯。",
            "Every key is a default — change them under Settings · Shortcuts to fit your habits.");
        public static readonly Str FloatingCard = new("help.floatingCard", "悬浮窗", "Floating card");
        public static readonly Str Format = new("help.format", "格式", "Format");
        public static readonly Str FrameRate = new("help.frameRate", "帧率", "Frame rate");
        public static readonly Str GifPlaysEverywhereMp4Webm = new(
            "help.gifPlaysEverywhereMp4Webm",
            "GIF 通用；MP4 / WebM 需 ffmpeg",
            "GIF plays everywhere; MP4 / WebM need ffmpeg");
        public static readonly Str GlobalHotkeysAndTheEditor = new(
            "help.globalHotkeysAndTheEditor",
            "全局快捷键与编辑器里的键都在「设置 · 快捷键」这一页改",
            "Global hotkeys and the editor's keys are all rebound on the Settings · Shortcuts page");
        public static readonly Str HiddenInSettings = new(
            "help.hiddenInSettings",
            "藏在设置和托盘里。",
            "Hidden in Settings and the tray.");
        public static readonly Str HoldRelease = new("help.holdRelease", "按住 / 松开", "Hold / release");
        public static readonly Str HoldToRecordRelease = new(
            "help.holdToRecordRelease",
            "按住录，松手成片。",
            "Hold to record, release for the clip.");
        public static readonly Str Hover = new("help.hover", "悬停", "Hover");
        public static readonly Str InTheEditorDoneCopies = new(
            "help.inTheEditorDoneCopies",
            "编辑器里：确定复制 / 取消",
            "In the editor: Done copies, cancel closes");
        public static readonly Str InTheTrayMenuGoes = new(
            "help.inTheTrayMenuGoes",
            "托盘菜单里，恢复「截光标所在屏」",
            "In the tray menu; goes back to capturing the screen under the cursor");
        public static readonly Str KeySettings = new("help.keySettings", "键位设置", "Key settings");
        public static readonly Str ManualFollowMonitorRefreshRecords = new(
            "help.manualFollowMonitorRefreshRecords",
            "手动 10–240；开「跟随显示器刷新率」自动录满高刷",
            "Manual 10–240; Follow monitor refresh records the display's full rate");
        public static readonly Str MoreToTry = new("help.moreToTry", "更多玩法", "More to try");
        public static readonly Str NextArrow = new("help.nextArrow", "下一页 →", "Next →");
        public static readonly Str NudgeThePinOnePixel = new(
            "help.nudgeThePinOnePixel",
            "把贴图挪 1 像素，按住 Shift 一次 10 像素",
            "Nudge the pin by 1 pixel, or 10 with Shift held");
        public static readonly Str OffFlickYourOwnOgg = new(
            "help.offFlickYourOwnOgg",
            "关闭 / 轻弹 / 自定义 ogg·wav，可试听",
            "Off / flick / your own ogg·wav, with a preview");
        public static readonly Str OneKeyStraight = new(
            "help.oneKeyStraight",
            "一次触发，进剪贴板。",
            "One key, straight to the clipboard.");
        public static readonly Str OpensInTheEditor = new(
            "help.opensInTheEditor",
            "自动进编辑器预览，Enter 复制成片",
            "Opens in the editor to preview; Enter copies the clip");
        public static readonly Str PauseGesturesClearRegionSettings = new(
            "help.pauseGesturesClearRegionSettings",
            "暂停手势 / 清除范围 / 设置…",
            "Pause gestures / clear region / Settings…");
        public static readonly Str PinAnImageOnto = new(
            "help.pinAnImageOnto",
            "把图钉在屏幕上。",
            "Pin an image onto the screen.");
        public static readonly Str PinsCanBeRead = new(
            "help.pinsCanBeRead",
            "也能对贴图识字",
            "Pins can be read the same way");
        public static readonly Str PinSnappingLinesThePin = new(
            "help.pinSnappingLinesThePin",
            "「贴图窗口吸附」：贴齐屏幕边、其他贴图、任意窗口边",
            "Pin snapping lines the pin up with screen edges, other pins, and any window edge");
        public static readonly Str PinsTheCurrentFrameFrom = new(
            "help.pinsTheCurrentFrameFrom",
            "编辑器里把当前帧钉到屏幕",
            "Pins the current frame from the editor");
        public static readonly Str PlayAndPauseStep = new(
            "help.playAndPauseStep",
            "播放暂停 / 逐帧 · Shift 走一秒",
            "Play and pause / step a frame · Shift steps a second");
        public static readonly Str PressTheToolKeyAgain = new(
            "help.pressTheToolKeyAgain",
            "再按一次工具键或 {0} 退出绘制；滚轮调粗细，文字工具调字号；Alt 吸色。",
            "Press the tool key again or {0} to leave drawing; the wheel changes thickness, the text tool changes size; Alt picks a color.");
        public static readonly Str QuickStart = new("help.quickStart", "快速上手", "Quick Start");
        public static readonly Str RecognizeTheCurrentFrameResults = new(
            "help.recognizeTheCurrentFrameResults",
            "识别当前帧 → 结果窗：选哪段复制哪段",
            "Recognize the current frame → results window: copy the part you select");
        public static readonly Str RecordHoldToStartRelease = new(
            "help.recordHoldToStartRelease",
            "录制：按住起录，松手停",
            "Record: hold to start, release to stop");
        public static readonly Str RightClickAPin = new("help.rightClickAPin", "贴图右键", "Right-click a pin");
        public static readonly Str SaveTheCurrentFrameAs = new(
            "help.saveTheCurrentFrameAs",
            "存当前帧 PNG / 导出 MP4·WebM·GIF",
            "Save the current frame as PNG / export MP4·WebM·GIF");
        public static readonly Str SetARegionTriggerCopy = new(
            "help.setARegionTriggerCopy",
            "定范围 → 触发 → 复制。",
            "Set a region → trigger → copy.");
        public static readonly Str SetPerHotkeyFloatingCard = new(
            "help.setPerHotkeyFloatingCard",
            "每条快捷键单独设：弹浮窗 / 进编辑器 / 什么都不做 / 贴图；写不写剪贴板是另一条独立开关",
            "Set per hotkey: floating card, open in editor, nothing, or pin it. Writing to the clipboard is a separate switch.");
        public static readonly Str SetTheCaptureRegionDrag = new(
            "help.setTheCaptureRegionDrag",
            "设置捕获范围 · 拖框或点窗口",
            "Set the capture region · drag a box or click a window");
        public static readonly Str SettingsCanKeepTheRegion = new(
            "help.settingsCanKeepTheRegion",
            "设置里可开「常显捕获范围轮廓」，范围一眼在；「吸附到控件」让悬停吸到按钮/文字级。",
            "Settings can keep the region outline always visible; Snap to controls hovers down to button and text level.");
        public static readonly Str SnapsToTheWholeWindow = new(
            "help.snapsToTheWholeWindow",
            "自动吸附整窗；Tab 上钻到父容器",
            "Snaps to the whole window; Tab steps up to the parent");
        public static readonly Str Sound = new("help.sound", "提示音", "Sound");
        public static readonly Str StartStopTheRedRec = new(
            "help.startStopTheRedRec",
            "起录 / 停止 · REC 红徽标表示正在录",
            "Start / stop · the red REC badge means it is recording");
        public static readonly Str Step = new("help.step", "第 {0} / {1} 步", "Step {0} of {1}");
        public static readonly Str StitchesALongPage = new(
            "help.stitchesALongPage",
            "长网页/长对话自动拼接成一张",
            "Stitches a long page or conversation into one image");
        public static readonly Str TheColumnOnTheLeft = new(
            "help.theColumnOnTheLeft",
            "左侧竖栏，单键切换。",
            "The column on the left switches sections with a single key.");
        public static readonly Str TheRecordingBorder = new(
            "help.theRecordingBorder",
            "录屏边框只提示不入镜；「包含鼠标指针」可关。",
            "The recording border is a hint and never lands in the clip; Include cursor can be turned off.");
        public static readonly Str TheSnappingSwitchLives = new(
            "help.theSnappingSwitchLives",
            "吸附开关在设置「贴图」页。",
            "The snapping switch lives on the Pins page in Settings.");
        public static readonly Str TheThemeFollowsTheSystem = new(
            "help.theThemeFollowsTheSystem",
            "主题跟随系统，强调色可自定义",
            "The theme follows the system, the accent color is yours");
        public static readonly Str ThisGuideIsAlwaysOne = new(
            "help.thisGuideIsAlwaysOne",
            "随时回到本指南",
            "This guide is always one key away");
        public static readonly Str ThisPageIsOnly = new(
            "help.thisPageIsOnly",
            "本页只是地图，细节都在各处的悬停气泡里——指到哪读到哪。",
            "This page is only the map; the details are in tooltips wherever you hover");
        public static readonly Str TrayMenu = new("help.trayMenu", "托盘菜单", "Tray menu");
        public static readonly Str TriggerCaptures = new("help.triggerCaptures", "触发即截", "Trigger captures");
        public static readonly Str TrimTheHead = new(
            "help.trimTheHead",
            "掐头 / 去尾，只导出要的一段",
            "Trim the head or the tail and export only the part you want");
        public static readonly Str TurnTheText = new(
            "help.turnTheText",
            "图里的字，变文本。",
            "Turn the text in an image into real text.");
        public static readonly Str PrevArrow = new("help.prevArrow", "← 上一页", "← Previous");
        public static readonly Str UsageGuide = new("help.usageGuide", "使用指南", "Usage Guide");
        public static readonly Str UsesTheBuiltInWindows = new(
            "help.usesTheBuiltInWindows",
            "用 Windows 内置 OCR，离线可用；关「显示结果窗」则直接全文进剪贴板。",
            "Uses the built-in Windows OCR and works offline; turn off Show result window and everything goes to the clipboard.");
        public static readonly Str WatchTrimAnnotateCopy = new(
            "help.watchTrimAnnotateCopy",
            "看、剪、标、复制。",
            "Watch, trim, annotate, copy.");
        public static readonly Str Wheel = new("help.wheel", "滚轮 / +/-", "Wheel / +-");
        public static readonly Str WithARegionItCaptures = new(
            "help.withARegionItCaptures",
            "有范围截范围，没范围截光标所在屏",
            "With a region it captures that, otherwise the screen under the cursor");
        public static readonly Str ZoomThePinChangeOpacity = new(
            "help.zoomThePinChangeOpacity",
            "缩放贴图 / 调透明度",
            "Zoom the pin / change opacity");
        public static readonly Str ZoomToInspectPixels = new(
            "help.zoomToInspectPixels",
            "缩放；1:1 看像素",
            "Zoom; 1:1 to inspect pixels");
    }
}
