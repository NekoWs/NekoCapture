namespace NekoCapture.Core;

/// <summary>触发来源目录，设置与运行入口共用。</summary>
public static class TriggerSources
{
    /// <summary>键盘或鼠标组合键。</summary>
    public const string Keyboard = "keyboard";
    /// <summary>单击托盘图标。</summary>
    public const string TrayClick = "tray.click";
    /// <summary>双击托盘图标。</summary>
    public const string TrayDoubleClick = "tray.doubleClick";
    /// <summary>托盘右键菜单项。</summary>
    public const string TrayMenu = "tray.menu";
    /// <summary>可选来源的稳定顺序。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Keyboard, TrayClick, TrayDoubleClick, TrayMenu };
    /// <summary>读取来源的显示名称。</summary>
    /// <param name="source">来源标识。</param>
    /// <returns>本地化文案。</returns>
    public static Str Label(string source) => source switch
    {
        TrayClick => K.Triggers.TrayClick, TrayDoubleClick => K.Triggers.TrayDoubleClick,
        TrayMenu => K.Triggers.TrayMenu, _ => K.Triggers.Keyboard,
    };
}
