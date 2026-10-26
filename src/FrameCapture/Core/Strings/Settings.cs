namespace FrameCapture.Core;

/// <summary>
/// 文案 · 设置窗。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>设置窗的文案。</summary>
    public static partial class Settings
    {
        public static readonly Str Accent = new("settings.accent", "强调色", "Accent");
        public static readonly Str AccentColorEditorSelectionsSwitches = new(
            "settings.accentColorEditorSelectionsSwitches",
            "强调色：编辑器选中、开关、滑杆、进度都用它",
            "Accent color — editor selections, switches, sliders, and progress all use it");
        public static readonly Str ACustomOggWavFile = new(
            "settings.aCustomOggWavFile",
            "自定义提示音文件 ogg / wav，点击可重新选择",
            "A custom ogg / wav file; click to pick another");
        public static readonly Str AddAStepAfterThis = new(
            "settings.addAStepAfterThis",
            "在后面加一步",
            "Add a step after this");
        public static readonly Str Advanced = new("settings.advanced", "高级", "Advanced");
        public static readonly Str AFlashConfirmsIt = new(
            "settings.aFlashConfirmsIt",
            "快闪：确认一下就走",
            "A quick flash: confirms it, then gone");
        public static readonly Str AfterACaptureOnTop = new(
            "settings.afterACaptureOnTop",
            "捕获完成后，除复制到剪贴板外，再存一份文件到本地目录：截图存 PNG，录制按上面的「录制格式」存 MP4/WebM，未装 ffmpeg 则回退 GIF。视频在后台编码、文件稍后出现。写盘失败不影响剪贴板，捕获永远成功",
            "After a capture, on top of the clipboard copy, a file also goes to a local folder: stills as PNG, recordings as MP4 or WebM in the recording format above, falling back to GIF when ffmpeg is missing. Video encodes in the background, so the file shows up a moment later. A failed write never touches the clipboard — the capture always succeeds");
        public static readonly Str AfterASuccessfulRecognition = new(
            "settings.afterASuccessfulRecognition",
            "「识别文字」成功后弹出结果窗预览：可查看/编辑识别文本、鼠标悬停部分选中复制、一键复制全部——预览即选取入口，此时不再自动写剪贴板，要哪段选哪段。关闭则退回快速路径：直接写剪贴板 + 轻提示，不弹窗",
            "After a successful recognition a result window previews the text: read it, edit it, hover to select part of it and copy, or copy everything at once. The preview is the selection entry, so nothing is written to the clipboard on its own — take the part you want. Off, the fast path returns: straight to the clipboard with a toast, no window");
        public static readonly Str AfterStartupANoticeAppears = new(
            "settings.afterStartupANoticeAppears",
            "启动完成后在屏幕右下角弹一条「FrameCapture 已启动」，确认它真的在后台待命了。关掉后启动静默进托盘，只剩托盘图标",
            "After startup a notice appears in the bottom-right corner saying FrameCapture has started, confirming it is waiting in the background. Off, startup goes quietly to the tray and only the icon remains");
        public static readonly Str AfterTheSelectionHotkey = new(
            "settings.afterTheSelectionHotkey",
            "按下选区快捷键后，用虚线框显示上次的选区位置，按 Enter 直接确认重选同区域；关闭后每次都是干净屏",
            "After the selection hotkey a dashed box marks where the last selection was, and Enter confirms that same region again. Off, every session starts on a clean screen");
        public static readonly Str AfterTheTraySDelayed = new(
            "settings.afterTheTraySDelayed",
            "托盘「延时截图」触发后倒数这么久再截当前范围，留时间打开右键菜单/悬停提示/下拉框这类一动就消失的界面",
            "After the tray's Delayed Capture is triggered, counts down this long before capturing the current region, leaving time to open a context menu, a tooltip, or a dropdown — the kind of interface that vanishes the moment you move");
        public static readonly Str AlwaysShowTheCaptureOutline = new(
            "settings.alwaysShowTheCaptureOutline",
            "常显捕获范围轮廓",
            "Always show the capture outline");
        public static readonly Str AnAnimatedImageThatPlays = new(
            "settings.anAnimatedImageThatPlays",
            "通用动图，任何地方都能放；受格式封顶约 50fps、体积大",
            "An animated image that plays anywhere; capped near 50fps by the format and large on disk");
        public static readonly Str AnOpenVideoFormatEncoded = new(
            "settings.anOpenVideoFormatEncoded",
            "开源视频格式，用 VP9 编码，同画质体积更小，网页/上传首选",
            "An open video format encoded with VP9: the same quality at a smaller size, the first choice for the web and uploads");
        public static readonly Str AppearanceTheme = new("settings.appearanceTheme", "外观主题", "Appearance theme");
        public static readonly Str ASoftBuiltInBeep = new(
            "settings.aSoftBuiltInBeep",
            "内置柔和「嘀」声，作听觉确认",
            "A soft built-in beep as an audible confirmation");
        public static readonly Str AsSoonAsASelection = new(
            "settings.asSoonAsASelection",
            "完成选区后立即截取一帧 PNG 静态图，同时复制到剪贴板",
            "As soon as a selection is complete, one PNG frame is captured and copied to the clipboard");
        public static readonly Str AutoSaveACopy = new("settings.autoSaveACopy", "自动保存副本", "Auto-save a copy");
        public static readonly Str BorderFillColor = new(
            "settings.borderFillColor",
            "边框填充色",
            "Border fill color");
        public static readonly Str BorderFlashAfterACapture = new(
            "settings.borderFlashAfterACapture",
            "截图后边框显示时间",
            "Border flash after a capture");
        public static readonly Str BriskAGlanceFits = new(
            "settings.briskAGlanceFits",
            "利落：来得及看一眼",
            "Brisk: enough to catch a glance");
        public static readonly Str CaptureOnSelection = new(
            "settings.captureOnSelection",
            "选区自动截取",
            "Capture on selection");
        public static readonly Str CaptureSound = new("settings.captureSound", "截图提示音", "Capture sound");
        public static readonly Str Change = new("settings.change", "更改", "Change");
        public static readonly Str Chime = new("settings.chime", "轻弹", "Chime");
        public static readonly Str ChooseANotificationSound = new(
            "settings.chooseANotificationSound",
            "选择提示音",
            "Choose a notification sound");
        public static readonly Str ChooseTheSaveFolderAuto = new(
            "settings.chooseTheSaveFolderAuto",
            "选择保存文件夹，自动保存副本和导出对话框都用它",
            "Choose the save folder; auto-saved copies and export dialogs all use it");
        public static readonly Str ClickAWindowToCapture = new(
            "settings.clickAWindowToCapture",
            "点击窗口时截取完整窗口",
            "Click a window to capture the whole window");
        public static readonly Str ClickingAWindowThePicker = new(
            "settings.clickingAWindowThePicker",
            "选区窗里点一个吸附到的窗口，不再按屏幕矩形抓像素，而是真正截取该窗口本体——Win11 圆角处透明、浏览器/GPU 窗也干净，背景透明。关掉则点窗恢复旧的「按矩形区域截图」。拖框选区域不受影响；目标窗被别的窗遮挡或取帧失败时自动回退区域截图",
            "Clicking a window the picker snapped to no longer grabs screen pixels by rectangle; it captures the window itself — transparent at Win11's rounded corners, clean on browser and GPU-composited windows, transparent behind. Off, clicking a window goes back to capturing the rectangle. Dragging a box is unaffected, and a covered window or a failed frame falls back to a rectangle capture");        public static readonly Str ClickToHearTheCurrent = new(
            "settings.clickToHearTheCurrent",
            "点一下试听当前提示音，选「自定义…」后也会立刻放一声让你确认",
            "Click to hear the current sound; choosing Custom also plays one right away so you can confirm it");
        public static readonly Str ClickToPickAnAccent = new(
            "settings.clickToPickAnAccent",
            "点击自定义强调色，取色器与编辑器同款；右键恢复跟随系统",
            "Click to pick an accent color with the editor's own picker; right-click to follow the system");
        public static readonly Str ClickToPickTheFillColor = new(
            "settings.clickToPickTheFillColor",
            "点击选择填充色，取色器与编辑器同款",
            "Click to pick the fill color with the editor's own picker");
        public static readonly Str ClosingTheEditorRemembers = new(
            "settings.closingTheEditorRemembers",
            "关闭编辑器时记住你最后用的标注工具、颜色和线宽，下次打开直接是它们，省去每次重选。关闭后每次打开都回到默认：浏览 / 红 / 中",
            "Closing the editor remembers the annotation tool, color, and line width you last used, so the next opening starts with them instead of making you pick again. Off, every opening returns to the default: Pan / Red / Medium");
        public static readonly Str ColorFormat = new("settings.colorFormat", "取色格式", "Color format");
        public static readonly Str ComfortableEnoughToExpand = new(
            "settings.comfortableEnoughToExpand",
            "从容：够展开下拉/悬停",
            "Comfortable: enough to expand a dropdown or hover over it");
        public static readonly Str ComfortableEnoughToMove = new(
            "settings.comfortableEnoughToMove",
            "从容：够移动鼠标去点",
            "Comfortable: enough to move the mouse and click");
        public static readonly Str Custom = new("settings.custom", "自定义…", "Custom…");
        public static readonly Str CustomAnyNumberOfSeconds = new(
            "settings.customAnyNumberOfSeconds",
            "自定义：输入 1–120 任意秒数，回车或移开焦点生效",
            "Custom: any number of seconds from 1 to 120, applied on Enter or when focus leaves");
        public static readonly Str CustomAnythingFromToSeconds = new(
            "settings.customAnythingFromToSeconds",
            "自定义：输入 0.5–60 任意秒数，0 为常驻，回车或移开焦点生效",
            "Custom: anything from 0.5 to 60 seconds, 0 keeps it resident, applied on Enter or when focus leaves");
        public static readonly Str Dark = new("settings.dark", "深色", "Dark");
        public static readonly Str DeleteThisHotkey = new(
            "settings.deleteThisHotkey",
            "删除这条快捷键",
            "Delete this hotkey");
        public static readonly Str DraggingAPinSnapsIt = new(
            "settings.draggingAPinSnapsIt",
            "拖动贴图时实时吸附对齐：贴近所在屏幕边缘则停靠四边/四角；贴近同屏其他贴图、以及浏览器或资源管理器等任何应用窗口的边或中线则与之对齐，把贴图摆齐像吸附网格一样自然。阈值调低为「贴边才吸」，不会隔空乱跳。关闭则松手停在原地",
            "Dragging a pin snaps it into alignment: near a screen edge it docks to an edge or a corner, and near another pin on the same screen or the edge or center line of any app window — a browser or File Explorer included — it lines up with it. A lower threshold means it only grabs when close, so nothing jumps across the screen. Off, a pin stops where you let go");
        public static readonly Str DragThisStepToReorder = new(
            "settings.dragThisStepToReorder",
            "按住拖动调整顺序",
            "Drag to reorder this step");
        public static readonly Str DuringARecordingTheReal = new(
            "settings.duringARecordingTheReal",
            "录制期间按真实捕获范围高亮描边，只提示、不录进产物。取代 Windows 11 那条绕整屏的黄色系统捕获边框——WGC 只能整显示器捕获，系统边框必然全屏、与所选范围无关",
            "During a recording the real capture region is outlined in a highlight, as a hint only — it never lands in the output. It stands in for the yellow system capture border Windows 11 draws around the whole screen: WGC captures a whole monitor, so that border is always full-screen and says nothing about your selection");
        public static readonly Str EditTheStepsThatRun = new(
            "settings.editTheStepsThatRun",
            "逐条编辑完成后与失败时要跑的动作",
            "Edit the steps that run on completion and on failure, one by one.");
        public static readonly Str FfmpegDetected = new(
            "settings.ffmpegDetected",
            " · 已检测到 ffmpeg",
            " · ffmpeg detected");
        public static readonly Str FfmpegDetectedMp4AndWebm = new(
            "settings.ffmpegDetectedMp4AndWebm",
            "已检测到 ffmpeg · 可导出 MP4 / WebM",
            "ffmpeg detected · MP4 and WebM export available");
        public static readonly Str FfmpegNotFoundInstallFfmpeg = new(
            "settings.ffmpegNotFoundInstallFfmpeg",
            " · 未检测到 ffmpeg，装 FFmpeg 后可选",
            " · ffmpeg not found; install FFmpeg to enable");
        public static readonly Str FfmpegNotFoundMp4 = new(
            "settings.ffmpegNotFoundMp4",
            "未检测到 ffmpeg · 导出 MP4/WebM 需先安装，见帮助 F1，GIF 不受影响",
            "ffmpeg not found · MP4 and WebM export needs it installed first, see the F1 help; GIF is unaffected");
        public static readonly Str FillsTheShadowBandFor = new(
            "settings.fillsTheShadowBandFor",
            "给不支持透明度的格式填充边框与阴影",
            "Fills the border and shadow band for formats without transparency");
        public static readonly Str FollowSystem = new("settings.followSystem", "跟随系统", "Follow system");
        public static readonly Str FramesCapturedPerSecondWhile = new(
            "settings.framesCapturedPerSecondWhile",
            "录制时每秒捕获的帧数；开启下方「跟随显示器刷新率」时本项隐藏，由刷新率取代",
            "Frames captured per second while recording. With Record at full monitor refresh below on, this row hides and the refresh rate takes over");
        public static readonly Str FreezeTheScreenWhileSelecting = new(
            "settings.freezeTheScreenWhileSelecting",
            "选区时定格画面",
            "Freeze the screen while selecting");
        public static readonly Str GroupInteraction = new("settings.groupInteraction", "交互", "Interaction");
        public static readonly Str GroupLine = new("settings.groupLine", "描边", "Lines");
        public static readonly Str GroupSemantic = new("settings.groupSemantic", "语义", "Semantic");
        public static readonly Str GroupSurface = new("settings.groupSurface", "表面", "Surfaces");
        public static readonly Str GroupText = new("settings.groupText", "文字色", "Text");
        public static readonly Str HighRefreshPanelsAtHz = new(
            "settings.highRefreshPanelsAtHz",
            "144 / 165 / 240Hz 这类高刷屏录满才丝滑；MP4/WebM 可录满，GIF 受格式封顶约 50fps",
            "High-refresh panels at 144 / 165 / 240Hz only look smooth when recorded at the full rate; MP4 and WebM can take it, GIF is capped near 50fps by the format");
        public static readonly Str Hotkeys = new("settings.hotkeys", "快捷键", "Hotkeys");
        public static readonly Str HoveringSnapsToASpecific = new(
            "settings.hoveringSnapsToASpecific",
            "悬停自动吸附窗口内的具体控件：输入框、按钮、链接、文字、图片、列表项等，按 Tab 可上钻到父容器。依赖应用暴露无障碍元素——浏览器与多数原生应用可以；基于 Electron 的应用多数只暴露整窗，会退化成整窗吸附",
            "Hovering snaps to a specific control inside the window — input boxes, buttons, links, text, images, list items — and Tab steps up to the parent container. It depends on the app exposing accessibility elements: browsers and most native apps do; Electron-based apps usually expose only the whole window, so snapping degrades to the window");
        public static readonly Str HowLongTheCopiedThumbnail = new(
            "settings.howLongTheCopiedThumbnail",
            "截完弹出的「已复制」缩略浮窗停留多久自动消失。悬停其上会暂停计时、移开再给满时长。想拍完即走调小、想留着慢慢点进编辑器调大或常驻。",
            "How long the copied thumbnail toast stays before dismissing itself. Hovering pauses the timer, and moving away gives it the full duration again. Turn it down to move on at once, or up — or choose Resident — to keep it around and click into the editor at leisure.");
        public static readonly Str HslHSLCommon = new(
            "settings.hslHSLCommon",
            "hsl(h, s%, l%) 调色和设计常用",
            "hsl(h, s%, l%), common for color and design work");
        public static readonly Str IncludeTheMousePointer = new(
            "settings.includeTheMousePointer",
            "录制包含鼠标指针",
            "Include the mouse pointer");
        public static readonly Str IncludeTheShadowInWindow = new(
            "settings.includeTheShadowInWindow",
            "窗口截图包含投影",
            "Include the shadow in window captures");
        public static readonly Str KeepsTheCurrentCaptureRegion = new(
            "settings.keepsTheCurrentCaptureRegion",
            "在屏幕上持续显示当前捕获范围的边框，开/关立刻生效。尚未设置范围时没有可显示的边框——先用托盘「设置捕获范围」",
            "Keeps the current capture region's border on screen, and switching takes effect at once. With no region set there is nothing to show — set one first from the tray under Set Capture Region");
        public static readonly Str Language = new("settings.language", "界面语言", "Language");
        public static readonly Str Light = new("settings.light", "浅色", "Light");
        public static readonly Str ListSeparator = new("settings.listSeparator", "、", ", ");
        public static readonly Str LowContrastOnSurfaces = new("settings.lowContrastOnSurfaces", "这个颜色和它要压着的底色对比不足，正文会读不清", "Low contrast against the surfaces this text sits on — body text will be hard to read");
        public static readonly Str NeverDisappearsOnItsOwn = new(
            "settings.neverDisappearsOnItsOwn",
            "不自动消失，点一下或 Esc 才关",
            "Never disappears on its own; a click or Esc closes it");
        public static readonly Str NoDelayCapturesAsSoon = new(
            "settings.noDelayCapturesAsSoon",
            "不延时，触发即截",
            "No delay; captures as soon as it triggers");
        public static readonly Str None = new("settings.none", "不预抓", "None");
        public static readonly Str NoSound = new("settings.noSound", "静音", "No sound");
        public static readonly Str NoSoundAFinishedCapture = new(
            "settings.noSoundAFinishedCapture",
            "静音：截图完成不放声音",
            "No sound: a finished capture stays quiet");
        public static readonly Str Off = new("settings.off", "关", "Off");
        public static readonly Str OffKeepsTheSetupBut = new(
            "settings.offKeepsTheSetupBut",
            "关掉即保留配置但不触发，无需删除",
            "Off keeps the setup but stops it from firing — no need to delete it.");
        public static readonly Str Output = new("settings.output", "产物", "Output");
        public static readonly Str PalettePresets = new("settings.palettePresets", "预设", "Presets");
        public static readonly Str PaletteRestore = new("settings.paletteRestore", "恢复出厂配色", "Restore the stock palette");
        public static readonly Str PicturesFramecaptureDefaultLocation = new(
            "settings.picturesFramecaptureDefaultLocation",
            "图片\\FrameCapture · 默认位置",
            "Pictures\\FrameCapture · default location");
        public static readonly Str PinSnapping = new("settings.pinSnapping", "贴图窗口吸附", "Pin snapping");
        public static readonly Str PlentyEnoughToLineUp = new(
            "settings.plentyEnoughToLineUp",
            "充裕：够摆好几个界面元素",
            "Plenty: enough to line up several interface elements");
        public static readonly Str PresetContrast = new("settings.presetContrast", "高对比", "High contrast");
        public static readonly Str PresetDefault = new("settings.presetDefault", "出厂", "Stock");
        public static readonly Str PresetGraphite = new("settings.presetGraphite", "石墨", "Graphite");
        public static readonly Str PresetMidnight = new("settings.presetMidnight", "午夜蓝", "Midnight");
        public static readonly Str PresetSand = new("settings.presetSand", "暖沙", "Warm sand");
        public static readonly Str Preview = new("settings.preview", "试听", "Preview");
        public static readonly Str QuickEnoughToMove = new(
            "settings.quickEnoughToMove",
            "快速：够移一下鼠标",
            "Quick: enough to move the mouse");
        public static readonly Str QuotedName = new("settings.quotedName", "「{0}」", "“{0}”");
        public static readonly Str RecordAtFullMonitorRefresh = new(
            "settings.recordAtFullMonitorRefresh",
            "跟随显示器刷新率录满",
            "Record at full monitor refresh");
        public static readonly Str RecordingFormat = new("settings.recordingFormat", "录制格式", "Recording format");
        public static readonly Str RecordingFrameRate = new(
            "settings.recordingFrameRate",
            "录制帧率",
            "Recording frame rate");
        public static readonly Str RecordWindowOnly = new("settings.recordWindowOnly", "只录窗口", "Record window only");
        public static readonly Str RememberTheLastToolColor = new(
            "settings.rememberTheLastToolColor",
            "记住上次的工具/颜色/粗细",
            "Remember the last tool, color, and width");
        public static readonly Str Reset = new("settings.reset", "恢复", "Reset");
        public static readonly Str Resident = new("settings.resident", "常驻", "Resident");
        public static readonly Str RestoreAllDefaultKeys = new(
            "settings.restoreAllDefaultKeys",
            "全部恢复默认键位",
            "Restore all default keys");
        public static readonly Str RgbRGBPastes = new(
            "settings.rgbRGBPastes",
            "rgb(r, g, b) 可直接贴进 CSS",
            "rgb(r, g, b) pastes straight into CSS");
        public static readonly Str RrggbbTheCommonForm = new(
            "settings.rrggbbTheCommonForm",
            "#RRGGBB 通用格式",
            "#RRGGBB, the common form");
        public static readonly Str S = new("settings.s", "{0:0.0} 秒", "{0:0.0} s");
        public static readonly Str S2 = new("settings.s2", "10秒", "10s");
        public static readonly Str S3 = new("settings.s3", "1秒", "1s");
        public static readonly Str S4 = new("settings.s4", "2秒", "2s");
        public static readonly Str S5 = new("settings.s5", "3秒", "3s");
        public static readonly Str S6 = new("settings.s6", "5秒", "5s");
        public static readonly Str S7 = new("settings.s7", "秒", "s");
        public static readonly Str SaveFolderAutoSavedCopies = new(
            "settings.saveFolderAutoSavedCopies",
            "保存文件夹：自动保存副本存到这里，编辑器/贴图「另存为」对话框也默认打开这里",
            "Save folder: auto-saved copies go here, and the editor and pin Save As dialogs open here by default");
        public static readonly Str SelectionMagnifier = new(
            "settings.selectionMagnifier",
            "选区放大镜",
            "Selection magnifier");
        public static readonly Str Semicolon = new("settings.semicolon", "；", "; ");
        public static readonly Str SharesAKeyWith = new(
            "settings.sharesAKeyWith",
            "与{0}同键 {1}",
            "Shares a key with {0} — {1}");
        public static readonly Str ShowsOnlyWhereEachCase = new(
            "settings.showsOnlyWhereEachCase",
            "只显示默认落点，用不到的不必展开",
            "Shows only where each case lands — what you don't use stays folded.");
        public static readonly Str ShowTheLastSelection = new(
            "settings.showTheLastSelection",
            "显示上次选区",
            "Show the last selection");
        public static readonly Str ShowTheRegionBorderWhile = new(
            "settings.showTheRegionBorderWhile",
            "录制时显示范围边框",
            "Show the region border while recording");
        public static readonly Str ShowTheResultWindowAfter = new(
            "settings.showTheResultWindowAfter",
            "识别文字后显示结果窗",
            "Show the result window after recognizing text");
        public static readonly Str Simple = new("settings.simple", "简单", "Simple");
        /// <summary>「可调整选区」标题（默认关闭）。</summary>
        public static readonly Str EditableSelection = new("settings.editableSelection", "可调整选区", "Adjustable selection");

        /// <summary>说明：松手后的框能拖边角改大小、可整块移动，确认才出图。</summary>
        public static readonly Str EditableSelectionDesc = new(
            "settings.editableSelectionDesc",
            "松手后先给一个能拖边角改大小、可整块移动的框，确认或按 Enter 才出图；关着时松手立刻出图",
            "After releasing, keep an adjustable frame — drag an edge or corner to resize, drag inside to move, confirm with Enter before the shot is taken. When off, releasing captures right away");


        /// <summary>键位行的说明：什么时候生效。</summary>
        public static readonly Str ConfirmCancelKeysHint = new(
            "settings.confirmCancelKeysHint",
            "确认与取消",
            "Confirm and cancel");

        /// <summary>上面那一行键位的长说明，放 ToolTip，不占行内宽度。</summary>
        public static readonly Str ConfirmCancelKeysTip = new(
            "settings.confirmCancelKeysTip",
            "可调整的框出现后生效，点右侧的键位可录入其他键",
            "Takes effect once the adjustable frame is up; click a field to record another key");
        public static readonly Str SnapToControls = new("settings.snapToControls", "吸附到控件", "Snap to controls");
        public static readonly Str SoundsOggWavOggWav = new(
            "settings.soundsOggWavOggWav",
            "提示音 (*.ogg;*.wav)|*.ogg;*.wav|所有文件 (*.*)|*.*",
            "Sounds (*.ogg;*.wav)|*.ogg;*.wav|All files (*.*)|*.*");
        public static readonly Str StartsFramecaptureWhenYouSign = new(
            "settings.startsFramecaptureWhenYouSign",
            "登录 Windows 时自动启动 FrameCapture",
            "Starts FrameCapture when you sign in to Windows");
        public static readonly Str ShowTheKeysInTheReady = new(
            "settings.showTheKeysInTheReady",
            "就绪提示列出键位",
            "Show the keys in the ready card");
        public static readonly Str CaptureTheAppItself = new(
            "settings.captureTheAppItself",
            "允许截到自己",
            "Allow capturing FrameCapture");
        public static readonly Str TokenCardBg = new("settings.tokenCardBg", "卡片底色", "Card");
        public static readonly Str TokenChromeBg = new("settings.tokenChromeBg", "标题栏底色", "Title bar");
        public static readonly Str TokenDanger = new("settings.tokenDanger", "提醒", "Danger");
        public static readonly Str TokenHover = new("settings.tokenHover", "悬停叠加", "Hover overlay");
        public static readonly Str TokenNavBg = new("settings.tokenNavBg", "侧栏底色", "Sidebar");
        public static readonly Str TokenPanelBg = new("settings.tokenPanelBg", "面板底色", "Panel");
        public static readonly Str TokenPress = new("settings.tokenPress", "按下叠加", "Pressed overlay");
        public static readonly Str TokenRowBg = new("settings.tokenRowBg", "行底色", "Row");
        public static readonly Str TokenRowHover = new("settings.tokenRowHover", "行悬停", "Row hover");
        public static readonly Str TokenStroke = new("settings.tokenStroke", "标准描边", "Stroke");
        public static readonly Str TokenStrokeSoft = new("settings.tokenStrokeSoft", "细描边", "Soft stroke");
        public static readonly Str TokenSuccess = new("settings.tokenSuccess", "成色", "Success");
        public static readonly Str TokenText = new("settings.tokenText", "正文", "Body text");
        public static readonly Str TokenTextDim = new("settings.tokenTextDim", "次要文字", "Secondary text");
        public static readonly Str TokenTrack = new("settings.tokenTrack", "轨道", "Track");
        public static readonly Str TokenWarning = new("settings.tokenWarning", "注意", "Warning");
        public static readonly Str TokenWindowBg = new("settings.tokenWindowBg", "窗口底色", "Window");
        public static readonly Str WhenOnTheEditorTheRecord = new(
            "settings.whenOnTheEditorTheRecord",
            "打开后，编辑器、录制控制条、选区遮罩都会出现在你的截图和录屏里——想录一段界面演示时用；平时关着，浮窗与轮廓不会进产物",
            "When on, the editor, the recording bar and the selection mask all show up in your shots and recordings - use it to demo the UI; leave it off and toasts and outlines stay out of the result");
        public static readonly Str CopyKey = new(
            "settings.copyKey",
            "截图后的复制键",
            "Key to copy a shot");
        public static readonly Str TheToastAlsoAccepts = new(
            "settings.theToastAlsoAccepts",
            "截图浮窗还在的那几秒里，这个组合键直接复制这张图；只认完全一致的修饰键，别的组合照常留给你正在用的软件",
            "While the capture card is up, this chord copies the shot; only the exact modifier set is taken - other combinations still go to the app you are using");
        public static readonly Str TheReadyCardLists = new(
            "settings.theReadyCardLists",
            "就绪卡会在名字旁边列出当前生效的截图键与录制键；对键位已经上手后可以关掉，卡片只留标题和帮助入口",
            "The ready card lists the capture and record keys that are live right now, next to their names. Once your keys are muscle memory, turn it off and the card keeps only its title and the help entry");
        public static readonly Str StartupNotification = new(
            "settings.startupNotification",
            "启动气泡提示",
            "Startup notification");
        public static readonly Str System = new("settings.system", "系统", "System");
        public static readonly Str System2 = new("settings.system2", "系统默认", "System");
        public static readonly Str TheCursorAppears = new(
            "settings.theCursorAppears",
            "录出的画面里带上鼠标光标，默认开。关掉后成片干净无光标，适合做教程/演示时后期自己加高亮，或不想让光标乱入",
            "The cursor appears in the recording, on by default. Off, the output stays clean, which suits tutorials where you add your own highlight afterwards, or times you would rather the cursor kept out");
        public static readonly Str TheFormatTheEditorS = new(
            "settings.theFormatTheEditorS",
            "录制完成后编辑器「导出录制」默认用的格式。MP4/WebM 需本机装有 ffmpeg，自动探测 PATH 与常见安装目录；未装则视频项会提示安装，GIF 始终可用",
            "The format the editor's Export recording uses by default. MP4 and WebM need ffmpeg on this machine, looked for on PATH and in the usual install folders; without it the video options offer to install it, and GIF is always available");
        public static readonly Str TheInstantTheSelectionHotkey = new(
            "settings.theInstantTheSelectionHotkey",
            "按下选区热键的瞬间抓一张全屏定格帧，整个选区过程画面看起来就定格了——放大镜读定格帧、拖框/区域截图也裁这张定格帧，不再选完后重新抓屏，所见即所得。关掉则全程实时：屏幕实时可见、放大镜每次移动实时跟画面、截图实时重抓",
            "The instant the selection hotkey goes down a full-screen frame is taken, and the whole selection looks frozen: the magnifier reads the frozen frame, and dragging a box or capturing a region crops that same frame instead of re-capturing the screen afterwards — what you see is what you get. Off, everything is live: the screen keeps moving, the magnifier follows it on every move, and captures re-grab the screen");
        public static readonly Str Theme = new("settings.theme", "主题", "Theme");
        public static readonly Str TheMomentACaptureFinishes = new(
            "settings.theMomentACaptureFinishes",
            "截完那一下范围边框「亮边闪一下」作确认——这里设它全亮停留多久再淡出。拍完即走调小、想多留一眼确认截对没调大。它与「常显轮廓」和录制边框各自独立",
            "The moment a capture finishes the region border lights up as confirmation; this sets how long it stays fully lit before fading. Turn it down to move on at once, up to double-check you caught the right thing. It is independent of the always-on outline and of the recording border");
        public static readonly Str TheMostCompatibleVideoFormat = new(
            "settings.theMostCompatibleVideoFormat",
            "兼容性最好的视频格式，用 libx264 编码，浏览器/剪辑软件/社交平台通吃",
            "The most compatible video format, encoded with libx264, at home in browsers, editors, and social platforms");
        public static readonly Str TheRecordingFrameRateThen = new(
            "settings.theRecordingFrameRateThen",
            "开启后录制帧率自动取当前录制显示器的刷新率，本机暂未检测到主屏刷新率，录制时按实际显示器实时检测，上面的手动帧率随之隐藏。",
            "The recording frame rate then follows the refresh rate of the display being recorded; no primary refresh rate was detected here, so it is read live from the actual display when recording starts and the manual frame rate above hides.");
        public static readonly Str TheRecordingFrameRateThen2 = new(
            "settings.theRecordingFrameRateThen2",
            "开启后录制帧率自动取当前录制显示器的刷新率，本机主屏检测到 {0}Hz → 录满 {1}fps，上面的手动帧率随之隐藏。",
            "The recording frame rate then follows the refresh rate of the display being recorded; the primary screen here reads {0}Hz, so recording runs at {1}fps and the manual frame rate above hides.");
        public static readonly Str TheSoundThatConfirms = new(
            "settings.theSoundThatConfirms",
            "静态截图完成时的听觉确认。静音=不放声音；轻弹=内置柔和「嘀」；自定义…=选一个 ogg 或 wav 文件，wav 最稳，ogg 需系统解码器，播不了会自动回退轻弹。随时可「试听」",
            "The sound that confirms a still capture finished. No sound is silence; Chime is a soft built-in beep; Custom picks an ogg or wav file — wav is the safest, ogg needs a system decoder and falls back to the chime when it cannot play. Preview is always there");
        public static readonly Str TheTextFormThePicker = new(
            "settings.theTextFormThePicker",
            "选区放大镜按 C 复制颜色时的文本形式：#RRGGBB / rgb(r, g, b) / hsl(h, s%, l%)",
            "The text form the picker's C key copies: #RRGGBB / rgb(r, g, b) / hsl(h, s%, l%)");
        public static readonly Str ToastLifetime = new("settings.toastLifetime", "悬浮窗存在时间", "Toast lifetime");
        public static readonly Str ToolbarEntranceAnimation = new(
            "settings.toolbarEntranceAnimation",
            "底部工具栏入场动效",
            "Toolbar entrance animation");
        public static readonly Str UsualEnoughToOpen = new(
            "settings.usualEnoughToOpen",
            "常用：够打开一个菜单",
            "Usual: enough to open a menu");
        public static readonly Str WhenBothFreezeTheScreen = new(
            "settings.whenBothFreezeTheScreen",
            "「定格画面」与「点击窗口时截取完整窗口」都开着时，定格瞬间后台按 Z 序、当前聚焦窗最优先，预抓这么多「完整可见」窗口的干净画面；点窗口即用缓存＝定格+干净圆角+投影，没缓存好则实时抓兜底。0 = 不预抓，点窗口一律实时抓",
            "When both Freeze the screen while selecting and Click a window to capture the whole window are on, the moment of the freeze pre-captures clean images of up to this many fully visible windows in Z order, focused window first. Clicking a window then reads the cache: freeze plus clean rounded corners plus shadow, with a live capture as the fallback. 0 means no pre-capture, so every window click captures live");
        public static readonly Str WhenTheEditorOpens = new(
            "settings.whenTheEditorOpens",
            "打开编辑器时，底部两排图标逐项落定，视线跟着落到工具上。关闭后工具栏随窗口直接出现，一个动画都不放",
            "When the editor opens, the two rows of icons at the bottom settle one by one and the eye lands on the tools with them. Off, the toolbar appears with the window and no animation plays");
        public static readonly Str WhenYouClickAWindow = new(
            "settings.whenYouClickAWindow",
            "录制选区里点一个窗口起录时，录的是这个窗口本体而不是那块屏幕矩形：别的窗口盖上来、窗口挪位置都不会进画面，成片里只有它。圆角处按白底拍平，GIF 与 MP4 没有透明通道。关掉则一律按屏幕矩形录",
            "When you click a window in the recording selection, it records that window itself rather than the screen rectangle: another window covering it, or moving it around, never enters the frame — the output holds only that window. Rounded corners are flattened onto white, because GIF and MP4 carry no transparency. Off, everything records as a screen rectangle");
        public static readonly Str WhileYouDragOrHover = new(
            "settings.whileYouDragOrHover",
            "拖框或悬停选区时，光标旁显示高倍像素放大镜、坐标/尺寸读数与光标下像素颜色，按 C 复制颜色、格式见下，便于像素级精确选区/取色；关闭可省一点开销",
            "While you drag or hover a selection, a high-magnification pixel view sits beside the cursor with coordinate and size readouts and the color under the cursor. C copies that color in the format set below, for pixel-precise selection and picking. Off saves a little overhead");
        public static readonly Str Windows = new("settings.windows", "{0} 个", "{0} windows");
        public static readonly Str WindowsPreCapturedOnFreeze = new(
            "settings.windowsPreCapturedOnFreeze",
            "定格时预抓窗口数",
            "Windows pre-captured on freeze");
        public static readonly Str WithTheSwitchAbove = new(
            "settings.withTheSwitchAbove",
            "在上面开关开启时，额外把窗口边缘的真实投影截进来、外缘柔和渐隐到透明。关掉则只截窗口本体，圆角透明、无投影",
            "With the switch above on, the real shadow along the window's edge is captured too, its outer edge fading softly to transparent. Off, only the window body is captured: transparent corners, no shadow");
    }
}
