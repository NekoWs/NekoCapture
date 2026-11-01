using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>卡片内的独立操作接管按下事件，避免整行按钮捕获鼠标。</summary>
public sealed partial class SettingsWindow
{
    private static void OwnSettingsPointerPress(FrameworkElement control)
    {
        control.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            control.Focus();
            for (DependencyObject? parent = VisualTreeHelper.GetParent(control); parent != null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not Button { Tag: "settings-overview" } card) continue;
                card.Background = Theme.CardBgBrush;
                break;
            }
        };
    }
}
