using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 全屏区域选择器：拖框选择，悬停窗口吸附（单击即选），Esc 取消。
/// </summary>
public sealed class RegionPickerWindow : Window
{
    public event Action<RectI?>? Result;

    private readonly MonitorInfo _monitor;
    private readonly RectI _monitorPhys;
    private readonly Canvas _canvas;
    private readonly Rectangle _prev = new(); // 旧选区虚线框（_veil/_spot 为历史死代码，已删）
    private System.Windows.Shapes.Path? _spotPath;
    private System.Windows.Shapes.Path? _holeFill;
    private int _dragX1, _dragY1;
    private bool _dragging;
    private bool _finished;
    private RectI? _existing;
    private readonly bool _snapContent;
    private readonly bool _magnifier;
    private Core.ColorCopyFormat _colorFormat; // R89：按 C 复制颜色时的文本形式；R96 按 F 可循环切换（Hex→RGB→HSL）
    private (int r, int g, int b)? _lastRgb; // R96：放大镜中心最近一次像素色，供 F 切换格式后就地刷新读数
    private System.Windows.Media.Imaging.BitmapSource? _snap; // 选区放大镜快照（构造时抓，遮罩未渲染→真实颜色）
    private byte[]? _snapBgra; // 快照原始 BGRA（取光标下像素颜色用）
    private int _snapW;
    private System.Windows.Controls.Border? _loupe;
    private Image? _loupeImg;
    private Rectangle? _loupeMarker;
    private System.Windows.Controls.Border? _loupeSwatch;
    private TextBlock? _loupeHex;
    private TextBlock? _loupeInfo;
    private string _loupeHexText = ""; // 当前放大镜中心像素 #RRGGBB，供 C 键复制
    private const int LoupeSpan = 15;   // 放大镜取源边长（源像素）
    private const double LoupeSize = 150; // 放大镜显示边长（DIP）

    /// <summary>mouseDown 时记录（物理坐标，相对监视器原点存 DIP）。</summary>
    readonly System.Windows.Threading.DispatcherTimer _topTimer;

    static System.Windows.Media.Animation.DoubleAnimation MaskAnim(double to, int ms, int delay)
        => new(to, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };

