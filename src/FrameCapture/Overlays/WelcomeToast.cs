using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 首次启动欢迎卡：新用户装完只看到托盘图标、不知按什么键——这是「人人都爱用」的头号上手摩擦。
/// 弹一张不抢焦点的卡片，直接点破两个热键 + 关键手感，给一个「查看帮助」入口；
/// 首启路径弹一次——调用方置位 FirstRunHintShown 后，自动弹出的条件即消失。
/// 复用 CaptureToast 的浮窗骨架：ShowActivated=false 不打断输入、WDA 排除不入镜、悬停暂停自动消失、Esc/点关闭。
/// </summary>
public sealed class WelcomeToast : Window
{
    static WelcomeToast? _current;

    readonly DispatcherTimer _dismiss;
    bool _closing;

    const double W = 372;      // DIP 宽
    const double EstH = 168;   // 估算高（落点用；实际 SizeToContent）

    /// <summary>
    /// 弹出欢迎卡：同一时刻只保留一张，再次调用先收起上一张。
    /// 非激活浮窗（ShowActivated=false），不打断用户当前输入；必须在 UI 线程调用。
    /// 本方法不写设置——是否记为「已看过」（FirstRunHintShown）由调用方决定。
    /// </summary>
    /// <param name="captureHotkey">「截图」键位的展示文案，原样上屏，不做二次解析。</param>
    /// <param name="recordHotkey">「录制」键位的展示文案，原样上屏。</param>
    public static void Show(string captureHotkey, string recordHotkey)
    {
        _current?.Close();
        _current = new WelcomeToast(captureHotkey, recordHotkey);
        _current.Show(); // ShowActivated=false：不打断用户当前输入
    }

    WelcomeToast(string captureHotkey, string recordHotkey)
    {
        Title = "FrameCapture Welcome";
        Fluent.BorderlessOverlay(this);

        // 落点：主屏工作区右下角（靠托盘图标，让用户把「图标在这」和「怎么开始」联系起来）。
        // SystemParameters.WorkArea 已是 DIP 且排除任务栏，直接用作 Left/Top，无需 DPI 换算。
        var wa = SystemParameters.WorkArea;
        Left = Math.Max(wa.Left, wa.Right - W - 16);
        Top = Math.Max(wa.Top, wa.Bottom - EstH - 16);

        var card = new Border
        {
            Width = W,
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 12, 14),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black },
        };

        var root = new StackPanel();
        Grid.SetIsSharedSizeScope(root, true);   // 两行热键的标签列共用一个宽度，键位才对齐

        // 标题行：标题 + 关闭 X
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Capture.FramecaptureIsReady),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Theme.Text),
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var close = Fluent.IconButton("\uE711", Loc.T(K.Common.Close), 26, 12,
            new SolidColorBrush(Theme.TextDim));
        close.Click += (_, _) => Dismiss();
        Grid.SetColumn(close, 1);
        head.Children.Add(close);
        root.Children.Add(head);

        // 两个热键：标签（暗）+ 键位（强调色加粗），一眼看清「按什么」
        root.Children.Add(HotkeyLine(Loc.T(K.Common.Capture), captureHotkey));
        root.Children.Add(HotkeyLine(Loc.T(K.Common.Recording), recordHotkey));

        root.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Capture.PickARegionToCapture),
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Theme.TextDim),
            FontFamily = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 4, 0),
        });

        // 查看帮助入口（打开帮助窗并收起本卡）
        var help = new Button
        {
            Content = Loc.T(K.Capture.SeeTheHelpAndEvery),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)), // 前景随强调色明暗自适应，浅强调色下不致白字不可读
            Background = new SolidColorBrush(Theme.Accent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 12, 0, 0),
            Template = RoundedBtnTemplate(),
        };
        help.Click += (_, _) => { Trace.Log("welcome→help"); HelpWindow.ShowOrFocus(); Dismiss(); };
        Fluent.Springy(help);   // 悬停只改底色带过渡，不缩放（与滚动控制条按钮同款反馈）
        root.Children.Add(help);

        card.Child = root;
        Content = card;
        // 弹性入场：与 CaptureToast 同款放大登场
        ContentRendered += (_, _) => Fluent.PopIn(card);

        // 键盘等价：浮窗不抢焦点，WPF 焦点路由收不到键——直接在 HWND 层处理 Esc
        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(h);
            // 欢迎卡默认不出现在任何截图里；FC_TOAST_NOEXCLUDE=1 仅供视觉审查截图（与 CaptureToast 同一开关）
            if (Environment.GetEnvironmentVariable("FC_TOAST_NOEXCLUDE") != "1")
                Win32.SetWindowDisplayAffinity(h, Win32.WDA_EXCLUDEFROMCAPTURE);
            if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource src)
                src.AddHook((hwnd, msg, w, l, ref handled) =>
                {
                    const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
                    if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && w.ToInt32() == 0x1B) // Esc
                    {
                        handled = true;
                        Dispatcher.BeginInvoke(Dismiss);
                    }
                    return IntPtr.Zero;
                });
        };

        // 上手信息给足阅读时间：12s，悬停暂停，移开再给 12s
        _dismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _dismiss.Tick += (_, _) => Dismiss();
        _dismiss.Start();
        MouseEnter += (_, _) => _dismiss.Stop();
        MouseLeave += (_, _) => _dismiss.Start();

        Closed += (_, _) => { _dismiss.Stop(); if (ReferenceEquals(_current, this)) _current = null; };

        Trace.Log("welcome shown");
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }

    /// <summary>一行「标签 + 键位」。标签列走 SharedSizeGroup：两行的键位对齐，列宽由两种语言里较宽的那条决定。
    /// 写死宽度只量得出中文那两个汉字，英文的 Capture / Recording 会被切掉半截，还和键位挤在一起。</summary>
    /// <param name="label">这条热键是干什么的，暗色小字。</param>
    /// <param name="key">当前生效的键位串，强调色加粗。</param>
    /// <returns>一行两列的版面，交给欢迎卡纵向堆叠。</returns>
    static FrameworkElement HotkeyLine(string label, string key)
    {
        var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "HotkeyLabel" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.TextDim),
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(lb, 0);
        g.Children.Add(lb);
        // 键位用键帽画，不写成彩色文字：与编辑器、设置页同一副长相——
        // 用户在首启卡学到的「键位长这样」，到下一扇窗照样得成立。
        g.Children.Add(Keycaps.Row(key, Core.Fluent.KeycapTheme(12.5)));
        Grid.SetColumn(g.Children[1], 1);
        return g;
    }

    // 圆角实心按钮模板（强调色底）；模板内无触发器，悬停底色由 Fluent.Springy 在代码里驱动
    static ControlTemplate RoundedBtnTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        t.VisualTree = border;
        return t;
    }

    void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _dismiss.Stop();
        Fluent.CloseWithFade(this);
    }
}
