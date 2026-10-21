using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 滚动截图控制条：选区后常驻，用户只管滚动目标窗口，后台定时器自动找重叠拼接；
/// 本条只做进度显示 + 自动滚动开关 + 完成/取消。WDA_EXCLUDEFROMCAPTURE 排除，绝不入镜。
/// 关键手感：ShowActivated=false **不抢焦点**——目标窗口保持焦点，滚轮/键盘滚动照常生效
/// （若本条抢了焦点，关闭「悬停滚动」的系统里滚轮会落到本条、目标窗纹丝不动）。
/// </summary>
public sealed class ScrollHintWindow : Window
{
    /// <value>「完成」/「取消」的回调；未挂时点按钮无副作用。
    /// 由按钮点击或 Enter/Esc 在 UI 线程触发；<c>CloseSelf</c> 不会调用它们。</value>
    public Action? OnDone, OnCancel;

    /// <value>「自动滚动」按钮的点击回调；未挂时点按钮无副作用。
    /// 本类只负责按钮状态与提示行，滚动本身由挂上来的控制器决定。</value>
    public Action? OnToggleAutoScroll;

    readonly TextBlock _progress;
    readonly TextBlock _hint;
    readonly Button _auto;
    bool _closing;
    bool _autoOn;   // 「自动滚动」开关态：决定提示行文案，拼接进度刷新时据此复位
    int _lastSegs = 1;   // 与构造里初始「已拼接 1 段」对齐，首帧不弹，之后每拼接一段弹一下

    // 提示行按当前语言取词，因此是取值属性而非常量
    static string DefaultHint => Loc.T(K.Capture.ScrollTheTargetWindow);
    static string AutoHint => Loc.T(K.Capture.AutoScrollingStops);
    const double W = 458;      // DIP 宽（估算落点用；实际 SizeToContent）
    const double EstH = 96;    // 估算高（落点用）

    /// <value>当前显示中的控制条，无则 null；<c>Closed</c> 时自行清零。</value>
    public static ScrollHintWindow? Current;

