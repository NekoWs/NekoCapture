using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private Border? _playPanel, _toolPanel;

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
        FrameCapture.Core.Fluent.BorderlessNative(this); // 真无框：消 DWM 非客户区白条，原生圆角+阴影
        FrameCapture.Core.Fluent.FadeIn(this, FrameCapture.Core.Fluent.Motion.EditorEnterMs, 14, offscreenFirstFrame: true);
        Background = new SolidColorBrush(DarkBg);
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };

        var mon = Monitors.ByDevice(seq.MonitorDevice) ?? Monitors.Nearest(seq.Region.X, seq.Region.Y);
        Width = Math.Min(1600, Math.Max(560, seq.Region.W + 120));
        Height = Math.Min(1000, Math.Max(420, seq.Region.H + (seq.Frames.Count > 1 ? 250 : 210))); // 停靠面板占位：播放+工具 / 仅工具
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (mon != null)
        {
            Left = mon.Bounds.X + mon.Bounds.W / 2.0 - Width / 2;
            Top = mon.Bounds.Y + mon.Bounds.H / 2.0 - Height / 2;
        }

        _view = new Image { Stretch = Stretch.Fill }; // 尺寸/偏移由 ApplyImageView 按 _imgRect 驱动（缩放/平移）
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
        _playTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000.0 / Math.Clamp(_settings.Current.Fps, AppSettings.FpsMin, AppSettings.FpsMax), 20));

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

    Button IconBtn(string glyph, string tip, double size = 28) =>
        FrameCapture.Core.Fluent.IconButton(glyph, tip, size, 13);

    void BuildLayout()
    {
        // 停靠式布局：预览在上，播放面板（录制时常驻）与工具面板依次停靠其下——控件不再悬浮在画面里
        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _viewGrid.Children.Add(_view);
        _viewGrid.Children.Add(_canvas);
        var viewHost = new Border
        {
            CornerRadius = new CornerRadius(8, 8, 0, 0), // 上角与 Win11 原生窗口圆角贴合；下角由工具面板收口
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            Child = _viewGrid,
        };
        Grid.SetRow(viewHost, 0);
        root.Children.Add(viewHost);
        Content = root;

        BuildPanels(root);
    }

    Border Panel(FrameworkElement child) => new()
    {
        Background = new SolidColorBrush(Theme.PanelBg),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
        BorderThickness = new Thickness(0, 1, 0, 0),
        Padding = new Thickness(8, 2, 8, 2),
        Child = child,
    };

    void Editor_OnTheme()
    {
        var bg = Background as System.Windows.Media.SolidColorBrush;
        if (bg != null)
        {
            var anim = new System.Windows.Media.Animation.ColorAnimation(Theme.WindowBg, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ThemeMs));
            var holder = new System.Windows.Media.SolidColorBrush(bg.Color);
            Background = holder;
            holder.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, anim);
        }
        _hud.Foreground = new System.Windows.Media.SolidColorBrush(Theme.Text);
        // 选中语言随行：工具/线宽的前景色随主题（选中项反白、未选中项 Theme.Text），否则换肤后旧色不可读
        for (int i = 0; i < _toolBtns.Count; i++)
            Highlight(_toolBtns[i], ToolOrder[i] == _tool);
        RefreshWidths();
        foreach (var p in new[] { _playPanel, _toolPanel })
            if (p != null)
            {
                p.Background = new System.Windows.Media.SolidColorBrush(Theme.PanelBg);
                p.BorderBrush = new System.Windows.Media.SolidColorBrush(Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B));
            }
        DrawRange();
    }

    bool MultiFrame => _seq.Frames.Count > 1;

    void BuildPanels(Grid root)
    {
        if (MultiFrame)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            int fps = (int)Math.Clamp(_settings.Current.Fps, AppSettings.FpsMin, AppSettings.FpsMax);
            var bPrevSec = IconBtn("\uF8AC", "上一秒 (Shift+←)"); bPrevSec.Click += (_, _) => Step(-fps);
            var bPrev = IconBtn("\uF08D", "上一帧 (←)"); bPrev.Click += (_, _) => Step(-1);
            _bPlay = IconBtn("\uF5B0", "播放/暂停 (空格)"); _bPlay.Click += (_, _) => TogglePlay();
            var bNext = IconBtn("\uF08F", "下一帧 (→)"); bNext.Click += (_, _) => Step(+1);
            var bNextSec = IconBtn("\uF8AD", "下一秒 (Shift+→)"); bNextSec.Click += (_, _) => Step(+fps);
            foreach (var b in new FrameworkElement[] { bPrevSec, bPrev, _bPlay, bNext, bNextSec }) transport.Children.Add(b);
            Grid.SetColumn(transport, 0);
            g.Children.Add(transport);

            if (_rangeHost != null)
            {
                _rangeHost.Background = Brushes.Transparent;
                _rangeHost.VerticalAlignment = VerticalAlignment.Stretch;
                _rangeHost.Margin = new Thickness(8, 0, 8, 0);
                Grid.SetColumn(_rangeHost, 1);
                g.Children.Add(_rangeHost);
            }
            _hud.Margin = new Thickness(6, 0, 8, 0);
            Grid.SetColumn(_hud, 2);
            g.Children.Add(_hud);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var bSaveFrame = IconBtn("\uE105", "保存当前帧 (PNG)  Ctrl+S"); bSaveFrame.Click += (_, _) => SaveFrame();
            var bCopyFrame = IconBtn("\uF413", "复制当前帧 (位图)  Ctrl+C"); bCopyFrame.Click += (_, _) => ClipboardService.SetFrame(ExportFrame(_index));
            var bSaveGif = IconBtn("\uE116", "保存为动图 (GIF)  Ctrl+E"); bSaveGif.Click += (_, _) => SaveGif();
            var bAfter = IconBtn("\uE955", "保存视觉暂留合成图  Ctrl+H"); bAfter.Click += (_, _) => SaveAfterimage();
            foreach (var b in new[] { bSaveFrame, bCopyFrame, bSaveGif, bAfter }) actions.Children.Add(b);
            actions.Children.Add(Sep());
            var bCancel = IconBtn("\uE711", "取消 (Esc)"); bCancel.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
            var bOk = IconBtn("\uF78C", "确定：GIF 写入剪贴板 (Enter)"); bOk.Click += (_, _) => Commit();
            actions.Children.Add(bCancel);
            actions.Children.Add(bOk);
            FrameCapture.Core.Fluent.HighlightBtn(bOk, true);
            _okBtns.Add(bOk); // 主按钮：accent 实心
            Grid.SetColumn(actions, 3);
            g.Children.Add(actions);

            _playPanel = Panel(g);
            Grid.SetRow(_playPanel, 1);
            root.Children.Add(_playPanel);
        }

        var tools = BuildToolsBar();
        if (!MultiFrame)
        {
            tools.Children.Add(Sep());
            var bSaveF = IconBtn("\uE105", "保存当前帧 (PNG)"); bSaveF.Click += (_, _) => SaveFrame();
            var bCopyF = IconBtn("\uF413", "复制当前帧 (位图)"); bCopyF.Click += (_, _) => ClipboardService.SetFrame(ExportFrame(_index));
            var bOkF = IconBtn("\uF78C", "确定：写入剪贴板 (Enter)"); bOkF.Click += (_, _) => Commit();
            var bX = IconBtn("\uE711", "取消 (Esc)"); bX.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
            foreach (var b in new[] { bSaveF, bCopyF, bOkF, bX }) tools.Children.Add(b);
            FrameCapture.Core.Fluent.HighlightBtn(bOkF, true);
            _okBtns.Add(bOkF); // 主按钮：accent 实心
        }
        tools.Children.Add(Sep());
        var bSet = IconBtn("\uE713", "设置"); bSet.Click += (_, _) => SettingsWindow.ShowOrFocus();
        var bHelp = IconBtn("\uE9CE", "帮助与快捷键"); bHelp.Click += (_, _) => HelpWindow.ShowOrFocus();
        tools.Children.Add(bSet);
        tools.Children.Add(bHelp);
        _toolPanel = Panel(tools);
        _toolPanel.CornerRadius = new CornerRadius(0, 0, 8, 8); // 底角收口，与原生窗口圆角贴合
        Grid.SetRow(_toolPanel, 2);
        root.Children.Add(_toolPanel);
    }

    static System.Windows.Shapes.Rectangle Sep() => new()
    {
        Width = 1,
        Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
        Margin = new Thickness(5, 5, 5, 5),
    };

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
        bar.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)), Margin = new Thickness(8, 6, 8, 6) });
        for (int i = 0; i < Swatches.Length; i++)
        {
            int idx = i;
            var inner = new System.Windows.Shapes.Ellipse
            {
                Width = 14, Height = 14, Fill = new SolidColorBrush(Swatches[idx]),
                Stroke = new SolidColorBrush(Theme.Stroke), StrokeThickness = 1, // 白色色块在浅底也可见
            };
            var bd = new Border
            {
                Child = inner,
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(11),
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
        // 自定义颜色：调色板入口（ColorDialog）+ 最近自定义色直接点选
        var pal = IconBtn("\uE790", "自定义颜色… (K)");
        pal.PreviewMouseLeftButtonDown += (_, e2) => { PickCustomColor(); e2.Handled = true; };
        pal.PreviewKeyDown += (_, ek) => { if (ek.Key is Key.Enter or Key.Space) { PickCustomColor(); ek.Handled = true; } };
        _paletteBtn = pal;
        bar.Children.Add(pal);
        _recentHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(_recentHost);
        RebuildRecentSwatches();
        bar.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)), Margin = new Thickness(8, 6, 8, 6) });
        foreach (var (w, lbl) in new[] { (0, "细"), (1, "中"), (2, "粗") }.Select((t, i) => (t.Item1, t.Item2)))
        {
            int idx = w;
            var b = IconBtn("", lbl + "笔画", 24);
            b.FontSize = 8 + idx * 3;
            b.PreviewMouseLeftButtonDown += (sender2, e2) => { _widthIdx = idx; RefreshWidths(); e2.Handled = true; };
            _widthBtns.Add(b);
            bar.Children.Add(b);
        }
        // 自定义线宽：⋯ 按钮 = 第 4 个宽度槽位，点开滑杆弹层（1..24px）
        var wMore = IconBtn("\uE712", "自定义线宽… (W)", 24);
        wMore.PreviewMouseLeftButtonDown += (_, e2) => { ShowWidthPopup(wMore); e2.Handled = true; };
        wMore.PreviewKeyDown += (_, ek) => { if (ek.Key is Key.Enter or Key.Space) { ShowWidthPopup(wMore); ek.Handled = true; } };
        _widthMoreBtn = wMore;
        _widthBtns.Add(wMore);
        bar.Children.Add(wMore);
        bar.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)), Margin = new Thickness(8, 6, 8, 6) });
        var undo = IconBtn("", "撤销 (Ctrl+Z)");
        undo.Click += (_, _) => UndoAnn();
        _undoBtn = undo;
        bar.Children.Add(undo);
        var redo = IconBtn("", "重做 (Ctrl+Y)");
        redo.Click += (_, _) => RedoAnn();
        _redoBtn = redo;
        bar.Children.Add(redo);
        RefreshUndo();
        RefreshWidths(); // 默认宽度"中"的选中态当场可见（此前要等点击才出现）
        return bar;
    }

    Button? _undoBtn;
    Button? _redoBtn;
    void RefreshUndo()
    {
        // 双流合并能力：标注或裁剪任一可撤销/重做，按钮即亮（次序由 ActionOrder 裁决）
        SetGhost(_undoBtn, _anns.CanUndo || _order.CanUndoCrop);
        SetGhost(_redoBtn, _anns.CanRedo || _order.CanRedoCrop);
    }
    static void SetGhost(Button? b, bool on)
    {
        if (b == null || b.IsEnabled == on) return;
        b.IsEnabled = on;
        b.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(on ? 1.0 : 0.35,
            new System.Windows.Duration(TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.HoverMs)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }

    void RefreshSwatches()
    {
        for (int i = 0; i < _swatchBtns.Count; i++)
            _swatchBtns[i].BorderBrush = i == _colorIdx ? new SolidColorBrush(Acc) : Brushes.Transparent;
        // 自定义槽位选中 = 调色板反白（与宽度/工具同一选中语言）
        if (_paletteBtn != null) FrameCapture.Core.Fluent.HighlightBtn(_paletteBtn, _colorIdx == Swatches.Length);
    }

    void RefreshWidths()
    {
        // 与绘制工具同一选中语言：accent 实心 + 反白图标（初值在 BuildToolsBar 尾部即刷新，默认宽度可见高亮）
        for (int i = 0; i < _widthBtns.Count; i++)
            FrameCapture.Core.Fluent.HighlightBtn(_widthBtns[i], i == _widthIdx);
    }

    // ---------------- 自定义颜色 / 线宽 ----------------

    Button? _paletteBtn;
    Button? _widthMoreBtn;
    StackPanel? _recentHost;
    Popup? _widthPop;
    Slider? _widthSlider;
    System.Windows.Shapes.Ellipse? _widthPreview;
    TextBlock? _widthVal;

    List<string> RecentColors => _settings.Current.CustomColors ?? new List<string>();

    void RebuildRecentSwatches()
    {
        if (_recentHost == null) return;
        _recentHost.Children.Clear();
        foreach (var hex in RecentColors.Take(4))
        {
            Color col;
            try { col = (Color)ColorConverter.ConvertFromString(hex); } catch { continue; }
            var inner = new System.Windows.Shapes.Ellipse
            {
                Width = 12, Height = 12, Fill = new SolidColorBrush(col),
                Stroke = new SolidColorBrush(Theme.Stroke), StrokeThickness = 1,
            };
            var bd = new Border
            {
                Child = inner, Width = 20, Height = 20, Margin = new Thickness(2, 0, 0, 0),
                CornerRadius = new CornerRadius(10), Background = Brushes.Transparent,
                Cursor = Cursors.Hand, ToolTip = $"最近用色 {hex}",
            };
            FrameCapture.Core.Fluent.ChipHover(bd);
            string h = hex;
            bd.MouseLeftButtonDown += (sender2, e2) =>
            {
                _customColor = col; _colorIdx = Swatches.Length; RefreshSwatches(); e2.Handled = true;
            };
            _recentHost.Children.Add(bd);
        }
    }

    /// <summary>把 WPF 窗口句柄递给 WinForms 对话框做 owner。</summary>
    sealed class Win32Owner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32Owner(IntPtr h) => Handle = h;
    }

    void PickCustomColor()
    {        var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
        var cur = _customColor ?? CurColor;
        dlg.Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B);
        // owner=编辑器：模态、居中于编辑器、始终在其之上（否则可能弹到别的屏幕/窗后）
        var got0 = dlg.ShowDialog(new Win32Owner(new System.Windows.Interop.WindowInteropHelper(this).Handle));
        if (got0 != System.Windows.Forms.DialogResult.OK) return;
        var got = dlg.Color;
        _customColor = Color.FromRgb(got.R, got.G, got.B);
        _colorIdx = Swatches.Length;
        _settings.Mutate(s => s.CustomColors = FrameCapture.Core.CustomPick.RememberColor(s.CustomColors, $"#{got.R:X2}{got.G:X2}{got.B:X2}"));
        RebuildRecentSwatches();
        RefreshSwatches();
    }

    void ShowWidthPopup(Button anchor)
    {
        if (_widthPop == null)
        {
            _widthSlider = new Slider { Minimum = 1, Maximum = 24, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 150 };
            FrameCapture.Core.Fluent.StyleSlider(_widthSlider);
            _widthVal = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _widthPreview = new System.Windows.Shapes.Ellipse { Fill = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            _widthSlider.ValueChanged += (_, e) =>
            {
                _customWidth = (float)e.NewValue;
                _widthIdx = Widths.Length; // 自定义槽位
                if (_widthVal != null) _widthVal.Text = $"{(int)e.NewValue}px";
                if (_widthPreview != null) { _widthPreview.Width = _widthPreview.Height = Math.Clamp(e.NewValue, 3, 20); }
                RefreshWidths();
            };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Children = { _widthSlider, _widthVal, _widthPreview } };
            _widthPop = new Popup
            {
                Child = new Border
                {
                    Background = new SolidColorBrush(Theme.PanelBg),
                    BorderBrush = new SolidColorBrush(Theme.Stroke),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 9, 12, 9),
                    Child = head,
                    // Popup 从 PlacementTarget（图标按钮）继承 FontFamily=图标字体 → 数字会变豆腐，_widthVal 显式复位
                },
                PlacementTarget = anchor,
                Placement = PlacementMode.Top,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
            };
        }
        _widthSlider.Value = Math.Clamp(_customWidth, 1, 24);
        if (_widthVal != null) _widthVal.Text = $"{(int)_widthSlider.Value}px";
        if (_widthPreview != null) _widthPreview.Width = _widthPreview.Height = Math.Clamp(_widthSlider.Value, 3, 20);
        _widthPop.IsOpen = true;
    }

    // ---------------- 行为 ----------------

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        Trace.Log($"editor key={e.KeyboardDevice.Modifiers}+{e.Key}");
        int fps = (int)Math.Clamp(_settings.Current.Fps, AppSettings.FpsMin, AppSettings.FpsMax);
        var mods = e.KeyboardDevice.Modifiers;
        if (mods == ModifierKeys.Control && (e.Key == Key.Z || e.Key == Key.U)) { UndoAnn(); return; }
        if (mods == ModifierKeys.Control && e.Key == Key.Y) { RedoAnn(); return; }
        // 底部工具快捷键
        if (mods == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.S: SaveFrame(); return;
                case Key.C: ClipboardService.SetFrame(ExportFrame(_index)); return;
                case Key.E: SaveGif(); return;
                case Key.H: SaveAfterimage(); return;
                // 视图缩放（浏览器惯例）：Ctrl +/− 以中心放大缩小，Ctrl+0 适配，Ctrl+1 逐像素 1:1
                case Key.OemPlus or Key.Add: ZoomStep(1.25); return;
                case Key.OemMinus or Key.Subtract: ZoomStep(1 / 1.25); return;
                case Key.D0 or Key.NumPad0: ZoomFit(); return;
                case Key.D1 or Key.NumPad1: ZoomOneToOne(); return;
            }
        }
        if (mods == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.X: SetTool(Tool.Browse); return;
                case Key.L: SetTool(Tool.Line); return;
                case Key.O: SetTool(Tool.Ellipse); return;
                case Key.A: SetTool(Tool.Arrow); return;
                case Key.P: SetTool(Tool.Freehand); return;
                case Key.C: SetTool(Tool.Crop); return;
                case Key.Delete: UndoAnn(); return;
                case Key.K: PickCustomColor(); return;
                case Key.W: if (_widthMoreBtn != null) ShowWidthPopup(_widthMoreBtn); return;
            }
        }
        switch (e.Key)
        {
            case Key.Escape:
                // 宽度弹层打开时：Esc 只收弹层（分层语义的第一层）
                if (_widthPop is { IsOpen: true }) { _widthPop.IsOpen = false; e.Handled = true; return; }
                // 分层语义（T82）：导出中=取消；绘制态=先退出工具，误触 Esc 绝不丢整次捕获；浏览态=关闭
                switch (FrameCapture.Core.EscStep.Select(_encoding, _tool != Tool.Browse))
                {
                    case FrameCapture.Core.EscAction.CancelEncode: _commitCts?.Cancel(); break;
                    case FrameCapture.Core.EscAction.ExitTool:
                        CancelPendingStroke(); SetTool(Tool.Browse);
                        FrameCapture.Core.Trace.Log("editor esc→browse");
                        e.Handled = true;
                        break;
                    default: FrameCapture.Core.Fluent.CloseWithFade(this); break;
                }
                break;
            case Key.Enter: Commit(); break;
            case Key.Space: TogglePlay(); e.Handled = true; break;
            case Key.Left when e.KeyboardDevice.Modifiers == ModifierKeys.Shift: Step(-fps); break;
            case Key.Right when e.KeyboardDevice.Modifiers == ModifierKeys.Shift: Step(+fps); break;
            case Key.Left: Step(-1); break;
            case Key.Right: Step(+1); break;
            case Key.Y: RedoAnn(); e.Handled = true; break;
            // 缩放裸键（与 Ctrl 惯例并存）：PageUp/Down 放大缩小，Home 适配，End 逐像素 1:1
            case Key.PageUp: ZoomStep(1.25); e.Handled = true; break;
            case Key.PageDown: ZoomStep(1 / 1.25); e.Handled = true; break;
            case Key.Home: ZoomFit(); e.Handled = true; break;
            case Key.End: ZoomOneToOne(); e.Handled = true; break;
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
        if (_bPlay != null) _bPlay.Content = _playing ? "" : "";
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
            Math.Max(1000.0 / Math.Clamp(_settings.Current.Fps, AppSettings.FpsMin, AppSettings.FpsMax), 20) / _speed);
        Show(_index);
    }

    void Show(int index)
    {
        _index = Math.Clamp(index, 0, _seq.Frames.Count - 1);
        _view.Source = _bitmaps[_index];
        double tMs = _seq.Frames[_index].TimeMs;
        double eff = _imgRect.Width / Math.Max(1, _seq.Frames[_index].W);
        string zoomTxt = _zoom <= 1.001 ? "" : (Math.Abs(eff - 1.0) < 0.02 ? "  1:1" : $"  {eff * 100:0}%");
        string cropTxt = "";
        if (_crop.IsCropped)
        {
            var (cqW, cqH) = CropPx(_seq.Frames[_index].W, _seq.Frames[_index].H);
            cropTxt = $"  裁剪 {cqW:0}×{cqH:0}";
        }
        _hud.Text = $"{_index + 1}/{_seq.Frames.Count}  {tMs / 1000.0:0.00}s"
            + (_speed != 1.0 ? $"  ×{_speed:0.###}" : "")
            + zoomTxt + cropTxt
            + (_seq.DroppedFrames > 0 ? $"  ⚠内存预算已丢 {_seq.DroppedFrames} 帧" : "");
        DrawRange();
    }

    readonly List<Button> _okBtns = new();
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
            FrameCapture.Core.Fluent.CloseWithFade(this);
        });
    }

    /// <summary>时间轴范围+标注的后台烘焙与 GIF 编码（HUD 进度，编码中 Esc 取消）；完成后回 UI 线程执行 onDone。</summary>
    void SetOkBusy(bool busy)
    {
        foreach (var b in _okBtns)
        {
            b.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(busy ? 0.55 : 1.0,
                new System.Windows.Duration(TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.HoverMs)))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        }
    }

    void StartEncode(Action<CapturedSequence, byte[]> onDone)
    {
        if (_encoding) return;
        try
        {
            var (a, b) = TrimRange;
            var subset = _seq.Frames.GetRange(a, b - a + 1);
            var anns = new List<FrameCapture.Drawing.Annotation>(_anns.Items);
            var region = _seq.Region; var monBounds = _seq.MonitorBounds; var monDev = _seq.MonitorDevice;
            int fps = Math.Clamp(_settings.Current.Fps, AppSettings.FpsMin, AppSettings.FpsMax);
            var cropSnap = _crop.Current; bool cropOn = _crop.IsCropped; // UI 线程快照，后台只读

            _encoding = true;
            SetOkBusy(true);
            _commitCts = new CancellationTokenSource();
            var ct = _commitCts.Token;
            _hud.Text = "导出 GIF…";
            System.Threading.Tasks.Task.Run(() =>
            {
                // 测试专用（与 FC_TEST_NO_FG 同约定）：FC_TEST_ENCODE_FAIL=1 令编码任务必然抛错，
                // 使"导出失败"HUD 终态可确定性触达并做双主题像素取证（UIAUDIT D9）。正常用户环境永不置位。
                if (System.Environment.GetEnvironmentVariable("FC_TEST_ENCODE_FAIL") == "1")
                    throw new System.InvalidOperationException("forced encode failure (FC_TEST_ENCODE_FAIL=1)");
                // 裁剪=视图状态：导出瞬间一次性折算（T78c 像素级等价：先裁剪后烘焙 ≡ 先烘焙后裁剪）
                var frames = subset; var outAnns = anns; var outRegion = region;
                if (cropOn && frames.Count > 0)
                {
                    int fw = frames[0].W, fh = frames[0].H;
                    var (cx, cy, cw, ch) = FrameCapture.Core.CropState.ToPixelOf(cropSnap, fw, fh);
                    frames = FrameCapture.Drawing.Bake.Crop(frames, cx, cy, cw, ch);
                    outAnns = FrameCapture.Core.CropRemap.Remap(anns, (double)cx / fw, (double)cy / fh, (double)cw / fw, (double)ch / fh);
                    outRegion = new RectI(region.X + cx, region.Y + cy, cw, ch);
                }
                var baked = FrameCapture.Drawing.Bake.Apply(frames, outAnns);
                var seq = new CapturedSequence
                {
                    Frames = baked,
                    Region = outRegion,
                    MonitorBounds = monBounds,
                    MonitorDevice = monDev,
                };
                var prog = new Progress<int>(d => Dispatcher.BeginInvoke(() => _hud.Text = $"导出 GIF… {d}/{baked.Count}"));
                byte[] gif = GifEncoder.Encode(baked, ComputeDelays(baked, fps), true, prog, ct);
                return (seq, gif);
            }, ct).ContinueWith(t =>
            {
                _encoding = false;
                SetOkBusy(false);
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
        BgraImage.SavePng(ExportFrame(_index), EnsureExt(dlg.FileName, ".png"));
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
        var combined = BgraImage.Afterimage(CropFramesForExport(_seq.Frames), 5);
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
        FrameCapture.Core.Fluent.CloseWithFade(this);
    }
}
