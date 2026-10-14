// Fluent 交互细节：悬停淡入、按下回缩、滑杆/开关动画、窗口淡入、全局样式。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FrameCapture.Core;

/// <summary>Fluent 视觉与交互工厂的窗口级 partial：入场/退场动效、无框窗口、投影层次与弹性反馈。
/// 全部成员必须在 UI 线程调用——它们订阅窗口事件并启动 Storyboard。</summary>
public static partial class Fluent
{
    /// <summary>图标字形回退链：Segoe Fluent Icons 优先，缺字形时逐级回退到 MDL2 / Symbol。</summary>
    public const string IconFontChain = "Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol";

    static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    static readonly FontFamily IconFont = new(IconFontChain);

    #region 窗口淡入

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
        public const int SpringMs = 260;     // 弹性回弹（松手/入场的 settling，BackEase 过冲）
    }

    /// <summary>弹性缓动工厂：BackEase 过冲回弹，用于松手/入场的「弹一下」手感。
    /// 受限滑轨指示器等不可越界处勿用（那里用单调 CubicEase）。</summary>
    /// <param name="amp">过冲幅度，越大弹得越明显；0 退化为无过冲。</param>
    /// <returns>新建的 <c>BackEase</c>（EaseOut）；每次调用返回新实例，可安全并发动画。</returns>
    public static EasingFunctionBase Spring(double amp = 0.6) => new BackEase { Amplitude = amp, EasingMode = EasingMode.EaseOut };

    /// <summary>真无框窗口：WindowStyle=None 的 WPF 窗口仍保留 DWM 非客户区（顶部白条）。
    /// WindowChrome（WPF 官方方案，内部处理 WM_NCCALCSIZE）让客户区覆盖整窗，白条不绘制，
    /// 且保留 WS_THICKFRAME → 原生阴影与 Win11 圆角、原生边缘调整大小，硬件加速不受影响
    /// （勿用 AllowsTransparency，软件渲染代价大）。CaptionHeight=0：拖拽由 DragMove 自理。</summary>
    /// <param name="w">目标窗口；各调用点一律在构造期（Show 前）设置 Chrome。</param>
    /// <param name="resizeBorder">边缘可拖拽调整大小的宽度（逻辑像素），过窄会难点中。</param>
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

    /// <summary>Win11 的 Mica 背板：桌面壁纸的模糊色调渗进窗口，替代平涂半透明底。
    /// 分层窗（<c>AllowsTransparency</c>）背后没有 DWM 背板，直接跳过；
    /// 系统不支持时属性返回非 0，窗口按自身底色显示，与不设背板一致。</summary>
    /// <param name="w">目标窗口；须先由 <see cref="BorderlessNative"/> 挂上 WindowChrome，
    /// 且窗口底色与内容根底色保持透明，否则背板被自身填充盖死。</param>
    public static void MicaBackdrop(Window w)
    {
        if (w.AllowsTransparency) return;
        // 玻璃框铺满客户区：WPF 只在玻璃框覆盖的区域内把像素让给 DWM 背板，Thickness(0) 等于整窗自绘不透明
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(w);
        if (chrome != null) chrome.GlassFrameThickness = new Thickness(-1);
        void ApplyTint()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            // 背板色调跟应用主题走：应用选浅色就出浅色 Mica，与系统主题设置无关
            int dark = Theme.IsDark ? 1 : 0;
            Native.Win32.DwmSetWindowAttribute(hwnd, Native.Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        }
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            int mica = Native.Win32.DWMSBT_MAINWINDOW;
            if (Native.Win32.DwmSetWindowAttribute(hwnd, Native.Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref mica, 4) != 0)
            {
                int on = 1;   // 22000 的等价开关，只有开与关
                Native.Win32.DwmSetWindowAttribute(hwnd, Native.Win32.DWMWA_MICA_EFFECT, ref on, 4);
            }
            ApplyTint();
        };
        Theme.Changed += ApplyTint;
        w.Closed += (_, _) => Theme.Changed -= ApplyTint;
    }

    static void SetAlphaZero(IntPtr hwnd)
    {
        Native.Win32.SetWindowLongW(hwnd, Native.Win32.GWL_EXSTYLE,
            Native.Win32.GetWindowLongW(hwnd, Native.Win32.GWL_EXSTYLE) | (uint)Native.Win32.WS_EX_LAYERED);
        Native.Win32.SetLayeredWindowAttributes(hwnd, IntPtr.Zero, 0, Native.Win32.LWA_ALPHA);
    }

    /// <summary>消除入场黑闪：WPF 的 Opacity=0 要等首帧渲染才应用到 HWND，未应用的那几帧 DWM 显示不透明黑面
    /// （或类背景白闪）。SourceInitialized（HWND 已建、未渲染）置 Win32 分层 alpha=0；WPF 首帧准备会冲掉手工样式，
    /// 故每次布局再补一次。默认 ContentRendered 放行；holdPastRender=true 时由返回的 release 决定放行时机
    /// （离屏首帧方案需要回屏后继续压住 alpha）。</summary>
    /// <param name="w">待压住首帧的窗口。</param>
    /// <param name="holdPastRender">true 表示渲染完成后仍压住 alpha，直到调用返回的 release。</param>
    /// <returns>放行函数：调用后停止补写 alpha=0，入场淡入才可生效。幂等，重复调用无副作用。</returns>
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
    /// <param name="w">待入场的窗口；内容须是 <see cref="FrameworkElement"/> 才有放大动画，否则只淡入。</param>
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
            w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            if (w.Content is FrameworkElement fe && HasNoTransform(fe))
            {
                fe.RenderTransformOrigin = new Point(0.5, 0.5);
                var st = new ScaleTransform(0.96, 0.96);
                fe.RenderTransform = st;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            }
        };
    }

    /// <summary>元素当前是否还没有自己的有效变换。
    /// <c>UIElement.RenderTransform</c> 的默认值是单位 <c>MatrixTransform</c> 而不是 null，
    /// 只判 null 会让「空闲才挂入场缩放」这类判断永远不成立。</summary>
    /// <param name="el">待判元素。</param>
    /// <returns>true = 尚无有效变换，可以直接挂新变换。</returns>
    public static bool HasNoTransform(FrameworkElement el)
        => el.RenderTransform is null or MatrixTransform { Value: { IsIdentity: true } };

    static readonly HashSet<Window> _fadingOut = new();

    /// <summary>退场：淡出 + 与入场对称的位移/缩放，再真正 Close（主题切换的交叉淡入不走此路径）。
    /// 大窗（截图编辑器）上纯 150ms 透明度淡出几乎看不出来，故按入场留下的变换类型反向补一点动效：
    /// 缩放窗（设置/帮助 PopupEnter）缩到 0.97，位移窗（编辑器 FadeIn rise）下沉 12。
    /// 只改 Opacity 与 RenderTransform，不碰 Left/Top——避免多窗关闭时闪出桌面。</summary>
    /// <param name="w">待关闭的窗口。淡出期间任何 Close（含按键重复）都被取消，重复调用只生效一次。</param>
    public static void CloseWithFade(Window w)
    {
        if (!_fadingOut.Add(w)) return; // 动画已在进行中（含按键重复触发的重入）
        w.Closing += (_, e) => { if (_fadingOut.Contains(w)) e.Cancel = true; }; // 动画期间任何 Close 一律拦下
        var cur = w.Opacity; // 先取当前有效值（PopupEnter 把基础值设成了 0，清动画后会闪没）
        var dur = new Duration(TimeSpan.FromMilliseconds(Motion.ExitMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var a = new DoubleAnimation(cur, 0.0, dur) { EasingFunction = ease };
        a.Completed += (_, _) => { _fadingOut.Remove(w); w.Close(); };
        w.BeginAnimation(UIElement.OpacityProperty, null);
        w.Opacity = cur;
        w.BeginAnimation(UIElement.OpacityProperty, a);
        if (w.Content is FrameworkElement fe)
        {
            if (fe.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, dur) { EasingFunction = ease });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, dur) { EasingFunction = ease });
            }
            else if (fe.RenderTransform is TranslateTransform tt)
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, dur) { EasingFunction = ease });
        }
    }

    /// <summary>窗口淡入：Opacity 0→1（CubicEase Out），可选自下方 risePx 抬起。
    /// 分层窗（AllowsTransparency）走独立分支：它自创建即 per-pixel alpha，首帧没有黑/白闪，
    /// 且 DWM 合成下 WPF 不调 UpdateLayeredWindow——手工 SetLayeredWindowAttributes(alpha=0)
    /// 永远不会被 WPF 清掉，用了就是整窗永久隐形。该分支只淡入，risePx 与 offscreenFirstFrame 不生效。
    /// 非分层窗由 <see cref="PreTransparent"/> 压住首帧，黑面/白闪不会先于淡入露出。</summary>
    /// <param name="w">待入场的窗口。</param>
    /// <param name="ms">淡入时长（ms）。</param>
    /// <param name="risePx">入场位移（逻辑像素），内容从下方 risePx 抬回 0；0 = 只淡入。
    /// 内容已挂 TransformGroup 时跳过，不覆盖调用方自己的变换。</param>
    /// <param name="offscreenFirstFrame">true = 先把窗口挪到 -32000 渲染首帧再回位，黑相永远发生在屏幕外；
    /// 代价是回位瞬间要再压一次 alpha 并等下一渲染帧放行。</param>
    public static void FadeIn(Window w, int ms = 160, double risePx = 0, bool offscreenFirstFrame = false)
    {
        w.Opacity = 0;
        // 分层窗（AllowsTransparency）自创建即 per-pixel alpha，首帧前没有黑/白闪；
        // 且 DWM 合成下 WPF 不调 UpdateLayeredWindow——手工 SetLayeredWindowAttributes(alpha=0)
        // 永远不会被 WPF 清掉，用了就是整窗永久隐形。本分支绝不碰 Win32 分层属性。
        if (w.AllowsTransparency)
        {
            void StartFade() => w.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(ms)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            // ContentRendered 只在首次渲染后触发一次：此刻内容已渲染（IsLoaded，例如 PinWindow 在自己的
            // ContentRendered 回调里才调 FadeIn）时再订阅永不触发，淡入不启动、Opacity 永久卡 0 → 整窗隐形。
            // 故此刻直接起淡入；Show 前调用的窗口 IsLoaded=false，仍走订阅路径。
            if (w.IsLoaded) { StartFade(); return; }
            w.ContentRendered += (_, _) => StartFade();
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
                        CompositionTarget.Rendering -= step;
                        Start();
                    };
                    CompositionTarget.Rendering += step;
                };
            }
            if (w.IsInitialized) Park(); else w.SourceInitialized += (_, _) => Park();
        }
        else w.ContentRendered += (_, _) => Start();
    }
    #endregion

    #region 层次与弹性：卡片浮起 + 交互回弹

    /// <summary>给卡片/浮层加柔和投影，使其从背景「浮起」。主题感知：暗底用较浓黑投影、亮底较淡。
    /// 仅用于卡片/弹层等少量容器，勿逐行加（DropShadowEffect 有渲染成本）。</summary>
    /// <param name="b">承载投影的卡片；Effect 会被整体替换。</param>
    /// <param name="blur">模糊半径（逻辑像素），越大越柔。</param>
    /// <param name="depth">投影偏移（逻辑像素），0 = 正下方贴边。</param>
    /// <param name="opacity">投影不透明度 0–1；负数（默认）表示按主题取档：暗底 0.5、亮底 0.13。</param>
    public static void Elevate(Border b, double blur = 22, double depth = 5, double opacity = -1)
    {
        // 亮底只需极淡投影提示层次，再深一点在白底上就成了脏灰块
        double op = opacity >= 0 ? opacity : (Theme.IsDark ? 0.5 : 0.13);
        var sh = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = blur,
            ShadowDepth = depth,
            Direction = 270,          // 向下投影
            Opacity = op,
        };
        sh.Freeze();
        b.Effect = sh;
    }

    /// <summary>弹层（Popup.Child）卡片：内层 PanelBg 圆角卡 + 柔和投影，外层透明留白给投影腾出渲染空间。
    /// Popup 按子元素 DesiredSize 开分层窗、而 Effect 不参与布局，故投影若直接挂内层会被窗缘裁掉——
    /// 外层 Padding 把 DesiredSize 撑大，投影才有地方画。外层不设 Background（null）→ 留白处点击穿透，
    /// StaysOpen=false 时点卡片外即正常关闭。取色/粗细/强调色等浮层由此从画布「浮起」。</summary>
    /// <param name="content">卡片内容。</param>
    /// <param name="innerPadding">内层留白；null = 12,10,12,10。</param>
    /// <returns>可直接赋给 <c>Popup.Child</c> 的外层 Border（四周留 18 给投影）。</returns>
    public static Border PopupCard(FrameworkElement content, Thickness? innerPadding = null)
    {
        var inner = new Border
        {
            Background = new SolidColorBrush(Theme.PanelBgSolid),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = innerPadding ?? new Thickness(12, 10, 12, 10),
            Child = content,
        };
        var sh = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 16,
            ShadowDepth = 4,
            Direction = 270,
            Opacity = Theme.IsDark ? 0.45 : 0.12,   // 亮底投影与 Elevate 同档，极淡
        };
        sh.Freeze();
        inner.Effect = sh;
        return new Border { Padding = new Thickness(18), Child = inner };
    }

    /// <summary>小按钮的悬停反馈：与普通按钮一致，只改底色带过渡，不缩放——缩放会让一排小按钮互相挤动。</summary>
    /// <param name="el">目标元素。</param>
    public static void Springy(FrameworkElement el) => BgHover(el);

    /// <summary>入场「弹一下」：元素从 from 缩放弹性放大到 1（BackEase 过冲），用于浮层/toast 的弹性登场。
    /// 只缩放元素自身（在其窗口边界内），不位移故不会越出宿主窗口。</summary>
    /// <param name="el">登场元素；RenderTransform 被替换为 ScaleTransform。</param>
    /// <param name="from">起始缩放倍率，小于 1 才有「放大进场」感。</param>
    /// <param name="ms">时长（ms）；≤0 表示用 <c>Motion.EnterMs + 70</c>。</param>
    public static void PopIn(FrameworkElement el, double from = 0.85, int ms = -1)
    {
        ms = ms > 0 ? ms : Motion.EnterMs + 70;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(from, from);
        el.RenderTransform = st;
        var a = new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(ms))) { EasingFunction = Spring(0.55) };
        a.Freeze();
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>逐项错位入场：每项淡入 + 自下方 risePx 抬起，第 i 项延后 i*step 起，用于列表/设置项的载入。
    /// step 取 <c>Motion.StaggerMs</c> 与整段预算算出的间隔中较小者：项数少时错位明显，
    /// 项数多时自动收窄，整段压在 budgetMs 内——几十行的列表不会拖成秒级。</summary>
    /// <param name="items">按期望出现顺序排列的元素，null 项跳过且不占时序；
    /// 每项的 Opacity 与 RenderTransform 被接管，已挂 TransformGroup 的项只淡入不位移。</param>
    /// <param name="risePx">单项入场的纵向位移（逻辑像素），≤0 表示不纵向抬升。</param>
    /// <param name="riseX">单项入场的横向位移（逻辑像素），给翻页这类有方向的切换留方向感；
    /// 与 <paramref name="risePx"/> 同时为 0 表示只淡入。</param>
    /// <param name="itemMs">单项时长（ms）；≤0 表示用 <c>Motion.EnterMs</c>。</param>
    /// <param name="budgetMs">整段入场预算（ms）；≤0 表示用 <c>Motion.StaggerMs × 3</c>。</param>
    public static void CascadeIn(IReadOnlyList<FrameworkElement?> items, double risePx = 8, double riseX = 0,
        int itemMs = -1, int budgetMs = -1)
    {
        int n = 0;
        for (int i = 0; i < items.Count; i++) if (items[i] is not null) n++;
        if (n == 0) return;
        itemMs = itemMs > 0 ? itemMs : Motion.EnterMs;
        budgetMs = budgetMs > 0 ? budgetMs : Motion.StaggerMs * 3;
        // 预算比单项还短时无空间错位：退化成整段同时淡入，不产生负延迟
        double step = n > 1 ? Math.Max(0, Math.Min(Motion.StaggerMs, (double)(budgetMs - itemMs) / (n - 1))) : 0;
        var dur = new Duration(TimeSpan.FromMilliseconds(itemMs));
        int k = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var el = items[i];
            if (el is null) continue;
            var begin = TimeSpan.FromMilliseconds(step * k);
            k++;
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 0;
            var fade = new DoubleAnimation(1, dur)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = begin };
            fade.Freeze();
            el.BeginAnimation(UIElement.OpacityProperty, fade);
            if (risePx <= 0 && riseX == 0) continue;
            // 已有 RenderTransform 的元素不能覆盖：图标按钮自带 ScaleTransform 供 hover 缩放，
            // 缩放动画遇到非 ScaleTransform 会静默 return，覆盖期间悬停没反应，收束置 null 则永久没反应。
            // 套一层 TransformGroup 只驱动位移子变换，收束时摘掉自己、把原对象放回原位。
            var prior = el.RenderTransform;
            var tt = new TranslateTransform(riseX, risePx);
            CascadeShifts.Add(tt, new object());   // 登记为本类所造，清零时只摘它
            TransformGroup? group = null;
            if (prior is null) el.RenderTransform = tt;
            else
            {
                group = new TransformGroup();
                group.Children.Add(prior);
                group.Children.Add(tt);
                el.RenderTransform = group;
            }
            // 位移归零后撤掉变换：留着 TranslateTransform 会让后续挂缩放的代码以为已有变换而跳过。
            // 清理挂在真正启动的那条动画上——只挂纵向会让纯横向位移的调用永远撤不掉变换。
            void ClearTransform()
            {
                if (ReferenceEquals(el.RenderTransform, tt)) { el.RenderTransform = null; return; }
                if (group is not null && ReferenceEquals(el.RenderTransform, group))
                {
                    group.Children.Remove(tt);
                    el.RenderTransform = group.Children.Count == 1 ? group.Children[0] : group;
                }
            }
            if (risePx > 0)
            {
                var rise = new DoubleAnimation(0, dur)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = begin };
                rise.Completed += (_, __) => ClearTransform();
                rise.Freeze();   // 回调必须赶在冻结前挂上：冻结后挂事件按「修改」处理
                tt.BeginAnimation(TranslateTransform.YProperty, rise);
            }
            if (riseX != 0)
            {
                var side = new DoubleAnimation(0, dur)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = begin };
                side.Completed += (_, __) => ClearTransform();
                side.Freeze();
                tt.BeginAnimation(TranslateTransform.XProperty, side);
            }
        }
    }

    /// <summary>面板直接子元素的逐项错位入场：列表、设置项、教程步骤的载入。</summary>
    /// <param name="body">承载行的面板，只取其直接子元素。</param>
    /// <param name="risePx">每项纵向位移（逻辑像素）。</param>
    /// <param name="riseX">每项横向位移（逻辑像素）。</param>
    public static void CascadeRows(Panel body, double risePx = 8, double riseX = 0)
    {
        var rows = new List<FrameworkElement?>(body.Children.Count);
        foreach (var c in body.Children) rows.Add(c as FrameworkElement);
        CascadeIn(rows, risePx, riseX);
    }

    /// <summary>本类创建的位移动画对象登记表。清零时只摘自己造的那个，
    /// 不动调用方挂在行上的合法位移；随对象回收自动失效，不构成引用泄漏。</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TranslateTransform, object> CascadeShifts = new();

    /// <summary>逐项入场的清零出口：撤掉 <see cref="CascadeIn"/> 挂上的动画与位移，恢复完全可见。
    /// 不走动画的重建路径（主题重建、翻页不带动画）必须调它，否则可能在飞的行会停在半透明。
    /// 包在 <see cref="TransformGroup"/> 里的位移也会被摘掉，原变换放回原位——
    /// 残留的 TransformGroup 会让只认 ScaleTransform 的缩放动画永久失效。</summary>
    /// <param name="body">承载行的面板。</param>
    public static void ResetCascade(Panel body)
    {
        foreach (var c in body.Children)
        {
            if (c is not FrameworkElement fe) continue;
            fe.BeginAnimation(UIElement.OpacityProperty, null);
            fe.Opacity = 1;
            if (fe.RenderTransform is TranslateTransform bare)
            {
                if (CascadeShifts.TryGetValue(bare, out _)) fe.RenderTransform = null;
            }
            else if (fe.RenderTransform is TransformGroup g)
            {
                for (int i = g.Children.Count - 1; i >= 0; i--)
                    if (g.Children[i] is TranslateTransform ct && CascadeShifts.TryGetValue(ct, out _))
                        g.Children.RemoveAt(i);
                if (g.Children.Count == 1) fe.RenderTransform = g.Children[0];
            }
        }
    }
    #endregion

}
