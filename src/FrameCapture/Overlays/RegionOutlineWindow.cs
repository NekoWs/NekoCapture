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
/// - 选区确认反馈 / 常显（<c>AlwaysShowRegionOutline</c>）：Flash + FadeAway；
/// - 录制中范围指示（<c>ShowCaptureScope</c>）：录制期间常亮，松开后淡出。
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

    // REC 徽标：按住超过点按阈值才出现——"还在按 = 正在录"一眼看懂，附实时时长
    private readonly Border _recBadge;
    private readonly System.Windows.Shapes.Ellipse _recDot;
    private readonly TextBlock _recText;
    private readonly System.Windows.Threading.DispatcherTimer _recTimer;
    private System.Diagnostics.Stopwatch? _recClock;

    /// <summary>
    /// 构造轮廓窗：无边框、点击穿透、不抢焦点、不进任务栏，并在句柄建好而尚未上屏时就打上 WDA 排除标记
    /// （先上屏再打标记会让轮廓的第一帧被录进产物）。必须在 UI 线程构造。
    /// </summary>
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

        _recDot = new System.Windows.Shapes.Ellipse
        {
            Width = 9, Height = 9,
            Fill = Brushes.White, // 红底上红点会糊掉，白点才是经典录制符号
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _recText = new TextBlock
        {
            Text = "REC 0:00.0",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _recBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0xC8, 0x1A, 0x1A)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel { Orientation = Orientation.Horizontal },
        };
        ((StackPanel)_recBadge.Child).Children.Add(_recDot);
        ((StackPanel)_recBadge.Child).Children.Add(_recText);

        var root = new Grid();
        root.Children.Add(_border);
        root.Children.Add(_recBadge);
        Content = root;

        _recTimer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(100) };
        _recTimer.Tick += (_, _) => UpdateRecText();

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

        // WPF 布局会用 DIP 覆写 SetWindowPos 的物理定位（实测 681→454）：位置一变就钉回去，
        // guard 防自激，最终稳定在物理坐标。
        LocationChanged += (_, _) =>
        {
            if (_placeGuard || _region.W <= 0) return;
            _placeGuard = true;
            try { Place(_region); } finally { _placeGuard = false; }
        };
    }

    bool _placeGuard;

    private RectI _region; // 最近一次目标区域（物理像素）

    /// <summary>设定轮廓矩形，并立刻按物理像素定位（当前 + 已加载 + 下一拍各钉一次，压住 WPF 布局的回写）。</summary>
    /// <param name="region">目标范围：物理像素、虚拟桌面绝对坐标。本窗不用 DIP 定位，传 DIP 会整体缩小一个 DPI 倍率。</param>
    public void SetRegion(RectI region)
    {
        _region = region;
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
        // WPF 的 Show/布局会用 Left/Top(DIP) 覆写我们 SetWindowPos 的物理定位（实测 681→454=÷1.5，
        // 逐帧抓到：录制轮廓整体偏左上）。Show 之后重新按物理像素钉死，再兜一拍防布局回写。
        Place(_region);
        Dispatcher.BeginInvoke(() => Place(_region));
        EnsureExcludedFromCapture();
    }

    /// <summary>
    /// 让本窗不出现在屏幕捕获里（BitBlt 与 WGC 都排除）。幂等：已排除过直接返回 true。
    /// </summary>
    /// <returns>true = 已排除；false = 取证开关打开（故意让轮廓可被截到）、HWND 尚未就绪，
    /// 或系统不支持（Win10 1803 以下）/ 权限不足。返回 false 时本窗一旦上屏就会被录进捕获产物。</returns>
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

    /// <summary>立即隐藏：停 REC 徽标、停在途淡出动画，直接 Hide()，不播退场动画。</summary>
    /// <remarks>起录与开选区前必须走这里——残留的半透明边框会和选区窗叠着闪，且可能被截进首帧。</remarks>
    public void HideSilently()
    {
        StopRecBadge();
        // 停掉在途的淡出动画，否则下次 Show 会被它的 Completed→Hide 拽掉
        _fadeTimer?.Stop();
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    /// <summary>录制指示徽标：录制进行中由控制器点亮，红点脉冲 + 实时时长。</summary>
    public void StartRecBadge()
    {
        if (_recBadge.Visibility == Visibility.Visible) { UpdateRecText(); return; }
        _recClock = System.Diagnostics.Stopwatch.StartNew();
        UpdateRecText();
        _recTimer.Start();
        _recBadge.Visibility = Visibility.Visible;
        _recBadge.BeginAnimation(OpacityProperty, null);
        _recBadge.Opacity = 0;
        _recBadge.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1,
                new System.Windows.Duration(TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.EnterMs)))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        // 红点 1s 呼吸脉冲（录制进行感）
        var pulse = new System.Windows.Media.Animation.DoubleAnimation(1, 0.35,
            new System.Windows.Duration(TimeSpan.FromSeconds(1)))
        { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        _recDot.BeginAnimation(OpacityProperty, pulse);
    }

    /// <summary>停掉 REC 计时与红点脉冲并收起徽标；轮廓边框的显隐与位置不受影响。</summary>
    public void StopRecBadge()
    {
        _recTimer?.Stop();
        _recClock = null;
        _recDot.BeginAnimation(OpacityProperty, null);
        if (_recBadge != null) _recBadge.Visibility = Visibility.Collapsed;
    }

    /// <summary>录制结束→编辑器构造这段 UI 冻结期，把 REC 徽标转成「处理中…」并停掉计时刷新
    /// （否则 _recTimer 每 100ms 会把文案覆盖回 REC 计时）。调用方随后强制刷新一帧再进编辑器。</summary>
    public void SetRecProcessing()
    {
        _recTimer?.Stop();
        _recClock = null;
        _recDot.BeginAnimation(OpacityProperty, null);
        _recDot.Opacity = 1;
        _recBadge.BeginAnimation(OpacityProperty, null);
        _recBadge.Opacity = 1;
        _recBadge.Visibility = Visibility.Visible;
        _recText.Text = "处理中…";
    }

    /// <summary>边框显隐：免按住录制且用户关了「录制边框」时，窗口只挂 REC 徽标不画框。</summary>
    /// <param name="visible">false = 描边宽度置 0（窗口与 REC 徽标仍在，只是不画框）；true = 恢复 2px 描边。</param>
    public void SetBorderVisible(bool visible)
    {
        _border.BorderThickness = new Thickness(visible ? 2 : 0);
    }

    void UpdateRecText()
    {
        if (_recClock == null) return;
        double t = _recClock.Elapsed.TotalSeconds;
        _recText.Text = $"REC {(int)(t / 60)}:{t % 60:00.0}";
    }

    System.Windows.Threading.DispatcherTimer? _fadeTimer;

    /// <summary>延时退场：保持常显 <c>seconds</c> 秒，到点后 350ms 淡出并 Hide。</summary>
    /// <param name="seconds">从本次调用起保持常显的秒数。重复调用以最后一次为准（计时被重置）。</param>
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

    /// <summary>一次强调：描边瞬时加亮到 alpha 0xFF / 3px，随后 450ms cubic-out 回落到淡色常态（0x60 / 2px）。</summary>
    /// <remarks>淡色常态由本动画落到；下一次 <see cref="ShowNoActivate"/> 会经 ResetBorder 复位到 0xC0 / 2px。</remarks>
    public void Flash()
    {
        // 亮起后 450ms cubic-out 连续回落到常态，中途不分档，避免看到"跳一下"
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
        // 上屏后 WPF 布局会用 Left/Top/Width/Height(DIP) 回写窗口位置（实测把 681 覆写成 454=÷1.5）。
        // 把同一物理矩形换算成 DIP 同步给 WPF，令它的回写变成幂等，两套坐标收敛到同一个物理矩形。
        if (IsLoaded && !_syncing)
        {
            _syncing = true;
            try
            {
                var d = VisualTreeHelper.GetDpi(this);
                Left = region.X / d.DpiScaleX; Top = region.Y / d.DpiScaleY;
                Width = region.W / d.DpiScaleX; Height = region.H / d.DpiScaleY;
            }
            catch { }
            _syncing = false;
        }
    }
    bool _syncing;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        uint ex = Win32.GetWindowLongW(hwnd, Win32.GWL_EXSTYLE);
        ex |= Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW;
        Win32.SetWindowLongW(hwnd, Win32.GWL_EXSTYLE, ex);
    }
}
