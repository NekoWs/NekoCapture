namespace FrameCapture.Core;

/// <summary>
/// 文案 · 托盘与进程入口。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>托盘与进程入口的文案。</summary>
    public static partial class Tray
    {
        public static readonly Str CaptureNow = new("tray.captureNow", "立即捕获", "Capture Now");
        public static readonly Str ClearCaptureRegion = new(
            "tray.clearCaptureRegion",
            "清除捕获范围",
            "Clear Capture Region");
        public static readonly Str ContinueTheLastScroll = new(
            "tray.continueTheLastScroll",
            "继续上次滚动",
            "Continue the Last Scroll");
        public static readonly Str CouldNotOpenTheSave = new(
            "tray.couldNotOpenTheSave",
            "打开保存文件夹失败",
            "Could not open the save folder");
        public static readonly Str EditLastScreenshot = new(
            "tray.editLastScreenshot",
            "编辑上次截图",
            "Edit Last Screenshot");
        public static readonly Str Exit = new("tray.exit", "退出", "Exit");
        public static readonly Str FramecaptureGestures = new(
            "tray.framecaptureGestures",
            "FrameCapture\n手势 {0} 条 · {1}",
            "FrameCapture\n{0} gestures · {1}");
        public static readonly Str FramecapturePausedHotkeysReleased = new(
            "tray.framecapturePausedHotkeysReleased",
            "FrameCapture · 已暂停，热键让位其他软件",
            "FrameCapture · paused, hotkeys released to other apps");
        public static readonly Str Help = new("tray.help", "帮助", "Help");
        public static readonly Str ShowKeyHints = new(
            "tray.showKeyHints",
            "查看快捷键提示",
            "Show key hints");
        public static readonly Str NoImageOnTheClipboard = new(
            "tray.noImageOnTheClipboard",
            "剪贴板里没有图片——先复制一张截图、图片或网页图再试",
            "No image on the clipboard. Copy a screenshot, a picture, or a web image first.");
        public static readonly Str NoRegionDoubleClick = new(
            "tray.noRegionDoubleClick",
            "未设范围 · 双击图标设置",
            "No region · double-click the icon to set one");
        public static readonly Str OpenSaveFolder = new("tray.openSaveFolder", "打开保存文件夹", "Open Save Folder");
        public static readonly Str PausedCaptureHotkeysAreReleased = new(
            "tray.pausedCaptureHotkeysAreReleased",
            "已暂停：截图热键暂时让位给其他软件，回到这里可恢复",
            "Paused: capture hotkeys are released to other apps. Come back here to resume.");
        public static readonly Str PauseGlobalHotkeys = new(
            "tray.pauseGlobalHotkeys",
            "暂停全局热键",
            "Pause Global Hotkeys");
        public static readonly Str PinFromClipboard = new("tray.pinFromClipboard", "从剪贴板贴图", "Pin From Clipboard");
        public static readonly Str RegionFps = new(
            "tray.regionFps",
            "范围 {0}×{1} · {2}fps",
            "Region {0}×{1} · {2} fps");
        public static readonly Str ResumedCaptureHotkeysAreActive = new(
            "tray.resumedCaptureHotkeysAreActive",
            "已恢复：截图热键重新生效",
            "Resumed: capture hotkeys are active again");
        public static readonly Str SetCaptureRegion = new("tray.setCaptureRegion", "设置捕获范围", "Set Capture Region");
        public static readonly Str Settings = new("tray.settings", "设置…", "Settings…");
    }
}
