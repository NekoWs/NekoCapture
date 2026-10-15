using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>
/// 截屏成功浮窗（拍完即见）：缩略图 + 「已复制」+ 点击继续标注。
/// 不抢焦点、WDA 排除不入镜、悬停暂停自动消失；落点由 Core.ToastPlace 决策（贴刚截完的区域）。
/// </summary>
public sealed class CaptureToast : Window
{
    static CaptureToast? _current;

    readonly DispatcherTimer _dismiss;
    bool _closing;
    bool _persistent;   // ToastSeconds=0 → 常驻，不自动消失

    const double W = 300;      // DIP 宽
    const double EstH = 84;    // 估算高（落点用；实际 SizeToContent）

    /// <summary>
    /// 弹出截屏成功浮窗：同一时刻只保留一张，再次调用先收起上一张。
    /// 非激活浮窗（ShowActivated=false），不打断用户当前输入；必须在 UI 线程调用。
    /// 剪贴板内容已由调用方按本条手势的开关写好——本方法只负责告知与「继续标注」入口。
    /// </summary>
    /// <param name="frame">刚截下的帧（BGRA），只用于生成 56×44 缩略图；建窗时即拷成冻结位图，之后改帧字节不影响浮窗。</param>
    /// <param name="region">截图区域，物理像素、虚拟桌面绝对坐标，决定落点。</param>
    /// <param name="monitorDevice">区域所在显示器 device name；按设备取不到时按区域坐标就近回退。</param>
    /// <param name="onEdit">点卡片或按 Enter 时执行的回调（进编辑器），在 UI 线程调用，执行后收起浮窗。</param>
    /// <param name="copied">这张图是否写进了剪贴板——决定标题说「已复制」还是「已截图」，两者不能混说。</param>
    public static void Show(CapturedFrame frame, RectI region, string monitorDevice, Action onEdit, bool copied = true)
    {
        _current?.Close();
        _current = new CaptureToast(frame, region, monitorDevice, onEdit, copied);
        _current.Show(); // ShowActivated=false：不打断用户当前输入
    }

    /// <summary>立即收起当前浮窗（进入选区等全屏交互前调用，避免残留半透明浮窗与选区窗叠在一起闪）。</summary>
    public static void DismissCurrent() => _current?.Dismiss();

    CaptureToast(CapturedFrame frame, RectI region, string monitorDevice, Action onEdit, bool copied)
    {
        Title = "FrameCapture Toast";
        Fluent.BorderlessOverlay(this);

        // 落点：物理像素决策 → 该显示器 DPI 换算 DIP（Show 前定好，无首帧闪烁）
        var mon = Monitors.ByDevice(monitorDevice) ?? Monitors.Nearest(region.X, region.Y);
        var (sx, sy) = Monitors.DpiScaleOf(mon);
        var work = new RectI(mon.Bounds.X, mon.Bounds.Y, mon.Bounds.W, mon.Bounds.H);
        var pos = ToastPlace.Place(region, work, (int)(W * sx), (int)(EstH * sy));
        Left = pos.X / sx; Top = pos.Y / sy;

        var strokeCol = Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B);
        var borderBrush = new SolidColorBrush(strokeCol);
        var card = new Border
        {
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 9, 10, 9),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black },
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        // 整张卡片可点→进编辑器；hover 描边亮起强调色，把「点我标注」的可点性显出来
        card.MouseEnter += (_, _) => borderBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Theme.Accent, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.HoverMs))));
        card.MouseLeave += (_, _) => borderBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(strokeCol, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs))));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Image
        {
            Source = Thumbnail(frame, 56, 44),
            Width = 56, Height = 44,
            Stretch = Stretch.Uniform,
        };
        var thumbBox = new Border
        {
            Child = thumb,
            CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(thumbBox, 0);
        grid.Children.Add(thumbBox);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = copied ? Loc.T(K.Capture.CopiedToClipboard) : Loc.T(K.Capture.ScreenshotTaken),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            FontFamily = new FontFamily("Segoe UI"),
        });
        texts.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Capture.ClickOrPressEnter),
            FontSize = 11,
            Foreground = new SolidColorBrush(Theme.TextDim),
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var close = Fluent.IconButton("\uE711", Loc.T(K.Common.Close), 26, 12,
            new SolidColorBrush(Theme.TextDim));
        close.Cursor = System.Windows.Input.Cursors.Arrow;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => Dismiss();
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        card.Child = grid;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Source is DependencyObject d && ReferenceEquals(d, close)) return;
            Trace.Log("toast→edit");
            onEdit();
            Dismiss();
        };
        Content = card;
        // 弹性入场：卡片从 0.85 放大到 1（BackEase 过冲），叠加 FadeIn 的淡入+上浮
        ContentRendered += (_, _) => Fluent.PopIn(card);

        // 键盘等价（Enter/Esc）：浮窗不抢焦点，WPF 焦点路由收不到键——直接在 HWND 层处理消息
        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(h);
            // 后续截屏拍不到浮窗；FC_TOAST_NOEXCLUDE=1 仅供视觉审查截图（生产默认排除）
            if (Environment.GetEnvironmentVariable("FC_TOAST_NOEXCLUDE") != "1")
                Win32.SetWindowDisplayAffinity(h, Win32.WDA_EXCLUDEFROMCAPTURE);
            // Enter/Esc 不在本 HWND 钩子里处理：ShowActivated=false 的窗口不是键盘焦点目标，
            // WM_KEYDOWN 不会路由到这里，挂在这里等于没挂。唯一入口见下方 HotkeyManager。
        };

        // 全局钩子代收 Enter/Esc（浮窗不抢焦点，只能这样）；生命周期绑定本浮窗，Closed 清零，绝不泄漏成「全局 Enter 失灵」。
        Services.HotkeyManager.ToastActive = true;
        Services.HotkeyManager.ToastEnter = () => Dispatcher.BeginInvoke(() => { Trace.Log("toast→edit(key)"); onEdit(); Dismiss(); });
        Services.HotkeyManager.ToastEsc = () => Dispatcher.BeginInvoke(Dismiss);

        // 存在时间可自定义（ToastSeconds，0=常驻不自动消失）。悬停暂停计时、移开重数满时长。
        double secs = SettingsService.Shared?.Current.ToastSeconds ?? 3.2;
        _persistent = secs <= 0;
        _dismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_persistent ? 3.2 : secs) };
        _dismiss.Tick += (_, _) => Dismiss();
        if (!_persistent) _dismiss.Start();
        MouseEnter += (_, _) => _dismiss.Stop();                              // 阅读/移动鼠标时不消失
        MouseLeave += (_, _) => { if (!_persistent) _dismiss.Start(); };      // 移开再给满时长

        Closed += (_, _) =>
        {
            _dismiss.Stop();
            if (ReferenceEquals(_current, this))
            {
                _current = null;
                Services.HotkeyManager.ToastActive = false;
                Services.HotkeyManager.ToastEnter = null;
                Services.HotkeyManager.ToastEsc = null;
            }
        };

        Trace.Log("toast shown");
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }

    void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _dismiss.Stop();
        Fluent.CloseWithFade(this);
    }

    static BitmapSource Thumbnail(CapturedFrame f, double maxW, double maxH)
    {
        var src = BgraImage.ToSource(f);
        double k = Math.Min(maxW / f.W, maxH / f.H);
        if (k >= 1) return src;
        return new TransformedBitmap(src, new ScaleTransform(k, k));
    }
}
