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

// 本 partial 职责：画布鼠标交互（绘制一笔/Shift 约束/取消落笔）与选中工具（点选/拖移/四角缩放/字号步进）。
public sealed partial class EditorWindow
{
    #region 画布交互

    void Canvas_Down(object sender, MouseButtonEventArgs e)
    {
        if (_tool == Tool.Browse) return;
        e.Handled = true;
        if (_tool == Tool.Select)
        {
            // 选中工具：点标注=选中并可拖移；点空白=取消选中
            var sp = e.GetPosition(_canvas);
            // 先试选中框四角缩放手柄：命中即进入缩放拖拽，优先于移动/重选
            if (_sel != null && _anns.Items.Contains(_sel) && TryBeginResize(sp)) return;
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
            // 文字：点哪写哪；点到已有文字 = 重新编辑
            var tp = e.GetPosition(_canvas);
            var hit = HitText(tp);
            OpenTextInput(hit != null ? NormToDisp(hit.Points[0]) : tp, hit);
            return;
        }
        if (_tool == Tool.Number)
        {
            // 序号：点哪放哪，编号 = 现有最大 + 1（删除/撤销后自动回退，绝不重号）
            var np = e.GetPosition(_canvas);
            var nc = CurColor;
            var a = MakeAnn(AnnKind.Number, Color.FromArgb(255, nc.R, nc.G, nc.B), CurWidth, new[] { Norm(np) });
            a.Text = FrameCapture.Core.NumberBadge.Next(
                _anns.Items.Where(x => x.Kind == AnnKind.Number).Select(x => x.Text)).ToString();
            CommitAnn(a);
            FrameCapture.Core.Trace.Log($"editor number #{a.Text}");
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
            case Tool.Rect:
                _pending = MakeAnn(AnnKind.Rect, color, thick, new[] { n, n });
                _preview = new Rectangle { Stroke = ToWpf(color), StrokeThickness = DispStroke(thick), Fill = Brushes.Transparent };
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
            case Tool.Highlight:
                // 荧光笔：马克笔宽度（线宽挡位 ×2.5）+ 半透明预览 + 平头帽；标注色仍存不透明，alpha 由类型统一施加
                float ht = thick * 2.5f;
                _pending = MakeAnn(AnnKind.Highlight, color, ht, new[] { n });
                _preview = new Polyline
                {
                    Stroke = ToWpf(Color.FromArgb(FrameCapture.Drawing.Annotation.HighlightAlpha, c0.R, c0.G, c0.B)),
                    StrokeThickness = DispStroke(ht),
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Flat,
                    StrokeEndLineCap = PenLineCap.Flat,
                };
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
            // Line/Arrow/Freehand/Highlight 的形状内部用绝对画布坐标（X1/Y1、Points、Path 几何），
            // 元素须留在画布原点(0,0)——与提交态渲染一致；若按 _pressPos 落点会被二次偏移→预览跑到屏幕外（修）。
            // Rect/Ellipse/Crop/Mosaic 用相对 Width/Height，位置在 Canvas_Move 里按包围盒重设，这里先落 _pressPos 无妨。
            if (_tool is not (Tool.Line or Tool.Arrow or Tool.Freehand or Tool.Highlight))
            {
                Canvas.SetLeft(_preview, _pressPos.X);
                Canvas.SetTop(_preview, _pressPos.Y);
            }
            _canvas.Children.Add(_preview);
        }
        e.Handled = true;
    }

