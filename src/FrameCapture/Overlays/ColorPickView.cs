using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Theme = FrameCapture.Core.Theme;

namespace FrameCapture.Overlays;

/// <summary>
/// 自绘 RGBA 调色板控件：方形色域 + 色相/透明度竖条，下面一行「预览 + HEX」直填。
/// 不用系统 ColorDialog：主题一致、就地弹出、拖动即时生效。
/// 纯视图：不关心弹出方式；宿主（编辑器弹层 / SelfTest 离屏渲染）订阅 Changed 收色。
/// 色域与两条竖条都是直角、不带裁剪：圆角会把四个角切掉一块，看着像内容被啃了。
/// </summary>
public sealed class ColorPickView : StackPanel
{
    const double SvSize = 168;   // 方形色域边长，也是两条竖条的高度
    const double BarW = 14;
    const double PreviewSize = 26;

    double _h, _s = 1, _v = 1, _a = 1;

    readonly Rectangle _svHue, _svVal, _alphaFill, _previewRect;
    readonly Ellipse _svKnob;
    readonly Rectangle _hueThumb, _alphaThumb;
    readonly TextBox _hexBox;

    /// <summary>用户改动（拖动/输入）后当前色。</summary>
    public event Action<Color>? Changed;

    /// <summary>当前颜色（RGBA）。程序性赋值不触发 <see cref="Changed"/>——只有用户交互才发事件。</summary>
    public Color Color
    {
        get { var (r, g, b) = Rgb(); return Color.FromArgb((byte)Math.Round(_a * 255), r, g, b); }
        set { SetState(value); Sync(); }
    }

