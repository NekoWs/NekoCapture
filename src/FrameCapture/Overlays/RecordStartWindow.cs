using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using FrameCapture.Capture;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>
/// 选区定完、还没起录时摆在选区旁边的那一条：一枚「开始录制」加一枚「取消」。
/// 选区切到录制之后先把这一条摆出来——范围定完往往还要摆一摆窗口、清一下桌面，确认选区不等于马上开录。
/// 窗口不抢焦点（<c>ShowActivated = false</c>），点按钮才起录；起录或取消之后自己收走。
/// </summary>
public sealed class RecordStartWindow : Window
{
    const double W = 210;
    const double H = 76;

    static RecordStartWindow? _current;

    /// <summary>摆出「开始录制」那一条；已有实例时先收掉。</summary>
    /// <param name="region">选中的区域（物理像素），用它给自己找落点。</param>
    /// <param name="monitorDevice">区域所在显示器，决定 DPI 与工作区。</param>
    /// <param name="onStart">点「开始录制」时要跑的动作。</param>
    public static void Show(RectI region, string monitorDevice, Action onStart)
    {
        DismissCurrent();
        var w = new RecordStartWindow(region, monitorDevice, onStart);
        _current = w;
        w.Show();
    }

    /// <summary>收起这一条（起录前、重新选区前调用）。</summary>
    public static void DismissCurrent()
    {
        var w = _current;
        _current = null;
        if (w == null) return;
        try { w.Close(); } catch { }
    }

    RecordStartWindow(RectI region, string monitorDevice, Action onStart)
    {
        Title = "FrameCapture Record Start";
        Fluent.BorderlessOverlay(this);
        var mon = Monitors.ByDevice(monitorDevice) ?? Monitors.Nearest(region.X, region.Y);
        var (sx, sy) = Monitors.DpiScaleOf(mon);
        int px = region.X;
        int py = region.Y + region.H + 12;
        int bottom = mon.Bounds.Y + mon.Bounds.H;
        if (py + (int)(H * sy) > bottom) py = region.Y + region.H - (int)(H * sy) - 12;   // 贴到屏底就挪到框内
        px = Math.Max(mon.Bounds.X + 8, Math.Min(px, mon.Bounds.X + mon.Bounds.W - (int)(W * sx) - 8));
        py = Math.Max(mon.Bounds.Y + 8, py);
        Left = px / sx;
        Top = py / sy;

        var start = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Theme.AccentBrush,
            Padding = new Thickness(12, 6, 12, 6),
            Cursor = Cursors.Hand,
            Focusable = true,
            Child = new TextBlock
            {
                Text = Loc.T(K.Actions.RecordStartLabel),
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)),
                VerticalAlignment = VerticalAlignment.Center,
            },
            ToolTip = Loc.T(K.Settings.StartRecordingAfterSelectingTip),
        };
        var cancel = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Theme.RowBgBrush,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            Focusable = true,
            Child = new TextBlock
            {
                Text = Loc.T(K.Common.Cancel),
                FontSize = 12.5,
                Foreground = Theme.TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Fluent.ChipHover(start);
        Fluent.ChipHover(cancel);
        void Go()
        {
            _current = null;
            try { Close(); } catch { }
            onStart();
        }
        start.MouseLeftButtonUp += (_, e) => { Go(); e.Handled = true; };
        cancel.MouseLeftButtonUp += (_, e) => { DismissCurrent(); e.Handled = true; };
        start.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Go(); e.Handled = true; } };
        cancel.KeyDown += (_, e) => { if (e.Key is Key.Escape) { DismissCurrent(); e.Handled = true; } };

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(start);
        row.Children.Add(cancel);
        var card = new Border
        {
            Background = Theme.CardBgBrush,
            BorderBrush = Fluent.CardEdge(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black },
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = Loc.T(K.Capture.ConfirmSelection),
                        FontSize = 11.5,
                        Foreground = Theme.TextDimBrush,
                        Margin = new Thickness(0, 0, 0, 8),
                    },
                    row,
                },
            },
        };
        Content = card;
        Width = W;
        Closed += (_, _) => { if (ReferenceEquals(_current, this)) _current = null; };
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }
}
