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

// 绘图工具层：线/圆/箭头/手绘/裁剪 + 撤销 + 时间轴范围
public sealed partial class EditorWindow
{
    enum Tool { Browse, Line, Ellipse, Arrow, Freehand, Crop }

    static readonly Tool[] ToolOrder = { Tool.Line, Tool.Ellipse, Tool.Arrow, Tool.Freehand, Tool.Crop };
    // 面性图标（GlyphProbe 渲染矩阵人工定稿）：线 E949 / 圆 EA3B / 箭头 F0AF / 手绘 E70F / 裁剪 E7A8
    static readonly string[] ToolGlyph = { "\uE949", "\uEA3B", "\uF0AF", "\uE70F", "\uE7A8" };
    static readonly string[] ToolTip = { "直线 (L)", "圆圈 (O)", "箭头 (A)", "手绘 (P)", "裁剪 (C)" };
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
    Canvas _rangeCanvas = new() { Height = 26, Cursor = Cursors.IBeam, Background = System.Windows.Media.Brushes.Transparent };


    void InitDrawing()
    {
        _canvas.Background = Brushes.Transparent;
        _canvas.Visibility = Visibility.Collapsed;
        _canvas.MouseLeftButtonDown += Canvas_Down;
        _canvas.MouseMove += Canvas_Move;
        _canvas.MouseLeftButtonUp += Canvas_Up;
        // 视图几何唯一驱动：_viewGrid（浏览态 canvas 是 Collapsed，不能靠它）
        _viewGrid.SizeChanged += (_, _) => { UpdateImgRect(); RedrawAnns(); };

        // 缩放：Ctrl+滚轮以光标为锚（真实用户路径）；平移：中键拖（任意工具）/ 浏览态放大后左键拖
        _viewGrid.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            var p = e.GetPosition(_viewGrid);
            SetZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), p.X, p.Y);
            e.Handled = true;
        };
        _viewGrid.MouseDown += Pan_Down;
        _viewGrid.MouseMove += Pan_Move;
        _viewGrid.MouseUp += Pan_Up;

        var rangeBorder = new Border { Child = _rangeCanvas };
        _rangeCanvas.MouseLeftButtonDown += Range_Down;
        _rangeCanvas.MouseMove += Range_Move;
        _rangeCanvas.MouseLeftButtonUp += Range_Up;
        _rangeCanvas.SizeChanged += (_, _) => DrawRange();
        _rangeHost = rangeBorder;
    }

    Border? _rangeHost;

    // 帧内容在 canvas 中的显示矩形（裁剪子区 × 适配 × 缩放 + 平移）——标注命中/绘制、Image 布局、导出折算共用这一事实源
    Rect _imgRect;
    double _zoom = 1.0, _panX, _panY;
    readonly FrameCapture.Core.CropState _crop = new();
    readonly FrameCapture.Core.ActionOrder _order = new();
    void UpdateImgRect()
    {
        // 用 _viewGrid 尺寸：_canvas 在浏览态 Collapsed（ActualWidth 恒 0），而 Image 任何状态都要跟随
        double cw = _viewGrid.ActualWidth, ch = _viewGrid.ActualHeight;
        var f = _seq.Frames[_index];
        var (fw, fh) = CropPx(f.W, f.H); // 裁剪子区像素尺寸：适配/钳制都以"可见区"为准
        double sFit = FrameCapture.Core.ZoomMath.FitScale(cw, ch, fw, fh);
        double s = sFit * _zoom;
        var (px, py) = FrameCapture.Core.ZoomMath.ClampPan(_panX, _panY, cw, ch, fw * s, fh * s);
        _panX = px; _panY = py; // 视口变小后自动回钳
        // 全帧显示矩形：其"裁剪子区"恰落在视口中心 + pan
        var c = _crop.Current;
        double w = f.W * s, h = f.H * s;
        _imgRect = new Rect(cw / 2 - (c.X + c.W / 2) * s + px - w / 2, ch / 2 - (c.Y + c.H / 2) * s + py - h / 2, w, h);
        ApplyImageView();
    }

    (double W, double H) CropPx(int fw, int fh)
    {
        var c = _crop.Current;
        return (Math.Max(1, c.W * fw), Math.Max(1, c.H * fh));
    }

    // Image 跟随 _imgRect：Fill + 显式尺寸/偏移；放大时最近邻（像素硬边，1:1 逐像素），适配时高质量
    void ApplyImageView()
    {
        _view.HorizontalAlignment = HorizontalAlignment.Left;
        _view.VerticalAlignment = VerticalAlignment.Top;
        _view.Width = _imgRect.Width;
        _view.Height = _imgRect.Height;
        _view.Margin = new Thickness(_imgRect.X, _imgRect.Y, 0, 0);
        RenderOptions.SetBitmapScalingMode(_view, _zoom > 1.001 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        // 裁剪=视图状态：帧数据完整，仅把裁剪子区之外的内容裁掉（Image 与标注层同容器同裁剪）
        var c = _crop.Current;
        if (_crop.IsCropped)
            _viewGrid.Clip = new RectangleGeometry(new Rect(
                _imgRect.X + c.X * _imgRect.Width, _imgRect.Y + c.Y * _imgRect.Height,
                c.W * _imgRect.Width, c.H * _imgRect.Height));
        else _viewGrid.Clip = null;
    }

    // ---------------- 缩放 / 平移 ----------------

    void SetZoom(double newZoom, double cx, double cy)
    {
        var f = _seq.Frames[_index];
        var (fw, fh) = CropPx(f.W, f.H);
        var (z, px, py) = FrameCapture.Core.ZoomMath.ZoomAt(_zoom, _panX, _panY, newZoom, cx, cy,
            _viewGrid.ActualWidth, _viewGrid.ActualHeight, fw, fh);
        if (Math.Abs(z - _zoom) < 1e-9 && Math.Abs(px - _panX) < 1e-9 && Math.Abs(py - _panY) < 1e-9) return;
        _zoom = z; _panX = px; _panY = py;
        UpdateImgRect(); RedrawAnns(); Show(_index);
        FrameCapture.Core.Trace.Log($"editor zoom={_zoom:F2} pan={_panX:F0},{_panY:F0}");
    }
    void ZoomStep(double factor) => SetZoom(_zoom * factor, _viewGrid.ActualWidth / 2, _viewGrid.ActualHeight / 2);
    void ZoomFit() => SetZoom(FrameCapture.Core.ZoomMath.MinZoom, _viewGrid.ActualWidth / 2, _viewGrid.ActualHeight / 2);
    void ZoomOneToOne()
    {
        var f = _seq.Frames[_index];
        var (fw, fh) = CropPx(f.W, f.H);
        double sFit = FrameCapture.Core.ZoomMath.FitScale(_viewGrid.ActualWidth, _viewGrid.ActualHeight, fw, fh);
        SetZoom(FrameCapture.Core.ZoomMath.OneToOneZoom(sFit), _viewGrid.ActualWidth / 2, _viewGrid.ActualHeight / 2);
    }

    bool _panning;
    Point _panPress;
    double _panPressX, _panPressY;
    void Pan_Down(object sender, MouseButtonEventArgs e)
    {
        bool mid = e.ChangedButton == MouseButton.Middle;
        bool browseLeft = _tool == Tool.Browse && e.ChangedButton == MouseButton.Left && _zoom > 1.001;
        if (!mid && !browseLeft) return;
        _panning = true;
        _panPress = e.GetPosition(_viewGrid);
        _panPressX = _panX; _panPressY = _panY;
        _viewGrid.CaptureMouse();
        Cursor = System.Windows.Input.Cursors.SizeAll;
        e.Handled = true;
    }
    void Pan_Move(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetPosition(_viewGrid);
        var f = _seq.Frames[_index];
        var (fw, fh) = CropPx(f.W, f.H);
        double sFit = FrameCapture.Core.ZoomMath.FitScale(_viewGrid.ActualWidth, _viewGrid.ActualHeight, fw, fh);
        var (x, y) = FrameCapture.Core.ZoomMath.PanBy(_panPressX, _panPressY, p.X - _panPress.X, p.Y - _panPress.Y,
            _viewGrid.ActualWidth, _viewGrid.ActualHeight, fw * sFit * _zoom, fh * sFit * _zoom);
        _panX = x; _panY = y;
        UpdateImgRect(); RedrawAnns();
    }
    void Pan_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        _viewGrid.ReleaseMouseCapture();
        Cursor = null;
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
        // 再点当前工具=回到浏览态（与线宽/颜色的单选切换同一语义，纯逻辑见 ToolToggle，SelfTest T72a）
        _tool = FrameCapture.Core.ToolToggle.Select(_tool, t, Tool.Browse);
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

    /// <summary>丢弃进行中的一笔（Esc 退出绘制态时用：释放捕获、删预览、不提交）。</summary>
    void CancelPendingStroke()
    {
        if (_dragging) { _dragging = false; _canvas.ReleaseMouseCapture(); }
        _pending = null;
        if (_preview != null) { _canvas.Children.Remove(_preview); _preview = null; }
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
        _order.AnnAdded(); // 新动作使裁剪 redo 失效（双流统一撤销语义）
        RefreshUndo();
        RedrawAnns();
    }

    void RedoAnn()
    {
        if (_order.RedoPopsCrop(_anns.CanRedo))
        {
            _crop.RedoCrop(); _order.CropRedone();
            UpdateImgRect(); RedrawAnns(); Show(_index); RefreshUndo();
            FrameCapture.Core.Trace.Log($"editor crop redo depth={_crop.Depth}");
            return;
        }
        if (_anns.Redo(out _)) { _order.AnnStepped(); RedrawAnns(); RefreshUndo(); }
    }

    void UndoAnn()
    {
        if (_order.UndoPopsCrop(_anns.CanUndo))
        {
            _crop.Pop(); _order.CropUndone();
            UpdateImgRect(); RedrawAnns(); Show(_index); RefreshUndo();
            FrameCapture.Core.Trace.Log($"editor crop undo depth={_crop.Depth}");
            return;
        }
        if (!_anns.CanUndo) return;
        _anns.Undo(out _);
        _order.AnnStepped();
        RefreshUndo();
        RedrawAnns();
    }

    double DispStroke(float thickness)
    {
        // 与烘焙严格同式：thickness × min(帧W,帧H)/1080（帧像素）× 当前显示倍率 → 任何缩放下所见即所烘焙
        var f = _seq.Frames[_index];
        double eff = _imgRect.Width / Math.Max(1, f.W);
        return Math.Max(1.2, thickness * Math.Min(f.W, f.H) / 1080.0 * eff);
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
        var n0 = Norm(a); var n1 = Norm(b); // 视口→全帧归一化（缩放/平移/已裁剪状态自动卷入）
        // 最小 48 帧像素折算为归一化比例（宽高各按自身维度）
        if (!_crop.Push(n0.X, n0.Y, n1.X, n1.Y, 48.0 / f.W, 48.0 / f.H)) return;
        _order.CropPushed();
        UpdateImgRect(); RedrawAnns(); Show(_index); RefreshUndo();
        var (cx, cy, cw, ch) = _crop.ToPixel(f.W, f.H);
        FrameCapture.Core.Trace.Log($"editor crop depth={_crop.Depth} px={cx},{cy} {cw}x{ch}");
    }

    /// <summary>导出收口：裁剪是视图状态，仅在导出瞬间一次性折算（T78c 已证 Crop→Remap→Bake ≡ Bake→Crop 逐字节）。</summary>
    List<FrameCapture.Capture.CapturedFrame> CropFramesForExport(IReadOnlyList<FrameCapture.Capture.CapturedFrame> frames)
    {
        if (!_crop.IsCropped || frames.Count == 0) return frames.ToList();
        var (x, y, w, h) = _crop.ToPixel(frames[0].W, frames[0].H);
        return FrameCapture.Drawing.Bake.Crop(frames, x, y, w, h);
    }
    List<Annotation> RemapAnnsForExport(IReadOnlyList<Annotation> anns, int fw, int fh)
    {
        if (!_crop.IsCropped) return anns.ToList();
        var (x, y, w, h) = _crop.ToPixel(fw, fh);
        return FrameCapture.Core.CropRemap.Remap(anns, (double)x / fw, (double)y / fh, (double)w / fw, (double)h / fh);
    }
    /// <summary>单帧导出（保存帧/复制帧）：与 GIF 同一裁剪收口。</summary>
    FrameCapture.Capture.CapturedFrame ExportFrame(int index) => CropFramesForExport(new[] { _seq.Frames[index] })[0];

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
        int fps = (int)Math.Clamp(_settings.Current.Fps, Core.AppSettings.FpsMin, Core.AppSettings.FpsMax);
        double px = cw / (n - 1.0);
        var tick = Color.FromArgb(0xC8, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B);

        // PR 式刻度尺：逐帧小刻度（太密则抽稀），整秒长刻度 + 秒标
        int step = px >= 3 ? 1 : Math.Max(1, (int)Math.Ceiling(3.0 / px));
        for (int i = 0; i < n; i += step)
        {
            double x = i * px;
            bool major = i % fps == 0;
            c.Children.Add(TlBar(x, major ? 9 : 13, 1, major ? 10 : 6, 0.5, tick));
            if (major && i > 0)
            {
                var tb = new TextBlock
                {
                    Text = $"{i / fps}s",
                    FontSize = 8.5,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xB0, Theme.Text.R, Theme.Text.G, Theme.Text.B)),
                };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(tb, Math.Clamp(x - tb.DesiredSize.Width / 2, 0, Math.Max(0, cw - tb.DesiredSize.Width)));
                Canvas.SetTop(tb, 0);
                c.Children.Add(tb);
            }
        }

        // 底轨 + 已走到位置的填充
        double ty = ch - 5;
        c.Children.Add(TlBar(0, ty, cw, 4, 2, Theme.TrackBg));
        double xm = _index / (n - 1.0) * cw;
        if (xm > 1) c.Children.Add(TlBar(0, ty, xm, 4, 2, FrameCapture.Core.Theme.Accent));

        // 裁剪范围：范围外整列压暗，两端强调色把手（通高，PR 入/出点感）
        var (a, b) = TrimRange;
        if (_rd.A >= 0 && b > a)
        {
            double xa = a * px, xb = b * px;
            if (xa > 1) c.Children.Add(TlBar(0, 0, xa, ch, 0, Color.FromArgb(0x99, 0x0A, 0x0A, 0x0C)));
            if (cw - xb > 1) c.Children.Add(TlBar(xb, 0, cw - xb, ch, 0, Color.FromArgb(0x99, 0x0A, 0x0A, 0x0C)));
            c.Children.Add(TlBar(xa - 1.5, 2, 3, ch - 4, 1.5, FrameCapture.Core.Theme.Accent));
            c.Children.Add(TlBar(xb - 1.5, 2, 3, ch - 4, 1.5, FrameCapture.Core.Theme.Accent));
        }

        // 播放头：白色竖线 + 顶部三角（PR 指针），与填充端点同坐标不可能分离
        c.Children.Add(TlBar(xm - 0.75, 4, 1.5, ch - 4, 0.75, Colors.White));
        var tri = new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection { new Point(0, 0), new Point(7, 0), new Point(3.5, 4.5) },
            Fill = Brushes.White,
        };
        Canvas.SetLeft(tri, xm - 3.5);
        Canvas.SetTop(tri, 0);
        c.Children.Add(tri);
    }

    (int a, int b) TrimRange { get { _rd.Count = _seq.Frames.Count; return _rd.Effective; } }
}
