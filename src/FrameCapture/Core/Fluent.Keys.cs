// Fluent partial：键帽配色。安装向导链的是 Theme/Fluent 那几份共享文件，这份带手势配置器依赖，它不链。
using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>Fluent 的键帽配色 partial：底色靠往卡面上叠一层 hover 立起来，
/// 合成算法与手势配置器画键帽用的是同一个。<see cref="KeycapFace"/> 只在主项目可用。</summary>
public static partial class Fluent
{
    /// <summary>键帽底色：卡面叠一层 hover，比所在卡片亮一档，键帽才从卡片里立出来。</summary>
    /// <returns>叠色后的不透明色，深浅主题各自成立。</returns>
    public static Color KeycapFace() => GestureEditor.Keycaps.Lift(Theme.Opaque(Theme.CardBg), Theme.Hover);
}
