using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Theme = FrameCapture.Core.Theme;

namespace FrameCapture.Overlays;

/// <summary>
/// 自绘 RGBA 调色板控件：DevTools 式 SV 区 + 色相/透明度竖条 + HEX/RGBA 直填。
/// 不用系统 ColorDialog：主题一致、就地弹出、拖动即时生效。
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

    /// <summary>当前颜色（RGBA）。程序性赋值不触发 <see cref="Changed"/>——只有用户交互才发事件。</summary>
    public Color Color
    {
        get { var (r, g, b) = Rgb(); return Color.FromArgb((byte)Math.Round(_a * 255), r, g, b); }
        set { SetState(value); Sync(); }
    }

    /// <summary>构建 SV 区、色相/透明度竖条与 HEX/RGBA 直填；初始色为纯红不透明，构造不发 <see cref="Changed"/>。</summary>
    public ColorPickView()
    {
        #region SV 区（180×120）：白→色相横渐变 + 透明→黑竖渐变，十字准星
        _svHue = new Rectangle { Fill = new LinearGradientBrush(Colors.White, Colors.Red, 0) };
        _svVal = new Rectangle { Fill = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90) };
        _svKnob = new Ellipse
        {
            Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.6 },
            IsHitTestVisible = false,
        };
        var sv = new Grid { Height = 120, Cursor = Cursors.Cross, ClipToBounds = true };
        sv.Children.Add(_svHue); sv.Children.Add(_svVal);
        // 游标必须活在 Canvas 里：Canvas.SetLeft/Top 对 Grid 子级无效，否则游标钉死中央
        var knobHost = new Canvas { IsHitTestVisible = false };
        knobHost.Children.Add(_svKnob);
        sv.Children.Add(knobHost);
        AttachDrag(sv, (x, y) => { _s = x; _v = 1 - y; Sync(); Fire(); });
        #endregion

        #region 色相竖条：彩虹常驻
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
        #endregion

        #region 透明度竖条：棋盘底 + 实色→透明
        var alphaHost = new Grid { Width = 14, Height = 120, Cursor = Cursors.Hand };
        alphaHost.Children.Add(new Rectangle { Width = 14, Height = 120, Fill = Checker() });
        _alphaFill = new Rectangle { Width = 14, Height = 120 };
        _alphaThumb = ThumbLine();
        alphaHost.Children.Add(_alphaFill); alphaHost.Children.Add(_alphaThumb);
        AttachDrag(alphaHost, (_, y) => { _a = 1 - y; Sync(); Fire(); });
        #endregion

        #region 预览（棋盘底看透明）
        var preview = new Grid { Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
        preview.Children.Add(new Rectangle { Width = 26, Height = 26, Fill = Checker(), RadiusX = 4, RadiusY = 4 });
        _previewRect = new Rectangle { Width = 26, Height = 26, RadiusX = 4, RadiusY = 4, Stroke = new SolidColorBrush(Color.FromArgb(0x66, Theme.Text.R, Theme.Text.G, Theme.Text.B)), StrokeThickness = 1 };
        preview.Children.Add(_previewRect);

        // 上排交给 Grid：SV 区坐在星型列里，把「下排比它宽出来的那截」全吃掉。
        // 不用「先量一遍再算宽度」：构造期的测量会被父级缓存住，之后改子元素宽度并不会让它重新长出来，
        // 结果 SV 区只剩一层边框。星型列由布局自己分配，不存在这个先后问题。
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        for (int i = 0; i < 3; i++) top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var svCell = Round(sv, 6);
        var hueCell = Round(hueHost, 4);
        hueCell.Margin = new Thickness(6, 0, 0, 0);
        var alphaCell = Round(alphaHost, 4);
        alphaCell.Margin = new Thickness(6, 0, 0, 0);
        preview.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(svCell); Grid.SetColumn(svCell, 0);
        top.Children.Add(hueCell); Grid.SetColumn(hueCell, 1);
        top.Children.Add(alphaCell); Grid.SetColumn(alphaCell, 2);
        top.Children.Add(preview); Grid.SetColumn(preview, 3);
        #endregion

        #region HEX + RGBA 直填（宿主 Popup 继承图标字体，文本框显式复位）
        _hexBox = TextBoxAt();
        _rBox = NumBox(); _gBox = NumBox(); _bBox = NumBox(); _aBox = NumBox();
        _hexBox.KeyDown += (_, ek) => { if (ek.Key == Key.Enter) { CommitHex(); ek.Handled = true; } };
        _hexBox.LostFocus += (_, _) => CommitHex();
        NumCommit(_rBox, v => SetRgbPart(0, v));
        NumCommit(_gBox, v => SetRgbPart(1, v));
        NumCommit(_bBox, v => SetRgbPart(2, v));
        NumCommit(_aBox, v => { _a = Math.Clamp(v, 0, 255) / 255.0; Sync(); Fire(); });
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0),
            Children = { Field(_hexBox, 96), Spacing(8), Labeled("R", _rBox), Spacing(6), Labeled("G", _gBox), Spacing(6), Labeled("B", _bBox), Spacing(6), Labeled("A", _aBox) },
        };
        // 上下两排同宽由布局保证：上排是 Grid（SV 占星型列），下排的自然宽度决定整块的总宽，
        // 星型列把差值全吃掉，右端不会留一块空着的暗面。
        Orientation = Orientation.Vertical;
        Children.Add(top); Children.Add(bottom);
    }

    void SetState(Color c)
    {
        (_h, _s, _v) = Core.Hsv.FromRgb(c.R, c.G, c.B);
        _a = c.A / 255.0;
    }

    (byte r, byte g, byte b) Rgb() => Core.Hsv.ToRgb(_h, _s, _v);

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
        var got = Core.HexColor.Parse(_hexBox.Text);
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

        var hex = Core.HexColor.Format(Color.FromArgb((byte)Math.Round(_a * 255), r, g, b));
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

    // 圆角包裹 + 细描边，SV/色相/透明条不生硬。
    // Border 的 CornerRadius 只把描边画圆，子内容仍按矩形边界裁——方角会从圆角外侧露出来，
    // 露出来的那一圈正是「四角被圆角切掉」的样子。用几何裁剪把内容收进同一条圆角轮廓，
    // 边缘由 WPF 自己抗锯齿，不需要再垫一层遮罩。
    static Border Round(UIElement e, double r)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(r), ClipToBounds = true, Child = e,
            BorderBrush = new SolidColorBrush(Theme.Stroke), BorderThickness = new Thickness(1),
        };
        void Clip()
        {
            if (b.ActualWidth <= 0 || b.ActualHeight <= 0) return;
            b.Clip = new RectangleGeometry(new Rect(0, 0, b.ActualWidth, b.ActualHeight), r, r);
        }
        b.SizeChanged += (_, _) => Clip();
        return b;
    }
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
                new TextBlock { Text = t, FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) },
                Field(box, 34),
            },
        },
    };
    // 主题化输入字段——深色圆角底 + 描边，聚焦转强调色
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
    static TextBox TextBoxAt() => new()
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

    /// <summary>透明预览用棋盘格刷：16×16 DIP 平铺，8×8 方格，灰 #CCCCCC 与白交替。</summary>
    /// <returns>可平铺的 <see cref="DrawingBrush"/>（Absolute 视口）；每次调用返回新实例。</returns>
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
        #endregion

}
