using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>时间轴的播放控制、范围信息与轻量绘制。</summary>
public sealed partial class EditorWindow
{
    TextBlock? _timeLabel, _trimSummary;
    Button? _speedButton, _resetTrimButton;

    sealed class TimelineSurface : FrameworkElement
    {
        /// <summary>绘制当前范围、刻度与播放头。</summary>
        internal Action<DrawingContext>? Paint { get; set; }

        /// <inheritdoc/>
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            Paint?.Invoke(drawingContext);
        }
    }

    FrameworkElement BuildTimelinePanel()
    {
        var panel = new StackPanel { Margin = new Thickness(4, 1, 4, 2) };
        var toolbar = new Grid();
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var transport = new StackPanel { Orientation = Orientation.Horizontal };
        transport.Children.Add(CommandIcon("\uF8AC", K.Editor.BackOneSecond, "sec-prev", () => StepSeconds(-1)));
        transport.Children.Add(CommandIcon("\uF08D", K.Editor.PreviousFrame, "frame-prev", () => Step(-1)));
        _bPlay = CommandIcon("\uF5B0", K.Commands.PlayToggleName, "play-toggle", TogglePlay);
        transport.Children.Add(_bPlay);
        transport.Children.Add(CommandIcon("\uF08F", K.Editor.NextFrame, "frame-next", () => Step(1)));
        transport.Children.Add(CommandIcon("\uF8AD", K.Editor.ForwardOneSecond, "sec-next", () => StepSeconds(1)));
        toolbar.Children.Add(transport);
        var clock = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _timeLabel = WorkspaceLabel("");
        _timeLabel.Margin = new Thickness(12, 0, 6, 0);
        clock.Children.Add(_timeLabel);
        _speedButton = TextButton(K.Commands.SpeedResetName, ShowSpeedMenu);
        _speedButton.Content = "1×";
        _speedButton.Padding = new Thickness(8, 0, 8, 0);
        _speedButton.BorderThickness = new Thickness(0);
        clock.Children.Add(_speedButton);
        Grid.SetColumn(clock, 1);
        toolbar.Children.Add(clock);
        var trim = new StackPanel { Orientation = Orientation.Horizontal };
        trim.Children.Add(TextButton(K.Editor.TrimStart, () => SetTrimPoint(true), "trim-a"));
        trim.Children.Add(TextButton(K.Editor.TrimEnd, () => SetTrimPoint(false), "trim-b"));
        _resetTrimButton = TextButton(K.Editor.ResetTrim, ResetTrim, "trim-clear");
        trim.Children.Add(_resetTrimButton);
        Grid.SetColumn(trim, 2);
        toolbar.Children.Add(trim);
        panel.Children.Add(toolbar);
        if (_rangeHost != null)
        {
            _rangeHost.Margin = new Thickness(6, 5, 6, 0);
            panel.Children.Add(_rangeHost);
        }
        _trimSummary = WorkspaceLabel(Loc.T(K.Editor.FullRecording), 11);
        _trimSummary.Margin = new Thickness(6, 0, 6, 0);
        panel.Children.Add(_trimSummary);
        return panel;
    }

    void SetTrimPoint(bool start)
    {
        if (_encoding) return;
        if (_playing) TogglePlay();
        _rd.Count = _seq.Frames.Count;
        if (start) _rd.SetA(_index); else _rd.SetB(_index);
        Show(_index);
    }

    void ResetTrim()
    {
        if (_encoding) return;
        _rd.A = _rd.B = -1;
        Show(_index);
    }

    double FrameTime(int index)
    {
        index = Math.Clamp(index, 0, _times.Length - 1);
        double time = (_times[index] - _times[0]) / 1000.0;
        return double.IsFinite(time) && time >= 0 && (_times[^1] > _times[0] || index == 0)
            ? time : index / (double)_fps;
    }

    static string ClockText(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}";
    }

    void UpdatePlaybackInterval()
    {
        if (!MultiFrame) return;
        var (a, b) = TrimRange;
        double delay = _index < b ? (FrameTime(_index + 1) - FrameTime(_index)) * 1000 : 1000.0 / _fps;
        if (!double.IsFinite(delay) || delay <= 0) delay = 1000.0 / _fps;
        _playTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(delay / _speed, 8, 60_000));
    }

    void StepSeconds(int seconds)
    {
        if (!MultiFrame) return;
        double target = FrameTime(_index) + seconds;
        int lo = 0, hi = _seq.Frames.Count - 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (FrameTime(mid) < target) lo = mid + 1; else hi = mid;
        }
        if (_playing) TogglePlay();
        Show(lo);
    }

    void UpdateTimelineStatus()
    {
        if (!MultiFrame) return;
        if (_timeLabel != null) _timeLabel.Text = $"{ClockText(FrameTime(_index))} / {ClockText(FrameTime(_seq.Frames.Count - 1))}";
        if (_speedButton != null) _speedButton.Content = $"{_speed:0.##}×";
        var (a, b) = TrimRange;
        bool trimmed = a > 0 || b < _seq.Frames.Count - 1;
        if (_trimSummary != null) _trimSummary.Text = trimmed
            ? Loc.F(K.Editor.SelectedDuration, ClockText(FrameTime(b) - FrameTime(a) + 1.0 / _fps), b - a + 1)
            : Loc.T(K.Editor.FullRecording);
        if (_resetTrimButton != null) _resetTrimButton.IsEnabled = trimmed && !_encoding;
    }

    void ShowSpeedMenu()
    {
        if (_speedButton == null || _encoding) return;
        var menu = NewWorkspaceMenu(_speedButton);
        foreach (double rate in new[] { 0.25, 0.5, 1, 2, 4, 8 })
        {
            double value = rate;
            var item = new MenuItem { Header = $"{value:0.##}×", IsChecked = Math.Abs(_speed - value) < 0.001 };
            item.Click += (_, _) => SetSpeed(value);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void DrawRange()
    {
        _rd.Count = _seq.Frames.Count;
        _rd.Width = _rangeCanvas.ActualWidth;
        _rangeCanvas.InvalidateVisual();
        UpdateTimelineStatus();
    }

    void PaintTimeline(DrawingContext context)
    {
        double width = _rangeCanvas.ActualWidth;
        if (width < 8 || !MultiFrame) return;
        int count = _seq.Frames.Count;
        double px = width / (count - 1.0);
        var accent = new SolidColorBrush(Theme.Accent);
        var text = Theme.TextDimBrush;
        var track = new SolidColorBrush(Theme.TrackBg);
        var (a, b) = TrimRange;
        double left = a * px, right = b * px;
        context.DrawRoundedRectangle(track, null, new Rect(0, 13, width, 12), 4, 4);
        if (a > 0 || b < count - 1)
        {
            var selected = new SolidColorBrush(Color.FromArgb(0x45, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B));
            context.DrawRoundedRectangle(selected, null, new Rect(left, 9, Math.Max(1, right - left), 20), 4, 4);
        }
        double tickStep = Math.Max(1, Math.Ceiling(10 / Math.Max(0.001, px)));
        var tickPen = new Pen(Theme.StrokeSoftBrush, 1);
        for (double frame = 0; frame < count; frame += tickStep)
        {
            double x = frame * px;
            context.DrawLine(tickPen, new Point(x, 16), new Point(x, 22));
        }
        int labelStep = Math.Max(1, (int)Math.Ceiling((count - 1.0) * 88 / width));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        for (int index = 0; index < count; index += labelStep)
        {
            var label = new FormattedText(ClockText(FrameTime(index)), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, text, dpi);
            double x = Math.Clamp(index * px - label.Width / 2, 0, Math.Max(0, width - label.Width));
            context.DrawText(label, new Point(x, 31));
        }
        bool active = _rd.A >= 0;
        var handles = active ? accent : new SolidColorBrush(Color.FromArgb((byte)(_tlHover ? 0xD0 : 0x80), Theme.Text.R, Theme.Text.G, Theme.Text.B));
        context.DrawRoundedRectangle(handles, null, new Rect(Math.Clamp(left - 2, 0, width - 4), 7, 4, 24), 2, 2);
        context.DrawRoundedRectangle(handles, null, new Rect(Math.Clamp(right - 2, 0, width - 4), 7, 4, 24), 2, 2);
        double position = Math.Clamp(_index * px, 1, width - 1);
        var head = Theme.TextBrush;
        context.DrawLine(new Pen(head, 1.5), new Point(position, 5), new Point(position, 29));
        context.DrawEllipse(head, null, new Point(position, 5), 3.5, 3.5);
    }
}
