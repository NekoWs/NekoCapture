using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 编辑器主 partial：构造与生命周期。帧显示 + 传输控制 + 时间轴 + 标注绘制 + 导出（确定=GIF 进剪贴板）。
/// 其余职责分布在同名 <c>EditorWindow.*.cs</c> partial 文件中。
/// </summary>
public sealed partial class EditorWindow : Window
{
    /// <summary>窗口关闭时触发且仅触发一次——用户放弃/确定与控制器主动关闭都走它；控制器据此释放帧源。</summary>
    public event Action? ClosedByUser;
    /// <summary>确认输出或保存成功时交出结果；接收者负责释放惰性烘焙帧的独立租约。</summary>
    public event Action<CapturedSequence>? OutputReady;
    /// <summary>输出失败时触发，取消导出不属于失败。</summary>
    public event Action? OutputFailed;
    /// <summary>是否由行为图负责确认后的剪贴板与显式保存。</summary>
    public bool ManagedOutput
    {
        get => _managedOutput;
        set { _managedOutput = value; UpdateEditorStatus(); }
    }
    bool _managedOutput;

    private readonly SettingsService _settings;
    private readonly CapturedSequence _seq;
    private readonly int _fps;   // 本序列实际帧率：优先 seq.Fps（跟随刷新率录的实际值），回退设置手动值——时间轴/时长/播放都用它
    // 预览缓存预算为 64 MB，至少容纳当前一帧。
    private readonly Dictionary<int, BitmapSource> _bmpCache = new();
    private readonly Queue<int> _bmpFifo = new();
    private int _bmpCacheMax;
    // 帧元数据缓存：spool 惰性列表读一帧=整块 BGRA 回盘，HUD/时间轴只要时间戳与尺寸，
    // 故构造时一次性把元数据抽进平行数组（spool 走内存索引，零回盘）。
    private double[] _times;
    private int _frameW, _frameH;
    private readonly Image _view;
    private readonly System.Windows.Threading.DispatcherTimer _playTimer;
    private readonly TextBlock _hud;
    // 预览底部轻提示（snackbar），单帧/多帧通用——OCR/复制等即时反馈用
    private Border? _snack;
    private TextBlock? _snackText;
    private System.Windows.Threading.DispatcherTimer? _snackTimer;
    private int _snackGen;   // 轻提示世代：淡出回调只收自己那一次，期间新提示不受牵连
    private System.Windows.Media.TranslateTransform? _snackShift;   // 轻提示入场位移，收起时摘掉

    private int _index;
    private bool _playing;
    private bool _userClosed;
    private bool _ocrBusy; // 识别进行中，防重入

    private static Color DarkBg => Theme.WindowBg;
    private Button? _bPlay;
    private Border? _playPanel, _toolPanel;
    private WrapPanel? _toolsBar;
    private bool _toolsCascaded;   // 工具条逐项落定只播一次
    readonly bool _mica;           // 背板在场停靠面板才透明，缺席必须平涂，否则黑面板

