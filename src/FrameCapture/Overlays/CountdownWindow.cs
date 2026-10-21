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
/// 延时截图倒计时浮窗：触发后倒数 N 秒再截「当前范围」，给用户时间去打开
/// 右键菜单 / 悬停提示 / 下拉框这类「鼠标一动就没了」的界面。
/// 关键手感：ShowActivated=false **不抢焦点**——否则目标窗失焦，正要截的菜单反而被关掉；
/// WDA_EXCLUDEFROMCAPTURE 排除，倒计时浮窗本身绝不入镜。点「取消」中止（点击不依赖焦点）。
/// </summary>
public sealed class CountdownWindow : Window
{
    /// <value>点「取消」的回调；未挂时点按钮无副作用，在 UI 线程执行。</value>
    public Action? OnCancel;

    readonly TextBlock _num;
    bool _closing;

    const double W = 300;      // DIP 宽（落点估算；实际 SizeToContent）
    const double EstH = 150;   // 估算高（落点用）

    /// <value>当前显示中的倒计时浮窗，无则 null；<c>Closed</c> 时自行清零。</value>
    public static CountdownWindow? Current;

    /// <summary>
    /// 倒计时卡落点：默认所在屏顶部居中；一旦压住要截的那块范围，就依次换到屏内其它锚位。
    /// 顺序是「顶部居中 → 底部居中 → 左上 → 右上 → 左下 → 右下」，全被盖住（范围就是整屏）时保持顶部居中。
    /// 之所以值得躲：倒计时这几秒用户正要在目标范围里点开菜单或悬停提示，卡压在上面就白等一轮。
    /// </summary>
    /// <param name="mon">倒计时所在显示器的物理矩形。</param>
    /// <param name="cardW">卡片宽（物理像素）。</param>
    /// <param name="cardH">卡片高（物理像素）。</param>
    /// <param name="avoid">要避开的捕获范围；null 表示无需避开。</param>
    /// <returns>卡片左上角的物理坐标，保证整张卡落在显示器内。</returns>
    public static (int X, int Y) PickPlace(RectI mon, int cardW, int cardH, RectI? avoid)
    {
        int ClampX(int cx) => Math.Max(mon.X + 6, Math.Min(cx, mon.Right - cardW - 6));
        int ClampY(int cy) => Math.Max(mon.Y + 6, Math.Min(cy, mon.Bottom - cardH - 6));
        var anchors = new (int, int)[]
        {
            (mon.X + mon.W / 2 - cardW / 2, mon.Y + 48),              // 顶部居中：标题栏一带
            (mon.X + mon.W / 2 - cardW / 2, mon.Bottom - cardH - 48), // 底部居中
            (mon.X + 48, mon.Y + 48), (mon.Right - cardW - 48, mon.Y + 48),
            (mon.X + 48, mon.Bottom - cardH - 48), (mon.Right - cardW - 48, mon.Bottom - cardH - 48),
        };
        foreach (var (ax, ay) in anchors)
        {
            var r = new RectI { X = ClampX(ax), Y = ClampY(ay), W = cardW, H = cardH };
            if (avoid == null || !Overlaps(r, avoid.Value)) return (r.X, r.Y);
        }
        return (ClampX(anchors[0].Item1), ClampY(anchors[0].Item2));
    }

