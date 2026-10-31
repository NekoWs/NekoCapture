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
        public static readonly Str SnapDwell = new("settings.snapDwell", "吸附停留", "Snap delay");
        public static readonly Str SnapDwellHint = new("settings.snapDwellHint", "0 毫秒为立即获取", "Set to 0 ms to snap immediately.");
        public static readonly Str Accent = new("settings.accent", "强调色", "Accent");
        public static readonly Str AccentColorEditorSelectionsSwitches = new("settings.accentColorEditorSelectionsSwitches", "用于选中、开关和进度", "Used for selections, switches, and progress");
        public static readonly Str ACustomOggWavFile = new("settings.aCustomOggWavFile", "自定义 OGG/WAV，点击更换", "Custom OGG/WAV; click to change");
        public static readonly Str AFlashConfirmsIt = new(
            "settings.aFlashConfirmsIt",
            "快闪：确认一下就走",
            "A quick flash: confirms it, then gone");
        public static readonly Str AfterACaptureOnTop = new("settings.afterACaptureOnTop", "另存本地副本。截图为 PNG，录制按所选格式保存；视频编码在后台完成", "Save a local copy: PNG for screenshots, the selected format for recordings. Video encodes in the background.");
        public static readonly Str AfterASuccessfulRecognition = new("settings.afterASuccessfulRecognition", "在原图上选字复制；关闭后直接复制全文", "Select text on the image; turn off to copy all text directly.");
        public static readonly Str AfterStartupANoticeAppears = new("settings.afterStartupANoticeAppears", "启动时显示通知；关闭后静默进入托盘", "Show a startup notice; turn off for a quiet tray startup.");
        public static readonly Str AfterTheSelectionHotkey = new("settings.afterTheSelectionHotkey", "显示上次选区，可按确认键复用", "Show the last region; confirm to reuse it.");
        public static readonly Str AfterTheTraySDelayed = new("settings.afterTheTraySDelayed", "延时截图的倒计时时长", "Countdown before a delayed capture");
        public static readonly Str AlwaysShowTheCaptureOutline = new("settings.alwaysShowTheCaptureOutline", "常显范围边框", "Keep region outline visible");
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
        public static readonly Str AutoSaveACopy = new("settings.autoSaveACopy", "自动保存副本", "Auto-save a copy");
        public static readonly Str BorderFillColor = new(
            "settings.borderFillColor",
            "边框填充色",
            "Border fill color");
        public static readonly Str BorderFlashAfterACapture = new("settings.borderFlashAfterACapture", "截图边框时长", "Screenshot outline duration");
        public static readonly Str BriskAGlanceFits = new(
            "settings.briskAGlanceFits",
            "利落：来得及看一眼",
            "Brisk: enough to catch a glance");
        public static readonly Str CaptureSound = new("settings.captureSound", "截图提示音", "Capture sound");
        public static readonly Str Change = new("settings.change", "更改", "Change");
        public static readonly Str Chime = new("settings.chime", "轻弹", "Chime");
        public static readonly Str ChooseANotificationSound = new(
            "settings.chooseANotificationSound",
            "选择提示音",
            "Choose a notification sound");
        public static readonly Str ChooseTheSaveFolderAuto = new("settings.chooseTheSaveFolderAuto", "选择副本与导出的默认目录", "Choose the default folder for copies and exports");
        public static readonly Str ClickAWindowToCapture = new("settings.clickAWindowToCapture", "捕获窗口本体", "Capture whole windows");
        public static readonly Str ClickingAWindowThePicker = new("settings.clickingAWindowThePicker", "点击窗口时保留透明边缘；捕获失败时使用屏幕区域", "Keep transparent window edges; fall back to the screen region if capture fails.");        public static readonly Str ClickToHearTheCurrent = new("settings.clickToHearTheCurrent", "试听当前提示音", "Preview the current sound");
        public static readonly Str ClickToPickAnAccent = new("settings.clickToPickAnAccent", "点击选色；右键跟随系统", "Click to choose a color; right-click to follow the system.");
        public static readonly Str ClickToPickTheFillColor = new("settings.clickToPickTheFillColor", "选择填充颜色", "Choose a fill color");
        public static readonly Str ClosingTheEditorRemembers = new("settings.closingTheEditorRemembers", "下次打开沿用工具、颜色和线宽", "Reuse the tool, color, and line width next time.");
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
            "点开挑一档，或直接打 1–120 的秒数，回车生效",
            "Pick a preset, or type any number of seconds from 1 to 120; Enter applies it");
        public static readonly Str CustomAnythingFromToSeconds = new(
            "settings.customAnythingFromToSeconds",
            "点开挑一档，或直接打 0.5–60 的秒数，0 为常驻，回车生效",
            "Pick a preset, or type 0.5 to 60 seconds; 0 keeps it resident, Enter applies it");
        public static readonly Str Dark = new("settings.dark", "深色", "Dark");
        public static readonly Str DeleteThisHotkey = new(
            "settings.deleteThisHotkey",
            "删除这条快捷键",
            "Delete this hotkey");
        public static readonly Str DraggingAPinSnapsIt = new("settings.draggingAPinSnapsIt", "拖动时对齐屏幕、窗口和其他贴图的边缘或中线", "Snap to screen, window, and pin edges or centers while dragging.");
        public static readonly Str DuringARecordingTheReal = new("settings.duringARecordingTheReal", "标出实际录制区域，边框不进入产物", "Outline the recording region without including the outline in the output.");
        public static readonly Str FfmpegDetectedMp4AndWebm = new(
            "settings.ffmpegDetectedMp4AndWebm",
            "已检测到 ffmpeg · 可导出 MP4 / WebM",
            "ffmpeg detected · MP4 and WebM export available");
        public static readonly Str ComponentNotInstalled = new("settings.componentNotInstalled", "未安装 · 下载约 {0} MiB", "Not installed · About {0} MiB to download");
        public static readonly Str ComponentConnecting = new("settings.componentConnecting", "正在连接下载源…", "Connecting to download sources…");
        public static readonly Str ComponentDownloading = new("settings.componentDownloading", "下载 {0}% · {1} / {2} MiB · {3}", "Downloading {0}% · {1} / {2} MiB · {3}");
        public static readonly Str ComponentInstallFailed = new("settings.componentInstallFailed", "安装失败，点击重试", "Installation failed. Click to retry.");
        public static readonly Str FillsTheShadowBandFor = new(
            "settings.fillsTheShadowBandFor",
            "给不支持透明度的格式填充边框与阴影",
            "Fills the border and shadow band for formats without transparency");
        public static readonly Str FollowSystem = new("settings.followSystem", "跟随系统", "Follow system");
        public static readonly Str FramesCapturedPerSecondWhile = new("settings.framesCapturedPerSecondWhile", "每秒捕获帧数；跟随刷新率时自动设置", "Frames per second; set automatically when matching the refresh rate.");
        public static readonly Str FreezeTheScreenWhileSelecting = new("settings.freezeTheScreenWhileSelecting", "选区时定格", "Freeze during selection");
        public static readonly Str HighRefreshPanelsAtHz = new("settings.highRefreshPanelsAtHz", "MP4/WebM 支持高帧率，GIF 约限 50 fps", "MP4/WebM support high frame rates; GIF is limited to about 50 fps.");
        public static readonly Str Hotkeys = new("settings.hotkeys", "快捷键", "Hotkeys");
        public static readonly Str HoveringSnapsToASpecific = new("settings.hoveringSnapsToASpecific", "悬停吸附控件；应用需支持无障碍接口", "Hover to snap to a control; requires accessibility support.");
        public static readonly Str HowLongTheCopiedThumbnail = new("settings.howLongTheCopiedThumbnail", "截图浮窗停留时间；悬停暂停，移开重新计时", "Screenshot toast duration; hovering pauses it and leaving restarts the timer.");
        public static readonly Str HslHSLCommon = new(
            "settings.hslHSLCommon",
            "hsl(h, s%, l%) 调色和设计常用",
            "hsl(h, s%, l%), common for color and design work");
        public static readonly Str IncludeTheMousePointer = new("settings.includeTheMousePointer", "录制鼠标指针", "Record cursor");
        public static readonly Str IncludeTheShadowInWindow = new("settings.includeTheShadowInWindow", "包含窗口投影", "Include window shadow");
        public static readonly Str InkClear = new("settings.inkClear", "正文在这四种底色上都读得清", "Body text reads clearly on all four surfaces");
        public static readonly Str InkLowOnSurfaces = new("settings.inkLowOnSurfaces", "{0} 处正文和它压着的底色对比不足", "{0} spots where body text lacks contrast against the surface under it");
        public static readonly Str KeepsTheCurrentCaptureRegion = new("settings.keepsTheCurrentCaptureRegion", "持续显示已选区域的边框，切换立即生效", "Keep the selected region outlined; changes apply immediately.");
        public static readonly Str Language = new("settings.language", "界面语言", "Language");
        public static readonly Str Light = new("settings.light", "浅色", "Light");
        public static readonly Str ListSeparator = new("settings.listSeparator", "、", ", ");
        public static readonly Str LowContrastOnSurfaces = new("settings.lowContrastOnSurfaces", "文字与底色对比不足", "Low text contrast against the background");
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
        public static readonly Str PresetDefault = new("settings.presetDefault", "默认", "Default");
        public static readonly Str PresetForest = new("settings.presetForest", "绿", "Green");
        public static readonly Str PresetGraphite = new("settings.presetGraphite", "灰", "Gray");
        public static readonly Str PresetMidnight = new("settings.presetMidnight", "蓝", "Blue");
        public static readonly Str PresetSand = new("settings.presetSand", "暖色", "Warm");
        public static readonly Str Preview = new("settings.preview", "试听", "Preview");
        public static readonly Str QuickEnoughToMove = new(
            "settings.quickEnoughToMove",
            "快速：够移一下鼠标",
            "Quick: enough to move the mouse");
        public static readonly Str QuotedName = new("settings.quotedName", "「{0}」", "“{0}”");
        public static readonly Str StartRecordingAfterSelecting = new("settings.startRecordingAfterSelecting", "录制前确认", "Confirm before recording");
        public static readonly Str StartRecordingAfterSelectingTip = new("settings.startRecordingAfterSelectingTip", "选区后显示准备面板；关闭后直接录制", "Show a ready panel after selection; turn off to record immediately.");
        public static readonly Str RecordAtFullMonitorRefresh = new("settings.recordAtFullMonitorRefresh", "跟随刷新率", "Match refresh rate");
        public static readonly Str RecordingFormat = new("settings.recordingFormat", "录制格式", "Recording format");
        public static readonly Str RecordingFrameRate = new(
            "settings.recordingFrameRate",
            "录制帧率",
            "Recording frame rate");
        public static readonly Str RecordWindowOnly = new("settings.recordWindowOnly", "只录窗口", "Record window only");
        public static readonly Str RememberTheLastToolColor = new("settings.rememberTheLastToolColor", "记住工具样式", "Remember tool settings");
        public static readonly Str RunAsAdministratorTip = new("settings.runAsAdministratorTip", "显示当前权限；切换后重启应用", "Shows current privileges; switching restarts the app.");
        public static readonly Str AdministratorChanging = new("settings.administratorChanging", "正在切换权限…", "Switching privileges…");
        public static readonly Str AdministratorChangeFailed = new("settings.administratorChangeFailed", "权限切换失败，请重试", "Could not switch privileges. Try again.");
        public static readonly Str RightClickCancelsPicker = new("settings.rightClickCancelsPicker", "右键关闭选区", "Right-click closes the picker");
        public static readonly Str RightClickCancelsPickerTip = new("settings.rightClickCancelsPickerTip", "右键取消选区，不执行捕获", "Right-click to cancel selection without capturing.");
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
        public static readonly Str SecondsUnit = new("settings.secondsUnit", "秒", "s");
        public static readonly Str SaveFolderAutoSavedCopies = new("settings.saveFolderAutoSavedCopies", "副本与导出的默认文件夹", "Default folder for copies and exports");
        public static readonly Str SelectionMagnifier = new(
            "settings.selectionMagnifier",
            "选区放大镜",
            "Selection magnifier");
        public static readonly Str Semicolon = new("settings.semicolon", "；", "; ");
        public static readonly Str SharesAKeyWith = new(
            "settings.sharesAKeyWith",
            "与{0}同键 {1}",
            "Shares a key with {0} — {1}");
        public static readonly Str ShowTheLastSelection = new(
            "settings.showTheLastSelection",
            "显示上次选区",
            "Show the last selection");
        public static readonly Str ShowTheRegionBorderWhile = new("settings.showTheRegionBorderWhile", "录制范围边框", "Recording outline");
        public static readonly Str ShowTheResultWindowAfter = new("settings.showTheResultWindowAfter", "OCR 选字预览", "OCR text selection");
        /// <summary>「可调整选区」标题（默认关闭）。</summary>
        public static readonly Str EditableSelection = new("settings.editableSelection", "可调整选区", "Adjustable selection");

        /// <summary>说明：松手后的框能拖边角改大小、可整块移动，确认才出图。</summary>
        public static readonly Str EditableSelectionDesc = new("settings.editableSelectionDesc", "松手后可调整位置与大小，确认后捕获", "Adjust position and size after dragging, then confirm to capture.");


        /// <summary>键位行的说明：什么时候生效。</summary>

        /// <summary>上面那一行键位的长说明，放 ToolTip，不占行内宽度。</summary>
        public static readonly Str ConfirmCancelKeysTip = new("settings.confirmCancelKeysTip", "用于选区确认与取消", "Confirm or cancel selection");
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
        public static readonly Str TokenChip = new("settings.tokenChip", "芯片底色", "Chip");
        public static readonly Str TokenChromeBg = new("settings.tokenChromeBg", "标题栏底色", "Title bar");
        public static readonly Str TokenDanger = new("settings.tokenDanger", "提醒", "Danger");
        public static readonly Str TokenHover = new("settings.tokenHover", "悬停叠加", "Hover overlay");
        public static readonly Str TokenKeycap = new("settings.tokenKeycap", "键帽底色", "Keycap");
        public static readonly Str TokenNavBg = new("settings.tokenNavBg", "侧栏底色", "Sidebar");
        public static readonly Str TokenPanelBg = new("settings.tokenPanelBg", "面板底色", "Panel");
        public static readonly Str TokenPress = new("settings.tokenPress", "按下叠加", "Pressed overlay");
        public static readonly Str TokenRowBg = new("settings.tokenRowBg", "行底色", "Row");
        public static readonly Str TokenRowHover = new("settings.tokenRowHover", "行悬停", "Row hover");
        public static readonly Str TokenStroke = new("settings.tokenStroke", "标准描边", "Stroke");
        public static readonly Str TokenStrokeField = new("settings.tokenStrokeField", "控件描边", "Control stroke");
        public static readonly Str TokenStrokeSoft = new("settings.tokenStrokeSoft", "细描边", "Soft stroke");
        public static readonly Str TokenSuccess = new("settings.tokenSuccess", "成功", "Success");
        public static readonly Str SnapLastRegion = new("settings.snapLastRegion", "吸附到上次选区", "Snap to last selection");
        public static readonly Str SnapLastRegionHint = new("settings.snapLastRegionHint", "悬停或点击上次选区时复用该区域", "Hover over or click the previous selection to reuse it");
        public static readonly Str TokenText = new("settings.tokenText", "正文", "Body text");
        public static readonly Str TokenTextDim = new("settings.tokenTextDim", "次要文字", "Secondary text");
        public static readonly Str TokenTrack = new("settings.tokenTrack", "轨道", "Track");
        public static readonly Str TokenWarning = new("settings.tokenWarning", "注意", "Warning");
        public static readonly Str TokenWindowBg = new("settings.tokenWindowBg", "窗口底色", "Window");
        public static readonly Str WhenOnTheEditorTheRecord = new("settings.whenOnTheEditorTheRecord", "捕获软件界面，范围边框除外", "Include the app UI, excluding capture outlines.");
        public static readonly Str CopyKey = new(
            "settings.copyKey",
            "截图后的复制键",
            "Key to copy a shot");
        public static readonly Str TheToastAlsoAccepts = new("settings.theToastAlsoAccepts", "浮窗显示时按此键复制截图", "Copy the screenshot while its toast is visible.");
        public static readonly Str TheReadyCardLists = new("settings.theReadyCardLists", "就绪卡显示截图与录制快捷键", "Show capture and recording shortcuts on the ready card.");
        public static readonly Str StartupNotification = new(
            "settings.startupNotification",
            "启动气泡提示",
            "Startup notification");
        public static readonly Str System = new("settings.system", "系统", "System");
        public static readonly Str System2 = new("settings.system2", "系统默认", "System");
        public static readonly Str TheCursorAppears = new("settings.theCursorAppears", "将鼠标指针包含在录制中", "Include the cursor in recordings.");
        public static readonly Str TheFormatTheEditorS = new("settings.theFormatTheEditorS", "录制默认导出格式；MP4/WebM 需要视频组件", "Default recording export format; MP4/WebM require the video component.");
        public static readonly Str TheInstantTheSelectionHotkey = new("settings.theInstantTheSelectionHotkey", "选区和放大镜使用打开时的定格画面；关闭后实时捕获", "Use the opening frame for selection and magnification; turn off for live capture.");
        public static readonly Str Theme = new("settings.theme", "主题", "Theme");
        public static readonly Str TheMomentACaptureFinishes = new("settings.theMomentACaptureFinishes", "截图后边框停留多久再淡出", "How long the screenshot outline stays before fading.");
        public static readonly Str TheMostCompatibleVideoFormat = new("settings.theMostCompatibleVideoFormat", "H.264 视频，兼容常见播放器与平台", "H.264 video, compatible with common players and platforms");
        public static readonly Str TheRecordingFrameRateThen = new("settings.theRecordingFrameRateThen", "按录制显示器的刷新率自动设置帧率", "Set the frame rate from the recording display refresh rate.");
        public static readonly Str TheRecordingFrameRateThen2 = new("settings.theRecordingFrameRateThen2", "主屏 {0} Hz，录制 {1} fps；其他屏幕自动检测", "Primary display: {0} Hz, {1} fps. Other displays are detected automatically.");
        public static readonly Str TheSoundThatConfirms = new("settings.theSoundThatConfirms", "截图完成时播放。自定义支持 OGG/WAV，播放失败时使用内置音", "Play after a screenshot. Custom OGG/WAV falls back to the built-in sound if playback fails.");
        public static readonly Str TheTextFormThePicker = new("settings.theTextFormThePicker", "放大镜复制颜色时使用的格式", "Format used when copying a color from the magnifier");
        public static readonly Str ToastLifetime = new("settings.toastLifetime", "悬浮窗存在时间", "Toast lifetime");
        public static readonly Str UsualEnoughToOpen = new(
            "settings.usualEnoughToOpen",
            "常用：够打开一个菜单",
            "Usual: enough to open a menu");
        public static readonly Str WhenBothFreezeTheScreen = new("settings.whenBothFreezeTheScreen", "定格时预抓可见窗口，点击后直接使用；0 表示实时抓取", "Cache visible windows when freezing; use 0 for live window capture.");
        public static readonly Str WhenYouClickAWindow = new("settings.whenYouClickAWindow", "录制窗口本体，不受遮挡或移动影响", "Record the window independently of overlap or movement.");
        public static readonly Str WhileYouDragOrHover = new("settings.whileYouDragOrHover", "显示像素、坐标与颜色；按 C 复制颜色", "Show pixels, coordinates, and color; press C to copy the color.");
        public static readonly Str Windows = new("settings.windows", "{0} 个", "{0} windows");
        public static readonly Str WindowsPreCapturedOnFreeze = new(
            "settings.windowsPreCapturedOnFreeze",
            "定格时预抓窗口数",
            "Windows pre-captured on freeze");
        public static readonly Str WithTheSwitchAbove = new("settings.withTheSwitchAbove", "保留窗口边缘的投影与透明渐变", "Keep window shadows and transparent edges.");
    }
}
