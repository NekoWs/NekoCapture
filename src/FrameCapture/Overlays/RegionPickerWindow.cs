using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;
using Loc = FrameCapture.Core.Loc;
using K = FrameCapture.Core.K;

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
    private readonly bool _freeze;
    readonly bool _showModeSwitch;

    /// <summary>整屏定格帧在建窗时就已到手 → 本窗按不透明全屏窗实现，不做分层透明窗。</summary>
    private readonly bool _opaqueFreeze;
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
                    Core.Trace.Log($"picker freeze snapshot ready ({mon.W}x{mon.H}) heap={GC.GetTotalMemory(false) >> 20}MB");
                });
            }
            catch { }
        });
    }

    /// <summary>定格+窗口截图都开时，后台按 Z 序（聚焦窗最优先）预抓「完整可见」窗口的干净画面（WGC，
    /// 带透明圆角+可选投影），缓存到 _winCache。点窗口命中即用＝定格+干净；没缓存好则控制器实时抓兜底。
    /// 只抓本屏、完整可见（未被更靠上窗遮挡）、数量取前 N 个；每个失败静默跳过；选区结束即停。</summary>
    /// <summary>
    /// 窗口预抓取推迟到首帧画完之后：它要按 Z 序抓若干窗口（WGC），
    /// 与首帧渲染抢同一批 CPU 与 DWM 资源时，选区窗会晚十几到几十毫秒才出现。
    /// 预抓的目的只是点选时有干净像素可用，晚开一拍没有损失。
    /// </summary>
    private void DeferWindowPregrab()
    {
        if (_firstFrameDone) { StartWindowPregrab(); return; }
        void Kick(object? s, EventArgs e)
        {
            ContentRendered -= Kick;
            StartWindowPregrab();
        }
        ContentRendered += Kick;
    }
    void StartWindowPregrab()
    {
        if (System.Threading.Interlocked.Exchange(ref _pregrabStarted, 1) != 0) return;
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
                var sizes = cands.ToDictionary(candidate => candidate.Hwnd, candidate => candidate.Bounds);
                foreach (var h in order)
                {
                    if (_finished) return; // 选区已结束，别再抓
                    if (System.Threading.Interlocked.Read(ref _pregrabBytes) >= PregrabBudgetBytes) break;
                    if (sizes.TryGetValue(h, out var expected) && (expected.W + 64L) * (expected.H + 64L) * 4
                        > PregrabBudgetBytes - System.Threading.Interlocked.Read(ref _pregrabBytes)) continue;
                    try
                    {
                        var (f, r) = WindowCapture.Grab(h, shadow);
                        if (_finished || f.Bgra.LongLength > PregrabBudgetBytes - System.Threading.Interlocked.Read(ref _pregrabBytes)) continue;
                        _winCache[h] = (f, r);
                        System.Threading.Interlocked.Add(ref _pregrabBytes, (long)f.W * f.H * 4);
                    }
                    catch { }
                }
                Core.Trace.Log($"picker pregrab cached={_winCache.Count}/{order.Count} " +
                    $"bytes={System.Threading.Interlocked.Read(ref _pregrabBytes) >> 20}MB");
            }
            catch (Exception e) { Core.Trace.Log("picker pregrab err " + e.Message); }
        });
    }

    /// <summary>窗口预抓按尺寸估算与实际像素双重限额；未缓存窗口使用定格区域裁剪。</summary>
    const long PregrabBudgetBytes = 48L << 20;

    /// <summary>本次预抓已占用的像素字节数，后台线程累加，选区关闭时读它决定要不要回收。</summary>
    private long _pregrabBytes;
    private int _pregrabStarted;

    /// <summary>大块选区缓存释放后请求优化回收，不强制整堆压缩或等待终结器。</summary>
    /// <param name="bigBytes">选区瞬时像素字节数，达到 48 MB 才请求回收。</param>
    private static void ReclaimAfterClose(long bigBytes)
    {
        if (bigBytes < 48L << 20) return;
        Core.Memory.Reclaim();
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
    /// <param name="magnifier">true = 建放大镜（光标旁像素窗 + 坐标/尺寸读数 + 取色行）。</param>
    /// <param name="colorFormat">按 C 复制像素色的文本形式；按 F 循环切换并写回设置。</param>
    /// <param name="windowCapture">true = 点吸附到的窗口时走真·窗口截图（带 alpha），而非按矩形截区域。</param>
    /// <param name="freeze">true = 开框瞬间定格整屏，背景/放大镜/裁选区共用这一帧。</param>
    /// <param name="pregrabMax">定格模式下后台预抓「完整可见」窗口干净画面的数量上限；0 = 不预抓，且只在 <c>freeze</c> 为 true 时才启动。</param>
    /// <param name="windowShadow">预抓的窗口画面是否带合成投影。</param>
    /// <param name="frozenScreen">建窗前就抓好的整屏定格帧。给了即走不透明全屏窗：帧直接铺成窗底，
    /// 窗口出现即有内容；null 且 <c>freeze</c> 为 true 则本窗做分层透明，定格帧在 Show 之后异步补。</param>
    /// <param name="recordMode">初始录制模式。</param>
    /// <param name="rightClickCancel">右键是否取消选区。</param>
    /// <param name="showModeSwitch">是否显示截图与录制模式按钮。</param>
    public RegionPickerWindow(MonitorInfo monitor, RectI? existing, bool snapContent = false, bool magnifier = false, Core.ColorCopyFormat colorFormat = Core.ColorCopyFormat.Hex, bool windowCapture = false, bool freeze = false, int pregrabMax = 0, bool windowShadow = false, CapturedFrame? frozenScreen = null, bool recordMode = false, bool rightClickCancel = true, bool showModeSwitch = true)
    {
        _showModeSwitch = showModeSwitch;
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        InputMethod.SetIsInputMethodEnabled(this, false);
        _monitor = monitor;
        _snapContent = snapContent;
        _windowCapture = windowCapture;
        _magnifier = magnifier;
        _freeze = freeze;
        _pregrabMax = pregrabMax;
        _windowShadow = windowShadow;
        RecordMode = recordMode;
        _rightClickCancel = rightClickCancel;
        _colorFormat = colorFormat;
        _monitorPhys = monitor.Bounds;
        _existing = existing;
        // 定格帧在建窗前就到手 → 本窗不必分层透明：整屏已被这一帧盖住，暗化交给遮罩层。
        // AllowsTransparency 强制软件合成，4K 全屏每一帧都在 CPU 上过一遍；不透明窗走硬件合成，
        // 且窗口出现即有完整画面，不必先见桌面再等定格帧盖上。整屏像素表面两种窗都要付，省不掉。
        _opaqueFreeze = freeze && frozenScreen != null;
        if (frozenScreen != null)
        {
            _snapFrame = frozenScreen; _snapBgra = frozenScreen.Bgra; _snapW = frozenScreen.W;
            try { _snap = Services.BgraImage.ToSource(frozenScreen); } catch { _snap = null; }
        }
        // 没有现成定格帧时，全屏快照在 Show 之后异步抓（见 SourceInitialized）：打开关键路径上没有 4K BitBlt。
        // 本窗默认设 WDA_EXCLUDEFROMCAPTURE（见 SourceInitialized）：抓取时即便遮罩已渲染也拍不到本窗，像素颜色仍真实。

        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            _selfHwnd = h;
            Win32.SetWindowTextW(h, "FrameCapture Region");
            // 选区窗本身绝不入镜（BitBlt/WGC 都拍不到）：放大镜快照因此可在 Show 之后异步抓，
            // 把 4K 抓取挪出打开关键路径 → 选区窗更快出现，且遮罩已渲染也不影响取色。
            // FC_TOAST_NOEXCLUDE=1 仅供视觉审查截图（与浮窗同一约定，用户环境永不置位）：
            // 排除开着时 PrintWindow 与桌面抓取都只拿到全黑，选区版面就没法人眼复核；代价是放大镜
            // 此时可能取到遮罩自己的像素，读数不作数。
            if (Environment.GetEnvironmentVariable("FC_TOAST_NOEXCLUDE") != "1")
                Core.Fluent.ExcludeFromCapture(h);   // 允许截到自己时放行：这层遮罩会进产物
            // WS_EX_NOACTIVATE——Show 与鼠标按下都不激活本窗，前台焦点始终留在目标窗。
            // 否则拖框的 MouseDown 会把焦点从浏览器抢走，Z 序/前台一变，选区里就截到别的软件。
            try { Win32.SetWindowLongW(h, Win32.GWL_EXSTYLE, Win32.GetWindowLongW(h, Win32.GWL_EXSTYLE) | Win32.WS_EX_NOACTIVATE); } catch { }
            // 点击穿透：本窗不进命中测试，UIA 的全局命中才能看到遮罩下面的目标窗口——
            // 有些应用（Electron 类）只暴露 UIA、且只在全局命中里出现，这是它们唯一的入口。
            // 输入改由鼠标捕获接收：捕获与命中测试无关，移动与点击照样到手，前台焦点也仍留在目标窗。
            // 捕获没拿到就立刻退回可命中状态：选区窗不能用比吸不到控件严重得多。
            // 定格模式开框瞬间抓一张全屏定格帧（喂背景+放大镜+裁选区）；实时模式不抓全屏，
            // 放大镜改每次移动实时小区域重抓（跟画面）。定格+窗口截图都开时，后台按 Z 序预抓可见窗口干净画面。
            if (_freeze)
            {
                if (!_opaqueFreeze) StartFreezeSnapshot();
                if (_windowCapture && _pregrabMax > 0) DeferWindowPregrab();
            }
            // 分层窗（AllowsTransparency）没有黑首帧问题，绝不能屏外停放：
            // 本窗 Left/Top 在 Loaded 才赋值，Park 抓到的 home=NaN，回写 NaN 会让系统级联位兜底（实测偏移 266,266，提示被推出屏外）
            // 定格模式下窗底就是那一帧画面，与按键前的屏幕完全相同——渐显反而让整屏暗、亮一次。
            // 只有分层窗（没有定格帧）才需要它盖住首帧。
            if (!_opaqueFreeze) Core.Fluent.FadeIn(this, Core.Fluent.Motion.QuickMs);
            else Opacity = 1;
        };
        {
            var swOpen = System.Diagnostics.Stopwatch.StartNew();
            ContentRendered += (_, _) =>
            {
                _firstFrameDone = true;
                Core.Trace.Log($"picker first frame rendered dt={swOpen.ElapsedMilliseconds}ms");
            };
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
        if (_opaqueFreeze)
        {
            // 不透明全屏窗：窗底就是那一帧定格画面，遮罩在其上半透明暗化，无需逐像素透明
            Background = Brushes.Black;
            AllowsTransparency = false;
        }
        else
        {
            Background = Brushes.Transparent;
            AllowsTransparency = true;
        }
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
        // 提示条只有一行：操作口径本身，不带教学副行
        _hintLabel = new TextBlock
        {
            Text = _existing != null
                ? Loc.T(K.Capture.DragToSelectHover2)
                : Loc.T(K.Capture.DragToSelectHover),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
        };
        _hint.Child = _hintLabel;
        UpdateRecordingHint();
        var rootGrid = new Grid();
        if (_freeze)
        {
            // 定格背景：铺在最底，快照到位后填 Source → 屏幕看起来就定格；遮罩(_canvas)盖在其上作暗化/挖洞
            _freezeBg = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false, Opacity = _opaqueFreeze ? 1 : 0 };
            if (_opaqueFreeze && _snap is { } s) _freezeBg.Source = s;
            rootGrid.Children.Add(_freezeBg);
        }
        rootGrid.Children.Add(_canvas);
        rootGrid.Children.Add(_hint);
        Content = rootGrid;

        Loaded += (_, _) => { _topTimer.Start(); StartPointerUpdates(); };
        Closed += (_, _) =>
        {
            _topTimer.Stop();
            StopPointerUpdates();
            // 控制器在关闭回调后读取预抓缓存，缓存随窗口实例释放。
            Services.HotkeyManager.PickerActive = false;
            Services.HotkeyManager.PickerKey = null; // 清零全局键拦截，绝不泄漏成失灵后无人重挂
            var sf = _snapFrame;
            ReclaimAfterClose((sf != null ? (long)sf.W * sf.H * 4 : 0) + System.Threading.Interlocked.Read(ref _pregrabBytes));
        };

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
            _spotPath = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),   // 选区期间整屏压暗 50%
                Data = BuildMask(new Rect(0, 0, Width, Height), null),
            };
            // 洞内补一层 alpha=1 的填充：分层窗 alpha=0 的像素鼠标会穿透到下面窗口，
            // 导致吸附/框选高亮区内目标窗口仍可交互、无法拖选。alpha=1 肉眼不可辨但可命中。
            _holeFill = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)),
                IsHitTestVisible = true,
            };
            _spotStroke = new Rectangle
            {
                Stroke = Theme.AccentBrush,
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
                Foreground = Theme.AccentBrush,
            };
            _badge = new Border
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
                    Stroke = Theme.AccentBrush,
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
                    Foreground = Theme.AccentBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                };
                // 取色行：色块 + #RRGGBB（按 C 复制）
                _loupeSwatch = new Border
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
                _loupe = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x1B, 0x1B)),
                    BorderBrush = new SolidColorBrush(Fluent.WithAlpha(Theme.Accent, 0x80)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4),
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                    Child = loupeStack,
                };
                _canvas.Children.Add(_loupe);

        // 可调整态的控制点与勾/叉按钮：排在最后加上，画在遮罩之上
        BuildAdjustChrome();
            }

            if (_existing != null)
            {
                var r = ToDip(_existing.Value);
                Canvas.SetLeft(_prev, r.X); Canvas.SetTop(_prev, r.Y);
                _prev.Width = r.Width; _prev.Height = r.Height;
                _prev.Stroke = new SolidColorBrush(Fluent.WithAlpha(Theme.Accent, 0x90));
                _prev.StrokeThickness = 2;
                _prev.StrokeDashArray = new DoubleCollection { 4, 3 };
            }
            // 入场：遮罩 150ms 渐显；上次选区虚线滞后错开并轻微放大落位（ArkPV 式分层，不打扰）
            if (_opaqueFreeze)
            {
                // 定格模式：窗底就是按键前那一帧，画面与按键前的屏幕一模一样。
                // 这里不渐显、也不压暗——压暗那一下是可见的"整屏黑"，而拖选一开始它才有用。
                _canvas.Opacity = 1;
                _canvas.BeginAnimation(OpacityProperty, null);
                _hint.BeginAnimation(OpacityProperty, MaskAnim(1, 120, 0));
            }
            else
            {
                _canvas.Opacity = 0;
                _canvas.BeginAnimation(OpacityProperty, MaskAnim(1, 150, 0));
                _hint.BeginAnimation(OpacityProperty, MaskAnim(1, 220, 320)); // 提示滞后入场，不打扰
            }
            if (_freezeBg != null && !_opaqueFreeze) _freezeBg.BeginAnimation(OpacityProperty, MaskAnim(1, 150, 0)); // 定格背景与遮罩同步淡入
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
        // 右键关整层：与 Esc 同一条出口，设置里可以关掉（网页里想右键复制时不至于把选区弄丢）
        MouseRightButtonUp += (_, _) => { if (_rightClickCancel) Finish(null); };
        // 启用的模式开关位于屏幕上方，尚未选区时即可切换。
        if (_showModeSwitch) BuildModeSwitch(Width);
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseDoubleClick += (_, _) =>
        {
            var p = WindowPosToPhys(Mouse.GetPosition(this));
            if (_windowCapture)
            {
                var wc = WindowHitForCapture(p);
                if (wc != null)
                {
                    if (!Editable) { Finish(wc.Value.Bounds, wc.Value.Hwnd); return; }
                    ShowSelection(wc.Value.Bounds);
                    _selHwnd = wc.Value.Hwnd;
                    _selHwndBounds = wc.Value.Bounds;
                    return;
                }
            }
            var hit = SnapWindowAt(p);
            Finish(hit ?? _existing ?? new RectI(_monitorPhys.X, _monitorPhys.Y, _monitorPhys.W, _monitorPhys.H));
        };
    }

    private Core.ElementHit? _curElem;
    private bool _drillLock;      // Tab 上钻后锁定选中的父容器：光标微动不会被 hover 吸附抢回子控件
    private Point _drillAnchor;   // 锁定时的光标 DIP 位置，移动超过阈值才解锁恢复常规吸附
    private Border _hint = null!;

    /// <summary>提示条里的那行字：调整态要把文案换成「拖边角改大小 · Enter 确认」。</summary>
    private TextBlock? _hintLabel;

    /// <summary>首帧是否已画完：窗口预抓取等它之后再开始。</summary>
    private bool _firstFrameDone;



}