    void Canvas_Move(object sender, MouseEventArgs e)
    {
        if (_resizeDrag && _resizeAnn != null && _resizeOrigPts != null)
        {
            ResizeTo(e.GetPosition(_canvas));   // 实时缩放预览（标注 + 选中框一起跟手）
            return;
        }
        if (_moveDrag && _sel != null)
        {
            // 拖移=整体平移元素与选择框（提交前不动数据；松手才 Replace，一次拖=一步撤销）
            var mp = e.GetPosition(_canvas);
            double dx = mp.X - _movePress.X, dy = mp.Y - _movePress.Y;
            _movePress = mp;
            // Line/Arrow/Freehand/Highlight 元素用绝对内部坐标、未设 Canvas.Left/Top → GetLeft 返回 NaN，
            // 直接 +dx 会把元素甩到 NaN（屏幕外）。NaN 视作 0（元素本就在原点），整体平移才正确（修）。
            static double CL(UIElement el) => double.IsNaN(Canvas.GetLeft(el)) ? 0 : Canvas.GetLeft(el);
            static double CT(UIElement el) => double.IsNaN(Canvas.GetTop(el)) ? 0 : Canvas.GetTop(el);
            foreach (var (el, a) in _annEls)
                if (ReferenceEquals(a, _sel)) { Canvas.SetLeft(el, CL(el) + dx); Canvas.SetTop(el, CT(el) + dy); }
            if (_selBox != null) { Canvas.SetLeft(_selBox, CL(_selBox) + dx); Canvas.SetTop(_selBox, CT(_selBox) + dy); }
            return;
        }
        if (_tool == Tool.Select) { UpdateResizeCursor(e.GetPosition(_canvas)); return; }   // 悬停角手柄→缩放光标
        if (!_dragging || _preview == null) return;
        var p = Constrain(e.GetPosition(_canvas));
        var n = Norm(p);
        // 统一：预览形状存绝对坐标（Canvas 左=0）
        switch (_tool)
        {
            case Tool.Line when _preview is Line ln:
                ln.X1 = _pressPos.X; ln.Y1 = _pressPos.Y; ln.X2 = p.X; ln.Y2 = p.Y;
                break;
            case Tool.Rect:
            case Tool.Ellipse:
                _preview.Width = Math.Abs(p.X - _pressPos.X);
                _preview.Height = Math.Abs(p.Y - _pressPos.Y);
                Canvas.SetLeft(_preview, Math.Min(_pressPos.X, p.X));
                Canvas.SetTop(_preview, Math.Min(_pressPos.Y, p.Y));
                break;
            case Tool.Arrow:
                DrawArrowPath((Path)_preview, _pressPos, p, DispStroke(CurWidth));
                break;
            case Tool.Freehand or Tool.Highlight when _preview is Polyline pl:
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
        if (_pending is { Points.Count: >= 2 } && _tool is Tool.Line or Tool.Rect or Tool.Ellipse or Tool.Arrow)
            { var nn2 = Norm(p); _pending.Points[1] = ((float)nn2.X, (float)nn2.Y); }
    }

    /// <summary>Shift 约束绘制：矩形/圆→正方/正圆，线/箭头→45° 吸附；其余原样。预览与提交两处共用，保证所见即所得。</summary>
    Point Constrain(Point p)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return p;
        if (_tool is Tool.Rect or Tool.Ellipse)
        { var (x, y) = FrameCapture.Core.ShapeConstraint.Square(_pressPos.X, _pressPos.Y, p.X, p.Y); return new Point(x, y); }
        if (_tool is Tool.Line or Tool.Arrow)
        { var (x, y) = FrameCapture.Core.ShapeConstraint.Angle45(_pressPos.X, _pressPos.Y, p.X, p.Y); return new Point(x, y); }
        return p;
    }

