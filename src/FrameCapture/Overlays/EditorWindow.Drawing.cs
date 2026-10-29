using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Drawing;
using Loc = FrameCapture.Core.Loc;
using Theme = FrameCapture.Core.Theme;
using K = FrameCapture.Core.K;

namespace FrameCapture.Overlays;

/// <summary>
/// EditorWindow partial：绘图工具层——线/矩形/圆/箭头/手绘/高亮/文字/马赛克/裁剪/序号 + 撤销 + 时间轴范围。
/// </summary>
public sealed partial class EditorWindow
{
    enum Tool { Browse, Select, Line, Rect, Ellipse, Arrow, Freehand, Highlight, Text, Mosaic, Crop, Number }

    // 工具顺序与 ToolCatalog.Tools 一一对应（锁死）；图标/提示文案的唯一数据源在 Core.ToolCatalog。
    // ToolOrder 由目录键位派生：顺序与目录的漂移无处可藏，未知键位直接抛。
    static Tool FromKey(string k) => k switch
    {
        "V" => Tool.Select, "L" => Tool.Line, "R" => Tool.Rect, "O" => Tool.Ellipse, "A" => Tool.Arrow,
        "P" => Tool.Freehand, "H" => Tool.Highlight, "T" => Tool.Text, "M" => Tool.Mosaic, "C" => Tool.Crop,
        "N" => Tool.Number,
        _ => throw new InvalidOperationException($"工具目录键 {k} 未接 Tool 枚举"),
    };
    static readonly Tool[] ToolOrder = Core.ToolCatalog.Tools.Select(t => FromKey(t.Key)).ToArray();
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
    float CurWidth => Core.CustomPick.Width(_widthIdx, Widths, _customWidth);

    Canvas _canvas = new();
    Grid _viewGrid = new();
    readonly Core.UndoRedo<Annotation> _anns = new();
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
    readonly Core.RangeDragState _rd = new();
    // Background 必须非 null（透明即可）：null 时画布空白区不参与命中测试，轨道点选/拖动会失效
    TimelineSurface _rangeCanvas = new()
    {
        Height = 46,
        Cursor = Cursors.Hand,
        ToolTip = Loc.T(K.Editor.DragToPickARange),
    };
    bool _tlHover;


