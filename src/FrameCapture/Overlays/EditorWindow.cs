using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Encoding;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>
/// 编辑器：帧显示 + 传输控制 + 时间轴 + 右侧操作栏 + 确定（GIF 进剪贴板）。
/// 绘图层在后续迭代叠加。
/// </summary>
public sealed partial class EditorWindow : Window
{
    public event Action? ClosedByUser;

    private readonly SettingsService _settings;
    private readonly CapturedSequence _seq;
    private List<BitmapSource> _bitmaps;
    private readonly Image _view;
    private readonly System.Windows.Threading.DispatcherTimer _playTimer;
    private readonly TextBlock _hud;

    private int _index;
    private bool _playing;
    private bool _userClosed;

    private static Color DarkBg => Theme.WindowBg;
    private static Color PanelBg => Theme.PanelBg;
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
    private Button? _bPlay;
    private FloatBar? _barTop, _barBottom;
    private System.Windows.Threading.DispatcherTimer? _hideTimer;

    public EditorWindow(SettingsService settings, CapturedSequence seq)
    {
        _settings = settings;
        _seq = seq;
        _bitmaps = seq.Frames.Select(BgraImage.ToSource).ToList();

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        Topmost = true;
        Deactivated += (_, _) => { if (IsLoaded) { Topmost = false; Topmost = true; } };
        Title = "FrameCapture Editor";
        FrameCapture.Core.Fluent.FadeIn(this, 240, 14);
        Background = new SolidColorBrush(DarkBg);
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x36)); // 消除 WindowStyle=None 的白边
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };

        var mon = Monitors.ByDevice(seq.MonitorDevice) ?? Monitors.Nearest(seq.Region.X, seq.Region.Y);
        Width = Math.Min(1600, Math.Max(560, seq.Region.W + 120));
        Height = Math.Min(1000, Math.Max(420, seq.Region.H + 220));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (mon != null)
        {
            Left = mon.Bounds.X + mon.Bounds.W / 2.0 - Width / 2;
            Top = mon.Bounds.Y + mon.Bounds.H / 2.0 - Height / 2;
        }

        _view = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.HighQuality);

        _hud = new TextBlock
        {
            Foreground = new SolidColorBrush(Theme.Text),
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };

        _playTimer = new System.Windows.Threading.DispatcherTimer();
        _playTimer.Tick += (_, _) =>
        {
            if (_seq.Frames.Count == 0) return;
            var (a, b) = TrimRange;
            int next = _index + 1;
            if (next > b || next >= _seq.Frames.Count) next = a;
            Show(next);
        };
        _playTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000.0 / Math.Clamp(_settings.Current.Fps, 5, 60), 20));

        InitDrawing();
        BuildLayout();
        Theme.Changed += Editor_OnTheme;
        Closed += (_, _) => Theme.Changed -= Editor_OnTheme;
        Show(0);
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        PreviewKeyDown += OnKeyDown;
        Closed += (_, _) =>
        {
            if (_userClosed) return;
            _userClosed = true;
            ClosedByUser?.Invoke();
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(hwnd);
            Win32.SetWindowTextW(hwnd, "FrameCapture Editor"); // WindowStyle=None 时原生标题为空，自动化需要
        };
    }

    // ---------------- 布局 ----------------

    Button IconBtn(string glyph, string tip, double size = 36) =>
        FrameCapture.Core.Fluent.IconButton(glyph, tip, size, 15);

    void BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(6) };
        _viewGrid.Children.Add(_view);
        _viewGrid.Children.Add(_canvas);
        var viewHost = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            Child = _viewGrid,
        };
        root.Children.Add(viewHost);
        Content = root;

        BuildHoverBars();

        LocationChanged += (_, _) => PositionBars();
        SizeChanged += (_, _) => PositionBars();
        Closed += (_, _) => { _hideTimer?.Stop(); _barTop?.Close(); _barBottom?.Close(); };
        MouseEnter += (_, _) => ShowBars();
        MouseLeave += (_, _) => ScheduleAutoHide();
        if (IsLoaded) ShowBars();
        else Loaded += (_, _) => ShowBars();
    }

    void Editor_OnTheme()
    {
        var bg = Background as System.Windows.Media.SolidColorBrush;
        if (bg != null)
        {
            var anim = new System.Windows.Media.Animation.ColorAnimation(Theme.WindowBg, TimeSpan.FromMilliseconds(260));
            var holder = new System.Windows.Media.SolidColorBrush(bg.Color);
            Background = holder;
            holder.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, anim);
        }
        BorderBrush = new System.Windows.Media.SolidColorBrush(Theme.IsDark ? Color.FromRgb(0x32, 0x32, 0x36) : Color.FromRgb(0xD4, 0xD4, 0xD4));
        _hud.Foreground = new System.Windows.Media.SolidColorBrush(Theme.Text);
        if (_rangeHost != null) _rangeHost.Background = new System.Windows.Media.SolidColorBrush(Theme.TrackBg);
        DrawRange();
    }

    bool MultiFrame => _seq.Frames.Count > 1;

    void BuildHoverBars()
    {
        var tools = BuildToolsBar();
        if (!MultiFrame)
        {
            tools.Children.Add(Sep());
            var bSaveF = IconBtn("\uE74E", "保存当前帧 (PNG)"); bSaveF.Click += (_, _) => SaveFrame();
            var bCopyF = IconBtn("\uE8C8", "复制当前帧 (位图)"); bCopyF.Click += (_, _) => ClipboardService.SetFrame(_seq.Frames[_index]);
            var bOkF = IconBtn("\uE73E", "确定：写入剪贴板 (Enter)"); bOkF.Click += (_, _) => Commit();
            var bX = IconBtn("\uE711", "取消 (Esc)"); bX.Click += (_, _) => Close();
            foreach (var b in new[] { bSaveF, bCopyF, bOkF, bX }) tools.Children.Add(b);
            FrameCapture.Core.Fluent.HighlightBtn(bOkF, true); // 主按钮：accent 实心
        }
        tools.Children.Add(Sep());
        var bSet = IconBtn("\uE713", "设置"); bSet.Click += (_, _) => SettingsWindow.ShowOrFocus();
        var bHelp = IconBtn("\uE897", "帮助与快捷键"); bHelp.Click += (_, _) => HelpWindow.ShowOrFocus();
        tools.Children.Add(bSet);
        tools.Children.Add(bHelp);
        _barTop = new FloatBar(this, tools);
        _barTop.EnterDelayMs = 0;

        if (MultiFrame)
        {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            int fps = (int)Math.Clamp(_settings.Current.Fps, 5, 60);
            var bPrevSec = IconBtn("\uE892", "上一秒 (Shift+←)"); bPrevSec.Click += (_, _) => Step(-fps);
            var bPrev = IconBtn("\uE100", "上一帧 (←)"); bPrev.Click += (_, _) => Step(-1);
            _bPlay = IconBtn("\uE768", "播放/暂停 (空格)"); _bPlay.Click += (_, _) => TogglePlay();
            var bNext = IconBtn("\uE101", "下一帧 (→)"); bNext.Click += (_, _) => Step(+1);
            var bNextSec = IconBtn("\uE893", "下一秒 (Shift+→)"); bNextSec.Click += (_, _) => Step(+fps);
            foreach (var b in new FrameworkElement[] { bPrevSec, bPrev, _bPlay, bNext, bNextSec }) bar.Children.Add(b);
            bar.Children.Add(Sep());
            if (_rangeHost != null)
            {
                _rangeHost.Width = 320;
                _rangeHost.VerticalAlignment = VerticalAlignment.Center;
                _rangeHost.Margin = new Thickness(4, 0, 2, 0);
                bar.Children.Add(_rangeHost);
            }
            _hud.Margin = new Thickness(8, 0, 4, 0);
            bar.Children.Add(_hud);
            bar.Children.Add(Sep());
            var bSaveFrame = IconBtn("\uE74E", "保存当前帧 (PNG)  Ctrl+S"); bSaveFrame.Click += (_, _) => SaveFrame();
            var bCopyFrame = IconBtn("\uE8C8", "复制当前帧 (位图)  Ctrl+C"); bCopyFrame.Click += (_, _) => ClipboardService.SetFrame(_seq.Frames[_index]);
            var bSaveGif = IconBtn("\uE116", "保存为动图 (GIF)  Ctrl+E"); bSaveGif.Click += (_, _) => SaveGif();
            var bAfter = IconBtn("\uE955", "保存视觉暂留合成图  Ctrl+H"); bAfter.Click += (_, _) => SaveAfterimage();
            foreach (var b in new[] { bSaveFrame, bCopyFrame, bSaveGif, bAfter }) bar.Children.Add(b);
            bar.Children.Add(Sep());
            var bCancel = IconBtn("\uE711", "取消 (Esc)"); bCancel.Click += (_, _) => Close();
            var bOk = IconBtn("\uE73E", "确定：GIF 写入剪贴板 (Enter)"); bOk.Click += (_, _) => Commit();
            bar.Children.Add(bCancel);
            bar.Children.Add(bOk);
            FrameCapture.Core.Fluent.HighlightBtn(bOk, true); // 主按钮：accent 实心
            _barBottom = new FloatBar(this, bar);
            _barBottom.EnterDelayMs = 110; // 错开入场：绘制条先、播放条后
        }

        if (_barBottom != null) HookBar(_barBottom);
    }

    static System.Windows.Shapes.Rectangle Sep() => new()
    {
        Width = 1,
        Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
        Margin = new Thickness(6, 7, 6, 7),
    };

    void HookBar(FloatBar b)
    {
        b.MouseEnter += (_, _) => { _hideTimer?.Stop(); };
        b.MouseLeave += (_, _) => ScheduleAutoHide();
    }

    void ShowBars()
    {
        _hideTimer?.Stop();
        bool any = false;
        if (_barTop is { IsVisible: false } t) { t.ShowBar(); any = true; }
        if (_barBottom is { IsVisible: false } b2) { b2.ShowBar(); any = true; }
        PositionBars();
        if (any) Dispatcher.BeginInvoke(PositionBars, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void ScheduleAutoHide()
    {
        if (_barBottom?.IsMouseOver == true) return;
        if (_hideTimer == null)
        {
            _hideTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _hideTimer.Tick += (_, _) => { _hideTimer!.Stop(); _barBottom?.HideBar(); };
        }
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    void PositionBars()
    {
        // 绘制条：预览下方外侧，常驻不自动隐藏；拖过的尊重用户位置
        if (_barTop is { ManualPos: false } t)
        {
            t.Left = Left + (ActualWidth - t.ActualWidth) / 2;
            t.Top = Top + ActualHeight + 10;
        }
        // 播放条：预览画面下缘内侧，随鼠标离开自动隐藏；拖过的尊重用户位置
        if (_barBottom is { ManualPos: false } b)
        {
            b.Left = Left + (ActualWidth - b.ActualWidth) / 2;
            b.Top = Top + ActualHeight - b.ActualHeight - 16;
        }
    }

    StackPanel BuildToolsBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < ToolOrder.Length; i++)
        {
            var tool = ToolOrder[i];
            var b = IconBtn(ToolGlyph[i], ToolTip[i]);
            b.PreviewMouseLeftButtonDown += (sender2, e2) => { e2.Handled = true; SetTool(tool); };
            _toolBtns.Add(b);
            bar.Children.Add(b);
        }
        bar.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(8, 6, 8, 6) });
        for (int i = 0; i < Swatches.Length; i++)
        {
            int idx = i;
            var inner = new System.Windows.Shapes.Ellipse
            {
                Width = 18, Height = 18, Fill = new SolidColorBrush(Swatches[idx]),
                Stroke = new SolidColorBrush(Theme.Stroke), StrokeThickness = 1, // 白色色块在浅底也可见
            };
            var bd = new Border
            {
                Child = inner,
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(13),
                Background = Brushes.Transparent,
                BorderBrush = i == 0 ? new SolidColorBrush(Acc) : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = "颜色",
            };
            FrameCapture.Core.Fluent.ChipHover(bd);
            bd.MouseLeftButtonDown += (sender2, e2) => { _colorIdx = idx; RefreshSwatches(); e2.Handled = true; };
            _swatchBtns.Add(bd);
            bar.Children.Add(bd);
        }
        bar.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(8, 6, 8, 6) });
        foreach (var (w, lbl) in new[] { (0, "细"), (1, "中"), (2, "粗") }.Select((t, i) => (t.Item1, t.Item2)))
        {
            int idx = w;
            var b = IconBtn("", lbl + "笔画", 28);
            b.FontSize = 9 + idx * 4;
            b.PreviewMouseLeftButtonDown += (sender2, e2) => { _widthIdx = idx; RefreshWidths(); e2.Handled = true; };
            _widthBtns.Add(b);
            bar.Children.Add(b);
        }
        bar.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(8, 6, 8, 6) });
        var undo = IconBtn("", "撤销 (Ctrl+Z)");
        undo.Click += (_, _) => UndoAnn();
        bar.Children.Add(undo);
        return bar;
    }

    void RefreshSwatches()
    {
        for (int i = 0; i < _swatchBtns.Count; i++)
            _swatchBtns[i].BorderBrush = i == _colorIdx ? new SolidColorBrush(Acc) : Brushes.Transparent;
    }

    void RefreshWidths()
    {
        for (int i = 0; i < _widthBtns.Count; i++)
            _widthBtns[i].Background = i == _widthIdx ? new SolidColorBrush(Acc) : Brushes.Transparent;
    }

    // ---------------- 行为 ----------------

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        Trace.Log($"editor key={e.KeyboardDevice.Modifiers}+{e.Key}");
        int fps = (int)Math.Clamp(_settings.Current.Fps, 5, 60);
        var mods = e.KeyboardDevice.Modifiers;
        if (mods == ModifierKeys.Control && (e.Key == Key.Z || e.Key == Key.U)) { UndoAnn(); return; }
        if (mods == ModifierKeys.Control && e.Key == Key.Y) { RedoAnn(); return; }
        // 右侧竖排工具快捷键
        if (mods == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.S: SaveFrame(); return;
                case Key.C: ClipboardService.SetFrame(_seq.Frames[_index]); return;
                case Key.E: SaveGif(); return;
                case Key.H: SaveAfterimage(); return;
            }
        }
        if (mods == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.X: SetTool(Tool.Browse); return;
                case Key.D: SetTool(Tool.Dot); return;
                case Key.L: SetTool(Tool.Line); return;
                case Key.O: SetTool(Tool.Ellipse); return;
                case Key.A: SetTool(Tool.Arrow); return;
                case Key.P: SetTool(Tool.Freehand); return;
                case Key.C: SetTool(Tool.Crop); return;
                case Key.Delete: UndoAnn(); return;
            }
        }
        switch (e.Key)
        {
            case Key.Escape:
                if (_encoding) { _commitCts?.Cancel(); }
                else Close();
                break;
            case Key.Enter: Commit(); break;
            case Key.Space: TogglePlay(); e.Handled = true; break;
            case Key.Left when e.KeyboardDevice.Modifiers == ModifierKeys.Shift: Step(-fps); break;
            case Key.Right when e.KeyboardDevice.Modifiers == ModifierKeys.Shift: Step(+fps); break;
            case Key.Left: Step(-1); break;
            case Key.Right: Step(+1); break;
            case Key.Y: RedoAnn(); e.Handled = true; break;
            case Key.OemPlus or Key.Add: SetSpeed(_speed * 2); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract: SetSpeed(_speed / 2); e.Handled = true; break;
            case Key.D0 or Key.Multiply: SetSpeed(1.0); e.Handled = true; break;
        }
    }

    void Step(int delta)
    {
        if (_playing) TogglePlay();
        Show(_index + delta);
    }

    void TogglePlay()
    {
        _playing = !_playing;
        if (_bPlay != null) _bPlay.Content = _playing ? "" : "";
        Trace.Log($"play={_playing}");
        if (_playing) _playTimer.Start();
        else _playTimer.Stop();
    }

    double _speed = 1.0;

    /// <summary>播放倍速 ×0.25–×8（＋/－ 调整，0 或 × 复位）。</summary>
    void SetSpeed(double s)
    {
        _speed = Math.Clamp(s, 0.25, 8.0);
        _playTimer.Interval = TimeSpan.FromMilliseconds(
            Math.Max(1000.0 / Math.Clamp(_settings.Current.Fps, 5, 60), 20) / _speed);
        Show(_index);
    }

    void Show(int index)
    {
        _index = Math.Clamp(index, 0, _seq.Frames.Count - 1);
        _view.Source = _bitmaps[_index];
        double tMs = _seq.Frames[_index].TimeMs;
        _hud.Text = $"{_index + 1}/{_seq.Frames.Count}  {tMs / 1000.0:0.00}s"
            + (_speed != 1.0 ? $"  ×{_speed:0.###}" : "")
            + (_seq.DroppedFrames > 0 ? $"  ⚠内存预算已丢 {_seq.DroppedFrames} 帧" : "");
        DrawRange();
    }

    bool _encoding;
    CancellationTokenSource? _commitCts;

    /// <summary>确定：后台烘焙+编码 GIF（HUD 进度，Esc 可取消）→ 剪贴板（文件+位图+自定义格式）。</summary>
    void Commit()
    {
        int idx = Math.Clamp(_index, 0, _seq.Frames.Count - 1);
        bool strip = _settings.Current.AgentStripEnabled;
        StartEncode((seq, gif) =>
        {
            Trace.Log($"gif encoded bytes={gif.Length} frames={seq.Frames.Count}");
            ClipboardService.SetCapture(seq, gif, idx);
            if (strip) TryGenerateAgentStrip(seq);
            Close();
        });
    }

    /// <summary>时间轴范围+标注的后台烘焙与 GIF 编码（HUD 进度，编码中 Esc 取消）；完成后回 UI 线程执行 onDone。</summary>
    void StartEncode(Action<CapturedSequence, byte[]> onDone)
    {
        if (_encoding) return;
        try
        {
            var (a, b) = TrimRange;
            var subset = _seq.Frames.GetRange(a, b - a + 1);
            var anns = new List<FrameCapture.Drawing.Annotation>(_anns.Items);
            var region = _seq.Region; var monBounds = _seq.MonitorBounds; var monDev = _seq.MonitorDevice;
            int fps = Math.Clamp(_settings.Current.Fps, 5, 60);

            _encoding = true;
            _commitCts = new CancellationTokenSource();
            var ct = _commitCts.Token;
            _hud.Text = "导出 GIF…";
            System.Threading.Tasks.Task.Run(() =>
            {
                var baked = FrameCapture.Drawing.Bake.Apply(subset, anns);
                var seq = new CapturedSequence
                {
                    Frames = baked,
                    Region = region,
                    MonitorBounds = monBounds,
                    MonitorDevice = monDev,
                };
                var prog = new Progress<int>(d => Dispatcher.BeginInvoke(() => _hud.Text = $"导出 GIF… {d}/{baked.Count}"));
                byte[] gif = GifEncoder.Encode(baked, ComputeDelays(baked, fps), true, prog, ct);
                return (seq, gif);
            }, ct).ContinueWith(t =>
            {
                _encoding = false;
                if (t.IsCanceled || (t.Exception?.InnerException is OperationCanceledException))
                {
                    Trace.Log("encode cancelled");
                    Show(_index); // 恢复 HUD
                    return;
                }
                if (t.IsFaulted)
                {
                    Trace.Log($"encode failed: {t.Exception}");
                    _hud.Text = "导出失败";
                    return;
                }
                onDone(t.Result.seq, t.Result.gif);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch (Exception ex)
        {
            _encoding = false;
            Trace.Log($"encode start failed: {ex}");
        }
    }

    /// <summary>时间轴范围 + 标注烘焙后的导出帧序列。</summary>
    CapturedSequence ExportSequence()
    {
        var (a, b) = TrimRange;
        var subset = _seq.Frames.GetRange(a, b - a + 1);
        var baked = FrameCapture.Drawing.Bake.Apply(subset, _anns.Items);
        return new CapturedSequence
        {
            Frames = baked,
            Region = _seq.Region,
            MonitorBounds = _seq.MonitorBounds,
            MonitorDevice = _seq.MonitorDevice,
        };
    }

    static List<int> ComputeDelays(IReadOnlyList<CapturedFrame> frames, int fps)
    {
        int defaultDelay = 1000 / fps;
        var delays = new List<int>(frames.Count);
        double total = frames.Count > 0 ? frames[^1].TimeMs : 0;
        for (int i = 0; i < frames.Count; i++)
        {
            double t0 = frames[i].TimeMs;
            double t1 = i + 1 < frames.Count ? frames[i + 1].TimeMs : total;
            int d = (int)(t1 - t0);
            // 环形缓冲丢帧会造成巨大时间洞；洞两侧用默认间隔，避免播放停顿
            delays.Add(d > 0 && d < 250 ? Math.Clamp(d, 10, 200) : defaultDelay);
        }
        return delays;
    }


    void TryGenerateAgentStrip(CapturedSequence export)
    {
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "FrameCapture", "agent");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"strip-{DateTime.Now:HHmmss}.png");
            AgentStrip.Generate(export, _settings.Current.AgentStripFrames, path);
        }
        catch (Exception ex) { Trace.Log("agent strip: " + ex.Message); }
    }

    void SaveFrame()
    {
        var dlg = NewSaveDialog("PNG 图片|*.png", $"frame-{_seq.Frames[_index].TimeMs / 1000.0:0.0}.png");
        if (dlg.ShowDialog() != true) return;
        BgraImage.SavePng(_seq.Frames[_index], EnsureExt(dlg.FileName, ".png"));
    }

    void SaveGif()
    {
        if (_encoding) return;
        var dlg = NewSaveDialog("GIF 动图|*.gif", "capture.gif");
        if (dlg.ShowDialog() != true) return;
        string path = EnsureExt(dlg.FileName, ".gif");
        StartEncode((_, gif) =>
        {
            try { File.WriteAllBytes(path, gif); Trace.Log($"gif saved {path} {gif.Length}B"); }
            catch (Exception ex) { Trace.Log($"gif save failed: {ex.Message}"); _hud.Text = "保存失败"; }
            Show(_index);
        });
    }

    void SaveAfterimage()
    {
        var dlg = NewSaveDialog("PNG 图片|*.png", "afterimage.png");
        if (dlg.ShowDialog() != true) return;
        var combined = BgraImage.Afterimage(_seq.Frames, 5);
        BgraImage.SavePng(combined, EnsureExt(dlg.FileName, ".png"));
    }

    static string EnsureExt(string path, string ext)
        => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? path : path + ext;

    static Microsoft.Win32.SaveFileDialog NewSaveDialog(string filter, string defaultName)
    {
        // 提权启动时当前目录可能是 System32：永远默认到用户目录
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string dir = System.IO.Path.Combine(pictures, "FrameCapture");
        try { Directory.CreateDirectory(dir); } catch { dir = pictures; }
        return new() { Filter = filter, FileName = defaultName, InitialDirectory = dir };
    }

    /// <summary>控制器主动关闭。</summary>
    public void CloseByController()
    {
        if (_userClosed) return;
        _userClosed = true;
        ClosedByUser?.Invoke();
        Close();
    }
}
