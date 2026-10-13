// Fluent 交互细节：悬停淡入、按下回缩、滑杆/开关动画、窗口淡入、全局样式。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FrameCapture.Core;

public static partial class Fluent
{
    public const string IconFontChain = "Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol";

    static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    static readonly FontFamily IconFont = new(IconFontChain);

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
        public const int SpringMs = 260;     // 弹性回弹（松手/入场的「QQ 弹弹」settling，BackEase 过冲）
    }

    /// <summary>弹性缓动工厂：BackEase 过冲回弹，用于松手/入场的「弹一下」手感。
    /// amp 越大过冲越明显；受限滑轨指示器等不可越界处勿用（那里用单调 CubicEase）。</summary>
    public static EasingFunctionBase Spring(double amp = 0.6) => new BackEase { Amplitude = amp, EasingMode = EasingMode.EaseOut };

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

    /// <summary>退场：淡出 + 与入场对称的位移/缩放，再真正 Close（主题切换的交叉淡入不使用此路径）。
    /// R163：纯 150ms 透明度淡出在大窗（截图编辑器）上几乎看不出来，用户反馈「编辑器没有关闭动画」——
    /// 按入场留下的变换类型反向补一点动效：缩放窗（设置/帮助 PopupEnter）缩到 0.97，位移窗（编辑器 FadeIn rise）下沉 12。</summary>
    public static void CloseWithFade(Window w)
    {
        if (!_fadingOut.Add(w)) return; // 动画已在进行中（含按键重复触发的重入）
        w.Closing += (_, e) => { if (_fadingOut.Contains(w)) e.Cancel = true; }; // 动画期间任何 Close 一律拦下
        var cur = w.Opacity; // 先取当前有效值（PopupEnter 把基础值设成了 0，清动画后会闪没）
        var dur = new Duration(TimeSpan.FromMilliseconds(Motion.ExitMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var a = new DoubleAnimation(cur, 0.0, dur) { EasingFunction = ease };
        a.Completed += (_, _) => { _fadingOut.Remove(w); w.Close(); };
        w.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
        w.Opacity = cur;
        w.BeginAnimation(System.Windows.UIElement.OpacityProperty, a);
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

    public static void FadeIn(Window w, int ms = 160, double risePx = 0, bool offscreenFirstFrame = false)
    {
        w.Opacity = 0;
        // 分层窗（AllowsTransparency）自创建即 per-pixel alpha，首帧前没有黑/白闪；
        // 且 DWM 合成下 WPF 不再调 UpdateLayeredWindow——手工 SetLayeredWindowAttributes(alpha=0)
        // 永远不会被 WPF 清掉，用了就是整窗永久隐形（R11 教训：选区窗遮罩+提示全隐形 6 轮无人察觉）。
        if (w.AllowsTransparency)
        {
            void StartFade() => w.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(ms)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            // 修复（R157）：若内容已渲染（ContentRendered 已过——PinWindow 是在自己的 ContentRendered 回调里才调 FadeIn），
            // 再订阅 ContentRendered 永不触发（该事件仅首次渲染后触发一次），淡入不启动、Opacity 永久卡 0 → 贴图完全隐形，
            // 表现为「Ctrl+P 钉到屏幕完全没反应」。此时立即淡入。Show 前调用的窗口 IsLoaded=false，仍走订阅路径，行为不变。
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

    // ---------------- 层次与弹性（现代化：卡片浮起 + 交互回弹，治「太平/老工具」） ----------------

    /// <summary>给卡片/浮层加柔和投影，使其从背景「浮起」。主题感知：暗底用较浓黑投影、亮底较淡。
    /// 仅用于卡片/弹层等少量容器，勿逐行加（DropShadowEffect 有渲染成本）。</summary>
    public static void Elevate(Border b, double blur = 22, double depth = 5, double opacity = -1)
    {
        // R161：亮色下 0.26 太深（白底上像脏灰块），降到 0.13——亮底只需极淡投影提示层次即可
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
    /// StaysOpen=false 时点卡片外即正常关闭。让取色/粗细/强调色等浮层从画布「浮起」，不再贴一张平卡。</summary>
    public static Border PopupCard(FrameworkElement content, Thickness? innerPadding = null)
    {
        var inner = new Border
        {
            Background = new SolidColorBrush(Theme.PanelBg),
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
            Opacity = Theme.IsDark ? 0.45 : 0.12,   // R161：亮色下 0.22 太深，随 Elevate 一起调浅
        };
        sh.Freeze();
        inner.Effect = sh;
        return new Border { Padding = new Thickness(18), Child = inner };
    }

    /// <summary>R162：与 ChipHover 同款——小按钮悬停/按住只改底色带过渡，不再放大
    /// （用户：「小按钮的变大动画改成和普通按钮一致」）。hover/press 参数保留兼容，已忽略。</summary>
    public static void Springy(FrameworkElement el, double hover = 1.02, double press = 0.985) => BgHover(el);

    /// <summary>满宽卡片的安全「浮起」反馈：hover 轻微上移 + 松手回弹，离开复位——不缩放故绝不横向溢出容器。</summary>
    public static void Lift(Border b, double dy = 2.0)
    {
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        var tt = new System.Windows.Media.TranslateTransform(0, 0);
        b.RenderTransform = tt;
        void Move(double y, int ms)
        {
            var a = new DoubleAnimation(y, new Duration(TimeSpan.FromMilliseconds(ms))) { EasingFunction = Spring(0.35) };
            a.Freeze();
            tt.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, a);
        }
        b.MouseEnter += (_, _) => Move(-dy, Motion.HoverMs);
        b.MouseLeave += (_, _) => Move(0, 160);
    }

    /// <summary>入场「弹一下」：元素从 from 缩放弹性放大到 1（BackEase 过冲），用于浮层/toast 的 QQ 弹弹登场。
    /// 只缩放元素自身（在其窗口边界内），不位移故不会越出宿主窗口。</summary>
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
}
