namespace NekoCapture.Core;

/// <summary>托盘菜单可选图标。</summary>
/// <param name="Id">保存到配置的稳定标识。</param>
/// <param name="Glyph">Segoe Fluent Icons 字形。</param>
/// <param name="Label">图标名称。</param>
public sealed record TrayMenuIcon(string Id, string Glyph, Str Label);

/// <summary>托盘菜单与设置预览共享的图标目录。</summary>
public static class TrayMenuIcons
{
    /// <summary>按用途排列的可选图标。</summary>
    public static IReadOnlyList<TrayMenuIcon> All { get; } = Array.AsReadOnly(new[]
    {
        new TrayMenuIcon("capture", "\uE7A8", K.Common.Capture),
        new TrayMenuIcon("record", "\uE714", K.Common.Recording),
        new TrayMenuIcon("scroll", "\uE8CB", K.Common.ScrollingCapture),
        new TrayMenuIcon("delay", "\uE823", K.Common.DelayedCapture),
        new TrayMenuIcon("pin", "\uE718", K.Common.Pin),
        new TrayMenuIcon("copy", "\uE8C8", K.Common.Copy),
        new TrayMenuIcon("edit", "\uE70F", K.Common.Edit),
        new TrayMenuIcon("save", "\uE74E", K.Common.Save),
        new TrayMenuIcon("folder", "\uE838", K.Triggers.FolderIcon),
        new TrayMenuIcon("star", "\uE734", K.Triggers.StarIcon),
        new TrayMenuIcon("settings", "\uE713", K.Common.Settings),
    });

    /// <summary>选择用户指定的图标；空值或未知标识按行为自动选择。</summary>
    /// <param name="config">当前共享行为配置。</param>
    /// <param name="binding">托盘菜单配置。</param>
    /// <returns>菜单与预览使用的字形。</returns>
    public static string Glyph(GestureConfig config, GestureBindingDto binding)
    {
        if (All.FirstOrDefault(icon => icon.Id == binding.MenuIcon) is { } selected) return selected.Glyph;
        var actions = config.FlowActions(binding.EntryId).ToHashSet();
        return actions.Contains(GestureActions.ScrollCapture) ? "\uE8CB"
            : actions.Contains(GestureActions.DelayedCapture) ? "\uE823"
            : actions.Contains(GestureActions.ReadClipboardImage) ? "\uE718"
            : actions.Contains(GestureActions.Select) ? "\uE7A8"
            : actions.Contains(GestureActions.RecordStart) || actions.Contains(GestureActions.RecordToggle) ? "\uE714"
            : actions.Contains(GestureActions.PinSelection) ? "\uE718"
            : actions.Contains(GestureActions.SaveResult) ? "\uE74E"
            : actions.Contains(GestureActions.CopyLast) ? "\uE8C8"
            : actions.Contains(GestureActions.EditResult) || actions.Contains(GestureActions.EditLastVideo) ? "\uE70F" : "\uE7A8";
    }
}
