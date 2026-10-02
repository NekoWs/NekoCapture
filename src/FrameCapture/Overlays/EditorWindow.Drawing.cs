using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Drawing;
using Theme = FrameCapture.Core.Theme;

namespace FrameCapture.Overlays;

// 绘图工具层：点/线/圆/箭头/手绘/裁剪 + 撤销 + 时间轴范围
public sealed partial class EditorWindow
{
    enum Tool { Browse, Dot, Line, Ellipse, Arrow, Freehand, Crop }

    static readonly Tool[] ToolOrder = { Tool.Dot, Tool.Line, Tool.Ellipse, Tool.Arrow, Tool.Freehand, Tool.Crop };
    static readonly string[] ToolGlyph = { "\uE915", "\uE949", "\uEA3F", "\uE74A", "\uE70F", "\uE7A8" };
    static readonly string[] ToolTip = { "标记点 (D)", "直线 (L)", "圆圈 (O)", "箭头 (A)", "手绘 (P)", "裁剪 (C)" };
    static readonly Color[] Swatches =
    {
        Color.FromRgb(0xE8, 0x11, 0x23), Color.FromRgb(0xFF, 0xB9, 0x00),
        Color.FromRgb(0x10, 0x89, 0x3E), Color.FromRgb(0x00, 0x78, 0xD4),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
    };
    static readonly float[] Widths = { 3f, 6f, 10f };

    Canvas _canvas = new();
    Grid _viewGrid = new();
    readonly FrameCapture.Core.UndoRedo<Annotation> _anns = new();
    Tool _tool = Tool.Browse;
    int _colorIdx = 0, _widthIdx = 1;
    readonly List<Button> _toolBtns = new();
    readonly List<Border> _swatchBtns = new();
    readonly List<Button> _widthBtns = new();

    // 进行中的一笔
    Annotation? _pending;
    Shape? _preview;
    Point _pressPos;
    bool _dragging;

    // 时间轴范围
    readonly FrameCapture.Core.RangeDragState _rd = new();
    // Background 必须非 null（透明即可）：null 时画布空白区不参与命中测试，轨道点选/拖动会失效
    Canvas _rangeCanvas = new() { Height = 22, Cursor = Cursors.IBeam, Background = System.Windows.Media.Brushes.Transparent };


