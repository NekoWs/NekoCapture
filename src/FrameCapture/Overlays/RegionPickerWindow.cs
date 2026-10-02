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
    private int _dragX1, _dragY1;
    private bool _dragging;
    private bool _finished;
    private RectI? _existing;

    /// <summary>mouseDown 时记录（物理坐标，相对监视器原点存 DIP）。</summary>
    readonly System.Windows.Threading.DispatcherTimer _topTimer;

    void ReassertTop()
    {
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        Native.Win32.SetWindowPos(h, Native.Win32.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOMOVE|NOSIZE|NOACTIVATE
    }

    public RegionPickerWindow(MonitorInfo monitor, RectI? existing)
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        _monitor = monitor;
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
        Content = _canvas;

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
            _spotStroke = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                Visibility = Visibility.Collapsed,
            };
            _canvas.Children.Add(_spotPath);
            _canvas.Children.Add(_spotStroke);
            _canvas.Children.Add(_prev);

            if (_existing != null)
            {
                var r = ToDip(_existing.Value);
                Canvas.SetLeft(_prev, r.X); Canvas.SetTop(_prev, r.Y);
                _prev.Width = r.Width; _prev.Height = r.Height;
                _prev.Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x4C, 0xC2, 0xFF));
                _prev.StrokeThickness = 2;
                _prev.StrokeDashArray = new DoubleCollection { 4, 3 };
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
            Finish(hit ?? new RectI(_monitorPhys.X, _monitorPhys.Y, _monitorPhys.W, _monitorPhys.H));
        };
    }

    private Rectangle _spotStroke = null!;

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
        return Core.SnapPick.Pick(WindowEnum.ListTopmostCandidates().Select(t => t.Bounds), p.X, p.Y);
    }

    private void ShowSpot(RectI? phys)
    {
        if (phys == null)
        {
            _spotStroke.Visibility = Visibility.Collapsed;
            _spotPath.Data = BuildMask(new Rect(0, 0, Width, Height), null);
            return;
        }
        var r = ToDip(phys.Value);
        _spotStroke.Visibility = Visibility.Visible;
        Canvas.SetLeft(_spotStroke, r.X); Canvas.SetTop(_spotStroke, r.Y);
        _spotStroke.Width = r.Width; _spotStroke.Height = r.Height;
        _spotPath.Data = BuildMask(new Rect(0, 0, Width, Height), r);
    }

    private void Finish(RectI? region)
    {
        if (_finished) return;
        _finished = true;
        Result?.Invoke(region);
        Close();
    }
}
