// Fluent partial：键帽配色。安装向导链的是 Theme/Fluent 那几份共享文件，这份带 WPF 控件依赖，它不链。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>Fluent 的键帽配色 partial：底色靠往卡面上叠一层 hover 立起来，
/// 与 <see cref="Keycaps"/> 画键帽用的是同一个合成算法。</summary>
public static partial class Fluent
{
    /// <summary>键帽底色：暗色往卡面上叠一层 hover 立起来；亮色取纯白——
    /// 卡面已经是最白，再压一层灰就成了「按不动」的禁用件。亮色里键帽坐在行底色上，
    /// 白面对浅灰面本身就有对比，厚度交给底边那一档加粗的描边。</summary>
    /// <returns>键帽底色的不透明实色，深浅主题各自成立。</returns>
    public static Color KeycapFace() => Theme.IsDark
        ? Keycaps.Lift(Theme.Opaque(Theme.CardBg), Theme.Hover)
        : Colors.White;

    /// <summary>标准键帽配色：底色取键帽面，字色与描边随主题。键帽在任何界面都该是同一副长相——
    /// 首启卡把键位写成彩色文字、编辑器写成键帽，用户在首启卡学到的「键位长这样」到下一扇窗就不成立了。</summary>
    /// <param name="size">键帽字号，默认 12。</param>
    /// <returns>可直接交给 <see cref="Keycaps.Row(string, KeycapTheme, string)"/> 的配色。</returns>
    public static KeycapTheme KeycapTheme(double size = 12) => new()
    {
        Face = KeycapFace(),
        Text = Theme.Text,
        Stroke = Theme.Stroke,
        Dim = Theme.TextDim,
        Size = size,
    };

    /// <summary>图标字形块：行高收到字号本身，字形在块里水平垂直都居中。
    /// 默认行盒比字形高出一截（字体升降部都算进去），同一行里图标与文字各按自己的行盒居中，
    /// 看着就是图标偏上或偏下。收掉多余行距，块的上下沿才等于字形自己的上下沿。</summary>
    /// <param name="glyph">Segoe Fluent Icons 的 PUA 码位。</param>
    /// <param name="size">字号（等于期望的图标高度）。</param>
    /// <param name="fg">字形颜色。</param>
    /// <param name="width">固定宽度（图标要在一列里左右对齐时给）；0 表示按内容宽。</param>
    /// <returns>可直接放进任意容器的字形块。</returns>
    public static TextBlock Glyph(string glyph, double size, Brush fg, double width = 0)
    {
        var t = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily(IconFontChain),
            FontSize = size,
            Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            LineHeight = Math.Ceiling(size),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextAlignment = TextAlignment.Center,
        };
        if (width > 0) t.Width = width;
        return t;
    }
}
