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
    int _restoreCat = -1;    // 重建（改主题/强调色）时要恢复的分页索引；-1 = 没指定，走默认第一页

    /// <summary>选中项左侧那枚滑条：位置由选中项的真实行位算，切换时滑到新行。</summary>
    Border? _selBar;

    /// <summary>侧栏各项，按分页顺序；滑条的中线拿它们的实际行位算。</summary>
    readonly List<Border> _navRows = new();

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
        // 窗口底就是「窗口底色」这枚令牌的实底：非分层窗背后没有模糊背板，带 alpha 的色会直接透成黑，
        // 所以平涂它的不透明版。Mica 只在窗口自己不画像素的地方显形，铺上实底等于把它盖掉——
        // 留着背板，「窗口底色」这一项怎么改都看不出变化。
        Background = Theme.WindowBgSolidBrush;
        SizeToContent = SizeToContent.Manual;   // 分页后每页很短，固定舒适尺寸起步，可拖边缩放
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 760; Height = Math.Min(660, SystemParameters.WorkArea.Height - 120);
        MinWidth = 680; MinHeight = 460;
        ShowInTaskbar = true;   // 普通工具窗：任务栏要有它，从别的应用切回来才找得到
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

    /// <summary>把「允许截到自己」这次改动应用到已经开着的窗口：程序性重设一遍 WDA，不必重启才生效。</summary>
    /// <param name="w">要重设的窗口；还没建句柄的直接跳过，它建成时会按设置走。</param>
    static void ApplyCaptureAffinity(Window w)
    {
        var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        if (Core.Fluent.CaptureSelfAllowed)
        {
            try { Win32.SetWindowDisplayAffinity(h, Win32.WDA_NONE); } catch { }
            return;
        }
        Core.Fluent.ExcludeFromCapture(h);
    }

    /// <summary>审查脚本用 <c>FC_SETTINGS_PAGE</c> 把窗口直接开到某一页截图（页名或下标）。
    /// 只认这一个环境变量：不给就回第一页，改主题/强调色重建时仍按 <c>_restoreCat</c> 停在原页。</summary>
    /// <returns>分页下标；没设或认不出返回 -1，由调用方兜到第一页。</returns>
    static int StartupPage()
    {
        var raw = Environment.GetEnvironmentVariable("FC_SETTINGS_PAGE");
        if (string.IsNullOrWhiteSpace(raw)) return -1;
        raw = raw.Trim().ToLowerInvariant();
        string[] names = { "hotkeys", "capture", "record", "output", "editor", "pin", "appearance", "palette", "system" };
        int at = Array.IndexOf(names, raw);
        return at >= 0 ? at : int.TryParse(raw, out var n) ? n : -1;
    }

    void Build()
    {
        // 自绘标题栏（拖动即走 + 自绘关闭）+ 按类别分页 + 配色自定义。
        // 标题栏是显式、可发现的拖拽区，正文区不承担拖拽。
        var root = new Border
        {
            CornerRadius = new CornerRadius(8),
            // 底色交给窗口自己那层：整窗铺同一支共享画刷，改「窗口底色」当场跟着走
            Background = Brushes.Transparent,
            BorderBrush = Theme.StrokeBrush,
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
        panel.Children.Add(EditableSelectionRow());
panel.Children.Add(ToggleRow("\uE7C4", Loc.T(K.Settings.RightClickCancelsPicker), Loc.T(K.Settings.RightClickCancelsPickerTip),
            _settings.Current.PickerRightClickCancel, v => _settings.Mutate(s => s.PickerRightClickCancel = v)));
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
        panel.Children.Add(OutlineFadeRow());
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
        // 跟随刷新率开启→手动帧率完全失效，整行原地收拢让位（关掉再原地拉开恢复），比灰着更诚实、页面更干净
        var fpsFold = Fluent.Revealable(fpsRow);
        if (!_settings.Current.FollowMonitorRefresh) Fluent.RevealOpen(fpsFold);
        panel.Children.Add(fpsFold);
        int hz = Core.DisplayRefresh.RateOf(Capture.Monitors.All().FirstOrDefault(m => m.IsPrimary)?.DeviceName);
        panel.Children.Add(ToggleRow("\uE7F4", Loc.T(K.Settings.RecordAtFullMonitorRefresh),
            (hz > 0 ? Loc.F(K.Settings.TheRecordingFrameRateThen2, hz, hz)
                    : Loc.T(K.Settings.TheRecordingFrameRateThen))
            + Loc.T(K.Settings.HighRefreshPanelsAtHz),
            _settings.Current.FollowMonitorRefresh,
            v =>
            {
                _settings.Mutate(s => s.FollowMonitorRefresh = v);
                if (v) Fluent.Conceal(fpsFold); else Fluent.Reveal(fpsFold);
            })); // 开→帧率行收拢让位，关→原地拉开恢复
panel.Children.Add(ToggleRow("\uE768", Loc.T(K.Settings.StartRecordingAfterSelecting), Loc.T(K.Settings.StartRecordingAfterSelectingTip),
            _settings.Current.RecordNeedsStartClick, v => _settings.Mutate(s => s.RecordNeedsStartClick = v)));
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

        Cardify(panel); // 外观页行归入卡片
        #endregion

        #region 配色
        panel = Cat(Loc.T(K.Common.Palette), "\uE771");
        BuildPalette(panel);   // 预设 / 预览 / 逐枚令牌各自成卡
        #endregion

        #region 系统
        panel = Cat(Loc.T(K.Settings.System), "\uE713");
        // 自启与提权是同一件事的两半：两个都开走计划任务（登录时不再弹 UAC），只开自启走 Run 项。
        // 每次改动都把两条入口一起摆正——只改一半会留下两个入口，登录时起来两个实例。
        void ApplyStartup()
        {
            var r = StartupService.Apply(_settings.Current.RunAtStartup, _settings.Current.RunAsAdmin);
            if (r is StartupResult.Cancelled or StartupResult.Failed)
                MessageBox.Show(this,
                    Loc.T(r == StartupResult.Cancelled ? K.Settings.StartupTaskCancelled : K.Settings.StartupTaskFailed),
                    Loc.T(K.Common.StartWithWindows), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        // 当前是否提权直接摆出来：绑了 Win 组合键却抓不到时，这一行是第一个该看的地方。
        // 文案随「开关开着但没提权」这一档改口，不是构造期写死的一句话
        var elevNote = UiNote("");
        void RefreshElevationNote()
        {
            bool wantUp = _settings.Current.RunAsAdmin;
            bool up = Services.StartupService.IsElevated;
            elevNote.Text = up ? Loc.T(K.Settings.ElevatedNow)
                : wantUp ? Loc.T(K.Settings.ElevationPending) : Loc.T(K.Settings.NotElevated);
        }
        RefreshElevationNote();
        panel.Children.Add(ToggleRow("\uE7E8", Loc.T(K.Common.StartWithWindows), Loc.T(K.Settings.StartsFramecaptureWhenYouSign),
            _settings.Current.RunAtStartup, v =>
            {
                _settings.Mutate(s => s.RunAtStartup = v);
                ApplyStartup();
            }));
        panel.Children.Add(ToggleRow("\uE7EF", Loc.T(K.Tray.RunAsAdministrator), Loc.T(K.Settings.RunAsAdministratorTip),
            _settings.Current.RunAsAdmin, v =>
            {
                _settings.Mutate(s => s.RunAsAdmin = v);
                if (!v) { ApplyStartup(); RefreshElevationNote(); return; }
                // 已经提权：只把登录入口换成任务形式，不必重启
                if (StartupService.IsElevated) { ApplyStartup(); return; }
                // 没提权：直接换成提权实例。这一档的全部意义就是让钩子抓住 Win 组合键与高权限窗口上的键，
                // 而 UAC 本身就是那次确认——自己再弹一个「要不要提权」只是多一次点击
                if (StartupService.RelaunchElevated()) { Fluent.CloseWithFade(this); return; }
                MessageBox.Show(this, Loc.T(K.Settings.ElevationRelaunchFailed), Loc.T(K.Tray.RunAsAdministrator),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }));
        panel.Children.Add(elevNote);
        panel.Children.Add(ToggleRow("\uE8B9", Loc.T(K.Settings.CaptureTheAppItself), Loc.T(K.Settings.WhenOnTheEditorTheRecord),
            _settings.Current.CaptureSelf, v =>
            {
                _settings.Mutate(s => s.CaptureSelf = v);
                // 已经开着的窗口不会自己回头改：当场的窗口逐个重设一次，省得为了生效还得重启。
                foreach (Window w in Application.Current.Windows) ApplyCaptureAffinity(w);
            }));
        panel.Children.Add(ToggleRow("\uE946", Loc.T(K.Settings.StartupNotification), Loc.T(K.Settings.AfterStartupANoticeAppears),
            _settings.Current.StartupToast, v => _settings.Mutate(s => s.StartupToast = v)));
        // 两个开关成对摆：一个管「要不要弹这条提示」，一个管「弹出来要不要列键位」。
        panel.Children.Add(ToggleRow("\uE765", Loc.T(K.Settings.ShowTheKeysInTheReady), Loc.T(K.Settings.TheReadyCardLists),
            _settings.Current.WelcomeShowsHotkeys, v => _settings.Mutate(s => s.WelcomeShowsHotkeys = v)));

        Cardify(panel); // 系统页行归入卡片
        #endregion

        #region 主从式装配：左栏竖向导航 + 右栏内容，中间细分隔线
        var iconFont = new FontFamily(Fluent.IconFontChain);
        var contentHeader = new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(22, 16, 22, 4),
        };
        var navItems = new List<(Border el, SolidColorBrush bg, TextBlock icon, TextBlock label)>();
        int navHover = -1;   // 指针当前压着的导航项；-1 = 没有

        // 滑动竖条（胶囊）：唯一做位移动画的元素，切换时滑到目标行
        var selBar = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(2), Background = Theme.AccentBrush };
        _selBar = selBar;
        selBar.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.Accent, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.75 };
        Canvas.SetLeft(selBar, 8);
        var navCanvas = new Canvas { IsHitTestVisible = false };
        navCanvas.Children.Add(selBar);

        // 每项高 38 + 上下边距各 1 = 40；滑条按这一项的真实位置算中线，不再用写死的行顶偏移——
        // 行顶偏移一改（外层内衬变了、边距变了），写死的那个数就让滑条整体偏出中线几像素
        const double RowPitch = 40, RowH = 38, BarH = 18;
        double RowTopOf(int idx)
        {
            if (idx >= 0 && idx < _navRows.Count && _navRows[idx].IsLoaded)
                return _navRows[idx].TranslatePoint(new Point(0, 0), navCanvas).Y;
            return idx * RowPitch + 1;   // 还没进树时按行高 + 上边距推算
        }
        void MoveIndicator(int idx, bool animate)
        {
            double barTop = RowTopOf(idx) + (RowH - BarH) / 2;
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
                // 不动画 = 调色板刚变，要当帧落色：先摘掉在飞的动画，否则它盖着基值，写了也看不见
                if (!animate) { b.BeginAnimation(SolidColorBrush.ColorProperty, null); b.Color = to; return; }
                b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else tb.Foreground = new SolidColorBrush(to);
        }
        void AnimBrush(SolidColorBrush b, Color to, bool animate, int ms)
        {
            if (!animate) { b.BeginAnimation(SolidColorBrush.ColorProperty, null); b.Color = to; return; }
            b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // 导航项的三态全部算在侧栏底色上，算出来都是不透明色：底色带 alpha、悬停再叠一层半透明白，
        // 落出来的颜色取决于窗口背后是什么；选中项上再压一层悬停，两层染色互相污染成一片脏色。
        Color NavPaint(int i) => Fluent.NavBacking(Theme.NavBg, i == _cat, i == navHover);
        // 选中项的文字压在「强调色淡底」上，比卡片底和窗口底都亮，照抄 AccentText 会糊成一片浅蓝；
        // 按这一项真正的底色判，读不清就退回黑或白，选中感交给色块与左侧滑条。
        Color NavFg(int i, bool icon) => Fluent.NavInk(NavPaint(i), i == _cat, icon);
        void PaintNav(bool animate)
        {
            for (int i = 0; i < navItems.Count; i++)
            {
                AnimBrush(navItems[i].bg, NavPaint(i), animate, i == navHover ? Fluent.Motion.HoverMs : Fluent.Motion.StateMs);
                AnimColor(navItems[i].icon, NavFg(i, true), animate);
                AnimColor(navItems[i].label, NavFg(i, false), animate);
            }
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
            PaintNav(animate);
            MoveIndicator(idx, animate);
        }

        // 左栏：一块侧栏底板上摆竖向导航项。静止项与底板同色（读作「还没被碰」），
        // 悬停浮起一枚药丸、选中项被强调色染过，两者叠加时按「选中底色上再走一档」算，不再叠第二层透明。
        var navPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        _navRows.Clear();   // 原地重建会再走一遍这里：不清掉，行位表会叠上一层旧窗口的元素
        for (int i = 0; i < cats.Count; i++)
        {
            int captured = i;
            var icon = new TextBlock { Text = cats[i].Glyph, FontFamily = iconFont, FontSize = 15, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center, Width = 22, TextAlignment = TextAlignment.Center };
            // Tag 是给版面探针认路用的：文案里的「设置 · X」指路句必须指着侧栏真存在的某一页，
            // 侧栏写 Hotkeys 而帮助页说 Shortcuts，新用户就会照着说明去找一个不存在的页。
            var label = new TextBlock { Text = cats[i].Name, Tag = "settings-page", FontSize = 13, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(15, 0, 10, 0) };
            row.Children.Add(icon); row.Children.Add(label);
            // 每项一支私有画刷：三态要各自淡变，挂在共享刷上等于把这一行的悬停涂到全窗每一处用侧栏底色的地方。
            var bg = new SolidColorBrush(Theme.NavBg);
            var el = new Border
            {
                CornerRadius = new CornerRadius(6),
                Height = RowH,
                Margin = new Thickness(0, 1, 0, 1),
                Background = bg,
                Child = row,
                Cursor = Cursors.Hand,
                Focusable = true,
            };
            el.MouseLeftButtonDown += (_, e) => { ShowCat(captured, true); e.Handled = true; };
            el.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { ShowCat(captured, true); e.Handled = true; } };
            // 悬停走 MouseEnter/MouseLeave 而不是 IsMouseOver 触发器：三态要一起算，
            // 触发器只会改底色这一项，文字与图标会停在旧色上，选中项还会被触发器的透明悬停盖回脏色。
            el.MouseEnter += (_, _) => { navHover = captured; PaintNav(true); };
            el.MouseLeave += (_, _) => { if (navHover == captured) { navHover = -1; PaintNav(true); } };
            // 键盘焦点环：与 toggle/chip/试听 同款——Tab 聚焦时描边转强调色，失焦归零
            el.GotKeyboardFocus += (_, _) => { el.BorderBrush = Theme.AccentBrush; el.BorderThickness = new Thickness(1); };
            el.LostKeyboardFocus += (_, _) => el.BorderThickness = new Thickness(0);
            navItems.Add((el, bg, icon, label));
            _navRows.Add(el);
            navPanel.Children.Add(el);
        }
        // 侧栏三态是算在 NavBg 上的颜色，各项拿着私有画刷：调色板一动就当帧重算一遍，胶囊的影也跟着换色。
        _repaint.Add(() =>
        {
            PaintNav(false);
            selBar.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.Accent, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.75 };
        });
        var navInner = new Grid();
        navInner.Children.Add(navPanel);    // 项在下层
        navInner.Children.Add(navCanvas);   // 竖条在上层，落在药丸左缘、不压文字
        var navHost = new Border
        {
            Width = 200,
            Margin = new Thickness(12, 10, 8, 12),
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(8),
            Background = Theme.NavBgBrush,
            BorderBrush = Theme.StrokeSoftBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Child = navInner,
        };

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

        var divider = new Border { Width = 1, Background = Theme.StrokeBrush };

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(navHost, 0); Grid.SetColumn(divider, 1); Grid.SetColumn(rightPanel, 2);
        body.Children.Add(navHost); body.Children.Add(divider); body.Children.Add(rightPanel);

        ShowCat(Math.Clamp(_restoreCat >= 0 ? _restoreCat : StartupPage(), 0, cats.Count - 1), false);   // 重建时停在原类别


        dock.Children.Add(body);   // LastChildFill 填满标题栏以下区域
        root.Child = dock;
        Content = root;

        // 深浅互换时整窗几十处颜色同帧跳变：盖一层退场那一侧的底色再淡出，变化就发生在幕布后面。
        // 逐枚改色不盖幕布——那种改动要的是当场看见，不该被一层淡出挡一下。
        Theme.Changed += OnPaletteChanged;
        Theme.Changing += OnPaletteChanging;
        Closed += (_, _) => { Theme.Changed -= OnPaletteChanged; Theme.Changing -= OnPaletteChanging; };

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