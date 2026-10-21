using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 通用提示气泡：一条图标 + 正文 + 可选副行的轻提示，落在主屏工作区右下角（托盘那一侧）。
/// 取代系统托盘气泡——样式、圆角、描边、动效与截屏浮窗同源，用户在一处学到的手感处处通用。
/// 复用同一套浮窗骨架：<c>ShowActivated=false</c> 不打断输入、WDA 排除不入镜、悬停暂停自动消失。
/// 任何需要「告知一句就走」的场景都用它，不必各写一份浮窗。
/// </summary>
public sealed class NoticeToast : Window
{
    static NoticeToast? _current;

    readonly DispatcherTimer _dismiss;
    bool _closing;

    // 336 放下中文启动行绰绰有余，英文同一句要 285 DIP 正文宽，加上图标列与关闭钮就顶到了边：
    // 一挤 WPF 便在 Ctrl+Alt+R 的 + 上找断点，键位被劈成两截。372 让最长那句一行放完。
    const double W = 372;      // DIP 宽
    const double EstH = 62;    // 估算高（落点用；实际 SizeToContent）

    /// <summary>弹一条提示气泡。同一时刻只保留一条，再次调用先收起上一条。</summary>
    /// <param name="message">正文，一行短文本，原样上屏。</param>
    /// <param name="detail">副行（暗色小字），补充一句上下文；传 null 则不出副行。</param>
    /// <param name="glyph">Segoe Fluent Icons 的 PUA 码位，只做装饰，语义由文字承担。</param>
    /// <param name="seconds">停留秒数，悬停期间暂停计时；传 0 或负数则常驻，由用户手动关闭。</param>
    public static void Show(string message, string? detail = null, string glyph = "\uE946", double seconds = 4.0)
    {
        Trace.Log("notice show");
        _current?.Close();
        _current = new NoticeToast(message, detail, glyph, seconds);
        _current.Show(); // ShowActivated=false：不打断用户当前输入
    }

    /// <summary>立即收起当前气泡（应用退出时调用，避免进程已走、气泡还挂在屏幕上）。</summary>
    public static void DismissCurrent() => _current?.Dismiss();

    NoticeToast(string message, string? detail, string glyph, double seconds)
    {
        Title = "FrameCapture Notice";
        // 外壳一律交给 BorderlessOverlay：它把 SizeToContent 设成 WidthAndHeight，
        // 窗口尺寸由卡片量出来。这里再覆盖成 Height，WindowChrome 的窗口与客户区就对不上，
        // 卡片画不出来，只剩一块黑底。
        Fluent.BorderlessOverlay(this);

        // 落点：主屏工作区右下角，靠托盘图标——用户视线本来就在「图标在这」，提示出现在同一侧才连得上。
        // SystemParameters.WorkArea 已是 DIP 且排除任务栏，直接用作 Left/Top，无需 DPI 换算。
        var wa = SystemParameters.WorkArea;
        Left = Math.Max(wa.Left, wa.Right - W - 16);
        Top = Math.Max(wa.Top, wa.Bottom - EstH - 16);

        var card = new Border
        {
            Width = W,
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = Core.Fluent.CardEdge(),   // 上缘亮下缘暗：暗色里投影看不见，靠这条边说清卡片是浮着的
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 8, 10),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black },
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
            FontSize = 17,
            Foreground = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 11, 0),
        });

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            FontFamily = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrEmpty(detail))
            texts.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 11,
                Foreground = new SolidColorBrush(Theme.TextDim),
                FontFamily = new FontFamily("Segoe UI"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
            });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var close = Fluent.IconButton("\uE711", Loc.T(K.Common.Close), 26, 12, new SolidColorBrush(Theme.TextDim));
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => Dismiss();
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        card.Child = grid;
        Content = card;
        // 宽度是定值，高度随正文与副行长出来：先在动画开始前按内容高把底边贴准
        // （FadeIn 收尾会把 Left/Top 写回它开始时的值，之后再改会被覆盖）。
        card.Measure(new Size(W, double.PositiveInfinity));
        Top = Math.Max(wa.Top, wa.Bottom - Math.Ceiling(card.DesiredSize.Height) - 16);
        SizeChanged += (_, _) =>
        {
            Left = Math.Max(wa.Left, wa.Right - ActualWidth - 16);
            Top = Math.Max(wa.Top, wa.Bottom - ActualHeight - 16);
        };
        // 与截屏浮窗同款登场：淡入 + 上浮，卡片再从 0.85 弹到位
        ContentRendered += (_, _) =>
        {
            // 入场动画收尾会把 Left/Top 写回它开始时的值：等这一帧的活干完再落一次位
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Left = Math.Max(wa.Left, wa.Right - ActualWidth - 16);
                Top = Math.Max(wa.Top, wa.Bottom - ActualHeight - 16);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Fluent.PopIn(card);
        };

        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(h);
            // 提示气泡不该出现在用户自己的截图里；FC_TOAST_NOEXCLUDE=1 仅供视觉审查截图
            Core.Fluent.ExcludeFromCapture(h);
        };

        _dismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds > 0 ? seconds : 3600) };
        _dismiss.Tick += (_, _) => Dismiss();
        if (seconds > 0) _dismiss.Start();
        MouseEnter += (_, _) => _dismiss.Stop();                    // 正在读就别收走
        MouseLeave += (_, _) => { if (seconds > 0) _dismiss.Start(); };

        Closed += (_, _) => { _dismiss.Stop(); if (ReferenceEquals(_current, this)) _current = null; };

        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }

    /// <summary>收起本条气泡，重复调用无副作用。</summary>
    void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _dismiss.Stop();
        Fluent.CloseWithFade(this);
    }
}
