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
    Border? _bar;       // 标题栏条带（HTCAPTION 命中区）
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

        // ---- 标题栏：取景框图标 + 标题 + 关闭；整条可拖 ----
        var bar = new Border
        {
            Height = 36,
            Background = new SolidColorBrush(Theme.CardBg),
            CornerRadius = new CornerRadius(11, 11, 0, 0),
            Padding = new Thickness(14, 0, 6, 0),
        };
        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.Children.Add(new TextBlock
        {
            Text = "\uE7A8", // 取景框（ToolCatalog 裁剪键已验证字形）
            FontFamily = new FontFamily(Fluent.IconFontChain),
            FontSize = 15,
            Foreground = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
        });
        var title = new TextBlock
        {
            Text = "FrameCapture 设置",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 1);
        barGrid.Children.Add(title);
        var closeBtn = SmallIcon("\uE711", "关闭 (Esc)");
        closeBtn.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
        Grid.SetColumn(closeBtn, 2);
        barGrid.Children.Add(closeBtn);
        bar.Child = barGrid;
        _bar = bar; _closeBtn = closeBtn;
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
        panel.Children.Add(ToggleRow("\uE71A", "常显捕获范围轮廓", "在屏幕上持续显示当前捕获范围的边框",
            _settings.Current.AlwaysShowRegionOutline, v => _settings.Mutate(s => s.AlwaysShowRegionOutline = v)));

        // ---- 录制 ----
        panel.Children.Add(Group("录制"));
        panel.Children.Add(ToggleRow("\uE815", "连按两下 = 录制（免按住）", "连按两下捕获快捷键开始录制（REC 徽标常驻），再连按两下结束进编辑器；按住录制照旧。关闭后点按截图零延迟（双击判定会把点按落地延后约 0.3 秒）",
            _settings.Current.DoubleTapRecord, v => _settings.Mutate(s => s.DoubleTapRecord = v)));
        panel.Children.Add(SliderRow("\uEC4A", "录制帧率 (FPS)", "长按录制时每秒捕获的帧数",
            AppSettings.FpsMin, AppSettings.FpsMax, 5, _settings.Current.Fps,
            v => $"{v:0} fps", v => _settings.Mutate(s => s.Fps = (int)v)));
        panel.Children.Add(SliderRow("\uE823", "长按录制最长时间", "长按超过此秒数自动停止录制",
            2, 30, 1, _settings.Current.MaxDurationMs / 1000.0,
            v => $"{v:0}s", v => _settings.Mutate(s => s.MaxDurationMs = (int)(v * 1000))));
        panel.Children.Add(ToggleRow("\uE7C4", "录制时显示范围边框", "录制期间按真实捕获范围高亮描边，只提示、不录进产物。取代 Windows 11 那条绕整屏的黄色系统捕获边框（WGC 只能整显示器捕获，系统边框必然全屏、与所选范围无关）",
            _settings.Current.ShowRecordingBorder, v => _settings.Mutate(s => s.ShowRecordingBorder = v)));

        // ---- 产物 ----
        panel.Children.Add(Group("产物"));
        panel.Children.Add(ToggleRow("\uE8B9", "自动生成 Agent 胶片条", "每次捕获后自动生成逐帧胶片条图，便于 AI 理解动作过程",
            _settings.Current.AgentStripEnabled, v => _settings.Mutate(s => s.AgentStripEnabled = v)));
        panel.Children.Add(SliderRow("\uE8FD", "胶片条抽帧数", "自动生成 Agent 胶片条时从录制帧中抽取的帧数",
            4, 24, 1, _settings.Current.AgentStripFrames,
            v => $"{v:0} 帧", v => _settings.Mutate(s => s.AgentStripFrames = (int)v)));

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
            // 标题栏 = 原生 HTCAPTION：OS 模态移动循环，位移 1:1、贴边吸附/多屏语义全免费；
            // 关闭按钮矩形让位给 WPF 命中（否则点不到）。仅标题栏 36 DIP 条带，内容区不受影响。
            System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook((h, msg, w, l, ref handled) =>
            {
                const int WM_NCHITTEST = 0x0084, HTCAPTION = 2;
                if (msg != WM_NCHITTEST) return IntPtr.Zero;
                int px = (short)((long)l & 0xFFFF), py = (short)((long)l >> 16);
                if (!Win32.GetWindowRect(h, out var rc)) return IntPtr.Zero;
                if (px < rc.Left || px >= rc.Right || py < rc.Top || py >= rc.Bottom) return IntPtr.Zero;
                var src = PresentationSource.FromVisual(this)?.CompositionTarget;
                double scale = src == null ? 1.0 : src.TransformToDevice.M11;
                if ((py - rc.Top) >= 36 * scale) return IntPtr.Zero; // 标题栏以下交回 WPF
                if (_closeBtn != null && _closeBtn.IsVisible)
                {
                    var o = _closeBtn.PointToScreen(new Point(0, 0)); // 设备像素
                    var size = new Size(_closeBtn.ActualWidth * scale, _closeBtn.ActualHeight * scale);
                    if (px >= o.X && px < o.X + size.Width && py >= o.Y && py < o.Y + size.Height) return IntPtr.Zero;
                }
                handled = true;
                return new IntPtr(HTCAPTION);
            });
        };
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

    Button SmallIcon(string glyph, string tip) =>
        FrameCapture.Core.Fluent.IconButton(glyph, tip, 32, 14);
}
