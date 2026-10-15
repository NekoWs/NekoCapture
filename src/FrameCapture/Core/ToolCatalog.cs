namespace FrameCapture.Core;

/// <summary>
/// 绘图工具目录：工具 → 快捷键 / 帮助符号 / 图标字形 / 工具条提示 / 帮助说明 的**唯一数据源**。
/// 工具条、帮助窗、快捷键分发都从这里取，加新工具只改一处（SelfTest 锁死目录完整性，防漂移）。
/// Glyph 是图标字体字形（仅工具条用，PUA 码位，GlyphProbe 人工验证）；Sym 是帮助窗键位列的普通 Unicode 符号。
/// 两者不可混用：帮助窗用默认字体，塞 PUA 码位会出豆腐块（离屏渲染抓到，锁死 Sym 非 PUA）。
/// </summary>
/// <param name="Key">快捷键字母，也是工具的稳定标识。</param>
/// <param name="Sym">帮助窗键位列显示的普通 Unicode 符号，须非 PUA。</param>
/// <param name="Glyph">工具条图标字形的 PUA 码位（单个字符）。</param>
/// <param name="Tip">工具条悬停提示。</param>
/// <param name="Help">帮助窗里该工具的一行说明。</param>
/// <returns>不可变的目录条目；工具条、帮助窗、快捷键分发取到的是同一份值。</returns>
public readonly record struct ToolInfo(string Key, string Sym, string Glyph, string Tip, string Help);

/// <summary>全部绘图工具的静态目录，以及退出绘制态的键位与帮助小节标题。</summary>
public static class ToolCatalog
{
    /// <summary>绘制工具（顺序 = 工具条从左到右；浏览/退出键 X 单列在 ExitKey）。</summary>
    public static readonly ToolInfo[] Tools =
    {
        // Glyph 均为 GlyphProbe 渲染矩阵人工定稿：
        // 选中 E8B0 / 线 E949 / 圆 EA3F（线性环，与画出的描边椭圆同语言）/ 箭头 F0AF / 手绘 E70F
        // 文字 E8D2 / 马赛克 E80A / 裁剪 E7A8
        new("V", "V", "\uE8B0", "选中", "点击标注=选中，虚线框标出；拖动=移动；Delete=删除；Esc 先取消选中。移动和删除都可 Ctrl+Z 撤销"), // lockeys
        new("L", "─", "\uE949", "直线", "拖出直线；按住 Shift 拖=吸附到 0/45/90°；再按 L 或点同一图标退出绘制"), // lockeys
        new("R", "□", "\uE739", "矩形", "拖出矩形框圈住目标区域，最常用的强调框；按住 Shift 拖=正方形；再按 R 或点同一图标退出绘制"), // lockeys
        new("O", "○", "\uEA3F", "圆圈", "拖出椭圆或圆框住目标区域；按住 Shift 拖=正圆；再按 O 或点同一图标退出绘制"), // lockeys
        new("A", "↗", "\uF0AF", "箭头", "拖出箭头，头部自动朝向终点；按住 Shift 拖=吸附到 0/45/90°"), // lockeys
        new("P", "✎", "\uE70F", "手绘", "按住拖动自由划线，松手成笔"), // lockeys
        new("H", "▨", "\uE7E6", "荧光笔", "拖出半透明马克笔高亮，盖在文字/区域上不遮字；线宽挡位 = 笔宽，比手绘粗约 2.5 倍"), // lockeys
        new("T", "T", "\uE8D2", "文字", "点画布就地输入，Enter 上屏 / Esc 取消；点已有文字可再编辑；字号随线宽挡位"), // lockeys
        new("M", "▦", "\uE80A", "马赛克", "拖框实时块状化，松手应用；线宽挡位 = 块粒度，细 6 / 中 12 / 粗 20 px @1080p"), // lockeys
        new("C", "✂", "\uE7A8", "裁剪", "拖框时框外压暗 + 三分网格，松开即裁；只改视图不毁数据，Ctrl+Z 可撤销"), // lockeys
        new("N", "①", "\uE71D", "序号", "点击放置递增编号圆标 1、2、3…，做步骤和教程标注；颜色随调色板、大小随线宽挡位；删除后编号自动回退不重号"), // lockeys
    };

    /// <summary>退出绘制态的快捷键。</summary>
    public const string ExitKey = "X";

    /// <summary><see cref="ExitKey"/> 切回的状态名，设置页键位表与帮助窗用。</summary>
    public const string ExitName = "浏览 / 退出绘制"; // lockeys
}
