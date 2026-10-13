// Fluent partial：控件工厂——图标按钮、自绘标题栏、滑杆、开关、工具按钮选中态、芯片/小按钮。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FrameCapture.Core;

/// <summary>Fluent 工厂的控件 partial：图标按钮、自绘标题栏、滑杆、开关、悬停反馈。
/// 产出的控件不带任何 x:Name，调用方自行挂事件；全部成员须在 UI 线程调用。</summary>
public static partial class Fluent
{
    #region 图标按钮：悬停淡入圆角高亮 + 按下回缩

    /// <summary>图标按钮：字形即内容，悬停淡入圆角高亮、按下回缩、松手弹性弹回。</summary>
    /// <param name="glyph">Segoe Fluent Icons 的 PUA 字形（选型须经 GlyphProbe 实测，不可心算）。</param>
    /// <param name="tip">悬停提示文案。</param>
    /// <param name="size">宽高（逻辑像素），按钮为正方形。</param>
    /// <param name="fontSize">字形字号。</param>
    /// <param name="fg">字形颜色；null = 主题正文色。</param>
    /// <returns>不可聚焦（Focusable=false）的按钮，不会抢走画布焦点。</returns>
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
        // 「QQ 弹弹」：hover 轻微放大、按下明显回缩、松手用 BackEase 过冲弹回——比纯 CubicEase 更有生命力
        b.MouseEnter += (_, _) => AnimateScale(b, 1.08, Motion.HoverMs, Spring(0.4));
        b.PreviewMouseLeftButtonDown += (_, _) => AnimateScale(b, 0.9, 70);
        b.MouseUp += (_, _) => AnimateScale(b, 1.08, Motion.SpringMs, Spring(0.7));
        b.MouseLeave += (_, _) => AnimateScale(b, 1, 120);
        return b;
    }

    static void AnimateScale(UIElement el, double to, int ms, EasingFunctionBase? ease = null)
    {
        if (el.RenderTransform is not ScaleTransform st) return;
        var dur = new Duration(TimeSpan.FromMilliseconds(ms));
        var ef = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (var p in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var a = new DoubleAnimation(to, dur) { EasingFunction = ef };
            a.Freeze();
            st.BeginAnimation(p, a);
        }
    }

    /// <summary>图标按钮的模板：底色由 TemplateBinding 透传，上面叠一层悬停高亮
    /// （Opacity 进 0.11s、出 0.16s），按下时高亮层换成 Theme.Press 底色。</summary>
    /// <returns>已 Seal 的可复用 ControlTemplate；每次调用重新解析 XAML 得到新实例。</returns>
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
    #endregion

    #region 自绘标题栏：CardBg 条带 + 原生拖窗区

    /// <summary>标题栏高度（逻辑像素）：既是条带高度，也是 <see cref="InstallCaptionDrag"/> 默认的可拖条带。</summary>
    public const double TitleBarHeight = 36;

    /// <summary>构建自绘标题栏（QQ 式）：CardBg 条带 + 图标 + 标题 + 关闭按钮。
    /// 设置窗与帮助窗共用同一件——全盘 UI 对齐同一套密度与视觉语言。</summary>
    /// <param name="glyph">左侧图标的 PUA 字形，取 Accent 色。</param>
    /// <param name="title">标题文字。</param>
    /// <param name="closeTip">关闭按钮的悬停提示。</param>
    /// <returns>条带 Border 与关闭按钮；条带须再经 <see cref="InstallCaptionDrag"/> 才成为原生拖窗区。</returns>
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
    /// <param name="w">目标窗口。已加载则立即挂钩，否则等 SourceInitialized。</param>
    /// <param name="closeBtn">要从拖窗区挖掉的按钮；不可见时不挖（整条都可拖）。</param>
    /// <param name="barHeight">可拖条带高度（逻辑像素），内部按 DPI 换算成物理像素比较。</param>
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
    #endregion

    #region 滑杆：细轨道 + 圆形滑块（悬停放大、光晕）

    /// <summary>细轨道 + 圆形滑块的滑杆样式，配色取主题默认（TrackBg / Accent）。</summary>
    /// <param name="sl">待美化的滑杆；Template 被整体替换。</param>
    public static void StyleSlider(Slider sl) => StyleSlider(sl, Theme.TrackBg, Theme.Accent);

    /// <summary>打开弹层且「本次按压的抬起不关板」：
    /// StaysOpen=false 会把打开后第一次全局抬起当"点了外面"，按下即开板 → 抬手秒关，永远点不进弹层。
    /// 先 StaysOpen=true 开板，下一次抬起再武装自动关闭。
    /// 双钩子（handledEventsToo）：开板按压必在宿主窗；抬起落点要么宿主窗（窗钩子命中→解除，
    /// 之后点外面即关）要么弹层（弹层独立 HWND，其子树钩子命中→解除，抬手进板即常驻可操作）。
    /// 一次性、谁先命中谁摘双钩。</summary>
    /// <param name="pop">要打开的弹层；Child 不能为空。</param>
    /// <param name="hostEl">开板按压所在的宿主元素，钩子挂在它及其子树上。</param>
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

    /// <summary>细轨道 + 圆形滑块的滑杆样式，配色由调用方指定（如滑杆底色与强调色不同于主题默认时）。</summary>
    /// <param name="sl">待美化的滑杆；Template 被整体替换，Focusable 置 false 以免抢焦点。</param>
    /// <param name="trackColor">未填充段（轨道）底色。</param>
    /// <param name="accentColor">已填充段与滑块颜色。</param>
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
                                  <DoubleAnimation Storyboard.TargetName="dot" Storyboard.TargetProperty="Width" To="16" Duration="0:0:0.16"><DoubleAnimation.EasingFunction><BackEase Amplitude="0.6" EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                                  <DoubleAnimation Storyboard.TargetName="dot" Storyboard.TargetProperty="Height" To="16" Duration="0:0:0.16"><DoubleAnimation.EasingFunction><BackEase Amplitude="0.6" EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
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
    #endregion

    #region 开关：滑钮平滑滑动 + 轨道渐变色

    /// <summary>开关的一次状态切换：滑钮滑到目标位（BackEase 轻回弹），轨道底色与描边同步淡变。</summary>
    /// <param name="knob">滑钮；无 RenderTransform 时本方法补一个 TranslateTransform。</param>
    /// <param name="track">轨道；底色与描边必须是 SolidColorBrush 才会做颜色动画。</param>
    /// <param name="on">目标状态。</param>
    /// <param name="travel">滑钮行程（逻辑像素），由调用方按轨道宽与钮宽算出。</param>
    public static void SlideToggle(FrameworkElement knob, Border track, bool on, double travel)
    {
        if (knob.RenderTransform is not TranslateTransform)
            knob.RenderTransform = new TranslateTransform();
        var tt = (TranslateTransform)knob.RenderTransform;
        tt.BeginAnimation(TranslateTransform.XProperty, null);
        tt.X = on ? travel : 0;
        var anim = new DoubleAnimation(on ? travel : 0.0, new Duration(TimeSpan.FromMilliseconds(Motion.StateMs)))
        {
            EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut },
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
        // 滑块恒白（QQ 式），不随状态在 accent 反白 / Theme.Text 之间跳——亮色主题下 Theme.Text=黑，
        // 关态黑滑块看着像没做适配；状态只靠轨道底色表达。
    }
    #endregion

    #region 工具按钮选中态：accent 实心 + 反白图标（与 ✓ 主按钮同一语言）

    /// <summary>accent 底色上的反白色：WCAG 相对亮度（sRGB 线性化）低于 0.35 → 白字，否则黑字。
    /// 线性化不可省：按 0.299/0.587/0.114 直接加权会在 128 一刀切，把 #7F75FF 这类中等饱和紫判成黑字，
    /// 紫底黑字糊成一片。</summary>
    /// <param name="a">底色，即 accent 实心按钮的背景。</param>
    /// <returns>纯白或纯黑两档之一，保证任何 accent 上图标都可读。</returns>
    public static Color OnAccentColor(Color a)
    {
        static double L(double c) { c /= 255.0; return c <= 0.03928 ? c / 12.92 : System.Math.Pow((c + 0.055) / 1.055, 2.4); }
        double lum = 0.2126 * L(a.R) + 0.7152 * L(a.G) + 0.0722 * L(a.B);
        return lum < 0.35 ? Colors.White : Colors.Black;
    }

    /// <summary>工具按钮选中态：accent 实心 + 反白图标（与 ✓ 主按钮同一语言）。底色与图标同步淡变。</summary>
    /// <param name="b">目标按钮；Background / Foreground 被换成本方法持有的可动画画刷。</param>
    /// <param name="on">选中与否；反白色由 <see cref="OnAccentColor"/> 按 accent 亮度决定。</param>
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
    #endregion

    #region 芯片/小按钮：悬停/按下只改底色带过渡（与普通按钮一致）

    /// <summary>小按钮悬停反馈：与普通按钮一致，只改底色带过渡，不缩放——放大动画会盖住相邻控件、显廉价。
    /// 基色只在「画刷实例不是我们这份」时重取（外部换底 = 选中态切换），动画目标永远从固定基色起算，
    /// 因此反复悬停不会把没走完的中途色累积成基色。</summary>
    /// <param name="el">目标芯片或小按钮：Border 或 Control，其余类型没有底色可动。</param>
    /// <param name="scale">保留位，不参与效果。</param>
    public static void ChipHover(FrameworkElement el, double scale = 1.18) => BgHover(el);

    static void BgHover(FrameworkElement el)
    {
        Color baseColor = Colors.Transparent;
        bool hasBase = false;
        SolidColorBrush? ours = null;   // 我们持有并驱动动画的那份画刷（实例身份=「底色还是我们的」）
        // Background 分别定义在 Border / Control 上（FrameworkElement 没有）——只读取当前画刷做颜色动画
        SolidColorBrush? Bg() => el is Border bd ? bd.Background as SolidColorBrush
                            : el is Control cc ? cc.Background as SolidColorBrush : null;
        void SetBg(SolidColorBrush b) { if (el is Border bd) bd.Background = b; else if (el is Control cc) cc.Background = b; }
        void An(Color to, int ms)
        {
            var cur = Bg();
            Color from = cur?.Color ?? baseColor;
            if (cur == null || cur.IsFrozen)   // Theme.B() 产出的画刷是冻结的——动画前先换成当前色的可动画副本
            {
                var nb = new SolidColorBrush(from);
                SetBg(nb);
                cur = nb;
            }
            ours = cur;
            cur.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms))));
        }
        static Color Blend(Color src, Color over)
        {
            double a = over.A / 255.0;
            return Color.FromArgb(src.A,
                (byte)(src.R * (1 - a) + over.R * a),
                (byte)(src.G * (1 - a) + over.G * a),
                (byte)(src.B * (1 - a) + over.B * a));
        }
        el.MouseEnter += (_, _) =>
        {
            var sb = Bg();
            if (sb == null) return;
            if (!hasBase || !ReferenceEquals(sb, ours)) { baseColor = sb.Color; hasBase = true; }   // 外部换底（选中态等）→ 重取基色
            An(Blend(baseColor, Theme.Hover), 140);
        };
        el.MouseLeave += (_, _) =>
        {
            var sb = Bg();
            if (sb == null) return;
            if (hasBase && ReferenceEquals(sb, ours)) An(baseColor, 160);
            else { baseColor = sb.Color; hasBase = true; ours = null; }   // 外部已换底：以新色为基色，别覆盖它
        };
        el.PreviewMouseLeftButtonDown += (_, _) => { if (hasBase) An(Blend(baseColor, Theme.Press), 70); };
        el.MouseUp += (_, _) => { if (hasBase) An(Blend(baseColor, Theme.Hover), 120); };
    }

    /// <summary>开关轨道 hover：进入时边框提亮一档、离开还原（on=accent / off=stroke）。
    /// 与 ToggleRow 内联同款手感，供手写开关（如「截图提示音」）复用，避免同类控件反馈不一致。</summary>
    /// <param name="track">轨道 Border；描边不是可冻结的 SolidColorBrush 时整段跳过。</param>
    /// <param name="isOn">离开时回读当前开关状态，决定描边回 Accent 还是 Stroke。</param>
    public static void ToggleBorderHover(Border track, Func<bool> isOn)
    {
        track.MouseEnter += (_, _) =>
        {
            if (track.BorderBrush is SolidColorBrush bb && bb.CanFreeze)
            {
                var h = new SolidColorBrush(bb.Color);
                track.BorderBrush = h;
                h.Color = Color.FromArgb((byte)Math.Min(255, bb.Color.A + 0x60), bb.Color.R, bb.Color.G, bb.Color.B);
            }
        };
        track.MouseLeave += (_, _) =>
        {
            if (track.BorderBrush is SolidColorBrush bb && bb.CanFreeze)
            {
                var h = new SolidColorBrush(bb.Color);
                track.BorderBrush = h;
                h.Color = isOn() ? Theme.Accent : Theme.Stroke;
            }
        };
    }
    #endregion

}
