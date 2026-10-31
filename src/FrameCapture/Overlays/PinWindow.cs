using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FrameCapture.Capture;
using FrameCapture.Encoding;
using FrameCapture.Native;
using FrameCapture.Services;
using K = FrameCapture.Core.K;

namespace FrameCapture.Overlays;

/// <summary>
/// 钉到屏幕——把当前帧（含标注/裁剪，WYSIWYG）作为一张无边框、置顶、可拖动/滚轮缩放的小图，
/// 钉在桌面原捕获位置。
/// 单位：入参与源区域都是物理像素、虚拟桌面绝对坐标；Loaded 时按可视树 DPI 换算成 DIP，故 1:1 时像素比 1:1。
/// 交互：按住拖动移动；滚轮以中心为锚缩放（10%~600%）；Ctrl+滚轮调不透明度（20%~100%）；
/// 双击或 Esc 关闭；R 键顺时针旋转 90°、F3 原地重抓源区域刷新内容、Ctrl+S 另存为、
/// Ctrl+C 复制回剪贴板、Ctrl+O 识别文字；
/// 右键菜单：复制 / 另存为 / 识别文字 / 旋转 / 更新内容 / 不透明度 / 关闭 / 关闭全部贴图。
/// 不进任务栏（ShowInTaskbar=false），关掉只靠上述手势。
/// </summary>
public sealed class PinWindow : Window
{
    // 所有在屏贴图的登记表——右键菜单「关闭全部贴图」用；UI 线程访问，无需加锁
    static readonly List<PinWindow> _open = new();

    CapturedFrame _frame; // 旋转会替换为旋转后的新帧
    readonly RectI _srcRegion; // 源捕获区域（物理像素），「更新内容」按此重抓
    int _rotSteps;             // 累计顺时针 90° 次数，刷新后按同角度重放
    readonly Image _img;
    readonly Border _snack;
    Border _card;   // 入场「落图」缩放动画挂这（RenderTransform 空闲，Rotate 转的是帧数据非变换）
    readonly TextBlock _snackText;
    DispatcherTimer? _snackTimer;
    DispatcherTimer? _refreshTimer; // 「更新内容」的 200ms 延迟定时器，关窗时要停掉
    bool _closed;                   // 窗口已关闭：在途的刷新 Tick 不得再 Show()
    bool _ocrBusy;
    // 贴图显示时是否抢焦点。默认 true（编辑器 Ctrl+P / 剪贴板贴图，用户正盯着它）；
    // 「截图后=贴图」传 false——贴图静默出现不抢焦点，符合"钉住参考后继续原应用操作"意图，点击时才激活。
    readonly bool _activateOnShow;
    double _scale = 1.0, _baseW, _baseH;
    // 手动拖拽 + 实时磁吸：DragMove 拿不到逐帧位置，做不到"拖动途中就吸到边上"的实时反馈
    bool _dragging;
    double _grabDX, _grabDY;   // 光标相对贴图左上角的抓取偏移（DIP），拖动时贴图不"跳"到光标下
    double _rawLeft, _rawTop;  // 未吸附的逻辑位置（磁吸作为显示偏移叠加其上，避免与光标互相拉扯）
    // 拖动实时磁吸：拖拽起点预计算的本屏参考边（DIP，带垂直跨度），左/右、上/下分开（同类边对同类边）
    List<Core.PinSnap.Edge>
        _snapXL = null!, _snapXR = null!, _snapYT = null!, _snapYB = null!;
    string? _snapScr;