    /// <summary>
    /// 构造编辑器窗口：帧元数据抽入平行数组、位图缓存上限按帧字节自适应、搭建布局并恢复标注设置。
    /// 必须在 UI 线程调用；帧源生命周期归控制器，窗口关闭（<see cref="ClosedByUser"/>）后不得再读。
    /// </summary>
    /// <param name="settings">设置服务：fps 回退值、总在最前、引导与标注设置的持久化来源。</param>
    /// <param name="seq">捕获序列：帧来源（内存表或磁盘 spool）、捕获区域与显示器信息；本窗口不拥有它。</param>
    /// <param name="initialFrame">已在后台读取的首帧；null 时按需读取。</param>
    /// <param name="initialBitmap">已冻结的首帧位图；null 时按需创建。</param>
    public EditorWindow(SettingsService settings, CapturedSequence seq, CapturedFrame? initialFrame = null, BitmapSource? initialBitmap = null)
    {
        _settings = settings;
        _seq = seq;
        if (initialFrame != null) { _curFrame = initialFrame; _curFrameIndex = 0; }
        if (initialBitmap != null) { _bmpCache[0] = initialBitmap; _bmpFifo.Enqueue(0); }
        _fps = _seq.Fps > 0 ? Math.Clamp(_seq.Fps, AppSettings.FpsMin, AppSettings.FpsMax)
                            : Math.Clamp(_settings.Current.Fps, AppSettings.FpsMin, AppSettings.FpsMax);
        // 元数据抽入平行数组 + 位图缓存上限按帧字节自适应（见字段注释）
        int n = seq.Frames.Count;
        _times = new double[n];
        for (int i = 0; i < n; i++) _times[i] = FrameMetadata.TimeAt(seq.Frames, i);
        if (n > 0) { var first = FrameMetadata.At(seq.Frames, 0); _frameW = first.W; _frameH = first.H; }
        long frameBytes = (long)_frameW * _frameH * 4;
        _bmpCacheMax = frameBytes > 0 ? (int)Math.Clamp((64L << 20) / frameBytes, 1, 24) : 1;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        // 总在最前=图钉开关（默认关）。开启时失活即重插置顶带；
        // 关闭时是普通窗口，绝不能被 Deactivated 兜底重新钉上去。
        Topmost = _settings.Current.EditorTopmost;
        Deactivated += (_, _) => { if (IsLoaded && Topmost) { Topmost = false; Topmost = true; } };
        Title = "FrameCapture Editor";
        Fluent.BorderlessNative(this); // 真无框：消 DWM 非客户区白条，原生圆角+阴影
        _mica = Fluent.MicaBackdrop(this);
        Fluent.FadeIn(this, Fluent.Motion.EditorEnterMs, 14, offscreenFirstFrame: true);
        // 工具条逐项落定，整段压在窗口淡入（EditorEnterMs）之内完成：
        // 淡入结束那一帧工具条必须已经到位，不能出现「窗口在、工具条还在路上」的空窗。
        // 开关关掉就一个动画都不挂：元素以常态直接可见，没有需要清零的中间态。
        ContentRendered += (_, _) =>
        {
            if (_toolsCascaded) return;
            _toolsCascaded = true;
            if (!_settings.Current.EditorToolbarAnim) return;
            if (_playPanel?.Child is Panel play)
                Fluent.CascadeRows(play, risePx: 6, itemMs: Fluent.Motion.PressMs,
                    budgetMs: Fluent.Motion.QuickMs);
            if (_toolsBar == null) return;
            Fluent.CascadeRows(_toolsBar, risePx: 6, itemMs: Fluent.Motion.PressMs,
                budgetMs: Fluent.Motion.QuickMs);
        };
        // 窗底交给背板：画布四周的中性暗底由 viewHost 自己涂，停靠面板透明才透出 Mica。
        Background = _mica ? Brushes.Transparent : new SolidColorBrush(DarkBg);
        var mon = Monitors.ByDevice(seq.MonitorDevice) ?? Monitors.Nearest(seq.Region.X, seq.Region.Y);
        FitMinWidthToToolbar();
        Width = Math.Min(SystemParameters.WorkArea.Width, Math.Min(1440, Math.Max(900, seq.Region.W + 100)));
        Height = Math.Min(SystemParameters.WorkArea.Height, Math.Min(1000, Math.Max(660, seq.Region.H + 200)));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (mon != null)
        {
            var (sx, sy) = Monitors.DpiScaleOf(mon);
            Left = (mon.Bounds.X + mon.Bounds.W / 2.0) / sx - Width / 2;
            Top = (mon.Bounds.Y + mon.Bounds.H / 2.0) / sy - Height / 2;
        }

        _view = new Image { Stretch = Stretch.Fill }; // 尺寸/偏移由 ApplyImageView 按 _imgRect 驱动（缩放/平移）
        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.HighQuality);

        _hud = new TextBlock
        {
            Foreground = Theme.TextBrush,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };

