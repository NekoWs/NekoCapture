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
    private readonly Rectangle _veil = new(), _spot = new(), _prev = new();
    private System.Windows.Shapes.Path? _spotPath;
    private System.Windows.Shapes.Path? _holeFill;
    private int _dragX1, _dragY1;
    private bool _dragging;
    private bool _finished;
    private RectI? _existing;
    private readonly bool _snapContent;

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

    public RegionPickerWindow(MonitorInfo monitor, RectI? existing, bool snapContent = false)
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        _monitor = monitor;
        _snapContent = snapContent;
        _monitorPhys = monitor.Bounds;
        _existing = existing;

        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetWindowTextW(h, "FrameCapture Region");
            FrameCapture.Core.Fluent.FadeIn(this, 120);
        };
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
            Child = new TextBlock
            {
                Text = "拖框选择 · 悬停点击吸附窗口 · Enter 确认上次选区 · Esc 取消",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            },
        };
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

            _veil.Width = Width; _veil.Height = Height;
            _veil.Fill = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0));

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
        var p = WindowPosToPhys(e.GetPosition(this));
        if (_dragging)
        {
            var rect = RectI.FromCorners(_dragX1, _dragY1, p.X, p.Y);
            ShowSpot(rect);
        }
        else
        {
            var hit = SnapWindowAt(p);
            if (hit != null) ShowSpot(hit.Value);
            else if (_existing == null) ShowSpot(null);
        }
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
            // 视为点击：选窗口或沿用 existing
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