    /// <summary>两个矩形是否有公共面积；贴边不算压住。</summary>
    static bool Overlaps(RectI a, RectI b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    /// <summary>
    /// 构造倒计时浮窗：只建界面、不起计时——调用方须自行 Show 并驱动 <see cref="SetNumber"/>。
    /// 构造期即定好落点（见 <see cref="PickPlace"/>，Show 前定位，无首帧闪烁），WDA 排除在 SourceInitialized 里设。
    /// 必须在 UI 线程构造。
    /// </summary>
    /// <param name="regionLabel">标题行右侧的范围描述文案（如「范围 800×600」「全屏」），原样上屏。</param>
    /// <param name="monitorDevice">倒计时所在显示器 device name；按设备取不到则退主屏，再退坐标就近屏。</param>
    /// <param name="avoidRegion">要避开的捕获范围（物理像素）：倒计时这几秒用户正要在里面点开菜单，卡压上去就白等一轮。</param>
    public CountdownWindow(string regionLabel, string monitorDevice, RectI? avoidRegion = null)
    {
        Title = "FrameCapture Countdown";
        Fluent.BorderlessOverlay(this);   // 不抢焦点：目标窗口保持焦点，用户才能打开要截的菜单

        // 落点：所在屏顶部居中的一组锚位里，第一个不压住目标范围的（WDA 排除，不入镜）
        var mon = Monitors.ByDevice(monitorDevice)
                  ?? Monitors.All().Find(m => m.IsPrimary)
                  ?? Monitors.Nearest(0, 0);
        var (sx, sy) = Monitors.DpiScaleOf(mon);
        int cardW = (int)(W * sx), cardH = (int)(EstH * sy);
        var (px, py) = PickPlace(mon.Bounds, cardW, cardH, avoidRegion);
        Left = px / sx; Top = py / sy;

        var card = new Border
        {
            Width = W,
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = Core.Fluent.CardEdge(),   // 上缘亮下缘暗：暗色里投影看不见，靠这条边说清卡片是浮着的
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black },
        };

        var root = new StackPanel();

        // 标题行：延时截图 + 将截取的范围（右对齐）
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Common.DelayedCapture),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Theme.Text),
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var scope = new TextBlock
        {
            Text = regionLabel,
            FontSize = 12,
            Foreground = new SolidColorBrush(Theme.TextDim),
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(scope, 1);
        head.Children.Add(scope);
        root.Children.Add(head);

        // 大号倒计时数字（居中，强调色）
        _num = new TextBlock
        {
            Text = "3",
            FontSize = 46,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Core.Fluent.AccentText()),
            FontFamily = new FontFamily("Segoe UI"),
            // 外壳是 SizeToContent 的：数字从三位掉到两位（100→99、10→9）那一下，
            // 整张卡片会跟着窄一截，倒计时看起来在抖。给数字留出三位的位置，
            // 卡片宽度由标题那行定，与秒数是几位数无关。
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 2),
        };
        root.Children.Add(_num);

        var hint = new TextBlock
        {
            Text = Loc.T(K.Capture.UseTheseSecondsToOpen),
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Theme.TextDim),
            FontFamily = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        root.Children.Add(hint);

        // 取消按钮（居中；点击不依赖焦点，Topmost 窗上照常命中）
        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = MakeBtn(Loc.T(K.Common.Cancel), Theme.Hover, Theme.Text);
        cancel.Click += (_, _) => OnCancel?.Invoke();
        btns.Children.Add(cancel);
        // 键位写出来才有人按：这扇窗不抢焦点，Esc 由全局钩子代收。
        // 用键帽而不是灰字——与首启卡、编辑器一副长相，键位在全应用只有一种写法。
        var esc = Keycaps.Row("Esc", Core.Fluent.KeycapTheme(11.5));
        esc.Margin = new Thickness(8, 0, 0, 0);
        btns.Children.Add(esc);
        root.Children.Add(btns);

        card.Child = root;
        Content = card;

        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(h);
            if (Environment.GetEnvironmentVariable("FC_TOAST_NOEXCLUDE") != "1")
                Win32.SetWindowDisplayAffinity(h, Win32.WDA_EXCLUDEFROMCAPTURE);
        };

        // 倒计时期间 Esc 全局取消：这扇窗 ShowActivated=false，键不会进它自己的 HWND，只能全局代收
        Loaded += (_, _) =>
        {
            Services.HotkeyManager.CountdownActive = true;
            // 回调在钩子回调线程里被同步调用，收窗要落回 UI 线程
            Services.HotkeyManager.CountdownEsc = () => Dispatcher.BeginInvoke(() => OnCancel?.Invoke());
        };
        Closed += (_, _) =>
        {
            Services.HotkeyManager.CountdownActive = false;
            Services.HotkeyManager.CountdownEsc = null;
            if (ReferenceEquals(Current, this)) Current = null;
        };
        Trace.Log("countdown shown");
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }

    /// <summary>刷新倒计时数字：每个数字弹入（0.6→1.0 BackEase 轻微过冲），与下拉/Toast 同款 PopIn 手感。</summary>
    /// <param name="n">剩余秒数，原样显示；本方法不做递减与「到 0 就截」判定——那是调用方的状态机。</param>
    public void SetNumber(int n)
    {
        _num.Text = n.ToString();
        var st = new ScaleTransform(0.6, 0.6);
        _num.RenderTransformOrigin = new Point(0.5, 0.5);
        _num.RenderTransform = st;
        var a = new DoubleAnimation(1.0, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.SpringMs)))
        { EasingFunction = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>带淡出收起本浮窗；重复调用无副作用。不停倒计时定时器、不触发 OnCancel——收尾由调用方负责。</summary>
    public void CloseSelf()
    {
        if (_closing) return;
        _closing = true;
        Fluent.CloseWithFade(this);
    }

    static Button MakeBtn(string text, Color bg, Color fg) => Fluent.RoundButton(text, bg, fg, 16, hoverFade: true);
}
