using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>
/// 选区录制上方那条控制面板：待开始时是「开始录制」，录制中变成「● 0:05.3 + 停止」。
/// 它在选区确认之后出现、贴在屏幕上方居中，与选区里那个「截图 / 录制」开关同一个位置——
/// 用户的眼睛已经在上面了，状态就不该再跑到右上角去。录制中的 REC 点与时长都在这里，
/// 不再由 <see cref="RegionOutlineWindow"/> 的右上角徽标说话（那条只服务快捷键起录）。
/// </summary>
public sealed class RecordPanelWindow : Window
{
    static RecordPanelWindow? _current;

    readonly TextBlock _time;
    readonly Ellipse _dot;
    readonly Border _start;
    readonly Border _stop;
    readonly TextBlock _startText;
    readonly TextBlock _stopText;
    readonly System.Windows.Threading.DispatcherTimer _tick;
    System.Diagnostics.Stopwatch? _clock;
    Action? _onStop;

    /// <summary>当前那条面板；没有时为 null。</summary>
    public static RecordPanelWindow? Current => _current;

    /// <summary>摆出「开始录制」那一态：等用户点下去再起录。</summary>
    /// <param name="region">选中的区域（物理像素），用它找落点。</param>
    /// <param name="monitorDevice">区域所在显示器，决定 DPI 与工作区。</param>
    /// <param name="onStart">点「开始录制」要跑的动作。</param>
    /// <param name="onStop">点「停止」要跑的动作。</param>
    public static void ShowArmed(RectI region, string monitorDevice, Action onStart, Action onStop)
    {
        Dismiss();
        var w = new RecordPanelWindow(region, monitorDevice, onStop);
        _current = w;
        w.SetArmed(onStart);
        w.Show();
    }

    /// <summary>直接摆出录制态：不经「开始」那一步起录的场合用它，面板一出现就在计时。</summary>
    /// <param name="region">选中的区域（物理像素）。</param>
    /// <param name="monitorDevice">区域所在显示器。</param>
    /// <param name="onStop">点「停止」要跑的动作。</param>
    public static void ShowRecording(RectI region, string monitorDevice, Action onStop)
    {
        Dismiss();
        var w = new RecordPanelWindow(region, monitorDevice, onStop);
        _current = w;
        w.Show();
        w.SwitchToRecording();
    }

    /// <summary>把手里那条面板切到录制态（开始计时，按钮换成「停止」）。</summary>
    public void SwitchToRecording()
    {
        _start.Visibility = Visibility.Collapsed;
        _dot.Visibility = Visibility.Visible;
        _time.Visibility = Visibility.Visible;
        _stop.Visibility = Visibility.Visible;
        _stopText.Visibility = Visibility.Visible;
        _clock = System.Diagnostics.Stopwatch.StartNew();
        UpdateTime();
        _tick.Start();
        var pulse = new DoubleAnimation(1, 0.35, new Duration(TimeSpan.FromSeconds(1)))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        _dot.BeginAnimation(OpacityProperty, pulse);
    }

    void SetArmed(Action onStart)
    {
        _start.Visibility = Visibility.Visible;
        _startText.Visibility = Visibility.Visible;
        _dot.Visibility = Visibility.Collapsed;
        _time.Visibility = Visibility.Collapsed;
        _stop.Visibility = Visibility.Collapsed;
        _stopText.Visibility = Visibility.Collapsed;
        _start.MouseLeftButtonUp += (_, e) => { _start.IsHitTestVisible = false; onStart(); e.Handled = true; };
    }

    /// <summary>收起面板（停止、取消、重新选区都走它）。计时器与红点一起停。</summary>
    public static void Dismiss()
    {
        var w = _current;
        _current = null;
        if (w == null) return;
        try { w.Close(); } catch { }
    }

    RecordPanelWindow(RectI region, string monitorDevice, Action onStop)
    {
        _onStop = onStop;
        Title = "FrameCapture Record Panel";
        Fluent.BorderlessOverlay(this);
        var mon = Monitors.ByDevice(monitorDevice) ?? Monitors.Nearest(region.X, region.Y);
        var (sx, sy) = Monitors.DpiScaleOf(mon);
        Width = 196;
        Height = 44;
        Left = (mon.Bounds.X + mon.Bounds.W / 2) / sx - Width / 2;
        Top = mon.Bounds.Y / sy + 56;   // 与选区里那枚「截图 / 录制」开关同一条线

        _dot = new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = new SolidColorBrush(Color.FromRgb(0xF2, 0x4B, 0x3F)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Visibility = Visibility.Collapsed,
        };
        _time = new TextBlock
        {
            Text = "0:00.0",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Width = 56,
        };
        _startText = new TextBlock
        {
            Text = Loc.T(K.Actions.RecordStartLabel),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _start = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Theme.AccentBrush,
            Padding = new Thickness(12, 5, 12, 5),
            Cursor = Cursors.Hand,
            Focusable = true,
            Child = _startText,
            ToolTip = Loc.T(K.Settings.StartRecordingAfterSelectingTip),
        };
        _stopText = new TextBlock
        {
            Text = Loc.T(K.Actions.RecordStopLabel),
            FontSize = 12.5,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _stop = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Theme.RowBgBrush,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            Focusable = true,
            Child = _stopText,
            ToolTip = Loc.T(K.Actions.RecordStopLabel),
        };
        Fluent.ChipHover(_start);
        Fluent.ChipHover(_stop);
        _stop.MouseLeftButtonUp += (_, e) => { Dismiss(); _onStop?.Invoke(); e.Handled = true; };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { _dot, _time, _start, _stop } };
        Content = new Border
        {
            Background = Theme.CardBgBrush,
            BorderBrush = Fluent.CardEdge(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 7, 12, 7),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black },
            Child = row,
        };

        _tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _tick.Tick += (_, _) => UpdateTime();
        Closed += (_, _) =>
        {
            _tick.Stop();
            _clock = null;
            if (ReferenceEquals(_current, this)) _current = null;
        };
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }

    void UpdateTime()
    {
        if (_clock == null) return;
        var t = _clock.Elapsed;
        _time.Text = $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }
}
