using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 全屏区域选择器：拖框选择，悬停窗口吸附（单击即选），Esc 取消。
/// </summary>
public sealed partial class RegionPickerWindow : Window
{
    public event Action<RectI?, IntPtr>? Result; // (选区矩形, 窗口句柄)：hwnd≠0 表示按窗口截图

    private readonly MonitorInfo _monitor;
    private readonly RectI _monitorPhys;
    private readonly Canvas _canvas;
    private readonly Rectangle _prev = new(); // 上次选区虚线框：_existing 非空才画出，Enter 即确认它
    private Path? _spotPath;
    private Path? _holeFill;
    private int _dragX1, _dragY1;   // 拖框起点：物理像素、虚拟桌面绝对坐标
    private bool _dragging;
    private bool _finished;
    private RectI? _existing;
    private readonly bool _snapContent;
    private readonly bool _windowCapture; // 点吸附窗口时走「真·窗口截图」
    private readonly bool _magnifier;
    private readonly bool _freeze;       // 选区时定格画面（显示/放大镜/截图都用定格帧）
    private readonly int _pregrabMax;    // 定格时后台预抓「完整可见」窗口干净画面的上限
    private readonly bool _windowShadow; // 预抓的窗口画面是否带合成投影
    private System.Windows.Media.Imaging.BitmapSource? _snap; // 定格全屏快照（定格模式：喂放大镜 + 背景 + 裁选区）
    private byte[]? _snapBgra; // 快照原始 BGRA（取光标下像素颜色用）
    private int _snapW;
    private CapturedFrame? _snapFrame;  // 定格全屏帧（裁选区用，物理 BGRA）
    private Image? _freezeBg; // 定格背景图（让屏幕看起来定格）
    // 定格时后台预抓的窗口干净画面缓存（HWND → 帧+区域）；点窗口命中即用，避免实时抓（定格+干净）
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, (CapturedFrame Frame, RectI Region)> _winCache = new();

    // 置顶带巡检定时器：Loaded 起每 400ms 重插一次，防被游戏等霸屏窗压住；窗口不可见即停
    readonly System.Windows.Threading.DispatcherTimer _topTimer;

    static System.Windows.Media.Animation.DoubleAnimation MaskAnim(double to, int ms, int delay)
        => new(to, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };

