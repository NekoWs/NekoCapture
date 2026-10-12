namespace FrameCapture.Core;

/// <summary>
/// 绘图工具目录（R48）：工具 → 快捷键 / 帮助符号 / 图标字形 / 工具条提示 / 帮助说明 的**唯一数据源**。
/// 工具条、帮助窗、快捷键分发都从这里取，加新工具只改一处——
/// 此前工具定义散在工具条数组、帮助窗、引导卡三处，加马赛克时就漏了帮助窗（SelfTest T92 锁死防漂移）。
/// Glyph 是图标字体字形（仅工具条用，PUA 码位，GlyphProbe 人工验证）；Sym 是帮助窗键位列的普通 Unicode 符号。
/// 两者不可混用：帮助窗用默认字体，塞 PUA 码位会出豆腐块（R49 离屏渲染抓到，T92f 锁死 Sym 非 PUA）。
/// </summary>
public readonly record struct ToolInfo(string Key, string Sym, string Glyph, string Tip, string Help);

public static class ToolCatalog
{
    /// <summary>绘制工具（顺序 = 工具条从左到右；浏览/退出键 X 单列在 ExitKey）。</summary>
    public static readonly ToolInfo[] Tools =
    {
        // Glyph 均为 GlyphProbe 渲染矩阵人工定稿：选中 E8B0 / 线 E949 / 圆 EA3F（线性环，与画出的描边椭圆同语言；EA3B 是实心盘已弃用）/ 箭头 F0AF / 手绘 E70F / 文字 E8D2 / 马赛克 E80A / 裁剪 E7A8
        new("V", "V", "\uE8B0", "选中 (V)", "点击标注=选中（虚线框）；拖动=移动；Delete=删除；Esc 先取消选中。移动/删除都可 Ctrl+Z 撤销"),
        new("L", "─", "\uE949", "直线 (L)", "拖出直线；按住 Shift 拖=吸附到 0/45/90°；再按 L 或点同一图标退出绘制"),
        new("R", "□", "\uE739", "矩形 (R)", "拖出矩形框，圈住/框选目标区域（最常用的强调框）；按住 Shift 拖=正方形；再按 R 或点同一图标退出绘制"),
        new("O", "○", "\uEA3F", "圆圈 (O)", "拖出椭圆/圆（框住目标区域）；按住 Shift 拖=正圆；再按 O 或点同一图标退出绘制"),
        new("A", "↗", "\uF0AF", "箭头 (A)", "拖出箭头，头部自动朝向终点；按住 Shift 拖=吸附到 0/45/90°"),
        new("P", "✎", "\uE70F", "手绘 (P)", "按住拖动自由划线，松手成笔"),
        new("H", "▨", "\uE7E6", "荧光笔 (H)", "拖出半透明马克笔高亮，盖在文字/区域上不遮字；线宽挡位 = 笔宽（比手绘粗约 2.5 倍）"),
        new("T", "T", "\uE8D2", "文字 (T)", "点画布就地输入，Enter 上屏 / Esc 取消；点已有文字可再编辑；字号随线宽挡位"),
        new("M", "▦", "\uE80A", "马赛克 (M)", "拖框实时块状化，松手应用；线宽挡位 = 块粒度（细 6 / 中 12 / 粗 20 px @1080p）"),
        new("C", "✂", "\uE7A8", "裁剪 (C)", "拖框时框外压暗 + 三分网格，松开即裁；只改视图不毁数据，Ctrl+Z 可撤销"),
        new("N", "①", "\uE71D", "序号 (N)", "点击放置递增编号圆标（1、2、3…），做步骤/教程标注；颜色随调色板、大小随线宽挡位；删除后编号自动回退不重号"),
    };

    /// <summary>退出绘制态的快捷键。</summary>
    public const string ExitKey = "X";

    /// <summary>帮助窗「绘图」小节标题（与目录同处维护，避免文案两头跑）。</summary>
    public static string HelpHeading => $"编辑器 · 绘图（再点当前工具 或 {ExitKey} = 退出绘制）";
}
