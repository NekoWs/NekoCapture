using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NekoCapture.Core;

/// <summary>录键框的常规与行内外观，边框和背景使用独立画刷。</summary>
public sealed partial class ChordCaptureBox
{
    private bool _inline;
    private readonly SolidColorBrush _face = new(), _edge = new();

    /// <summary>行内模式仅保留键帽，悬停、焦点与录入时显示交互轮廓。</summary>
    public bool Inline
    {
        get => _inline;
        set
        {
            _inline = value;
            Padding = value ? new Thickness(4, 3, 4, 3) : new Thickness(10, 6, 10, 6);
            CornerRadius = new CornerRadius(value ? 10 : 6);
            Render();
        }
    }

    private void PaintChrome(bool animate = false)
    {
        bool focus = _capturing || IsKeyboardFocusWithin;
        Color edge = focus ? Theme.Accent : !_inline || IsMouseOver ? Theme.StrokeField : Colors.Transparent;
        Color face = _inline ? _capturing ? Theme.Hover : Colors.Transparent : Theme.KeycapBg;
        Background = _face; BorderBrush = _edge;
        Paint(_face, face); Paint(_edge, edge);
        void Paint(SolidColorBrush brush, Color target)
        {
            if (animate) brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(target, TimeSpan.FromMilliseconds(Fluent.Motion.HoverMs)));
            else { brush.BeginAnimation(SolidColorBrush.ColorProperty, null); brush.Color = target; }
        }
    }
}