    /// <summary>
    /// 构造滚动截图控制条：只建界面、不起会话——调用方须自行 Show 并挂 <see cref="OnDone"/>/<see cref="OnCancel"/>。
    /// 构造期即定好落点（Show 前定位，无首帧闪烁），WDA 排除在 SourceInitialized 里设。
    /// 必须在 UI 线程构造。
    /// </summary>
    /// <param name="region">要滚的选区，物理像素、虚拟桌面绝对坐标；控制条贴其上方居中，放不下则落回区域上沿内侧。</param>
    /// <param name="monitorDevice">选区所在显示器 device name；按设备取不到时按区域坐标就近回退。</param>
    /// <param name="entranceLeadMs">入场起跳前的等待（ms）。滚动流程的三层浮层同刻 Show，
    /// 由调用方给出各自的错位量，让它们按取景层 → 预览卡 → 控制条的顺序依次落位。</param>
    public ScrollHintWindow(RectI region, string monitorDevice, int entranceLeadMs = 0)
    {
        Title = "FrameCapture Scroll";
        Fluent.BorderlessOverlay(this);   // 不抢焦点：目标窗口继续接收滚轮/键盘滚动

        // 落点：区域顶部居中，优先置于区域**上方**（不遮挡要滚动的内容）；上方放不下则贴区域上沿（WDA 排除，不入镜）
        var mon = Monitors.ByDevice(monitorDevice) ?? Monitors.Nearest(region.X, region.Y);
        var (sx, sy) = Monitors.DpiScaleOf(mon);
        int barW = (int)(W * sx), barH = (int)(EstH * sy);
        int left = region.X + region.W / 2 - barW / 2;
        int top = region.Y - barH - 10;
        if (top < mon.Bounds.Y) top = region.Y + 10;   // 区域顶到屏幕边→改贴区域上沿内侧
        left = Math.Max(mon.Bounds.X + 6, Math.Min(left, mon.Bounds.X + mon.Bounds.W - barW - 6));
        Left = left / sx; Top = top / sy;

        var card = new Border
        {
            Width = W,
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = Core.Fluent.CardEdge(),   // 上缘亮下缘暗：暗色里投影看不见，靠这条边说清卡片是浮着的
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 14, 12),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black },
        };

        var root = new StackPanel();

        // 标题行：滚动截图 + 实时进度（右对齐）
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Common.ScrollingCapture),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Theme.Text),
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _progress = new TextBlock
        {
            Text = Loc.T(K.Capture.SegmentStitched),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Core.Fluent.AccentText()),
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_progress, 1);
        head.Children.Add(_progress);
        root.Children.Add(head);

        _hint = new TextBlock
        {
            Text = DefaultHint,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Theme.TextDim),
            FontFamily = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 4, 0),
        };
        root.Children.Add(_hint);

        // 按钮行：自动滚动（开关）· 完成（强调）· 取消
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        // 完成按钮前景随强调色明暗自适应（浅强调色→黑字，深→白字），同 OCR 窗 OnAccentColor，避免浅底白字不可读
        var done = MakeBtn(Loc.T(K.Capture.Done), Theme.Accent, Fluent.OnAccentColor(Theme.Accent));
        done.Click += (_, _) => OnDone?.Invoke();
        var cancel = MakeBtn(Loc.T(K.Common.Cancel), Theme.Hover, Theme.TextDim);
        cancel.Click += (_, _) => OnCancel?.Invoke();
        // 开关按钮的关态与「取消」同色，开态换强调底——配色与文案一起由 SetAutoScrolling 决定
        _auto = MakeBtn(Loc.T(K.Capture.AutoScroll), Theme.Hover, Theme.TextDim);
        _auto.Click += (_, _) => OnToggleAutoScroll?.Invoke();
        // 悬停只改底色带过渡，不缩放——缩放会让一排小按钮互相挤动
        Fluent.Springy(done);
        Fluent.Springy(cancel);
        Fluent.Springy(_auto);
        btns.Children.Add(_auto);
        btns.Children.Add(new Border { Width = 8 });
        btns.Children.Add(done);
        btns.Children.Add(new Border { Width = 8 });
        btns.Children.Add(cancel);
        root.Children.Add(btns);

        card.Child = root;
        Content = card;

        // 键在 HWND 层处理：本条被点击获得焦点后，Esc/Enter 可作键盘等价（不依赖它常驻焦点）
        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(h);
            Core.Fluent.ExcludeFromCapture(h);
            if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource src)
                src.AddHook((hwnd, msg, w, l, ref handled) =>
                {
                    const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        int vk = w.ToInt32();
                        if (vk == 0x0D) { handled = true; Dispatcher.BeginInvoke(() => OnDone?.Invoke()); }    // Enter
                        else if (vk == 0x1B) { handled = true; Dispatcher.BeginInvoke(() => OnCancel?.Invoke()); }  // Esc
                    }
                    return IntPtr.Zero;
                });
        };

        Closed += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
        Trace.Log("scroll bar shown");
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true, leadMs: entranceLeadMs);
    }

    /// <summary>刷新进度：段数与拼接后总高，并把提示行复位到当前开关态的文案（清掉 <see cref="FlashHint"/> 的提醒）。
    /// 段数变化才让数字弹一下，刷同一值不动画。</summary>
    /// <param name="segments">已拼接段数，从 1 起（基准帧算第 1 段）。</param>
    /// <param name="heightPx">拼接后位图的总高，物理像素。</param>
    public void SetProgress(int segments, int heightPx)
    {
        _progress.Text = Loc.F(K.Capture.SegmentsStitchedPxTall, segments, heightPx);
        _hint.Text = _autoOn ? AutoHint : DefaultHint;
        _hint.Foreground = new SolidColorBrush(Theme.TextDim);
        if (segments != _lastSegs) { _lastSegs = segments; PopProgress(); }
    }

    /// <summary>切换「自动滚动」的按钮文案、配色与提示行。底色与文案同拍换、不做颜色过渡——
    /// 文案没法动画，半淡的底色和已经换掉的文案对不齐。</summary>
    /// <param name="on">开＝「停止自动滚动」+ 强调底，关＝「自动滚动」+ 次级底。</param>
    public void SetAutoScrolling(bool on)
    {
        _autoOn = on;
        _auto.Content = on ? Loc.T(K.Capture.StopAutoScroll) : Loc.T(K.Capture.AutoScroll);
        _auto.Background = new SolidColorBrush(on ? Theme.Accent : Theme.Hover);
        _auto.Foreground = new SolidColorBrush(on ? Fluent.OnAccentColor(Theme.Accent) : Theme.TextDim);
        _hint.Text = on ? AutoHint : DefaultHint;
        _hint.Foreground = new SolidColorBrush(Theme.TextDim);
    }

    /// <summary>每成功拼接一段，进度数字弹一下（0.72→1.0 BackEase 过冲，右缘锚定不撞标题）——滚动拼接时的弹性即时反馈，同倒计时数字弹入。</summary>
    void PopProgress()
    {
        var st = new ScaleTransform(0.72, 0.72);
        _progress.RenderTransformOrigin = new Point(1, 0.5);
        _progress.RenderTransform = st;
        var a = new DoubleAnimation(1.0, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.SpringMs)))
        { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>刷一句反馈（如「滚动太快」），琥珀色提醒；下次成功拼接由 SetProgress 复位。</summary>
    /// <param name="msg">整句提示文案，原样上屏；超宽由 TextWrapping 折行，本方法不做长度裁剪。</param>
    public void FlashHint(string msg)
    {
        _hint.Text = msg;
        _hint.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xA8, 0x4B)); // 琥珀色提醒
    }

    /// <summary>带淡出收起本控制条；重复调用无副作用。不触发 OnDone/OnCancel，也不停拼接定时器——收尾由调用方负责。</summary>
    public void CloseSelf()
    {
        if (_closing) return;
        _closing = true;
        Fluent.CloseWithFade(this);
    }

    static Button MakeBtn(string text, Color bg, Color fg) => Fluent.RoundButton(text, bg, fg, 14, hoverFade: false);
}
