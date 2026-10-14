using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>紧凑 Fluent 设置面板：图标 + 控件，文字全部进悬停气泡。</summary>
public sealed partial class SettingsWindow : Window
{
    readonly SettingsService _settings;
    int _cat;           // 当前分页索引
    int _restoreCat;    // 重建（改主题/强调色）时要恢复的分页索引

    /// <summary>
    /// 设置窗的窗口属性：无边框 + WindowChrome 原生拖窗区与缩放边框、固定起始尺寸、不进任务栏、不总在最前。
    /// 必须在 UI 线程调用；此处只取 <c>SettingsService</c> 引用，内容装配在 <see cref="Build"/>。
    /// </summary>
    public SettingsWindow()
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        InputMethod.SetIsInputMethodEnabled(this, false);
        _settings = AppHost.Current.Settings;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;   // 可拖边缩放（WindowChrome 的 6px 缩放边框）
        // WindowChrome 原生缩放边框 + 原生圆角/阴影（与编辑器同款）；不用 AllowsTransparency——整窗会被推到软件渲染。
        Fluent.BorderlessNative(this, resizeBorder: 6);
        // 标题条 = WindowChrome 原生 CaptionHeight 拖窗区（比 WM_NCHITTEST 钩子更稳，避免与 WindowChrome 命中测试抢序）；
        // 关闭按钮在 Build 里标 IsHitTestVisibleInChrome 让位给 WPF 命中。
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null) chrome.CaptionHeight = Fluent.TitleBarHeight;
        Background = new SolidColorBrush(Theme.WindowBg);
        SizeToContent = SizeToContent.Manual;   // 分页后每页很短，固定舒适尺寸起步，可拖边缩放
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 760; Height = Math.Min(660, SystemParameters.WorkArea.Height - 120);
        MinWidth = 680; MinHeight = 460;
        ShowInTaskbar = false;
        Topmost = false;   // 普通工具窗：不该压住用户正在截/录的画面
        Title = "FrameCapture Settings";
        // 拖窗区只有标题条那一条（CaptionHeight = Fluent.TitleBarHeight）：整窗背景不承担拖拽——
        // 整窗拖拽会被 ScrollContentPresenter 吞掉命中（滚动画布里拖不动），还会与标题栏的拖拽双路径叠加出现位移超调。
        Fluent.PopupEnter(this);
    }

    /// <summary>
    /// 打开设置窗；已有实例时只激活它，全进程同时最多一个设置窗。
    /// 必须在 UI 线程调用（遍历 <c>Application.Current.Windows</c> 找现存实例）。
    /// </summary>
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
        // 自绘标题栏（QQ 式：拖动即走 + 自绘关闭）+ 按类别分页 + 强调色自定义。
        // 标题栏是显式、可发现的拖拽区，正文区不承担拖拽。
        var root = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Theme.WindowBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            // 阴影交给 WindowChrome 的原生 DWM 阴影（自绘 DropShadow 与原生缩放窗叠加会重影、发虚）
        };
        var dock = new DockPanel { LastChildFill = true };

        #region 标题栏：图标 + 标题 + 关闭（共享件 Fluent.TitleBar）
        var (bar, closeBtn) = Fluent.TitleBar("\uE7A8", "设置");
        closeBtn.Click += (_, _) => Fluent.CloseWithFade(this);
        // 关闭按钮落在原生 CaptionHeight 拖窗区内，须显式让位给 WPF 命中，否则点它会拖窗而非关闭
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        #endregion

        #region 类别容器：每类一个 StackPanel，一次只显示一个
        var cats = new List<(string Name, string Glyph, StackPanel Body)>();
        StackPanel Cat(string name, string glyph)
        {
            var p = new StackPanel { Margin = new Thickness(20, 10, 20, 16), Visibility = Visibility.Collapsed };
            cats.Add((name, glyph, p));
            return p;
        }
        StackPanel panel = Cat("快捷键", "\uE765");
        #endregion

        #region 快捷键页说明：键位统一由手势配置器管理
        panel.Children.Add(new TextBlock
        {
            Foreground = new SolidColorBrush(Theme.TextDim),
            FontSize = 12.5,
            Margin = new Thickness(2, 2, 0, 6),
            TextWrapping = TextWrapping.Wrap,
            Text = "当前生效的键位如下，增删改请打开下方手势配置器。",
        });
        #endregion

        #region 当前生效键位（从手势绑定推导，免开配置器即可一眼看到截图/选区键）
        var gk = _settings.Current.Gestures;
        var keyCard = new Border
        {
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            Background = new SolidColorBrush(Theme.CardBg),
            Padding = new Thickness(12, 10, 12, 10),
        };
        var keyStack = new StackPanel();
        void KeyRow(string label, string key)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lb = new TextBlock { Text = label, Foreground = new SolidColorBrush(Theme.TextDim), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            var kv = new TextBlock { Text = key, Foreground = new SolidColorBrush(Theme.Accent), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lb, 0); Grid.SetColumn(kv, 1);
            row.Children.Add(lb); row.Children.Add(kv);
            keyStack.Children.Add(row);
        }
        // 逐条列出用户真实绑定（动作→键）：手势可自定义，固定「截图键/选区键」两槽会漏掉录制键，
        // 且没绑定的槽位会显示成一个按了没反应的死键。
        var active = gk.Bindings
            .Where(b => b.Enabled && b.Actions.Length > 0 && HotkeySpec.Parse(b.Chord) != null)
            .ToList();
        if (active.Count == 0)
            KeyRow("键位", "未配置");
        else
            foreach (var b in active)
            {
                var suf = GestureConfig.TriggerLabel(b.Trigger);
                KeyRow(string.Join("·", b.Actions.Select(GestureActions.LabelOf)),
                       b.Chord + (suf.Length > 0 ? " " + suf : ""));
            }
        keyCard.Child = keyStack;
        Fluent.Elevate(keyCard, blur: 24, depth: 6);
        panel.Children.Add(keyCard);
        #endregion

        #region 手势配置器入口：可视化增删改键位绑定，保存即热重载生效
        var geBtn = new Border
        {
            Margin = new Thickness(0, 8, 0, 2),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            Background = new SolidColorBrush(Theme.CardBg),
            Padding = new Thickness(12, 9, 12, 9),
            Cursor = Cursors.Hand,
        };
        var geIcon = new TextBlock { Text = "\uE713", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"), FontSize = 15, Foreground = new SolidColorBrush(Theme.Accent), VerticalAlignment = VerticalAlignment.Center };
        var geRow = new StackPanel { Orientation = Orientation.Horizontal };
        geRow.Children.Add(geIcon);
        geRow.Children.Add(new TextBlock { Text = "   打开手势配置器…", Foreground = new SolidColorBrush(Theme.Text), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        geBtn.Child = geRow;
        Fluent.Elevate(geBtn, blur: 24, depth: 6);
        Fluent.ChipHover(geBtn);   // 悬停只改底色带过渡，不上浮、不缩放（与普通按钮同语言）
        geBtn.MouseLeftButtonUp += (_, _) => GestureEditorHost.ShowOrFocus();
        panel.Children.Add(geBtn);
        #endregion

        #region 截图
        panel = Cat("截图", "\uE7A8");
        panel.Children.Add(ToggleRow("\uE7B5", "选区自动截取", "完成选区后立即截取一帧静态图（PNG，同时复制到剪贴板）",
            _settings.Current.AutoCaptureOnSelect, v => _settings.Mutate(s => s.AutoCaptureOnSelect = v)));
        panel.Children.Add(ToggleRow("\uE740", "吸附到控件", "悬停自动吸附窗口内的具体控件：输入框、文字、图片、分组容器等（Tab 可上钻到父容器）。依赖应用无障碍接口，不支持时自动回退整窗吸附",
            _settings.Current.SnapContentArea, v => _settings.Mutate(s => s.SnapContentArea = v)));
        panel.Children.Add(ToggleRow("\uE7C8", "点击窗口时截取完整窗口", "选区窗里点一个吸附到的窗口，不再按屏幕矩形抓像素，而是真正截取该窗口本体——Win11 圆角处透明、浏览器/GPU 窗也干净，背景透明。关掉则点窗恢复旧的「按矩形区域截图」。拖框选区域不受影响；目标窗被别的窗遮挡或取帧失败时自动回退区域截图",
            _settings.Current.WindowCaptureEnabled, v => _settings.Mutate(s => s.WindowCaptureEnabled = v)));
        panel.Children.Add(ToggleRow("\uE7B3", "窗口截图包含投影", "在上面开关开启时，额外把窗口边缘的真实投影截进来、外缘柔和渐隐到透明。关掉则只截窗口本体（圆角透明、无投影）",
            _settings.Current.WindowCaptureShadow, v => _settings.Mutate(s => s.WindowCaptureShadow = v)));
        panel.Children.Add(ToggleRow("\uE7C4", "显示上次选区", "按下选区快捷键后，用虚线框显示上次的选区位置（Enter 可直接确认重选同区域）；关闭后每次都是干净屏",
            _settings.Current.ShowLastRegion, v => _settings.Mutate(s => s.ShowLastRegion = v)));
        panel.Children.Add(ToggleRow("\uE721", "选区放大镜", "拖框或悬停选区时，光标旁显示高倍像素放大镜、坐标/尺寸读数与光标下像素颜色（按 C 复制颜色，格式见下），便于像素级精确选区/取色；关闭可省一点开销",
            _settings.Current.PickerMagnifier, v => _settings.Mutate(s => s.PickerMagnifier = v)));
        // 定格画面开关 + 定格时预抓窗口上限（上限仅在定格开时显示，仿「跟随刷新率→隐藏手动帧率」）
        var pregrabRow = SliderRow("\uE7C8", "定格时预抓窗口数", "「定格画面」与「点击窗口时截取完整窗口」都开着时，定格瞬间后台按 Z 序（当前聚焦窗最优先）预抓这么多「完整可见」窗口的干净画面；点窗口即用缓存＝定格+干净圆角+投影，没缓存好则实时抓兜底。0=不预抓（点窗口一律实时抓）",
            0, 16, 1, _settings.Current.FreezePregrabMaxWindows,
            v => v <= 0 ? "不预抓" : $"{v:0} 个", v => _settings.Mutate(s => s.FreezePregrabMaxWindows = (int)v));
        void SetPregrabVisible(bool vis)
        {
            if (vis)
            {
                pregrabRow.Visibility = Visibility.Visible;
                pregrabRow.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0,
                    new Duration(TimeSpan.FromMilliseconds(200)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                var fade = new DoubleAnimation(0.0,
                    new Duration(TimeSpan.FromMilliseconds(160)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                fade.Completed += (_, _) => pregrabRow.Visibility = Visibility.Collapsed;
                pregrabRow.BeginAnimation(OpacityProperty, fade);
            }
        }
        if (!_settings.Current.FreezeOnSelect) { pregrabRow.Opacity = 0; pregrabRow.Visibility = Visibility.Collapsed; }
        panel.Children.Add(ToggleRow("\uE7C4", "选区时定格画面", "按下选区热键的瞬间抓一张全屏定格帧，整个选区过程画面看起来就定格了——放大镜读定格帧、拖框/区域截图也裁这张定格帧（不再选完后重新抓屏），所见即所得。关掉则全程实时：屏幕实时可见、放大镜每次移动实时跟画面、截图实时重抓",
            _settings.Current.FreezeOnSelect, v => { _settings.Mutate(s => s.FreezeOnSelect = v); SetPregrabVisible(v); }));
        panel.Children.Add(pregrabRow);
        panel.Children.Add(ColorFormatRow());
        panel.Children.Add(ToggleRow("\uE71A", "常显捕获范围轮廓", "在屏幕上持续显示当前捕获范围的边框，开/关立刻生效。尚未设置范围时没有可显示的边框——先用托盘「设置捕获范围」",
            _settings.Current.AlwaysShowRegionOutline, v => _settings.Mutate(s => s.AlwaysShowRegionOutline = v)));
        panel.Children.Add(SliderRow("\uE7C4", "截图后边框显示时间", "截完那一下范围边框「亮边闪一下」作确认——这里设它全亮停留多久再淡出。拍完即走调小、想多留一眼确认截对没调大（与「常显轮廓」和录制边框各自独立）",
            0.2, 3.0, 0.1, _settings.Current.OutlineFadeSeconds,
            v => $"{v:0.0} 秒", v => _settings.Mutate(s => s.OutlineFadeSeconds = v)));
        panel.Children.Add(ToastRow());
        panel.Children.Add(CaptureSoundRow());
        panel.Children.Add(DelayRow());

        Cardify(panel); // 截图页行归入卡片
        #endregion

        #region 录制
        panel = Cat("录制", "\uE714");
        var fpsRow = SliderRow("\uEC4A", "录制帧率 (FPS)", "录制时每秒捕获的帧数；开启下方「跟随显示器刷新率」时本项隐藏（被刷新率取代）",
            AppSettings.FpsMin, AppSettings.FpsMax, 5, _settings.Current.Fps,
            v => $"{v:0} fps", v => _settings.Mutate(s => s.Fps = (int)v));
        // 跟随刷新率开启→手动帧率完全失效，整行淡出隐藏（关闭再淡入恢复），比灰着更诚实、页面更干净
        void SetFpsVisible(bool vis)
        {
            if (vis)
            {
                fpsRow.Visibility = Visibility.Visible;
                fpsRow.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0,
                    new Duration(TimeSpan.FromMilliseconds(200)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                var fade = new DoubleAnimation(0.0,
                    new Duration(TimeSpan.FromMilliseconds(160)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                fade.Completed += (_, _) => fpsRow.Visibility = Visibility.Collapsed;
                fpsRow.BeginAnimation(OpacityProperty, fade);
            }
        }
        if (_settings.Current.FollowMonitorRefresh) { fpsRow.Opacity = 0; fpsRow.Visibility = Visibility.Collapsed; }
        panel.Children.Add(fpsRow);
        int hz = Core.DisplayRefresh.RateOf(Capture.Monitors.All().FirstOrDefault(m => m.IsPrimary)?.DeviceName);
        panel.Children.Add(ToggleRow("\uE7F4", "跟随显示器刷新率录满",
            (hz > 0 ? $"开启后录制帧率自动取当前录制显示器的刷新率（本机主屏检测到 {hz}Hz → 录满 {hz}fps），上面的手动帧率随之隐藏。"
                    : "开启后录制帧率自动取当前录制显示器的刷新率（本机暂未检测到主屏刷新率，录制时按实际显示器实时检测），上面的手动帧率随之隐藏。")
            + "高刷屏（144/165/240Hz）录满才丝滑；MP4/WebM 可录满，GIF 受格式封顶约 50fps",
            _settings.Current.FollowMonitorRefresh,
            v => { _settings.Mutate(s => s.FollowMonitorRefresh = v); SetFpsVisible(!v); })); // 开→帧率行淡出隐藏，关→淡入恢复
        panel.Children.Add(RecordFormatRow());
        // 长按录制没有时长上限——录到用户停为止，帧持续落盘，不受内存与时长约束，因此这里没有可调数值。
        // 唯一护栏是磁盘剩余空间（<1GB 自动停并保留已录内容）。
        panel.Children.Add(ToggleRow("\uE7C4", "录制时显示范围边框", "录制期间按真实捕获范围高亮描边，只提示、不录进产物。取代 Windows 11 那条绕整屏的黄色系统捕获边框（WGC 只能整显示器捕获，系统边框必然全屏、与所选范围无关）",
            _settings.Current.ShowRecordingBorder, v => _settings.Mutate(s => s.ShowRecordingBorder = v)));
        panel.Children.Add(ToggleRow("\uE8B0", "录制包含鼠标指针", "录出的画面里带上鼠标光标（默认开）。关掉后成片干净无光标，适合做教程/演示时后期自己加高亮，或不想让光标乱入",
            _settings.Current.RecordIncludeCursor, v => _settings.Mutate(s => s.RecordIncludeCursor = v)));

        Cardify(panel); // 录制页行归入卡片
        #endregion

        #region 产物
        panel = Cat("产物", "\uE74E");
        panel.Children.Add(ToggleRow("\uE74E", "自动保存副本", "捕获完成后，除复制到剪贴板外，再存一份文件到本地目录（截图存 PNG；录制按上面的「录制格式」存 MP4/WebM，未装 ffmpeg 则回退 GIF）。视频在后台编码、文件稍后出现。写盘失败不影响剪贴板，捕获永远成功",
            _settings.Current.AutoSaveCopy, v => _settings.Mutate(s => s.AutoSaveCopy = v)));
        panel.Children.Add(PathRow());

        Cardify(panel); // 产物页行归入卡片
        #endregion

        #region 编辑器
        panel = Cat("编辑器", "\uE70F");
        panel.Children.Add(ToggleRow("\uEDD7", "记住上次的工具/颜色/粗细", "关闭编辑器时记住你最后用的标注工具、颜色和线宽，下次打开直接是它们，省去每次重选。关闭后每次打开都回到默认（浏览 / 红 / 中）",
            _settings.Current.RememberEditorTools, v => _settings.Mutate(s => s.RememberEditorTools = v)));
        panel.Children.Add(ToggleRow("\uE8C1", "识别文字后显示结果窗", "「识别文字」成功后弹出结果窗预览：可查看/编辑识别文本、鼠标悬停部分选中复制、一键复制全部——预览即选取入口，此时不再自动写剪贴板（要哪段选哪段）。关闭则退回快速路径：直接写剪贴板 + 轻提示，不弹窗",
            _settings.Current.OcrShowResult, v => _settings.Mutate(s => s.OcrShowResult = v)));

        Cardify(panel); // 编辑器页行归入卡片
        #endregion

        #region 贴图
        panel = Cat("贴图", "\uE718");
        panel.Children.Add(ToggleRow("\uE718", "贴图窗口吸附", "拖动贴图时实时吸附对齐：贴近所在屏幕边缘则停靠四边/四角；贴近同屏其他贴图、以及任何应用窗口（浏览器/资源管理器…）的边或中线则与之对齐，把贴图摆齐像吸附网格一样自然。阈值调低为「贴边才吸」，不会隔空乱跳。关闭则松手停在原地",
            _settings.Current.PinSnapEdges, v => _settings.Mutate(s => s.PinSnapEdges = v)));

        Cardify(panel); // 贴图页行归入卡片
        #endregion

        #region 外观
        panel = Cat("外观", "\uE790");
        panel.Children.Add(ThemeRow());
        panel.Children.Add(AccentRow());

        Cardify(panel); // 外观页行归入卡片
        #endregion

        #region 系统
        panel = Cat("系统", "\uE713");
        panel.Children.Add(ToggleRow("\uE7E8", "开机自启", "登录 Windows 时自动启动 FrameCapture",
            _settings.Current.RunAtStartup, v =>
            {
                _settings.Mutate(s => s.RunAtStartup = v);
                try { StartupService.Apply(v); } catch { }
            }));

        Cardify(panel); // 系统页行归入卡片
        #endregion

        #region 主从式装配：左栏竖向导航 + 右栏内容，中间细分隔线
        var iconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
        var contentHeader = new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            Margin = new Thickness(22, 16, 22, 4),
        };
        var navItems = new List<(Border el, TextBlock icon, TextBlock label, Border accent)>();

        // 滑动竖条（胶囊）：唯一做位移动画的元素，切换时滑到目标行；选中底色由每项自带的强调色卡片淡入淡出
        var selBar = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Theme.Accent) };
        selBar.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.Accent, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.75 };
        Canvas.SetLeft(selBar, 4);
        var navCanvas = new Canvas { IsHitTestVisible = false };
        navCanvas.Children.Add(selBar);

        const double RowPitch = 40, RowTop = 1, RowH = 38, BarH = 18;   // 每项高 38 + 上下边距各 1 = 40
        void MoveIndicator(int idx, bool animate)
        {
            double barTop = idx * RowPitch + RowTop + (RowH - BarH) / 2;
            if (!animate) { Canvas.SetTop(selBar, barTop); return; }
            // 单调缓动、绝不过冲：弹性缓动（ElasticEase）会滑过目标行再回弹，视觉上冲出选项卡边框。
            // CubicEase EaseOut 平滑滑入即停，全程落在首末选项卡之间，不越界。
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            selBar.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(barTop, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        }
        void AnimColor(TextBlock tb, Color to, bool animate)
        {
            if (tb.Foreground is SolidColorBrush b)
            {
                if (!animate) { b.Color = to; return; }
                b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else tb.Foreground = new SolidColorBrush(to);
        }
        void AnimOpacity(UIElement el, double to, bool animate)
        {
            if (!animate) { el.Opacity = to; return; }
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        ScrollViewer? scroller = null;   // 前向声明：ShowCat 切类别时需复位滚动（实际构造在下方右栏装配处）
        void ShowCat(int idx, bool animate)
        {
            int prev = _cat;
            // 重复点击当前已选类别：内容/胶囊/标题都已在位，直接返回，不重播进入动画。
            if (animate && prev == idx) return;
            _cat = idx;
            bool doFade = animate && prev != idx && prev >= 0 && prev < cats.Count;
            // 内容切换（对标 PCL2 平滑切页）：离开的面板淡出后收起、进入的面板淡入 + 轻微上浮。
            // 面板同处一个 Grid（重叠），故可交叉淡入；快速连切时先把无关面板动画清零并收起，避免竞态残留。
            for (int i = 0; i < cats.Count; i++)
            {
                var b = cats[i].Body;
                if (i == idx || (doFade && i == prev)) continue;
                b.BeginAnimation(UIElement.OpacityProperty, null);
                b.Opacity = 1; b.RenderTransform = null; b.Visibility = Visibility.Collapsed;
            }
            if (doFade)
            {
                var outgoing = cats[prev].Body;
                outgoing.BeginAnimation(UIElement.OpacityProperty, null);
                int capturedPrev = prev;
                var fo = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                fo.Completed += (_, __) => { if (_cat != capturedPrev) outgoing.Visibility = Visibility.Collapsed; };
                outgoing.BeginAnimation(UIElement.OpacityProperty, fo);
            }
            var incoming = cats[idx].Body;
            incoming.Visibility = Visibility.Visible;
            if (prev != idx) scroller?.ScrollToTop();   // 切类别回到顶部（主题重建 prev==idx 不复位，保留滚动位置）
            incoming.BeginAnimation(UIElement.OpacityProperty, null);
            incoming.RenderTransform = null;
            if (animate)
            {
                var tt = new TranslateTransform(0, 12);
                incoming.RenderTransform = tt;
                incoming.Opacity = 0;
                incoming.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                slide.Completed += (_, __) => { if (incoming.RenderTransform == tt) incoming.RenderTransform = null; };
                tt.BeginAnimation(TranslateTransform.YProperty, slide);
            }
            else incoming.Opacity = 1;
            contentHeader.Text = cats[idx].Name;
            // 标题随正文一起淡入（改文本时置 0 再淡入，避免文字瞬切的「跳字」）
            if (animate)
                contentHeader.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else contentHeader.Opacity = 1;
            var acc = Theme.Accent;
            for (int i = 0; i < navItems.Count; i++)
            {
                bool on = i == idx;
                AnimOpacity(navItems[i].accent, on ? 1 : 0, animate);   // 强调色卡片淡入淡出
                AnimColor(navItems[i].icon, on ? acc : Theme.TextDim, animate);
                AnimColor(navItems[i].label, on ? acc : Theme.Text, animate);
            }
            MoveIndicator(idx, animate);
        }

        // 左栏：竖向导航项（图标 + 名称；选中态 = 每项自带强调色卡片淡入淡出 + 共享竖条滑动）
        // hover 用 IsMouseOver 触发器（WPF 自维护，绝不卡住）；选中项的强调色卡片在 hover 之上，故不会互相盖
        var hoverStyle = new Style(typeof(Border));
        var hovTrig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hovTrig.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Theme.Hover)));
        hoverStyle.Triggers.Add(hovTrig);

        var navPanel = new StackPanel { Width = 184, VerticalAlignment = VerticalAlignment.Top };
        for (int i = 0; i < cats.Count; i++)
        {
            int captured = i;
            var icon = new TextBlock { Text = cats[i].Glyph, FontFamily = iconFont, FontSize = 15, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center, Width = 22, TextAlignment = TextAlignment.Center };
            var label = new TextBlock { Text = cats[i].Name, FontSize = 13, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(13, 0, 10, 0) };
            row.Children.Add(icon); row.Children.Add(label);
            var accent = new Border { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0x26, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B)), Opacity = 0 };
            var cell = new Grid();
            cell.Children.Add(accent);   // 强调色卡片：淡入淡出，铺满整项
            cell.Children.Add(row);      // 内容在上
            var el = new Border
            {
                CornerRadius = new CornerRadius(6),
                Height = 38,
                Margin = new Thickness(0, 1, 0, 1),
                Style = hoverStyle,
                Child = cell,
                Cursor = Cursors.Hand,
                Focusable = true,
            };
            el.MouseLeftButtonDown += (_, e) => { ShowCat(captured, true); e.Handled = true; };
            el.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { ShowCat(captured, true); e.Handled = true; } };
            // 键盘焦点环：与 toggle/chip/试听 同款——Tab 聚焦时描边转强调色，失焦归零
            el.GotKeyboardFocus += (_, _) => { el.BorderBrush = new SolidColorBrush(Theme.Accent); el.BorderThickness = new Thickness(1); };
            el.LostKeyboardFocus += (_, _) => el.BorderThickness = new Thickness(0);
            navItems.Add((el, icon, label, accent));
            navPanel.Children.Add(el);
        }
        var navHost = new Grid { Width = 184, Margin = new Thickness(10, 10, 6, 12), VerticalAlignment = VerticalAlignment.Top };
        navHost.Children.Add(navPanel);    // 项在下层（含淡入淡出强调色卡片）
        navHost.Children.Add(navCanvas);   // 竖条在上层，落在左侧留白区、压在卡片上、不压文字

        // 右栏：内容标题 + 可滚动内容宿主
        var host = new Grid();
        foreach (var c in cats) host.Children.Add(c.Body);
        scroller = new ScrollViewer
        {
            Content = host,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var rightPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(contentHeader, Dock.Top);
        rightPanel.Children.Add(contentHeader);
        rightPanel.Children.Add(scroller);

        var divider = new Border { Width = 1, Background = new SolidColorBrush(Theme.Stroke) };

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(navHost, 0); Grid.SetColumn(divider, 1); Grid.SetColumn(rightPanel, 2);
        body.Children.Add(navHost); body.Children.Add(divider); body.Children.Add(rightPanel);

        ShowCat(Math.Clamp(_restoreCat, 0, cats.Count - 1), false);   // 重建时停在原类别，首帧不播动画

        dock.Children.Add(body);   // LastChildFill 填满标题栏以下区域
        root.Child = dock;
        Content = root;

        // 窗级 Esc 关窗、F1 打开帮助。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Fluent.CloseWithFade(this);
            else if (e.Key == Key.F1) { HelpWindow.ShowOrFocus(); e.Handled = true; }
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(hwnd);
            Win32.SetWindowTextW(hwnd, "FrameCapture Settings");
        };
        // 关窗时重挂一次全局钩子：<c>InstallHotkeys</c> 可重复调用，确保设置窗期间的改动都已装进引擎。
        Closed += (_, _) => AppHost.Current.InstallHotkeys();
    }
        #endregion

}
