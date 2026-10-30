using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>搜索框焦点、占位文字和清空按钮的连续状态过渡。</summary>
public sealed partial class SettingsWindow
{
    private Action<bool> WireSettingsSearchMotion(Border frame, TextBlock icon, TextBlock placeholder, Border clear)
    {
        var stroke = new SolidColorBrush(Theme.StrokeField);
        var fill = new SolidColorBrush(Theme.CardBg);
        var ink = new SolidColorBrush(Theme.TextDim);
        frame.BorderBrush = stroke; frame.Background = fill; icon.Foreground = ink;
        void Refresh(bool animate)
        {
            bool focused = frame.IsKeyboardFocusWithin;
            var accent = Theme.Accent;
            Color backing = focused ? Fluent.Mix(Theme.CardBg, Color.FromArgb(0x18, accent.R, accent.G, accent.B))
                : frame.IsMouseOver ? Fluent.Mix(Theme.CardBg, Theme.Hover) : Theme.CardBg;
            void Paint(SolidColorBrush brush, Color color)
            {
                if (!animate) { brush.BeginAnimation(SolidColorBrush.ColorProperty, null); brush.Color = color; return; }
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(brush.Color, color,
                    TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
            }
            Paint(stroke, focused ? accent : Theme.StrokeField);
            Paint(fill, backing); Paint(ink, focused ? accent : Theme.TextDim);
        }
        frame.IsKeyboardFocusWithinChanged += (_, _) => Refresh(true);
        frame.MouseEnter += (_, _) => Refresh(true);
        frame.MouseLeave += (_, _) => Refresh(true);
        _repaint.Add(() => Refresh(false));
        int generation = 0;
        bool shown = false;
        return hasText =>
        {
            if (shown == hasText) return;
            shown = hasText; int current = ++generation;
            double targetWidth = hasText ? 23 : 0;
            if (!frame.IsLoaded)
            {
                placeholder.Opacity = hasText ? 0 : 1; clear.Width = targetWidth;
                clear.Opacity = hasText ? 1 : 0; clear.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            clear.Visibility = Visibility.Visible; clear.IsHitTestVisible = hasText;
            var duration = TimeSpan.FromMilliseconds(Fluent.Motion.StateMs);
            placeholder.BeginAnimation(OpacityProperty, new DoubleAnimation(placeholder.Opacity, hasText ? 0 : 1, duration));
            clear.BeginAnimation(OpacityProperty, new DoubleAnimation(clear.Opacity, hasText ? 1 : 0, duration));
            var width = new DoubleAnimation(clear.Width, targetWidth, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            width.Completed += (_, _) =>
            {
                if (generation != current) return;
                clear.BeginAnimation(WidthProperty, null); clear.Width = targetWidth;
                clear.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            };
            clear.BeginAnimation(WidthProperty, width);
        };
    }
}
