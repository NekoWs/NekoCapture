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
}
