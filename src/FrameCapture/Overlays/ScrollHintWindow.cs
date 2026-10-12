using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 滚动截图控制条（R88）：选区后常驻，用户只管滚动目标窗口，后台定时器自动找重叠拼接；
/// 本条只做进度显示 + 完成/取消。WDA_EXCLUDEFROMCAPTURE 排除，绝不入镜。
/// 关键手感：ShowActivated=false **不抢焦点**——目标窗口保持焦点，滚轮/键盘滚动照常生效
/// （若本条抢了焦点，关闭「悬停滚动」的系统里滚轮会落到本条、目标窗纹丝不动）。
/// </summary>
public sealed class ScrollHintWindow : Window
{
    public Action? OnDone, OnCancel;

    readonly TextBlock _progress;
    readonly TextBlock _hint;
    bool _closing;

    const string DefaultHint = "滚动目标窗口，自动拼接 · 点「完成」结束";
    const double W = 372;      // DIP 宽（估算落点用；实际 SizeToContent）
    const double EstH = 96;    // 估算高（落点用）

    public static ScrollHintWindow? Current;

    public ScrollHintWindow(RectI region, string monitorDevice)
    {
        Title = "FrameCapture Scroll";
        Fluent.BorderlessNative(this, resizeBorder: 0);
        ShowActivated = false;   // 不抢焦点：目标窗口继续接收滚轮/键盘滚动
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;

        // 落点：区域顶部居中，优先置于区域**上方**（不遮挡要滚动的内容）；上方放不下则贴区域上沿（WDA 排除，不入镜）
        var mon = Monitors.ByDevice(monitorDevice) ?? Monitors.Nearest(region.X, region.Y);
        double sx = 1.0, sy = 1.0;
        try
        {
            Win32.GetDpiForMonitor(mon.ResolveHMonitor(), Win32.DPI_EFFECTIVE, out uint dx, out uint dy);
            if (dx > 0 && dy > 0) { sx = dx / 96.0; sy = dy / 96.0; }
        }
        catch { }
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
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
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
            Text = "滚动截图",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Theme.Text),
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _progress = new TextBlock
        {
            Text = "已拼接 1 段",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Accent),
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

        // 按钮行：完成（强调）· 取消
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        // 完成按钮前景随强调色明暗自适应（浅强调色→黑字，深→白字），同 OCR 窗 OnAccentColor，避免浅底白字不可读
        var done = MakeBtn("完成", Theme.Accent, Fluent.OnAccentColor(Theme.Accent));
        done.Click += (_, _) => OnDone?.Invoke();
        var cancel = MakeBtn("取消", Theme.Hover, Theme.TextDim);
        cancel.Click += (_, _) => OnCancel?.Invoke();
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
            if (Environment.GetEnvironmentVariable("FC_TOAST_NOEXCLUDE") != "1")
                Win32.SetWindowDisplayAffinity(h, Win32.WDA_EXCLUDEFROMCAPTURE);
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
        FrameCapture.Core.Trace.Log("scroll bar shown");
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }

    public void SetProgress(int segments, int heightPx)
    {
        _progress.Text = $"已拼接 {segments} 段 · 高 {heightPx} px";
        _hint.Text = DefaultHint;
        _hint.Foreground = new SolidColorBrush(Theme.TextDim);
    }

    /// <summary>刷一句反馈（如「滚动太快」），琥珀色提醒；下次成功拼接由 SetProgress 复位。</summary>
    public void FlashHint(string msg)
    {
        _hint.Text = msg;
        _hint.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xA8, 0x4B)); // 琥珀色提醒
    }

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
            Padding = new Thickness(14, 6, 14, 6),
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
