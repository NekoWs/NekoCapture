namespace FrameCapture.Core;

/// <summary>
/// 绘图工具目录（R48）：工具 → 快捷键 / 字形 / 工具条提示 / 帮助说明 的**唯一数据源**。
/// 工具条、帮助窗、快捷键分发都从这里取，加新工具只改一处——
/// 此前工具定义散在工具条数组、帮助窗、引导卡三处，加马赛克时就漏了帮助窗（SelfTest T92 锁死防漂移）。
/// 字形只用 GlyphProbe 渲染矩阵人工验证过的码位。
/// </summary>
public readonly record struct ToolInfo(string Key, string Glyph, string Tip, string Help);

public static class ToolCatalog
{
    /// <summary>绘制工具（顺序 = 工具条从左到右；浏览/退出键 X 单列在 ExitKey）。</summary>
    public static readonly ToolInfo[] Tools =
    {
        new("L", "", "直线 (L)", "拖出直线；再按 L 或点同一图标退出绘制"),
        new("O", "", "圆圈 (O)", "拖出椭圆/圆（按住拖成正圆靠比例接近即可）"),
        new("A", "", "箭头 (A)", "拖出箭头，箭头头部自动朝向终点"),
        new("P", "", "手绘 (P)", "按住拖动自由划线，松手成笔"),
        new("T", "", "文字 (T)", "点画布就地输入，Enter 上屏 / Esc 取消；字号随线宽挡位"),
        new("M", "", "马赛克 (M)", "拖框实时块状化，松手应用；线宽挡位 = 块粒度（细 6 / 中 12 / 粗 20 px @1080p）"),
        new("C", "", "裁剪 (C)", "拖框时框外压暗 + 三分网格，松开即裁；只改视图不毁数据，Ctrl+Z 可撤销"),
    };

    /// <summary>退出绘制态的快捷键。</summary>
    public const string ExitKey = "X";

    /// <summary>帮助窗「绘图」小节标题（与目录同处维护，避免文案两头跑）。</summary>
    public static string HelpHeading => $"编辑器 · 绘图（再点当前工具 或 {ExitKey} = 退出绘制）";
}
