using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>常显捕获范围轮廓：点击穿透、不抢焦点。</summary>
public sealed class RegionOutlineWindow : Window
{
    private readonly Border _border;
    private readonly System.Windows.Threading.DispatcherTimer _flashTimer;

    public RegionOutlineWindow()
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        IsHitTestVisible = false;
        Focusable = false;
        ShowActivated = false;

        _border = new Border
        {
            BorderBrush = new SolidColorBrush(AccentCol(0xC0)),
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
        };
        Content = _border;

        _flashTimer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(600) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            _border.BorderBrush = new SolidColorBrush(AccentCol(0x60));
            _border.BorderThickness = new Thickness(2);
        };
    }

    public void SetRegion(RectI region)
    {
        // 物理像素窗口：直接用 SetWindowPos 精确定位
        SourceInitialized += (_, _) => Place(region);
        if (IsLoaded) Place(region);
        Dispatcher.BeginInvoke(() => Place(region));
    }

    public void ShowNoActivate()
    {
        _fadeTimer?.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (!IsVisible) Show();
    }

    static Color AccentCol(byte a)
    {
        var c = FrameCapture.Core.Theme.Accent;
        return Color.FromArgb(a, c.R, c.G, c.B);
    }

    public void HideSilently()
    {
        Hide();
    }

    System.Windows.Threading.DispatcherTimer? _fadeTimer;

    public void FadeAway(double seconds)
    {
        if (_fadeTimer == null)
        {
            _fadeTimer = new System.Windows.Threading.DispatcherTimer();
            _fadeTimer.Tick += (_, _) =>
            {
                _fadeTimer!.Stop();
                var a = new System.Windows.Media.Animation.DoubleAnimation(0, new System.Windows.Duration(TimeSpan.FromMilliseconds(350)))
                { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
                a.Completed += (_, _) => Dispatcher.Invoke(Hide);
                BeginAnimation(OpacityProperty, a);
            };
        }
        _fadeTimer.Stop();
        _fadeTimer.Interval = TimeSpan.FromSeconds(seconds);
        _fadeTimer.Start();
    }

    public void Flash()
    {
        // 强调衰减：亮起后 450ms cubic-out 回落常态（不再是 600ms 后的步进两跳）
        _flashTimer.Stop();
        var b = new SolidColorBrush(AccentCol(0xFF));
        _border.BorderBrush = b;
        _border.BorderThickness = new Thickness(3);
        var ca = new System.Windows.Media.Animation.ColorAnimation(AccentCol(0x60),
            new System.Windows.Duration(TimeSpan.FromMilliseconds(450)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        b.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, ca);
        var ta = new System.Windows.Media.Animation.ThicknessAnimation(new Thickness(2),
            new System.Windows.Duration(TimeSpan.FromMilliseconds(450)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        _border.BeginAnimation(Border.BorderThicknessProperty, ta);
    }

    private void Place(RectI region)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        const int SWP_NOMOVE_ = 0;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, region.X, region.Y, region.W, region.H,
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        uint ex = Win32.GetWindowLongW(hwnd, Win32.GWL_EXSTYLE);
        ex |= Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW;
        Win32.SetWindowLongW(hwnd, Win32.GWL_EXSTYLE, ex);
    }
}
