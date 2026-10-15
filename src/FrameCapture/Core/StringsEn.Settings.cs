namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 设置窗分区：分页标题、行标签、选项名。
/// 行标签是名词短语，不带冒号与句号；解释性文字属于提示与文档，不进标签。
/// </summary>
static partial class StringsEn
{
    /// <summary>设置窗的译名。</summary>
    static readonly (string Zh, string En)[] Settings =
    {
        // 窗口标题、左侧分页导航与分页标题
        ("设置", "Settings"),
        ("快捷键", "Hotkeys"),
        ("截图", "Screenshot"),
        ("录制", "Recording"),
        ("产物", "Output"),
        ("编辑器", "Editor"),
        ("贴图", "Pins"),
        ("外观", "Appearance"),
        ("系统", "System"),

        // 外观分页
        ("界面语言", "Language"),
        ("系统默认", "System"),
        ("主题", "Theme"),
        ("外观主题", "Appearance theme"),
        ("跟随系统", "Follow system"),
        ("浅色", "Light"),
        ("深色", "Dark"),
        ("强调色", "Accent"),
        ("强调色：编辑器选中、开关、滑杆、进度都用它",
            "Accent color — editor selections, switches, sliders, and progress all use it"),
        ("点击自定义强调色，取色器与编辑器同款；右键恢复跟随系统",
            "Click to pick an accent color with the editor's own picker; right-click to follow the system"),
    };
}