        _playTimer = new System.Windows.Threading.DispatcherTimer();
        _playTimer.Tick += (_, _) =>
        {
            if (_userClosed || _seq.Frames.Count == 0) return; // 关闭中：spool 已释放，绝不读帧（磁盘 spool 后读已关文件会抛）
            var (a, b) = TrimRange;
            int next = _index + 1;
            if (next > b || next >= _seq.Frames.Count) next = a;
            Show(next);
        };
        _playTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000.0 / _fps, 20));

        InitDrawing();
        BuildLayout();
        RestoreEditorPrefs(); // 恢复上次的工具/颜色/粗细（关=保持默认）
        RefreshToolOptions();
        if (!_settings.Current.EditorGuideShown) ShowGuide(); // 首次打开：一次性上手引导
        Theme.Changed += Editor_OnTheme;
        Theme.Changing += OnPaletteChanging;
        Closed += (_, _) => { Theme.Changed -= Editor_OnTheme; Theme.Changing -= OnPaletteChanging; };
        // 键位表随设置热重载：设置页改完键位，开着的编辑器下一次按键就用新表
        _settings.Changed += Editor_OnKeysChanged;
        Closed += (_, _) => _settings.Changed -= Editor_OnKeysChanged;
        Closed += (_, _) => SaveEditorPrefs(); // 关闭时记住标注设置（✓/✕/Esc 三条路径都经此）
        Show(0);
        InputMethod.SetIsInputMethodEnabled(this, false);
        PreviewKeyDown += OnKeyDown;
        Closed += (_, _) =>
        {
            if (_workspaceMenu != null) _workspaceMenu.IsOpen = false;
            if (_colorPop != null) _colorPop.IsOpen = false;
            if (_widthPop != null) _widthPop.IsOpen = false;
            _view.Source = null; _bmpCache.Clear(); _bmpFifo.Clear(); _mosaicCache.Clear();
            _curFrame = null; _curFrameIndex = -1; _commitCts?.Cancel();
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

    #region 首次上手引导

    Grid? _root;
    Border? _guide;

    void ShowGuide()
    {
        if (_root == null) return;
        var rows = new (string K, string V)[]
        {
            (Loc.T(K.Editor.Annotate), Loc.T(K.Editor.PickATool)),
            (Loc.T(K.Editor.Crop2), Loc.T(K.Editor.DragWithTheCropTool)),
            (Loc.T(K.Editor.DoneCancel), Loc.T(K.Editor.EnterCopiesToTheClipboard)),
            (Loc.T(K.Editor.More), Loc.T(K.Editor.F1OpensTheFullHelp)),
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Editor.WelcomeToTheFramecaptureEditor),
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 10),
        });
        foreach (var (k, v) in rows)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            // 名字列按内容自适应：写死的宽度只量过中文，换成英文「Confirm / cancel」就会被切掉
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var kt = new TextBlock { Text = k, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Acc), Margin = new Thickness(0, 0, 10, 0) };
            var vt = new TextBlock
            {
                Text = v, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, Theme.Text.R, Theme.Text.G, Theme.Text.B)),
            };
            Grid.SetColumn(kt, 0); Grid.SetColumn(vt, 1);
            row.Children.Add(kt); row.Children.Add(vt);
            sp.Children.Add(row);
        }
        var ok = Fluent.IconButton(Loc.T(K.Editor.GotIt), Loc.T(K.Editor.StartHere), 84, 12.5);
        ok.ClearValue(Control.FontFamilyProperty); // 文字按钮：回到继承的 UI 字体（IconButton 默认是图标字体）
        ok.Height = 30;
        Fluent.HighlightBtn(ok, true);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Margin = new Thickness(0, 14, 0, 0);
        ok.Click += (_, _) => CloseGuide();
        sp.Children.Add(ok);

        var card = new Border
        {
            MaxWidth = 520,
            CornerRadius = new CornerRadius(12),
            Background = Theme.PanelBgBrush,
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
        Grid.SetRowSpan(_guide, _root.RowDefinitions.Count);
        System.Windows.Controls.Panel.SetZIndex(_guide, 99);
        _root.Children.Add(_guide);
        _guide.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EditorEnterMs)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        Trace.Log("editor guide shown");
    }

    void CloseGuide()
    {
        if (_guide == null) return;
        var g = _guide;
        _guide = null;
        _settings.Mutate(s => s.EditorGuideShown = true);
        Trace.Log("editor guide closed");
        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
        fade.Completed += (_, _) => _root?.Children.Remove(g);
        g.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>控制器主动关闭：与用户关闭走同一收口——停播放表、触发 <see cref="ClosedByUser"/>、淡出。</summary>
    public void CloseByController()
    {
        if (_userClosed) return;
        _userClosed = true;
        _commitCts?.Cancel();
        _playTimer.Stop(); // 停播放表，避免释放 spool 后定时器再读已关文件
        ClosedByUser?.Invoke();
        Fluent.CloseWithFade(this);
    }
    #endregion

}
