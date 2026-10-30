using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>贴近选区的录制确认与计时面板；等待、录制和关闭拥有独立状态。</summary>
public sealed partial class RecordPanelWindow : Window
{
    static RecordPanelWindow? _current;
    readonly TextBlock _time, _startText, _stopText, _recordStatus;
    readonly Ellipse _dot;
    readonly Border _start, _stop, _card;
    readonly StackPanel _readyDetails;
    readonly System.Windows.Threading.DispatcherTimer _tick;
    readonly Rect _regionDip, _workDip;
    System.Diagnostics.Stopwatch? _clock;
    Action? _onStart, _onStop;
    bool _armed, _finished;

    /// <summary>当前录制面板；没有时为 null。</summary>
    public static RecordPanelWindow? Current => _current;

    /// <summary>显示已确认的录制范围，等待点击或确认键后起录。</summary>
    /// <param name="region">选中的物理像素区域。</param>
    /// <param name="monitorDevice">区域所在显示器。</param>
    /// <param name="onStart">开始录制的回调。</param>
    /// <param name="onStop">取消或停止的回调。</param>
    public static void ShowArmed(RectI region, string monitorDevice, Action onStart, Action onStop)
    {
        Dismiss();
        var window = new RecordPanelWindow(region, monitorDevice, onStop);
        _current = window; window.SetArmed(onStart); window.Show();
    }

    /// <summary>显示正在录制的时长和停止按钮。</summary>
    /// <param name="region">选中的物理像素区域。</param>
    /// <param name="monitorDevice">区域所在显示器。</param>
    /// <param name="onStop">停止录制的回调。</param>
    public static void ShowRecording(RectI region, string monitorDevice, Action onStop)
    {
        Dismiss();
        var window = new RecordPanelWindow(region, monitorDevice, onStop);
        _current = window; window.SwitchToRecording(); window.Show();
    }