    void ReassertTop()
    {
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        Native.Win32.SetWindowPos(h, Native.Win32.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOMOVE|NOSIZE|NOACTIVATE
    }

    /// <summary>定格模式——Show 之后在后台线程抓全屏定格帧（本窗已 WDA 排除，遮罩已渲染也拍不到，颜色真实）。
    /// 抓完回 UI 线程填 _snap/_snapFrame 并把定格帧铺成背景（屏幕看起来就定格了）；放大镜在定格帧到位后显示。</summary>
    void StartFreezeSnapshot()
    {
        var mon = _monitorPhys;
        _ = Task.Run(() =>
        {
            var f = LiveCapture.Grab(mon);
            if (f == null) return;
            System.Windows.Media.Imaging.BitmapSource src;
            try { src = Services.BgraImage.ToSource(f); }
            catch { return; }
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _snap = src; _snapBgra = f.Bgra; _snapW = f.W; _snapFrame = f;
                    if (_freezeBg != null) _freezeBg.Source = src;
                    Core.Trace.Log($"picker freeze snapshot ready ({mon.W}x{mon.H})");
                });
            }
            catch { }
        });
    }

    /// <summary>定格+窗口截图都开时，后台按 Z 序（聚焦窗最优先）预抓「完整可见」窗口的干净画面（WGC，
    /// 带透明圆角+可选投影），缓存到 _winCache。点窗口命中即用＝定格+干净；没缓存好则控制器实时抓兜底。
    /// 只抓本屏、完整可见（未被更靠上窗遮挡）、数量取前 N 个；每个失败静默跳过；选区结束即停。</summary>
    void StartWindowPregrab()
    {
        var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int max = _pregrabMax; bool shadow = _windowShadow; var mon = _monitorPhys;
        _ = Task.Run(() =>
        {
            try
            {
                var cands = WindowEnum.ListTopmostCandidates(self).ToList(); // Z 序顶在前
                var bounds = cands.Select(t => t.Bounds).ToList();
                var visible = new List<IntPtr>();
                for (int i = 0; i < cands.Count; i++)
                {
                    var b = cands[i].Bounds;
                    if (b.W < 4 || b.H < 4) continue;
                    if (b.X >= mon.Right || mon.X >= b.Right || b.Y >= mon.Bottom || mon.Y >= b.Bottom) continue; // 不在本屏
                    if (Core.SnapPick.AnyAboveIntersects(bounds, i, b)) continue;                                 // 被遮挡
                    visible.Add(cands[i].Hwnd);
                }
                var order = Core.SnapPick.PregrabOrder(Win32.GetForegroundWindow(), visible, max);
                foreach (var h in order)
                {
                    if (_finished) return; // 选区已结束，别再抓
                    try { var (f, r) = WindowCapture.Grab(h, shadow); _winCache[h] = (f, r); }
                    catch { }
                }
                Core.Trace.Log($"picker pregrab cached={_winCache.Count}/{order.Count}");
            }
            catch (Exception e) { Core.Trace.Log("picker pregrab err " + e.Message); }
        });
    }

    /// <summary>
    /// 取定格瞬间预抓的窗口干净画面（干净圆角 + 可选投影），命中即免去实时重抓。
    /// 缓存只增不淘汰，随本窗 <c>Closed</c> 一并清空。
    /// </summary>
    /// <param name="hwnd">目标窗口句柄；0 与未预抓的句柄一律不命中。</param>
    /// <param name="frame">命中时为窗口帧（BGRA，可能带 alpha）；未命中时为 null，不得读取。</param>
    /// <param name="region">命中时为该帧对应的捕获矩形（物理像素、虚拟桌面绝对坐标）；未命中时为默认值。</param>
    /// <returns>true = 命中，两个 out 参数有效；false = 该窗没被抓到（被遮挡、不在本屏或尚未轮到），调用方自行实时抓。</returns>
    public bool TryGetCachedWindow(IntPtr hwnd, out CapturedFrame frame, out RectI region)
    {
        if (_winCache.TryGetValue(hwnd, out var v)) { frame = v.Frame; region = v.Region; return true; }
        frame = null!; region = default; return false;
    }

    /// <summary>开框瞬间的全屏定格帧：选区应裁自它而非重抓，所见即所得。</summary>
    /// <value>定格模式且抓取已完成时为整屏帧（物理 BGRA，覆盖范围 = <see cref="FrozenOrigin"/> 所指显示器）；
    /// 实时模式、抓取失败或尚未到位时为 null，调用方须回退实时抓。</value>
    public CapturedFrame? FrozenFrame => _snapFrame;
    /// <summary><see cref="FrozenFrame"/> 左上角对应的坐标，即本窗所在显示器的物理原点。</summary>
    /// <value>物理像素、虚拟桌面绝对坐标；与定格帧是否到位无关，始终有效。</value>
    public RectI FrozenOrigin => _monitorPhys;

    /// <summary>
    /// 全屏选区窗：几何在构造期按 <c>monitor</c> 的 DPI 定好，Show 即全屏；非激活窗，不抢前台。
    /// 必须在 UI 线程构造与显示（Loaded 时会注册全局键代收，并读可视树 DPI 复核几何）。
    /// </summary>
    /// <param name="monitor">要覆盖的显示器；选区、定格帧与预抓候选都限定在它的物理范围内。</param>
    /// <param name="existing">上次选区（物理像素、虚拟桌面绝对坐标），画出虚线框并允许 Enter 确认；null = 干净屏选区。</param>
    /// <param name="snapContent">true = 吸附优先取 UIA 控件矩形（不含标题栏/边框），取不到才回退窗口矩形。</param>
    /// <param name="teachTip">提示条第二行的教学文案；null = 只显示一行。</param>
    /// <param name="magnifier">true = 建放大镜（光标旁像素窗 + 坐标/尺寸读数 + 取色行）。</param>
    /// <param name="colorFormat">按 C 复制像素色的文本形式；按 F 循环切换并写回设置。</param>
    /// <param name="windowCapture">true = 点吸附到的窗口时走真·窗口截图（带 alpha），而非按矩形截区域。</param>
    /// <param name="freeze">true = 开框瞬间定格整屏，背景/放大镜/裁选区共用这一帧。</param>
    /// <param name="pregrabMax">定格模式下后台预抓「完整可见」窗口干净画面的数量上限；0 = 不预抓，且只在 <c>freeze</c> 为 true 时才启动。</param>
    /// <param name="windowShadow">预抓的窗口画面是否带合成投影。</param>
    public RegionPickerWindow(MonitorInfo monitor, RectI? existing, bool snapContent = false, string? teachTip = null, bool magnifier = false, Core.ColorCopyFormat colorFormat = Core.ColorCopyFormat.Hex, bool windowCapture = false, bool freeze = false, int pregrabMax = 0, bool windowShadow = false)
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        InputMethod.SetIsInputMethodEnabled(this, false);
        _monitor = monitor;
        _snapContent = snapContent;
        _windowCapture = windowCapture;
        _magnifier = magnifier;
        _freeze = freeze;
        _pregrabMax = pregrabMax;
        _windowShadow = windowShadow;
        _colorFormat = colorFormat;
        _monitorPhys = monitor.Bounds;
        _existing = existing;
        // 全屏快照一律在 Show 之后异步抓（见 SourceInitialized）：打开关键路径上没有 4K BitBlt。
        // 本窗设了 WDA_EXCLUDEFROMCAPTURE，抓取时即便遮罩已渲染也拍不到本窗，像素颜色仍真实。

        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetWindowTextW(h, "FrameCapture Region");
            // 选区窗本身绝不入镜（BitBlt/WGC 都拍不到）：放大镜快照因此可在 Show 之后异步抓，
            // 把 4K 抓取挪出打开关键路径 → 选区窗更快出现，且遮罩已渲染也不影响取色。
            try { Win32.SetWindowDisplayAffinity(h, Win32.WDA_EXCLUDEFROMCAPTURE); } catch { }
            // WS_EX_NOACTIVATE——Show 与鼠标按下都不激活本窗，前台焦点始终留在目标窗。
            // 否则拖框的 MouseDown 会把焦点从浏览器抢走，Z 序/前台一变，选区里就截到别的软件。
            try { Win32.SetWindowLongW(h, Win32.GWL_EXSTYLE, Win32.GetWindowLongW(h, Win32.GWL_EXSTYLE) | Win32.WS_EX_NOACTIVATE); } catch { }
            // 定格模式开框瞬间抓一张全屏定格帧（喂背景+放大镜+裁选区）；实时模式不抓全屏，
            // 放大镜改每次移动实时小区域重抓（跟画面）。定格+窗口截图都开时，后台按 Z 序预抓可见窗口干净画面。
            if (_freeze) { StartFreezeSnapshot(); if (_windowCapture && _pregrabMax > 0) StartWindowPregrab(); }
            // 分层窗（AllowsTransparency）没有黑首帧问题，绝不能屏外停放：
            // 本窗 Left/Top 在 Loaded 才赋值，Park 抓到的 home=NaN，回写 NaN 会让系统级联位兜底（实测偏移 266,266，提示被推出屏外）
            Core.Fluent.FadeIn(this, 120);
        };
        {
            var swOpen = System.Diagnostics.Stopwatch.StartNew();
            ContentRendered += (_, _) => Core.Trace.Log($"picker first frame rendered dt={swOpen.ElapsedMilliseconds}ms");
        }
        Core.Fluent.BorderlessNative(this, 0); // 全屏覆盖层：消除 DWM 非客户区白条，无需边缘调整
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Title = "FrameCapture Region"; // 与 SourceInitialized 里 SetWindowText 一致，自动化可定位
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false; // 不夺前台焦点（配合 SourceInitialized 里的 WS_EX_NOACTIVATE）——目标窗（如浏览器）保持焦点，截到的才是选区里那个窗
        // 与其它置顶窗（游戏等）同带竞争：失活即重插到置顶带最前，另有 400ms 巡检兜底
        Deactivated += (_, _) => { if (IsLoaded) ReassertTop(); };
        _topTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _topTimer.Tick += (_, _) => { if (IsVisible) ReassertTop(); else _topTimer!.Stop(); };
        Cursor = Cursors.Cross;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // 打开即全屏：构造期就按显示器 DPI 定好几何，消除"默认尺寸→Loaded 跳全屏"那一跳的边框闪烁。
        // （本窗 FadeIn 不传 offscreenFirstFrame，不走屏外 Park；Loaded 仍会按可视树 DPI 兜底校正。）
        {
            double gx = 1, gy = 1;
            try
            {
                Win32.GetDpiForMonitor(_monitor.ResolveHMonitor(), Win32.DPI_EFFECTIVE, out uint dx, out uint dy);
                if (dx > 0 && dy > 0) { gx = dx / 96.0; gy = dy / 96.0; }
            }
            catch { }
            Left = _monitorPhys.X / gx; Top = _monitorPhys.Y / gy;
            Width = _monitorPhys.W / gx; Height = _monitorPhys.H / gy;
        }

        _canvas = new Canvas();
        _hint = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x20, 0x20, 0x20)),
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 46),
            IsHitTestVisible = false,
            Opacity = 0,
        };
        {
            var hintPanel = new StackPanel();
            hintPanel.Children.Add(new TextBlock
            {
                Text = _existing != null
                    ? "拖框选择 · 悬停点击吸附窗口 · Enter 确认上次选区 · Esc 取消"
                    : "拖框选择 · 悬停点击吸附窗口 · Esc 取消",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            });
            if (teachTip != null)
                hintPanel.Children.Add(new TextBlock
                {
                    Text = teachTip,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Core.Theme.Accent),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                });
            _hint.Child = hintPanel;
        }
        var rootGrid = new Grid();
        if (_freeze)
        {
            // 定格背景：铺在最底，快照到位后填 Source → 屏幕看起来就定格；遮罩(_canvas)盖在其上作暗化/挖洞
            _freezeBg = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false, Opacity = 0 };
            rootGrid.Children.Add(_freezeBg);
        }
        rootGrid.Children.Add(_canvas);
        rootGrid.Children.Add(_hint);
        Content = rootGrid;

        Loaded += (_, _) => _topTimer.Start();
        Closed += (_, _) => { _topTimer.Stop(); _winCache.Clear(); Services.HotkeyManager.PickerActive = false; Services.HotkeyManager.PickerKey = null; }; // 清零全局键拦截，绝不泄漏成"全局键失灵"；释放预抓窗口缓存

        Loaded += (_, _) =>
        {
            double sx, sy;
            var tr = VisualTreeHelper.GetDpi(this);
            sx = tr.DpiScaleX; sy = tr.DpiScaleY;

            Left = _monitorPhys.X / sx;
            Top = _monitorPhys.Y / sy;
            Width = _monitorPhys.W / sx;
            Height = _monitorPhys.H / sy;

            // 挖洞遮罩：用 Path 奇偶填充
            _spotPath = new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)),
                Data = BuildMask(new Rect(0, 0, Width, Height), null),
            };
            // 洞内补一层 alpha=1 的填充：分层窗 alpha=0 的像素鼠标会穿透到下面窗口，
            // 导致吸附/框选高亮区内目标窗口仍可交互、无法拖选。alpha=1 肉眼不可辨但可命中。
            _holeFill = new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)),
                IsHitTestVisible = true,
            };
            _spotStroke = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                Visibility = Visibility.Collapsed,
            };
            _canvas.Children.Add(_holeFill);
            _canvas.Children.Add(_spotPath);
            _canvas.Children.Add(_spotStroke);
            _canvas.Children.Add(_prev);

            // 尺寸徽标：跟随选区左上角，实时显示物理像素 W×H
            _badgeText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
            };
            _badge = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x1B, 0x1B)),
                Padding = new Thickness(8, 3, 8, 3),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Child = _badgeText,
            };
            _canvas.Children.Add(_badge);

            // 放大镜：光标旁高倍像素窗 + 坐标/尺寸读数。UI 先建好，_snap 异步到位后 UpdateLoupe 自动显示。
            if (_magnifier)
            {
                double cell = LoupeSize / LoupeSpan;
                _loupeImg = new Image { Width = LoupeSize, Height = LoupeSize, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(_loupeImg, BitmapScalingMode.NearestNeighbor);
                _loupeMarker = new Rectangle
                {
                    Width = cell, Height = cell,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
                    StrokeThickness = 1, SnapsToDevicePixels = true,
                };
                var markerCanvas = new Canvas { Width = LoupeSize, Height = LoupeSize, IsHitTestVisible = false };
                markerCanvas.Children.Add(_loupeMarker);
                var loupeGrid = new Grid();
                loupeGrid.Children.Add(_loupeImg);
                loupeGrid.Children.Add(markerCanvas);
                _loupeInfo = new TextBlock
                {
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                };
                // 取色行：色块 + #RRGGBB（按 C 复制）
                _loupeSwatch = new System.Windows.Controls.Border
                {
                    Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _loupeHex = new TextBlock
                {
                    FontSize = 11,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var colorRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                };
                colorRow.Children.Add(_loupeSwatch);
                colorRow.Children.Add(_loupeHex);
                var loupeStack = new StackPanel();
                loupeStack.Children.Add(loupeGrid);
                loupeStack.Children.Add(_loupeInfo);
                loupeStack.Children.Add(colorRow);
                _loupe = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x1B, 0x1B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4C, 0xC2, 0xFF)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4),
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                    Child = loupeStack,
                };
                _canvas.Children.Add(_loupe);
            }

            if (_existing != null)
            {
                var r = ToDip(_existing.Value);
                Canvas.SetLeft(_prev, r.X); Canvas.SetTop(_prev, r.Y);
                _prev.Width = r.Width; _prev.Height = r.Height;
                _prev.Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x4C, 0xC2, 0xFF));
                _prev.StrokeThickness = 2;
                _prev.StrokeDashArray = new DoubleCollection { 4, 3 };
            }
            // 入场：遮罩 150ms 渐显；上次选区虚线滞后错开并轻微放大落位（ArkPV 式分层，不打扰）
            _canvas.Opacity = 0;
            _canvas.BeginAnimation(OpacityProperty, MaskAnim(1, 150, 0));
            if (_freezeBg != null) _freezeBg.BeginAnimation(OpacityProperty, MaskAnim(1, 150, 0)); // 定格背景与遮罩同步淡入
            _hint.BeginAnimation(OpacityProperty, MaskAnim(1, 220, 320)); // 提示滞后入场，不打扰
            if (_existing != null)
            {
                _prev.Opacity = 0;
                _prev.RenderTransformOrigin = new Point(0.5, 0.5);
                var st = new ScaleTransform(0.98, 0.98);
                _prev.RenderTransform = st;
                _prev.BeginAnimation(OpacityProperty, MaskAnim(1, 190, 90));
                st.BeginAnimation(ScaleTransform.ScaleXProperty, MaskAnim(1, 190, 90));
                st.BeginAnimation(ScaleTransform.ScaleYProperty, MaskAnim(1, 190, 90));
            }
            // 这里不 Activate、不抢键盘焦点——选区窗是非激活窗，键由 HotkeyManager 全局钩子代收（见 OnPickerKey）。
            Services.HotkeyManager.PickerActive = true;
            Services.HotkeyManager.PickerKey = OnPickerKey;
        };

        // 选区窗没有 WPF KeyDown 处理：非激活窗（WS_EX_NOACTIVATE）收不到路由键事件，
        // Esc/Enter/C/F/Tab 全部由 HotkeyManager 全局钩子代收（见类末 OnPickerKey）。
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseDoubleClick += (_, _) =>
        {
            var p = WindowPosToPhys(Mouse.GetPosition(this));
            if (_windowCapture)
            {
                var wc = WindowHitForCapture(p);
                if (wc != null) { Finish(wc.Value.Bounds, wc.Value.Hwnd); return; }
            }
            var hit = SnapWindowAt(p);
            Finish(hit ?? _existing ?? new RectI(_monitorPhys.X, _monitorPhys.Y, _monitorPhys.W, _monitorPhys.H));
        };
    }

    private Core.ElementHit? _curElem;
    private bool _drillLock;      // Tab 上钻后锁定选中的父容器：光标微动不会被 hover 吸附抢回子控件
    private Point _drillAnchor;   // 锁定时的光标 DIP 位置，移动超过阈值才解锁恢复常规吸附
    private System.Windows.Controls.Border _hint = null!;

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var p = WindowPosToPhys(e.GetPosition(this));
        _dragX1 = p.X; _dragY1 = p.Y;
        _dragging = true;
        _drillLock = false;   // 起手拖拽即解除 Tab 钻取锁定
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var dip = e.GetPosition(this);
        var p = WindowPosToPhys(dip);
        if (_dragging)
        {
            var rect = RectI.FromCorners(_dragX1, _dragY1, p.X, p.Y);
            ShowSpot(rect);
            UpdateLoupe(dip, p, rect);
        }
        else
        {
            // Tab 上钻锁定：光标未明显移动就保持锁定的父容器，否则鼠标一抖就被 hover 吸附回子控件。
            if (_drillLock)
            {
                if ((dip - _drillAnchor).Length <= 6)
                {
                    if (_curElem != null) { ShowSpot(_curElem.Rect); UpdateLoupe(dip, p, _curElem.Rect); }
                    return;
                }
                _drillLock = false;   // 明显移动→解锁，恢复常规吸附
            }
            // 控件级吸附（UIA）优先：输入框/文字/图片/容器…；不支持时回退窗口级
            if (_snapContent)
            {
                var selfH = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var eh = Core.ElementSnap.At(p.X, p.Y, selfH);
                if (eh != null && eh.Rect.W >= 8 && eh.Rect.H >= 8)
                {
                    _curElem = eh;
                    ShowSpot(eh.Rect);
                    UpdateLoupe(dip, p, eh.Rect);
                    return;
                }
            }
            _curElem = null;
            var hit = SnapWindowAt(p);
            if (hit != null) { ShowSpot(hit.Value); UpdateLoupe(dip, p, hit); }
            else if (_existing == null) { ShowSpot(null); UpdateLoupe(dip, p, null); }
            else UpdateLoupe(dip, p, _existing);
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var p = WindowPosToPhys(e.GetPosition(this));
        var rect = RectI.FromCorners(_dragX1, _dragY1, p.X, p.Y);
        if (rect.W < 8 || rect.H < 8)
        {
            // 视为点击：优先当前吸附的控件，其次窗口，再次沿用 existing
            if (_snapContent)
            {
                var selfH = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var eh = Core.ElementSnap.At(p.X, p.Y, selfH);
                if (eh != null && eh.Rect.W >= 8 && eh.Rect.H >= 8) { Finish(eh.Rect); return; }
            }
            // 点吸附到的窗口 → 真·窗口截图（开关开 + 未被遮挡 + 吸附到的是窗口而非 UIA 子元素）
            if (_windowCapture)
            {
                var wc = WindowHitForCapture(p);
                if (wc != null) { Finish(wc.Value.Bounds, wc.Value.Hwnd); return; }
            }
            var hit = SnapWindowAt(p);
            Finish(hit ?? _existing ?? _monitorPhys);
            return;
        }
        Finish(rect);
    }

}
