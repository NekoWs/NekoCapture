using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Services;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace FrameCapture.Overlays;

/// <summary>紧凑 Fluent 设置面板：图标 + 控件，文字全部进悬停气泡。</summary>
public sealed class SettingsWindow : Window
{
    readonly SettingsService _settings;
    Action? _hkCancel; // 当前正在捕获新热键的行的取消动作（null=无）
    TextBlock? _hkWarn; // 两热键同键时的内联警示（管理器按"捕获优先"消歧，但用户应知情）
    Button? _closeBtn;  // 关闭按钮（从 HTCAPTION 中让位）

    public SettingsWindow()
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        _settings = AppHost.Current.Settings;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        // SizeToContent 下 Show 前 ActualWidth=0，手工居中会退化成"左上角落在屏幕中心"；
        // 交给 WPF 在首帧测量后居中（R18）
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinWidth = 420;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "FrameCapture Settings";
        // 拖动改由 WM_NCHITTEST→HTCAPTION 原生处理（见 Build 的标题栏钩子）：
        // 旧「整窗背景可拖」既被 ScrollContentPresenter 吞掉命中（用户实测拖不动），
        // 又与标题栏 DragMove 双路径叠加出现位移超调，一并废除。
        FrameCapture.Core.Fluent.PopupEnter(this);
    }

    public static void ShowOrFocus()
    {
        var app = Application.Current;
        var existing = Find();
        if (existing != null) { existing.Activate(); return; }
        var w = new SettingsWindow();
        w.Build();
        w.Show();
        w.Activate();
    }

    static SettingsWindow? Find()
    {
        foreach (Window w in Application.Current.Windows)
            if (w is SettingsWindow s) return s;
        return null;
    }

    void Build()
    {
        // R59 改版：自绘标题栏（QQ 式，拖动即走+自绘关闭）+ 按类别分组 + 强调色自定义。
        // 旧版整窗"背景可拖"实际被 ScrollViewer 吞掉 → 用户实测"无法拖动"，标题栏是显式、可发现的拖拽区。
        var root = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Theme.WindowBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.5 },
        };
        var dock = new DockPanel { LastChildFill = true };

        // ---- 标题栏：取景框图标 + 标题 + 关闭；整条可拖（共享件 Fluent.TitleBar，与帮助窗同款） ----
        var (bar, closeBtn) = FrameCapture.Core.Fluent.TitleBar("\uE7A8", "FrameCapture 设置");
        closeBtn.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
        _closeBtn = closeBtn;
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);

        var panel = new StackPanel { Margin = new Thickness(16, 8, 16, 14) };

        // ---- 快捷键 ----
        panel.Children.Add(Group("快捷键"));
        panel.Children.Add(HotkeyRow("\uE7A8", "选区快捷键", "呼出区域选取窗", "scope")); // 裁剪角标=选取；E71A 留给"轮廓"行避免重复
        panel.Children.Add(HotkeyRow("\uE7F4", "捕获快捷键", "点按 = 单帧 / 长按 = 录屏", "capture"));
        _hkWarn = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD1, 0x8B, 0x00)),
            FontSize = 12,
            Margin = new Thickness(32, 2, 0, 4),
            TextWrapping = TextWrapping.Wrap,
            Text = "两个热键相同：按下时按「捕获」处理，选区快捷键不会触发。",
        };
        panel.Children.Add(_hkWarn);
        UpdateHkWarn();

        // ---- 截图 ----
        panel.Children.Add(Group("截图"));
        panel.Children.Add(SliderRow("\uE916", "点按单帧阈值", "按下到放开小于此值算单帧；0 = 全部长按",
            0, 1000, 50, _settings.Current.TapThresholdMs,
            v => $"{v / 1000.0:0.00}s", v => _settings.Mutate(s => s.TapThresholdMs = (int)v)));
        panel.Children.Add(ToggleRow("\uE722", "点按总是先显示选区", "每次点按捕获快捷键会先弹出选区，选完立即截图写入剪贴板（QQ 式手感）；长按录制不受影响",
            _settings.Current.TapAlwaysShowsRegion, v => _settings.Mutate(s => s.TapAlwaysShowsRegion = v)));
        panel.Children.Add(ToggleRow("\uE7B5", "选区自动截取", "完成选区后立即截取一帧静态图（PNG，同时复制到剪贴板）",
            _settings.Current.AutoCaptureOnSelect, v => _settings.Mutate(s => s.AutoCaptureOnSelect = v)));
        panel.Children.Add(ToggleRow("\uE740", "吸附到控件", "悬停自动吸附窗口内的具体控件：输入框、文字、图片、分组容器等（Snipaste 式，Tab 可上钻到父容器）。依赖应用无障碍接口，不支持时自动回退整窗吸附",
            _settings.Current.SnapContentArea, v => _settings.Mutate(s => s.SnapContentArea = v)));
        panel.Children.Add(ToggleRow("\uE7C4", "显示上次选区", "按下选区快捷键后，用虚线框显示上次的选区位置（Enter 可直接确认重选同区域）；关闭后每次都是干净屏",
            _settings.Current.ShowLastRegion, v => _settings.Mutate(s => s.ShowLastRegion = v)));
        panel.Children.Add(ToggleRow("\uE721", "选区放大镜", "拖框或悬停选区时，光标旁显示高倍像素放大镜、坐标/尺寸读数与光标下像素颜色（按 C 复制颜色，格式见下），便于像素级精确选区/取色（QQ/Snipaste 式）；关闭可省一点开销",
            _settings.Current.PickerMagnifier, v => _settings.Mutate(s => s.PickerMagnifier = v)));
        panel.Children.Add(ColorFormatRow());
        panel.Children.Add(ToggleRow("\uE71A", "常显捕获范围轮廓", "在屏幕上持续显示当前捕获范围的边框",
            _settings.Current.AlwaysShowRegionOutline, v => _settings.Mutate(s => s.AlwaysShowRegionOutline = v)));
        panel.Children.Add(AfterCaptureRow());
        panel.Children.Add(CaptureSoundRow());
        panel.Children.Add(DelayRow());

        // ---- 录制 ----
        panel.Children.Add(Group("录制"));
        panel.Children.Add(ToggleRow("\uE815", "连按两下 = 录制（免按住）", "连按两下捕获快捷键开始录制（REC 徽标常驻），再连按两下结束进编辑器；按住录制照旧。关闭后点按截图零延迟（双击判定会把点按落地延后约 0.3 秒）",
            _settings.Current.DoubleTapRecord, v => _settings.Mutate(s => s.DoubleTapRecord = v)));
        panel.Children.Add(SliderRow("\uEC4A", "录制帧率 (FPS)", "录制时每秒捕获的帧数",
            AppSettings.FpsMin, AppSettings.FpsMax, 5, _settings.Current.Fps,
            v => $"{v:0} fps", v => _settings.Mutate(s => s.Fps = (int)v)));
        int hz = Core.DisplayRefresh.RateOf(FrameCapture.Capture.Monitors.All().FirstOrDefault(m => m.IsPrimary)?.DeviceName);
        panel.Children.Add(ToggleRow("\uE7F4", "跟随显示器刷新率录满",
            (hz > 0 ? $"开启后录制帧率自动取当前录制显示器的刷新率（本机主屏检测到 {hz}Hz → 录满 {hz}fps），忽略上面的手动帧率。"
                    : "开启后录制帧率自动取当前录制显示器的刷新率（本机暂未检测到主屏刷新率，录制时按实际显示器实时检测）。")
            + "高刷屏（144/165/240Hz）录满才丝滑；MP4/WebM 可录满，GIF 受格式封顶约 50fps",
            _settings.Current.FollowMonitorRefresh, v => _settings.Mutate(s => s.FollowMonitorRefresh = v)));
        panel.Children.Add(RecordFormatRow());
        // R61：长按录制时长上限已移除——录到用户停为止（帧落盘，不再受内存/时长限制）。
        // 唯一护栏是磁盘剩余空间（<1GB 自动停并保留已录内容），无需再暴露数值。
        panel.Children.Add(ToggleRow("\uE7C4", "录制时显示范围边框", "录制期间按真实捕获范围高亮描边，只提示、不录进产物。取代 Windows 11 那条绕整屏的黄色系统捕获边框（WGC 只能整显示器捕获，系统边框必然全屏、与所选范围无关）",
            _settings.Current.ShowRecordingBorder, v => _settings.Mutate(s => s.ShowRecordingBorder = v)));

        // ---- 产物 ----
        panel.Children.Add(Group("产物"));
        panel.Children.Add(ToggleRow("\uE8B9", "自动生成 Agent 胶片条", "每次捕获后自动生成逐帧胶片条图，便于 AI 理解动作过程",
            _settings.Current.AgentStripEnabled, v => _settings.Mutate(s => s.AgentStripEnabled = v)));
        panel.Children.Add(SliderRow("\uE8FD", "胶片条抽帧数", "自动生成 Agent 胶片条时从录制帧中抽取的帧数",
            4, 24, 1, _settings.Current.AgentStripFrames,
            v => $"{v:0} 帧", v => _settings.Mutate(s => s.AgentStripFrames = (int)v)));
        panel.Children.Add(ToggleRow("\uE74E", "自动保存副本", "捕获完成后，除复制到剪贴板外，再存一份文件到本地目录（截图存 PNG、录制存 GIF）。写盘失败不影响剪贴板，捕获永远成功",
            _settings.Current.AutoSaveCopy, v => _settings.Mutate(s => s.AutoSaveCopy = v)));
        panel.Children.Add(PathRow());

        // ---- 编辑器 ----
        panel.Children.Add(Group("编辑器"));
        panel.Children.Add(ToggleRow("\uEDD7", "记住上次的工具/颜色/粗细", "关闭编辑器时记住你最后用的标注工具、颜色和线宽，下次打开直接是它们，省去每次重选。关闭后每次打开都回到默认（浏览 / 红 / 中）",
            _settings.Current.RememberEditorTools, v => _settings.Mutate(s => s.RememberEditorTools = v)));
        panel.Children.Add(ToggleRow("\uE8C1", "识别文字后显示结果窗", "「识别文字」成功后，除写剪贴板外再弹出结果窗：可查看/编辑识别文本、部分选中复制、一键复制全部。关闭则仅写剪贴板 + 轻提示（更快）",
            _settings.Current.OcrShowResult, v => _settings.Mutate(s => s.OcrShowResult = v)));

        // ---- 贴图 ----
        panel.Children.Add(Group("贴图"));
        panel.Children.Add(ToggleRow("\uE718", "拖动贴图自动磁边", "拖动贴图松手时自动吸附对齐：贴近所在屏幕边缘则贴合停靠到屏幕四边/四角，贴近同屏其他贴图的边或中线则与之对齐（便于整齐排布多张贴图，Snipaste 式手感）。关闭则松手停在原地",
            _settings.Current.PinSnapEdges, v => _settings.Mutate(s => s.PinSnapEdges = v)));

        // ---- 外观 ----
        panel.Children.Add(Group("外观"));
        panel.Children.Add(ThemeRow());
        panel.Children.Add(AccentRow());

        // ---- 系统 ----
        panel.Children.Add(Group("系统"));
        panel.Children.Add(ToggleRow("\uE7E8", "开机自启", "登录 Windows 时自动启动 FrameCapture",
            _settings.Current.RunAtStartup, v =>
            {
                _settings.Mutate(s => s.RunAtStartup = v);
                try { StartupService.Apply(v); } catch { }
            }));

        // 小屏防护：工作区更矮时滚动而非裁掉底部（SizeToContent 受 MaxHeight 封顶）
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 48);
        var scroller = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        DockPanel.SetDock(scroller, Dock.Bottom); // LastChildFill 仍填满剩余
        dock.Children.Add(scroller);
        root.Child = dock;
        Content = root;

        // 窗级 Esc 先于热键行的 PreviewKeyDown（tunnel 自上而下）：捕获新键期间必须放行，
        // 否则"Esc 取消捕获"会变成"关掉整个设置窗"（R23 修）；F1 同理（录键中 F1 是新热键候选）
        PreviewKeyDown += (_, e) =>
        {
            if (_hkCancel != null) return;
            if (e.Key == Key.Escape) FrameCapture.Core.Fluent.CloseWithFade(this);
            else if (e.Key == Key.F1) { FrameCapture.Overlays.HelpWindow.ShowOrFocus(); e.Handled = true; }
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(hwnd);
            Win32.SetWindowTextW(hwnd, "FrameCapture Settings");
        };
        // 标题栏 = 原生 HTCAPTION（共享件，与帮助窗同款）：位移 1:1、贴边吸附/多屏语义全免费；关闭按钮让位给 WPF 命中。
        FrameCapture.Core.Fluent.InstallCaptionDrag(this, _closeBtn);
        Closed += (_, _) => AppHost.Current.InstallHotkeys();
    }

    /// <summary>分组标题：小字暗色，替代旧版一整屏 Rule() 平铺（R59 按类别分组）。</summary>
    static FrameworkElement Group(string name) => new TextBlock
    {
        Text = name,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Theme.TextDim),
        Margin = new Thickness(2, 10, 0, 2),
    };

    /// <summary>主题/强调色变更后原地重建：同位交叉淡入新窗（旧淡出与新淡入非对称节拍）。</summary>
    void RebuildInPlace()
    {
        var oldw = this;
        var nw = new SettingsWindow();
        nw.Build();
        nw.Left = oldw.Left; nw.Top = oldw.Top;
        nw.Opacity = 0;
        nw.Show();
        var fo = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ExitMs + 20));
        fo.Completed += (_, _) => oldw.Close();
        oldw.BeginAnimation(OpacityProperty, fo);
        nw.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ThemeMs)));
    }

    // ---------- 强调色（R59：设置里此前无处可改，Core 早已支持） ----------

    Popup? _accentPop;
    ColorPickView? _accentView;

    FrameworkElement AccentRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 色片列
        g.Children.Add(Icon("\uE790", "强调色：全应用高亮色（编辑器选中、开关、滑杆、进度）"));
        var lbl = new TextBlock
        {
            Text = "强调色",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var chip = new Border
        {
            Width = 34,
            Height = 20,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Theme.Accent),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = "点击自定义强调色（与编辑器调色板同款）；右键恢复跟随系统",
        };
        Grid.SetColumn(chip, 2);
        void Open()
        {
            if (_accentPop == null)
            {
                _accentView = new ColorPickView();
                Color previewed = Theme.Accent;
                _accentView.Changed += c =>
                {
                    // 拖动中只预览（改静态色+全局样式），松板才落盘重建——避免每像素重建窗口
                    previewed = c;
                    Theme.Accent = Color.FromRgb(c.R, c.G, c.B);
                    FrameCapture.Core.Fluent.ApplyGlobalStyles();
                };
                _accentPop = new Popup
                {
                    Child = new Border
                    {
                        Background = new SolidColorBrush(Theme.PanelBg),
                        BorderBrush = new SolidColorBrush(Theme.Stroke),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(12, 10, 12, 10),
                        Child = _accentView,
                    },
                    Placement = PlacementMode.Top,
                    AllowsTransparency = true,
                    PopupAnimation = PopupAnimation.Fade,
                };
                _accentPop.Closed += (_, _) =>
                {
                    var c = _accentView!.Color;
                    var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                    _settings.Mutate(s => s.Accent = hex);
                    Theme.Apply(_settings.Current.Theme, hex);
                    FrameCapture.Core.Fluent.ApplyGlobalStyles();
                    RebuildInPlace(); // 本窗静态刷色需重建才跟上
                };
            }
            _accentView!.Color = Theme.Accent;
            _accentPop.PlacementTarget = chip;
            FrameCapture.Core.Fluent.OpenPopSticky(_accentPop, this);
        }
        chip.MouseLeftButtonDown += (_, e) => { Open(); e.Handled = true; };
        chip.MouseRightButtonUp += (_, _) =>
        {
            _settings.Mutate(s => s.Accent = "");
            Theme.Apply(_settings.Current.Theme, "");
            FrameCapture.Core.Fluent.ApplyGlobalStyles();
            RebuildInPlace();
        };
        chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Open(); e.Handled = true; } };
        g.Children.Add(chip);
        return g;
    }

    // ---------- 快捷键捕获框 ----------

    FrameworkElement HotkeyRow(string glyph, string label, string tip, string which)
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 键位盒列
        g.Children.Add(Icon(glyph, tip));
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var box = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Theme.Hover),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 6, 14, 6),
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand,
            ToolTip = "点击后按下新快捷键（Esc 取消）",
        };
        Grid.SetColumn(box, 2);
        var tb = new TextBlock
        {
            Text = which == "scope" ? _settings.Current.ScopeHotkey : _settings.Current.CaptureHotkey,
            Foreground = new SolidColorBrush(Theme.Text),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        box.Child = tb;
        var hbBase = (SolidColorBrush)box.Background;
        box.MouseEnter += (_, _) =>
        {
            var bgh = new SolidColorBrush(hbBase.Color); box.Background = bgh;
            bgh.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty,
                new System.Windows.Media.Animation.ColorAnimation(Theme.Press, new System.Windows.Duration(TimeSpan.FromMilliseconds(120))));
        };
        box.MouseLeave += (_, _) => { box.Background = new SolidColorBrush(hbBase.Color); };
        g.Children.Add(box);

        bool capturing = false;
        Action cancel = null!;
        cancel = () => { if (!capturing) return; capturing = false; if (ReferenceEquals(_hkCancel, cancel)) _hkCancel = null; Refresh(); UpdateHkWarn(); };
        box.MouseLeftButtonDown += (_, e) => { Begin(); e.Handled = true; };
        void Begin()
        {
            _hkCancel?.Invoke();           // 同一时刻只允许一行处于捕获态
            capturing = true; _hkCancel = cancel;
            tb.Text = "按下新键…";
            tb.Foreground = new SolidColorBrush(Theme.TextDim);
            box.BorderBrush = new SolidColorBrush(Theme.Accent);
            box.Focus();                   // 键盘焦点留在本行，捕获键直达窗级处理器
        }
        // R29：键盘可达——Tab 聚焦、空格/Enter 进入捕获（与点击同一路径）
        box.Focusable = true;
        box.KeyDown += (_, e) =>
        {
            if (!capturing && e.Key is Key.Space or Key.Enter) { Begin(); e.Handled = true; }
        };
        box.GotKeyboardFocus += (_, _) => { if (!capturing) box.BorderBrush = new SolidColorBrush(Theme.Accent); };
        box.LostKeyboardFocus += (_, _) => { if (!capturing) box.BorderBrush = new SolidColorBrush(Theme.Stroke); };
        PreviewKeyDown += (_, e) =>
        {
            if (!capturing) return;
            e.Handled = true;
            if (e.Key == Key.Escape) { cancel(); return; }
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return; // 等主键
            var mods = e.KeyboardDevice.Modifiers;
            if (e.Key is Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 or Key.F6
                or Key.F7 or Key.F8 or Key.F9 or Key.F10 or Key.F11 or Key.F12)
            {
                mods = ModifierKeys.None; // F 键允许裸按
            }
            else if (mods == ModifierKeys.None) return; // 必须带修饰键

            string text = BuildSpec(e.Key, mods);
            if (HotkeySpec.Parse(text) == null) return;
            if (which == "scope") _settings.Mutate(s => s.ScopeHotkey = text);
            else _settings.Mutate(s => s.CaptureHotkey = text);
            cancel();
        };

        void Refresh()
        {
            tb.Foreground = new SolidColorBrush(Theme.Text);
            tb.Text = which == "scope" ? _settings.Current.ScopeHotkey : _settings.Current.CaptureHotkey;
            box.BorderBrush = new SolidColorBrush(Theme.Stroke);
        }
        return g;
    }

    void UpdateHkWarn()
    {
        if (_hkWarn == null) return;
        var s = _settings.Current;
        _hkWarn.Visibility = HotkeySpec.Parse(s.ScopeHotkey) != null && s.ScopeHotkey == s.CaptureHotkey
            ? Visibility.Visible : Visibility.Collapsed;
    }

    static string BuildSpec(Key key, ModifierKeys mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyNameOf(key));
        return string.Join("+", parts);
    }

    static string KeyNameOf(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.F1 && key <= Key.F24) return key.ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "NumPad" + (key - Key.NumPad0);
        return key switch
        {
            Key.Space => "Space",
            Key.Enter => "Return",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.PageUp => "Prior",
            Key.PageDown => "Next",
            Key.Home => "Home",
            Key.End => "End",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Back => "Back",
            Key.OemPlus => "Oemplus",
            Key.OemMinus => "OemMinus",
            Key.OemQuestion => "Oemquestion",
            Key.OemPeriod => "OemPeriod",
            Key.OemComma => "Oemcomma",
            _ => key.ToString(),
        };
    }

    // ---------- 滑杆行 ----------

    FrameworkElement SliderRow(string glyph, string label, string tip, double min, double max, double tick, double val,
        Func<double, string> fmt, Action<double> onChange)
    {
        // 两行式：首行 [图标][名称][当前值]，次行整宽滑杆（名称常显，不依赖悬停）
        var outer = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Icon(glyph, tip));
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(lbl, 1);
        head.Children.Add(lbl);
        var hud = new TextBlock
        {
            Text = fmt(val),
            FontSize = 11,
            Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(hud, 2);
        head.Children.Add(hud);
        var sl = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = tick,
            IsSnapToTickEnabled = true,
            Value = val,
            Foreground = new SolidColorBrush(Theme.Accent),
            Margin = new Thickness(4, 4, 0, 0),
            // 吸附开启时步长必须=档位：默认 SmallChange=1 会被吸回原档，方向键看似失灵（R29）
            SmallChange = tick,
            LargeChange = tick * 2,
        };
        FrameCapture.Core.Fluent.StyleSlider(sl);
        sl.ValueChanged += (_, e) =>
        {
            hud.Text = fmt(e.NewValue);
            onChange(e.NewValue);
        };
        outer.Children.Add(head);
        outer.Children.Add(sl);
        return outer;
    }

    // ---------- 开关行 ----------

    FrameworkElement ToggleRow(string glyph, string label, string tip, bool initial, Action<bool> onChange)
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 开关列
        g.Children.Add(Icon(glyph, tip));
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var track = new Border
        {
            Width = 40,
            Height = 20,
            Margin = new Thickness(12, 0, 0, 0),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(initial ? Theme.Accent : Theme.TrackBg),
            BorderBrush = new SolidColorBrush(initial ? Theme.Accent : Theme.Stroke),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Focusable = true, // R29：键盘可达——Tab 聚焦、空格/Enter 切换
        };
        var knob = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(initial ? Color.FromRgb(0, 0, 0) : Theme.Text),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(4, 0, 4, 0),
            RenderTransform = new System.Windows.Media.TranslateTransform(initial ? 20 : 0, 0),
        };
        Grid.SetColumn(track, 2);
        track.Child = knob;
        bool on = initial;
        void Flip()
        {
            on = !on;
            FrameCapture.Core.Fluent.SlideToggle(knob, track, on, 20);
            onChange(on);
        }
        track.MouseLeftButtonDown += (_, e) => { Flip(); e.Handled = true; };
        track.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter) { Flip(); e.Handled = true; }
        };
        track.GotKeyboardFocus += (_, _) => { track.BorderBrush = new SolidColorBrush(Theme.Accent); };
        track.LostKeyboardFocus += (_, _) => { track.BorderBrush = new SolidColorBrush(on ? Theme.Accent : Theme.Stroke); };
        track.MouseEnter += (_, _) =>
        {
            if (track.BorderBrush is System.Windows.Media.SolidColorBrush bb && bb.CanFreeze)
            {
                var h = new System.Windows.Media.SolidColorBrush(bb.Color);
                track.BorderBrush = h;
                h.Color = System.Windows.Media.Color.FromArgb((byte)Math.Min(255, bb.Color.A + 0x60), bb.Color.R, bb.Color.G, bb.Color.B);
            }
        };
        track.MouseLeave += (_, _) =>
        {
            if (track.BorderBrush is System.Windows.Media.SolidColorBrush bb && bb.CanFreeze)
            {
                var h = new System.Windows.Media.SolidColorBrush(bb.Color);
                track.BorderBrush = h;
                h.Color = on ? Theme.Accent : Theme.Stroke;
            }
        };
        g.Children.Add(track);
        return g;
    }

    /// <summary>自动保存位置行（R63）：整行可点，弹系统文件夹选择器；空路径显示默认「图片\FrameCapture」。</summary>
    FrameworkElement PathRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 更改列
        g.Children.Add(Icon("\uE838", "选择自动保存副本的文件夹"));
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand, Background = Brushes.Transparent,
            ToolTip = "点击选择保存文件夹",
        };
        var lbl = new TextBlock { Text = "保存位置", FontSize = 13, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center };
        var path = new TextBlock
        {
            FontSize = 12, Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 250,
        };
        void Refresh()
        {
            var p = _settings.Current.AutoSavePath;
            path.Text = string.IsNullOrWhiteSpace(p) ? @"图片\FrameCapture（默认）" : p;
        }
        Refresh();
        stack.Children.Add(lbl);
        stack.Children.Add(path);
        Grid.SetColumn(stack, 1);
        g.Children.Add(stack);
        var browse = new TextBlock
        {
            Text = "更改", FontSize = 12, Foreground = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Margin = new Thickness(12, 0, 6, 0),
        };
        Grid.SetColumn(browse, 2);
        g.Children.Add(browse);
        void Open()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            { Description = "选择自动保存副本的文件夹", UseDescriptionForTitle = true };
            var cur = _settings.Current.AutoSavePath;
            if (!string.IsNullOrWhiteSpace(cur) && System.IO.Directory.Exists(cur)) dlg.SelectedPath = cur;
            // 设置窗 Topmost：对话框须挂到本窗句柄，否则可能盖在设置窗之下
            var owner = new Win32.WndOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            if (dlg.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
                _settings.Mutate(s => s.AutoSavePath = dlg.SelectedPath);
            Refresh();
        }
        stack.MouseLeftButtonDown += (_, e) => { Open(); e.Handled = true; };
        browse.MouseLeftButtonDown += (_, e) => { Open(); e.Handled = true; };
        return g;
    }

    // ---------- 主题 ----------

    FrameworkElement ThemeRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 芯片列
        g.Children.Add(Icon("\uE793", "主题（跟随系统 / 浅色 / 深色）"));
        var lbl = new TextBlock
        {
            Text = "外观主题",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        string cur = _settings.Current.Theme;
        foreach (var (id, glyph, tip) in new[]
                 { ("system", "\uE977", "跟随系统"), ("light", "\uE706", "浅色"), ("dark", "\uE708", "深色") })
        {
            string captured = id;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(captured == cur ? Theme.Accent : Theme.Hover),
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(captured == cur ? Color.FromRgb(0, 0, 0) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            var localChip = chip;
            var localText = (TextBlock)chip.Child;
            Color chipBase = captured == cur ? Theme.Accent : Theme.Hover;
            Color chipHover = captured == cur
                ? System.Windows.Media.Color.FromArgb(0xFF, 0x6A, 0xD0, 0xFF)
                : (Theme.IsDark ? System.Windows.Media.Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF) : System.Windows.Media.Color.FromArgb(0x4A, 0x00, 0x00, 0x00));
            localChip.MouseEnter += (_, _) =>
            {
                if (localChip.Background is System.Windows.Media.SolidColorBrush bg0)
                {
                    var anim = new System.Windows.Media.Animation.ColorAnimation(chipHover, new Duration(TimeSpan.FromMilliseconds(120)));
                    var holder = new System.Windows.Media.SolidColorBrush(bg0.Color);
                    localChip.Background = holder;
                    holder.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, anim);
                }
            };
            localChip.MouseLeave += (_, _) =>
            {
                if (localChip.Background is System.Windows.Media.SolidColorBrush bg1)
                {
                    var anim = new System.Windows.Media.Animation.ColorAnimation(chipBase, new Duration(TimeSpan.FromMilliseconds(160)));
                    var holder = new System.Windows.Media.SolidColorBrush(bg1.Color);
                    localChip.Background = holder;
                    holder.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, anim);
                }
            };
            void ApplyTheme()
            {
                _settings.Mutate(s => s.Theme = captured);
                Theme.Apply(captured, _settings.Current.Accent);
                FrameCapture.Core.Fluent.ApplyGlobalStyles();
                // 无缝过渡：旧窗淡出的同时同位置淡入新窗（Theme.Changed 已让其他窗口即时换色）
                RebuildInPlace();
            }
            chip.MouseLeftButtonDown += (_, e) => { ApplyTheme(); e.Handled = true; };
            // R29：键盘可达——Tab 聚焦、空格/Enter 切换主题（与点击同一路径）
            chip.Focusable = true;
            chip.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Space or Key.Enter) { ApplyTheme(); e.Handled = true; }
            };
            chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = new SolidColorBrush(Theme.Accent); chip.BorderThickness = new Thickness(1); };
            chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
            chips.Children.Add(chip);
        }
        g.Children.Add(chips);
        return g;
    }

    // 截图后行为三选项（R83）：文字芯片比图标语义更清楚；点击就地更新选中态，不整窗重建
    FrameworkElement AfterCaptureRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 芯片列
        g.Children.Add(Icon("\uE7B5", "截图完成后做什么：弹提示 / 直接进编辑器 / 只复制"));
        var lbl = new TextBlock
        {
            Text = "截图后",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        string cur = _settings.Current.AfterCapture;
        var opts = new[]
        {
            ("toast", "弹提示", "复制后弹缩略浮窗，点一下或按 Enter 进编辑器标注（默认，兼顾确认与标注）"),
            ("editor", "进编辑器", "复制后直接打开标注编辑器（QQ 式，省一步点击）"),
            ("copy", "只复制", "只复制到剪贴板，不弹窗、不开编辑器（最快，范围轮廓闪一下作反馈）"),
        };
        var chipList = new System.Collections.Generic.List<(Border chip, string id)>();
        foreach (var (id, text, tip) in opts)
        {
            string captured = id;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(captured == cur ? Theme.Accent : Theme.Hover),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(captured == cur ? Color.FromRgb(0, 0, 0) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            chipList.Add((chip, id));
            void Apply()
            {
                _settings.Mutate(s => s.AfterCapture = captured);
                foreach (var (c, cid) in chipList)
                {
                    bool sel = cid == captured;
                    c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
                    if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Color.FromRgb(0, 0, 0) : Theme.Text);
                }
            }
            chip.MouseLeftButtonDown += (_, e) => { Apply(); e.Handled = true; };
            chip.Focusable = true;
            chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Apply(); e.Handled = true; } };
            chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = new SolidColorBrush(Theme.Accent); chip.BorderThickness = new Thickness(1); };
            chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
            chips.Children.Add(chip);
        }
        g.Children.Add(chips);
        return g;
    }

    // 取色复制格式（R89）：选区放大镜按 C 复制颜色时的文本形式，HEX/RGB/HSL 三选（QQ 只有 HEX）
    FrameworkElement RecordFormatRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 芯片列
        g.Children.Add(Icon("\uE786", "录制完成后编辑器「导出录制」默认用的格式。MP4/WebM 需本机装有 ffmpeg（自动探测 PATH 与常见安装目录）；未装则视频项会提示安装，GIF 始终可用"));
        var lbl = new TextBlock
        {
            Text = "录制格式",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        var cur = _settings.Current.RecordFormat;
        bool hasFf = FrameCapture.Encoding.FfmpegLocator.Find() != null;
        string ffNote = hasFf ? "（已检测到 ffmpeg）" : "（未检测到 ffmpeg，装 FFmpeg 后可选）";
        var opts = new (RecordFormat id, string text, string tip)[]
        {
            (RecordFormat.Gif, "GIF", "通用动图，任何地方都能放；受格式封顶约 50fps、体积大"),
            (RecordFormat.Mp4, "MP4", "兼容性最好的视频格式（libx264），微信/浏览器/剪辑软件通吃 " + ffNote),
            (RecordFormat.WebM, "WebM", "开源视频格式（VP9），同画质体积更小，网页/上传首选 " + ffNote),
        };
        var chipList = new System.Collections.Generic.List<(Border chip, RecordFormat id)>();
        foreach (var (id, text, tip) in opts)
        {
            RecordFormat captured = id;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(captured == cur ? Theme.Accent : Theme.Hover),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(captured == cur ? Color.FromRgb(0, 0, 0) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            chipList.Add((chip, id));
            void Apply()
            {
                _settings.Mutate(s => s.RecordFormat = captured);
                foreach (var (c, cid) in chipList)
                {
                    bool sel = cid == captured;
                    c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
                    if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Color.FromRgb(0, 0, 0) : Theme.Text);
                }
            }
            chip.MouseLeftButtonDown += (_, e) => { Apply(); e.Handled = true; };
            chip.Focusable = true;
            chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Apply(); e.Handled = true; } };
            chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = new SolidColorBrush(Theme.Accent); chip.BorderThickness = new Thickness(1); };
            chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
            chips.Children.Add(chip);
        }
        g.Children.Add(chips);
        return g;
    }

    FrameworkElement ColorFormatRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 芯片列
        g.Children.Add(Icon("\uE790", "选区放大镜按 C 复制颜色时的文本形式：#RRGGBB / rgb(r, g, b) / hsl(h, s%, l%)"));
        var lbl = new TextBlock
        {
            Text = "取色格式",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        string cur = _settings.Current.ColorCopyFormat;
        var opts = new[]
        {
            ("hex", "HEX", "#RRGGBB（QQ 同款，通用）"),
            ("rgb", "RGB", "rgb(r, g, b)（直接贴进 CSS）"),
            ("hsl", "HSL", "hsl(h, s%, l%)（调色/设计常用）"),
        };
        var chipList = new System.Collections.Generic.List<(Border chip, string id)>();
        foreach (var (id, text, tip) in opts)
        {
            string captured = id;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(captured == cur ? Theme.Accent : Theme.Hover),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(captured == cur ? Color.FromRgb(0, 0, 0) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            chipList.Add((chip, id));
            void Apply()
            {
                _settings.Mutate(s => s.ColorCopyFormat = captured);
                foreach (var (c, cid) in chipList)
                {
                    bool sel = cid == captured;
                    c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
                    if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Color.FromRgb(0, 0, 0) : Theme.Text);
                }
            }
            chip.MouseLeftButtonDown += (_, e) => { Apply(); e.Handled = true; };
            chip.Focusable = true;
            chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Apply(); e.Handled = true; } };
            chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = new SolidColorBrush(Theme.Accent); chip.BorderThickness = new Thickness(1); };
            chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
            chips.Children.Add(chip);
        }
        g.Children.Add(chips);
        return g;
    }
    FrameworkElement DelayRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 芯片列
        g.Children.Add(Icon("\uE916", "托盘「延时截图」触发后倒数这么久再截当前范围，留时间打开右键菜单/悬停提示/下拉框这类一动就消失的界面"));
        var lbl = new TextBlock
        {
            Text = "延时截图",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        int cur = _settings.Current.DelayCaptureSeconds;
        var opts = new (int secs, string text, string tip)[]
        {
            (0, "关", "不延时，触发即截"),
            (1, "1秒", "快速：够移一下鼠标"),
            (3, "3秒", "常用：够打开一个菜单"),
            (5, "5秒", "从容：够展开下拉/悬停"),
            (10, "10秒", "充裕：够摆好几个界面元素"),
        };
        var chipList = new System.Collections.Generic.List<(Border chip, int secs)>();
        foreach (var (secs, text, tip) in opts)
        {
            int captured = secs;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(captured == cur ? Theme.Accent : Theme.Hover),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(captured == cur ? Color.FromRgb(0, 0, 0) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            chipList.Add((chip, secs));
            void Apply()
            {
                _settings.Mutate(s => s.DelayCaptureSeconds = captured);
                foreach (var (c, cs) in chipList)
                {
                    bool sel = cs == captured;
                    c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
                    if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Color.FromRgb(0, 0, 0) : Theme.Text);
                }
            }
            chip.MouseLeftButtonDown += (_, e) => { Apply(); e.Handled = true; };
            chip.Focusable = true;
            chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Apply(); e.Handled = true; } };
            chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = new SolidColorBrush(Theme.Accent); chip.BorderThickness = new Thickness(1); };
            chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
            chips.Children.Add(chip);
        }
        g.Children.Add(chips);
        return g;
    }
    FrameworkElement CaptureSoundRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 控件列
        g.Children.Add(Icon("\uE767", "静态截图完成时放一声柔和短促的「嘀」作听觉确认（QQ 招牌反馈）；默认关，可点「试听」先听"));
        var lbl = new TextBlock
        {
            Text = "截图提示音",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 2);

        // 试听：点一下立刻放一声，让用户判断这个音量/音色能否接受
        var preview = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 12, 0),
            Background = new SolidColorBrush(Theme.Hover),
            Child = new TextBlock { Text = "试听", FontSize = 12.5, Foreground = new SolidColorBrush(Theme.Text) },
            Cursor = Cursors.Hand,
            ToolTip = "点一下试听这声提示音（无论开关是否开启都可试听）",
            Focusable = true,
        };
        void DoPreview() => FrameCapture.Core.CaptureSound.Play();
        preview.MouseLeftButtonDown += (_, e) => { DoPreview(); e.Handled = true; };
        preview.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { DoPreview(); e.Handled = true; } };
        preview.MouseEnter += (_, _) => preview.Background = new SolidColorBrush(Theme.Press);
        preview.MouseLeave += (_, _) => preview.Background = new SolidColorBrush(Theme.Hover);
        preview.GotKeyboardFocus += (_, _) => { preview.BorderBrush = new SolidColorBrush(Theme.Accent); preview.BorderThickness = new Thickness(1); };
        preview.LostKeyboardFocus += (_, _) => preview.BorderThickness = new Thickness(0);
        right.Children.Add(preview);

        // 开关（与 ToggleRow 同款 track/knob）
        bool on = _settings.Current.CaptureSound;
        var track = new Border
        {
            Width = 40, Height = 20, CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(on ? Theme.Accent : Theme.TrackBg),
            BorderBrush = new SolidColorBrush(on ? Theme.Accent : Theme.Stroke),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
            ToolTip = "开启后每次静态截图完成放一声柔和提示音", Focusable = true,
        };
        var knob = new Ellipse
        {
            Width = 12, Height = 12, Fill = new SolidColorBrush(on ? Color.FromRgb(0, 0, 0) : Theme.Text),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 4, 0),
            RenderTransform = new System.Windows.Media.TranslateTransform(on ? 20 : 0, 0),
        };
        track.Child = knob;
        void Flip() { on = !on; FrameCapture.Core.Fluent.SlideToggle(knob, track, on, 20); _settings.Mutate(s => s.CaptureSound = on); }
        track.MouseLeftButtonDown += (_, e) => { Flip(); e.Handled = true; };
        track.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Flip(); e.Handled = true; } };
        track.GotKeyboardFocus += (_, _) => track.BorderBrush = new SolidColorBrush(Theme.Accent);
        track.LostKeyboardFocus += (_, _) => track.BorderBrush = new SolidColorBrush(on ? Theme.Accent : Theme.Stroke);
        right.Children.Add(track);

        g.Children.Add(right);
        return g;
    }

    // ---------- 小部件 ----------

    static Grid Row()
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    static TextBlock Icon(string glyph, string tip) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
        FontSize = 16,
        Foreground = new SolidColorBrush(Theme.Text),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 14, 0),
        ToolTip = tip,
    };
}
