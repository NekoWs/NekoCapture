// Fluent partial：键帽配色。安装向导链的是 Theme/Fluent 那几份共享文件，这份带 WPF 控件依赖，它不链。
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
}
