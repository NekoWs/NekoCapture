using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    int _cat;           // 当前分页索引（R119）
    int _restoreCat;    // 重建（改主题/强调色）时要恢复的分页索引

    public SettingsWindow()
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        _settings = AppHost.Current.Settings;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;   // 可拖边缩放（R119「允许更改窗口尺寸」）
        // WindowChrome 原生缩放边框 + 原生圆角/阴影（与编辑器同款）；不再用 AllowsTransparency（软渲染代价大）。
        FrameCapture.Core.Fluent.BorderlessNative(this, resizeBorder: 6);
        // 标题条 = WindowChrome 原生 CaptionHeight 拖窗区（比 WM_NCHITTEST 钩子更稳，避免与 WindowChrome 命中测试抢序）；
        // 关闭按钮在 Build 里标 IsHitTestVisibleInChrome 让位给 WPF 命中。
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null) chrome.CaptionHeight = FrameCapture.Core.Fluent.TitleBarHeight;
        Background = new SolidColorBrush(Theme.WindowBg);
        SizeToContent = SizeToContent.Manual;   // 分页后每页很短，固定舒适尺寸起步，可拖边缩放
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 760; Height = Math.Min(660, SystemParameters.WorkArea.Height - 120);
        MinWidth = 680; MinHeight = 460;
        ShowInTaskbar = false;
        Topmost = false;   // 不再总在最前（R119）：设置窗是普通工具窗，不该压住用户正在截/录的画面
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
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Theme.WindowBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            // 阴影交给 WindowChrome 的原生 DWM 阴影（自绘 DropShadow 与原生缩放窗叠加会重影/发虚，R119）
        };
        var dock = new DockPanel { LastChildFill = true };

        // ---- 标题栏：取景框图标 + 标题 + 关闭；整条可拖（共享件 Fluent.TitleBar，与帮助窗同款） ----
        var (bar, closeBtn) = FrameCapture.Core.Fluent.TitleBar("\uE7A8", "设置");
        closeBtn.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
        // 关闭按钮落在原生 CaptionHeight 拖窗区内，须显式让位给 WPF 命中，否则点它会拖窗而非关闭（R119）
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);

        // ---- 主从式（B1）：每个类别一个 StackPanel，左栏竖向导航一次只显示一个，治「布局太长」----
        var cats = new List<(string Name, string Glyph, StackPanel Body)>();
        StackPanel Cat(string name, string glyph)
        {
            var p = new StackPanel { Margin = new Thickness(20, 10, 20, 16), Visibility = Visibility.Collapsed };
            cats.Add((name, glyph, p));
            return p;
        }
        StackPanel panel = Cat("快捷键", "\uE765");

        // ---- 快捷键（R126 B2：旧「选区/捕获快捷键」两行退役，键位统一由手势配置器管理）----
        panel.Children.Add(new TextBlock
        {
            Foreground = new SolidColorBrush(Theme.TextDim),
            FontSize = 12.5,
            Margin = new Thickness(2, 2, 0, 6),
            TextWrapping = TextWrapping.Wrap,
            Text = "当前生效的键位如下，增删改请打开下方手势配置器。",
        });

        // ---- 当前生效键位（R126 B2：从手势绑定推导，免开配置器即可一眼看到截图/选区键）----
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
        // R149：逐条列出用户真实绑定（动作→键），不再写死「截图键/选区键」两槽——
        // 自定义手势下旧 ScopeKeyText 会回落成没绑定的死键（Ctrl+Shift+R），且漏掉录制键。
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
        FrameCapture.Core.Fluent.Elevate(keyCard, blur: 24, depth: 6);
        panel.Children.Add(keyCard);

        // ---- 手势配置器入口（R122）：可视化增删改键位绑定，保存即热重载生效 ----
        var geBtn = new Border
        {
            Margin = new Thickness(0, 8, 0, 2),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            Background = new SolidColorBrush(Theme.CardBg),
            Padding = new Thickness(12, 9, 12, 9),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        var geIcon = new TextBlock { Text = "\uE713", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"), FontSize = 15, Foreground = new SolidColorBrush(Theme.Accent), VerticalAlignment = VerticalAlignment.Center };
        var geRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        geRow.Children.Add(geIcon);
        geRow.Children.Add(new TextBlock { Text = "   打开手势配置器…", Foreground = new SolidColorBrush(Theme.Text), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        geBtn.Child = geRow;
        FrameCapture.Core.Fluent.Elevate(geBtn, blur: 24, depth: 6);
        Fluent.ChipHover(geBtn);   // R162：去掉悬停上浮（Lift），与普通按钮一致只改底色带过渡
        geBtn.MouseLeftButtonUp += (_, _) => FrameCapture.Services.GestureEditorHost.ShowOrFocus();
        panel.Children.Add(geBtn);

        // ---- 截图 ----
        panel = Cat("截图", "\uE7A8");
        panel.Children.Add(ToggleRow("\uE7B5", "选区自动截取", "完成选区后立即截取一帧静态图（PNG，同时复制到剪贴板）",
            _settings.Current.AutoCaptureOnSelect, v => _settings.Mutate(s => s.AutoCaptureOnSelect = v)));
        panel.Children.Add(ToggleRow("\uE740", "吸附到控件", "悬停自动吸附窗口内的具体控件：输入框、文字、图片、分组容器等（Snipaste 式，Tab 可上钻到父容器）。依赖应用无障碍接口，不支持时自动回退整窗吸附",
            _settings.Current.SnapContentArea, v => _settings.Mutate(s => s.SnapContentArea = v)));
        panel.Children.Add(ToggleRow("\uE7C4", "显示上次选区", "按下选区快捷键后，用虚线框显示上次的选区位置（Enter 可直接确认重选同区域）；关闭后每次都是干净屏",
            _settings.Current.ShowLastRegion, v => _settings.Mutate(s => s.ShowLastRegion = v)));
        panel.Children.Add(ToggleRow("\uE721", "选区放大镜", "拖框或悬停选区时，光标旁显示高倍像素放大镜、坐标/尺寸读数与光标下像素颜色（按 C 复制颜色，格式见下），便于像素级精确选区/取色（QQ/Snipaste 式）；关闭可省一点开销",
            _settings.Current.PickerMagnifier, v => _settings.Mutate(s => s.PickerMagnifier = v)));
        panel.Children.Add(ColorFormatRow());
        panel.Children.Add(ToggleRow("\uE71A", "常显捕获范围轮廓", "在屏幕上持续显示当前捕获范围的边框，开/关立刻生效（R161 修复：此前要等下一次捕获才动）。尚未设置范围时没有可显示的边框——先用托盘「设置捕获范围」",
            _settings.Current.AlwaysShowRegionOutline, v => _settings.Mutate(s => s.AlwaysShowRegionOutline = v)));
        panel.Children.Add(ToastRow());
        panel.Children.Add(CaptureSoundRow());
        panel.Children.Add(DelayRow());

        Cardify(panel); // R158 截图页行归入卡片

        // ---- 录制 ----
        panel = Cat("录制", "\uE714");
        var fpsRow = SliderRow("\uEC4A", "录制帧率 (FPS)", "录制时每秒捕获的帧数；开启下方「跟随显示器刷新率」时本项隐藏（被刷新率取代）",
            AppSettings.FpsMin, AppSettings.FpsMax, 5, _settings.Current.Fps,
            v => $"{v:0} fps", v => _settings.Mutate(s => s.Fps = (int)v));
        // R161：跟随刷新率开启→手动帧率完全失效，整行淡出隐藏（关闭再淡入恢复），比灰着更诚实、页面更干净
        void SetFpsVisible(bool vis)
        {
            if (vis)
            {
                fpsRow.Visibility = Visibility.Visible;
                fpsRow.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1.0,
                    new System.Windows.Duration(TimeSpan.FromMilliseconds(200)))
                { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
            }
            else
            {
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0.0,
                    new System.Windows.Duration(TimeSpan.FromMilliseconds(160)))
                { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
                fade.Completed += (_, _) => fpsRow.Visibility = Visibility.Collapsed;
                fpsRow.BeginAnimation(OpacityProperty, fade);
            }
        }
        if (_settings.Current.FollowMonitorRefresh) { fpsRow.Opacity = 0; fpsRow.Visibility = Visibility.Collapsed; }
        panel.Children.Add(fpsRow);
        int hz = Core.DisplayRefresh.RateOf(FrameCapture.Capture.Monitors.All().FirstOrDefault(m => m.IsPrimary)?.DeviceName);
        panel.Children.Add(ToggleRow("\uE7F4", "跟随显示器刷新率录满",
            (hz > 0 ? $"开启后录制帧率自动取当前录制显示器的刷新率（本机主屏检测到 {hz}Hz → 录满 {hz}fps），上面的手动帧率随之隐藏。"
                    : "开启后录制帧率自动取当前录制显示器的刷新率（本机暂未检测到主屏刷新率，录制时按实际显示器实时检测），上面的手动帧率随之隐藏。")
            + "高刷屏（144/165/240Hz）录满才丝滑；MP4/WebM 可录满，GIF 受格式封顶约 50fps",
            _settings.Current.FollowMonitorRefresh,
            v => { _settings.Mutate(s => s.FollowMonitorRefresh = v); SetFpsVisible(!v); })); // 开→帧率行淡出隐藏，关→淡入恢复（R161）
        panel.Children.Add(RecordFormatRow());
        // R61：长按录制时长上限已移除——录到用户停为止（帧落盘，不再受内存/时长限制）。
        // 唯一护栏是磁盘剩余空间（<1GB 自动停并保留已录内容），无需再暴露数值。
        panel.Children.Add(ToggleRow("\uE7C4", "录制时显示范围边框", "录制期间按真实捕获范围高亮描边，只提示、不录进产物。取代 Windows 11 那条绕整屏的黄色系统捕获边框（WGC 只能整显示器捕获，系统边框必然全屏、与所选范围无关）",
            _settings.Current.ShowRecordingBorder, v => _settings.Mutate(s => s.ShowRecordingBorder = v)));
        panel.Children.Add(ToggleRow("\uE8B0", "录制包含鼠标指针", "录出的画面里带上鼠标光标（默认开）。关掉后成片干净无光标，适合做教程/演示时后期自己加高亮，或不想让光标乱入",
            _settings.Current.RecordIncludeCursor, v => _settings.Mutate(s => s.RecordIncludeCursor = v)));

        Cardify(panel); // R158 录制页行归入卡片

        // ---- 产物 ----
        panel = Cat("产物", "\uE74E");
        // R161：「自动生成 Agent 胶片条 / 抽帧数」两行移除——胶片条改为视频编辑器里的功能按钮
        // （「生成胶片条」→ 小窗问抽帧参数 → 直接复制剪贴板），参数记忆在小窗里，不再是全局设置。
        panel.Children.Add(ToggleRow("\uE74E", "自动保存副本", "捕获完成后，除复制到剪贴板外，再存一份文件到本地目录（截图存 PNG；录制按上面的「录制格式」存 MP4/WebM，未装 ffmpeg 则回退 GIF）。视频在后台编码、文件稍后出现。写盘失败不影响剪贴板，捕获永远成功",
            _settings.Current.AutoSaveCopy, v => _settings.Mutate(s => s.AutoSaveCopy = v)));
        panel.Children.Add(PathRow());

        Cardify(panel); // R158 产物页行归入卡片

        // ---- 编辑器 ----
        panel = Cat("编辑器", "\uE70F");
        panel.Children.Add(ToggleRow("\uEDD7", "记住上次的工具/颜色/粗细", "关闭编辑器时记住你最后用的标注工具、颜色和线宽，下次打开直接是它们，省去每次重选。关闭后每次打开都回到默认（浏览 / 红 / 中）",
            _settings.Current.RememberEditorTools, v => _settings.Mutate(s => s.RememberEditorTools = v)));
        panel.Children.Add(ToggleRow("\uE8C1", "识别文字后显示结果窗", "「识别文字」成功后弹出结果窗预览：可查看/编辑识别文本、鼠标悬停部分选中复制、一键复制全部——预览即选取入口，此时不再自动写剪贴板（要哪段选哪段）。关闭则退回快速路径：直接写剪贴板 + 轻提示，不弹窗",
            _settings.Current.OcrShowResult, v => _settings.Mutate(s => s.OcrShowResult = v)));

        Cardify(panel); // R158 编辑器页行归入卡片

        // ---- 贴图 ----
        panel = Cat("贴图", "\uE718");
        panel.Children.Add(ToggleRow("\uE718", "贴图窗口吸附", "拖动贴图时实时吸附对齐（R161 由「磁边」升级）：贴近所在屏幕边缘则停靠四边/四角；贴近同屏其他贴图、以及任何应用窗口（浏览器/资源管理器…）的边或中线则与之对齐，把贴图摆齐像吸附网格一样自然。阈值调低为「贴边才吸」，不会隔空乱跳。关闭则松手停在原地",
            _settings.Current.PinSnapEdges, v => _settings.Mutate(s => s.PinSnapEdges = v)));

        Cardify(panel); // R158 贴图页行归入卡片

        // ---- 外观 ----
        panel = Cat("外观", "\uE790");
        panel.Children.Add(ThemeRow());
        panel.Children.Add(AccentRow());

        Cardify(panel); // R158 外观页行归入卡片

        // ---- 系统 ----
        panel = Cat("系统", "\uE713");
        panel.Children.Add(ToggleRow("\uE7E8", "开机自启", "登录 Windows 时自动启动 FrameCapture",
            _settings.Current.RunAtStartup, v =>
            {
                _settings.Mutate(s => s.RunAtStartup = v);
                try { StartupService.Apply(v); } catch { }
            }));

        Cardify(panel); // R158 系统页行归入卡片

        // ---- 主从式装配（B1）：左栏竖向导航「设置项」+ 右栏「设置内容」，中间细分隔线 ----
        var iconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
        var contentHeader = new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            Margin = new Thickness(22, 16, 22, 4),
        };
        var navItems = new List<(Border el, TextBlock icon, TextBlock label, Border accent)>();

        // 滑动竖条（胶囊）：唯一平移的元素，切换时滑过去 + 轻微回弹；强调色卡片改为每项自带、淡入淡出
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
            // 单调缓动、绝不过冲：弹性缓动（ElasticEase）会滑过目标行再回弹，视觉上冲出选项卡边框（R157 用户反馈）。
            // 改 CubicEase EaseOut——平滑滑入即停，全程落在首末选项卡之间，不越界。
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
            // 重复点击当前已选类别：内容/胶囊/标题都已在位，直接返回，不重播进入动画（R157 用户反馈：点同一页会重复播放切换动画）。
            if (animate && prev == idx) return;
            _cat = idx;
            bool doFade = animate && prev != idx && prev >= 0 && prev < cats.Count;
            // 内容切换（对标 PCL2 平滑切页，替代原 Visibility 硬切）：旧面板淡出后收起、新面板淡入 + 轻微上浮。
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
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.EnterMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.EnterMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                slide.Completed += (_, __) => { if (incoming.RenderTransform == tt) incoming.RenderTransform = null; };
                tt.BeginAnimation(TranslateTransform.YProperty, slide);
            }
            else incoming.Opacity = 1;
            contentHeader.Text = cats[idx].Name;
            // 标题随正文一起淡入（改文本时置 0 再淡入，避免文字瞬切的「跳字」）
            if (animate)
                contentHeader.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.EnterMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
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
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(13, 0, 10, 0) };
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
            // 键盘焦点环：与 toggle/chip/试听 同款（此前可 Tab 聚焦却无可见焦点态，指令④一致性）
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

        ShowCat(Math.Clamp(_restoreCat, 0, cats.Count - 1), false);   // 重建时停在原类别（R119），首帧不播动画

        dock.Children.Add(body);   // LastChildFill 填满标题栏以下区域
        root.Child = dock;
        Content = root;

        // 窗级 Esc 关窗、F1 打开帮助。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) FrameCapture.Core.Fluent.CloseWithFade(this);
            else if (e.Key == Key.F1) { FrameCapture.Overlays.HelpWindow.ShowOrFocus(); e.Handled = true; }
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(hwnd);
            Win32.SetWindowTextW(hwnd, "FrameCapture Settings");
        };
        // 拖窗改由 WindowChrome 原生 CaptionHeight 处理（见 ctor）；不再挂 WM_NCHITTEST 钩子，避免与 WindowChrome 命中测试抢序。
        Closed += (_, _) => AppHost.Current.InstallHotkeys();
    }

    /// <summary>主题/强调色变更后原地重建：同位交叉淡入新窗（旧淡出与新淡入非对称节拍）。</summary>
    void RebuildInPlace()
    {
        var oldw = this;
        var nw = new SettingsWindow();
        nw._restoreCat = oldw._cat;   // 改主题/强调色重建后停在原分页，不跳回首页（R119）
        nw.Build();
        // 构造里是 CenterScreen，Show 时会覆盖下面手设的 Left/Top → 每次改主题/强调色都弹回屏幕中心。
        // 重建必须走 Manual，才尊重原位置（R119 修「改主题/强调色窗口跑到中心」）。
        nw.WindowStartupLocation = WindowStartupLocation.Manual;
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
        Fluent.ChipHover(chip, 1.12);   // 可点色片补 hover 缩放弹性，与编辑器色板同款手感（此前仅光标变手型、零反馈）
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
                    chip.Background = new SolidColorBrush(Theme.Accent); // 色片实时跟色，所见即所得（R119 缓解「强调色不实时」）
                    FrameCapture.Core.Fluent.ApplyGlobalStyles();
                };
                _accentPop = new Popup
                {
                    Child = FrameCapture.Core.Fluent.PopupCard(_accentView),
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
        // 键盘焦点环：与「试听」按钮同款——聚焦时描边转强调色（此前可聚焦却无可见焦点态，指令④一致性）
        chip.GotKeyboardFocus += (_, _) => chip.BorderBrush = new SolidColorBrush(Theme.Accent);
        chip.LostKeyboardFocus += (_, _) => chip.BorderBrush = new SolidColorBrush(Theme.Stroke);
        g.Children.Add(chip);
        return g;
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
            // R162：滑块恒白 + 极淡描边（QQ 式）——旧版亮色主题下黑滑块/黑字压 accent，适配感缺失
            Fill = new SolidColorBrush(Colors.White),
            Stroke = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)),
            StrokeThickness = 1,
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
        g.Children.Add(Icon("\uE838", "保存文件夹：自动保存副本存到这里，编辑器/贴图「另存为」对话框也默认打开这里"));
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand, Background = Brushes.Transparent,
            ToolTip = "点击选择保存文件夹（自动保存副本 + 导出对话框默认目录）",
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
        // 整行可点（弹文件夹选择器）：hover 时路径文字提亮，提示这块也能点，与右侧「更改」链接一致
        stack.MouseEnter += (_, _) => path.Foreground = new SolidColorBrush(Theme.Text);
        stack.MouseLeave += (_, _) => path.Foreground = new SolidColorBrush(Theme.TextDim);
        Grid.SetColumn(stack, 1);
        g.Children.Add(stack);
        var browse = new TextBlock
        {
            Text = "更改", FontSize = 12, Foreground = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Margin = new Thickness(12, 0, 6, 0),
        };
        Grid.SetColumn(browse, 2);
        g.Children.Add(browse);
        // 文字链接 hover 才显下划线（默认干净、悬停才提示可点），比常驻下划线更克制
        browse.MouseEnter += (_, _) => browse.TextDecorations = System.Windows.TextDecorations.Underline;
        browse.MouseLeave += (_, _) => browse.TextDecorations = null;
        void Open()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            { Description = "选择保存文件夹（自动保存副本 + 导出对话框默认目录）", UseDescriptionForTitle = true };
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
                    Foreground = new SolidColorBrush(captured == cur ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text),
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

    // R157：全局「截图后」四选一已移除——改由每条手势在「手势配置器」里各自设置。
    // R161：连「见手势配置器」的占位行也删了——截图页顶部就有手势配置器入口，不必再留指路行。

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
                    Foreground = new SolidColorBrush(captured == cur ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            chipList.Add((chip, id));
            Fluent.ChipHover(chip, 1.06);   // 分段芯片补 hover 缩放弹性（此前仅手型光标、零反馈，与主题芯片不一致）
            void Apply()
            {
                _settings.Mutate(s => s.RecordFormat = captured);
                foreach (var (c, cid) in chipList)
                {
                    bool sel = cid == captured;
                    c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
                    if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text);
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
        // R112：ffmpeg 检测状态即时可见（原来只在 MP4/WebM 芯片 tooltip 里，选格式时看不到）；
        // 用绘制的小圆点而非字形，规避未验证字形；只读、开设置时算一次
        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 3, 0, 0) };
        var dotColor = hasFf ? Color.FromRgb(0x4C, 0xAF, 0x50) : Color.FromRgb(0xD1, 0x8B, 0x00);
        status.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = new SolidColorBrush(dotColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        status.Children.Add(new TextBlock
        {
            Text = hasFf ? "已检测到 ffmpeg · 可导出 MP4 / WebM"
                         : "未检测到 ffmpeg · 导出 MP4/WebM 需先安装（见帮助 F1），GIF 不受影响",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        var wrap = new StackPanel();
        wrap.Children.Add(g);
        wrap.Children.Add(status);
        return wrap;
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
                    Foreground = new SolidColorBrush(captured == cur ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            chipList.Add((chip, id));
            Fluent.ChipHover(chip, 1.06);   // 分段芯片补 hover 缩放弹性（此前仅手型光标、零反馈，与主题芯片不一致）
            void Apply()
            {
                _settings.Mutate(s => s.ColorCopyFormat = captured);
                foreach (var (c, cid) in chipList)
                {
                    bool sel = cid == captured;
                    c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
                    if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text);
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
        // R161：预设之外补「自定义」输入（此前只有 5 档，想要 2 秒/8 秒都调不了）；文字居中，与悬浮窗时长同款交互
        var box = new TextBox
        {
            Text = cur.ToString(),
            Width = 30,
            FontSize = 12.5,
            TextAlignment = TextAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Theme.Text),
            CaretBrush = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var unit = new TextBlock { Text = "秒", FontSize = 12.5, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        var customBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8, 0, 2, 0),
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, unit } },
            ToolTip = "自定义：输入任意秒数（1–120），回车或移开焦点生效",
            Cursor = Cursors.Hand,
        };
        customBox.MouseLeftButtonUp += (_, _) => { box.Focus(); box.SelectAll(); };
        void RefreshSel(int v)
        {
            bool anySel = false;
            foreach (var (c, cs) in chipList)
            {
                bool sel = cs == v;
                anySel |= sel;
                c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
                if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text);
            }
            customBox.Background = new SolidColorBrush(anySel ? Theme.CardBg : Theme.Accent);
            customBox.BorderBrush = new SolidColorBrush(anySel ? Theme.Stroke : Theme.Accent);
            box.Foreground = new SolidColorBrush(anySel ? Theme.Text : Fluent.OnAccentColor(Theme.Accent));
            unit.Foreground = new SolidColorBrush(anySel ? Theme.TextDim : Fluent.OnAccentColor(Theme.Accent));
            box.Text = v.ToString();
        }
        void Commit()
        {
            var raw = (box.Text ?? "").Trim();
            if (!int.TryParse(raw, out int v) || v < 1) { box.Text = _settings.Current.DelayCaptureSeconds.ToString(); return; }
            v = Math.Clamp(v, 1, 120);
            _settings.Mutate(s => s.DelayCaptureSeconds = v);
            RefreshSel(v);
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; } };
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
                    Foreground = new SolidColorBrush(captured == cur ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            chipList.Add((chip, secs));
            Fluent.ChipHover(chip, 1.06);   // 分段芯片补 hover 缩放弹性（此前仅手型光标、零反馈，与主题芯片不一致）
            void Apply()
            {
                _settings.Mutate(s => s.DelayCaptureSeconds = captured);
                RefreshSel(captured);
            }
            chip.MouseLeftButtonDown += (_, e) => { Apply(); e.Handled = true; };
            chip.Focusable = true;
            chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Apply(); e.Handled = true; } };
            chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = new SolidColorBrush(Theme.Accent); chip.BorderThickness = new Thickness(1); };
            chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
            chips.Children.Add(chip);
        }
        chips.Children.Add(customBox);
        RefreshSel(cur);
        g.Children.Add(chips);
        return g;
    }
    // R120 C：截图后悬浮窗存在时间——文字芯片（常驻/秒数），点击就地更新选中态
    FrameworkElement ToastRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 芯片列
        g.Children.Add(Icon("\uE916", "截完弹出的「已复制」缩略浮窗停留多久自动消失。悬停其上会暂停计时、移开再给满时长。想拍完即走调小、想留着慢慢点进编辑器调大或常驻。"));
        var lbl = new TextBlock
        {
            Text = "悬浮窗存在时间",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        double cur = _settings.Current.ToastSeconds;
        // R161：预设精简为 常驻/1秒/2秒/5秒 + 自定义输入（此前 常驻/2/3.2/5/8 五档太密且默认值占一档）
        var opts = new (double secs, string text, string tip)[]
        {
            (0, "常驻", "不自动消失，点一下或 Esc 才关"),
            (1, "1秒", "快闪：确认一下就走"),
            (2, "2秒", "利落：来得及看一眼"),
            (5, "5秒", "从容：够移动鼠标去点"),
        };
        var chipList = new System.Collections.Generic.List<(Border chip, double secs)>();
        string Fmt(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var box = new TextBox
        {
            Text = Fmt(cur),
            Width = 34,
            FontSize = 12.5,
            TextAlignment = TextAlignment.Center,   // R161：自定义输入框文字居中（此前右对齐，数字贴边难看）
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Theme.Text),
            CaretBrush = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var unit = new TextBlock { Text = "秒", FontSize = 12.5, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        var customBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8, 0, 2, 0),
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, unit } },
            ToolTip = "自定义：输入任意秒数（0.5–60，0=常驻），回车或移开焦点生效",
            Cursor = Cursors.Hand,
        };
        customBox.MouseLeftButtonUp += (_, _) => { box.Focus(); box.SelectAll(); };
        void RefreshSel(double v)
        {
            bool anySel = false;
            foreach (var (c, cs) in chipList)
            {
                bool sel = Math.Abs(cs - v) < 0.01;
                anySel |= sel;
                c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
                if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text);
            }
            // 值不落在任何预设档 = 自定义生效，自定义框高亮成选中态（此前永远灰底，看不出当前用的是哪个值）
            customBox.Background = new SolidColorBrush(anySel ? Theme.CardBg : Theme.Accent);
            customBox.BorderBrush = new SolidColorBrush(anySel ? Theme.Stroke : Theme.Accent);
            box.Foreground = new SolidColorBrush(anySel ? Theme.Text : Fluent.OnAccentColor(Theme.Accent));
            unit.Foreground = new SolidColorBrush(anySel ? Theme.TextDim : Fluent.OnAccentColor(Theme.Accent));
            box.Text = Fmt(v);
        }
        void Commit()
        {
            var raw = (box.Text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || v < 0)
            { box.Text = Fmt(_settings.Current.ToastSeconds); return; }   // 非法输入回退当前值
            _settings.Mutate(s => s.ToastSeconds = v);                     // Normalize 夹 0.5–60，0=常驻
            RefreshSel(_settings.Current.ToastSeconds);                    // 用夹取后的实际值回显
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; } };
        foreach (var (secs, text, tip) in opts)
        {
            double captured = secs;
            bool sel0 = Math.Abs(captured - cur) < 0.01;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(sel0 ? Theme.Accent : Theme.Hover),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(sel0 ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            chipList.Add((chip, secs));
            Fluent.ChipHover(chip, 1.06);   // 分段芯片补 hover 缩放弹性（此前仅手型光标、零反馈，与主题芯片不一致）
            void Apply()
            {
                _settings.Mutate(s => s.ToastSeconds = captured);
                RefreshSel(captured);
            }
            chip.MouseLeftButtonDown += (_, e) => { Apply(); e.Handled = true; };
            chip.Focusable = true;
            chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Apply(); e.Handled = true; } };
            chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = new SolidColorBrush(Theme.Accent); chip.BorderThickness = new Thickness(1); };
            chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
            chips.Children.Add(chip);
        }
        chips.Children.Add(customBox);
        RefreshSel(cur);   // 初始回显：值不在预设档时自定义框即刻高亮
        g.Children.Add(chips);
        return g;
    }
    FrameworkElement CaptureSoundRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 控件列
        g.Children.Add(Icon("\uE767", "静态截图完成时的听觉确认。关闭=静音；轻弹=内置柔和「嘀」；自定义…=选一个 ogg/wav 文件（wav 最稳，ogg 需系统解码器，播不了会自动回退轻弹）。随时可「试听」"));
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

        // 试听：按当前选择（含自定义文件）立刻放一声
        var preview = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 12, 0),
            // R162：垂直居中——横向 StackPanel 里 Border 被拉满行高，文字贴左上「错位」真凶
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Theme.Hover),
            Child = new TextBlock { Text = "试听", FontSize = 12.5, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center },
            Cursor = Cursors.Hand,
            ToolTip = "点一下试听当前提示音（选「自定义…」后也会立刻放一声让你确认）",
            Focusable = true,
        };
        void DoPreview() => FrameCapture.Core.CaptureSound.Play(
            _settings.Current.CaptureSound ? _settings.Current.CustomSoundPath : "");
        preview.MouseLeftButtonDown += (_, e) => { DoPreview(); e.Handled = true; };
        preview.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { DoPreview(); e.Handled = true; } };
        preview.GotKeyboardFocus += (_, _) => { preview.BorderBrush = new SolidColorBrush(Theme.Accent); preview.BorderThickness = new Thickness(1); };
        preview.LostKeyboardFocus += (_, _) => preview.BorderThickness = new Thickness(0);
        Fluent.ChipHover(preview);   // R162：与普通按钮一致——悬停/按住只改底色带过渡，不再放大
        right.Children.Add(preview);

        // R161：开关+固定音 → 三选下拉「关闭 / 轻弹 / 自定义…」。选「自定义…」弹文件选择窗（ogg/wav）；
        // 取消则回退上一个选项，绝不出现「选了自定义却没文件」的悬空态。
        var combo = new ComboBox { Width = 118, VerticalAlignment = VerticalAlignment.Center };
        GestureEditor.EditorStyles.ApplyCombo(combo, EditorPal());
        combo.Items.Add("关闭");
        combo.Items.Add("轻弹");
        combo.Items.Add("自定义…");
        int last = !_settings.Current.CaptureSound ? 0 : (string.IsNullOrWhiteSpace(_settings.Current.CustomSoundPath) ? 1 : 2);
        combo.SelectedIndex = last;
        combo.ToolTip = SoundTip(combo.SelectedIndex);
        bool suppress = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (suppress) return;
            int i = combo.SelectedIndex;
            if (i == 2)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择提示音（ogg / wav）",
                    Filter = "提示音 (*.ogg;*.wav)|*.ogg;*.wav|所有文件 (*.*)|*.*",
                    CheckFileExists = true,
                };
                if (dlg.ShowDialog(this) == true)
                {
                    _settings.Mutate(s => { s.CaptureSound = true; s.CustomSoundPath = dlg.FileName; });
                    FrameCapture.Core.CaptureSound.Play(dlg.FileName);   // 选完立刻放一声确认效果
                }
                else { suppress = true; combo.SelectedIndex = last; suppress = false; }   // 取消→回退原选项
            }
            else if (i == 0) _settings.Mutate(s => s.CaptureSound = false);
            else _settings.Mutate(s => { s.CaptureSound = true; s.CustomSoundPath = ""; });
            last = combo.SelectedIndex;
            combo.ToolTip = SoundTip(last);
        };
        right.Children.Add(combo);

        g.Children.Add(right);
        return g;
    }

    static string SoundTip(int i) => i switch
    {
        0 => "静音：截图完成不放声音",
        1 => "内置柔和「嘀」声（QQ 式听觉确认）",
        _ => "自定义提示音文件（ogg/wav）；点击可重新选择",
    };

    /// <summary>R161：手势编辑器同款下拉/输入样式的配色（lib 不引用宿主，配色由宿主注入）。</summary>
    static GestureEditor.EditorPalette EditorPal() => new()
    {
        WindowBg = Theme.WindowBg, PanelBg = Theme.PanelBg, CardBg = Theme.CardBg,
        Text = Theme.Text, TextDim = Theme.TextDim, Stroke = Theme.Stroke,
        Accent = Theme.Accent, Hover = Theme.Hover, Press = Theme.Press, TrackBg = Theme.TrackBg,
        IsDark = Theme.IsDark,
    };

    // ---------- 小部件 ----------

    static Grid Row()
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    /// <summary>R158：把一页里平铺的设置行整体归入一张浮起卡片（沿用快捷键页 keyCard 模式），
    /// 让截图/录制/产物…各页与快捷键页视觉一致、不再裸铺在深色背景上（治「太平」）。快捷键页自带卡片，不调本方法。</summary>
    void Cardify(StackPanel p)
    {
        if (p.Children.Count == 0) return;
        var inner = new StackPanel();
        var kids = new System.Collections.Generic.List<UIElement>();
        foreach (UIElement k in p.Children) kids.Add(k);
        p.Children.Clear();
        foreach (var k in kids) inner.Children.Add(k);
        var card = new Border
        {
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            Background = new SolidColorBrush(Theme.CardBg),
            Padding = new Thickness(12, 6, 12, 6),
            Child = inner,
        };
        FrameCapture.Core.Fluent.Elevate(card, blur: 24, depth: 6);
        p.Children.Add(card);
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