    /// <summary>切到录制状态，释放准备阶段的按键接管并开始计时。</summary>
    public void SwitchToRecording()
    {
        _armed = false; ReleaseReadyKeys();
        _readyDetails.Visibility = Visibility.Collapsed;
        _start.Visibility = Visibility.Collapsed;
        _recordStatus.Visibility = _dot.Visibility = _time.Visibility = Visibility.Visible;
        _stopText.Text = Loc.T(K.Actions.RecordStopLabel); _stop.ToolTip = _stopText.Text;
        _clock = System.Diagnostics.Stopwatch.StartNew(); UpdateTime(); _tick.Start();
        _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(1))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        ResizePanel();
    }

    void SetArmed(Action onStart)
    {
        _armed = true; _onStart = onStart;
        _readyDetails.Visibility = _start.Visibility = Visibility.Visible;
        _recordStatus.Visibility = _dot.Visibility = _time.Visibility = Visibility.Collapsed;
        _stopText.Text = Loc.T(K.Common.Cancel); _stop.ToolTip = Loc.F(K.Capture.RecordCancelKey, _cancelChord);
        Services.HotkeyManager.ArmedRecordKey = HandleReadyKey;
        ResizePanel();
    }

    void StartRequested()
    {
        if (!_armed || _finished || !ReferenceEquals(_current, this)) return;
        _armed = false; ReleaseReadyKeys(); _start.IsHitTestVisible = false;
        var start = _onStart; _onStart = null; start?.Invoke();
    }

    void StopRequested()
    {
        if (_finished) return;
        _finished = true;
        var stop = _onStop; _onStop = null;
        Dismiss(); stop?.Invoke();
    }

    /// <summary>关闭当前面板并停止其计时和会话按键接管。</summary>
    public static void Dismiss()
    {
        var window = _current; _current = null;
        window?.Close();
    }

    RecordPanelWindow(RectI region, string monitorDevice, Action onStop)
    {
        _onStop = onStop;
        Title = "FrameCapture Record Panel";
        Fluent.BorderlessOverlay(this); SizeToContent = SizeToContent.Manual;
        var monitor = Monitors.ByDevice(monitorDevice) ?? Monitors.Nearest(region.X, region.Y);
        var (sx, sy) = Monitors.DpiScaleOf(monitor);
        var work = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == monitor.DeviceName)?.WorkingArea
            ?? new System.Drawing.Rectangle(monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.W, monitor.Bounds.H);
        _workDip = new Rect(work.X / sx, work.Y / sy, work.Width / sx, work.Height / sy);
        _regionDip = new Rect(region.X / sx, region.Y / sy, region.W / sx, region.H / sy);
        _dot = new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(Theme.Danger),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _time = new TextBlock { Text = "0:00.0", FontSize = 13, Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center, Width = 62 };
        _recordStatus = new TextBlock { Text = Loc.T(K.Capture.RecordActive), FontSize = 11.5,
            Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _startText = new TextBlock { Text = Loc.T(K.Common.RecordStartLabel), FontSize = 12.5,
            Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)) };
        _stopText = new TextBlock { FontSize = 12.5, Foreground = Theme.TextBrush };
        _start = PanelButton(_startText, Theme.AccentBrush);
        _stop = PanelButton(_stopText, Theme.RowBgBrush); _stop.Margin = new Thickness(8, 0, 0, 0);
        _start.MouseLeftButtonUp += (_, e) => { StartRequested(); e.Handled = true; };
        _stop.MouseLeftButtonUp += (_, e) => { StopRequested(); e.Handled = true; };
        _start.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { StartRequested(); e.Handled = true; } };
        _stop.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { StopRequested(); e.Handled = true; } };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { _recordStatus, _dot, _time, _start, _stop } };
        var settings = Services.AppHost.Current.Settings?.Current;
        _confirmChord = ValidPanelChord(settings?.PickerConfirmChord, "Enter");
        _cancelChord = ValidPanelChord(settings?.PickerCancelChord, "Esc");
        _confirmKey = PanelChord(_confirmChord); _cancelKey = PanelChord(_cancelChord);
        _readyDetails = new StackPanel { MaxWidth = Math.Min(300, _workDip.Width - 40), Margin = new Thickness(0, 0, 0, 10) };
        _readyDetails.Children.Add(new TextBlock { Text = Loc.T(K.Capture.RecordReady), FontSize = 13,
            FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
        _readyDetails.Children.Add(new TextBlock { Text = Loc.F(K.Capture.Region, region.W, region.H), FontSize = 11,
            Foreground = Theme.TextBrush, Margin = new Thickness(0, 3, 0, 0) });
        _start.ToolTip = Loc.F(K.Capture.RecordStartKey, _confirmChord);
        _card = new Border { Background = Theme.CardBgBrush, BorderBrush = Fluent.CardEdge(), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9),
            Child = new StackPanel { Children = { _readyDetails, row } } };
        Content = _card;
        _tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _tick.Tick += (_, _) => UpdateTime();
        SizeChanged += (_, _) => PlacePanel();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            uint style = Native.Win32.GetWindowLongW(hwnd, Native.Win32.GWL_EXSTYLE);
            Native.Win32.SetWindowLongW(hwnd, Native.Win32.GWL_EXSTYLE,
                style | Native.Win32.WS_EX_NOACTIVATE | Native.Win32.WS_EX_TOOLWINDOW);
            Fluent.ExcludeFromCapture(hwnd);
        };
        Closed += (_, _) =>
        {
            _armed = false; _finished = true; ReleaseReadyKeys(); _tick.Stop(); _clock = null;
            _dot.BeginAnimation(OpacityProperty, null);
            if (ReferenceEquals(_current, this)) _current = null;
        };
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }

    static Border PanelButton(TextBlock text, Brush fill)
    {
        var button = new Border { Child = text, Background = fill, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 6, 12, 6), Cursor = Cursors.Hand, Focusable = true };
        Fluent.ChipHover(button); return button;
    }

    void UpdateTime()
    {
        if (_clock == null) return;
        var time = _clock.Elapsed;
        _time.Text = $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds / 100}";
    }
}