    /// <summary>构建方形色域、色相/透明度竖条与「预览 + HEX」；初始色为纯红不透明，构造不发 <see cref="Changed"/>。</summary>
    public ColorPickView()
    {
        #region 方形色域：白→色相横渐变 + 透明→黑竖渐变，十字准星
        _svHue = new Rectangle { Fill = new LinearGradientBrush(Colors.White, Colors.Red, 0) };
        _svVal = new Rectangle { Fill = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90) };
        _svKnob = new Ellipse
        {
            Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.6 },
            IsHitTestVisible = false,
        };
        var sv = new Grid { Width = SvSize, Height = SvSize, Cursor = Cursors.Cross, ClipToBounds = true };
        sv.Children.Add(_svHue); sv.Children.Add(_svVal);
        // 游标必须活在 Canvas 里：Canvas.SetLeft/Top 对 Grid 子级无效，否则游标钉死中央
        var knobHost = new Canvas { IsHitTestVisible = false };
        knobHost.Children.Add(_svKnob);
        sv.Children.Add(knobHost);
        AttachDrag(sv, (x, y) => { _s = x; _v = 1 - y; Sync(); Fire(); });
        #endregion

        #region 色相竖条：彩虹常驻
        var hue = new Rectangle { Width = BarW, Height = SvSize };
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
        var hueHost = new Grid { Width = BarW, Height = SvSize, Cursor = Cursors.Hand };
        hueHost.Children.Add(hue); hueHost.Children.Add(_hueThumb);
        AttachDrag(hueHost, (_, y) => { _h = y * 360; Sync(); Fire(); });
        #endregion

        #region 透明度竖条：棋盘底 + 实色→透明
        var alphaHost = new Grid { Width = BarW, Height = SvSize, Cursor = Cursors.Hand };
        alphaHost.Children.Add(new Rectangle { Width = BarW, Height = SvSize, Fill = Checker() });
        _alphaFill = new Rectangle { Width = BarW, Height = SvSize };
        _alphaThumb = ThumbLine();
        alphaHost.Children.Add(_alphaFill); alphaHost.Children.Add(_alphaThumb);
        AttachDrag(alphaHost, (_, y) => { _a = 1 - y; Sync(); Fire(); });
        #endregion

        #region 上排：色域 + 两条竖条，边距写死成同一组数，两排自然同宽
        var top = new Grid { Height = SvSize };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var svCell = Span(sv);
        var hueCell = Span(hueHost);
        hueCell.Margin = new Thickness(6, 0, 0, 0);
        var alphaCell = Span(alphaHost);
        alphaCell.Margin = new Thickness(6, 0, 0, 0);
        top.Children.Add(svCell); Grid.SetColumn(svCell, 0);
        top.Children.Add(hueCell); Grid.SetColumn(hueCell, 1);
        top.Children.Add(alphaCell); Grid.SetColumn(alphaCell, 2);
        #endregion

        #region 下排：颜色预览 + HEX 直填（RGBA 四个输入框撤掉：调色靠拖，精确值靠 HEX）
        _hexBox = new TextBox
        {
            FontFamily = new FontFamily("Consolas"), FontSize = 12.5, Foreground = new SolidColorBrush(Theme.Text),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), CaretBrush = new SolidColorBrush(Theme.Text),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _hexBox.KeyDown += (_, ek) => { if (ek.Key == Key.Enter) { CommitHex(); ek.Handled = true; } };
        _hexBox.LostFocus += (_, _) => CommitHex();

        var preview = new Grid { Width = PreviewSize, Height = PreviewSize };
        preview.Children.Add(new Rectangle { Width = PreviewSize, Height = PreviewSize, Fill = Checker(), RadiusX = 4, RadiusY = 4 });
        _previewRect = new Rectangle { Width = PreviewSize, Height = PreviewSize, RadiusX = 4, RadiusY = 4, Stroke = new SolidColorBrush(Color.FromArgb(0x66, Theme.Text.R, Theme.Text.G, Theme.Text.B)), StrokeThickness = 1 };
        preview.Children.Add(_previewRect);

        // 下排交给 Grid：HEX 框坐星型列，宽度由「上排有多宽」决定。
        // 手算「色域 + 两条竖条 + 两段 6」会漏掉三个外框各 1px 的描边，差出一截。
        var bottom = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.Children.Add(preview);
        var hexField = Field(_hexBox);
        Grid.SetColumn(hexField, 2);
        bottom.Children.Add(hexField);
        #endregion

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

    void CommitHex()
    {
        var got = Core.HexColor.Parse(_hexBox.Text);
        if (got == null) { Sync(); return; } // 非法输入：回弹当前色
        SetState(got.Value);
        Sync(); Fire();
    }

    /// <summary>状态→一切视觉：渐变、准星、滑块、预览、HEX 框（聚焦中的框不回写，防打字被吞）。</summary>
    void Sync()
    {
        var (r, g, b) = Rgb();
        var opaque = Color.FromRgb(r, g, b);
        _svHue.Fill = new LinearGradientBrush(Colors.White, opaque, 0);
        double w = _svHue.ActualWidth, h = _svHue.ActualHeight;
        Canvas.SetLeft(_svKnob, _s * w - 6); Canvas.SetTop(_svKnob, (1 - _v) * h - 6);
        _hueThumb.Margin = new Thickness(0, ThumbTop(_h / 360, _hueThumb), 0, 0);
        _alphaFill.Fill = new LinearGradientBrush { GradientStops = new GradientStopCollection { new(opaque, 0), new(Color.FromArgb(0, r, g, b), 1) }, StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        _alphaThumb.Margin = new Thickness(0, ThumbTop(1 - _a, _alphaThumb), 0, 0);
        _previewRect.Fill = new SolidColorBrush(Color.FromArgb((byte)Math.Round(_a * 255), r, g, b));

        if (!_hexBox.IsKeyboardFocusWithin)
            _hexBox.Text = Core.HexColor.Format(Color.FromArgb((byte)Math.Round(_a * 255), r, g, b));
    }

    /// <summary>滑块上沿的位置：按父级实测高度算，写死一个数会在改色域高度时让滑块越界或提前停住。</summary>
    static double ThumbTop(double t, Rectangle thumb)
    {
        double h = thumb.Parent is FrameworkElement p ? p.ActualHeight : SvSize;
        double max = Math.Max(0, h - thumb.Height);
        return Math.Clamp(t * h, 0, max);
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

    /// <summary>色域/竖条的外框：直角 + 一根细描边，不做圆角、不裁剪。
    /// 圆角会把内容的四个角切掉一块（Border 只圆描边，内容仍按矩形画，多出来的方角露在圆外），
    /// 取色器要的是完整的一块颜色，四角不该少。</summary>
    static Border Span(UIElement e) => new()
    {
        Child = e,
        BorderBrush = new SolidColorBrush(Theme.Stroke),
        BorderThickness = new Thickness(1),
    };

    static Rectangle ThumbLine() => new()
    {
        Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Brushes.White,
        Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), StrokeThickness = 1,
        VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
    };

    // 主题化输入字段——深色底 + 描边，聚焦转强调色；宽度由所在列给（不写死，换语言/换字体不会裂）
    static Border Field(UIElement inner)
    {
        var b = new Border
        {
            Height = 26,
            Background = new SolidColorBrush(Theme.TrackBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 0, 8, 0),
            Child = inner,
        };
        inner.GotKeyboardFocus += (_, _) => b.BorderBrush = new SolidColorBrush(Theme.Accent);
        inner.LostKeyboardFocus += (_, _) => b.BorderBrush = new SolidColorBrush(Theme.Stroke);
        return b;
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
}
