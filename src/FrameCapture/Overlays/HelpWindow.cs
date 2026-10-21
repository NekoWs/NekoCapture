using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 使用指南：渐进式分步演示，一步只讲一件事。布局与设置页同语言——左侧本步大标题（图标+名称），
/// 右侧正文只讲这一步（几行短句，细节进悬停气泡），右下角 上一页/下一页 翻页。
/// 标注工具目录的唯一数据源是 <see cref="ToolCatalog"/>，本页不另存一份，故不会与编辑器漂移。
/// </summary>
public sealed class HelpWindow : Window
{
    static int _lastStep;   // 主题切换重建窗口时停留在原步骤；跨次打开也沿用（学到哪下次从哪继续）

    int _step;
    StackPanel _body = null!;
    TextBlock _bigIcon = null!, _bigTitle = null!, _bigSub = null!, _pager = null!;
    Border _prevBtn = null!, _nextBtn = null!;
    List<(string icon, string title, string sub, Action<StackPanel> body)> _steps = null!;

    /// <summary>
    /// 打开使用指南：已开则激活既有窗口——同屏至多一张指南，重复触发不叠窗。
    /// 必须在 UI 线程调用；无边框 Topmost 窗，Show 后 Activate 会取前台焦点。
    /// </summary>
    public static void ShowOrFocus()
    {
        foreach (Window w in Application.Current.Windows)
            if (w is HelpWindow h) { h.Activate(); return; }
        var w2 = new HelpWindow();
        w2.Build();
        w2.Show();
        w2.Activate();
    }

    /// <summary>SelfTest renderhelp 离屏取证用：构建但不 Show——不占任务栏、不抢焦点、不碰热键，与运行实例互不干扰。</summary>
    /// <returns>已 Build 但未 Show 的实例，停在 <c>_lastStep</c> 钳制后的步骤，可直接 measure/arrange 渲染 <c>Content</c>。</returns>
    public static HelpWindow CreateForCapture()
    {
        var w = new HelpWindow();
        w.Build();
        return w;
    }

