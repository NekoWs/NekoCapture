using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 捕获范围轮廓：点击穿透、不抢焦点、**不进屏幕捕获**。
///
/// 两种用法：
///  - 选区确认反馈 / 常显（<c>AlwaysShowRegionOutline</c>）：Flash + FadeAway；
///  - 录制中范围指示（<c>ShowCaptureScope</c>）：录制期间常亮，松开后淡出。
///
/// 之所以必须"不进捕获"：Windows 11 的系统捕获提示边框（#FFD11C）由 WGC 按"被捕获对象"绘制，
/// 而 WGC 只能整显示器捕获，于是它永远绕整个屏幕、与我们选的范围无关。关掉它
/// （<c>IGraphicsCaptureSession3.IsBorderRequired=false</c>）之后，范围提示就由本窗按真实区域画；
/// 因为 WGC/GDI 都是整屏抓，本窗只要在录制期间上屏就必须被排除，否则会被录进产物。
/// </summary>
public sealed class RegionOutlineWindow : Window
{
    private readonly Border _border;
    private readonly System.Windows.Threading.DispatcherTimer _flashTimer;
    private bool _captureExcluded;

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

        // 先把 HWND 建好（此刻还未上屏），再打上"不进捕获"，避免第一帧被录进去
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        EnsureExcludedFromCapture();
    }

    public void SetRegion(RectI region)
    {
        // 物理像素窗口：直接用 SetWindowPos 精确定位
        Place(region);
        if (IsLoaded) Place(region);
        Dispatcher.BeginInvoke(() => Place(region));
    }

    /// <summary>显示并复位到常态（用于录制期间的常亮范围指示）。</summary>
    public void ShowNoActivate()
    {
        _fadeTimer?.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        ResetBorder();
        if (!IsVisible) Show();
        EnsureExcludedFromCapture();
    }

    /// <summary>
    /// 让本窗不出现在屏幕捕获里（BitBlt 与 WGC 都排除）。
    /// Win10 1803+/Win11 支持；失败（旧系统或权限不足）返回 false，调用方据此决定是否上屏。
    /// </summary>
    public bool EnsureExcludedFromCapture()
    {
        if (_captureExcluded) return true;
        if (TestShowInCapture) return false; // 仅取证：让轮廓能被截到，便于人工核对位置
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return false;
        if (!Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_EXCLUDEFROMCAPTURE))
        {
            FrameCapture.Core.Trace.Log("outline: SetWindowDisplayAffinity failed err="
                + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            return false;
        }
        _captureExcluded = true;
        FrameCapture.Core.Trace.Log("outline: capture-excluded via WDA_EXCLUDEFROMCAPTURE");
        return true;
    }

    /// <summary>测试专用（同 FC_TEST_NO_FG 约定，用户环境永不置位）：让轮廓留在捕获画面里，供视觉取证。</summary>
    static readonly bool TestShowInCapture =
        Environment.GetEnvironmentVariable("FC_TEST_OUTLINE_IN_CAPTURE") == "1";

    /// <summary>复位到常态描边（清掉 Flash 的动画残留）。</summary>
    private void ResetBorder()
    {
        _border.BeginAnimation(Border.BorderThicknessProperty, null);
        if (_border.BorderBrush is SolidColorBrush sb) sb.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _border.BorderBrush = new SolidColorBrush(AccentCol(0xC0));
        _border.BorderThickness = new Thickness(2);
    }

    static Color AccentCol(byte a)
    {
        var c = FrameCapture.Core.Theme.Accent;
        return Color.FromArgb(a, c.R, c.G, c.B);
    }

    public void HideSilently()
    {
        // 停掉在途的淡出动画，否则下次 Show 会被它的 Completed→Hide 拽掉
        _fadeTimer?.Stop();
        BeginAnimation(OpacityProperty, null);
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