    /// <summary>
    /// 构造一张贴图：帧数据直接铺成无边框窗口，尺寸 = 帧的物理像素按 DPI 换算出的 DIP（1:1 显示）。
    /// 构造后须由调用方 <c>Show()</c>；入场淡入与「落图」在 <c>ContentRendered</c> 上触发。必须在 UI 线程构造。
    /// </summary>
    /// <param name="frame">要显示的帧（BGRA，物理像素）。贴图持有它直到旋转或刷新替换，调用方不必再拷贝。</param>
    /// <param name="physPos">物理像素、虚拟桌面绝对坐标。位置取 X/Y；整个矩形被记住作为「更新内容」的重抓区域，
    /// 因此 W/H 应等于该区域尺寸，否则刷新会按错的尺寸重抓。</param>
    /// <param name="activateOnShow">true = Show 时激活并抢焦点（用户主动钉图，正盯着它）；
    /// false = 静默出现、点击客户区才激活（"截图后=贴图"，不打断原应用操作）。</param>
    /// <param name="initialBitmap">后台准备的冻结位图；null 时按帧创建。</param>
    public PinWindow(CapturedFrame frame, RectI physPos, bool activateOnShow = true, System.Windows.Media.Imaging.BitmapSource? initialBitmap = null)
    {
        _frame = frame;
        _srcRegion = physPos; // 记住源区域，供「更新内容」原地重抓
        _activateOnShow = activateOnShow;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        ShowActivated = activateOnShow; // false：Show 时不激活（不抢焦点），点击客户区仍会激活→键盘快捷键可用
        Background = Brushes.Transparent;
        Title = "FrameCapture Pin";
        UseLayoutRounding = true;
        Focusable = true;

        var src = initialBitmap ?? BgraImage.ToSource(frame);
        src.Freeze();
        _img = new Image { Source = src, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);

        // 1px 深色描边：任何桌面背景上都看得见边界；圆角与截图工具贴图观感一致
        var border = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Child = _img,
            ClipToBounds = true,
        };
        border.RenderTransformOrigin = new Point(0.5, 0.5);
        _card = border;
        Opacity = 0; // 入场动画首帧前即隐形（避免缩放起始态闪一帧）；ContentRendered 后淡入+落图
        // 贴图内轻提示浮层（识别文字等反馈用；贴图无独立 snackbar），默认隐藏，Flash 淡入淡出
        _snackText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 260,
        };
        _snack = new Border
        {
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 20)),
            Padding = new Thickness(12, 7, 12, 7),
            Child = _snackText,
        };
        Content = new Grid();
        ((Grid)Content).Children.Add(border);
        ((Grid)Content).Children.Add(_snack);

        Loaded += (_, _) =>
        {
            // 物理像素 → DIP：1:1 显示（与 RegionOutlineWindow 同款换算），钉在原捕获位置
            var d = VisualTreeHelper.GetDpi(this);
            _baseW = frame.W / d.DpiScaleX;
            _baseH = frame.H / d.DpiScaleY;
            Left = physPos.X / d.DpiScaleX;
            Top = physPos.Y / d.DpiScaleY;
            ApplyScale();
            if (_activateOnShow) Activate();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { CloseAnimated(); return; } // 双击关闭（先于拖动判定）
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                // 若上一次磁边滑动动画还在飞，先结算到目标值再拖，避免起点与本地值不一致
                BeginAnimation(LeftProperty, null);
                BeginAnimation(TopProperty, null);
                // 按住即捕获鼠标，改手动拖拽（见 MouseMove 实时磁吸）。用光标物理坐标换算 DIP，
                // 记录抓取偏移，使贴图跟随光标且不跳到光标正下方。
                if (Win32.GetCursorPos(out var pt))
                {
                    var d = VisualTreeHelper.GetDpi(this);
                    _grabDX = pt.X / d.DpiScaleX - Left;
                    _grabDY = pt.Y / d.DpiScaleY - Top;
                }
                _rawLeft = Left; _rawTop = Top;
                _winGuides = BuildWinGuides();   // 拖拽起点快照窗口参考线，拖动途中零枚举
                _snapScr = null;
                BuildSnapLines();                // 起点预计算本屏参考边（带跨度），拖动途中零枚举
                _dragging = true;
                CaptureMouse();
                e.Handled = true;
            }
        };
        MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            if (!Win32.GetCursorPos(out var pt)) return;
            var d = VisualTreeHelper.GetDpi(this);
            _rawLeft = pt.X / d.DpiScaleX - _grabDX;
            _rawTop = pt.Y / d.DpiScaleY - _grabDY;
            if (Core.SettingsService.Shared?.Current.PinSnapEdges == true)
            {
                // 实时磁吸（Slick Window Arrangement 手感）：临近屏幕边/窗口边/其他贴图边即吸附对齐。
                // 关键＝只有垂直跨度真正重叠的同类边才吸（杜绝「到处吸附」）；无滞回也不抖（重叠判定天然稳定）。
                double ccx = (_rawLeft + Width / 2) * d.DpiScaleX, ccy = (_rawTop + Height / 2) * d.DpiScaleY;
                if (_snapScr == null ||
                    System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)ccx, (int)ccy)).DeviceName != _snapScr)
                    BuildSnapLines();   // 仅跨屏时重建（罕见），同屏零枚举
                double md = Core.PinSnap.MagnetDist(Width, Height);
                Left = Core.PinSnap.Magnet(_rawLeft, _rawLeft + Width, _rawTop, _rawTop + Height, _snapXL, _snapXR, md);
                Top = Core.PinSnap.Magnet(_rawTop, _rawTop + Height, Left, Left + Width, _snapYT, _snapYB, md);
            }
            else { Left = _rawLeft; Top = _rawTop; }
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            // 磁吸已在拖动中把贴图放到线上，松手无需再吸一次；清拖拽态
            _winGuides = null; _snapScr = null;
            _snapXL = _snapXR = _snapYT = _snapYB = null!;
        };
        MouseWheel += (_, e) =>
        {
            // Ctrl+滚轮=调不透明度（20%~100%）；纯滚轮=以中心为锚缩放（10%~600%）
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                // 先提交淡入动画的可见值再清动画：清动画后 WPF 回退到基础值 0，
                // 不先提交则 Next(0,±) 一律夹到下限 0.2——首滚突跳 0.2、之后向下滚恒 0.2「无效果」。
                CommitOpacity();
                Opacity = Core.PinOpacity.Next(Opacity, e.Delta);
                e.Handled = true;
                return;
            }
            double old = _scale;
            _scale = Core.PinZoom.Next(_scale, e.Delta);
            if (Math.Abs(_scale - old) < 1e-9) return;
            double cx = Left + Width / 2, cy = Top + Height / 2; // 以中心为锚，缩放不漂移
            ApplyScale();
            Left = cx - Width / 2; Top = cy - Height / 2;
        };
        MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) CloseAnimated();
            else if (e.Key == Key.R) Rotate();
            else if (e.Key == Key.F3) Refresh();
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) SaveAs();
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Copy(); // 复制回剪贴板
            else if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) != 0) OcrFrame(); // 识别文字（与编辑器一致）
            else
            {
                // 方向键微调位置：鼠标拖不出单像素精度，跟屏幕上的既有界面对齐要靠它。Shift 走粗格。
                var (dx, dy) = Core.PinNudge.Move(e.Key, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                if (dx != 0 || dy != 0)
                {
                    Left += dx; Top += dy;
                    _rawLeft = Left; _rawTop = Top;   // 拖动基准跟着走，否则一起手就被拽回微调前的位置
                    e.Handled = true;
                }
            }
        };

        Loaded += (_, _) => _open.Add(this);
        Closed += (_, _) => { _open.Remove(this); _closed = true; _refreshTimer?.Stop(); }; // 关窗后刷新不得再复活本窗

        // 入场=淡入 + 卡片从 0.82 缩放「落图」带轻微回弹（BackEase），像把图钉到桌面
        ContentRendered += (_, _) =>
        {
            Core.Fluent.FadeIn(this, Core.Fluent.Motion.HoverMs);
            PlayDrop();
            // 淡入后 200ms 记一次 Opacity：贴图停在 Opacity=0 时整窗隐形且无任何报错，只能靠这条日志归因
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            t.Tick += (_, __) => { t.Stop(); Core.Trace.Log($"pin shown opacity={Opacity:F2}"); };
            t.Start();
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetWindowTextW(hwnd, "FrameCapture Pin"); // 自动化/焦点安全需要稳定标题
        };
    }

    /// <summary>把动画当前可见值落为基础值再清掉动画。</summary>
    /// <remarks>清动画后 WPF 回退到基础值（这里是入场前置的 0），任何"读 Opacity 再算下一档"的调用
    /// 都必须先走这一步。同 <c>Fluent.PopupEnter</c> 模式。</remarks>
    void CommitOpacity()
    {
        double cur = Opacity;
        BeginAnimation(OpacityProperty, null);
        Opacity = cur;
    }

    /// <summary>入场「落图」——卡片从 0.94 缩放到 1，BackEase 过冲 0.25，配合淡入像把图钉到桌面。</summary>
    /// <remarks>幅度不能再大：起始更小或过冲更强时，大图闪一下会被当成缩放渲染故障。</remarks>
    void PlayDrop()
    {
        if (_card == null) return;
        var st = new ScaleTransform(0.94, 0.94);
        _card.RenderTransform = st;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 };
        var dur = new Duration(TimeSpan.FromMilliseconds(Core.Fluent.Motion.EnterMs));
        var sx = new DoubleAnimation(0.94, 1.0, dur) { EasingFunction = ease };
        var sy = new DoubleAnimation(0.94, 1.0, dur) { EasingFunction = ease };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
    }

    bool _closingOut;
    /// <summary>退场动画——淡出 + 轻微收缩（与入场呼应、幅度小），130ms 后真正 Close。
    /// 双击/Esc/右键菜单关闭统一走这里；「关闭全部贴图」保持即时关（批量不排队演动画）。</summary>
    void CloseAnimated()
    {
        if (_closingOut) return;
        _closingOut = true;
        CommitOpacity();
        var dur = new Duration(TimeSpan.FromMilliseconds(Core.Fluent.Motion.QuickMs));
        var ease = new CircleEase { EasingMode = EasingMode.EaseIn };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
        if (_card != null)
        {
            if (_card.RenderTransform is not ScaleTransform st)
            {
                st = new ScaleTransform(1, 1);
                _card.RenderTransform = st;
            }
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, dur) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, dur) { EasingFunction = ease });
        }
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
        t.Tick += (_, __) => { t.Stop(); Close(); };
        t.Start();
    }

    void ApplyScale()
    {
        _img.Width = _baseW * _scale;
        _img.Height = _baseH * _scale;
        Width = _img.Width + 2;
        Height = _img.Height + 2;
    }

    /// <summary>顺时针旋转 90°（无损像素重排），以中心为锚保持位置不漂移；转 4 次回原图。</summary>
    void Rotate()
    {
        _rotSteps = (_rotSteps + 1) % 4; // 记录角度，刷新后重放
        _frame = Core.PinRotate.Rotate90Cw(_frame);
        var src = BgraImage.ToSource(_frame);
        src.Freeze();
        _img.Source = src;
        var d = VisualTreeHelper.GetDpi(this);
        _baseW = _frame.W / d.DpiScaleX;
        _baseH = _frame.H / d.DpiScaleY;
        double cx = Left + Width / 2, cy = Top + Height / 2;
        ApplyScale();
        Left = cx - Width / 2; Top = cy - Height / 2;
        Core.Trace.Log($"pin-rotate → {_frame.W}x{_frame.H}");
    }

    /// <summary>原地重抓源区域刷新贴图内容——盯梢会变的区域（进度条、数值、直播画面）
    /// 不必重新截图。先隐藏本贴图避免把自己截进新帧（反馈循环），短暂延迟让隐藏生效，再按当前旋转角度重放。
    /// 延迟期间窗口被关闭（Esc / 关闭全部贴图）则本次刷新作废：对已关闭的窗调 Show 会抛。</summary>
    void Refresh()
    {
        if (_srcRegion.W < 1 || _srcRegion.H < 1) { Flash(Core.Loc.T(K.Pin.NothingToRefresh)); return; }
        Hide(); // 先离场，否则置顶贴图会被自己截进新帧
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _refreshTimer = t;
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (_closed) return;
            var f = LiveCapture.Grab(_srcRegion);
            if (f != null && f.W > 0 && f.H > 0)
            {
                if (_rotSteps != 0) f = Core.PinRotate.Rotate(f, _rotSteps);
                _frame = f;
                var src = BgraImage.ToSource(f); src.Freeze();
                _img.Source = src;
                var d = VisualTreeHelper.GetDpi(this);
                _baseW = f.W / d.DpiScaleX; _baseH = f.H / d.DpiScaleY;
                double cx = Left + Width / 2, cy = Top + Height / 2; // 中心锚定，尺寸变化不漂移
                ApplyScale();
                Left = cx - Width / 2; Top = cy - Height / 2;
                Flash(Core.Loc.T(K.Pin.Updated));
                Core.Trace.Log($"pin-refresh {f.W}x{f.H}");
            }
            else Flash(Core.Loc.T(K.Pin.RefreshFailed));
            Show();
        };
        t.Start();
    }

    /// <summary>把贴图当前画面（含旋转后）复制回剪贴板，Ctrl+C / 右键「复制」共用。</summary>
    async void Copy()
    {
        bool copied = await ClipboardService.SetFrameAsync(_frame, includePng: true);
        if (IsVisible) Flash(Core.Loc.T(copied ? K.Common.Copied : K.Common.ClipboardUnavailable));
        Core.Trace.Log("pin-copy");
    }

    /// <summary>把贴图当前帧（含旋转后状态）另存为 PNG；弹保存框，取消则不留任何文件与提示。</summary>
    async void SaveAs()
    {
        // 默认目录与「自动保存副本」一致：尊重自定义 AutoSavePath，回退 Pictures\FrameCapture
        string dir = await BackgroundWork.Run(SaveService.ResolveFolder);
        if (!IsVisible) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Core.Loc.T(K.Common.PngImagePng),
            FileName = $"pin-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            InitialDirectory = dir
        };
        if (dlg.ShowDialog(this) != true) return;
        string path = dlg.FileName;
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
        var frame = _frame;
        try
        {
            await BackgroundWork.Run(() => { BgraImage.SavePng(frame, path); return true; });
            if (IsVisible) Flash(Core.Loc.T(K.Pin.Saved));
            Core.Trace.Log($"pin-save {path}");
        }
        catch { if (IsVisible) Flash(Core.Loc.T(K.Common.SaveFailed)); }
    }

    List<(double L, double T, double R, double B)>? _winGuides;   // DIP，拖拽期间有效

    /// <summary>拖拽起点一次性算好本屏磁吸参考边（屏幕工作区四边 + 同屏其他贴图边 + 同屏窗口边，全 DIP，
    /// 每条边带垂直跨度）。左/上边进 near 列表、右/下边进 far 列表（同类边对同类边）。
    /// 参考边只在起点与跨屏时重建：MouseMove 热路径因此只剩纯几何比较，不含屏幕与窗口枚举。</summary>
    void BuildSnapLines()
    {
        _snapXL = new(); _snapXR = new(); _snapYT = new(); _snapYB = new();
        try
        {
            var d = VisualTreeHelper.GetDpi(this);
            double cx = (Left + Width / 2) * d.DpiScaleX, cy = (Top + Height / 2) * d.DpiScaleY;
            var scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)cx, (int)cy));
            _snapScr = scr.DeviceName;
            var wa = scr.WorkingArea;
            double cl = wa.X / d.DpiScaleX, ct = wa.Y / d.DpiScaleY, cr = wa.Right / d.DpiScaleX, cb = wa.Bottom / d.DpiScaleY;
            // 屏幕工作区四边：左/上边可被本贴图左/上边吸，右/下边可被本贴图右/下边吸
            _snapXL.Add(new Core.PinSnap.Edge(cl, ct, cb));
            _snapXR.Add(new Core.PinSnap.Edge(cr, ct, cb));
            _snapYT.Add(new Core.PinSnap.Edge(ct, cl, cr));
            _snapYB.Add(new Core.PinSnap.Edge(cb, cl, cr));
            void AddRect(double l, double t, double r, double b)
            {
                _snapXL.Add(new Core.PinSnap.Edge(l, t, b));
                _snapXR.Add(new Core.PinSnap.Edge(r, t, b));
                _snapYT.Add(new Core.PinSnap.Edge(t, l, r));
                _snapYB.Add(new Core.PinSnap.Edge(b, l, r));
            }
            foreach (var p in _open)
            {
                if (ReferenceEquals(p, this)) continue;
                double pcx = (p.Left + p.Width / 2) * d.DpiScaleX, pcy = (p.Top + p.Height / 2) * d.DpiScaleY;
                if (System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)pcx, (int)pcy)).DeviceName != _snapScr) continue;
                AddRect(p.Left, p.Top, p.Left + p.Width, p.Top + p.Height);
            }
            // 窗口边只取"可见段"——被更靠上窗口盖住的部分剔除（Slick RemoveOverlappedTargets 思路）：
            // 整条边都当参考就会把压在后面的背景窗边也算进来，吸到一条看不见的边＝莫名其妙。
            var wins = _winGuides ??= BuildWinGuides();   // Z 序上→下，DIP
            for (int i = 0; i < wins.Count; i++)
            {
                var (wl, wt, wr, wb) = wins[i];
                double wcx = (wl + wr) / 2 * d.DpiScaleX, wcy = (wt + wb) / 2 * d.DpiScaleY;
                if (System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)wcx, (int)wcy)).DeviceName != _snapScr) continue;
                AddVisible(wl, wt, wb, _snapXL, true, wins, i);
                AddVisible(wr, wt, wb, _snapXR, true, wins, i);
                AddVisible(wt, wl, wr, _snapYT, false, wins, i);
                AddVisible(wb, wl, wr, _snapYB, false, wins, i);
            }
        }
        catch { _snapXL.Clear(); _snapXR.Clear(); _snapYT.Clear(); _snapYB.Clear(); }
    }

    /// <summary>把窗口一条边 [lo,hi]@pos 减去更靠上（wins[0..above-1]，Z 序更靠前）窗口在该边线上的覆盖区间，
    /// 只把可见子段加入目标边列表。vertical=true 表示竖边（x=pos，跨度沿 Y，用窗口 L/R 判是否盖住该竖线）。</summary>
    static void AddVisible(double pos, double lo, double hi,
        List<Core.PinSnap.Edge> target, bool vertical,
        List<(double L, double T, double R, double B)> wins, int above)
    {
        if (hi <= lo) return;
        var covered = new List<(double a, double b)>();
        for (int j = 0; j < above; j++)
        {
            var a = wins[j];
            double c0, c1;
            if (vertical) { if (!(a.L <= pos && pos < a.R)) continue; c0 = Math.Max(lo, a.T); c1 = Math.Min(hi, a.B); }
            else { if (!(a.T <= pos && pos < a.B)) continue; c0 = Math.Max(lo, a.L); c1 = Math.Min(hi, a.R); }
            if (c1 > c0) covered.Add((c0, c1));
        }
        foreach (var (s0, s1) in Core.PinSnap.VisibleSegments(lo, hi, covered))
            target.Add(new Core.PinSnap.Edge(pos, s0, s1));
    }

    List<(double L, double T, double R, double B)> BuildWinGuides()
    {
        var list = new List<(double, double, double, double)>();
        try
        {
            var d = VisualTreeHelper.GetDpi(this);
            // 排除自己——否则拖拽起点快照会把本贴图此刻的边当参考边，拖起来"吸回自己起点"＝莫名其妙。
            var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            // 保持 EnumWindows 的 Z 序（上→下），供 BuildSnapLines 做遮挡裁剪：只把"看得见的边"当参考。
            foreach (var (_, b, _) in WindowEnum.ListTopmostCandidates(self, skipIconic: true))
                list.Add((b.X / d.DpiScaleX, b.Y / d.DpiScaleY, (b.X + b.W) / d.DpiScaleX, (b.Y + b.H) / d.DpiScaleY));
        }
        catch { /* 枚举失败不影响贴图/屏幕边吸附 */ }
        return list;
    }

    /// <summary>识别贴图文字，在原图上选字或直接复制全文；失败时在贴图内提示。</summary>
    async void OcrFrame()
    {
        if (_ocrBusy) return;
        _ocrBusy = true;
        using var activity = BackgroundWork.Hold();
        Flash(Core.Loc.T(K.Common.RecognizeText2));
        try
        {
            bool preview = Core.SettingsService.Shared?.Current.OcrShowResult == true;
            var outcome = await Core.OcrService.RecognizeAsync(_frame, preview);
            if (!IsVisible) return;
            if (outcome.Status == Core.OcrStatus.Success)
            {
                var text = Core.OcrService.Normalize(outcome.Text);
                if (preview)
                {
                    HideSnack();
                    OcrResultWindow.Show(outcome);
                }
                else
                {
                    bool copied = await ClipboardService.SetTextAsync(text);
                    if (IsVisible) Flash(copied ? Core.OcrService.Feedback(Core.OcrStatus.Success, text.Length) : Core.Loc.T(K.Common.ClipboardUnavailable));
                }
                Core.Trace.Log($"pin ocr ok {text.Length} chars");
            }
            else
            {
                Flash(Core.OcrService.Feedback(outcome.Status, 0));
                Core.Trace.Log($"pin ocr {outcome.Status}");
            }
        }
        catch (Exception ex)
        {
            Core.Trace.Log($"pin ocr failed: {ex.Message}");
            Flash(Core.Loc.T(K.Common.RecognitionFailed));
        }
        finally { _ocrBusy = false; }
    }

    /// <summary>贴图内浮层提示：淡入、停留 ~1.6s、淡出。重复调用会重置计时与文案。</summary>
    void Flash(string msg)
    {
        _snackTimer?.Stop();
        _snackText.Text = msg;
        _snack.Visibility = Visibility.Visible;
        _snack.BeginAnimation(OpacityProperty, new DoubleAnimation(1,
            new Duration(TimeSpan.FromMilliseconds(Core.Fluent.Motion.HoverMs)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _snackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _snackTimer.Tick += (_, _) =>
        {
            _snackTimer!.Stop();
            var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(Core.Fluent.Motion.ExitMs)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => _snack.Visibility = Visibility.Collapsed;
            _snack.BeginAnimation(OpacityProperty, fade);
        };
        _snackTimer.Start();
    }

    void HideSnack()
    {
        _snackTimer?.Stop();
        _snack.BeginAnimation(OpacityProperty, null);
        _snack.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 搭出贴图右键菜单（不打开）。公开只为版面探针能逐项量到菜单文字——
    /// 菜单开在独立 Popup 里，桌面截图与主窗视觉树都扫不到它，英文长条目被裁掉没人知道。
    /// </summary>
    /// <returns>挂好条目与动作的 <see cref="ContextMenu"/>，尚未设落点。</returns>
    public ContextMenu BuildMenu()
    {
        var menu = Core.Fluent.Ui(new ContextMenu { Resources = Core.Fluent.MenuResources() }); // 套用共享 Fluent 菜单样式：裸 ContextMenu 走系统默认样式，与全局观感不符
        var copy = new MenuItem { Header = Core.Loc.T(K.Pin.Copy), InputGestureText = "Ctrl+C" };
        copy.Click += (_, _) => Copy();
        var saveAs = new MenuItem { Header = Core.Loc.T(K.Pin.SaveAs), InputGestureText = "Ctrl+S" };
        saveAs.Click += (_, _) => SaveAs();
        var ocr = new MenuItem { Header = Core.Loc.T(K.Common.RecognizeText), InputGestureText = "Ctrl+O" };
        ocr.Click += (_, _) => OcrFrame();
        var rotate = new MenuItem { Header = Core.Loc.T(K.Pin.Rotate), InputGestureText = "R" };
        rotate.Click += (_, _) => Rotate();
        var refresh = new MenuItem { Header = Core.Loc.T(K.Pin.RefreshContent), InputGestureText = "F3" };
        refresh.Click += (_, _) => Refresh();
        // 不透明度子菜单：预设档位（Ctrl+滚轮可微调）
        var opacity = new MenuItem { Header = Core.Loc.T(K.Pin.Opacity) };
        foreach (var pct in new[] { 100, 75, 50, 25 })
        {
            double v = pct / 100.0;
            var mi = new MenuItem { Header = pct + "%" };
            mi.Click += (_, _) => { CommitOpacity(); Opacity = v; }; // 提交淡入可见值再设绝对档，避免清动画后闪回 0 / 淡入未完成时竞态
            opacity.Items.Add(mi);
        }
        var close = new MenuItem { Header = Core.Loc.T(K.Common.Close), InputGestureText = "Esc" };
        close.Click += (_, _) => CloseAnimated();
        var closeAll = new MenuItem { Header = Core.Loc.F(K.Pin.CloseAllPins, _open.Count) };
        closeAll.Click += (_, _) => { foreach (var p in _open.ToArray()) p.Close(); };
        menu.Items.Add(copy);
        menu.Items.Add(saveAs);
        menu.Items.Add(ocr);
        menu.Items.Add(rotate);
        menu.Items.Add(refresh);
        menu.Items.Add(opacity);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.Items.Add(closeAll);
        return menu;
    }

    /// <summary>在本贴图处打开右键菜单。</summary>
    void ShowMenu()
    {
        var menu = BuildMenu();
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }
}
