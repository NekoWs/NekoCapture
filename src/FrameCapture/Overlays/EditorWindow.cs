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
