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
    enum Tool { Browse, Select, Line, Ellipse, Arrow, Freehand, Text, Mosaic, Crop }

    // 工具顺序与 ToolCatalog.Tools 一一对应（T92 锁死）；图标/提示文案的唯一数据源在 Core.ToolCatalog。
    // ToolOrder 由目录键位派生（R52 教训：手写平行数组+断言=断言写错直接 abort 进程，派生让漂移无处可漂）
    static Tool FromKey(string k) => k switch
    {
        "V" => Tool.Select, "L" => Tool.Line, "O" => Tool.Ellipse, "A" => Tool.Arrow,
        "P" => Tool.Freehand, "T" => Tool.Text, "M" => Tool.Mosaic, "C" => Tool.Crop,
        _ => throw new InvalidOperationException($"工具目录键 {k} 未接 Tool 枚举"),
    };
    static readonly Tool[] ToolOrder = FrameCapture.Core.ToolCatalog.Tools.Select(t => FromKey(t.Key)).ToArray();
    static readonly Color[] Swatches =
    {
        Color.FromRgb(0xE8, 0x11, 0x23), Color.FromRgb(0xFF, 0xB9, 0x00),
        Color.FromRgb(0x10, 0x89, 0x3E), Color.FromRgb(0x00, 0x78, 0xD4),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
    };
    static readonly float[] Widths = { 3f, 6f, 10f };
    // 自定义槽位：_colorIdx == Swatches.Length → 用 _customColor；_widthIdx == Widths.Length → 用 _customWidth
    float _customWidth = 6f;
    Color? _customColor;
    Color CurColor => _colorIdx < Swatches.Length ? Swatches[_colorIdx] : _customColor ?? Swatches[0];
    float CurWidth => FrameCapture.Core.CustomPick.Width(_widthIdx, Widths, _customWidth);

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
    bool _tlHover;


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
        // 悬停提示可裁剪：未裁剪时两端也画出淡把手（PR 式 affordance，不悬停不打扰）
        _rangeCanvas.MouseEnter += (_, _) => { _tlHover = true; DrawRange(); };
        _rangeCanvas.MouseLeave += (_, _) => { _tlHover = false; DrawRange(); };
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
        _sel = null; // 换工具即丢选中（选中只属于选中工具，R52）
        _canvas.Visibility = _tool == Tool.Browse ? Visibility.Collapsed : Visibility.Visible;
        _canvas.Cursor = _tool switch { Tool.Browse or Tool.Select => Cursors.Arrow, Tool.Text => Cursors.IBeam, _ => Cursors.Cross };
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
        if (_tool == Tool.Select)
        {
            // 选中工具（R52）：点标注=选中并可拖移；点空白=取消选中
            var sp = e.GetPosition(_canvas);
            var hit = HitAnn(sp);
            SetSelection(hit);
            if (hit != null)
            {
                _moveDrag = true; _moveAnn = hit; _moveStart = _movePress = sp;
                _canvas.CaptureMouse();
            }
            return;
        }
        if (_tool == Tool.Text)
        {
            // 文字：点哪写哪；点到已有文字 = 重新编辑（R50）
            var tp = e.GetPosition(_canvas);
            var hit = HitText(tp);
            OpenTextInput(hit != null ? NormToDisp(hit.Points[0]) : tp, hit);
            return;
        }
        _canvas.CaptureMouse();
        _pressPos = e.GetPosition(_canvas);
        _dragging = true;
        var n = Norm(_pressPos);
        var c0 = CurColor;
        var color = Color.FromArgb(255, c0.R, c0.G, c0.B);
        float thick = CurWidth;
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
                StartCropGuide();
                break;
            case Tool.Mosaic:
                _pending = null;
                _preview = new Rectangle
                {
                    Stroke = new SolidColorBrush(Acc), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 },
                    Fill = MosaicBrush(GetMosaicRect(_index, CurWidth, (int)_pressPos.X, (int)_pressPos.Y, 2, 2, cache: false), new Rect(0, 0, 1, 1), new Rect(0, 0, 1, 1)),
                };
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
        if (_moveDrag && _sel != null)
        {
            // 拖移=整体平移元素与选择框（提交前不动数据；松手才 Replace，一次拖=一步撤销）
            var mp = e.GetPosition(_canvas);
            double dx = mp.X - _movePress.X, dy = mp.Y - _movePress.Y;
            _movePress = mp;
            foreach (var (el, a) in _annEls)
                if (ReferenceEquals(a, _sel)) { Canvas.SetLeft(el, Canvas.GetLeft(el) + dx); Canvas.SetTop(el, Canvas.GetTop(el) + dy); }
            if (_selBox != null) { Canvas.SetLeft(_selBox, Canvas.GetLeft(_selBox) + dx); Canvas.SetTop(_selBox, Canvas.GetTop(_selBox) + dy); }
            return;
        }
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
                DrawArrowPath((Path)_preview, _pressPos, p, DispStroke(CurWidth));
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
                UpdateCropGuide(_preview);
                break;
            case Tool.Mosaic when _preview is Rectangle mr && mr.Fill is ImageBrush mb:
            {
                double w = Math.Abs(p.X - _pressPos.X), h = Math.Abs(p.Y - _pressPos.Y);
                double x = Math.Min(_pressPos.X, p.X), y = Math.Min(_pressPos.Y, p.Y);
                // 预览位图 = 当前拖框在帧像素上的区域马赛克（块对齐框原点，与烘焙一致）；拖框中不进缓存
                var m0 = Norm(new Point(x, y)); var m1 = Norm(new Point(x + w, y + h));
                var mf = _seq.Frames[_index];
                int rx = (int)Math.Round(m0.X * mf.W), ry = (int)Math.Round(m0.Y * mf.H);
                int rw = Math.Max(1, (int)Math.Round((m1.X - m0.X) * mf.W)), rh = Math.Max(1, (int)Math.Round((m1.Y - m0.Y) * mf.H));
                mr.Fill = MosaicBrush(GetMosaicRect(_index, CurWidth, rx, ry, rw, rh, cache: false), new Rect(0, 0, 1, 1), new Rect(0, 0, Math.Max(1, w), Math.Max(1, h))); // 元素本地坐标系
                _preview.Width = w; _preview.Height = h;
                Canvas.SetLeft(_preview, x); Canvas.SetTop(_preview, y);
                break;
            }
        }
        if (_pending is { Points.Count: >= 2 } && _tool is Tool.Line or Tool.Ellipse or Tool.Arrow)
            { var nn2 = Norm(p); _pending.Points[1] = ((float)nn2.X, (float)nn2.Y); }
    }

    void Canvas_Up(object sender, MouseButtonEventArgs e)
    {
        if (_moveDrag)
        {
            _moveDrag = false;
            _canvas.ReleaseMouseCapture();
            var mp = e.GetPosition(_canvas);
            double dx = mp.X - _moveStart.X, dy = mp.Y - _moveStart.Y;
            var moved = _moveAnn; _moveAnn = null;
            if (moved != null && Math.Abs(dx) + Math.Abs(dy) >= 3)
            {
                var m = new Annotation { Kind = moved.Kind, Color = moved.Color, Thickness = moved.Thickness, Text = moved.Text };
                foreach (var pt in moved.Points)
                    m.Points.Add(((float)Math.Clamp(pt.X + dx / Math.Max(1, _imgRect.Width), 0f, 1f),
                                  (float)Math.Clamp(pt.Y + dy / Math.Max(1, _imgRect.Height), 0f, 1f)));
                if (_anns.Replace(moved, m)) { _order.AnnAdded(); RefreshUndo(); }
                _sel = m;
                FrameCapture.Core.Trace.Log($"editor ann move kind={moved.Kind} d=({dx:F0},{dy:F0})");
            }
            RedrawAnns();
            e.Handled = true;
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        _canvas.ReleaseMouseCapture();
        var p = e.GetPosition(_canvas);
        if (_tool == Tool.Crop)
        {
            ApplyCrop(_pressPos, p);
        }
        else if (_tool == Tool.Mosaic)
        {
            ApplyMosaic(_pressPos, p);
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
        EndCropGuide();
        e.Handled = true;
    }

    /// <summary>丢弃进行中的一笔（Esc 退出绘制态时用：释放捕获、删预览、不提交）。</summary>
    void CancelPendingStroke()
    {
        if (_dragging) { _dragging = false; _canvas.ReleaseMouseCapture(); }
        if (_moveDrag) { _moveDrag = false; _moveAnn = null; _canvas.ReleaseMouseCapture(); }
        _pending = null;
        if (_preview != null) { _canvas.Children.Remove(_preview); _preview = null; }
        EndCropGuide();
    }

    // ---------------- 选中工具（R52）：点选→虚线框；拖移/删除都走操作栈可撤销 ----------------

    Annotation? _sel;
    Rectangle? _selBox;
    bool _moveDrag;
    Annotation? _moveAnn;
    Point _moveStart, _movePress;

    readonly List<(UIElement el, Annotation a)> _annEls = new();

    void SetSelection(Annotation? a) { _sel = a; RedrawAnns(); }

    /// <summary>标注的画布坐标包围盒：文字用元素实测；其余由归一化点折算（Line/Path 几何在元素内部，GetLeft 不可靠）。</summary>
    Rect AnnBDisp(Annotation a)
    {
        if (a.Points.Count == 0) return Rect.Empty;
        if (a.Kind == AnnKind.Text)
            for (int i = _annEls.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_annEls[i].a, a))
                {
                    var el = _annEls[i].el;
                    return new Rect(Canvas.GetLeft(el), Canvas.GetTop(el), Math.Max(6, el.RenderSize.Width), Math.Max(6, el.RenderSize.Height));
                }
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var pt in a.Points)
        {
            var d = NormToDisp((pt.X, pt.Y));
            x0 = Math.Min(x0, d.X); y0 = Math.Min(y0, d.Y); x1 = Math.Max(x1, d.X); y1 = Math.Max(y1, d.Y);
        }
        double infl = a.Kind == AnnKind.Arrow ? Math.Max(10, DispStroke(a.Thickness) * 4) : 6;
        return new Rect(x0 - infl, y0 - infl, Math.Max(8, x1 - x0) + infl * 2, Math.Max(8, y1 - y0) + infl * 2);
    }

    /// <summary>命中测试：后画在上 → 倒序取顶，包围盒外扩 4px 便于点选。</summary>
    Annotation? HitAnn(Point p)
    {
        var items = _anns.Items;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var b = AnnBDisp(items[i]);
            if (b.IsEmpty) continue;
            b.Inflate(4, 4);
            if (b.Contains(p)) return items[i];
        }
        return null;
    }

    void DeleteSelected()
    {
        if (_sel == null) return;
        if (_anns.Remove(_sel)) { _order.AnnAdded(); RefreshUndo(); }
        FrameCapture.Core.Trace.Log($"editor ann delete kind={_sel.Kind}");
        _sel = null;
        RedrawAnns();
    }

    // ---------------- 裁剪引导层（R45）：框外压暗 + 三分网格 + 「松开应用」 ----------------

    System.Windows.Shapes.Path? _cropMask, _cropGrid;
    Border? _cropHint;

    void StartCropGuide()
    {
        _cropMask = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0)), IsHitTestVisible = false };
        _cropGrid = new System.Windows.Shapes.Path
        {
            Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1, IsHitTestVisible = false,
        };
        _cropHint = new Border
        {
            Background = new SolidColorBrush(Acc),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 3),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = "松开应用裁剪", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
        };
        _canvas.Children.Add(_cropMask);
        _canvas.Children.Add(_cropGrid);
        _canvas.Children.Add(_cropHint);
    }

    void UpdateCropGuide(FrameworkElement box)
    {
        if (_cropMask == null || _cropGrid == null || _cropHint == null) return;
        var (x, y, w, h) = (Canvas.GetLeft(box), Canvas.GetTop(box), box.Width, box.Height);
        double cw = _canvas.ActualWidth, ch = _canvas.ActualHeight;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(0, 0), true, true);
            g.LineTo(new Point(cw, 0), true, false); g.LineTo(new Point(cw, ch), true, false); g.LineTo(new Point(0, ch), true, false);
            g.BeginFigure(new Point(x, y), true, true);
            g.LineTo(new Point(x + w, y), true, false); g.LineTo(new Point(x + w, y + h), true, false); g.LineTo(new Point(x, y + h), true, false);
        }
        geo.FillRule = FillRule.EvenOdd;
        _cropMask.Data = geo;
        var grid = new StreamGeometry();
        using (var g = grid.Open())
        {
            for (int i = 1; i <= 2; i++)
            {
                g.BeginFigure(new Point(x + w * i / 3, y), true, false); g.LineTo(new Point(x + w * i / 3, y + h), true, true);
                g.BeginFigure(new Point(x, y + h * i / 3), true, false); g.LineTo(new Point(x + w, y + h * i / 3), true, true);
            }
        }
        _cropGrid.Data = grid;
        // 气泡尺寸未测量前用估算值（CropGuide 纯逻辑负责翻转/钳边决策）
        double hw = _cropHint.ActualWidth > 4 ? _cropHint.ActualWidth : 92, hh = _cropHint.ActualHeight > 4 ? _cropHint.ActualHeight : 22;
        var (hx, hy) = FrameCapture.Core.CropGuide.HintPos(x, y, w, h, hw, hh, cw, ch);
        Canvas.SetLeft(_cropHint, hx); Canvas.SetTop(_cropHint, hy);
    }

    void EndCropGuide()
    {
        if (_cropMask != null) { _canvas.Children.Remove(_cropMask); _cropMask = null; }
        if (_cropGrid != null) { _canvas.Children.Remove(_cropGrid); _cropGrid = null; }
        if (_cropHint != null) { _canvas.Children.Remove(_cropHint); _cropHint = null; }
    }

    // ---------------- 文字标注（R46）：点画布→就地输入→Enter/失焦上屏；R50 点已有文字=再编辑 ----------------

    TextBox? _textInput;
    Point _textPos;
    Annotation? _textEditing;

    /// <summary>命中标注元素矩形（后画在上 → 倒序取顶）。</summary>
    Annotation? HitText(Point p)
    {
        for (int i = _annEls.Count - 1; i >= 0; i--)
        {
            var (el, a) = _annEls[i];
            if (a.Kind != AnnKind.Text) continue;
            var r = new Rect(Canvas.GetLeft(el), Canvas.GetTop(el), Math.Max(8, el.RenderSize.Width), Math.Max(8, el.RenderSize.Height));
            if (r.Contains(p)) return a;
        }
        return null;
    }

    Point NormToDisp((float X, float Y) n) => new(_imgRect.X + n.X * _imgRect.Width, _imgRect.Y + n.Y * _imgRect.Height);

    void OpenTextInput(Point pos, Annotation? editing = null)
    {
        if (_textInput != null) CommitTextInput();
        var f = _seq.Frames[_index];
        // 与上屏 TextBlock 完全同式的字号（R50：此前 clamp 下限不同导致预览/上屏字号错位）
        double disp = FrameCapture.Core.AnnFont.BakePx(editing?.Thickness ?? CurWidth, f.W, f.H) * Math.Max(0.05, _imgRect.Width / Math.Max(1, f.W));
        var box = new TextBox
        {
            MinWidth = 120, MaxWidth = 640,
            FontSize = Math.Clamp(disp, 8, 400),
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(editing != null
                ? System.Windows.Media.Color.FromArgb(editing.Color.A, editing.Color.R, editing.Color.G, editing.Color.B)
                : CurColor),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Acc),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(5, 2, 5, 2),
            AcceptsReturn = false,
        };
        if (editing != null && editing.Points.Count > 0)
        {
            box.Text = editing.Text;
            box.SelectAll();
            _textEditing = editing;
        }
        // 文字起点 = pos：把边框+内边距反向挪掉，输入框里的字与上屏后的字同位（R50 修预览错位）
        Canvas.SetLeft(box, pos.X - 6.5); Canvas.SetTop(box, pos.Y - 3.5);
        _textPos = pos;
        box.PreviewKeyDown += (_, ek) =>
        {
            if (ek.Key == Key.Enter) { CommitTextInput(); ek.Handled = true; }
            else if (ek.Key == Key.Escape) { CancelTextInput(); ek.Handled = true; }
        };
        box.LostFocus += (_, _) => CommitTextInput(); // 点别处 = 视为上屏
        _textInput = box;
        _canvas.Children.Add(box);
        box.Focus();
        System.Windows.Input.Keyboard.Focus(box);
    }

    void CommitTextInput()
    {
        var box = _textInput;
        if (box == null) return;
        _textInput = null;
        string txt = box.Text.Trim();
        _canvas.Children.Remove(box);
        var editing = _textEditing;
        _textEditing = null;
        if (editing != null)
        {
            // 再编辑：空文或无改动 = 保持原样（删除对象随对象编辑子系统一起上）；有改动 = 原位替换，可撤销
            if (txt.Length > 0 && txt != editing.Text)
            {
                var e = new Annotation { Kind = AnnKind.Text, Color = editing.Color, Thickness = editing.Thickness };
                foreach (var p in editing.Points) e.Points.Add(p);
                e.Text = txt;
                if (_anns.Replace(editing, e)) { _order.AnnAdded(); RefreshUndo(); }
                FrameCapture.Core.Trace.Log($"editor text edit len={txt.Length}");
            }
            RedrawAnns();
            return;
        }
        if (txt.Length == 0) return;
        var n = Norm(_textPos);
        var c0 = CurColor;
        var a = MakeAnn(AnnKind.Text, Color.FromArgb(255, c0.R, c0.G, c0.B), CurWidth, new[] { n });
        a.Text = txt;
        CommitAnn(a);
        FrameCapture.Core.Trace.Log($"editor text ann len={txt.Length}");
    }

    void CancelTextInput()
    {
        if (_textInput == null) return;
        _canvas.Children.Remove(_textInput);
        _textInput = null;
        _textEditing = null;
    }

    // ---------------- 马赛克（R47）：拖框实时块均值预览，与烘焙共用 MosaicInPlace ----------------

    readonly Dictionary<(int, float, int, int, int, int), BitmapSource> _mosaicCache = new();
    readonly List<(System.Windows.Shapes.Rectangle el, Annotation a)> _mosaicEls = new();

    /// <summary>标注矩形的帧像素矩形（钳进画布）。</summary>
    (bool Ok, int Rx, int Ry, int Rw, int Rh) MosaicPx(Annotation a)
    {
        if (a.Points.Count < 2) return (false, 0, 0, 0, 0);
        var f = _seq.Frames[_index];
        int rx = (int)Math.Round(Math.Clamp(Math.Min(a.Points[0].X, a.Points[1].X), 0f, 1f) * f.W);
        int ry = (int)Math.Round(Math.Clamp(Math.Min(a.Points[0].Y, a.Points[1].Y), 0f, 1f) * f.H);
        int rw = (int)Math.Round(Math.Clamp(Math.Abs(a.Points[1].X - a.Points[0].X), 0f, 1f) * f.W);
        int rh = (int)Math.Round(Math.Clamp(Math.Abs(a.Points[1].Y - a.Points[0].Y), 0f, 1f) * f.H);
        rw = Math.Min(rw, f.W - rx); rh = Math.Min(rh, f.H - ry);
        return (rw > 0 && rh > 0, rx, ry, rw, rh);
    }

    /// <summary>
    /// 帧的指定区域马赛克位图（R50）：只算区域不算整帧 → 翻帧/播放时逐帧重算也便宜，
    /// 马赛克从「静止贴图」变成「区域滤镜」。块从区域原点起算，与 Bake 完全一致（WYSIWYG，T94 锁）。
    /// </summary>
    BitmapSource GetMosaicRect(int frameIndex, float thickness, int rx, int ry, int rw, int rh, bool cache = true)
    {
        var f = _seq.Frames[frameIndex];
        rx = Math.Clamp(rx, 0, Math.Max(0, f.W - 1)); ry = Math.Clamp(ry, 0, Math.Max(0, f.H - 1));
        rw = Math.Max(1, Math.Min(rw, f.W - rx)); rh = Math.Max(1, Math.Min(rh, f.H - ry));
        var key = (frameIndex, thickness, rx, ry, rw, rh);
        if (cache && _mosaicCache.TryGetValue(key, out var hit)) return hit;
        var buf = new byte[rw * rh * 4];
        for (int row = 0; row < rh; row++)
            Buffer.BlockCopy(f.Bgra, ((ry + row) * f.W + rx) * 4, buf, row * rw * 4, rw * 4);
        FrameCapture.Services.BgraImage.MosaicInPlace(buf, rw, rh, 0, 0, rw, rh,
            FrameCapture.Core.MosaicBlock.Px(thickness, f.W, f.H));
        var src = BitmapSource.Create(rw, rh, 96, 96, PixelFormats.Bgra32, null, buf, rw * 4);
        src.Freeze();
        if (cache)
        {
            if (_mosaicCache.Count > 24) _mosaicCache.Clear(); // 区域小，24 帧也才几十 MB 上限内
            _mosaicCache[key] = src;
        }
        return src;
    }

    /// <summary>翻帧/播放时刷新所有马赛克元素的内容（R50：马赛克是区域滤镜，不是静止贴图）。</summary>
    void UpdateMosaicBrushes()
    {
        if (_mosaicEls.Count == 0) return;
        foreach (var (el, a) in _mosaicEls)
        {
            var (ok, rx, ry, rw, rh) = MosaicPx(a);
            if (!ok) continue;
            el.Fill = MosaicBrush(GetMosaicRect(_index, a.Thickness, rx, ry, rw, rh), new Rect(0, 0, 1, 1),
                new Rect(0, 0, Math.Max(1, el.Width), Math.Max(1, el.Height)));
        }
    }

    static ImageBrush MosaicBrush(BitmapSource src, Rect viewbox, Rect viewport) => new(src)
    {
        Viewbox = viewbox, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
        Viewport = viewport, ViewportUnits = BrushMappingMode.Absolute,
        Stretch = Stretch.Fill, TileMode = TileMode.None,
    };

    void ApplyMosaic(Point a, Point b)
    {
        if (Math.Abs(b.X - a.X) < 8 && Math.Abs(b.Y - a.Y) < 8) return; // 误点不成一笔
        var n0 = Norm(a); var n1 = Norm(b);
        var ann = MakeAnn(AnnKind.Mosaic, Color.FromArgb(255, 0, 0, 0), CurWidth, new[] { n0, n1 });
        CommitAnn(ann);
        FrameCapture.Core.Trace.Log("editor mosaic ann");
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
        if (_anns.Redo(out _)) { _order.AnnStepped(); DropSelIfGone(); RedrawAnns(); RefreshUndo(); }
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
        DropSelIfGone();
        RefreshUndo();
        RedrawAnns();
    }

    void DropSelIfGone() { if (_sel != null && !_anns.Items.Contains(_sel)) _sel = null; }

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
        _mosaicEls.Clear();
        _annEls.Clear();
        _selBox = null;
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
                    _annEls.Add((el, a));
                    break;
                }
                case AnnKind.Line when pts.Count >= 2:
                {
                    var el = new Line { X1 = pts[0].X, Y1 = pts[0].Y, X2 = pts[1].X, Y2 = pts[1].Y, Stroke = wpfCol, StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    _canvas.Children.Add(el);
                    _annEls.Add((el, a));
                    break;
                }
                case AnnKind.Ellipse when pts.Count >= 2:
                {
                    var el = new Ellipse { Stroke = wpfCol, StrokeThickness = stroke };
                    double w = Math.Abs(pts[1].X - pts[0].X), h2 = Math.Abs(pts[1].Y - pts[0].Y);
                    el.Width = Math.Max(2, w); el.Height = Math.Max(2, h2);
                    Canvas.SetLeft(el, Math.Min(pts[0].X, pts[1].X)); Canvas.SetTop(el, Math.Min(pts[0].Y, pts[1].Y));
                    _canvas.Children.Add(el);
                    _annEls.Add((el, a));
                    break;
                }
                case AnnKind.Arrow when pts.Count >= 2:
                {
                    var path = new Path { Stroke = wpfCol, StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round };
                    DrawArrowPath(path, pts[0], pts[1], stroke);
                    _canvas.Children.Add(path);
                    _annEls.Add((path, a));
                    var head = new Polygon { Fill = wpfCol };
                    double len = Math.Max(8, stroke * 6), ang = Math.Atan2(pts[1].Y - pts[0].Y, pts[1].X - pts[0].X), sp = Math.PI / 7;
                    head.Points = new PointCollection
                    {
                        pts[1],
                        new Point(pts[1].X - len * Math.Cos(ang - sp), pts[1].Y - len * Math.Sin(ang - sp)),
                        new Point(pts[1].X - len * Math.Cos(ang + sp), pts[1].Y - len * Math.Sin(ang + sp)),
                    };
                    _canvas.Children.Add(head);
                    _annEls.Add((head, a));
                    break;
                }
                case AnnKind.Freehand when pts.Count >= 2:
                {
                    var el = new Polyline { Points = new PointCollection(pts), Stroke = wpfCol, StrokeThickness = stroke, StrokeLineJoin = PenLineJoin.Round };
                    _canvas.Children.Add(el);
                    _annEls.Add((el, a));
                    break;
                }
                case AnnKind.Mosaic when pts.Count >= 2:
                {
                    var vp = new Rect(Math.Min(pts[0].X, pts[1].X), Math.Min(pts[0].Y, pts[1].Y),
                        Math.Max(1, Math.Abs(pts[1].X - pts[0].X)), Math.Max(1, Math.Abs(pts[1].Y - pts[0].Y)));
                    var (ok, rx, ry, rw, rh) = MosaicPx(a);
                    if (!ok) break;
                    // 区域位图铺满元素：Viewbox 全幅，Viewport 是元素本地坐标 (0,0,w,h)，画布定位交给 Canvas.SetLeft/Top
                    var el = new Rectangle { Fill = MosaicBrush(GetMosaicRect(_index, a.Thickness, rx, ry, rw, rh), new Rect(0, 0, 1, 1), new Rect(0, 0, vp.Width, vp.Height)), Width = vp.Width, Height = vp.Height };
                    Canvas.SetLeft(el, vp.X); Canvas.SetTop(el, vp.Y);
                    _canvas.Children.Add(el);
                    _mosaicEls.Add((el, a));
                    _annEls.Add((el, a));
                    break;
                }
                case AnnKind.Text when pts.Count > 0 && !string.IsNullOrEmpty(a.Text):
                {
                    var f = _seq.Frames[_index];
                    double fs = FrameCapture.Core.AnnFont.BakePx(a.Thickness, f.W, f.H) * Math.Max(0.05, _imgRect.Width / Math.Max(1, f.W));
                    var tb = new TextBlock
                    {
                        Text = a.Text,
                        FontSize = Math.Clamp(fs, 8, 400),
                        FontWeight = FontWeights.Bold,
                        Foreground = wpfCol,
                    };
                    Canvas.SetLeft(tb, pts[0].X); Canvas.SetTop(tb, pts[0].Y);
                    _canvas.Children.Add(tb);
                    _annEls.Add((tb, a));
                    break;
                }
            }
        }
        // 选中框（R52）：虚线强调色，纯装饰不吃命中
        if (_sel != null && _anns.Items.Contains(_sel))
        {
            var b = AnnBDisp(_sel);
            if (!b.IsEmpty)
            {
                _selBox = new Rectangle
                {
                    Stroke = new SolidColorBrush(Acc), StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false,
                    Width = Math.Max(10, b.Width), Height = Math.Max(10, b.Height),
                };
                Canvas.SetLeft(_selBox, b.X); Canvas.SetTop(_selBox, b.Y);
                _canvas.Children.Add(_selBox);
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
    /// <summary>导出单帧（复制/存帧用）。R46 修复：所见即所得——裁剪→重映射→烘焙，与 GIF 确定同管线。</summary>
    FrameCapture.Capture.CapturedFrame ExportFrame(int index)
    {
        var f = _seq.Frames[index];
        var frames = new List<FrameCapture.Capture.CapturedFrame> { f };
        var anns = _anns.Items.ToList();
        if (_crop.IsCropped)
        {
            var (cx, cy, cw, ch) = _crop.ToPixel(f.W, f.H);
            frames = FrameCapture.Drawing.Bake.Crop(frames, cx, cy, cw, ch);
            anns = FrameCapture.Core.CropRemap.Remap(anns, (double)cx / f.W, (double)cy / f.H, (double)cw / f.W, (double)ch / f.H);
        }
        return FrameCapture.Drawing.Bake.Apply(frames, anns)[0];
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
        int fps = (int)Math.Clamp(_settings.Current.Fps, Core.AppSettings.FpsMin, Core.AppSettings.FpsMax);
        double px = cw / (n - 1.0);
        var tick = Color.FromArgb(0xC8, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B);

        // PR 式刻度尺：逐帧小刻度（太密则抽稀）；长刻度+秒标步长走 Core.TimelineScale 决策表
        int step = px >= 3 ? 1 : Math.Max(1, (int)Math.Ceiling(3.0 / px));
        int fStep = Core.TimelineScale.LabelStepFrames(px, fps);
        for (int i = 0; i < n; i += step)
            c.Children.Add(TlBar(i * px, 13, 1, 6, 0.5, tick));
        for (int i = fStep; i < n; i += fStep)
        {
            double x = i * px;
            c.Children.Add(TlBar(x, 9, 1, 10, 0.5, tick));
            var tb = new TextBlock
            {
                Text = (i / (double)fps).ToString("0.##") + "s",
                FontSize = 8.5,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromArgb(0xB0, Theme.Text.R, Theme.Text.G, Theme.Text.B)),
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, Math.Clamp(x - tb.DesiredSize.Width / 2, 0, Math.Max(0, cw - tb.DesiredSize.Width)));
            Canvas.SetTop(tb, 0);
            c.Children.Add(tb);
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
        else if (_tlHover)
        {
            c.Children.Add(TlBar(1.5, 2, 3, ch - 4, 1.5, Color.FromArgb(0x8C, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)));
            c.Children.Add(TlBar(cw - 4.5, 2, 3, ch - 4, 1.5, Color.FromArgb(0x8C, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)));
        }

        // 播放头：主题色竖线 + 顶部三角（PR 指针）——浅色主题下白色会隐身（R19 逐帧审查抓到），跟 Theme.Text 走
        var ph = Color.FromArgb(0xFF, Theme.Text.R, Theme.Text.G, Theme.Text.B);
        c.Children.Add(TlBar(xm - 0.75, 4, 1.5, ch - 4, 0.75, ph));
        var tri = new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection { new Point(0, 0), new Point(7, 0), new Point(3.5, 4.5) },
            Fill = new SolidColorBrush(ph),
        };
        Canvas.SetLeft(tri, xm - 3.5);
        Canvas.SetTop(tri, 0);
        c.Children.Add(tri);
    }

    (int a, int b) TrimRange { get { _rd.Count = _seq.Frames.Count; return _rd.Effective; } }
}