    void InitDrawing()
    {
        _canvas.Background = Brushes.Transparent;
        _canvas.Visibility = Visibility.Collapsed;
        _canvas.MouseLeftButtonDown += Canvas_Down;
        _canvas.MouseMove += Canvas_Move;
        _canvas.MouseLeftButtonUp += Canvas_Up;
        _canvas.SizeChanged += (_, _) => { UpdateImgRect(); RedrawAnns(); };

        var rangeBorder = new Border
        {
            Child = _rangeCanvas,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Theme.TrackBg),
        };
        _rangeCanvas.MouseLeftButtonDown += Range_Down;
        _rangeCanvas.MouseMove += Range_Move;
        _rangeCanvas.MouseLeftButtonUp += Range_Up;
        _rangeCanvas.SizeChanged += (_, _) => DrawRange();
        _rangeHost = rangeBorder;
    }

    Border? _rangeHost;

    // 帧内容在 canvas 中的显示矩形（Uniform）
    Rect _imgRect;
    void UpdateImgRect()
    {
        double cw = _canvas.ActualWidth, ch = _canvas.ActualHeight;
        var f = _seq.Frames[_index];
        double s = Math.Min(cw / f.W, ch / f.H);
        double w = f.W * s, h = f.H * s;
        _imgRect = new Rect((cw - w) / 2, (ch - h) / 2, w, h);
    }

    Point Norm(Point p)
    {
        UpdateImgRect();
        double nx = Math.Clamp((p.X - _imgRect.X) / Math.Max(1, _imgRect.Width), 0, 1);
        double ny = Math.Clamp((p.Y - _imgRect.Y) / Math.Max(1, _imgRect.Height), 0, 1);
        return new Point(nx, ny);
    }

    void SetTool(Tool t)
    {
        _tool = t == _tool ? Tool.Browse : t;
        _canvas.Visibility = _tool == Tool.Browse ? Visibility.Collapsed : Visibility.Visible;
        _canvas.Cursor = _tool == Tool.Browse ? Cursors.Arrow : Cursors.Cross;
        for (int i = 0; i < _toolBtns.Count; i++)
            Highlight(_toolBtns[i], ToolOrder[i] == _tool);
    }

    void Highlight(Button b, bool on) => FrameCapture.Core.Fluent.HighlightBtn(b, on);

    static Color Acc => FrameCapture.Core.Theme.Accent;

    // ---------------- 画布交互 ----------------

    void Canvas_Down(object sender, MouseButtonEventArgs e)
    {
        if (_tool == Tool.Browse) return;
        e.Handled = true;
        _canvas.CaptureMouse();
        _pressPos = e.GetPosition(_canvas);
        _dragging = true;
        var n = Norm(_pressPos);
        var color = Color.FromArgb(255, Swatches[_colorIdx].R, Swatches[_colorIdx].G, Swatches[_colorIdx].B);
        float thick = Widths[_widthIdx];
        switch (_tool)
        {
            case Tool.Dot:
                CommitAnn(MakeAnn(AnnKind.Dot, color, thick, new[] { n }));
                _dragging = false;
                break;
            case Tool.Line:
                _pending = MakeAnn(AnnKind.Line, color, thick, new[] { n, n });
                _preview = new Line { Stroke = ToWpf(color), StrokeThickness = DispStroke(thick) };
                break;
            case Tool.Ellipse:
                _pending = MakeAnn(AnnKind.Ellipse, color, thick, new[] { n, n });
                _preview = new Ellipse { Stroke = ToWpf(color), StrokeThickness = DispStroke(thick), Fill = Brushes.Transparent };
                break;
            case Tool.Arrow:
                _pending = MakeAnn(AnnKind.Arrow, color, thick, new[] { n, n });
                _preview = new Path { Stroke = ToWpf(color), StrokeThickness = DispStroke(thick), Fill = ToWpf(color) };
                break;
            case Tool.Freehand:
                _pending = MakeAnn(AnnKind.Freehand, color, thick, new[] { n });
                _preview = new Polyline { Stroke = ToWpf(color), StrokeThickness = DispStroke(thick), StrokeLineJoin = PenLineJoin.Round };
                ((Polyline)_preview).Points.Add(_pressPos);
                break;
            case Tool.Crop:
                _preview = new Rectangle { Stroke = new SolidColorBrush(Acc), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x4C, 0xC2, 0xFF)) };
                _pending = null;
                break;
        }
        if (_preview != null)
        {
            Canvas.SetLeft(_preview, _pressPos.X);
            Canvas.SetTop(_preview, _pressPos.Y);
            _canvas.Children.Add(_preview);
        }
        e.Handled = true;
    }

    void Canvas_Move(object sender, MouseEventArgs e)
    {
        if (!_dragging || _preview == null) return;
        var p = e.GetPosition(_canvas);
        var n = Norm(p);
        // 统一：预览形状存绝对坐标（Canvas 左=0）
        switch (_tool)
        {
            case Tool.Line when _preview is Line ln:
                ln.X1 = _pressPos.X; ln.Y1 = _pressPos.Y; ln.X2 = p.X; ln.Y2 = p.Y;
                break;
            case Tool.Ellipse:
                _preview.Width = Math.Abs(p.X - _pressPos.X);
                _preview.Height = Math.Abs(p.Y - _pressPos.Y);
                Canvas.SetLeft(_preview, Math.Min(_pressPos.X, p.X));
                Canvas.SetTop(_preview, Math.Min(_pressPos.Y, p.Y));
                break;
            case Tool.Arrow:
                DrawArrowPath((Path)_preview, _pressPos, p, DispStroke(Widths[_widthIdx]));
                break;
            case Tool.Freehand when _preview is Polyline pl:
                pl.Points.Add(p);
                if (_pending != null) { var nn = Norm(p); _pending.Points.Add(((float)nn.X, (float)nn.Y)); }
                break;
            case Tool.Crop:
                _preview.Width = Math.Abs(p.X - _pressPos.X);
                _preview.Height = Math.Abs(p.Y - _pressPos.Y);
                Canvas.SetLeft(_preview, Math.Min(_pressPos.X, p.X));
                Canvas.SetTop(_preview, Math.Min(_pressPos.Y, p.Y));
                break;
        }
        if (_pending is { Points.Count: >= 2 } && _tool is Tool.Line or Tool.Ellipse or Tool.Arrow)
            { var nn2 = Norm(p); _pending.Points[1] = ((float)nn2.X, (float)nn2.Y); }
    }

    void Canvas_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _canvas.ReleaseMouseCapture();
        var p = e.GetPosition(_canvas);
        if (_tool == Tool.Crop)
        {
            ApplyCrop(_pressPos, p);
        }
        else if (_pending != null)
        {
            if (_tool is Tool.Line or Tool.Ellipse or Tool.Arrow)
            {
                var n = Norm(p);
                if (_pending.Points.Count >= 2) _pending.Points[1] = ((float)n.X, (float)n.Y);
                double dist = Math.Abs(p.X - _pressPos.X) + Math.Abs(p.Y - _pressPos.Y);
                if (dist < 6) { /* 太短：丢弃 */ }
                else CommitAnn(_pending);
            }
            else CommitAnn(_pending);
        }
        _pending = null;
        if (_preview != null) { _canvas.Children.Remove(_preview); _preview = null; }
        e.Handled = true;
    }

    Annotation MakeAnn(AnnKind k, Color c, float w, IEnumerable<Point> normPts)
    {
        var a = new Annotation { Kind = k, Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B), Thickness = w };
        foreach (var pt in normPts) a.Points.Add(((float)pt.X, (float)pt.Y));
        return a;
    }

    void CommitAnn(Annotation a)
    {
        _anns.Add(a);
        RedrawAnns();
    }

    void RedoAnn()
    {
        if (_anns.Redo(out _)) RedrawAnns();
    }

    void UndoAnn()
    {
        _anns.Undo(out _);
        RedrawAnns();
    }

    double DispStroke(float thickness)
    {
        double m = Math.Max(1, Math.Min(_canvas.ActualWidth, _canvas.ActualHeight));
        return Math.Max(1.2, thickness * m / 1080.0);
    }

    static SolidColorBrush ToWpf(Color c) => new(Color.FromArgb(c.A, c.R, c.G, c.B));

    void DrawArrowPath(Path path, Point a, Point b, double stroke)
    {
        double len = Math.Max(8, stroke * 6);
        double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
        double spread = Math.PI / 7;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            double bx = b.X - len * 0.75 * Math.Cos(ang), by = b.Y - len * 0.75 * Math.Sin(ang);
            ctx.BeginFigure(a, false, false);
            ctx.LineTo(new Point(bx, by), true, false);
        }
        path.Data = geo;
    }

    void RedrawAnns()
    {
        _canvas.Children.Clear();
        foreach (var a in _anns.Items)
        {
            double s = Math.Max(1, Math.Min(_imgRect.Width, _imgRect.Height));
            double stroke = Math.Max(1.2, a.Thickness * s / 1080.0);
            var wpfCol = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a.Color.A, a.Color.R, a.Color.G, a.Color.B));
            Point P(double nx, double ny) => new(_imgRect.X + nx * _imgRect.Width, _imgRect.Y + ny * _imgRect.Height);
            var pts = a.Points.Select(p => P(p.X, p.Y)).ToList();
            switch (a.Kind)
            {
                case AnnKind.Dot when pts.Count > 0:
                {
                    double r = stroke * 2.2;
                    var el = new Ellipse { Width = r * 2, Height = r * 2, Fill = wpfCol };
                    Canvas.SetLeft(el, pts[0].X - r); Canvas.SetTop(el, pts[0].Y - r);
                    _canvas.Children.Add(el);
                    break;
                }
                case AnnKind.Line when pts.Count >= 2:
                    _canvas.Children.Add(new Line { X1 = pts[0].X, Y1 = pts[0].Y, X2 = pts[1].X, Y2 = pts[1].Y, Stroke = wpfCol, StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                    break;
                case AnnKind.Ellipse when pts.Count >= 2:
                {
                    var el = new Ellipse { Stroke = wpfCol, StrokeThickness = stroke };
                    double w = Math.Abs(pts[1].X - pts[0].X), h2 = Math.Abs(pts[1].Y - pts[0].Y);
                    el.Width = Math.Max(2, w); el.Height = Math.Max(2, h2);
                    Canvas.SetLeft(el, Math.Min(pts[0].X, pts[1].X)); Canvas.SetTop(el, Math.Min(pts[0].Y, pts[1].Y));
                    _canvas.Children.Add(el);
                    break;
                }
                case AnnKind.Arrow when pts.Count >= 2:
                {
                    var path = new Path { Stroke = wpfCol, StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round };
                    DrawArrowPath(path, pts[0], pts[1], stroke);
                    _canvas.Children.Add(path);
                    var head = new Polygon { Fill = wpfCol };
                    double len = Math.Max(8, stroke * 6), ang = Math.Atan2(pts[1].Y - pts[0].Y, pts[1].X - pts[0].X), sp = Math.PI / 7;
                    head.Points = new PointCollection
                    {
                        pts[1],
                        new Point(pts[1].X - len * Math.Cos(ang - sp), pts[1].Y - len * Math.Sin(ang - sp)),
                        new Point(pts[1].X - len * Math.Cos(ang + sp), pts[1].Y - len * Math.Sin(ang + sp)),
                    };
                    _canvas.Children.Add(head);
                    break;
                }
                case AnnKind.Freehand when pts.Count >= 2:
                    _canvas.Children.Add(new Polyline { Points = new PointCollection(pts), Stroke = wpfCol, StrokeThickness = stroke, StrokeLineJoin = PenLineJoin.Round });
                    break;
            }
        }
        if (_tool != Tool.Browse && _canvas.Visibility != Visibility.Visible)
            _canvas.Visibility = Visibility.Visible;
    }

    // ---------------- 裁剪 ----------------

    void ApplyCrop(Point a, Point b)
    {
        UpdateImgRect();
        if (_imgRect.Width < 4 || _imgRect.Height < 4) return;
        var f = _seq.Frames[_index];
        int x = (int)Math.Round((Math.Min(a.X, b.X) - _imgRect.X) / _imgRect.Width * f.W);
        int y = (int)Math.Round((Math.Min(a.Y, b.Y) - _imgRect.Y) / _imgRect.Height * f.H);
        int w = (int)Math.Round(Math.Abs(b.X - a.X) / _imgRect.Width * f.W);
        int h = (int)Math.Round(Math.Abs(b.Y - a.Y) / _imgRect.Height * f.H);
        x = Math.Clamp(x, 0, f.W - 1); y = Math.Clamp(y, 0, f.H - 1);
        w = Math.Min(w, f.W - x); h = Math.Min(h, f.H - y);
        if (w < 48 || h < 48) return;

        try
        {
            _seq.Frames = Bake.Crop(_seq.Frames, x, y, w, h);
        }
        catch { return; }

        // 标注：全部丢弃（原全帧坐标已失效，行为可预期）
        _anns.Clear();
        _bitmaps = _seq.Frames.Select(FrameCapture.Services.BgraImage.ToSource).ToList();
        _seq.Region = new RectI(_seq.Region.X + x, _seq.Region.Y + y, w, h);
        RedrawAnns();
        Show(_index);
    }

    // ---------------- 时间轴范围 ----------------

    int _tlMode; // 0 空闲 1 拖动跳转 2 拖范围左端 3 拖范围右端

    void Range_Down(object sender, MouseButtonEventArgs e)
    {
        _rd.Count = _seq.Frames.Count;
        _rd.Width = _rangeCanvas.ActualWidth;
        double x = e.GetPosition(_rangeCanvas).X;
        var (a, b) = TrimRange;
        _tlMode = 1;
        if (_rd.A >= 0 && _seq.Frames.Count > 1)
        {
            double xa = _rd.FrameX(a), xb = _rd.FrameX(b);
            if (Math.Abs(x - xb) <= 10) _tlMode = 3;
            else if (Math.Abs(x - xa) <= 10) _tlMode = 2;
        }
        if (_tlMode == 1)
        {
            if (_playing) TogglePlay();
            Show(Core.RangeDragState.XToIdx(x, _rangeCanvas.ActualWidth, _seq.Frames.Count));
        }
        _rangeCanvas.CaptureMouse();
        e.Handled = true;
    }

    void Range_Move(object sender, MouseEventArgs e)
    {
        double x = e.GetPosition(_rangeCanvas).X;
        if (_tlMode == 0)
        {
            if (_rd.Width != _rangeCanvas.ActualWidth) _rd.Width = _rangeCanvas.ActualWidth;
            var (a, b) = TrimRange;
            bool near = _rd.A >= 0 && _seq.Frames.Count > 1 &&
                (Math.Abs(x - _rd.FrameX(a)) <= 10 || Math.Abs(x - _rd.FrameX(b)) <= 10);
            _rangeCanvas.Cursor = near ? System.Windows.Input.Cursors.SizeWE : Cursors.IBeam;
            return;
        }
        int idx = Core.RangeDragState.XToIdx(x, _rangeCanvas.ActualWidth, _seq.Frames.Count);
        if (_tlMode == 1) Show(idx);
        else
        {
            if (_tlMode == 2) _rd.SetA(idx); else _rd.SetB(idx);
            DrawRange();
        }
        e.Handled = true;
    }

    void Range_Up(object sender, MouseButtonEventArgs e)
    {
        _tlMode = 0;
        _rangeCanvas.ReleaseMouseCapture();
        DrawRange();
        e.Handled = true;
    }

    static System.Windows.Shapes.Rectangle TlBar(double x, double y, double w, double h, double r, Color col)
    {
        var rect = new System.Windows.Shapes.Rectangle { Width = Math.Max(0.5, w), Height = h, RadiusX = r, RadiusY = r, Fill = new SolidColorBrush(col) };
        Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
        return rect;
    }

    void DrawRange()
    {
        var c = _rangeCanvas;
        c.Children.Clear();
        double cw = c.ActualWidth, ch = c.ActualHeight;
        if (cw < 8) return;
        int n = Math.Max(2, _seq.Frames.Count);
        double th = 5, ty = (ch - th) / 2;

        // 底轨 + 已走到位置的填充
        c.Children.Add(TlBar(0, ty, cw, th, 2.5, Theme.TrackBg));
        double xm = _index / (n - 1.0) * cw;
        if (xm > 2) c.Children.Add(TlBar(0, ty, xm, th, 2.5, FrameCapture.Core.Theme.Accent));

        // 裁剪范围：范围外压暗，两端强调色把手
        var (a, b) = TrimRange;
        if (_rd.A >= 0 && b > a)
        {
            double xa = a / (n - 1.0) * cw, xb = b / (n - 1.0) * cw;
            if (xa > 1) c.Children.Add(TlBar(0, ty, xa, th, 2.5, Color.FromArgb(0xB0, 0x14, 0x14, 0x16)));
            if (cw - xb > 1) c.Children.Add(TlBar(xb, ty, cw - xb, th, 2.5, Color.FromArgb(0xB0, 0x14, 0x14, 0x16)));
            c.Children.Add(TlBar(xa - 1.75, ch / 2 - 7, 3.5, 14, 1.75, FrameCapture.Core.Theme.Accent));
            c.Children.Add(TlBar(xb - 1.75, ch / 2 - 7, 3.5, 14, 1.75, FrameCapture.Core.Theme.Accent));
        }

        // 拇指（与填充端点同坐标，不可能分离）
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 13, Height = 13,
            Fill = new SolidColorBrush(FrameCapture.Core.Theme.Accent),
            Stroke = new SolidColorBrush(Color.FromArgb((byte)(Theme.Stroke.A * 0x60 / 0xFF), Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
            StrokeThickness = 1.5,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.55 },
        };
        Canvas.SetLeft(dot, Math.Clamp(xm, 0, cw) - 6.5);
        Canvas.SetTop(dot, ch / 2 - 6.5);
        c.Children.Add(dot);
    }

    (int a, int b) TrimRange { get { _rd.Count = _seq.Frames.Count; return _rd.Effective; } }
}
