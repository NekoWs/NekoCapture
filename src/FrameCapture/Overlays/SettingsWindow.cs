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
    readonly bool _mica;   // 背板在场才透明，缺席必须平涂
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
        _mica = Fluent.MicaBackdrop(this);
        // 标题条 = WindowChrome 原生 CaptionHeight 拖窗区（比 WM_NCHITTEST 钩子更稳，避免与 WindowChrome 命中测试抢序）；
        // 关闭按钮在 Build 里标 IsHitTestVisibleInChrome 让位给 WPF 命中。
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null) chrome.CaptionHeight = Fluent.TitleBarHeight;
        // 背板在场时窗口底必须透明：Mica 只在窗口自己不画像素的地方显形，铺任何底色都会把它盖成平涂。
        // 背板缺席时相反——非分层窗没有可透的背板，透明会被画成纯黑。
        Background = _mica ? Brushes.Transparent : new SolidColorBrush(Theme.WindowBg);
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
        // 自绘标题栏（拖动即走 + 自绘关闭）+ 按类别分页 + 强调色自定义。
        // 标题栏是显式、可发现的拖拽区，正文区不承担拖拽。
        var root = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = _mica ? Brushes.Transparent : new SolidColorBrush(Theme.WindowBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            // 阴影交给 WindowChrome 的原生 DWM 阴影（自绘 DropShadow 与原生缩放窗叠加会重影、发虚）
        };
        var dock = new DockPanel { LastChildFill = true };

        #region 标题栏：图标 + 标题 + 关闭（共享件 Fluent.TitleBar）
        var (bar, closeBtn) = Fluent.TitleBar("\uE7A8", Loc.T(K.Common.Settings), Loc.T(K.Common.Close));
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
        StackPanel panel = Cat(Loc.T(K.Settings.Hotkeys), "\uE765");
        #endregion

        // 快捷键页：总开关 + 逐条可编辑的快捷键，装配在 SettingsWindow.Hotkeys.cs
        BuildHotkeys(panel);

        // 编辑器键位一节：一条命令一行，装配在 SettingsWindow.Keys.cs
        BuildEditorKeys(panel);

        #region 截图
        panel = Cat(Loc.T(K.Common.Capture), "\uE7A8");
        panel.Children.Add(ToggleRow("\uE7B5", Loc.T(K.Settings.CaptureOnSelection), Loc.T(K.Settings.AsSoonAsASelection),
            _settings.Current.AutoCaptureOnSelect, v => _settings.Mutate(s => s.AutoCaptureOnSelect = v)));
        panel.Children.Add(ToggleRow("\uE740", Loc.T(K.Settings.SnapToControls), Loc.T(K.Settings.HoveringSnapsToASpecific),
            _settings.Current.SnapContentArea, v => _settings.Mutate(s => s.SnapContentArea = v)));
        panel.Children.Add(ToggleRow("\uE7C8", Loc.T(K.Settings.ClickAWindowToCapture), Loc.T(K.Settings.ClickingAWindowThePicker),
            _settings.Current.WindowCaptureEnabled, v => _settings.Mutate(s => s.WindowCaptureEnabled = v)));
        panel.Children.Add(ToggleRow("\uE7B3", Loc.T(K.Settings.IncludeTheShadowInWindow), Loc.T(K.Settings.WithTheSwitchAbove),
            _settings.Current.WindowCaptureShadow, v => _settings.Mutate(s => s.WindowCaptureShadow = v)));
        panel.Children.Add(ToggleRow("\uE7C4", Loc.T(K.Settings.ShowTheLastSelection), Loc.T(K.Settings.AfterTheSelectionHotkey),
            _settings.Current.ShowLastRegion, v => _settings.Mutate(s => s.ShowLastRegion = v)));
        panel.Children.Add(ToggleRow("\uE721", Loc.T(K.Settings.SelectionMagnifier), Loc.T(K.Settings.WhileYouDragOrHover),
            _settings.Current.PickerMagnifier, v => _settings.Mutate(s => s.PickerMagnifier = v)));
        // 定格画面开关 + 定格时预抓窗口上限（上限仅在定格开时显示，仿「跟随刷新率→隐藏手动帧率」）
        var pregrabRow = SliderRow("\uE7C8", Loc.T(K.Settings.WindowsPreCapturedOnFreeze), Loc.T(K.Settings.WhenBothFreezeTheScreen),
            0, 16, 1, _settings.Current.FreezePregrabMaxWindows,
            v => v <= 0 ? Loc.T(K.Settings.None) : Loc.F(K.Settings.Windows, v), v => _settings.Mutate(s => s.FreezePregrabMaxWindows = (int)v));
        void SetPregrabVisible(bool vis)
        {
            if (vis)
            {
                pregrabRow.Visibility = Visibility.Visible;
                pregrabRow.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0,
                    new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                var fade = new DoubleAnimation(0.0,
                    new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.ExitMs)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                fade.Completed += (_, _) => pregrabRow.Visibility = Visibility.Collapsed;
                pregrabRow.BeginAnimation(OpacityProperty, fade);
            }
        }
        if (!_settings.Current.FreezeOnSelect) { pregrabRow.Opacity = 0; pregrabRow.Visibility = Visibility.Collapsed; }
        panel.Children.Add(ToggleRow("\uE7C4", Loc.T(K.Settings.FreezeTheScreenWhileSelecting), Loc.T(K.Settings.TheInstantTheSelectionHotkey),
            _settings.Current.FreezeOnSelect, v => { _settings.Mutate(s => s.FreezeOnSelect = v); SetPregrabVisible(v); }));
        panel.Children.Add(pregrabRow);
        panel.Children.Add(ColorFormatRow());
        panel.Children.Add(ToggleRow("\uE71A", Loc.T(K.Settings.AlwaysShowTheCaptureOutline), Loc.T(K.Settings.KeepsTheCurrentCaptureRegion),
            _settings.Current.AlwaysShowRegionOutline, v => _settings.Mutate(s => s.AlwaysShowRegionOutline = v)));
        panel.Children.Add(SliderRow("\uE7C4", Loc.T(K.Settings.BorderFlashAfterACapture), Loc.T(K.Settings.TheMomentACaptureFinishes),
            0.2, 3.0, 0.1, _settings.Current.OutlineFadeSeconds,
            v => Loc.F(K.Settings.S, v), v => _settings.Mutate(s => s.OutlineFadeSeconds = v)));
        panel.Children.Add(ToastRow());
        panel.Children.Add(CaptureSoundRow());
        panel.Children.Add(DelayRow());

        Cardify(panel); // 截图页行归入卡片
        #endregion

        #region 录制
        panel = Cat(Loc.T(K.Common.Recording), "\uE714");
        var fpsRow = SliderRow("\uEC4A", Loc.T(K.Settings.RecordingFrameRate), Loc.T(K.Settings.FramesCapturedPerSecondWhile),
            AppSettings.FpsMin, AppSettings.FpsMax, 5, _settings.Current.Fps,
            v => $"{v:0} fps", v => _settings.Mutate(s => s.Fps = (int)v));
        // 跟随刷新率开启→手动帧率完全失效，整行淡出隐藏（关闭再淡入恢复），比灰着更诚实、页面更干净
        void SetFpsVisible(bool vis)
        {
            if (vis)
            {
                fpsRow.Visibility = Visibility.Visible;
                fpsRow.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0,
                    new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                var fade = new DoubleAnimation(0.0,
                    new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.ExitMs)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                fade.Completed += (_, _) => fpsRow.Visibility = Visibility.Collapsed;
                fpsRow.BeginAnimation(OpacityProperty, fade);
            }
        }
        if (_settings.Current.FollowMonitorRefresh) { fpsRow.Opacity = 0; fpsRow.Visibility = Visibility.Collapsed; }
        panel.Children.Add(fpsRow);
        int hz = Core.DisplayRefresh.RateOf(Capture.Monitors.All().FirstOrDefault(m => m.IsPrimary)?.DeviceName);
        panel.Children.Add(ToggleRow("\uE7F4", Loc.T(K.Settings.RecordAtFullMonitorRefresh),
            (hz > 0 ? Loc.F(K.Settings.TheRecordingFrameRateThen2, hz, hz)
                    : Loc.T(K.Settings.TheRecordingFrameRateThen))
            + Loc.T(K.Settings.HighRefreshPanelsAtHz),
            _settings.Current.FollowMonitorRefresh,
            v => { _settings.Mutate(s => s.FollowMonitorRefresh = v); SetFpsVisible(!v); })); // 开→帧率行淡出隐藏，关→淡入恢复
        panel.Children.Add(RecordFormatRow());
        // 长按录制没有时长上限——录到用户停为止，帧持续落盘，不受内存与时长约束，因此这里没有可调数值。
        // 唯一护栏是磁盘剩余空间（<1GB 自动停并保留已录内容）。
        panel.Children.Add(ToggleRow("\uE7C8", Loc.T(K.Settings.RecordWindowOnly), Loc.T(K.Settings.WhenYouClickAWindow),
            _settings.Current.RecordWindowClean, v => _settings.Mutate(s => s.RecordWindowClean = v)));
        panel.Children.Add(ToggleRow("\uE7C4", Loc.T(K.Settings.ShowTheRegionBorderWhile), Loc.T(K.Settings.DuringARecordingTheReal),
            _settings.Current.ShowRecordingBorder, v => _settings.Mutate(s => s.ShowRecordingBorder = v)));
        panel.Children.Add(ToggleRow("\uE8B0", Loc.T(K.Settings.IncludeTheMousePointer), Loc.T(K.Settings.TheCursorAppears),
            _settings.Current.RecordIncludeCursor, v => _settings.Mutate(s => s.RecordIncludeCursor = v)));

        Cardify(panel); // 录制页行归入卡片
        #endregion

        #region 产物
        panel = Cat(Loc.T(K.Settings.Output), "\uE74E");
        panel.Children.Add(ToggleRow("\uE74E", Loc.T(K.Settings.AutoSaveACopy), Loc.T(K.Settings.AfterACaptureOnTop),
            _settings.Current.AutoSaveCopy, v => _settings.Mutate(s => s.AutoSaveCopy = v)));
        panel.Children.Add(PathRow());

        Cardify(panel); // 产物页行归入卡片
        #endregion

        #region 编辑器
        panel = Cat(Loc.T(K.Common.Editor), "\uE70F");
        panel.Children.Add(ToggleRow("\uE8A9", Loc.T(K.Settings.ToolbarEntranceAnimation), Loc.T(K.Settings.WhenTheEditorOpens),
            _settings.Current.EditorToolbarAnim, v => _settings.Mutate(s => s.EditorToolbarAnim = v)));
        panel.Children.Add(ToggleRow("\uEDD7", Loc.T(K.Settings.RememberTheLastToolColor), Loc.T(K.Settings.ClosingTheEditorRemembers),
            _settings.Current.RememberEditorTools, v => _settings.Mutate(s => s.RememberEditorTools = v)));
        panel.Children.Add(ToggleRow("\uE8C1", Loc.T(K.Settings.ShowTheResultWindowAfter), Loc.T(K.Settings.AfterASuccessfulRecognition),
            _settings.Current.OcrShowResult, v => _settings.Mutate(s => s.OcrShowResult = v)));

        Cardify(panel); // 编辑器页行归入卡片
        #endregion

        #region 贴图
        panel = Cat(Loc.T(K.Common.Pin), "\uE718");
        panel.Children.Add(ToggleRow("\uE718", Loc.T(K.Settings.PinSnapping), Loc.T(K.Settings.DraggingAPinSnapsIt),
            _settings.Current.PinSnapEdges, v => _settings.Mutate(s => s.PinSnapEdges = v)));

        Cardify(panel); // 贴图页行归入卡片
        #endregion

        #region 外观
        panel = Cat(Loc.T(K.Common.Appearance), "\uE790");
        panel.Children.Add(ThemeRow());
        panel.Children.Add(LanguageRow());
        panel.Children.Add(AccentRow());

        Cardify(panel); // 外观页行归入卡片
        #endregion

        #region 系统
        panel = Cat(Loc.T(K.Settings.System), "\uE713");
        panel.Children.Add(ToggleRow("\uE7E8", Loc.T(K.Common.StartWithWindows), Loc.T(K.Settings.StartsFramecaptureWhenYouSign),
            _settings.Current.RunAtStartup, v =>
            {
                _settings.Mutate(s => s.RunAtStartup = v);
                try { StartupService.Apply(v); } catch { }
            }));
        panel.Children.Add(ToggleRow("\uE946", Loc.T(K.Settings.StartupNotification), Loc.T(K.Settings.AfterStartupANoticeAppears),
            _settings.Current.StartupToast, v => _settings.Mutate(s => s.StartupToast = v)));

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
            selBar.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(barTop, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)) { EasingFunction = ease });
        }
        void AnimColor(TextBlock tb, Color to, bool animate)
        {
            if (tb.Foreground is SolidColorBrush b)
            {
                if (!animate) { b.Color = to; return; }
                b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else tb.Foreground = new SolidColorBrush(to);
        }
        void AnimOpacity(UIElement el, double to, bool animate)
        {
            if (!animate) { el.Opacity = to; return; }
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
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
                var fo = new DoubleAnimation(0, TimeSpan.FromMilliseconds(Fluent.Motion.ExitMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
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
                // 容器只淡入、位移交给行的逐项错位：两层同时位移会叠成明显跳动
                incoming.Opacity = 0;
                incoming.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                Fluent.CascadeRows(incoming);
            }
            else
            {
                incoming.Opacity = 1;
                Fluent.ResetCascade(incoming);   // 主题重建等不走动画的路径：把可能在飞的逐项入场清零，避免停在半透明
            }
            contentHeader.Text = cats[idx].Name;
            // 标题随正文一起淡入（改文本时置 0 再淡入，避免文字瞬切的「跳字」）
            if (animate)
                contentHeader.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else contentHeader.Opacity = 1;
            var acc = Fluent.AccentText();
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
            // 让出滚动条那一格：滚动条浮在这一格里，长页短页之间正文宽度始终不变
            Padding = new Thickness(0, 0, 8, 0),
        };
        Fluent.OverlayScroll(scroller);
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

        ShowCat(Math.Clamp(_restoreCat, 0, cats.Count - 1), false);   // 重建时停在原类别

        dock.Children.Add(body);   // LastChildFill 填满标题栏以下区域
        root.Child = dock;
        Content = root;

        // 首帧一次到位：整窗连同正文由构造函数里的 PopupEnter 同一帧淡入。
        // 行的逐项错位只用在切类别（ShowCat animate=true）——那里内容确实在变。

        // 窗级 Esc 关窗、F1 打开帮助。Esc 挂在前置（隧道）事件上，窗口比子控件先收到按键：
        // 正在等键的键位框要拿它解绑，正在打字的输入框要拿它作废，都不能顺手把窗关掉。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (InCapturingBox(e.OriginalSource as DependencyObject) || e.OriginalSource is TextBox) return;
                Fluent.CloseWithFade(this);
            }
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
