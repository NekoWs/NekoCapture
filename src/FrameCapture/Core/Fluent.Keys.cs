// Fluent partial：键帽配色。安装向导链的是 Theme/Fluent 那几份共享文件，这份带 WPF 控件依赖，它不链。
using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>Fluent 的键帽配色 partial：底色靠往卡面上叠一层 hover 立起来，
/// 与 <see cref="Keycaps"/> 画键帽用的是同一个合成算法。</summary>
public static partial class Fluent
{
    /// <summary>键帽底色：卡面叠一层 hover，比所在卡片亮一档，键帽才从卡片里立出来。</summary>
    /// <returns>叠色后的不透明色，深浅主题各自成立。</returns>
    public static Color KeycapFace() => Keycaps.Lift(Theme.Opaque(Theme.CardBg), Theme.Hover);
}
