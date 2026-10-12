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
    readonly TextBlock _snackText;
    DispatcherTimer? _snackTimer;
    double _scale = 1.0, _baseW, _baseH;

    public PinWindow(CapturedFrame frame, RectI physPos)
    {
        _frame = frame;
        _srcRegion = physPos; // R84：记住源区域，供「更新内容」原地重抓
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
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
            Activate();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { Close(); return; } // 双击关闭（先于拖动判定，避免 DragMove 吞掉第二击）
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                // 若上一次磁边滑动动画还在飞，先结算到目标值再拖，避免 DragMove 起点与本地值不一致
                BeginAnimation(LeftProperty, null);
                BeginAnimation(TopProperty, null);
                try { DragMove(); } catch { }
                SnapToEdge(); // R74：松手后磁边（Snipaste 式停靠屏幕边缘）
            }
        };
        MouseWheel += (_, e) =>
        {
            // Ctrl+滚轮=调不透明度（20%~100%）；纯滚轮=以中心为锚缩放（10%~600%）
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
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
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.R) Rotate();
            else if (e.Key == Key.F3) Refresh();
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) SaveAs();
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Copy(); // R95 复制回剪贴板
            else if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) != 0) OcrFrame(); // R95 识别文字（与编辑器一致）
        };

        Loaded += (_, _) => _open.Add(this);
        Closed += (_, _) => _open.Remove(this);

        FrameCapture.Core.Fluent.FadeIn(this, 120, 10);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetWindowTextW(hwnd, "FrameCapture Pin"); // 自动化/焦点安全需要稳定标题
        };
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

    /// <summary>R74 磁边：拖动松手后，若贴图某条边贴近所在屏幕工作区边缘（阈值内）则平滑吸附贴合。</summary>
    void SnapToEdge()
    {
        if (FrameCapture.Core.SettingsService.Shared?.Current.PinSnapEdges != true) return;
        try
        {
            var d = VisualTreeHelper.GetDpi(this);
            // 用贴图中心定位所在屏幕（多屏），工作区物理像素 → DIP 与 Left/Top 同单位
            double cx = (Left + Width / 2) * d.DpiScaleX, cy = (Top + Height / 2) * d.DpiScaleY;
            var scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)cx, (int)cy));
            var wa = scr.WorkingArea;
            double cl = wa.X / d.DpiScaleX, ct = wa.Y / d.DpiScaleY, cr = wa.Right / d.DpiScaleX, cb = wa.Bottom / d.DpiScaleY;
            // R77：同屏其他贴图的边/中线作为对齐参考线（跨屏 DPI 不同，仅同屏参与，避免 DIP 混算）
            var xAny = new System.Collections.Generic.List<double>();
            var yAny = new System.Collections.Generic.List<double>();
            foreach (var p in _open)
            {
                if (ReferenceEquals(p, this)) continue;
                double pcx = (p.Left + p.Width / 2) * d.DpiScaleX, pcy = (p.Top + p.Height / 2) * d.DpiScaleY;
                if (System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)pcx, (int)pcy)).DeviceName != scr.DeviceName) continue;
                xAny.Add(p.Left); xAny.Add(p.Left + p.Width / 2); xAny.Add(p.Left + p.Width);
                yAny.Add(p.Top); yAny.Add(p.Top + p.Height / 2); yAny.Add(p.Top + p.Height);
            }
            var (l, t) = FrameCapture.Core.PinSnap.SnapToLines(Left, Top, Width, Height,
                new[] { cl, cr }, xAny, new[] { ct, cb }, yAny,
                FrameCapture.Core.PinSnap.Threshold(Width, Height));
            if (Math.Abs(l - Left) < 0.5 && Math.Abs(t - Top) < 0.5) return;
            double ol = Left, ot = Top;
            SlideTo(l, t);
            FrameCapture.Core.Trace.Log($"pin-snap ({ol:F0},{ot:F0})→({l:F0},{t:F0}) otherPins={xAny.Count / 3}");
        }
        catch { }
    }

    void SlideTo(double l, double t)
    {
        double oldL = Left, oldT = Top;
        Left = l; Top = t; // 先落本地值=目标，动画从旧值滑来，结束后回落到本地值即目标（不留动画锁）
        var dur = new Duration(TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.StateMs));
        var ease = new CircleEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(LeftProperty, new DoubleAnimation(oldL, l, dur) { EasingFunction = ease });
        BeginAnimation(TopProperty, new DoubleAnimation(oldT, t, dur) { EasingFunction = ease });
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
                try { Clipboard.SetText(text); } catch { }
                if (FrameCapture.Core.SettingsService.Shared?.Current.OcrShowResult == true)
                {
                    HideSnack();
                    OcrResultWindow.Show(text);
                }
                else
                    Flash(FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.Success, text.Length));
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
        _snack.BeginAnimation(OpacityProperty, null);
        _snack.Opacity = 1;
        _snack.Visibility = Visibility.Visible;
        _snackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _snackTimer.Tick += (_, _) =>
        {
            _snackTimer!.Stop();
            var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ExitMs)));
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
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "复制 (Ctrl+C)" };
        copy.Click += (_, _) => Copy();
        var saveAs = new MenuItem { Header = "另存为…" };
        saveAs.Click += (_, _) => SaveAs();
        var ocr = new MenuItem { Header = "识别文字" };
        ocr.Click += (_, _) => OcrFrame();
        var rotate = new MenuItem { Header = "旋转 90° (R)" };
        rotate.Click += (_, _) => Rotate();
        var refresh = new MenuItem { Header = "更新内容 (F3)" };
        refresh.Click += (_, _) => Refresh();
        // 不透明度子菜单：预设档位（Ctrl+滚轮可微调）
        var opacity = new MenuItem { Header = "不透明度" };
        foreach (var pct in new[] { 100, 75, 50, 25 })
        {
            double v = pct / 100.0;
            var mi = new MenuItem { Header = pct + "%" };
            mi.Click += (_, _) => Opacity = v;
            opacity.Items.Add(mi);
        }
        var close = new MenuItem { Header = "关闭 (Esc)" };
        close.Click += (_, _) => Close();
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