    void OnThemeRebuild()
    {
        var oldw = this;
        var nw = new HelpWindow();
        nw.Build();
        nw.Left = oldw.Left; nw.Top = oldw.Top;
        nw.Opacity = 0;
        nw.Show();
        var fo = new DoubleAnimation(0, TimeSpan.FromMilliseconds(Fluent.Motion.ExitMs + 20)); // 交叉淡入的非对称节拍
        fo.Completed += (_, _) => oldw.Close();
        oldw.BeginAnimation(OpacityProperty, fo);
        nw.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(Fluent.Motion.ThemeMs)));
    }

    void Build()
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        InputMethod.SetIsInputMethodEnabled(this, false);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;   // 用户主动打开的一扇窗：任务栏要有它，被别的窗盖住也切得回来
        Topmost = true;
        Title = "FrameCapture Help";
        Width = 660;
        Height = 430;   // 固定高度：翻页时窗口不跳，正文超高在右栏内滚
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _step = Math.Clamp(_lastStep, 0, 99);   // 步数可能变少，Show 前再钳一次

        var root = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Theme.WindowBgSolid),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = Theme.IsDark ? 0.5 : 0.15 },
        };
        var dock = new DockPanel { LastChildFill = true };

        var (bar, closeBtn) = Fluent.TitleBar("\uE9CE", Loc.T(K.Help.UsageGuide), Loc.T(K.Common.Close));
        closeBtn.Click += (_, _) => Fluent.CloseWithFade(this);
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);

        BuildSteps();
        _step = Math.Clamp(_step, 0, _steps.Count - 1);

        #region 主体：左大标题 | 右正文（与设置页同款视觉分工）
        // 主体是 DockPanel 的填充子项（最后加入），不钉固定高度——钉死会让内容少时底部一大片空。
        // 左栏与正文都垂直居中：短页居中不空，长页仍在右栏内滚。
        var main = new Grid { Margin = new Thickness(18, 8, 18, 0) };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(2, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        _bigIcon = new TextBlock { FontFamily = new FontFamily(Fluent.IconFontChain), FontSize = 34, Foreground = new SolidColorBrush(Theme.Accent) };
        _bigTitle = new TextBlock { FontSize = 21, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Theme.Text), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        _bigSub = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Theme.TextDim), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        left.Children.Add(_bigIcon); left.Children.Add(_bigTitle); left.Children.Add(_bigSub);
        Grid.SetColumn(left, 0);
        main.Children.Add(left);

        _body = new StackPanel { Margin = new Thickness(6, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        // 内容不足一屏时垂直居中（不留空底），超过一屏时正常滚动：Grid.MinHeight 绑 ScrollViewer.ViewportHeight，
        // 短→Grid 撑到视口高、StackPanel 居中；长→Grid 随内容长、StackPanel 填满可滚。
        var bodyHost = new Grid();
        bodyHost.Children.Add(_body);
        var bodyScroll = new ScrollViewer
        {
            Content = bodyHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        bodyHost.SetBinding(FrameworkElement.MinHeightProperty,
            new System.Windows.Data.Binding("ViewportHeight") { Source = bodyScroll });
        Grid.SetColumn(bodyScroll, 1);
        main.Children.Add(bodyScroll);
        #endregion

        #region 底部：左「第 N / M 步」，右 上一页 / 下一页（固定右下角）
        var foot = new Grid { Margin = new Thickness(18, 6, 18, 14) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _pager = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_pager, 0);
        foot.Children.Add(_pager);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _prevBtn = NavBtn(Loc.T(K.Help.PrevArrow), on: false, () => Go(-1));
        _nextBtn = NavBtn(Loc.T(K.Help.NextArrow), on: true, () => Go(+1));
        nav.Children.Add(_prevBtn); nav.Children.Add(_nextBtn);
        Grid.SetColumn(nav, 1);
        foot.Children.Add(nav);
        DockPanel.SetDock(foot, Dock.Bottom);   // 底部翻页条固定，主体填充剩余高度
        dock.Children.Add(foot);
        dock.Children.Add(main);                // 最后加入 = DockPanel 填充子项

        root.Child = dock;
        Content = root;
        ShowStep(animate: false);

        Theme.Changed += OnThemeRebuild;
        Closed += (_, _) => Theme.Changed -= OnThemeRebuild;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Fluent.CloseWithFade(this);
            else if (e.Key == Key.Left) { Go(-1); e.Handled = true; }
            else if (e.Key == Key.Right) { Go(+1); e.Handled = true; }
        };
        Fluent.PopupEnter(this);
        // 载入不逐项错位：整窗一次淡入，首帧就是完整的一页。翻页的逐行落定见 Go。
        Fluent.InstallCaptionDrag(this, closeBtn);
        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(h);
            Win32.SetWindowTextW(h, "FrameCapture Help");
        };
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + (wa.Height - Height) / 2;
        };
    }

    void Go(int dir)
    {
        int n = Math.Clamp(_step + dir, 0, _steps.Count - 1);
        if (n == _step)
        {
            if (dir > 0) Fluent.CloseWithFade(this);   // 最后一步「完成」= 关闭
            return;
        }
        _step = n;
        _lastStep = n;
        ShowStep(animate: true, dir);
    }

    void ShowStep(bool animate, int dir = 0)
    {
        var (icon, title, sub, body) = _steps[_step];
        _bigIcon.Text = icon;
        _bigTitle.Text = title;
        _bigSub.Text = sub;
        _body.Children.Clear();
        body(_body);
        _pager.Text = Loc.F(K.Help.Step, _step + 1, _steps.Count);
        SetNavEnabled(_prevBtn, _step > 0);
        // 最后一步：下一页变「完成」
        if (_nextBtn.Child is TextBlock tb) tb.Text = _step == _steps.Count - 1 ? Loc.T(K.Help.DoneCheck) : Loc.T(K.Help.NextArrow);
        if (animate)
        {
            // 翻页：整块位移比逐行浮现重得多，整块淡入还会盖掉步骤标题的节奏。
            // 每行淡入 + 顺翻页方向 10px 收拢：方向感留在行上，幅度压在克制区间内。
            Fluent.CascadeRows(_body, risePx: 6, riseX: dir >= 0 ? 10 : -10);
        }
        else
        {
            Fluent.ResetCascade(_body);   // 不走动画的重建路径：把可能在飞的行清零，避免停在半透明
        }
    }
        #endregion

    #region 翻页按钮（芯片样式，与设置页同语言）
    Border NavBtn(string text, bool on, Action onClick)
    {
        var tb = new TextBlock { Text = text, FontSize = 12.5, Foreground = new SolidColorBrush(on ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text) };
        var b = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(on ? Theme.Accent : Theme.CardBg),
            BorderBrush = new SolidColorBrush(on ? Theme.Accent : Theme.Stroke),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Focusable = true,
            Child = tb,
        };
        Fluent.ChipHover(b);
        // 禁用态用低透明度+箭头光标表达（SetNavEnabled），点击/回车都先查这个门槛
        b.MouseLeftButtonUp += (_, _) => { if (b.Opacity > 0.6) onClick(); };
        b.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter && b.Opacity > 0.6) { onClick(); e.Handled = true; } };
        return b;
    }

    void SetNavEnabled(Border b, bool en)
    {
        b.Opacity = en ? 1.0 : 0.35;
        b.Cursor = en ? Cursors.Hand : Cursors.Arrow;
        if (en) b.Focusable = true; else b.Focusable = false;
    }
    #endregion

    #region 内容行
    static void KV(StackPanel p, string k, string v)
    {
        var g = new Grid { Margin = new Thickness(2, 3, 0, 3) };
        // 键位列要装得下两条组合键并排（「设范围」那行两条出厂手势都含选区那一步）：
        // 128 只够一条，第二条会在 + 号上被劈开，Ctrl+Alt / +R 读起来像两个键。
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var kt = KeyCell(k);
        var vt = new TextBlock { Text = v, FontSize = 12.5, Foreground = new SolidColorBrush(Theme.Text), TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
        Grid.SetColumn(kt, 0); Grid.SetColumn(vt, 1);
        g.Children.Add(kt); g.Children.Add(vt);
        p.Children.Add(g);
    }

    /// <summary>帮助页键位列的长相：键帽，与设置页、就绪卡同一副长相。
    /// 这一列以前是蓝色粗体文字，读起来像小标题，不像要按的键——而这一页正是迷路的人最先翻到的地方。
    /// 渲染交给 <see cref="Keycaps.ChordRow"/>：多条按法之间一条斜杠，触发后缀（「按下」）跟在键帽后面；
    /// 「未设键」这类占位文案不是键位，照文字排。</summary>
    /// <param name="k">键位串，如 <c>F6</c>、<c>Ctrl+Alt+R</c>、<c>F2 按下</c>、<c>Enter / Esc</c>。</param>
    /// <returns>可直接放进网格那一列的元素。</returns>
    static FrameworkElement KeyCell(string k)
    {
        var text = new TextBlock
        {
            Text = k, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Core.Fluent.AccentText()),
            VerticalAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap,
        };
        return Keycaps.LooksLikeKeys(k, KeySeparators)
            ? Keycaps.ChordRow(k, Core.Fluent.KeycapTheme(11.5), KeySeparators)
            : text;
    }

    /// <summary>分隔多条按法：本地化逗号（中文是全角「，」）与带空格的斜杠。
    /// 后缀里的斜杠不带空格（<c>按下/松开</c>），带空格才是在说「或者」。</summary>
    static string[] KeySeparators { get; } = { Loc.T(K.Hotkeys.Comma), ", ", " / " };

    static void Note(StackPanel p, string v)
        => p.Children.Add(new TextBlock { Text = v, FontSize = 11.5, Foreground = new SolidColorBrush(Theme.TextDim), TextWrapping = TextWrapping.Wrap, LineHeight = 16, Margin = new Thickness(2, 6, 0, 0) });

    static string KeysFor(params string[] actions)
    {
        var g = SettingsService.Shared?.Current?.Gestures;
        return g == null ? Loc.T(K.Common.NoKey) : g.DescribeKeysFor(actions);
    }

    /// <summary>编辑器命令的当前键位。键位可在设置里改，帮助里的键位跟着设置走，不写死。</summary>
    /// <param name="ids">命令 Id，可给多条，显示时用斜杠分隔。</param>
    /// <returns>每条命令取它当前第一个按法，如 <c>Space / ← / →</c>；一个按法都没留的命令显示「未设键」。</returns>
    static string EditorKeysFor(params string[] ids)
    {
        var noKey = Loc.T(K.Common.NoKey);
        return string.Join(" / ", EditorKeyParts(ids).Select(p => p.Length == 0 ? noKey : p));
    }

    // 每个 id 一段键位文字，没绑键的那段是空串：「没键」是个状态，不是一句拿来比较的文案
    static List<string> EditorKeyParts(params string[] ids)
    {
        var s = SettingsService.Shared?.Current;
        return ids.Select(id =>
            s != null
            && EditorCommands.Find(id) is { } c
            && EditorChord.Parse(EditorCommands.BindingsFor(s, c).FirstOrDefault() ?? "") is { } ec
                ? string.Join("+", ec.Tokens())
                : "").ToList();
    }
    #endregion

    #region 步骤内容（一步只讲一件事，细节进气泡）
    void BuildSteps()
    {
        _steps = new()
        {
            ("\uE945", Loc.T(K.Help.QuickStart), Loc.T(K.Help.SetARegionTriggerCopy), p =>
            {
                KV(p, KeysFor(GestureActions.ScopeSelect), Loc.T(K.Help.SetTheCaptureRegionDrag));
                KV(p, KeysFor(GestureActions.CaptureStored, GestureActions.CaptureFrame), Loc.T(K.Help.CaptureTheShotLands));
                KV(p, KeysFor(GestureActions.RecordToggle, GestureActions.RecordStart, GestureActions.RecordStop), Loc.T(K.Help.RecordPressToStartPressAgain));
                KV(p, EditorKeysFor("commit", "close"), Loc.T(K.Help.InTheEditorDoneCopies));
                KV(p, EditorKeysFor("help"), Loc.T(K.Help.ThisGuideIsAlwaysOne));
                Note(p, Loc.T(K.Help.EveryKeyIsADefault));
            }),

            ("\uE7A8", Loc.T(K.Help.ChooseTheRegion), Loc.T(K.Help.CaptureExactlyWhatYouPoint), p =>
            {
                KV(p, Loc.T(K.Help.DragABox), Loc.T(K.Help.AnyRectangleTheWheelResizes));
                KV(p, Loc.T(K.Help.Hover), Loc.T(K.Help.SnapsToTheWholeWindow));
                KV(p, "Enter / Esc", Loc.T(K.Help.ConfirmRegionCancel));
                KV(p, Loc.T(K.Help.ClearTheRegion), Loc.T(K.Help.InTheTrayMenuGoes));
                Note(p, Loc.T(K.Help.SettingsCanKeepTheRegion));
            }),

            ("\uE7B5", Loc.T(K.Common.Capture), Loc.T(K.Help.OneKeyStraight), p =>
            {
                KV(p, Loc.T(K.Help.TriggerCaptures), Loc.T(K.Help.WithARegionItCaptures));
                KV(p, Loc.T(K.Help.AfterCapture), Loc.T(K.Help.SetPerHotkeyFloatingCard));
                KV(p, Loc.T(K.Help.FloatingCard), Loc.T(K.Help.ClickItToOpen));
                KV(p, Loc.T(K.Help.Sound), Loc.T(K.Help.OffFlickYourOwnOgg));
                Note(p, Loc.T(K.Help.AfterCaptureIsSetPer));
            }),

            ("\uE714", Loc.T(K.Common.Recording), Loc.T(K.Help.PressToRecordPressAgain), p =>
            {
                KV(p, Loc.T(K.Help.PressPressAgain), Loc.T(K.Help.StartStopTheRedRec));
                KV(p, Loc.T(K.Help.FrameRate), Loc.T(K.Help.ManualFollowMonitorRefreshRecords));
                KV(p, Loc.T(K.Help.Format), Loc.T(K.Help.GifPlaysEverywhereMp4Webm));
                KV(p, Loc.T(K.Help.AfterRecording), Loc.T(K.Help.OpensInTheEditor));
                Note(p, Loc.T(K.Help.TheRecordingBorder));
            }),

            ("\uE70F", Loc.T(K.Common.Editor), Loc.T(K.Help.WatchTrimAnnotateCopy), p =>
            {
                KV(p, EditorKeysFor("play-toggle", "frame-prev", "frame-next"), Loc.T(K.Help.PlayAndPauseStep));
                KV(p, Loc.T(K.Help.CtrlWheel), Loc.T(K.Help.ZoomToInspectPixels));
                KV(p, EditorKeysFor("trim-a", "trim-b"), Loc.T(K.Help.TrimTheHead));
                KV(p, EditorKeysFor("commit", "close"), Loc.T(K.Help.DoneCopiesTheClipCancel));
                KV(p, EditorKeysFor("save-frame", "save-recording"), Loc.T(K.Help.SaveTheCurrentFrameAs));
                Note(p, Loc.T(K.Help.CtrlHBlendsTheFrames));
            }),

            ("\uE71C", Loc.F(K.Help.EditorDrawingClickTheCurrent, ToolCatalog.ExitKey), Loc.T(K.Help.TheColumnOnTheLeft), p => Tools(p)),

            ("\uE718", Loc.T(K.Common.Pin), Loc.T(K.Help.PinAnImageOnto), p =>
            {
                KV(p, EditorKeysFor("pin-frame"), Loc.T(K.Help.PinsTheCurrentFrameFrom));
                KV(p, Loc.T(K.Help.Wheel), Loc.T(K.Help.ZoomThePinChangeOpacity));
                KV(p, Loc.T(K.Help.Drag), Loc.T(K.Help.PinSnappingLinesThePin));
                KV(p, Loc.T(K.Help.ArrowKeys), Loc.T(K.Help.NudgeThePinOnePixel));
                KV(p, Loc.T(K.Help.EscRightClick), Loc.T(K.Help.CloseTheMenuSavesReads));
                Note(p, Loc.T(K.Help.TheSnappingSwitchLives));
            }),

            ("\uE8C1", Loc.T(K.Common.RecognizeText), Loc.T(K.Help.TurnTheText), p =>
            {
                KV(p, EditorKeysFor("ocr"), Loc.T(K.Help.RecognizeTheCurrentFrameResults));
                KV(p, Loc.T(K.Help.RightClickAPin), Loc.T(K.Help.PinsCanBeRead));
                Note(p, Loc.T(K.Help.UsesTheBuiltInWindows));
            }),

            ("\uE713", Loc.T(K.Help.MoreToTry), Loc.T(K.Help.HiddenInSettings), p =>
            {
                KV(p, Loc.T(K.Common.DelayedCapture), Loc.T(K.Help.CountsDownBeforeShootingSo));
                KV(p, Loc.T(K.Common.ScrollingCapture), Loc.T(K.Help.StitchesALongPage));
                KV(p, Loc.T(K.Help.DoubleClickTheCanvas), Loc.T(K.Help.JumpsBetweenActualAnd));
                KV(p, Loc.T(K.Help.KeySettings), Loc.T(K.Help.GlobalHotkeysAndTheEditor));
                KV(p, Loc.T(K.Help.TrayMenu), Loc.T(K.Help.PauseGesturesClearRegionSettings));
                KV(p, Loc.T(K.Common.Appearance), Loc.T(K.Help.TheThemeFollowsTheSystem));
                Note(p, Loc.T(K.Help.ThisPageIsOnly));
            }),
        };
    }

    /// <summary>「工具」这一页：绘图工具排成两张一行的卡片，图标 + 名字 + 键位。
    /// 每个工具那句长说明进 tooltip：一列长句把这一页撑成一屏滚动，扫一眼反而抓不到重点。</summary>
    /// <param name="p">这一页的正文面板。</param>
    void Tools(StackPanel p)
    {
        var grid = new UniformGrid { Columns = 2 };
        foreach (var t in ToolCatalog.Tools) grid.Children.Add(ToolCard(t));
        p.Children.Add(grid);
        p.Children.Add(new TextBlock
        {
            Text = Loc.F(K.Help.PressTheToolKeyAgain, EditorKeysFor("tool.X")),
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Theme.TextDim),
            Margin = new Thickness(2, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
    }

    /// <summary>一张工具卡：图标、名字、绑到的键。悬停填色，长说明在 tooltip。</summary>
    /// <param name="t">目录里的一条工具。</param>
    /// <returns>卡片。</returns>
    static Border ToolCard(ToolInfo t)
    {
        string key = EditorKeyParts("tool." + t.Key)[0];
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock
        {
            Text = t.Glyph,
            FontFamily = new FontFamily(Fluent.IconFontChain),
            FontSize = 15,
            Foreground = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 24,
        });
        head.Children.Add(new TextBlock
        {
            Text = Loc.T(t.Tip),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        // 没绑键就只写名字：名字后面挂一个空位比什么都不写更乱
        if (key.Length > 0)
            head.Children.Add(new TextBlock
            {
                Text = key,
                FontSize = 11,
                Foreground = new SolidColorBrush(Theme.TextDim),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            });
        var card = new Border
        {
            Margin = new Thickness(2),
            Padding = new Thickness(9, 7, 9, 7),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Theme.StrokeSoft),
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            Child = head,
            ToolTip = Loc.T(t.Help),
        };
        Fluent.ChipHover(card);
        return card;
    }
    #endregion

}
