namespace NekoCapture.Core;

/// <summary>界面文案目录。</summary>
public static partial class K
{
    /// <summary>设置层级、入口说明与搜索词。</summary>
    public static class SettingsNavigation
    {
        public static readonly Str Back = new("settingsnavigation.back", "返回", "Back");
        public static readonly Str Search = new("settingsnavigation.search", "搜索设置", "Search settings");
        public static readonly Str ClearSearch = new("settingsnavigation.clearSearch", "清空搜索", "Clear search");
        public static readonly Str NoResults = new("settingsnavigation.noResults", "没有找到相关设置", "No matching settings");
        public static readonly Str TryAnother = new("settingsnavigation.tryAnother", "试试功能名称或换一个说法", "Try a feature name or another term");
        public static readonly Str MyHotkeys = new("settingsnavigation.myHotkeys", "我的触发", "My triggers");
        public static readonly Str BindingName = new("settingsnavigation.bindingName", "名称", "Name");
        public static readonly Str KeysAndTrigger = new("settingsnavigation.keysAndTrigger", "触发设置", "Trigger settings");
        public static readonly Str More = new("settingsnavigation.more", "更多设置", "More settings");
        public static readonly Str Presets = new("settingsnavigation.presets", "预设方案", "Presets");
        public static readonly Str PresetsHint = new("settingsnavigation.presetsHint", "触发入口与行为的预设方案", "Ready-made triggers and behaviors");
        public static readonly Str UiKeys = new("settingsnavigation.uiKeys", "界面快捷键", "Interface shortcuts");
        public static readonly Str UiKeysHint = new("settingsnavigation.uiKeysHint", "确认、取消、复制与编辑器按键", "Confirm, cancel, copy, and editor keys");
        public static readonly Str SharedHint = new("settingsnavigation.sharedHint", "行为入口与动作链", "Reusable behaviors and action chains");
        public static readonly Str Execute = new("settingsnavigation.execute", "执行行为", "Run behavior");
        public static readonly Str ExecuteHint = new("settingsnavigation.executeHint", "操作顺序与事件分支", "Action order and event branches");
        public static readonly Str Selection = new("settingsnavigation.selection", "选区与吸附", "Selection and snapping");
        public static readonly Str SelectionHint = new("settingsnavigation.selectionHint", "控件吸附、停留时间与选区辅助", "Control snapping, dwell time, and selection aids");
        public static readonly Str SearchPointer = new("settingsnavigation.searchPointer", "方向键 鼠标 光标 微调 像素", "arrows mouse cursor pointer nudge fine pixel");
        public static readonly Str Feedback = new("settingsnavigation.feedback", "截图反馈", "Capture feedback");
        public static readonly Str FeedbackHint = new("settingsnavigation.feedbackHint", "边框、提示与提示音", "Outlines, notifications, and sounds");
        public static readonly Str Editing = new("settingsnavigation.editing", "编辑与标注", "Editing and annotation");
        public static readonly Str EditingHint = new("settingsnavigation.editingHint", "工具偏好与标注设置", "Tool preferences and annotation settings");
        public static readonly Str OcrHint = new("settingsnavigation.ocrHint", "识别引擎、模型与识别结果", "Recognition engines, models, and results");
        public static readonly Str Recognition = new("settingsnavigation.recognition", "文字识别", "Text recognition");
        public static readonly Str PinHint = new("settingsnavigation.pinHint", "窗口吸附与贴图设置", "Snapping and pinned image preferences");
        public static readonly Str Files = new("settingsnavigation.files", "保存与文件", "Saving and files");
        public static readonly Str FilesHint = new("settingsnavigation.filesHint", "保存方式与保存位置", "Saving preferences and file locations");
        public static readonly Str SharedFiles = new("settingsnavigation.sharedFiles", "以下设置同时用于截图与录制", "These settings apply to both screenshots and recordings");
        public static readonly Str RecordStartHint = new("settingsnavigation.recordStartHint", "开始方式与确认操作", "Start behavior and confirmation");
        public static readonly Str Quality = new("settingsnavigation.quality", "画面与帧率", "Picture and frame rate");
        public static readonly Str QualityHint = new("settingsnavigation.qualityHint", "帧率、刷新率与窗口捕获", "Frame rate, refresh rate, and window capture");
        public static readonly Str Export = new("settingsnavigation.export", "格式与导出", "Format and export");
        public static readonly Str ExportHint = new("settingsnavigation.exportHint", "录制格式与视频组件", "Recording formats and video components");
        public static readonly Str RecordFeedback = new("settingsnavigation.recordFeedback", "录制反馈", "Recording feedback");
        public static readonly Str RecordFeedbackHint = new("settingsnavigation.recordFeedbackHint", "录制边框与鼠标光标", "Recording outlines and mouse pointers");
        public static readonly Str Theme = new("settingsnavigation.theme", "主题与语言", "Theme and language");
        public static readonly Str ThemeHint = new("settingsnavigation.themeHint", "界面主题与显示语言", "Appearance mode and display language");
        public static readonly Str PaletteHint = new("settingsnavigation.paletteHint", "配色预设、强调色与自定义颜色", "Color presets, accent, and custom colors");
        public static readonly Str Colors = new("settingsnavigation.colors", "自定义颜色", "Custom colors");
        public static readonly Str ColorsHint = new("settingsnavigation.colorsHint", "窗口、文字、交互与语义颜色", "Window, text, interaction, and semantic colors");
        public static readonly Str Startup = new("settingsnavigation.startup", "启动与权限", "Startup and permissions");
        public static readonly Str StartupHint = new("settingsnavigation.startupHint", "开机自启与管理员权限", "Start with Windows and administrator access");
        public static readonly Str WelcomeHint = new("settingsnavigation.welcomeHint", "启动通知与快捷键提示", "Startup notifications and shortcut hints");
        public static readonly Str UpdatesHint = new("settingsnavigation.updatesHint", "版本检查与更新偏好", "Version checks and update preferences");
        public static readonly Str Project = new("settingsnavigation.project", "项目与许可", "Project and license");
        public static readonly Str ProjectHint = new("settingsnavigation.projectHint", "源代码与开源许可", "Source code and open-source license");
        public static readonly Str SearchKeys = new("settingsnavigation.searchKeys", "快捷键 热键 绑键 按键 键位", "shortcut hotkey keybind binding keys");
        public static readonly Str SearchSnap = new("settingsnavigation.searchSnap", "吸附 磁吸 贴边 对齐", "snap snapping align alignment");
        public static readonly Str SearchSound = new("settingsnavigation.searchSound", "声音 音效 静音 提示音 快门声", "sound audio mute shutter notification");
        public static readonly Str SearchAdmin = new("settingsnavigation.searchAdmin", "权限 提权 管理员 管理员权限", "admin administrator elevation permission uac");
        public static readonly Str SearchStartup = new("settingsnavigation.searchStartup", "开机 自启 开机自启 随系统启动", "startup autostart login boot");
        public static readonly Str SearchOcr = new("settingsnavigation.searchOcr", "识字 识别文字 提取文字 文字提取 文字识别", "ocr text recognition recognize extract");
        public static readonly Str SearchModels = new("settingsnavigation.searchModels", "模型 离线识别 识别引擎", "model engine offline small medium");
        public static readonly Str SearchQuality = new("settingsnavigation.searchQuality", "流畅 流畅度 画质 质量 帧率 刷新率", "fps frame rate quality refresh smooth");
        public static readonly Str SearchCodec = new("settingsnavigation.searchCodec", "视频组件 编码器 编码 转码", "ffmpeg codec encoder encode transcode");
        public static readonly Str SearchFiles = new("settingsnavigation.searchFiles", "保存目录 保存位置 保存路径 文件夹 目录 路径", "folder directory path location save");
        public static readonly Str SearchTheme = new("settingsnavigation.searchTheme", "主题 深色 浅色 暗色 亮色 夜间模式", "theme dark light night appearance");
        public static readonly Str SearchPin = new("settingsnavigation.searchPin", "贴图 钉图 悬浮图片", "pin pinned floating image");
        public static readonly Str SearchBehavior = new("settingsnavigation.searchBehavior", "行为 动作链 流程 执行顺序 后续操作", "behavior action chain workflow sequence");
        public static readonly Str SearchDelay = new("settingsnavigation.searchDelay", "延迟 等待 停留 停顿", "delay dwell wait pause");
        public static readonly Str SearchVerbs = new("settingsnavigation.searchVerbs", "关闭 打开 修改 调整 不要 不想 禁用 启用 怎么 如何 关掉", "disable enable change adjust turnoff turnon how");
    }
}
