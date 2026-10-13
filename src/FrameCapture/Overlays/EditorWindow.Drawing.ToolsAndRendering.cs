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

/// <summary>
/// EditorWindow partial：裁剪引导层、文字标注、马赛克，与标注渲染/撤销重做。
/// </summary>
public sealed partial class EditorWindow
{
    #region 裁剪引导层：框外压暗 + 三分网格 + 「松开应用」

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
            Child = new TextBlock { Text = "松开应用裁剪", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(FrameCapture.Core.Fluent.OnAccentColor(Acc)) }, // 前景随强调色明暗自适应，浅色强调色下转黑可读
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
    #endregion

    #region 文字标注：点画布→就地输入→Enter/失焦上屏；点已有文字=再编辑

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
        // 与上屏 TextBlock 完全同式的字号：clamp 下限一致，预览与上屏才不错位
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
        // 文字起点 = pos：把边框+内边距反向挪掉，输入框里的字与上屏后的字同位
        Canvas.SetLeft(box, pos.X - 6.5); Canvas.SetTop(box, pos.Y - 3.5);
        _textPos = pos;
        box.PreviewKeyDown += (_, ek) =>
        {
            if (ek.Key == Key.Enter) { CommitTextInput(); ek.Handled = true; }
            else if (ek.Key == Key.Escape) { CancelTextInput(); ek.Handled = true; }
        };
        box.LostFocus += (_, _) => CommitTextInput(); // 点别处 = 视为上屏
        _textInput = box;
        RedrawAnns();          // 先重绘（此时 _textEditing 已置，原字被跳过）再把输入框叠上去
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
            // 再编辑：空文或无改动 = 保持原样；有改动 = 原位替换，可撤销
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
    #endregion

    #region 马赛克：拖框实时块均值预览，与烘焙共用 MosaicInPlace

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
    /// 帧的指定区域马赛克位图：只算区域不算整帧 → 翻帧/播放时逐帧重算也便宜，
    /// 马赛克从「静止贴图」变成「区域滤镜」。块从区域原点起算，与 Bake 完全一致（WYSIWYG，锁）。
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

    /// <summary>翻帧/播放时刷新所有马赛克元素的内容（马赛克是区域滤镜，不是静止贴图）。</summary>
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
            if (ReferenceEquals(a, _textEditing)) continue;   // 正在编辑的文字：只留输入框，隐藏原字，避免与输入框重叠
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
                case AnnKind.Rect when pts.Count >= 2:
                {
                    var el = new Rectangle { Stroke = wpfCol, StrokeThickness = stroke };
                    double w = Math.Abs(pts[1].X - pts[0].X), h2 = Math.Abs(pts[1].Y - pts[0].Y);
                    el.Width = Math.Max(2, w); el.Height = Math.Max(2, h2);
                    Canvas.SetLeft(el, Math.Min(pts[0].X, pts[1].X)); Canvas.SetTop(el, Math.Min(pts[0].Y, pts[1].Y));
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
                case AnnKind.Highlight when pts.Count >= 2:
                {
                    var hlCol = System.Windows.Media.Color.FromArgb(FrameCapture.Drawing.Annotation.HighlightAlpha, a.Color.R, a.Color.G, a.Color.B);
                    var el = new Polyline { Points = new PointCollection(pts), Stroke = new SolidColorBrush(hlCol), StrokeThickness = stroke, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat };
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
                case AnnKind.Number when pts.Count > 0 && !string.IsNullOrEmpty(a.Text):
                {
                    var f = _seq.Frames[_index];
                    double fs = Math.Clamp(FrameCapture.Core.AnnFont.BakePx(a.Thickness, f.W, f.H) * Math.Max(0.05, _imgRect.Width / Math.Max(1, f.W)), 8, 400);
                    double d = FrameCapture.Core.NumberBadge.Diameter(fs);
                    var grid = new Grid { Width = d, Height = d };
                    grid.Children.Add(new Ellipse { Fill = wpfCol });
                    grid.Children.Add(new TextBlock
                    {
                        Text = a.Text, FontSize = fs, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(FrameCapture.Core.NumberBadge.NumberOnLightCircle(a.Color.R, a.Color.G, a.Color.B) ? System.Windows.Media.Colors.Black : System.Windows.Media.Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    });
                    Canvas.SetLeft(grid, pts[0].X - d / 2); Canvas.SetTop(grid, pts[0].Y - d / 2);
                    _canvas.Children.Add(grid);
                    _annEls.Add((grid, a));
                    break;
                }
            }
        }
        // 选中框：虚线强调色，纯装饰不吃命中
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
                // 四角缩放手柄：实心小方块标出可拖角，命中按位置测（TryBeginResize），故元素本身不吃命中
                foreach (var c in SelCornersDisp(_sel))
                {
                    var h = new Rectangle
                    {
                        Width = 9, Height = 9, Fill = new SolidColorBrush(Acc),
                        Stroke = Brushes.White, StrokeThickness = 1.2, IsHitTestVisible = false,
                        RadiusX = 1.5, RadiusY = 1.5,
                    };
                    Canvas.SetLeft(h, c.X - 4.5); Canvas.SetTop(h, c.Y - 4.5);
                    _canvas.Children.Add(h);
                }
            }
        }
        // 画布常显，无需兜底恢复 Visibility；命中态由 SetTool 统一管。
    }
    #endregion

}
