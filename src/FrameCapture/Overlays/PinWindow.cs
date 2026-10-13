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

namespace FrameCapture.Overlays;

/// <summary>
/// R65：钉到屏幕——把当前帧（含标注/裁剪，WYSIWYG）作为一张无边框、置顶、可拖动/滚轮缩放的小图，
/// 钉在桌面原捕获位置。Snipaste/QQ 的招牌手感，此前缺位。
/// 交互：按住拖动移动；滚轮以中心为锚缩放（10%~600%）；Ctrl+滚轮调不透明度（20%~100%）；
/// 双击或 Esc 关闭；R 键顺时针旋转 90°、F3 原地重抓源区域刷新内容、Ctrl+S 另存为、Ctrl+C 复制回剪贴板、Ctrl+O 识别文字；右键菜单复制/另存为/识别文字/旋转/更新内容/不透明度/关闭/关闭全部贴图。
/// 不进任务栏（ShowInTaskbar=false），关掉只靠上述手势——与 Snipaste 贴图一致。
/// </summary>
public sealed class PinWindow : Window
{
    // R66：所有在屏贴图的登记表——右键菜单「关闭全部贴图」用；UI 线程访问，无需加锁
    static readonly List<PinWindow> _open = new();

    CapturedFrame _frame; // R79：旋转会替换为旋转后的新帧
    readonly RectI _srcRegion; // R84：源捕获区域（物理像素），「更新内容」按此重抓
    int _rotSteps;             // R84：累计顺时针 90° 次数，刷新后按同角度重放
    readonly Image _img;
    readonly Border _snack;
    Border _card;   // R120 D3：入场「落图」缩放动画挂这（RenderTransform 空闲，Rotate 转的是帧数据非变换）
    readonly TextBlock _snackText;
    DispatcherTimer? _snackTimer;
    // R118：贴图显示时是否抢焦点。默认 true（编辑器 Ctrl+P / 剪贴板贴图，用户正盯着它）；
    // 「截图后=贴图」传 false——贴图静默出现不抢焦点，符合"钉住参考后继续原应用操作"意图，点击时才激活（Snipaste 式）。
    readonly bool _activateOnShow;
    double _scale = 1.0, _baseW, _baseH;
    // R120 A3：手动拖拽 + 实时磁吸状态（替代 DragMove——旧版只在松手瞬间吸附、过程零反馈，用户感知"磁边无效"）
    bool _dragging;
    double _grabDX, _grabDY;   // 光标相对贴图左上角的抓取偏移（DIP），拖动时贴图不"跳"到光标下
    double _rawLeft, _rawTop;  // 未吸附的逻辑位置（磁吸作为显示偏移叠加其上，避免与光标互相拉扯）
    // R163 拖动实时磁吸：拖拽起点预计算的本屏参考边（DIP，带垂直跨度），左/右、上/下分开（同类边对同类边）
    System.Collections.Generic.List<FrameCapture.Core.PinSnap.Edge>
        _snapXL = null!, _snapXR = null!, _snapYT = null!, _snapYB = null!;
    string? _snapScr;

