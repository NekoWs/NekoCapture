namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 常驻外壳分区：托盘菜单与提示、截屏后的浮窗、滚动截图的小窗。
/// 这一区的文案都出现在系统界面旁边，长度按"一眼读完"控制，不写整句解释。
/// </summary>
static partial class StringsEn
{
    /// <summary>托盘、浮窗与小窗的译名。</summary>
    static readonly (string Zh, string En)[] Shell =
    {
        // 托盘菜单：菜单项每次右键重建，语言切换下一次打开就是新词
        ("设置捕获范围", "Set Capture Region"),
        ("清除捕获范围", "Clear Capture Region"),
        ("立即捕获", "Capture Now"),
        ("编辑上次截图", "Edit Last Screenshot"),
        ("滚动截图", "Scrolling Capture"),
        ("延时截图", "Delayed Capture"),
        ("从剪贴板贴图", "Pin From Clipboard"),
        ("剪贴板里没有图片——先复制一张截图、图片或网页图再试",
            "No image on the clipboard. Copy a screenshot, a picture, or a web image first."),
        ("打开保存文件夹", "Open Save Folder"),
        ("打开保存文件夹失败", "Could not open the save folder"),
        ("设置…", "Settings…"),
        ("帮助", "Help"),
        ("开机自启", "Start With Windows"),
        ("暂停全局热键", "Pause Global Hotkeys"),
        ("管理员权限运行", "Running As Administrator"),
        ("以管理员身份运行", "Run As Administrator"),
        ("退出", "Exit"),

        // 托盘图标悬停提示：NotifyIcon.Text 上限 63 字符，英文更要短
        ("FrameCapture · 已暂停，热键让位其他软件", "FrameCapture · paused, hotkeys released to other apps"),
        ("范围 {0}×{1} · {2}fps", "Region {0}×{1} · {2} fps"),
        ("未设范围 · 双击图标设置", "No region · double-click the icon to set one"),
        ("FrameCapture\n手势 {0} 条 · {1}", "FrameCapture\n{0} gestures · {1}"),
        ("已暂停：截图热键暂时让位给其他软件，回到这里可恢复",
            "Paused: capture hotkeys are released to other apps. Come back here to resume."),
        ("已恢复：截图热键重新生效", "Resumed: capture hotkeys are active again"),

        // 截屏后的浮窗
        ("已复制到剪贴板", "Copied to clipboard"),
        ("已截图", "Screenshot taken"),
        ("点击或按 Enter 继续标注 · Esc 关闭", "Click or press Enter to annotate · Esc to dismiss"),
        ("关闭", "Close"),

        // 滚动截图小窗
        ("滚动目标窗口，自动拼接 · 点「完成」结束", "Scroll the target window to stitch automatically · press Done to finish"),
        ("自动滚动中 · 到底自动停止", "Auto-scrolling · stops at the bottom"),
        ("已拼接 1 段", "1 segment stitched"),
        ("已拼接 {0} 段 · 高 {1} px", "{0} segments stitched · {1} px tall"),
        ("完成", "Done"),
        ("取消", "Cancel"),
        ("自动滚动", "Auto-Scroll"),
        ("停止自动滚动", "Stop Auto-Scroll"),
    };
}
