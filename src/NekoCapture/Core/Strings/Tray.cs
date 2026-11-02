namespace NekoCapture.Core;

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
        public static readonly Str Exit = new("tray.exit", "退出", "Exit");
        public static readonly Str NekoCaptureGestures = new(
            "tray.framecaptureGestures",
            "NekoCapture\n触发 {0} 项 · {1}",
            "NekoCapture\n{0} triggers · {1}");
        public static readonly Str NekoCapturePausedHotkeysReleased = new(
            "tray.framecapturePausedHotkeysReleased",
            "NekoCapture · 已暂停，热键让位其他软件",
            "NekoCapture · paused, hotkeys released to other apps");
        public static readonly Str Help = new("tray.help", "帮助", "Help");
        public static readonly Str NoImageOnTheClipboard = new(
            "tray.noImageOnTheClipboard",
            "剪贴板里没有图片——先复制一张截图、图片或网页图再试",
            "No image on the clipboard. Copy a screenshot, a picture, or a web image first.");
        public static readonly Str NoRegionDoubleClick = new(
            "tray.noRegionDoubleClick",
            "未保存选区",
            "No saved region");
        public static readonly Str PausedCaptureHotkeysAreReleased = new(
            "tray.pausedCaptureHotkeysAreReleased",
            "已暂停：截图热键暂时让位给其他软件，回到这里可恢复",
            "Paused: capture hotkeys are released to other apps. Come back here to resume.");
        public static readonly Str PinFromClipboard = new("tray.pinFromClipboard", "从剪贴板贴图", "Pin From Clipboard");
        public static readonly Str RegionFps = new(
            "tray.regionFps",
            "范围 {0}×{1} · {2}fps",
            "Region {0}×{1} · {2} fps");
        public static readonly Str ResumedCaptureHotkeysAreActive = new(
            "tray.resumedCaptureHotkeysAreActive",
            "已恢复：截图热键重新生效",
            "Resumed: capture hotkeys are active again");
    }
}