    void InitDrawing()
    {
        _canvas.Background = Brushes.Transparent;
        // 画布常显（标注是画在它上面的，收起画布＝所有编辑凭空消失）。浏览态改「不可命中」而非 Collapsed：
        // 标注照常显示，鼠标事件穿透到 _viewGrid 走平移/缩放（Canvas_Down 在 Browse 本就 return）。
        _canvas.IsHitTestVisible = false;
        _canvas.MouseLeftButtonDown += Canvas_Down;
        _canvas.MouseMove += Canvas_Move;
        _canvas.MouseLeftButtonUp += Canvas_Up;
        // 视图几何唯一驱动：_viewGrid——视图容器的尺寸即视口，与画布命中态无关
        _viewGrid.SizeChanged += (_, _) => { UpdateImgRect(); RedrawAnns(); };

        // 缩放：Ctrl+滚轮以光标为锚（真实用户路径）；平移：中键拖（任意工具）/ 浏览态放大后左键拖
        _viewGrid.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            var p = e.GetPosition(_viewGrid);
            SetZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), p.X, p.Y);
            e.Handled = true;
        };
        _viewGrid.PreviewMouseDown += (_, _) => { if (_textInput == null) Focus(); };
        _viewGrid.MouseDown += Pan_Down;
        _viewGrid.MouseMove += Pan_Move;
        _viewGrid.MouseUp += Pan_Up;

        _rangeCanvas.Paint = PaintTimeline;
        var rangeBorder = new Border { Child = _rangeCanvas };
        _rangeCanvas.MouseLeftButtonDown += Range_Down;
        // 悬停提示可裁剪：未裁剪时两端也画出淡把手（PR 式 affordance，不悬停不打扰）
        _rangeCanvas.MouseEnter += (_, _) => { _tlHover = true; DrawRange(); };
        _rangeCanvas.MouseLeave += (_, _) => { _tlHover = false; DrawRange(); };
        _rangeCanvas.MouseMove += Range_Move;
        _rangeCanvas.MouseLeftButtonUp += Range_Up;
        _rangeCanvas.LostMouseCapture += (_, _) => { _tlMode = 0; DrawRange(); };
        _rangeCanvas.SizeChanged += (_, _) => DrawRange();
        _rangeHost = rangeBorder;
    }

    Border? _rangeHost;

    // 帧内容在 canvas 中的显示矩形（裁剪子区 × 适配 × 缩放 + 平移）——标注命中/绘制、Image 布局、导出折算共用这一事实源
    Rect _imgRect;
    double _zoom = 1.0, _panX, _panY;
    readonly Core.CropState _crop = new();
    readonly Core.ActionOrder _order = new();

    // 当前帧缓存：录制产物的帧逐帧落盘，每次索引器访问都要 Seek+ReadExactly 整帧并新分配像素数组。
    // 鼠标拖动期间同一帧会被反复取用，而帧内容不可变、_seq 终身不变，故按帧号缓存即可。
    Capture.CapturedFrame? _curFrame;
    int _curFrameIndex = -1;

    /// <summary>取当前帧，按 <c>_index</c> 缓存。翻页自动重取；同一帧内重复取用不触碰磁盘。</summary>
    /// <returns>当前帧。帧像素数组在多次调用间共享——帧内容只读，不得写入返回值的 <c>Bgra</c>。</returns>
    Capture.CapturedFrame Cur()
    {
        if (_curFrameIndex != _index || _curFrame == null)
        {
            _curFrame = _seq.Frames[_index];
            _curFrameIndex = _index;
        }
        return _curFrame;
    }

    void UpdateImgRect()
    {
        // 用 _viewGrid 尺寸：视图容器任何状态都被布局且有尺寸，Image 任何状态都要跟随
        double cw = _viewGrid.ActualWidth, ch = _viewGrid.ActualHeight;
        var f = Cur();
        var (fw, fh) = CropPx(f.W, f.H); // 裁剪子区像素尺寸：适配/钳制都以"可见区"为准
        double sFit = Core.ZoomMath.FitScale(cw, ch, fw, fh);
        double s = sFit * _zoom;
        var (px, py) = Core.ZoomMath.ClampPan(_panX, _panY, cw, ch, fw * s, fh * s);
        _panX = px; _panY = py; // 视口变小后自动回钳
        // 全帧显示矩形：其"裁剪子区"中心恰落在视口中心 + pan（pan=裁剪内容中心相对视口中心偏移，与 ZoomMath 约定一致）
        var c = _crop.Current;
        var (rx, ry, rw, rh) = Core.ZoomMath.CropViewRect(cw, ch, f.W, f.H, c.X, c.Y, c.W, c.H, s, px, py);
        _imgRect = new Rect(rx, ry, rw, rh);
        ApplyImageView();
        UpdateEditorStatus();
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

    #region 缩放 / 平移

    void SetZoom(double newZoom, double cx, double cy)
    {
        var f = Cur();
        var (fw, fh) = CropPx(f.W, f.H);
        var (z, px, py) = Core.ZoomMath.ZoomAt(_zoom, _panX, _panY, newZoom, cx, cy,
            _viewGrid.ActualWidth, _viewGrid.ActualHeight, fw, fh);
        if (Math.Abs(z - _zoom) < 1e-9 && Math.Abs(px - _panX) < 1e-9 && Math.Abs(py - _panY) < 1e-9) return;
        _zoom = z; _panX = px; _panY = py;
        UpdateImgRect(); RedrawAnns(); Show(_index);
        Core.Trace.Log($"editor zoom={_zoom:F2} pan={_panX:F0},{_panY:F0}");
    }
    void ZoomStep(double factor) => SetZoom(_zoom * factor, _viewGrid.ActualWidth / 2, _viewGrid.ActualHeight / 2);
    void ZoomFit() => SetZoom(Core.ZoomMath.MinZoom, _viewGrid.ActualWidth / 2, _viewGrid.ActualHeight / 2);
    void ZoomOneToOne()
    {
        var f = Cur();
        var (fw, fh) = CropPx(f.W, f.H);
        double sFit = Core.ZoomMath.FitScale(_viewGrid.ActualWidth, _viewGrid.ActualHeight, fw, fh);
        SetZoom(Core.ZoomMath.OneToOneZoom(sFit), _viewGrid.ActualWidth / 2, _viewGrid.ActualHeight / 2);
    }

    /// <summary>双击画布：1:1 与适配之间来回。已经停在 1:1（含小图被夹住不放大的那一档）就回适配，否则进 1:1。</summary>
    void ZoomToggleActual()
    {
        var f = Cur();
        var (fw, fh) = CropPx(f.W, f.H);
        double sFit = Core.ZoomMath.FitScale(_viewGrid.ActualWidth, _viewGrid.ActualHeight, fw, fh);
        if (Math.Abs(_zoom - Core.ZoomMath.OneToOneZoom(sFit)) < 0.001) ZoomFit();
        else ZoomOneToOne();
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
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }
    void Pan_Move(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetPosition(_viewGrid);
        var f = Cur();
        var (fw, fh) = CropPx(f.W, f.H);
        double sFit = Core.ZoomMath.FitScale(_viewGrid.ActualWidth, _viewGrid.ActualHeight, fw, fh);
        var (x, y) = Core.ZoomMath.PanBy(_panPressX, _panPressY, p.X - _panPress.X, p.Y - _panPress.Y,
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
        if (_encoding) return;
        if (_playing) TogglePlay();
        CommitTextInput();
        CancelPendingStroke();
        // 再点当前工具=回到浏览态（与线宽/颜色的单选切换同一语义，纯逻辑见 ToolToggle）
        _tool = Core.ToolToggle.Select(_tool, t, Tool.Browse);
        _sel = null; // 换工具即丢选中（选中只属于选中工具）
        _canvas.IsHitTestVisible = _tool != Tool.Browse;   // 浏览态画布不可命中（标注仍显示、平移穿透），绘制态可命中
        _canvas.Cursor = _tool switch { Tool.Browse or Tool.Select => Cursors.Arrow, Tool.Text => Cursors.IBeam, _ => Cursors.Cross };
        RefreshToolHighlight();
        RefreshToolOptions();
    }

    void Highlight(Button b, bool on) => Core.Fluent.HighlightBtn(b, on);

    /// <summary>同步工具栏及浏览按钮的单选状态。</summary>
    void RefreshToolHighlight()
    {
        for (int i = 0; i < _toolBtns.Count; i++) Highlight(_toolBtns[i], ToolOrder[i] == _tool);
        if (_browseButton != null) Highlight(_browseButton, _tool == Tool.Browse);
    }
    #endregion

    #region 记住上次的工具/颜色/粗细

    /// <summary>打开编辑器时恢复上次的标注设置（RememberEditorTools 关则不动，保持默认浏览/红/中）。
    /// 在 BuildLayout 之后调用——此时工具/色板/宽度按钮已建好，SetTool/Refresh* 的高亮才生效。</summary>
    void RestoreEditorPrefs()
    {
        if (!_settings.Current.RememberEditorTools) return;
        var s = _settings.Current;
        if (!string.IsNullOrEmpty(s.LastTool) && Enum.TryParse<Tool>(s.LastTool, out var t) && t != Tool.Browse)
            SetTool(t); // 当前是 Browse，SetTool 切到 t（toggle 语义此时等价"选中 t"）
        if (!string.IsNullOrEmpty(s.LastColor) && TryParseHex(s.LastColor, out var col))
            ApplyColor(col);
        if (s.LastWidth > 0)
            ApplyWidth((float)s.LastWidth);
    }

    /// <summary>关闭编辑器时落盘当前工具/颜色/粗细——只在关闭写一次，避免每次点选都写盘。</summary>
    void SaveEditorPrefs()
    {
        if (!_settings.Current.RememberEditorTools) return;
        string tool = _tool == Tool.Browse ? "" : _tool.ToString();
        var c = CurColor;
        string color = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        double width = CurWidth;
        _settings.Mutate(st => { st.LastTool = tool; st.LastColor = color; st.LastWidth = width; });
    }

    /// <summary>把任意颜色落到色板：命中预设槽位则选中它，否则进自定义槽位（保留 alpha）。</summary>
    void ApplyColor(Color col)
    {
        int idx = Core.EditorPrefs.ColorSlot(col.A, col.R, col.G, col.B,
            Swatches.Select(c => (c.A, c.R, c.G, c.B)).ToArray());
        if (idx >= 0) _colorIdx = idx;
        else { _customColor = col; _colorIdx = Swatches.Length; }
        RefreshSwatches();
    }

    /// <summary>把任意线宽落到宽度槽位：命中预设（细/中/粗）则选中它，否则进自定义槽位。</summary>
    void ApplyWidth(float w)
    {
        int idx = Core.EditorPrefs.WidthSlot(w, Widths);
        if (idx >= 0) _widthIdx = idx;
        else { _customWidth = w; _widthIdx = Widths.Length; }
        RefreshWidths();
    }

    static bool TryParseHex(string hex, out Color col)
    {
        col = default;
        try { col = (Color)ColorConverter.ConvertFromString(hex); return true; } catch { return false; }
    }

    static Color Acc => Core.Theme.Accent;
    #endregion

    #region 裁剪

    void ApplyCrop(Point a, Point b)
    {
        UpdateImgRect();
        if (_imgRect.Width < 4 || _imgRect.Height < 4) return;
        var f = Cur();
        var n0 = Norm(a); var n1 = Norm(b); // 视口→全帧归一化（缩放/平移/已裁剪状态自动卷入）
        // 最小 48 帧像素折算为归一化比例（宽高各按自身维度）
        if (!_crop.Push(n0.X, n0.Y, n1.X, n1.Y, 48.0 / f.W, 48.0 / f.H)) return;
        _order.CropPushed();
        _zoom = Core.ZoomMath.MinZoom; _panX = 0; _panY = 0;   // 裁剪后回到「适配 + 居中」：防小裁剪停在放大/偏移态飞出视口、中键拖不回
        UpdateImgRect(); RedrawAnns(); Show(_index); RefreshUndo();
        var (cx, cy, cw, ch) = _crop.ToPixel(f.W, f.H);
        Core.Trace.Log($"editor crop depth={_crop.Depth} px={cx},{cy} {cw}x{ch}");
    }

    /// <summary>单帧导出（保存帧/复制帧）：所见即所得——裁剪→重映射→烘焙，与 GIF 确定同管线。</summary>
    Capture.CapturedFrame ExportFrame(int index)
    {
        var f = _seq.Frames[index];
        var frames = new List<Capture.CapturedFrame> { f };
        var anns = _anns.Items.ToList();
        if (_crop.IsCropped)
        {
            var (cx, cy, cw, ch) = _crop.ToPixel(f.W, f.H);
            frames = Bake.Crop(frames, cx, cy, cw, ch);
            anns = Core.CropRemap.Remap(anns, (double)cx / f.W, (double)cy / f.H, (double)cw / f.W, (double)ch / f.H);
        }
        return Bake.Apply(frames, anns)[0];
    }
    #endregion

}
