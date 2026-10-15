namespace FrameCapture.Core;

/// <summary>
/// 文案 · 绘图工具的悬停提示与帮助行。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，目录与取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>绘图工具的悬停提示与帮助行。</summary>
    public static partial class Tools
    {
        public static readonly Str ArrowHelp = new("tools.arrowHelp", "拖出箭头，头部自动朝向终点；按住 Shift 拖=吸附到 0/45/90°", "Drag an arrow, its head points at the end point on its own; hold Shift to snap to 0/45/90°");
        public static readonly Str ArrowTip = new("tools.arrowTip", "箭头", "Arrow");
        public static readonly Str BrowseOrDraw = new("tools.browseOrDraw", "浏览 / 退出绘制", "Browse / stop drawing");
        public static readonly Str CropHelp = new("tools.cropHelp", "拖框时框外压暗 + 三分网格，松开即裁；只改视图不毁数据，Ctrl+Z 可撤销", "While you drag, the outside dims over a rule-of-thirds grid; release to crop. Only the view changes, the pixels stay — Ctrl+Z undoes it");
        public static readonly Str HighlightHelp = new("tools.highlightHelp", "拖出半透明马克笔高亮，盖在文字/区域上不遮字；线宽挡位 = 笔宽，比手绘粗约 2.5 倍", "Drag a translucent highlighter over text or an area without hiding the words; the width step sets the pen width, about 2.5× thicker than freehand");
        public static readonly Str HighlightTip = new("tools.highlightTip", "荧光笔", "Highlighter");
        public static readonly Str LineHelp = new("tools.lineHelp", "拖出直线；按住 Shift 拖=吸附到 0/45/90°；再按 L 或点同一图标退出绘制", "Drag out a line; hold Shift to snap to 0/45/90°; press L again or click the same icon to leave drawing");
        public static readonly Str LineTip = new("tools.lineTip", "直线", "Line");
        public static readonly Str MosaicHelp = new("tools.mosaicHelp", "拖框实时块状化，松手应用；线宽挡位 = 块粒度，细 6 / 中 12 / 粗 20 px @1080p", "Drag a box to pixelate live, release to apply; the width step sets the block size, Thin 6 / Medium 12 / Bold 20 px @1080p");
        public static readonly Str MosaicTip = new("tools.mosaicTip", "马赛克", "Mosaic");
        public static readonly Str NumberHelp = new("tools.numberHelp", "点击放置递增编号圆标 1、2、3…，做步骤和教程标注；颜色随调色板、大小随线宽挡位；删除后编号自动回退不重号", "Click to place numbered badges 1, 2, 3… for steps and tutorials; the color comes from the palette and the size from the width step; deleting one renumbers the rest");
        public static readonly Str NumberTip = new("tools.numberTip", "序号", "Number");
        public static readonly Str OvalHelp = new("tools.ovalHelp", "拖出椭圆或圆框住目标区域；按住 Shift 拖=正圆；再按 O 或点同一图标退出绘制", "Drag an ellipse or circle around the target; hold Shift for a perfect circle; press O again or click the same icon to leave drawing");
        public static readonly Str OvalTip = new("tools.ovalTip", "圆圈", "Ellipse");
        public static readonly Str PenHelp = new("tools.penHelp", "按住拖动自由划线，松手成笔", "Hold and drag to draw freely; letting go finishes the stroke");
        public static readonly Str PenTip = new("tools.penTip", "手绘", "Freehand");
        public static readonly Str RectHelp = new("tools.rectHelp", "拖出矩形框圈住目标区域，最常用的强调框；按住 Shift 拖=正方形；再按 R 或点同一图标退出绘制", "Drag a rectangle around the target, the most common emphasis box; hold Shift for a square; press R again or click the same icon to leave drawing");
        public static readonly Str RectTip = new("tools.rectTip", "矩形", "Rectangle");
        public static readonly Str SelectHelp = new("tools.selectHelp", "点击标注=选中，虚线框标出；拖动=移动；Delete=删除；Esc 先取消选中。移动和删除都可 Ctrl+Z 撤销", "Click a mark to select it, outlined by a dashed box; drag to move; Delete removes it; Esc clears the selection first. Ctrl+Z undoes both moving and deleting");
        public static readonly Str SelectTip = new("tools.selectTip", "选中", "Select");
        public static readonly Str TextHelp = new("tools.textHelp", "点画布就地输入，Enter 上屏 / Esc 取消；点已有文字可再编辑；字号随线宽挡位", "Click the canvas and type in place, Enter commits / Esc cancels; click existing text to edit it again; the font size follows the width step");
        public static readonly Str TextTip = new("tools.textTip", "文字", "Text");
    }
}
