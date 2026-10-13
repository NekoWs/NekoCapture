using System;
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
    /// 构造倒计时浮窗：只建界面、不起计时——调用方须自行 Show 并驱动 <see cref="SetNumber"/>。
    /// 构造期即定好落点（所在屏顶部居中，Show 前定位，无首帧闪烁），WDA 排除在 SourceInitialized 里设。
    /// 必须在 UI 线程构造。
    /// </summary>
    /// <param name="regionLabel">标题行右侧的范围描述文案（如「范围 800×600」「全屏」），原样上屏。</param>
    /// <param name="monitorDevice">倒计时所在显示器 device name；按设备取不到则退主屏，再退坐标就近屏。</param>
    public CountdownWindow(string regionLabel, string monitorDevice)
    {
        Title = "FrameCapture Countdown";
        Fluent.BorderlessNative(this, resizeBorder: 0);
        ShowActivated = false;   // 不抢焦点：目标窗口保持焦点，用户才能打开要截的菜单
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;

        // 落点：所在屏顶部居中（标题栏一带，尽量不挡目标内容；WDA 排除，不入镜）
        var mon = Monitors.ByDevice(monitorDevice)
                  ?? Monitors.All().Find(m => m.IsPrimary)
                  ?? Monitors.Nearest(0, 0);
        double sx = 1.0, sy = 1.0;
        try
        {
            Win32.GetDpiForMonitor(mon.ResolveHMonitor(), Win32.DPI_EFFECTIVE, out uint dx, out uint dy);
            if (dx > 0 && dy > 0) { sx = dx / 96.0; sy = dy / 96.0; }
        }
        catch { }
        int cardW = (int)(W * sx), cardH = (int)(EstH * sy);
        int left = mon.Bounds.X + mon.Bounds.W / 2 - cardW / 2;
        int top = mon.Bounds.Y + 48;
        left = Math.Max(mon.Bounds.X + 6, Math.Min(left, mon.Bounds.X + mon.Bounds.W - cardW - 6));
        top = Math.Max(mon.Bounds.Y + 6, Math.Min(top, mon.Bounds.Y + mon.Bounds.H - cardH - 6));
        Left = left / sx; Top = top / sy;

        var card = new Border
        {
            Width = W,
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
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
            Text = "延时截图",
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
            Foreground = new SolidColorBrush(Theme.Accent),
            FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 2),
        };
        root.Children.Add(_num);

        var hint = new TextBlock
        {
            Text = "趁这几秒打开要截的菜单 / 悬停提示…",
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
        var cancel = MakeBtn("取消", Theme.Hover, Theme.Text);
        cancel.Click += (_, _) => OnCancel?.Invoke();
        btns.Children.Add(cancel);
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

        Closed += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
        FrameCapture.Core.Trace.Log("countdown shown");
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

    static Button MakeBtn(string text, Color bg, Color fg)
    {
        return new Button
        {
            Content = text,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(fg),
            Background = new SolidColorBrush(bg),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 6, 16, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = RoundedBtnTemplate(),
        };
    }

    static ControlTemplate RoundedBtnTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        t.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88));
        t.Triggers.Add(hover);
        return t;
    }
}
