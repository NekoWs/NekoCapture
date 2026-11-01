using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NekoCapture.Capture;
using NekoCapture.Native;
using Loc = NekoCapture.Core.Loc;
using K = NekoCapture.Core.K;

namespace NekoCapture.Overlays;

/// <summary>
/// 捕获范围轮廓：点击穿透、不抢焦点，描边位于捕获范围外。
/// 范围与 REC 徽标按自身界面捕获开关决定是否通过 WDA 排除；
/// 普通模式排除失败时保持隐藏。
/// </summary>
public sealed class RegionOutlineWindow : Window
{
    private readonly Border _border;
    private bool _captureExcluded;
    private bool _borderVisible = true;

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
            BorderThickness = new Thickness(Stroke),
            Background = Brushes.Transparent,
            UseLayoutRounding = false,
            SnapsToDevicePixels = true,
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

        // 先把 HWND 建好（此刻还未上屏），再打上"不进捕获"，避免第一帧被录进去
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        EnsureExcludedFromCapture();

        // WPF 使用 DIP，原生位置使用物理像素；位置变化后同步两套坐标。
        LocationChanged += (_, _) =>
        {
            if (_placeGuard || _region.W <= 0) return;
            _placeGuard = true;
            try { Place(_region); } finally { _placeGuard = false; }
        };
    }

    bool _placeGuard;

    /// <summary>描边宽度（物理像素）。窗口按它向外扩，边框因此完整落在捕获范围之外。</summary>
    const int Stroke = 2;

    private RectI _region; // 最近一次目标区域（物理像素）

    /// <summary>设定轮廓矩形，并立刻按物理像素定位（当前 + 已加载 + 下一拍各钉一次，压住 WPF 布局的回写）。</summary>
    /// <param name="region">目标范围：物理像素、虚拟桌面绝对坐标。本窗不用 DIP 定位，传 DIP 会整体缩小一个 DPI 倍率。</param>
    public void SetRegion(RectI region)
    {
        _region = region;
        // 物理像素窗口：直接用 SetWindowPos 精确定位
        Place(region);
        if (IsLoaded) Place(region);
        Dispatcher.BeginInvoke(() => Place(_region));
    }

    /// <summary>显示并复位到常态（用于录制期间的常亮范围指示）。</summary>
    public void ShowNoActivate()
    {
        if (!EnsureExcludedFromCapture() && !TestShowInCapture && !Core.Fluent.CaptureSelfAllowed)
        {
            HideSilently();
            return;
        }
        _fadeTimer?.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        ResetBorder();
        if (!IsVisible) Show();
        // 显示后的布局同步物理位置，延迟回写始终读取当前区域。
        Place(_region);
        Dispatcher.BeginInvoke(() => Place(_region));
    }

    /// <summary>
    /// 按自身界面捕获开关设置范围轮廓的捕获排除，普通模式下幂等复用排除状态。
    /// </summary>
    /// <returns>true 表示已排除；false 表示允许入镜、句柄未就绪或系统调用失败。</returns>
    public bool EnsureExcludedFromCapture()
    {
        if (Core.Fluent.CaptureSelfAllowed)
        {
            _captureExcluded = false;
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) Win32.SetWindowDisplayAffinity(handle, Win32.WDA_NONE);
            return false;
        }
        if (_captureExcluded) return true;
        if (TestShowInCapture) return false; // 仅取证：让轮廓能被截到，便于人工核对位置
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return false;
        if (!Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_EXCLUDEFROMCAPTURE))
        {
            Core.Trace.Log("outline: SetWindowDisplayAffinity failed err="
                + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            return false;
        }
        _captureExcluded = true;
        Core.Trace.Log("outline: capture-excluded via WDA_EXCLUDEFROMCAPTURE");
        return true;
    }

    /// <summary>测试专用（同 NC_TEST_NO_FG 约定，用户环境永不置位）：让轮廓留在捕获画面里，供视觉取证。</summary>
    static readonly bool TestShowInCapture =
        Environment.GetEnvironmentVariable("NC_TEST_OUTLINE_IN_CAPTURE") == "1";

    /// <summary>复位到常态描边（清掉 Flash 的动画残留）。</summary>
    private void ResetBorder()
    {
        _border.BeginAnimation(Border.BorderThicknessProperty, null);
        if (_border.BorderBrush is SolidColorBrush sb) sb.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _border.BorderBrush = new SolidColorBrush(AccentCol(0xC0));
        UpdateStroke();
    }

    static Color AccentCol(byte a)
    {
        var c = Core.Theme.Accent;
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
                new Duration(TimeSpan.FromMilliseconds(Core.Fluent.Motion.EnterMs)))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        // 红点 1s 呼吸脉冲（录制进行感）
        var pulse = new System.Windows.Media.Animation.DoubleAnimation(1, 0.35,
            new Duration(TimeSpan.FromSeconds(1)))
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
        _recText.Text = Loc.T(K.Capture.Working);
    }

    /// <summary>边框显隐：免按住录制且用户关了「录制边框」时，窗口只挂 REC 徽标不画框。</summary>
    /// <param name="visible">是否绘制两物理像素的描边；不影响 REC 徽标。</param>
    public void SetBorderVisible(bool visible)
    {
        _borderVisible = visible;
        UpdateStroke();
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
                var a = new System.Windows.Media.Animation.DoubleAnimation(0, new System.Windows.Duration(TimeSpan.FromMilliseconds(Core.Fluent.Motion.DissolveMs)))
                { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
                a.Completed += (_, _) => Dispatcher.Invoke(Hide);
                BeginAnimation(OpacityProperty, a);
            };
        }
        _fadeTimer.Stop();
        _fadeTimer.Interval = TimeSpan.FromSeconds(seconds);
        _fadeTimer.Start();
    }

    /// <summary>描边瞬时加亮，按强调动效时长回落到淡色常态；宽度始终保持两物理像素。</summary>
    public void Flash()
    {
        // 亮起后按 FlashMs 单向往落到常态，cubic-out 中途不分档，避免看到"跳一下"
        var b = new SolidColorBrush(AccentCol(0xFF));
        _border.BorderBrush = b;
        UpdateStroke();
        var ca = new System.Windows.Media.Animation.ColorAnimation(AccentCol(0x60),
            new Duration(TimeSpan.FromMilliseconds(Core.Fluent.Motion.FlashMs)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        b.BeginAnimation(SolidColorBrush.ColorProperty, ca);
    }

    private static Thickness StrokeThickness(DpiScale dpi) =>
        new(Stroke / dpi.DpiScaleX, Stroke / dpi.DpiScaleY, Stroke / dpi.DpiScaleX, Stroke / dpi.DpiScaleY);

    private void UpdateStroke() =>
        _border.BorderThickness = _borderVisible ? StrokeThickness(VisualTreeHelper.GetDpi(this)) : new Thickness(0);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateStroke();
    }

    /// <summary>描边窗口要盖住的物理矩形：比捕获范围四周各外扩一个描边宽度。
    /// 描边画在窗口自身的内沿，扩出来之后它正好落在捕获范围之外，范围里的像素一个都不碰。</summary>
    /// <param name="region">捕获范围（物理像素、虚拟桌面绝对坐标）。</param>
    /// <returns>外扩后的矩形：左上各减 <see cref="Stroke"/>、宽高各加两倍。</returns>
    public static RectI OuterRect(RectI region) =>
        new(region.X - Stroke, region.Y - Stroke, region.W + Stroke * 2, region.H + Stroke * 2);
    private void Place(RectI region)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var outer = OuterRect(region);
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, outer.X, outer.Y, outer.W, outer.H,
            Win32.SWP_NOACTIVATE);
        UpdateStroke();
        // 定位不改变显隐；原生位置与 WPF 布局共享同一物理矩形。
        if (IsLoaded && !_syncing)
        {
            _syncing = true;
            try
            {
                var d = VisualTreeHelper.GetDpi(this);
                Left = outer.X / d.DpiScaleX; Top = outer.Y / d.DpiScaleY;
                Width = outer.W / d.DpiScaleX; Height = outer.H / d.DpiScaleY;
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
