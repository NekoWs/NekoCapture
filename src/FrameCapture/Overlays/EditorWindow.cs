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
    private readonly int _fps;   // 本序列实际帧率：优先 seq.Fps（跟随刷新率录的实际值），回退设置手动值——时间轴/时长/播放都用它（R119）
    // R61：位图惰性缓存（LRU）——长录制帧数可达数万，全量转 BitmapSource 必 OOM。
    // 只缓存最近浏览的若干帧；缓存上限按帧字节数自适应（约 600MB 帧数据）。
    private readonly Dictionary<int, BitmapSource> _bmpCache = new();
    private readonly Queue<int> _bmpFifo = new();
    private int _bmpCacheMax = 24;
    // 帧元数据缓存：spool 惰性列表读一帧=整块 BGRA 回盘，HUD/时间轴只要时间戳与尺寸，
    // 故构造时一次性把元数据抽进平行数组（spool 走内存索引，零回盘）。
    private double[] _times = System.Array.Empty<double>();
    private int _frameW, _frameH;
    private readonly Image _view;
    private readonly System.Windows.Threading.DispatcherTimer _playTimer;
    private readonly TextBlock _hud;
    // R69：预览底部轻提示（snackbar），单帧/多帧通用——OCR/复制等即时反馈用
    private Border? _snack;
    private TextBlock? _snackText;
    private System.Windows.Threading.DispatcherTimer? _snackTimer;

    private int _index;
    private bool _playing;
    private bool _userClosed;
    private bool _ocrBusy; // R69：识别进行中，防重入

    private static Color DarkBg => Theme.WindowBg;
    private static Color PanelBg => Theme.PanelBg;
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
    private Button? _bPlay;
    private Border? _playPanel, _toolPanel;
    private StackPanel? _toolsBar; // R93：底部工具条，用于让 MinWidth 跟随其实际宽度（防窄图裁切两端工具）

    public EditorWindow(SettingsService settings, CapturedSequence seq)
    {
        _settings = settings;
        _seq = seq;
        _fps = _seq.Fps > 0 ? Math.Clamp(_seq.Fps, AppSettings.FpsMin, AppSettings.FpsMax)
                            : Math.Clamp(_settings.Current.Fps, AppSettings.FpsMin, AppSettings.FpsMax);
        // 元数据抽入平行数组 + 位图缓存上限按帧字节自适应（见字段注释）
        int n = seq.Frames.Count;
        _times = new double[n];
        if (seq.Frames is FrameSpoolList sl)
        {
            for (int i = 0; i < n; i++) _times[i] = sl.Meta(i).TimeMs;
            if (n > 0) { var e0 = sl.Meta(0); _frameW = e0.W; _frameH = e0.H; }
        }
        else
        {
            for (int i = 0; i < n; i++) _times[i] = seq.Frames[i].TimeMs;
            if (n > 0) { _frameW = seq.Frames[0].W; _frameH = seq.Frames[0].H; }
        }
        long frameBytes = (long)_frameW * _frameH * 4;
        _bmpCacheMax = frameBytes > 0 ? (int)Math.Clamp((600L << 20) / frameBytes, 6, 96) : 24;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        // 总在最前=图钉开关（R44，默认关）。开启时失活即重插置顶带（与旧行为一致），
        // 关闭时是普通窗口，绝不能被 Deactivated 兜底重新钉上去。
        Topmost = _settings.Current.EditorTopmost;
        Deactivated += (_, _) => { if (IsLoaded && Topmost) { Topmost = false; Topmost = true; } };
        Title = "FrameCapture Editor";
        FrameCapture.Core.Fluent.BorderlessNative(this); // 真无框：消 DWM 非客户区白条，原生圆角+阴影
        FrameCapture.Core.Fluent.FadeIn(this, FrameCapture.Core.Fluent.Motion.EditorEnterMs, 14, offscreenFirstFrame: true);
        Background = new SolidColorBrush(DarkBg);
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };

        var mon = Monitors.ByDevice(seq.MonitorDevice) ?? Monitors.Nearest(seq.Region.X, seq.Region.Y);
        // 812 DIP 是工具条早期列宽的下限（R34/R44）。R93 起 FitMinWidthToToolbar 会按工具条
        // 实测宽度动态抬高 MinWidth（工具/色板/最近用色/宽度/撤销重做/保存·复制·图钉·OCR·确定·取消/置顶·设置·帮助），
        // 这里只留一个不会更小的地板值。
        MinWidth = 812;
        Width = Math.Min(1600, Math.Max(812, seq.Region.W + 120));
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
            if (_userClosed || _seq.Frames.Count == 0) return; // 关闭中：spool 已释放，绝不再读帧（R61 磁盘 spool 后读已关文件会抛）
            var (a, b) = TrimRange;
            int next = _index + 1;
            if (next > b || next >= _seq.Frames.Count) next = a;
            Show(next);
        };
        _playTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000.0 / _fps, 20));

        InitDrawing();
        BuildLayout();
        RestoreEditorPrefs(); // R64：恢复上次的工具/颜色/粗细（关=保持默认）
        FitMinWidthToToolbar(); // R93：工具条列（含最近用色）实测宽 → MinWidth，窄图不再裁切两端工具
        if (_toolsBar != null) _toolsBar.SizeChanged += (_, _) => FitMinWidthToToolbar(); // 最近用色增减时跟随
        if (!_settings.Current.EditorGuideShown) ShowGuide(); // 首次打开：一次性上手引导（R45）
        Theme.Changed += Editor_OnTheme;
        Closed += (_, _) => Theme.Changed -= Editor_OnTheme;
        Closed += (_, _) => SaveEditorPrefs(); // R64：关闭时记住标注设置（✓/✕/Esc 三条路径都经此）
        Show(0);
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        PreviewKeyDown += OnKeyDown;
        Closed += (_, _) =>
        {
            if (_userClosed) return;
            _userClosed = true;
            _playTimer.Stop(); // 停播放表，避免释放 spool 后定时器再读已关文件
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

    // 文字按钮（非图标字体）：字号步进器 A−/A+ 用，走同一 IconButton 模板保持悬停/按压手感一致
    Button TextSizeBtn(string text, string tip)
    {
        var b = new Button
        {
            Content = text, ToolTip = tip, Width = 26, Height = 26, Margin = new Thickness(1),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Theme.Text), Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, Template = FrameCapture.Core.Fluent.IconButtonTemplate(),
            Focusable = false,
        };
        return b;
    }

    // R81：钉到屏幕按钮改用实心图钉 E840（招牌动作配招牌图钉，Snipaste/QQ 惯例）；「总在最前」改置顶箭头 E898 以免两图钉混淆
    Button PinBtn()
    {
        var b = IconBtn("\uE840", "钉到屏幕 (Ctrl+P)");
        b.Click += (_, _) => PinCurrentFrame();
        return b;
    }

    // R69：识别文字按钮（E8C1「A字」= 文字识别，与文本工具/复制明显区分）
    Button OcrBtn()
    {
        var b = IconBtn("\uE8C1", "识别文字（复制到剪贴板）  Ctrl+O");
        b.Click += (_, _) => OcrCurrentFrame();
        return b;
    }

    void BuildLayout()
    {
        // 停靠式布局：预览在上，播放面板（录制时常驻）与工具面板依次停靠其下——控件不再悬浮在画面里
        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _viewGrid.Children.Add(_view);
        _viewGrid.Children.Add(_canvas);
        // R69：预览底部居中轻提示浮层（默认隐藏，ShowSnack 淡入淡出）
        _snackText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI"),
            TextAlignment = TextAlignment.Center,
        };
        _snack = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x1F, 0x1F, 0x1F)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 22),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _snackText,
        };
        _viewGrid.Children.Add(_snack);
        var viewHost = new Border
        {
            CornerRadius = new CornerRadius(8, 8, 0, 0), // 上角与 Win11 原生窗口圆角贴合；下角由工具面板收口
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            Child = _viewGrid,
        };
        Grid.SetRow(viewHost, 0);
        root.Children.Add(viewHost);
        Content = root;
        _root = root;

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
            int fps = _fps;
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
            // HUD 定底宽右对齐（R57）：帧数/时间/裁剪段文本变宽不再挤压时间轴列（用户实测：布局整体抖动）。
            // MinWidth 而非 Width：罕见的「⚠丢帧」长警告仍完整可读，只是那种情况下才允许变宽。
            _hud.MinWidth = 210;
            _hud.TextAlignment = TextAlignment.Right;
            _hud.Margin = new Thickness(6, 0, 8, 0);
            Grid.SetColumn(_hud, 2);
            g.Children.Add(_hud);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var bSaveFrame = IconBtn("\uE105", "保存当前帧 (PNG)  Ctrl+S"); bSaveFrame.Click += (_, _) => SaveFrame();
            var bCopyFrame = IconBtn("\uF413", "复制当前帧 (位图)  Ctrl+C"); bCopyFrame.Click += (_, _) => ClipboardService.SetFrame(ExportFrame(_index));
            var bPinFrame = PinBtn();
            var bOcrFrame = OcrBtn();
            var bSaveGif = IconBtn("\uE116", "导出录制 (MP4 / WebM / GIF)  Ctrl+E"); bSaveGif.Click += (_, _) => SaveRecording();
            var bAfter = IconBtn("\uE955", "保存视觉暂留合成图  Ctrl+H"); bAfter.Click += (_, _) => SaveAfterimage();
            foreach (var b in new[] { bSaveFrame, bCopyFrame, bPinFrame, bOcrFrame, bSaveGif, bAfter }) actions.Children.Add(b);
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
        _toolsBar = tools; // R93：留引用，供 FitMinWidthToToolbar 量宽
        if (!MultiFrame)
        {
            tools.Children.Add(Sep());
            var bSaveF = IconBtn("\uE105", "保存当前帧 (PNG)"); bSaveF.Click += (_, _) => SaveFrame();
            var bCopyF = IconBtn("\uF413", "复制当前帧 (位图)"); bCopyF.Click += (_, _) => ClipboardService.SetFrame(ExportFrame(_index));
            var bPinF = PinBtn();
            var bOcrF = OcrBtn();
            var bOkF = IconBtn("\uF78C", "确定：写入剪贴板 (Enter)"); bOkF.Click += (_, _) => Commit();
            var bX = IconBtn("\uE711", "取消 (Esc)"); bX.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
            foreach (var b in new[] { bSaveF, bCopyF, bPinF, bOcrF, bOkF, bX }) tools.Children.Add(b);
            FrameCapture.Core.Fluent.HighlightBtn(bOkF, true);
            _okBtns.Add(bOkF); // 主按钮：accent 实心
        }
        tools.Children.Add(Sep());
        // R81：编辑器总在最前（R44）。改用置顶箭头 E898（向上箭头带顶栏，GlyphProbe 已验证），开关态靠高亮底色区分，不再与「钉到屏幕」图钉混淆
        var bPin = IconBtn("\uE898", "总在最前");
        FrameCapture.Core.Fluent.HighlightBtn(bPin, _settings.Current.EditorTopmost);
        bPin.Click += (_, _) =>
        {
            bool on = !Topmost;
            Topmost = on;
            _settings.Mutate(s => s.EditorTopmost = on);
            FrameCapture.Core.Fluent.HighlightBtn(bPin, on);
            FrameCapture.Core.Trace.Log("editor topmost=" + on);
        };
        tools.Children.Add(bPin);
        var bSet = IconBtn("\uE713", "设置"); bSet.Click += (_, _) => SettingsWindow.ShowOrFocus();
        var bHelp = IconBtn("\uE9CE", "帮助与快捷键"); bHelp.Click += (_, _) => HelpWindow.ShowOrFocus();
        tools.Children.Add(bSet);
        tools.Children.Add(bHelp);
        _toolPanel = Panel(tools);
        _toolPanel.CornerRadius = new CornerRadius(0, 0, 8, 8); // 底角收口，与原生窗口圆角贴合
        Grid.SetRow(_toolPanel, 2);
        root.Children.Add(_toolPanel);
    }

    /// <summary>
    /// R93：让窗口 MinWidth 跟随底部工具条的实际宽度。工具条是居中 StackPanel，窗口比它窄时
    /// 两端工具会被裁掉（历史上 MinWidth 是写死的 812，但工具条随功能增长早已超过它）。
    /// 量工具条 DesiredSize（含最近用色，最多 4 枚）+ 面板内边距，夹到 [812, 工作区宽]。
    /// DesiredSize 是内容宽（StackPanel 不受容器约束），故窗口窄时也能量到真实需要。
    /// </summary>
    void FitMinWidthToToolbar()
    {
        if (_toolsBar == null) return;
        _toolsBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double need = Math.Ceiling(_toolsBar.DesiredSize.Width) + 20; // 面板左右内边距 8+8 + 余量
        double cap = SystemParameters.WorkArea.Width;                 // 不超过工作区，避免窗口比屏还宽
        double target = Math.Max(812, Math.Min(need, cap));
        if (Math.Abs(MinWidth - target) <= 0.5) return;               // 收敛：值没变就不再触发重排
        MinWidth = target;
        // 窄图时最小宽会超过当前宽：撑到最小宽并（仅未显示时）重新居中，避免窗口偏右或编辑中跳动
        if (Width < target)
        {
            double grow = target - Width;
            Width = target;
            if (!IsLoaded) Left -= grow / 2;
        }
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
            var b = IconBtn(FrameCapture.Core.ToolCatalog.Tools[i].Glyph, FrameCapture.Core.ToolCatalog.Tools[i].Tip);
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
                // 40% 文字色环：Theme.Stroke 在浅色主题太淡，白色色块会融进白工具条（R28 浅色截图抓到）
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, Theme.Text.R, Theme.Text.G, Theme.Text.B)), StrokeThickness = 1,
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
            bd.MouseLeftButtonDown += (sender2, e2) => { _colorIdx = idx; RefreshSwatches(); RestyleSelected(CurColor, null); e2.Handled = true; };
            _swatchBtns.Add(bd);
            bar.Children.Add(bd);
        }
        // 自定义颜色：调色板入口（ColorDialog）+ 最近自定义色直接点选
        var pal = IconBtn("\uE790", "RGBA 调色板 (K)");
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
            b.PreviewMouseLeftButtonDown += (sender2, e2) => { _widthIdx = idx; RefreshWidths(); RestyleSelected(null, CurWidth); e2.Handled = true; };
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
        // 字号步进器（C3）：仅文字工具/选中文字时可见。A−/A+ 调字号——选中文字=改该文字（可撤销），文字工具下=定下次文字字号。
        _fontGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _fontGroup.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)), Margin = new Thickness(8, 6, 8, 6) });
        var fMinus = TextSizeBtn("A\u2212", "字号减小"); fMinus.Click += (_, _) => StepFontSize(-1);
        var fPlus = TextSizeBtn("A+", "字号增大"); fPlus.Click += (_, _) => StepFontSize(+1);
        _fontLabel = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center, MinWidth = 36, TextAlignment = TextAlignment.Center };
        _fontGroup.Children.Add(fMinus);
        _fontGroup.Children.Add(_fontLabel);
        _fontGroup.Children.Add(fPlus);
        bar.Children.Add(_fontGroup);
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
        bool canU = _anns.CanUndo || _order.CanUndoCrop, canR = _anns.CanRedo || _order.CanRedoCrop;
        SetGhost(_undoBtn, canU);
        SetGhost(_redoBtn, canR);
        // 步数进 tooltip（R45）：撤销到哪一步、还能撤几步，悬停即知
        int u = _anns.Count + _order.CropUndoCount, r = _anns.RedoCount + _order.CropRedoCount;
        if (_undoBtn != null) _undoBtn.ToolTip = canU ? $"撤销上一笔（可撤 {u} 步）  Ctrl+Z" : "撤销 (Ctrl+Z)";
        if (_redoBtn != null) _redoBtn.ToolTip = canR ? $"重做上一笔（可重做 {r} 步）  Ctrl+Y" : "重做 (Ctrl+Y)";
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
        UpdateFontStepper();
    }

    // ---------------- 自定义颜色 / 线宽 ----------------

    Button? _paletteBtn;
    Button? _widthMoreBtn;
    StackPanel? _recentHost;
    StackPanel? _fontGroup;   // 字号步进器（C3）：文字工具/选中文字时可见
    TextBlock? _fontLabel;
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
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, Theme.Text.R, Theme.Text.G, Theme.Text.B)), StrokeThickness = 1,
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
                _customColor = col; _colorIdx = Swatches.Length; RefreshSwatches(); RestyleSelected(CurColor, null); e2.Handled = true;
            };
            _recentHost.Children.Add(bd);
        }
    }

    void PickCustomColor()
    {
        // R54：系统 ColorDialog 退役 → 自绘 RGBA 调色板弹层（EditorWindow.ColorPick.cs，主题一致+就地+即时生效）
        if (_paletteBtn != null) ShowColorPicker(_paletteBtn);
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
            // R76：滑杆拖动只改默认值（实时预览），关闭弹层时一次性把最终粗细应用到选中项（单条撤销）
            _widthPop.Closed += (_, _) => RestyleSelected(null, CurWidth);
        }
        _widthSlider.Value = Math.Clamp(_customWidth, 1, 24);
        if (_widthVal != null) _widthVal.Text = $"{(int)_widthSlider.Value}px";
        if (_widthPreview != null) _widthPreview.Width = _widthPreview.Height = Math.Clamp(_widthSlider.Value, 3, 20);
        OpenPopSticky(_widthPop, this);
    }

    // OpenPopSticky 已上收 Fluent（R59，设置窗调色板共用）
    static void OpenPopSticky(Popup pop, UIElement hostEl) => FrameCapture.Core.Fluent.OpenPopSticky(pop, hostEl);

    // ---------------- 首次上手引导（R45，一次性） ----------------

    Grid? _root;
    Border? _guide;

    void ShowGuide()
    {
        if (_root == null) return;
        var rows = new (string K, string V)[]
        {
            ("标注", "底部工具条选 线/矩形 R/圆 O/箭头/手绘/文字 T/马赛克 M，再点同一工具退出绘制；选中 V 可点选已画标注拖动/删除"),
            ("裁剪", "裁剪工具拖框（框外压暗+网格提示），松开即应用；Ctrl+Z 可撤销"),
            ("确定/取消", "Enter 写入剪贴板 · Esc 关闭 · 图钉=总在最前"),
            ("更多", "F1 完整帮助，任意按钮悬停有说明"),
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = "欢迎用 FrameCapture 编辑器",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            Margin = new Thickness(0, 0, 0, 10),
        });
        foreach (var (k, v) in rows)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var kt = new TextBlock { Text = k, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Acc) };
            var vt = new TextBlock
            {
                Text = v, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, Theme.Text.R, Theme.Text.G, Theme.Text.B)),
            };
            Grid.SetColumn(kt, 0); Grid.SetColumn(vt, 1);
            row.Children.Add(kt); row.Children.Add(vt);
            sp.Children.Add(row);
        }
        var ok = FrameCapture.Core.Fluent.IconButton("知道了", "开始使用", 84, 12.5);
        ok.ClearValue(System.Windows.Controls.Control.FontFamilyProperty); // 文字按钮：回到继承的 UI 字体（IconButton 默认是图标字体）
        ok.Height = 30;
        FrameCapture.Core.Fluent.HighlightBtn(ok, true);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Margin = new Thickness(0, 14, 0, 0);
        ok.Click += (_, _) => CloseGuide();
        sp.Children.Add(ok);

        var card = new Border
        {
            MaxWidth = 520,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Theme.PanelBg),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22, 18, 22, 18),
            Child = sp,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _guide = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x10, 0x10, 0x10)),
            Child = card,
        };
        _guide.MouseLeftButtonUp += (_, _) => CloseGuide(); // 点空白处也可关
        System.Windows.Controls.Panel.SetZIndex(_guide, 99);
        _root.Children.Add(_guide);
        _guide.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1,
            new System.Windows.Duration(TimeSpan.FromMilliseconds(220)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        FrameCapture.Core.Trace.Log("editor guide shown");
    }

    void CloseGuide()
    {
        if (_guide == null) return;
        var g = _guide;
        _guide = null;
        _settings.Mutate(s => s.EditorGuideShown = true);
        FrameCapture.Core.Trace.Log("editor guide closed");
        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
            new System.Windows.Duration(TimeSpan.FromMilliseconds(160)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
        fade.Completed += (_, _) => _root?.Children.Remove(g);
        g.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------- 行为 ----------------

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        Trace.Log($"editor key={e.KeyboardDevice.Modifiers}+{e.Key}");
        // 首次引导打开时吞掉所有键（Enter/Esc 只关引导，绝不落到"确定/放弃捕获"上）
        if (_guide != null)
        {
            if (e.Key is Key.Escape or Key.Enter or Key.Space) CloseGuide();
            e.Handled = true;
            return;
        }
        // 文字输入中：编辑快捷键全部让位（Enter 上屏 / Esc 取消由输入框自理，不落到确定/放弃上）
        if (_textInput != null) return;
        int fps = _fps;
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
                case Key.P: PinCurrentFrame(); return; // R65 钉到屏幕
                case Key.O: OcrCurrentFrame(); return; // R69 识别文字
                case Key.E: SaveRecording(); return;
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
                case Key.V: SetTool(Tool.Select); return;
                case Key.L: SetTool(Tool.Line); return;
                case Key.R: SetTool(Tool.Rect); return;
                case Key.O: SetTool(Tool.Ellipse); return;
                case Key.A: SetTool(Tool.Arrow); return;
                case Key.P: SetTool(Tool.Freehand); return;
                case Key.H: SetTool(Tool.Highlight); return;
                case Key.T: SetTool(Tool.Text); return;
                case Key.M: SetTool(Tool.Mosaic); return;
                case Key.C: SetTool(Tool.Crop); return;
                case Key.N: SetTool(Tool.Number); return;
                // 选中工具下 Delete/Backspace=删对象（优先于全局语义：Delete 撤销 / Backspace 清范围）
                case Key.Delete when _tool == Tool.Select && _sel != null: DeleteSelected(); return;
                case Key.Back when _tool == Tool.Select && _sel != null: DeleteSelected(); return;
                case Key.Delete: UndoAnn(); return;
                case Key.K: PickCustomColor(); return;
                case Key.W: if (_widthMoreBtn != null) ShowWidthPopup(_widthMoreBtn); return;
                // 时间轴裁剪键盘化（R31）：端点=当前帧，钳位保证至少 2 帧；Backspace 清除
                case Key.OemOpenBrackets: _rd.Count = _seq.Frames.Count; _rd.SetA(_index); Show(_index); e.Handled = true; return;
                case Key.OemCloseBrackets: _rd.Count = _seq.Frames.Count; _rd.SetB(_index); Show(_index); e.Handled = true; return;
                case Key.Back: _rd.A = _rd.B = -1; Show(_index); e.Handled = true; return;
            }
        }
        switch (e.Key)
        {
            case Key.Escape:
                // 弹层打开时：Esc 只收弹层（分层语义的第一层）
                if (_colorPop is { IsOpen: true }) { _colorPop.IsOpen = false; e.Handled = true; return; }
                if (_widthPop is { IsOpen: true }) { _widthPop.IsOpen = false; e.Handled = true; return; }
                // 分层语义（T82/T96）：导出中=取消；有选中=先取消选中；绘制态=先退出工具，误触 Esc 绝不丢整次捕获；浏览态=关闭
                switch (FrameCapture.Core.EscStep.Select(_encoding, _tool != Tool.Browse, _sel != null))
                {
                    case FrameCapture.Core.EscAction.CancelEncode: _commitCts?.Cancel(); break;
                    case FrameCapture.Core.EscAction.ClearSelection:
                        SetSelection(null);
                        FrameCapture.Core.Trace.Log("editor esc→deselect");
                        e.Handled = true;
                        break;
                    case FrameCapture.Core.EscAction.ExitTool:
                        CancelPendingStroke(); SetTool(Tool.Browse);
                        FrameCapture.Core.Trace.Log("editor esc→browse");
                        e.Handled = true;
                        break;
                    default: FrameCapture.Core.Fluent.CloseWithFade(this); break;
                }
                break;
            case Key.Enter: Commit(); break;
            case Key.F1: HelpWindow.ShowOrFocus(); e.Handled = true; break;
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
            Math.Max(1000.0 / _fps, 20) / _speed);
        Show(_index);
    }

    /// <summary>惰性取帧位图：命中缓存直接给，否则从帧源（内存表或 spool 磁盘）转一张，
    /// 超上限按 FIFO 逐出最久未看的。避免长录制全量驻留 OOM。</summary>
    BitmapSource Bmp(int i)
    {
        if (_bmpCache.TryGetValue(i, out var hit)) return hit;
        var bmp = BgraImage.ToSource(_seq.Frames[i]);
        _bmpCache[i] = bmp; _bmpFifo.Enqueue(i);
        while (_bmpFifo.Count > _bmpCacheMax)
        {
            int k = _bmpFifo.Dequeue();
            if (k != i) _bmpCache.Remove(k);
        }
        return bmp;
    }

    void Show(int index)
    {
        _index = Math.Clamp(index, 0, _seq.Frames.Count - 1);
        _view.Source = Bmp(_index);
        UpdateMosaicBrushes(); // 马赛克=区域滤镜：内容随帧走，不是静止贴图（R50）
        // 计数区时间用「帧号/帧率」= 播放基准，与时间轴刻度(DrawRange 同用 i/fps)一致；
        // 不用 _times 实际捕获时间戳（首帧非 0、且与刻度基准两套，R111 看截图抓到不一致）
        int fps = _fps;
        double eff = _imgRect.Width / Math.Max(1, _frameW);
        string zoomTxt = _zoom <= 1.001 ? "" : (Math.Abs(eff - 1.0) < 0.02 ? "  1:1" : $"  {eff * 100:0}%");
        string cropTxt = "";
        if (_crop.IsCropped)
        {
            var (cqW, cqH) = CropPx(_frameW, _frameH);
            cropTxt = $"  裁剪 {cqW:0}×{cqH:0}";
        }
        var (ta, tb2) = TrimRange;
        // 含两端闭区间 [a,b]：1-based 显示须 tb+1（R31 抓到差一）；全范围=未裁剪不显示（R39 逐帧复查抓到 tb2<count-1 差一）
        string trimTxt = _rd.A >= 0 && tb2 > ta && (ta > 0 || tb2 < _seq.Frames.Count - 1) ? $"  导出 {ta + 1}–{tb2 + 1}" : "";
        // 计数区：帧号 + 当前帧时刻 / 总时长（末帧时刻，与刻度/播放同基准）；总时长让用户一眼看到录制长度（R114）
        _hud.Text = $"{_index + 1}/{_seq.Frames.Count}  {_index / (double)fps:0.00}s / {(_seq.Frames.Count - 1) / (double)fps:0.00}s"
            + (_speed != 1.0 ? $"  ×{_speed:0.###}" : "")
            + zoomTxt + cropTxt + trimTxt
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
        StartEncode(null, null, (seq, gif) =>
        {
            Trace.Log($"gif encoded bytes={gif!.Length} frames={seq.Frames.Count}");
            ClipboardService.SetCapture(seq, gif, idx);
            FrameCapture.Services.SaveService.MaybeSaveArtifact(seq, gif); // R63 自动保存副本：单帧 PNG / 多帧按 RecordFormat（R103，视频后台编码；关=零副作用）
            if (strip) TryGenerateAgentStrip(seq);
            FrameCapture.Core.Fluent.CloseWithFade(this);
        });
    }

    /// <summary>R65：把当前帧（含标注+裁剪，WYSIWYG）烘焙成一张位图帧，用于钉到屏幕。
    /// 单帧同步烘焙（快，无需后台任务）；返回帧 + 其对应的物理屏幕矩形（裁剪后随之收缩）。</summary>
    (CapturedFrame? frame, RectI region) BakeCurrentFrame()
    {
        int idx = Math.Clamp(_index, 0, _seq.Frames.Count - 1);
        var frame = _seq.Frames[idx];
        var anns = new List<FrameCapture.Drawing.Annotation>(_anns.Items);
        var frames = new List<CapturedFrame> { frame };
        var outAnns = anns;
        var region = _seq.Region;
        if (_crop.IsCropped)
        {
            int fw = frame.W, fh = frame.H;
            var (cx, cy, cw, ch) = FrameCapture.Core.CropState.ToPixelOf(_crop.Current, fw, fh);
            frames = FrameCapture.Drawing.Bake.Crop(frames, cx, cy, cw, ch);
            outAnns = FrameCapture.Core.CropRemap.Remap(anns, (double)cx / fw, (double)cy / fh, (double)cw / fw, (double)ch / fh);
            region = new RectI(region.X + cx, region.Y + cy, cw, ch);
        }
        var baked = FrameCapture.Drawing.Bake.Apply(frames, outAnns);
        return (baked.Count > 0 ? baked[0] : null, region);
    }

    /// <summary>R65：钉到屏幕——烘焙当前帧并在原捕获位置弹出一张贴图（可多张并存，Snipaste 式）。</summary>
    void PinCurrentFrame()
    {
        var (f, region) = BakeCurrentFrame();
        if (f == null) return;
        var w = new PinWindow(f, region);
        w.Show();
        FrameCapture.Core.Trace.Log($"pin frame {f.W}x{f.H} at {region.X},{region.Y}");
    }

    /// <summary>预览底部轻提示：淡入显示 msg，约 1.8s 后淡出。OCR/复制等即时反馈用（R69）。</summary>
    void ShowSnack(string msg)
    {
        if (_snack == null || _snackText == null) return;
        _snackText.Text = msg;
        _snack.Visibility = Visibility.Visible;
        _snack.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            1.0, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.HoverMs))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        if (_snackTimer == null)
        {
            _snackTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            _snackTimer.Tick += (_, _) =>
            {
                _snackTimer!.Stop();
                _snack!.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
                    0.0, TimeSpan.FromMilliseconds(320))
                { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } });
            };
        }
        _snackTimer.Stop();
        _snackTimer.Start();
    }

    /// <summary>R69：识别当前帧文字（Windows 内置 OCR）→ 纯文本写剪贴板 → 轻提示。
    /// 帧像素在首个 await 前同步拷贝，故与关窗/释放 spool 无竞态；无语言包优雅降级不抛。</summary>
    async void OcrCurrentFrame()
    {
        if (_ocrBusy || _seq.Frames.Count == 0) return;
        _ocrBusy = true;
        ShowSnack("识别文字…");
        try
        {
            var frame = _seq.Frames[_index];
            var outcome = await FrameCapture.Core.OcrService.RecognizeAsync(frame);
            if (_userClosed) return; // 识别期间关窗：不再提示
            switch (outcome.Status)
            {
                case FrameCapture.Core.OcrStatus.Success:
                    var text = FrameCapture.Core.OcrService.Normalize(outcome.Text);
                    // R120 D2 预览优先：开结果窗时以「预览选取」为主，不自动写剪贴板（要哪段选哪段 / 点复制全部）；
                    // 关结果窗才走快速路径——直接复制 + 轻提示。
                    if (_settings.Current.OcrShowResult)
                        OcrResultWindow.Show(text);
                    else
                    {
                        try { System.Windows.Clipboard.SetText(text); } catch { }
                        ShowSnack(FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.Success, text.Length));
                    }
                    FrameCapture.Core.Trace.Log($"ocr ok {text.Length} chars");
                    break;
                case FrameCapture.Core.OcrStatus.NoEngine:
                    ShowSnack(FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.NoEngine, 0));
                    FrameCapture.Core.Trace.Log("ocr no-engine");
                    break;
                default:
                    ShowSnack(FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.Empty, 0));
                    FrameCapture.Core.Trace.Log("ocr empty");
                    break;
            }
        }
        catch (Exception ex)
        {
            FrameCapture.Core.Trace.Log($"ocr failed: {ex.Message}");
            if (!_userClosed) ShowSnack("识别失败");
        }
        finally { _ocrBusy = false; }
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

    /// <summary>
    /// 导出录制。videoFmt==null → GIF（onDone 收字节）；否则 ffmpeg 编码到 outPath（onDone 收 null）。
    /// 子集/裁剪/烘焙两路共用，只在末步分派编码器。
    /// </summary>
    void StartEncode(FrameCapture.Encoding.VideoFormat? videoFmt, string? outPath, Action<CapturedSequence, byte[]?> onDone)
    {
        if (_encoding) return;
        try
        {
            var (a, b) = TrimRange;
            // 导出子集：spool 用 Slice（共享文件、只切索引，不整块物化）；内存表用 GetRange。
            // 后续 Bake 会按需物化裁剪范围——导出体量由时间轴裁剪范围决定，与总录制时长解耦。
            IReadOnlyList<CapturedFrame> subset = _seq.Frames is FrameSpoolList spl
                ? spl.Slice(a, b - a + 1)
                : _seq.Frames.Skip(a).Take(b - a + 1).ToList();
            var anns = new List<FrameCapture.Drawing.Annotation>(_anns.Items);
            var region = _seq.Region; var monBounds = _seq.MonitorBounds; var monDev = _seq.MonitorDevice;
            int fps = _fps;
            var cropSnap = _crop.Current; bool cropOn = _crop.IsCropped; // UI 线程快照，后台只读
            string verb = videoFmt == null ? "GIF" : videoFmt == FrameCapture.Encoding.VideoFormat.Mp4 ? "MP4" : "WebM";

            _encoding = true;
            SetOkBusy(true);
            _commitCts = new CancellationTokenSource();
            var ct = _commitCts.Token;
            _hud.Text = $"导出 {verb}…";
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
                var prog = new Progress<int>(d => Dispatcher.BeginInvoke(() => _hud.Text = $"导出 {verb}… {d}/{baked.Count}"));
                if (videoFmt == null)
                {
                    byte[] gif = GifEncoder.Encode(baked, ComputeDelays(baked, fps), true, prog, ct);
                    return (seq, gif: (byte[]?)gif);
                }
                string ff = FrameCapture.Encoding.FfmpegLocator.Find()
                    ?? throw new System.InvalidOperationException("未找到 ffmpeg，无法导出视频");
                // 用帧时间戳推实际帧率：静态段少帧 → 低 fps，但总时长与录制一致（播放速度正确）
                double vfps = FrameCapture.Encoding.VideoEncoder.FpsFromFrames(baked);
                FrameCapture.Encoding.VideoEncoder.Encode(baked, ff, outPath!, videoFmt.Value, vfps, prog, ct);
                return (seq, gif: (byte[]?)null);
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
                    _hud.Text = t.Exception?.InnerException is System.InvalidOperationException ? "未找到 ffmpeg" : "导出失败";
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
        var dlg = NewSaveDialog("PNG 图片|*.png", $"frame-{_times[_index] / 1000.0:0.0}.png");
        if (dlg.ShowDialog(this) != true) return;
        BgraImage.SavePng(ExportFrame(_index), EnsureExt(dlg.FileName, ".png"));
    }

    void SaveRecording()
    {
        if (_encoding) return;
        // 测试专用（与 FC_TEST_ENCODE_FAIL 同约定）：置 FC_TEST_EXPORT=<路径> 则跳过存盘对话框，
        // 直接按扩展名导出，使「录制→编辑器→ffmpeg 落盘」全链路可确定性 e2e + ffprobe 核对。正常用户环境永不置位。
        string? testExport = System.Environment.GetEnvironmentVariable("FC_TEST_EXPORT");
        if (!string.IsNullOrEmpty(testExport)) { DispatchSave(testExport); return; }
        var def = _settings.Current.RecordFormat;
        string defName = def switch
        {
            RecordFormat.Mp4 => "capture.mp4",
            RecordFormat.WebM => "capture.webm",
            _ => "capture.gif",
        };
        var dlg = NewSaveDialog("MP4 视频|*.mp4|WebM 视频|*.webm|GIF 动图|*.gif", defName);
        // 类型下拉默认选中当前「录制格式」，与默认文件名扩展一致（否则总停在首项 MP4，与 capture.gif 打架）
        dlg.FilterIndex = def switch { RecordFormat.Mp4 => 1, RecordFormat.WebM => 2, _ => 3 };
        if (dlg.ShowDialog(this) != true) return;
        DispatchSave(dlg.FileName);
    }

    /// <summary>按扩展名分派导出：.mp4/.webm 走 ffmpeg，其余走 GIF。</summary>
    void DispatchSave(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".mp4" or ".webm")
        {
            if (FrameCapture.Encoding.FfmpegLocator.Find() == null)
            {
                _hud.Text = "未找到 ffmpeg，无法导出视频 — 安装方法见帮助（按 F1）";
                return;
            }
            var fmt = ext == ".mp4" ? FrameCapture.Encoding.VideoFormat.Mp4 : FrameCapture.Encoding.VideoFormat.WebM;
            path = EnsureExt(path, ext);
            StartEncode(fmt, path, (_, _) =>
            {
                _hud.Text = $"已导出 {System.IO.Path.GetFileName(path)}";
                Trace.Log($"video saved {path}");
            });
        }
        else
        {
            path = EnsureExt(path, ".gif");
            StartEncode(null, null, (_, gif) =>
            {
                try { File.WriteAllBytes(path, gif!); Trace.Log($"gif saved {path} {gif!.Length}B"); }
                catch (Exception ex) { Trace.Log($"gif save failed: {ex.Message}"); _hud.Text = "保存失败"; }
                Show(_index);
            });
        }
    }

    void SaveAfterimage()
    {
        var dlg = NewSaveDialog("PNG 图片|*.png", "afterimage.png");
        if (dlg.ShowDialog(this) != true) return;
        // 残影只稀疏采样 5 帧：用惰性裁剪投影，绝不全段物化（长录制全量转内存必 OOM）
        IReadOnlyList<CapturedFrame> src = _seq.Frames;
        if (_crop.IsCropped && _seq.Frames.Count > 0 && _frameW > 0)
        {
            var (cx, cy, cw, ch) = _crop.ToPixel(_frameW, _frameH);
            src = new LazyCropList(_seq.Frames, cx, cy, cw, ch);
        }
        var combined = BgraImage.Afterimage(src, 5);
        // 合成图同样带标注（R46）：裁剪坐标已随裁剪折算，标注做同一重映射后烘焙
        var anns = _anns.Items.ToList();
        if (_crop.IsCropped && combined.W > 0 && _frameW > 0)
        {
            var (cx, cy, cw, ch) = _crop.ToPixel(_frameW, _frameH);
            anns = FrameCapture.Core.CropRemap.Remap(anns, (double)cx / _frameW, (double)cy / _frameH, (double)cw / _frameW, (double)ch / _frameH);
        }
        var baked = FrameCapture.Drawing.Bake.Apply(new[] { combined }, anns)[0];
        BgraImage.SavePng(baked, EnsureExt(dlg.FileName, ".png"));
    }

    /// <summary>惰性裁剪投影：按需裁剪单帧，不整段物化（残影稀疏采样用）。</summary>
    sealed class LazyCropList : IReadOnlyList<CapturedFrame>
    {
        readonly IReadOnlyList<CapturedFrame> _src; readonly int _x, _y, _w, _h;
        public LazyCropList(IReadOnlyList<CapturedFrame> s, int x, int y, int w, int h) { _src = s; _x = x; _y = y; _w = w; _h = h; }
        public int Count => _src.Count;
        public CapturedFrame this[int i] => FrameCapture.Drawing.Bake.CropFrame(_src[i], _x, _y, _w, _h);
        public IEnumerator<CapturedFrame> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    static string EnsureExt(string path, string ext)
        => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? path : path + ext;

    static Microsoft.Win32.SaveFileDialog NewSaveDialog(string filter, string defaultName)
    {
        // 默认目录与「自动保存副本」保持一致：尊重用户自定义 AutoSavePath，非法/未设则回退 Pictures\FrameCapture
        string dir = FrameCapture.Services.SaveService.ResolveFolder();
        return new() { Filter = filter, FileName = defaultName, InitialDirectory = dir };
    }

    /// <summary>控制器主动关闭。</summary>
    public void CloseByController()
    {
        if (_userClosed) return;
        _userClosed = true;
        _playTimer.Stop(); // 停播放表，避免释放 spool 后定时器再读已关文件
        ClosedByUser?.Invoke();
        FrameCapture.Core.Fluent.CloseWithFade(this);
    }
}