    public PinWindow(CapturedFrame frame, RectI physPos, bool activateOnShow = true)
    {
        _frame = frame;
        _srcRegion = physPos; // R84：记住源区域，供「更新内容」原地重抓
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

        var src = BgraImage.ToSource(frame);
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
        // R75：贴图内轻提示浮层（识别文字等反馈用；贴图无独立 snackbar），默认隐藏，Flash 淡入淡出
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
                // R120 A3：按住即捕获鼠标，改手动拖拽（见 MouseMove 实时磁吸）。用光标物理坐标换算 DIP，
                // 记录抓取偏移，使贴图跟随光标且不跳到光标正下方。
                if (Win32.GetCursorPos(out var pt))
                {
                    var d = VisualTreeHelper.GetDpi(this);
                    _grabDX = pt.X / d.DpiScaleX - Left;
                    _grabDY = pt.Y / d.DpiScaleY - Top;
                }
                _rawLeft = Left; _rawTop = Top;
                _winGuides = BuildWinGuides();   // R162：拖拽起点快照窗口参考线，拖动途中不再全量枚举
                _snapScr = null;
                BuildSnapLines();                // R163：起点预计算本屏参考边（带跨度），拖动途中零枚举
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
            if (FrameCapture.Core.SettingsService.Shared?.Current.PinSnapEdges == true)
            {
                // R163 实时磁吸（Slick Window Arrangement 手感）：临近屏幕边/窗口边/其他贴图边就吸过去。
                // 关键＝只有垂直跨度真正重叠的同类边才吸（杜绝「到处吸附」）；无滞回也不抖（重叠判定天然稳定）。
                double ccx = (_rawLeft + Width / 2) * d.DpiScaleX, ccy = (_rawTop + Height / 2) * d.DpiScaleY;
                if (_snapScr == null ||
                    System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)ccx, (int)ccy)).DeviceName != _snapScr)
                    BuildSnapLines();   // 仅跨屏时重建（罕见），同屏零枚举
                double md = FrameCapture.Core.PinSnap.MagnetDist(Width, Height);
                Left = FrameCapture.Core.PinSnap.Magnet(_rawLeft, _rawLeft + Width, _rawTop, _rawTop + Height, _snapXL, _snapXR, md);
                Top = FrameCapture.Core.PinSnap.Magnet(_rawTop, _rawTop + Height, Left, Left + Width, _snapYT, _snapYB, md);
            }
            else { Left = _rawLeft; Top = _rawTop; }
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            // R163：磁吸已在拖动中把贴图放到线上，松手无需再吸一次；清拖拽态
            _winGuides = null; _snapScr = null;
            _snapXL = _snapXR = _snapYT = _snapYB = null!;
        };
        MouseWheel += (_, e) =>
        {
            // Ctrl+滚轮=调不透明度（20%~100%）；纯滚轮=以中心为锚缩放（10%~600%）
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                // 先提交淡入动画的可见值再清动画：否则清动画后 Opacity 读回基础值 0，Next(0,±) 一律夹到 0.2
                // ——首滚突跳 0.2、再向下滚恒 0.2「无效果」（R120 修 R119 未竟的不透明度失效）
                CommitOpacity();
                Opacity = FrameCapture.Core.PinOpacity.Next(Opacity, e.Delta);
                e.Handled = true;
                return;
            }
            double old = _scale;
            _scale = FrameCapture.Core.PinZoom.Next(_scale, e.Delta);
            if (System.Math.Abs(_scale - old) < 1e-9) return;
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
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Copy(); // R95 复制回剪贴板
            else if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) != 0) OcrFrame(); // R95 识别文字（与编辑器一致）
        };

        Loaded += (_, _) => _open.Add(this);
        Closed += (_, _) => _open.Remove(this);

        // R120 D3：入场=淡入 + 卡片从 0.82 缩放「落图」带轻微回弹（BackEase），像把图钉到桌面
        ContentRendered += (_, _) =>
        {
            FrameCapture.Core.Fluent.FadeIn(this, 140);
            PlayDrop();
            // 淡入后确认 Opacity 已起来（R157：曾因 FadeIn 在已渲染时不再淡入，贴图永久 Opacity=0 隐形）
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            t.Tick += (_, __) => { t.Stop(); FrameCapture.Core.Trace.Log($"pin shown opacity={Opacity:F2}"); };
            t.Start();
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetWindowTextW(hwnd, "FrameCapture Pin"); // 自动化/焦点安全需要稳定标题
        };
    }

    /// <summary>把淡入动画的当前可见值落为基础值并清掉动画：清动画后 WPF 回退到基础值（0），
    /// 不先提交就会读回 0——不透明度调节因此首滚跳 0.2、再滚无反应（R120 修）。同 Fluent.PopupEnter 模式。</summary>
    void CommitOpacity()
    {
        double cur = Opacity;
        BeginAnimation(OpacityProperty, null);
        Opacity = cur;
    }

    /// <summary>R120 D3：入场「落图」——卡片缩放带 BackEase 轻微过冲，配合淡入像钉到桌面。
    /// R162：幅度调小（0.82→0.94、过冲 0.55→0.25）——原幅度太大，大图闪一下像 zoom 故障。</summary>
    void PlayDrop()
    {
        if (_card == null) return;
        var st = new ScaleTransform(0.94, 0.94);
        _card.RenderTransform = st;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 };
        var dur = new Duration(TimeSpan.FromMilliseconds(180));
        var sx = new DoubleAnimation(0.94, 1.0, dur) { EasingFunction = ease };
        var sy = new DoubleAnimation(0.94, 1.0, dur) { EasingFunction = ease };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
    }

    bool _closingOut;
    /// <summary>R162：退场动画——淡出 + 轻微收缩（与入场呼应、幅度小），130ms 后真正 Close。
    /// 双击/Esc/右键菜单关闭统一走这里；「关闭全部贴图」保持即时关（批量不排队演动画）。</summary>
    void CloseAnimated()
    {
        if (_closingOut) return;
        _closingOut = true;
        CommitOpacity();
        var dur = new Duration(TimeSpan.FromMilliseconds(120));
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
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
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

    /// <summary>R79：顺时针旋转 90°（无损像素重排），以中心为锚保持位置不漂移；转 4 次回原图。</summary>
    void Rotate()
    {
        _rotSteps = (_rotSteps + 1) % 4; // R84：记录角度，刷新后重放
        _frame = FrameCapture.Core.PinRotate.Rotate90Cw(_frame);
        var src = BgraImage.ToSource(_frame);
        src.Freeze();
        _img.Source = src;
        var d = VisualTreeHelper.GetDpi(this);
        _baseW = _frame.W / d.DpiScaleX;
        _baseH = _frame.H / d.DpiScaleY;
        double cx = Left + Width / 2, cy = Top + Height / 2;
        ApplyScale();
        Left = cx - Width / 2; Top = cy - Height / 2;
        FrameCapture.Core.Trace.Log($"pin-rotate → {_frame.W}x{_frame.H}");
    }

    /// <summary>R84：原地重抓源区域刷新贴图内容（Snipaste F3 式）——盯梢会变的区域（进度条、数值、直播画面）
    /// 不必重新截图。先隐藏本贴图避免把自己截进新帧（反馈循环），短暂延迟让隐藏生效，再按当前旋转角度重放。</summary>
    void Refresh()
    {
        if (_srcRegion.W < 1 || _srcRegion.H < 1) { Flash("无法更新"); return; }
        Hide(); // 先离场，否则置顶贴图会被自己截进新帧
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            var f = LiveCapture.Grab(_srcRegion);
            if (f != null && f.W > 0 && f.H > 0)
            {
                if (_rotSteps != 0) f = FrameCapture.Core.PinRotate.Rotate(f, _rotSteps);
                _frame = f;
                var src = BgraImage.ToSource(f); src.Freeze();
                _img.Source = src;
                var d = VisualTreeHelper.GetDpi(this);
                _baseW = f.W / d.DpiScaleX; _baseH = f.H / d.DpiScaleY;
                double cx = Left + Width / 2, cy = Top + Height / 2; // 中心锚定，尺寸变化不漂移
                ApplyScale();
                Left = cx - Width / 2; Top = cy - Height / 2;
                Flash("已更新");
                FrameCapture.Core.Trace.Log($"pin-refresh {f.W}x{f.H}");
            }
            else Flash("更新失败");
            Show();
        };
        t.Start();
    }

    /// <summary>R80：把贴图当前帧（含旋转后状态）另存为 PNG，默认目录「图片\FrameCapture」，与编辑器保存一致。</summary>
    /// <summary>R95 把贴图当前画面（含旋转后）复制回剪贴板，Ctrl+C / 右键「复制」共用。</summary>
    void Copy()
    {
        ClipboardService.SetFrame(_frame);
        Flash("已复制");
        FrameCapture.Core.Trace.Log("pin-copy");
    }

    void SaveAs()
    {
        // 默认目录与「自动保存副本」一致：尊重自定义 AutoSavePath，回退 Pictures\FrameCapture
        string dir = FrameCapture.Services.SaveService.ResolveFolder();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG 图片|*.png",
            FileName = $"pin-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            InitialDirectory = dir
        };
        if (dlg.ShowDialog(this) != true) return;
        string path = dlg.FileName;
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
        try { BgraImage.SavePng(_frame, path); Flash("已保存"); FrameCapture.Core.Trace.Log($"pin-save {path}"); }
        catch { Flash("保存失败"); }
    }

    List<(double L, double T, double R, double B)>? _winGuides;   // DIP，拖拽期间有效

    /// <summary>R163：拖拽起点一次性算好本屏磁吸参考边（屏幕工作区四边 + 同屏其他贴图边 + 同屏窗口边，全 DIP，
    /// 每条边带垂直跨度）。左/上边进 near 列表、右/下边进 far 列表（同类边对同类边）。此前每个 MouseMove 全量重算
    /// （含多次 Screen.FromPoint）＝拖动掉帧「卡卡的」来源之一；预计算后热路径只剩纯几何。</summary>
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
            _snapXL.Add(new FrameCapture.Core.PinSnap.Edge(cl, ct, cb));
            _snapXR.Add(new FrameCapture.Core.PinSnap.Edge(cr, ct, cb));
            _snapYT.Add(new FrameCapture.Core.PinSnap.Edge(ct, cl, cr));
            _snapYB.Add(new FrameCapture.Core.PinSnap.Edge(cb, cl, cr));
            void AddRect(double l, double t, double r, double b)
            {
                _snapXL.Add(new FrameCapture.Core.PinSnap.Edge(l, t, b));
                _snapXR.Add(new FrameCapture.Core.PinSnap.Edge(r, t, b));
                _snapYT.Add(new FrameCapture.Core.PinSnap.Edge(t, l, r));
                _snapYB.Add(new FrameCapture.Core.PinSnap.Edge(b, l, r));
            }
            foreach (var p in _open)
            {
                if (ReferenceEquals(p, this)) continue;
                double pcx = (p.Left + p.Width / 2) * d.DpiScaleX, pcy = (p.Top + p.Height / 2) * d.DpiScaleY;
                if (System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)pcx, (int)pcy)).DeviceName != _snapScr) continue;
                AddRect(p.Left, p.Top, p.Left + p.Width, p.Top + p.Height);
            }
            foreach (var (wl, wt, wr, wb) in _winGuides ??= BuildWinGuides())
            {
                double wcx = (wl + wr) / 2 * d.DpiScaleX, wcy = (wt + wb) / 2 * d.DpiScaleY;
                if (System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)wcx, (int)wcy)).DeviceName != _snapScr) continue;
                AddRect(wl, wt, wr, wb);
            }
        }
        catch { _snapXL.Clear(); _snapXR.Clear(); _snapYT.Clear(); _snapYB.Clear(); }
    }

    List<(double L, double T, double R, double B)> BuildWinGuides()
    {
        var list = new System.Collections.Generic.List<(double, double, double, double)>();
        try
        {
            var d = VisualTreeHelper.GetDpi(this);
            foreach (var (_, b, _) in FrameCapture.Native.WindowEnum.ListTopmostCandidates(skipIconic: true))
                list.Add((b.X / d.DpiScaleX, b.Y / d.DpiScaleY, (b.X + b.W) / d.DpiScaleX, (b.Y + b.H) / d.DpiScaleY));
        }
        catch { /* 枚举失败不影响贴图/屏幕边吸附 */ }
        return list;
    }

    /// <summary>R75：识别贴图里的印刷文字（Windows 内置 OCR）→ 写剪贴板；开「识别后显示结果窗」则弹结果窗，
    /// 否则贴图内浮层提示。无语言包/无文字等优雅降级，浮层给出原因。</summary>
    async void OcrFrame()
    {
        Flash("识别文字…");
        try
        {
            var outcome = await FrameCapture.Core.OcrService.RecognizeAsync(_frame);
            if (outcome.Status == FrameCapture.Core.OcrStatus.Success)
            {
                var text = FrameCapture.Core.OcrService.Normalize(outcome.Text);
                // R120 D2 预览优先：开结果窗时以「预览选取」为主，不自动写剪贴板；关结果窗才走快速复制路径。
                if (FrameCapture.Core.SettingsService.Shared?.Current.OcrShowResult == true)
                {
                    HideSnack();
                    OcrResultWindow.Show(text);
                }
                else
                {
                    try { Clipboard.SetText(text); } catch { }
                    Flash(FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.Success, text.Length));
                }
                FrameCapture.Core.Trace.Log($"pin ocr ok {text.Length} chars");
            }
            else
            {
                Flash(FrameCapture.Core.OcrService.Feedback(outcome.Status, 0));
                FrameCapture.Core.Trace.Log($"pin ocr {outcome.Status}");
            }
        }
        catch (Exception ex)
        {
            FrameCapture.Core.Trace.Log($"pin ocr failed: {ex.Message}");
            Flash("识别失败");
        }
    }

    /// <summary>贴图内浮层提示：淡入、停留 ~1.6s、淡出。重复调用会重置计时与文案。</summary>
    void Flash(string msg)
    {
        _snackTimer?.Stop();
        _snackText.Text = msg;
        _snack.Visibility = Visibility.Visible;
        _snack.BeginAnimation(OpacityProperty, new DoubleAnimation(1,
            new Duration(TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.HoverMs)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _snackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _snackTimer.Tick += (_, _) =>
        {
            _snackTimer!.Stop();
            var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ExitMs)))
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

    void ShowMenu()
    {
        var menu = new ContextMenu { Resources = FrameCapture.Core.Fluent.MenuResources() }; // R120 D4：套用共享 Fluent 菜单样式（原裸 ContextMenu 系统默认样式很丑）
        var copy = new MenuItem { Header = "复制", InputGestureText = "Ctrl+C" };
        copy.Click += (_, _) => Copy();
        var saveAs = new MenuItem { Header = "另存为…", InputGestureText = "Ctrl+S" };
        saveAs.Click += (_, _) => SaveAs();
        var ocr = new MenuItem { Header = "识别文字", InputGestureText = "Ctrl+O" };
        ocr.Click += (_, _) => OcrFrame();
        var rotate = new MenuItem { Header = "旋转 90°", InputGestureText = "R" };
        rotate.Click += (_, _) => Rotate();
        var refresh = new MenuItem { Header = "更新内容", InputGestureText = "F3" };
        refresh.Click += (_, _) => Refresh();
        // 不透明度子菜单：预设档位（Ctrl+滚轮可微调）
        var opacity = new MenuItem { Header = "不透明度" };
        foreach (var pct in new[] { 100, 75, 50, 25 })
        {
            double v = pct / 100.0;
            var mi = new MenuItem { Header = pct + "%" };
            mi.Click += (_, _) => { CommitOpacity(); Opacity = v; }; // 提交淡入可见值再设绝对档，避免清动画后闪回 0 / 淡入未完成时竞态
            opacity.Items.Add(mi);
        }
        var close = new MenuItem { Header = "关闭", InputGestureText = "Esc" };
        close.Click += (_, _) => CloseAnimated();
        var closeAll = new MenuItem { Header = $"关闭全部贴图 ({_open.Count})" };
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
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }
}