    void Canvas_Up(object sender, MouseButtonEventArgs e)
    {
        if (_resizeDrag)
        {
            _resizeDrag = false;
            _canvas.ReleaseMouseCapture();
            var ann = _resizeAnn; var orig = _resizeOrigPts;
            _resizeAnn = null; _resizeOrigPts = null;
            if (ann != null && orig != null)
            {
                var scaled = new List<(float X, float Y)>(ann.Points);   // 拖拽后的当前几何
                bool changed = false;
                for (int i = 0; i < scaled.Count && i < orig.Count; i++)
                    if (Math.Abs(scaled[i].X - orig[i].X) > 1e-3 || Math.Abs(scaled[i].Y - orig[i].Y) > 1e-3) { changed = true; break; }
                // 还原原标注几何（Replace 按引用换，原对象须保持原样），再换成缩放副本，记一步撤销
                ann.Points.Clear(); foreach (var pt0 in orig) ann.Points.Add(pt0);
                if (changed)
                {
                    var m = new Annotation { Kind = ann.Kind, Color = ann.Color, Thickness = ann.Thickness, Text = ann.Text };
                    foreach (var pt1 in scaled) m.Points.Add(pt1);
                    if (_anns.Replace(ann, m)) { _order.AnnAdded(); RefreshUndo(); }
                    _sel = m;
                    FrameCapture.Core.Trace.Log($"editor ann resize kind={ann.Kind}");
                }
            }
            RedrawAnns();
            e.Handled = true;
            return;
        }
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
        var p = Constrain(e.GetPosition(_canvas));
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
            if (_tool is Tool.Line or Tool.Rect or Tool.Ellipse or Tool.Arrow)
            {
                var n = Norm(p);
                if (_pending.Points.Count >= 2) _pending.Points[1] = ((float)n.X, (float)n.Y);
                double dist = Math.Abs(p.X - _pressPos.X) + Math.Abs(p.Y - _pressPos.Y);
                if (dist < 6) { /* 太短：丢弃 */ }
                else CommitAnn(_pending);
            }
            else CommitAnn(_pending); // 手绘/荧光笔：单击也提交（预览已修，点下去实时可见，不静默丢弃）
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
    #endregion

    #region 选中工具：点选→虚线框；拖移/删除都走操作栈可撤销

    Annotation? _sel;
    Rectangle? _selBox;
    bool _moveDrag;
    Annotation? _moveAnn;
    Point _moveStart, _movePress;
    // 缩放手柄：选中框四角拖拽改大小。归一化点集对所有标注类型通用缩放。
    bool _resizeDrag;
    int _resizeCorner;                              // 0=TL 1=TR 2=BR 3=BL
    Annotation? _resizeAnn;
    List<(float X, float Y)>? _resizeOrigPts;
    Rect _resizeOrigBox;                            // 原始点集归一化包围盒

    readonly List<(UIElement el, Annotation a)> _annEls = new();

    void SetSelection(Annotation? a) { _sel = a; RedrawAnns(); UpdateFontStepper(); }
    #endregion

    #region 字号步进器（C3）：文字工具/选中文字时可见，A−/A+ 调字号
    bool TextRelevant => _tool == Tool.Text || _sel is { Kind: AnnKind.Text or AnnKind.Number };
    bool SelIsText => _sel is { Kind: AnnKind.Text or AnnKind.Number };

    void StepFontSize(int dir)
    {
        float cur = SelIsText ? _sel!.Thickness : CurWidth;
        float nw = Math.Clamp(cur + dir, 1f, 24f);
        if (Math.Abs(nw - cur) < 1e-6) return;   // 已到上下限
        ApplyWidth(nw);                            // 落到宽度槽位（下次文字/绘制沿用）
        if (SelIsText) RestyleSelected(null, nw);  // 选中文字：原位改字号（可撤销）
        UpdateFontStepper();
    }

    void UpdateFontStepper()
    {
        if (_fontGroup == null || _fontLabel == null) return;
        bool show = TextRelevant;
        _fontGroup.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            float t = SelIsText ? _sel!.Thickness : CurWidth;
            _fontLabel.Text = $"{(int)FrameCapture.Core.AnnFont.FramePx(t)}px";
        }
    }
    #endregion

    #region 缩放手柄：选中框四角拖拽，按对角锚点等比缩放归一化点集（对所有标注类型通用）