    void ReassertTop()
    {
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        Native.Win32.SetWindowPos(h, Native.Win32.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOMOVE|NOSIZE|NOACTIVATE
    }

    public RegionPickerWindow(MonitorInfo monitor, RectI? existing, bool snapContent = false, string? teachTip = null, bool magnifier = false, Core.ColorCopyFormat colorFormat = Core.ColorCopyFormat.Hex)
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        _monitor = monitor;
        _snapContent = snapContent;
        _magnifier = magnifier;
        _colorFormat = colorFormat;
        _monitorPhys = monitor.Bounds;
        _existing = existing;
        // 放大镜快照：此刻本窗尚未 Show、遮罩未渲染，BitBlt 拿到干净桌面（旧选区虚线窗已 WDA 排除，不入镜）
        if (_magnifier)
        {
            var f = LiveCapture.Grab(_monitorPhys);
            if (f != null) { _snap = FrameCapture.Services.BgraImage.ToSource(f); _snapBgra = f.Bgra; _snapW = f.W; }
            else _magnifier = false;
        }

        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetWindowTextW(h, "FrameCapture Region");
            // 分层窗（AllowsTransparency）没有黑首帧问题，绝不能屏外停放：
            // 本窗 Left/Top 在 Loaded 才赋值，Park 抓到的 home=NaN，回写 NaN 会让系统级联位兜底（实测偏移 266,266，提示被推出屏外）
            FrameCapture.Core.Fluent.FadeIn(this, 120);
        };
        FrameCapture.Core.Fluent.BorderlessNative(this, 0); // 全屏覆盖层：消除 DWM 非客户区白条，无需边缘调整
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Title = "FrameCapture Region"; // 与 SourceInitialized 里 SetWindowText 一致，自动化可定位
        ShowInTaskbar = false;
        Topmost = true;
        // 与其它置顶窗（游戏等）同带竞争：失活即重插到置顶带最前，另有 400ms 巡检兜底
        Deactivated += (_, _) => { if (IsLoaded) ReassertTop(); };
        _topTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _topTimer.Tick += (_, _) => { if (IsVisible) ReassertTop(); else _topTimer!.Stop(); };
        Cursor = Cursors.Cross;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _canvas = new Canvas();
        _hint = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x20, 0x20, 0x20)),
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 46),
            IsHitTestVisible = false,
            Opacity = 0,
        };
        {
            var hintPanel = new StackPanel();
            hintPanel.Children.Add(new TextBlock
            {
                Text = _existing != null
                    ? "拖框选择 · 悬停点击吸附窗口 · Enter 确认上次选区 · Esc 取消"
                    : "拖框选择 · 悬停点击吸附窗口 · Esc 取消",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            });
            if (teachTip != null)
                hintPanel.Children.Add(new TextBlock
                {
                    Text = teachTip,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(FrameCapture.Core.Theme.Accent),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                });
            _hint.Child = hintPanel;
        }
        var rootGrid = new Grid();
        rootGrid.Children.Add(_canvas);
        rootGrid.Children.Add(_hint);
        Content = rootGrid;

        Loaded += (_, _) => _topTimer.Start();
        Closed += (_, _) => _topTimer.Stop();

        Loaded += (_, _) =>
        {
            var src = (PresentationSource)PresentationSource.FromVisual(this);
            double sx = 1, sy = 1;
            var tr = VisualTreeHelper.GetDpi(this);
            sx = tr.DpiScaleX; sy = tr.DpiScaleY;

            Left = _monitorPhys.X / sx;
            Top = _monitorPhys.Y / sy;
            Width = _monitorPhys.W / sx;
            Height = _monitorPhys.H / sy;

            // 挖洞遮罩：用 Path 奇偶填充
            _spotPath = new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)),
                Data = BuildMask(new Rect(0, 0, Width, Height), null),
            };
            // 洞内补一层 alpha=1 的填充：分层窗 alpha=0 的像素鼠标会穿透到下面窗口，
            // 导致吸附/框选高亮区内目标窗口仍可交互、无法拖选。alpha=1 肉眼不可辨但可命中。
            _holeFill = new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)),
                IsHitTestVisible = true,
            };
            _spotStroke = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                Visibility = Visibility.Collapsed,
            };
            _canvas.Children.Add(_holeFill);
            _canvas.Children.Add(_spotPath);
            _canvas.Children.Add(_spotStroke);
            _canvas.Children.Add(_prev);

            // 尺寸徽标：跟随选区左上角，实时显示物理像素 W×H
            _badgeText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
            };
            _badge = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x1B, 0x1B)),
                Padding = new Thickness(8, 3, 8, 3),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Child = _badgeText,
            };
            _canvas.Children.Add(_badge);

            // 放大镜：光标旁高倍像素窗 + 坐标/尺寸读数（R70）
            if (_magnifier && _snap != null)
            {
                double cell = LoupeSize / LoupeSpan;
                _loupeImg = new Image { Width = LoupeSize, Height = LoupeSize, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(_loupeImg, BitmapScalingMode.NearestNeighbor);
                _loupeMarker = new Rectangle
                {
                    Width = cell, Height = cell,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
                    StrokeThickness = 1, SnapsToDevicePixels = true,
                };
                var markerCanvas = new Canvas { Width = LoupeSize, Height = LoupeSize, IsHitTestVisible = false };
                markerCanvas.Children.Add(_loupeMarker);
                var loupeGrid = new Grid();
                loupeGrid.Children.Add(_loupeImg);
                loupeGrid.Children.Add(markerCanvas);
                _loupeInfo = new TextBlock
                {
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                };
                // 取色行：色块 + #RRGGBB（按 C 复制）
                _loupeSwatch = new System.Windows.Controls.Border
                {
                    Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _loupeHex = new TextBlock
                {
                    FontSize = 11,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var colorRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                };
                colorRow.Children.Add(_loupeSwatch);
                colorRow.Children.Add(_loupeHex);
                var loupeStack = new StackPanel();
                loupeStack.Children.Add(loupeGrid);
                loupeStack.Children.Add(_loupeInfo);
                loupeStack.Children.Add(colorRow);
                _loupe = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x1B, 0x1B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4C, 0xC2, 0xFF)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4),
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                    Child = loupeStack,
                };
                _canvas.Children.Add(_loupe);
            }

            if (_existing != null)
            {
                var r = ToDip(_existing.Value);
                Canvas.SetLeft(_prev, r.X); Canvas.SetTop(_prev, r.Y);
                _prev.Width = r.Width; _prev.Height = r.Height;
                _prev.Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x4C, 0xC2, 0xFF));
                _prev.StrokeThickness = 2;
                _prev.StrokeDashArray = new DoubleCollection { 4, 3 };
            }
            // 入场：遮罩 150ms 渐显；旧区域虚线滞后错开并轻微放大落位（ArkPV 式分层，不打扰）
            _canvas.Opacity = 0;
            _canvas.BeginAnimation(OpacityProperty, MaskAnim(1, 150, 0));
            _hint.BeginAnimation(OpacityProperty, MaskAnim(1, 220, 320)); // 提示滞后入场，不打扰
            if (_existing != null)
            {
                _prev.Opacity = 0;
                _prev.RenderTransformOrigin = new Point(0.5, 0.5);
                var st = new ScaleTransform(0.98, 0.98);
                _prev.RenderTransform = st;
                _prev.BeginAnimation(OpacityProperty, MaskAnim(1, 190, 90));
                st.BeginAnimation(ScaleTransform.ScaleXProperty, MaskAnim(1, 190, 90));
                st.BeginAnimation(ScaleTransform.ScaleYProperty, MaskAnim(1, 190, 90));
            }
            Activate();
            Keyboard.Focus(this);
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(null);
            else if (e.Key == Key.Enter && _existing != null) Finish(_existing);
            else if (e.Key == Key.C && _loupeHexText.Length > 0)
            {
                // 取色：复制光标下像素的 #RRGGBB 到剪贴板
                try { System.Windows.Clipboard.SetText(_loupeHexText); } catch { }
                if (_loupeHex != null) _loupeHex.Text = "已复制 " + _loupeHexText;
                e.Handled = true;
            }
            else if (e.Key == Key.F)
            {
                // R96：循环取色复制格式 Hex→RGB→HSL，就地刷新放大镜读数并持久化（下次捕获/设置窗同步）
                _colorFormat = (Core.ColorCopyFormat)(((int)_colorFormat + 1) % 3);
                if (_lastRgb is { } c && _loupeHex != null)
                {
                    _loupeHexText = Core.ColorFormat.Format(c.r, c.g, c.b, _colorFormat);
                    _loupeHex.Text = _loupeHexText;
                }
                Services.AppHost.Current.Settings.Mutate(s => s.ColorCopyFormat = Core.ColorFormat.ToSetting(_colorFormat));
                e.Handled = true;
            }
            else if (e.Key == Key.Tab && _curElem != null)
            {
                // Tab 上钻控件层级：输入框 → 表单容器 → … → 整窗
                var up = Core.ElementSnap.Parent(_curElem);
                if (up != null) { _curElem = up; ShowSpot(up.Rect); e.Handled = true; }
            }
        };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseDoubleClick += (_, _) =>
        {
            var p = WindowPosToPhys(Mouse.GetPosition(this));
            var hit = SnapWindowAt(p);
            Finish(hit ?? _existing ?? new RectI(_monitorPhys.X, _monitorPhys.Y, _monitorPhys.W, _monitorPhys.H));
        };
    }

    private Rectangle _spotStroke = null!;
    private Core.ElementHit? _curElem;
    private System.Windows.Controls.Border _hint = null!;
    private System.Windows.Controls.Border _badge = null!;
    private TextBlock _badgeText = null!;

    private Rect ToDip(RectI phys)
    {
        var tr = VisualTreeHelper.GetDpi(this);
        return new Rect(
            (phys.X - _monitorPhys.X) / tr.DpiScaleX,
            (phys.Y - _monitorPhys.Y) / tr.DpiScaleY,
            phys.W / tr.DpiScaleX,
            phys.H / tr.DpiScaleY);
    }

    private (int X, int Y) WindowPosToPhys(Point dipInWindow)
    {
        var tr = VisualTreeHelper.GetDpi(this);
        return (
            _monitorPhys.X + (int)Math.Round(dipInWindow.X * tr.DpiScaleX),
            _monitorPhys.Y + (int)Math.Round(dipInWindow.Y * tr.DpiScaleY));
    }

    private static Geometry BuildMask(Rect outer, Rect? hole)
    {
        var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
        g.Children.Add(new RectangleGeometry(outer));
        if (hole != null) g.Children.Add(new RectangleGeometry(hole.Value));
        return g;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var p = WindowPosToPhys(e.GetPosition(this));
        _dragX1 = p.X; _dragY1 = p.Y;
        _dragging = true;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var dip = e.GetPosition(this);
        var p = WindowPosToPhys(dip);
        if (_dragging)
        {
            var rect = RectI.FromCorners(_dragX1, _dragY1, p.X, p.Y);
            ShowSpot(rect);
            UpdateLoupe(dip, p, rect);
        }
        else
        {
            // 控件级吸附（UIA）优先：输入框/文字/图片/容器…；不支持时回退窗口级
            if (_snapContent)
            {
                var selfH = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var eh = Core.ElementSnap.At(p.X, p.Y, selfH);
                if (eh != null && eh.Rect.W >= 8 && eh.Rect.H >= 8)
                {
                    _curElem = eh;
                    ShowSpot(eh.Rect);
                    UpdateLoupe(dip, p, eh.Rect);
                    return;
                }
            }
            _curElem = null;
            var hit = SnapWindowAt(p);
            if (hit != null) { ShowSpot(hit.Value); UpdateLoupe(dip, p, hit); }
            else if (_existing == null) { ShowSpot(null); UpdateLoupe(dip, p, null); }
            else UpdateLoupe(dip, p, _existing);
        }
    }

    /// <summary>更新放大镜：从快照取光标周围像素高倍显示，标记光标正下方像素，读数显示坐标(+选区尺寸)。</summary>
    private void UpdateLoupe(Point dip, (int X, int Y) phys, RectI? sel)
    {
        if (!_magnifier || _loupe == null || _loupeImg == null || _loupeMarker == null || _loupeInfo == null || _snap == null) return;
        int sx = phys.X - _monitorPhys.X, sy = phys.Y - _monitorPhys.Y;
        var (cx, cy, size) = Core.MagnifierMath.Crop(sx, sy, _snap.PixelWidth, _snap.PixelHeight, LoupeSpan);
        _loupeImg.Source = new System.Windows.Media.Imaging.CroppedBitmap(_snap, new Int32Rect(cx, cy, size, size));
        double cell = LoupeSize / size;
        _loupeMarker.Width = _loupeMarker.Height = cell;
        Canvas.SetLeft(_loupeMarker, (sx - cx) * cell);
        Canvas.SetTop(_loupeMarker, (sy - cy) * cell);
        _loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var (lx, ly) = Core.MagnifierMath.Place(dip.X, dip.Y, _loupe.DesiredSize.Width, _loupe.DesiredSize.Height, Width, Height, 18);
        Canvas.SetLeft(_loupe, lx); Canvas.SetTop(_loupe, ly);
        _loupeInfo.Text = sel != null ? $"{phys.X}, {phys.Y}   {sel.Value.W}×{sel.Value.H}" : $"{phys.X}, {phys.Y}";
        if (_snapBgra != null && _loupeSwatch != null && _loupeHex != null)
        {
            var (r, g, b) = Core.MagnifierMath.PixelRgb(_snapBgra, _snapW, sx, sy);
            _lastRgb = ((int)r, (int)g, (int)b); // R96：记住当前色，F 切格式时就地重算读数
            _loupeHexText = Core.ColorFormat.Format(r, g, b, _colorFormat);
            _loupeSwatch.Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
            _loupeHex.Text = _loupeHexText;
        }
        _loupe.Visibility = Visibility.Visible;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var p = WindowPosToPhys(e.GetPosition(this));
        var rect = RectI.FromCorners(_dragX1, _dragY1, p.X, p.Y);
        if (rect.W < 8 || rect.H < 8)
        {
            // 视为点击：优先当前吸附的控件，其次窗口，再次沿用 existing
            if (_snapContent)
            {
                var selfH = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var eh = Core.ElementSnap.At(p.X, p.Y, selfH);
                if (eh != null && eh.Rect.W >= 8 && eh.Rect.H >= 8) { Finish(eh.Rect); return; }
            }
            var hit = SnapWindowAt(p);
            Finish(hit ?? _existing ?? _monitorPhys);
            return;
        }
        Finish(rect);
    }

    private RectI? SnapWindowAt((int X, int Y) p)
    {
        var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        // snapContent: prefer client-area rects (exclude window frame / title bar, e.g. game viewport)
        return Core.SnapPick.Pick(
            WindowEnum.ListTopmostCandidates(self).Select(t => _snapContent ? t.Client : t.Bounds), p.X, p.Y);
    }

    private void ShowSpot(RectI? phys)
    {
        if (phys == null)
        {
            _spotStroke.Visibility = Visibility.Collapsed;
            _badge.Visibility = Visibility.Collapsed;
            _spotPath.Data = BuildMask(new Rect(0, 0, Width, Height), null);
            if (_holeFill != null) _holeFill.Data = null;
            return;
        }
        var r = ToDip(phys.Value);
        _spotStroke.Visibility = Visibility.Visible;
        Canvas.SetLeft(_spotStroke, r.X); Canvas.SetTop(_spotStroke, r.Y);
        _spotStroke.Width = r.Width; _spotStroke.Height = r.Height;
        _spotPath.Data = BuildMask(new Rect(0, 0, Width, Height), r);
        _holeFill.Data = new RectangleGeometry(r);
        // 尺寸徽标：贴选区左上角外侧；顶部空间不足时贴入内侧
        _badgeText.Text = $"{phys.Value.W} × {phys.Value.H}";
        _badge.Visibility = Visibility.Visible;
        _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double bw = _badge.DesiredSize.Width, bh = _badge.DesiredSize.Height;
        double bx = r.X - 2 < 0 ? r.X + r.Width - bw - 2 : Math.Min(r.X - 2, Width - bw - 4);
        double by = r.Y - bh - 6 < 0 ? Math.Min(r.Y + 4, Height - bh - 4) : r.Y - bh - 6;
        Canvas.SetLeft(_badge, bx); Canvas.SetTop(_badge, by);
    }

    private void Finish(RectI? region)
    {
        if (_finished) return;
        _finished = true;
        Result?.Invoke(region);
        Close();
    }
}
