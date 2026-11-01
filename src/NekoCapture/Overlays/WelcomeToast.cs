using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using NekoCapture.Core;
using NekoCapture.Native;

namespace NekoCapture.Overlays;

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

    const double W = 372;      // 卡片最宽（DIP）：再宽就成横幅了
    const double MinW = 244;   // 最窄：键位行的标签加一长串键帽要排得下
    const double EstH = 168;   // 落点用的估算高，ContentRendered 后以实测宽高贴准

    /// <summary>
    /// 弹出欢迎卡：同一时刻只保留一张，再次调用先收起上一张。
    /// 非激活浮窗（ShowActivated=false），不打断用户当前输入；必须在 UI 线程调用。
    /// 本方法不写设置——是否记为「已看过」（FirstRunHintShown）由调用方决定。
    /// </summary>
    /// <param name="captureHotkey">「截图」键位的展示文案，原样上屏，不做二次解析。</param>
    /// <param name="recordHotkey">「录制」键位的展示文案，原样上屏。</param>
    /// <param name="sticky">首启那张不自己走：装完软件的人若没看见右下角这一下，12 秒后卡片消失，
    /// 就等于从没告诉过他键位在哪。脚本叫出来审查的（<c>--welcome</c>）保持自动走，免得卡在屏幕上。</param>
    public static void Show(string captureHotkey, string recordHotkey, bool sticky = false)
    {
        _current?.Close();
        _current = new WelcomeToast(captureHotkey, recordHotkey, sticky);
        _current.Show(); // ShowActivated=false：不打断用户当前输入
    }

    WelcomeToast(string captureHotkey, string recordHotkey, bool sticky)
    {
        Title = "NekoCapture Welcome";
        Fluent.BorderlessOverlay(this);

        // 落点：主屏工作区右下角（靠托盘图标，让用户把「图标在这」和「怎么开始」联系起来）。
        // SystemParameters.WorkArea 已是 DIP 且排除任务栏，直接用作 Left/Top，无需 DPI 换算。
        var wa = SystemParameters.WorkArea;
        Left = Math.Max(wa.Left, wa.Right - W - 16);
        Top = Math.Max(wa.Top, wa.Bottom - EstH - 16);

        var card = new Border
        {
            MinWidth = MinW,
            MaxWidth = W,   // 宽度跟着内容走：列键位时宽一点，关掉键位时收回来，右边不空出一片
            Background = Theme.CardBgBrush,
            BorderBrush = Core.Fluent.CardEdge(),   // 上缘亮下缘暗：暗色里投影看不见，靠这条边说清卡片是浮着的
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
            Text = Loc.T(K.Capture.NekoCaptureIsReady),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Theme.TextBrush,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var close = Fluent.IconButton("\uE711", Loc.T(K.Common.Close), 26, 12,
            Theme.TextDimBrush);
        close.Click += (_, _) => Dismiss();
        Grid.SetColumn(close, 1);
        head.Children.Add(close);
        root.Children.Add(head);

        // 两个热键：标签（暗）+ 键位（键帽），一眼看清「按什么」。
        // 键位列可关：老手不需要每次开机都被提醒一遍键在哪，关掉后卡片只剩标题。
        if (SettingsService.Shared?.Current.WelcomeShowsHotkeys ?? true)
        {
            root.Children.Add(HotkeyLine(Loc.T(K.Common.Capture), captureHotkey));
            root.Children.Add(HotkeyLine(Loc.T(K.Common.Recording), recordHotkey));
        }

        card.Child = root;
        Content = card;
        // 落点必须在入场动画开始前定对：FadeIn 的「先移出屏外再归位」会在收尾时把
        // Left/Top 写回它开始时的值——那时若还是构造期的估算宽，卡片右侧就会空出一条。
        // 所以先把卡片量一遍，按它自己的宽高把右下角贴准，再交给 SizeToContent。
        card.Measure(new Size(W, double.PositiveInfinity));   // 量卡片本身（含内边距），量内容会少算一圈边
        double cw = Math.Min(W, Math.Ceiling(card.DesiredSize.Width));
        double ch = Math.Ceiling(card.DesiredSize.Height);
        Left = Math.Max(wa.Left, wa.Right - cw - 16);
        Top = Math.Max(wa.Top, wa.Bottom - ch - 16);
        // 实测尺寸若与量出来的不同（换了字体/语言），落位时再校一次
        SizeChanged += (_, _) =>
        {
            Left = Math.Max(wa.Left, wa.Right - ActualWidth - 16);
            Top = Math.Max(wa.Top, wa.Bottom - ActualHeight - 16);
        };
        ContentRendered += (_, _) =>
        {
            // 入场动画收尾会把 Left/Top 写回它开始时的值：等这一帧的活干完再落一次位
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Left = Math.Max(wa.Left, wa.Right - ActualWidth - 16);
                Top = Math.Max(wa.Top, wa.Bottom - ActualHeight - 16);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Fluent.PopIn(card);   // 弹性入场：与 CaptureToast 同款放大登场
        };

        // 键盘等价：浮窗不抢焦点，WPF 焦点路由收不到键——直接在 HWND 层处理 Esc
        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(h);
            // 欢迎卡默认不出现在任何截图里；NC_TOAST_NOEXCLUDE=1 仅供视觉审查截图（与 CaptureToast 同一开关）
            Core.Fluent.ExcludeFromCapture(h);
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

        // 上手信息给足阅读时间：12s，悬停暂停，移开再给 12s。
        // 首启那张不计时（sticky）：没看见的人不该被倒计时没收唯一一次说明。
        // 关法是点右上角 ✕（唯一出口，护栏盯着它在不在）：卡片不抢焦点，Esc 归用户当下正在用的那个窗口，
        // 要让它认 Esc 就得在卡片存在期间全局吞键，而它没有到期时间——那种拦截不能接受。
        _dismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _dismiss.Tick += (_, _) => Dismiss();
        if (!sticky) _dismiss.Start();
        MouseEnter += (_, _) => _dismiss.Stop();
        MouseLeave += (_, _) => { if (!sticky) _dismiss.Start(); };

        Closed += (_, _) => { _dismiss.Stop(); if (ReferenceEquals(_current, this)) _current = null; };

        Trace.Log("welcome shown");
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }

    /// <summary>一行「标签 + 键位」。标签列走 SharedSizeGroup：两行的键位对齐，列宽由两种语言里较宽的那条决定。
    /// 写死宽度只量得出中文那两个汉字，英文的 Capture / Recording 会被切掉半截，还和键位挤在一起。</summary>
    /// <param name="label">这条热键是干什么的，暗色小字。</param>
    /// <param name="key">当前生效的键位串，强调色加粗。</param>
    /// <returns>一行两列的版面，交给欢迎卡纵向堆叠。</returns>
    /// <summary>分隔多条按法：本地化逗号（中文是全角「，」）与带空格的斜杠，与帮助页同一份判法。</summary>
    static string[] KeySeparators { get; } = { Loc.T(K.Hotkeys.Comma), ", ", " / " };

    static FrameworkElement HotkeyLine(string label, string key)
    {
        var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "HotkeyLabel" });
        // 星型列吃掉「卡片比这一行宽」的那截：键帽因此贴住右沿，与标题行的 ✕ 落在同一条线上。
        // 都用 Auto 时整行只占自己的宽度，右边空出的一条谁也没用。
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lb = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = Theme.TextDimBrush,
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(lb, 0);
        g.Children.Add(lb);
        // 键位用键帽画，不写成彩色文字：与编辑器、设置页同一副长相——
        // 用户在首启卡学到的「键位长这样」，到下一扇窗照样得成立。
        // 走 ChordRow 而不是 Row：一个动作可以绑多条按法（DescribeKeysFor 用本地化逗号拼），
        // 也可以带触发后缀（「F2 按下」）；整串当一个组合键切，键帽会裂成「F6，Ctrl」。
        var keys = Keycaps.ChordRow(key, Core.Fluent.KeycapTheme(12.5), KeySeparators,
            Loc.T(K.Common.NoKey));
        keys.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(keys);
        Grid.SetColumn(keys, 1);
        return g;
    }


    void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _dismiss.Stop();
        Fluent.CloseWithFade(this);
    }
}
