using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Theme = FrameCapture.Core.Theme;

namespace FrameCapture.Overlays;

/// <summary>
/// 自绘 RGBA 调色板控件：DevTools 式 SV 区 + 色相/透明度竖条 + HEX/RGBA 直填。
/// 替代系统 ColorDialog——主题一致、就地弹出、拖动即时生效。
/// 纯视图：不关心弹出方式；宿主（编辑器弹层 / SelfTest 离屏渲染）订阅 Changed 收色。
/// </summary>
public sealed class ColorPickView : StackPanel
{
    double _h, _s = 1, _v = 1, _a = 1;

    readonly Rectangle _svHue, _svVal, _alphaFill, _previewRect;
    readonly Ellipse _svKnob;
    readonly Rectangle _hueThumb, _alphaThumb;
    readonly TextBox _hexBox, _rBox, _gBox, _bBox, _aBox;

    /// <summary>用户改动（拖动/输入）后当前色。</summary>
    public event Action<Color>? Changed;

    public Color Color
    {
        get { var (r, g, b) = Rgb(); return Color.FromArgb((byte)Math.Round(_a * 255), r, g, b); }
        set { SetState(value); Sync(); }
    }

    public ColorPickView()
    {
        // ---- SV 区（180×120）：白→色相横渐变 + 透明→黑竖渐变，十字准星 ----
        _svHue = new Rectangle { Fill = new LinearGradientBrush(Colors.White, Colors.Red, 0) };
        _svVal = new Rectangle { Fill = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90) };
        _svKnob = new Ellipse
        {
            Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.6 },
            IsHitTestVisible = false,
        };
        var sv = new Grid { Width = 180, Height = 120, Cursor = Cursors.Cross, ClipToBounds = true };
        sv.Children.Add(_svHue); sv.Children.Add(_svVal);
        // 游标必须活在 Canvas 里：Canvas.SetLeft/Top 对 Grid 子级无效（起游标一直钉死中央）
        var knobHost = new Canvas { IsHitTestVisible = false };
        knobHost.Children.Add(_svKnob);
        sv.Children.Add(knobHost);
        AttachDrag(sv, (x, y) => { _s = x; _v = 1 - y; Sync(); Fire(); });

        // ---- 色相竖条：彩虹常驻 ----
        var hue = new Rectangle { Width = 14, Height = 120 };
        hue.Fill = new LinearGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                new(Colors.Red, 0), new(Colors.Yellow, 0.1667), new(Colors.Lime, 0.3333), new(Colors.Cyan, 0.5),
                new(Colors.Blue, 0.6667), new(Colors.Magenta, 0.8333), new(Colors.Red, 1),
            },
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
        };
        _hueThumb = ThumbLine();
        var hueHost = new Grid { Width = 14, Height = 120, Cursor = Cursors.Hand };
        hueHost.Children.Add(hue); hueHost.Children.Add(_hueThumb);
        AttachDrag(hueHost, (_, y) => { _h = y * 360; Sync(); Fire(); });

        // ---- 透明度竖条：棋盘底 + 实色→透明 ----
        var alphaHost = new Grid { Width = 14, Height = 120, Cursor = Cursors.Hand };
        alphaHost.Children.Add(new Rectangle { Width = 14, Height = 120, Fill = Checker() });
        _alphaFill = new Rectangle { Width = 14, Height = 120 };
        _alphaThumb = ThumbLine();
        alphaHost.Children.Add(_alphaFill); alphaHost.Children.Add(_alphaThumb);
        AttachDrag(alphaHost, (_, y) => { _a = 1 - y; Sync(); Fire(); });

        // ---- 预览（棋盘底看透明）----
        var preview = new Grid { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
        preview.Children.Add(new Rectangle { Width = 28, Height = 28, Fill = Checker(), RadiusX = 4, RadiusY = 4 });
        _previewRect = new Rectangle { Width = 28, Height = 28, RadiusX = 4, RadiusY = 4, Stroke = new SolidColorBrush(Color.FromArgb(0x66, Theme.Text.R, Theme.Text.G, Theme.Text.B)), StrokeThickness = 1 };
        preview.Children.Add(_previewRect);

        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Round(sv, 6), Spacing(6), Round(hueHost, 4), Spacing(6), Round(alphaHost, 4), preview },
        };

        // ---- HEX + RGBA 直填（宿主 Popup 继承图标字体，文本框显式复位）----
        _hexBox = TextBoxAt(96);
        _rBox = NumBox(); _gBox = NumBox(); _bBox = NumBox(); _aBox = NumBox();
        _hexBox.KeyDown += (_, ek) => { if (ek.Key == Key.Enter) { CommitHex(); ek.Handled = true; } };
        _hexBox.LostFocus += (_, _) => CommitHex();
        NumCommit(_rBox, v => SetRgbPart(0, v));
        NumCommit(_gBox, v => SetRgbPart(1, v));
        NumCommit(_bBox, v => SetRgbPart(2, v));
        NumCommit(_aBox, v => { _a = Math.Clamp(v, 0, 255) / 255.0; Sync(); Fire(); });
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0),
            Children = { Field(_hexBox, 104), Spacing(8), Labeled("R", _rBox), Spacing(6), Labeled("G", _gBox), Spacing(6), Labeled("B", _bBox), Spacing(6), Labeled("A", _aBox) },
        };
        Orientation = Orientation.Vertical;
        Children.Add(top); Children.Add(bottom);
    }

    void SetState(Color c)
    {
        (_h, _s, _v) = FrameCapture.Core.Hsv.FromRgb(c.R, c.G, c.B);
        _a = c.A / 255.0;
    }

    (byte r, byte g, byte b) Rgb() => FrameCapture.Core.Hsv.ToRgb(_h, _s, _v);

    void Fire() => Changed?.Invoke(Color);

    void SetRgbPart(int i, int v)
    {
        v = Math.Clamp(v, 0, 255);
        var (r, g, b) = Rgb();
        if (i == 0) r = (byte)v; else if (i == 1) g = (byte)v; else b = (byte)v;
        SetState(Color.FromRgb(r, g, b));
        Sync(); Fire();
    }

    void CommitHex()
    {
        var got = FrameCapture.Core.HexColor.Parse(_hexBox.Text);
        if (got == null) { Sync(); return; } // 非法输入：回弹当前色
        SetState(got.Value);
        Sync(); Fire();
    }

    /// <summary>状态→一切视觉：渐变、准星、滑块、预览、文本框（聚焦中的框不回写，防打字被吞）。</summary>
    void Sync()
    {
        var (r, g, b) = Rgb();
        var opaque = Color.FromRgb(r, g, b);
        _svHue.Fill = new LinearGradientBrush(Colors.White, opaque, 0);
        double w = _svHue.ActualWidth, h = _svHue.ActualHeight;
        Canvas.SetLeft(_svKnob, _s * w - 6); Canvas.SetTop(_svKnob, (1 - _v) * h - 6);
        _hueThumb.Margin = new Thickness(0, Math.Clamp(_h / 360 * (_hueThumb.Parent is FrameworkElement p ? p.ActualHeight : 120), 0, 117), 0, 0);
        _alphaFill.Fill = new LinearGradientBrush { GradientStops = new GradientStopCollection { new(opaque, 0), new(Color.FromArgb(0, r, g, b), 1) }, StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        _alphaThumb.Margin = new Thickness(0, Math.Clamp((1 - _a) * (_alphaThumb.Parent is FrameworkElement ap ? ap.ActualHeight : 120), 0, 117), 0, 0);
        _previewRect.Fill = new SolidColorBrush(Color.FromArgb((byte)Math.Round(_a * 255), r, g, b));

        var hex = FrameCapture.Core.HexColor.Format(Color.FromArgb((byte)Math.Round(_a * 255), r, g, b));
        SetTextUnlessFocused(_hexBox, hex);
        SetTextUnlessFocused(_rBox, r.ToString());
        SetTextUnlessFocused(_gBox, g.ToString());
        SetTextUnlessFocused(_bBox, b.ToString());
        SetTextUnlessFocused(_aBox, ((byte)Math.Round(_a * 255)).ToString());
    }

    static void SetTextUnlessFocused(TextBox? box, string text)
    {
        if (box == null || box.IsKeyboardFocusWithin) return;
        box.Text = text;
    }

    /// <summary>把元素变成归一化拖拽面（0..1，钳进边界），按下即捕获、移动即回调。</summary>
    static void AttachDrag(FrameworkElement el, Action<double, double> onPos)
    {
        void Move(MouseEventArgs e)
        {
            var p = e.GetPosition(el);
            onPos(Math.Clamp(p.X / Math.Max(1, el.ActualWidth), 0, 1), Math.Clamp(p.Y / Math.Max(1, el.ActualHeight), 0, 1));
        }
        el.MouseLeftButtonDown += (s, e) => { el.CaptureMouse(); Move(e); e.Handled = true; };
        el.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) Move(e); };
        el.MouseLeftButtonUp += (s, e) => { el.ReleaseMouseCapture(); e.Handled = true; };
    }

    static UIElement Spacing(double w) => new Rectangle { Width = w };
    // 圆角包裹（Border 按 CornerRadius 裁剪子内容）+ 细描边，让 SV/色相/透明条不再是生硬方块
    static Border Round(UIElement e, double r) => new()
    {
        CornerRadius = new CornerRadius(r), ClipToBounds = true, Child = e,
        BorderBrush = new SolidColorBrush(Theme.Stroke), BorderThickness = new Thickness(1),
    };
    static Rectangle ThumbLine() => new()
    {
        Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Brushes.White,
        Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), StrokeThickness = 1,
        VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
    };
    static Border Labeled(string t, UIElement box) => new()
    {
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = t, FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) },
                Field(box, 40),
            },
        },
    };
    // 主题化输入字段——深色圆角底 + 描边，聚焦转强调色（原默认白底系统边框在深色弹层里很突兀）
    static Border Field(UIElement inner, double w)
    {
        var b = new Border
        {
            Width = w, Height = 24,
            Background = new SolidColorBrush(Theme.TrackBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 0, 6, 0),
            Child = inner,
        };
        inner.GotKeyboardFocus += (_, _) => b.BorderBrush = new SolidColorBrush(Theme.Accent);
        inner.LostKeyboardFocus += (_, _) => b.BorderBrush = new SolidColorBrush(Theme.Stroke);
        return b;
    }
    static TextBox TextBoxAt(double w) => new()
    {
        FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = new SolidColorBrush(Theme.Text),
        Background = Brushes.Transparent, BorderThickness = new Thickness(0), CaretBrush = new SolidColorBrush(Theme.Text),
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    static TextBox NumBox() => new()
    {
        FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = new SolidColorBrush(Theme.Text),
        Background = Brushes.Transparent, BorderThickness = new Thickness(0), CaretBrush = new SolidColorBrush(Theme.Text),
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    static void NumCommit(TextBox box, Action<int> apply)
    {
        box.PreviewTextInput += (_, e) => { foreach (var ch in e.Text) if (!char.IsDigit(ch)) { e.Handled = true; return; } };
        box.KeyDown += (_, ek) => { if (ek.Key == Key.Enter) { if (int.TryParse(box.Text, out var v)) apply(v); ek.Handled = true; } };
        box.LostFocus += (_, _) => { if (int.TryParse(box.Text, out var v)) apply(v); };
    }

    public static DrawingBrush Checker()
    {
        var d = new DrawingGroup();
        var a = new SolidColorBrush(Color.FromRgb(204, 204, 204));
        var w = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        d.Children.Add(new GeometryDrawing(a, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        d.Children.Add(new GeometryDrawing(w, null, new RectangleGeometry(new Rect(8, 0, 8, 8))));
        d.Children.Add(new GeometryDrawing(w, null, new RectangleGeometry(new Rect(0, 8, 8, 8))));
        d.Children.Add(new GeometryDrawing(a, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        return new DrawingBrush(d) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
    }
}
