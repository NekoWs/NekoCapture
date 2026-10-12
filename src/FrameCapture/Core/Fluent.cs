// Fluent 交互细节：悬停淡入、按下回缩、滑杆/开关动画、窗口淡入、全局样式。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FrameCapture.Core;

public static class Fluent
{
    public const string IconFontChain = "Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol";

    static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    static readonly FontFamily IconFont = new(IconFontChain);

    // ---------------- 图标按钮：悬停淡入圆角高亮 + 按下回缩 ----------------

    public static Button IconButton(string glyph, string tip, double size, double fontSize = 15, Brush? fg = null)
    {
        var b = new Button
        {
            Content = glyph,
            Focusable = false,
            ToolTip = tip,
            Width = size,
            Height = size,
            Margin = new Thickness(2),
            FontFamily = IconFont,
            FontSize = fontSize,
            Foreground = fg ?? new SolidColorBrush(Theme.Text),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Template = IconButtonTemplate(),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        b.RenderTransform = new ScaleTransform(1, 1);
        b.PreviewMouseLeftButtonDown += (_, _) => AnimateScale(b, 0.92, 70);
        b.MouseUp += (_, _) => AnimateScale(b, 1, 160);
        b.MouseLeave += (_, _) => AnimateScale(b, 1, 120);
        return b;
    }

    static void AnimateScale(UIElement el, double to, int ms)
    {
        if (el.RenderTransform is not ScaleTransform st) return;
        var dur = new Duration(TimeSpan.FromMilliseconds(ms));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (var p in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var a = new DoubleAnimation(to, dur) { EasingFunction = ease };
            a.Freeze();
            st.BeginAnimation(p, a);
        }
    }

    public static ControlTemplate IconButtonTemplate()
    {
        string xaml = $"""
            <ControlTemplate TargetType="Button"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid>
                <Border x:Name="bd" CornerRadius="7" Background="TBD"/>
                <Border x:Name="ho" CornerRadius="7" Background="{Hex(Theme.Hover)}" Opacity="0"/>
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Grid>
              <ControlTemplate.Triggers>
                <EventTrigger RoutedEvent="UIElement.MouseEnter">
                  <BeginStoryboard><Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ho" Storyboard.TargetProperty="Opacity"
                                     To="1" Duration="0:0:0.11"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                  </Storyboard></BeginStoryboard>
                </EventTrigger>
                <EventTrigger RoutedEvent="UIElement.MouseLeave">
                  <BeginStoryboard><Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ho" Storyboard.TargetProperty="Opacity"
                                     To="0" Duration="0:0:0.16"/>
                  </Storyboard></BeginStoryboard>
                </EventTrigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter TargetName="ho" Property="Background" Value="PRESSCLR"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """;
        xaml = xaml.Replace("PRESSCLR", Hex(Theme.Press)).Replace("TBD", "{TemplateBinding Background}");
        var tpl = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        tpl.Seal();
        return tpl;
    }

    // ---------------- 自绘标题栏（设置/帮助共用，R62）：CardBg 条带 + 图标 + 标题 + 关闭，整条原生可拖 ----------------

    public const double TitleBarHeight = 36;

    /// <summary>构建自绘标题栏（QQ 式）：返回条带 Border 与关闭按钮。整条经 <see cref="InstallCaptionDrag"/> 变原生拖窗区。
    /// 设置窗与帮助窗共用同一件——全盘 UI 对齐同一套密度与视觉语言。</summary>
    public static (Border bar, Button close) TitleBar(string glyph, string title, string closeTip = "关闭 (Esc)")
    {
        var bar = new Border
        {
            Height = TitleBarHeight,
            Background = new SolidColorBrush(Theme.CardBg),
            CornerRadius = new CornerRadius(11, 11, 0, 0),
            Padding = new Thickness(14, 0, 6, 0),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = glyph, FontFamily = IconFont, FontSize = 15,
            Foreground = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0),
        });
        var title1 = new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title1, 1);
        grid.Children.Add(title1);
        var close = IconButton("\uE711", closeTip, 32, 14);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        bar.Child = grid;
        return (bar, close);
    }

    /// <summary>把窗口顶部 barHeight 条带设为原生 HTCAPTION 拖窗区（OS 模态移动：位移 1:1、贴边吸附/多屏语义免费）；
    /// 关闭按钮矩形让位给 WPF 命中。内部自挂 SourceInitialized，调用方无需关心时机。</summary>
    public static void InstallCaptionDrag(Window w, Button closeBtn, double barHeight = TitleBarHeight)
    {
        void Hook()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook((h, msg, wp, lp, ref handled) =>
            {
                const int WM_NCHITTEST = 0x0084, HTCAPTION = 2;
                if (msg != WM_NCHITTEST) return IntPtr.Zero;
                int px = (short)((long)lp & 0xFFFF), py = (short)((long)lp >> 16);
                if (!Native.Win32.GetWindowRect(h, out var rc)) return IntPtr.Zero;
                if (px < rc.Left || px >= rc.Right || py < rc.Top || py >= rc.Bottom) return IntPtr.Zero;
                var src = PresentationSource.FromVisual(w)?.CompositionTarget;
                double scale = src == null ? 1.0 : src.TransformToDevice.M11;
                if ((py - rc.Top) >= barHeight * scale) return IntPtr.Zero; // 标题栏以下交回 WPF
                if (closeBtn.IsVisible)
                {
                    var o = closeBtn.PointToScreen(new Point(0, 0)); // 设备像素
                    var size = new Size(closeBtn.ActualWidth * scale, closeBtn.ActualHeight * scale);
                    if (px >= o.X && px < o.X + size.Width && py >= o.Y && py < o.Y + size.Height) return IntPtr.Zero;
                }
                handled = true;
                return new IntPtr(HTCAPTION);
            });
        }
        if (w.IsLoaded) Hook(); else w.SourceInitialized += (_, _) => Hook();
    }

    // ---------------- 滑杆：细轨道 + 圆形滑块（悬停放大、光晕） ----------------

    public static void StyleSlider(Slider sl) => StyleSlider(sl, Theme.TrackBg, Theme.Accent);

    /// <summary>
    /// 打开弹层且「本次按压的抬起不关板」（R55，R59 移入 Fluent 供编辑器/设置共用）：
    /// StaysOpen=false 会把打开后第一次全局抬起当"点了外面"，按下即开板 → 抬手秒关，永远点不进弹层。
    /// 先 StaysOpen=true 开板，下一次抬起再武装自动关闭。
    /// 双钩子（handledEventsToo）：开板按压必在宿主窗；抬起落点要么宿主窗（窗钩子命中→解除，
    /// 之后点外面即关）要么弹层（弹层独立 HWND，其子树钩子命中→解除，抬手进板即常驻可操作）。
    /// 一次性、谁先命中谁摘双钩。
    /// </summary>
    public static void OpenPopSticky(Popup pop, UIElement hostEl)
    {
        pop.StaysOpen = true;
        pop.IsOpen = true;
        var popEl = (UIElement)pop.Child;
        System.Windows.Input.MouseButtonEventHandler up = null!;
        up = (_, _) =>
        {
            hostEl.RemoveHandler(System.Windows.Input.Mouse.PreviewMouseUpEvent, up);
            popEl.RemoveHandler(System.Windows.Input.Mouse.PreviewMouseUpEvent, up);
            pop.StaysOpen = false;
        };
        hostEl.AddHandler(System.Windows.Input.Mouse.PreviewMouseUpEvent, up, true);
        popEl.AddHandler(System.Windows.Input.Mouse.PreviewMouseUpEvent, up, true);
    }

    public static void StyleSlider(Slider sl, Color trackColor, Color accentColor)
    {
        string track = Hex(trackColor), acc = Hex(accentColor);
        string xaml = $"""
            <ControlTemplate TargetType="Slider"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid VerticalAlignment="Center" Height="20">
                <Border Height="3" CornerRadius="2" Background="{track}"/>
                <Track x:Name="PART_Track">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Focusable="False">
                      <RepeatButton.Template>
                        <ControlTemplate TargetType="RepeatButton">
                          <!-- 负右边距把填充线延到滑块格中心（格宽 22 由光晕决定，圆钮只占 13：
                               不延伸则线与圆之间露 4.5px 底色缝，用户实测抓到） -->
                          <Border Height="3" CornerRadius="2" Background="{acc}" Opacity="0.9" Margin="0,0,-11,0"/>
                        </ControlTemplate>
                      </RepeatButton.Template>
                    </RepeatButton>
                  </Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Focusable="False">
                      <RepeatButton.Template>
                        <ControlTemplate TargetType="RepeatButton">
                          <Border Background="Transparent"/>
                        </ControlTemplate>
                      </RepeatButton.Template>
                    </RepeatButton>
                  </Track.IncreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb x:Name="th" Focusable="False">
                      <Thumb.Template>
                        <ControlTemplate TargetType="Thumb">
                          <Grid>
                            <Ellipse x:Name="halo" Width="22" Height="22" Fill="{acc}" Opacity="0"/>
                            <Ellipse x:Name="dot" Width="13" Height="13" Fill="{acc}" Stroke="{track}" StrokeThickness="1"/>
                          </Grid>
                          <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                              <Trigger.EnterActions>
                                <BeginStoryboard><Storyboard>
                                  <DoubleAnimation Storyboard.TargetName="dot" Storyboard.TargetProperty="Width" To="16" Duration="0:0:0.12"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                                  <DoubleAnimation Storyboard.TargetName="dot" Storyboard.TargetProperty="Height" To="16" Duration="0:0:0.12"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                                  <DoubleAnimation Storyboard.TargetName="halo" Storyboard.TargetProperty="Opacity" To="0.25" Duration="0:0:0.18"/>
                                </Storyboard></BeginStoryboard>
                              </Trigger.EnterActions>
                              <Trigger.ExitActions>
                                <BeginStoryboard><Storyboard>
                                  <DoubleAnimation Storyboard.TargetName="dot" Storyboard.TargetProperty="Width" To="13" Duration="0:0:0.14"/>
                                  <DoubleAnimation Storyboard.TargetName="dot" Storyboard.TargetProperty="Height" To="13" Duration="0:0:0.14"/>
                                  <DoubleAnimation Storyboard.TargetName="halo" Storyboard.TargetProperty="Opacity" To="0" Duration="0:0:0.14"/>
                                </Storyboard></BeginStoryboard>
                              </Trigger.ExitActions>
                            </Trigger>
                          </ControlTemplate.Triggers>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                </Track>
              </Grid>
            </ControlTemplate>
            """;
        var tpl = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        tpl.Seal();
        sl.Template = tpl;
        sl.Height = 20;
    }

    // ---------------- 开关：滑钮平滑滑动 + 轨道渐变色 ----------------

    public static void SlideToggle(FrameworkElement knob, Border track, bool on, double travel)
    {
        if (knob.RenderTransform is not TranslateTransform)
            knob.RenderTransform = new TranslateTransform();
        var tt = (TranslateTransform)knob.RenderTransform;
        tt.BeginAnimation(TranslateTransform.XProperty, null);
        tt.X = on ? travel : 0;
        var anim = new DoubleAnimation(on ? travel : 0.0, new Duration(TimeSpan.FromMilliseconds(Motion.StateMs)))
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut },
        };
        tt.BeginAnimation(TranslateTransform.XProperty, anim);
        if (track.Background is SolidColorBrush bgOld)
        {
            var bg = new SolidColorBrush(bgOld.Color);
            track.Background = bg;
            bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Theme.Accent : Theme.TrackBg, new Duration(TimeSpan.FromMilliseconds(Motion.StateMs))));
        }
        if (track.BorderBrush is SolidColorBrush bbOld)
        {
            var bb = new SolidColorBrush(bbOld.Color);
            track.BorderBrush = bb;
            bb.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Theme.Accent : Theme.Stroke, new Duration(TimeSpan.FromMilliseconds(Motion.StateMs))));
        }
        if (knob is Shape sh && sh.Fill is SolidColorBrush kfOld)
        {
            var kf = new SolidColorBrush(kfOld.Color);
            sh.Fill = kf;
            kf.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Color.FromRgb(0, 0, 0) : Theme.Text, new Duration(TimeSpan.FromMilliseconds(Motion.StateMs))));
        }
    }

    // ---------------- 窗口淡入 ----------------

    /// <summary>全站动效令牌：入场 cubic-out，退场 cubic-in，节奏统一（对标 Fluent Dependency/Transient）。</summary>
    public static class Motion
    {
        public const int EnterMs = 190;      // 弹出层/浮层入场
        public const int EditorEnterMs = 240; // 编辑器入场（大表面稍慢半拍）
        public const int ExitMs = 150;       // 退场（比入场快 = 更干脆）
        public const int StaggerMs = 110;    // 分层错位间隔
        public const int HoverMs = 110;      // hover 反馈
        public const int StateMs = 150;      // 状态切换（开关/选中），与 Exit 同速
        public const int ThemeMs = 260;      // 主题交叉淡入
    }

    /// <summary>QQ 式真无框窗口：WindowStyle=None 的 WPF 窗口仍保留 DWM 非客户区（顶部白条）。
    /// WindowChrome（WPF 官方方案，内部处理 WM_NCCALCSIZE）让客户区覆盖整窗——白条不再绘制，
    /// 且保留 WS_THICKFRAME → 原生阴影与 Win11 圆角、原生边缘调整大小，硬件加速不受影响
    /// （勿用 AllowsTransparency，软件渲染代价大）。CaptionHeight=0：拖拽由 DragMove 自理。</summary>
    public static void BorderlessNative(Window w, double resizeBorder = 5)
    {
        System.Windows.Shell.WindowChrome.SetWindowChrome(w, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(resizeBorder),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            int round = Native.Win32.DWMWCP_ROUND;
            Native.Win32.DwmSetWindowAttribute(hwnd, Native.Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        };
    }

    static void SetAlphaZero(IntPtr hwnd)
    {
        Native.Win32.SetWindowLongW(hwnd, Native.Win32.GWL_EXSTYLE,
            Native.Win32.GetWindowLongW(hwnd, Native.Win32.GWL_EXSTYLE) | (uint)Native.Win32.WS_EX_LAYERED);
        Native.Win32.SetLayeredWindowAttributes(hwnd, IntPtr.Zero, 0, Native.Win32.LWA_ALPHA);
    }

    /// <summary>消除入场黑闪：WPF 的 Opacity=0 要等首帧渲染才应用到 HWND，之前 DWM 显示不透明黑面（或类背景白闪）。
    /// SourceInitialized（HWND 已建、未渲染）置 Win32 分层 alpha=0；WPF 首帧准备会冲掉手工样式，
    /// 故每次布局再补一次。默认 ContentRendered 放行；holdPastRender=true 时由返回的 release 决定放行时机
    /// （离屏首帧方案需要回屏后继续压住 alpha）。返回的 release 幂等。</summary>
    public static Action PreTransparent(Window w, bool holdPastRender = false)
    {
        bool released = false;
        void Apply()
        {
            if (released) return;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetAlphaZero(hwnd);
        }
        w.SourceInitialized += (_, _) => Apply();
        w.LayoutUpdated += (_, _) => Apply(); // 首帧前有多次布局，逐次补回 alpha=0
        if (!holdPastRender) w.ContentRendered += (_, _) => released = true;
        return () => released = true;
    }

    /// <summary>紧凑弹窗（设置/帮助）入场：淡入 + 0.96→1 内容放大（WinUI Dialog 范式）。</summary>
    public static void PopupEnter(Window w)
    {
        w.Opacity = 0;
        // 同 FadeIn：分层窗绝不能上手工 LWA_ALPHA=0（DWM 下无人清除=永久隐形），直接靠 WPF 淡入
        if (w.AllowsTransparency)
        {
            w.ContentRendered += (_, _) =>
                w.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(Motion.EnterMs)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            return;
        }
        PreTransparent(w);
        w.ContentRendered += (_, _) =>
        {
            var dur = new Duration(TimeSpan.FromMilliseconds(Motion.EnterMs));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            w.BeginAnimation(System.Windows.UIElement.OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            if (w.Content is FrameworkElement fe && fe.RenderTransform == null)
            {
                fe.RenderTransformOrigin = new Point(0.5, 0.5);
                var st = new ScaleTransform(0.96, 0.96);
                fe.RenderTransform = st;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            }
        };
    }

    static readonly HashSet<Window> _fadingOut = new();

    /// <summary>退场：淡出 150ms cubic-in 后再真正 Close（主题切换的交叉淡入不使用此路径）。</summary>
    public static void CloseWithFade(Window w)
    {
        if (!_fadingOut.Add(w)) return; // 动画已在进行中（含按键重复触发的重入）
        w.Closing += (_, e) => { if (_fadingOut.Contains(w)) e.Cancel = true; }; // 动画期间任何 Close 一律拦下
        var cur = w.Opacity; // 先取当前有效值（PopupEnter 把基础值设成了 0，清动画后会闪没）
        var a = new DoubleAnimation(cur, 0.0, new Duration(TimeSpan.FromMilliseconds(Motion.ExitMs)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        a.Completed += (_, _) => { _fadingOut.Remove(w); w.Close(); };
        w.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
        w.Opacity = cur;
        w.BeginAnimation(System.Windows.UIElement.OpacityProperty, a);
    }

    public static void FadeIn(Window w, int ms = 160, double risePx = 0, bool offscreenFirstFrame = false)
    {
        w.Opacity = 0;
        // 分层窗（AllowsTransparency）自创建即 per-pixel alpha，首帧前没有黑/白闪；
        // 且 DWM 合成下 WPF 不再调 UpdateLayeredWindow——手工 SetLayeredWindowAttributes(alpha=0)
        // 永远不会被 WPF 清掉，用了就是整窗永久隐形（R11 教训：选区窗遮罩+提示全隐形 6 轮无人察觉）。
        if (w.AllowsTransparency)
        {
            w.ContentRendered += (_, _) =>
                w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(ms)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            return;
        }
        var releasePre = PreTransparent(w, holdPastRender: offscreenFirstFrame);
        void Start()
        {
            releasePre();
            var dur = new Duration(TimeSpan.FromMilliseconds(ms));
            var a = new DoubleAnimation(1, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            w.BeginAnimation(UIElement.OpacityProperty, a);
            if (risePx > 0 && w.Content is FrameworkElement fe && fe.RenderTransform is not TransformGroup)
            {
                var tt = new TranslateTransform(0, risePx);
                fe.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }
        if (offscreenFirstFrame)
        {
            // WPF 首帧渲染前窗口是不透明黑面（且会冲掉手工 alpha）：先在屏外渲染首帧，
            // ContentRendered 后回到真实位置——黑相永远发生在屏幕外，用户只见淡入。
            void Park()
            {
                double homeL = w.Left, homeT = w.Top;
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -32000; w.Top = -32000;
                w.ContentRendered += (_, _) =>
                {
                    w.Left = homeL; w.Top = homeT;
                    // 移回屏内 DWM 可能闪一帧不透明：同步压回 alpha=0，下一渲染帧再放行淡入
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                    if (hwnd != IntPtr.Zero) SetAlphaZero(hwnd);
                    EventHandler step = null!;
                    step = (_, _) =>
                    {
                        System.Windows.Media.CompositionTarget.Rendering -= step;
                        Start();
                    };
                    System.Windows.Media.CompositionTarget.Rendering += step;
                };
            }
            if (w.IsInitialized) Park(); else w.SourceInitialized += (_, _) => Park();
        }
        else w.ContentRendered += (_, _) => Start();
    }

    // ---------------- 工具按钮选中态：accent 实心 + 反白图标（与 ✓ 主按钮同一语言） ----------------

    /// <summary>accent 底色上的反白色：按亮度取黑/白（浅色 accent 配黑字，深色 accent 配白字）。</summary>
    public static Color OnAccentColor(Color accent)
        => (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) > 128 ? Colors.Black : Colors.White;

    public static void HighlightBtn(Button b, bool on)
    {
        Color to = on ? Theme.Accent : Colors.Transparent;
        var holder = b.Background is SolidColorBrush cur ? new SolidColorBrush(cur.Color) : new SolidColorBrush(Colors.Transparent);
        b.Background = holder;
        holder.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(Motion.StateMs))));
        // 反白图标与底色同步淡变（StateMs：这是状态切换，不是 hover）
        var fgFrom = b.Foreground is SolidColorBrush curFg ? curFg.Color : Theme.Text;
        var fg = new SolidColorBrush(fgFrom);
        b.Foreground = fg;
        fg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? OnAccentColor(Theme.Accent) : Theme.Text, new Duration(TimeSpan.FromMilliseconds(Motion.StateMs))));
    }

    // ---------------- 芯片（颜色选择）：悬停放大 + 按下回缩 ----------------

    public static void ChipHover(FrameworkElement el, double scale = 1.18)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new ScaleTransform(1, 1);
        el.MouseEnter += (_, _) => AnimateScale(el, scale, 120);
        el.MouseLeave += (_, _) => AnimateScale(el, 1, 140);
        el.MouseUp += (_, _) => AnimateScale(el, scale, 120);
        el.PreviewMouseLeftButtonDown += (_, _) => AnimateScale(el, 0.9, 70);
    }

    // ---------------- 全局样式：主题化 ToolTip（淡入卡片）+ 细滚动条 ----------------

    public static void ApplyGlobalStyles()
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;
        app.Resources[typeof(System.Windows.Controls.ToolTip)] = ToolTipStyle();
        app.Resources[typeof(ScrollBar)] = ScrollBarStyle();
    }

    public static Style ToolTipStyle()
    {
        string xaml = $"""
            <Style TargetType="ToolTip"
                   xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Setter Property="OverridesDefaultStyle" Value="True"/>
              <Setter Property="HasDropShadow" Value="False"/>
              <Setter Property="FontSize" Value="12"/>
              <Setter Property="FontFamily" Value="Microsoft YaHei UI, Microsoft YaHei, Segoe UI"/>
              <Setter Property="TextBlock.Foreground" Value="{Hex(Theme.Text)}"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ToolTip">
                    <Border x:Name="bd" CornerRadius="8" Background="{Hex(Theme.CardBg)}"
                            BorderBrush="{Hex(Theme.Stroke)}" BorderThickness="1"
                            Padding="10,7,10,7" Opacity="0">
                      <Border.Effect><DropShadowEffect BlurRadius="14" ShadowDepth="3" Opacity="0.35"/></Border.Effect>
                      <ContentPresenter VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                      <EventTrigger RoutedEvent="FrameworkElement.Loaded">
                        <BeginStoryboard><Storyboard>
                          <DoubleAnimation Storyboard.TargetName="bd" Storyboard.TargetProperty="Opacity"
                                           From="0" To="1" Duration="0:0:0.14"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                        </Storyboard></BeginStoryboard>
                      </EventTrigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """;
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    public static Style ScrollBarStyle()
    {
        string thumb = Hex(Theme.Stroke), thumbHi = Hex(Theme.Press);
        string sbXaml = $"""
            <Style TargetType="ScrollBar"
                   xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Setter Property="Background" Value="Transparent"/>
              <Setter Property="Width" Value="10"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Grid Background="Transparent">
                      <Track x:Name="PART_Track" IsDirectionReversed="True">
                        <Track.DecreaseRepeatButton>
                          <RepeatButton Focusable="False">
                            <RepeatButton.Template>
                              <ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate>
                            </RepeatButton.Template>
                          </RepeatButton>
                        </Track.DecreaseRepeatButton>
                        <Track.IncreaseRepeatButton>
                          <RepeatButton Focusable="False">
                            <RepeatButton.Template>
                              <ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate>
                            </RepeatButton.Template>
                          </RepeatButton>
                        </Track.IncreaseRepeatButton>
                        <Track.Thumb>
                          <Thumb Focusable="False">
                            <Thumb.Template>
                              <ControlTemplate TargetType="Thumb">
                                <Border x:Name="tb" CornerRadius="4" Background="{thumb}" Margin="2,1,3,1"/>
                                <ControlTemplate.Triggers>
                                  <Trigger Property="IsMouseOver" Value="True">
                                    <Setter TargetName="tb" Property="Background" Value="{thumbHi}"/>
                                  </Trigger>
                                </ControlTemplate.Triggers>
                              </ControlTemplate>
                            </Thumb.Template>
                          </Thumb>
                        </Track.Thumb>
                      </Track>
                    </Grid>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
              <Style.Triggers>
                <Trigger Property="Orientation" Value="Horizontal">
                  <Setter Property="Width" Value="Auto"/>
                  <Setter Property="Height" Value="10"/>
                </Trigger>
              </Style.Triggers>
            </Style>
            """;
        var st = (Style)System.Windows.Markup.XamlReader.Parse(sbXaml);
        return st;
    }

    public static ResourceDictionary MenuResources()
    {
        string xaml = $"""
            <ResourceDictionary
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style TargetType="ContextMenu">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="TextElement.Foreground" Value="{Hex(Theme.Text)}"/>
                <Setter Property="MinWidth" Value="176"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="ContextMenu">
                      <Border CornerRadius="8" Background="{Hex(Theme.CardBg)}"
                              BorderBrush="{Hex(Theme.Stroke)}" BorderThickness="1" Padding="3,4,3,4">
                        <Border.Effect><DropShadowEffect BlurRadius="16" ShadowDepth="4" Opacity="0.4"/></Border.Effect>
                        <ItemsPresenter/>
                      </Border>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
              <Style TargetType="Separator">
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="Separator">
                      <Border Height="1" Background="{Hex(Theme.Stroke)}" Margin="8,3,8,3"/>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
              <Style TargetType="MenuItem">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="FontSize" Value="12"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="MenuItem">
                      <Grid><Border x:Name="bd" CornerRadius="5" Background="Transparent">
                        <Grid Height="27">
                          <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="24"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                          </Grid.ColumnDefinitions>
                          <TextBlock x:Name="chk" Grid.Column="0" Text="" FontFamily="Segoe Fluent Icons"
                                     FontSize="11" HorizontalAlignment="Center" VerticalAlignment="Center"
                                     Foreground="{Hex(Theme.Accent)}"/>
                          <ContentPresenter Grid.Column="1" ContentSource="Header" VerticalAlignment="Center" Margin="0,0,14,0"/><TextBlock x:Name="arrow" Grid.Column="2" Text="&#xE76C;" FontFamily="Segoe Fluent Icons" FontSize="10" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0" Foreground="{Hex(Theme.Text)}" Visibility="Collapsed"/>
                        </Grid>
                      </Border>
                      <Popup x:Name="PART_Popup" AllowsTransparency="True" Focusable="False" Placement="Right"
                             HorizontalOffset="-4" PopupAnimation="Fade">
                        <Border CornerRadius="8" Background="{Hex(Theme.CardBg)}" BorderBrush="{Hex(Theme.Stroke)}"
                                BorderThickness="1" Padding="3,4,3,4" Margin="0,0,10,10">
                          <Border.Effect><DropShadowEffect BlurRadius="16" ShadowDepth="4" Opacity="0.4"/></Border.Effect>
                          <ItemsPresenter/>
                        </Border>
                      </Popup>
                      </Grid>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                          <Setter TargetName="bd" Property="Background" Value="{Hex(Theme.Hover)}"/>
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                          <Setter TargetName="chk" Property="Text" Value="&#xE73E;"/></Trigger><Trigger Property="Role" Value="SubmenuHeader"><Setter TargetName="arrow" Property="Visibility" Value="Visible"/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
            </ResourceDictionary>
            """;
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
    }
}
