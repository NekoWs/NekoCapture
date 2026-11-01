namespace NekoCapture.Core;

/// <summary>
/// 绘图工具目录：工具 → 快捷键 / 帮助符号 / 图标字形 / 工具条提示 / 帮助说明 的**唯一数据源**。
/// 工具条、帮助窗、快捷键分发都从这里取，加新工具只改一处（SelfTest 锁死目录完整性，防漂移）。
/// Glyph 是图标字体字形（仅工具条用，PUA 码位，GlyphProbe 人工验证）；Sym 是帮助窗键位列的普通 Unicode 符号。
/// 两者不可混用：帮助窗用默认字体，塞 PUA 码位会出豆腐块（离屏渲染抓到，锁死 Sym 非 PUA）。
/// </summary>
/// <param name="Key">快捷键字母，也是工具的稳定标识。</param>
/// <param name="Sym">帮助窗键位列显示的普通 Unicode 符号，须非 PUA。</param>
/// <param name="Glyph">工具条图标字形的 PUA 码位（单个字符）。</param>
/// <param name="Tip">工具条悬停提示文案。</param>
/// <param name="Help">帮助窗里该工具的一行说明文案。</param>
/// <returns>不可变的目录条目；工具条、帮助窗、快捷键分发取到的是同一份值。</returns>
public readonly record struct ToolInfo(string Key, string Sym, string Glyph, Str Tip, Str Help);

/// <summary>全部绘图工具的静态目录，以及退出绘制态的键位与帮助小节标题。</summary>
public static class ToolCatalog
{
    /// <summary>绘制工具（顺序 = 工具条从左到右；浏览/退出键 X 单列在 ExitKey）。</summary>
    public static readonly ToolInfo[] Tools =
    {
        // Glyph 均为 GlyphProbe 渲染矩阵人工定稿：
        // 选中 E8B0 / 线 E949 / 圆 EA3F（线性环，与画出的描边椭圆同语言）/ 箭头 F0AF / 手绘 E70F
        // 文字 E8D2 / 马赛克 E80A / 裁剪 E7A8
        new("V", "V", "\uE8B0", K.Tools.SelectTip, K.Tools.SelectHelp),
        new("L", "─", "\uE949", K.Tools.LineTip, K.Tools.LineHelp),
        new("R", "□", "\uE739", K.Tools.RectTip, K.Tools.RectHelp),
        new("O", "○", "\uEA3F", K.Tools.OvalTip, K.Tools.OvalHelp),
        new("A", "↗", "\uF0AF", K.Tools.ArrowTip, K.Tools.ArrowHelp),
        new("P", "✎", "\uE70F", K.Tools.PenTip, K.Tools.PenHelp),
        new("H", "▨", "\uE7E6", K.Tools.HighlightTip, K.Tools.HighlightHelp),
        new("T", "T", "\uE8D2", K.Tools.TextTip, K.Tools.TextHelp),
        new("M", "▦", "\uE80A", K.Tools.MosaicTip, K.Tools.MosaicHelp),
        new("C", "✂", "\uE7A8", K.Editor.Crop2, K.Tools.CropHelp),
        new("N", "①", "\uE71D", K.Tools.NumberTip, K.Tools.NumberHelp),
    };

    /// <summary>退出绘制态的快捷键。</summary>
    public const string ExitKey = "X";

    /// <summary><see cref="ExitKey"/> 切回的状态名，设置页键位表与帮助窗用。</summary>
    public static readonly Str ExitName = K.Tools.BrowseOrDraw;
}