    /// <summary>点集归一化包围盒（不含 AnnBDisp 的描边外扩，缩放以真实几何为准）。</summary>
    static Rect NormBox(List<(float X, float Y)> pts)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        return new Rect(x0, y0, Math.Max(1e-4, x1 - x0), Math.Max(1e-4, y1 - y0));
    }

    /// <summary>选中框四角（画布坐标），顺序 TL/TR/BR/BL。供绘制与命中测试共用。</summary>
    List<Point> SelCornersDisp(Annotation a)
    {
        var nb = NormBox(a.Points);
        var tl = NormToDisp(((float)nb.X, (float)nb.Y)); var br = NormToDisp(((float)nb.Right, (float)nb.Bottom));
        return new List<Point> { new(tl.X, tl.Y), new(br.X, tl.Y), new(br.X, br.Y), new(tl.X, br.Y) };
    }

    /// <summary>命中四角手柄（容差 9 DIP）→ 进入缩放拖拽。返回是否命中。</summary>
    bool TryBeginResize(Point sp)
    {
        if (_sel == null || _sel.Points.Count == 0) return false;
        var corners = SelCornersDisp(_sel);
        int hitC = -1; double best = 9 * 9;
        for (int i = 0; i < 4; i++)
        {
            double dx = sp.X - corners[i].X, dy = sp.Y - corners[i].Y, d2 = dx * dx + dy * dy;
            if (d2 <= best) { best = d2; hitC = i; }
        }
        if (hitC < 0) return false;
        _resizeCorner = hitC;
        _resizeAnn = _sel;
        _resizeOrigPts = new List<(float, float)>(_sel.Points);
        _resizeOrigBox = NormBox(_sel.Points);
        _resizeDrag = true;   // 关键：Canvas_Move 的缩放分支以 _resizeDrag 为门，漏设则拖角无反应（C1 修）
        _canvas.CaptureMouse();
        FrameCapture.Core.Trace.Log($"editor resize start corner={hitC}");
        return true;
    }

    /// <summary>拖拽中：以对角为锚点，把原始点集线性映射到「锚点→当前光标」的新框，实时改写 _resizeAnn 几何并重绘。</summary>
    void ResizeTo(Point sp)
    {
        if (_resizeAnn == null || _resizeOrigPts == null) return;
        var cur = Norm(sp);
        var ob = _resizeOrigBox;
        double ax = (_resizeCorner == 0 || _resizeCorner == 3) ? ob.Right : ob.X;   // 锚点=对角
        double ay = (_resizeCorner == 0 || _resizeCorner == 1) ? ob.Bottom : ob.Y;
        double nx0 = Math.Min(ax, cur.X), ny0 = Math.Min(ay, cur.Y);
        double nw = Math.Max(0.01, Math.Abs(cur.X - ax)), nh = Math.Max(0.01, Math.Abs(cur.Y - ay));
        _resizeAnn.Points.Clear();
        foreach (var p in _resizeOrigPts)
        {
            double fx = (p.X - ob.X) / ob.Width, fy = (p.Y - ob.Y) / ob.Height;
            _resizeAnn.Points.Add(((float)Math.Clamp(nx0 + fx * nw, 0, 1), (float)Math.Clamp(ny0 + fy * nh, 0, 1)));
        }
        RedrawAnns();
    }

    /// <summary>悬停在四角附近时切换对角缩放光标，让手柄「看得见、找得到」（直接回应「看不到角手柄」）。</summary>
    void UpdateResizeCursor(Point sp)
    {
        if (_tool != Tool.Select || _sel == null || _sel.Points.Count == 0) { _canvas.Cursor = null; return; }
        var corners = SelCornersDisp(_sel);
        int hitC = -1; double best = 9 * 9;
        for (int i = 0; i < 4; i++)
        {
            double dx = sp.X - corners[i].X, dy = sp.Y - corners[i].Y, d2 = dx * dx + dy * dy;
            if (d2 <= best) { best = d2; hitC = i; }
        }
        // TL/BR 对角 → ↖↘；TR/BL → ↗↙
        _canvas.Cursor = hitC == 0 || hitC == 2 ? System.Windows.Input.Cursors.SizeNWSE
                       : hitC == 1 || hitC == 3 ? System.Windows.Input.Cursors.SizeNESW : null;
    }

    /// <summary>标注的画布坐标包围盒：文字用元素实测；其余由归一化点折算（Line/Path 几何在元素内部，GetLeft 不可靠）。</summary>
    Rect AnnBDisp(Annotation a)
    {
        if (a.Points.Count == 0) return Rect.Empty;
        if (a.Kind == AnnKind.Text || a.Kind == AnnKind.Number)
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

    /// <summary>选中已有标注时改颜色/粗细——原位替换（可撤销），同时该项已是当前默认（后续新标注沿用）。
    /// 传 null 表示该项不改（改色时不动粗细、改粗细时不动颜色）。文字/序号的粗细即字号。</summary>
    void RestyleSelected(Color? wpfColor, float? thickness)
    {
        if (_sel == null || !_anns.Items.Contains(_sel)) return;
        var s = _sel;
        System.Drawing.Color? gdi = wpfColor.HasValue
            ? System.Drawing.Color.FromArgb(wpfColor.Value.A, wpfColor.Value.R, wpfColor.Value.G, wpfColor.Value.B)
            : null;
        bool colorChanged = gdi.HasValue && (gdi.Value.R != s.Color.R || gdi.Value.G != s.Color.G || gdi.Value.B != s.Color.B || gdi.Value.A != s.Color.A);
        bool widthChanged = thickness.HasValue && Math.Abs(s.Thickness - thickness.Value) > 1e-6;
        if (!colorChanged && !widthChanged) return; // 无实际变化 → 不产生撤销项
        var e = s.WithStyle(colorChanged ? gdi : null, widthChanged ? thickness : null);
        if (_anns.Replace(s, e))
        {
            _order.AnnAdded(); RefreshUndo(); _sel = e; RedrawAnns();
            FrameCapture.Core.Trace.Log($"editor restyle kind={s.Kind} color={colorChanged} w={widthChanged}");
        }
    }
    #endregion

}
