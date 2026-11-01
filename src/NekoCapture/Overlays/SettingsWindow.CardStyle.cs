using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>设置卡片共享外边距与轻量投影。</summary>
public sealed partial class SettingsWindow
{
    private const double SettingsCardGap = 12;
    private static Thickness SettingsCardMargin => new(0, 0, 0, SettingsCardGap);

    private static void StyleSettingsCard(FrameworkElement card, bool outerSpacing = true)
    {
        if (outerSpacing) card.Margin = SettingsCardMargin;
        var shadow = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Direction = 270 };
        void Refresh() => shadow.Opacity = Theme.IsDark ? 0.12 : 0.08;
        Refresh(); card.Effect = shadow;
        card.Loaded += (_, _) => { Theme.Changed -= Refresh; Theme.Changed += Refresh; Refresh(); };
        card.Unloaded += (_, _) => Theme.Changed -= Refresh;
    }
}
