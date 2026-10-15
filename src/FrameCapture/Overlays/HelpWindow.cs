using System.Windows;
using System.Windows.Controls;
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
        ShowInTaskbar = false;
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

        var (bar, closeBtn) = Fluent.TitleBar("\uE9CE", Loc.T("使用指南"));
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
        _prevBtn = NavBtn(Loc.T("\u2190 上一页"), on: false, () => Go(-1));
        _nextBtn = NavBtn(Loc.T("下一页 \u2192"), on: true, () => Go(+1));
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
        _pager.Text = Loc.F("第 {0} / {1} 步", _step + 1, _steps.Count);
        SetNavEnabled(_prevBtn, _step > 0);
        // 最后一步：下一页变「完成」
        if (_nextBtn.Child is TextBlock tb) tb.Text = _step == _steps.Count - 1 ? Loc.T("完成 \u2713") : Loc.T("下一页 \u2192");
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
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var kt = new TextBlock { Text = k, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Theme.Accent), VerticalAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap };
        var vt = new TextBlock { Text = v, FontSize = 12.5, Foreground = new SolidColorBrush(Theme.Text), TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
        Grid.SetColumn(kt, 0); Grid.SetColumn(vt, 1);
        g.Children.Add(kt); g.Children.Add(vt);
        p.Children.Add(g);
    }

    static void Note(StackPanel p, string v)
        => p.Children.Add(new TextBlock { Text = v, FontSize = 11.5, Foreground = new SolidColorBrush(Theme.TextDim), TextWrapping = TextWrapping.Wrap, LineHeight = 16, Margin = new Thickness(2, 6, 0, 0) });

    static string KeysFor(params string[] actions)
    {
        var g = SettingsService.Shared?.Current?.Gestures;
        return g == null ? Loc.T("未绑定") : g.DescribeKeysFor(actions);
    }

    /// <summary>编辑器命令的当前键位。键位可在设置里改，帮助里的键位跟着设置走，不写死。</summary>
    /// <param name="ids">命令 Id，可给多条，显示时用斜杠分隔。</param>
    /// <returns>每条命令取它当前第一个按法，如 <c>Space / ← / →</c>；一个按法都没留的命令显示「未设键」。</returns>
    static string EditorKeysFor(params string[] ids)
    {
        var s = SettingsService.Shared?.Current;
        if (s == null) return Loc.T("未设键");
        return string.Join(" / ", ids.Select(id =>
            EditorCommands.Find(id) is { } c
            && EditorChord.Parse(EditorCommands.BindingsFor(s, c).FirstOrDefault() ?? "") is { } ec
                ? string.Join("+", ec.Tokens())
                : Loc.T("未设键")));
    }
    #endregion

    #region 步骤内容（一步只讲一件事，细节进气泡）
    void BuildSteps()
    {
        _steps = new()
        {
            ("\uE945", Loc.T("快速上手"), Loc.T("定范围 → 触发 → 复制。"), p =>
            {
                KV(p, KeysFor(GestureActions.ScopeSelect), Loc.T("设置捕获范围 · 拖框或点窗口"));
                KV(p, KeysFor(GestureActions.CaptureFrame, GestureActions.CaptureSelection), Loc.T("截图：截完自动进剪贴板"));
                KV(p, KeysFor(GestureActions.RecordToggle, GestureActions.RecordStart, GestureActions.RecordStop), Loc.T("录制：按住起录，松手停"));
                KV(p, EditorKeysFor("commit", "close"), Loc.T("编辑器里：确定复制 / 取消"));
                KV(p, EditorKeysFor("help"), Loc.T("随时回到本指南"));
                Note(p, Loc.T("键位都是默认值，可在「手势配置器」里改成自己的习惯。"));
            }),

            ("\uE7A8", Loc.T("选捕获范围"), Loc.T("指哪儿，截哪儿。"), p =>
            {
                KV(p, Loc.T("拖框"), Loc.T("选任意矩形；滚轮微调、方向键移动"));
                KV(p, Loc.T("悬停"), Loc.T("自动吸附整窗；Tab 上钻到父容器"));
                KV(p, "Enter / Esc", Loc.T("确认范围 / 取消"));
                KV(p, Loc.T("清除范围"), Loc.T("托盘菜单里，恢复「截光标所在屏」"));
                Note(p, Loc.T("设置里可开「常显捕获范围轮廓」，范围一眼在；「吸附到控件」让悬停吸到按钮/文字级。"));
            }),

            ("\uE7B5", Loc.T("截图"), Loc.T("一次触发，进剪贴板。"), p =>
            {
                KV(p, Loc.T("触发即截"), Loc.T("有范围截范围，没范围截光标所在屏"));
                KV(p, Loc.T("截图后"), Loc.T("每条手势单独设：弹浮窗 / 进编辑器 / 什么都不做 / 贴图；写不写剪贴板是另一条独立开关"));
                KV(p, Loc.T("悬浮窗"), Loc.T("点它进编辑器；常驻/1/2/5秒/自定义可调"));
                KV(p, Loc.T("提示音"), Loc.T("关闭 / 轻弹 / 自定义 ogg·wav，可试听"));
                Note(p, Loc.T("「截图后」在「手势配置器」每条手势里设置；托盘还能「编辑上次截图」。"));
            }),

            ("\uE714", Loc.T("录制"), Loc.T("按住录，松手成片。"), p =>
            {
                KV(p, Loc.T("按住 / 松开"), Loc.T("起录 / 停止 · REC 红徽标表示正在录"));
                KV(p, Loc.T("帧率"), Loc.T("手动 10–240；开「跟随显示器刷新率」自动录满高刷"));
                KV(p, Loc.T("格式"), Loc.T("GIF 通用；MP4 / WebM 需 ffmpeg"));
                KV(p, Loc.T("录完"), Loc.T("自动进编辑器预览，Enter 复制成片"));
                Note(p, Loc.T("录屏边框只提示不入镜；「包含鼠标指针」可关。"));
            }),

            ("\uE70F", Loc.T("编辑器"), Loc.T("看、剪、标、复制。"), p =>
            {
                KV(p, EditorKeysFor("play-toggle", "frame-prev", "frame-next"), Loc.T("播放暂停 / 逐帧 · Shift 走一秒"));
                KV(p, Loc.T("Ctrl+滚轮"), Loc.T("缩放；1:1 看像素"));
                KV(p, EditorKeysFor("trim-a", "trim-b"), Loc.T("掐头 / 去尾，只导出要的一段"));
                KV(p, EditorKeysFor("commit", "close"), Loc.T("确定复制成片 / 取消"));
                KV(p, EditorKeysFor("save-frame", "save-recording"), Loc.T("存当前帧 PNG / 导出 MP4·WebM·GIF"));
                Note(p, Loc.T("Ctrl+H 视觉暂留合成图；「生成胶片条」抽帧拼成一张逐帧图直接复制，喂 AI 最好用。"));
            }),

            ("\uE71C", Loc.F("编辑器 · 绘图 — 再点当前工具或 {0} 退出绘制", ToolCatalog.ExitKey), Loc.T("左侧竖栏，单键切换。"), p => Tools(p)),

            ("\uE718", Loc.T("贴图"), Loc.T("把图钉在屏幕上。"), p =>
            {
                KV(p, EditorKeysFor("pin-frame"), Loc.T("编辑器里把当前帧钉到屏幕"));
                KV(p, Loc.T("滚轮 / +/-"), Loc.T("缩放贴图 / 调透明度"));
                KV(p, Loc.T("拖动"), Loc.T("「贴图窗口吸附」：贴齐屏幕边、其他贴图、任意窗口边"));
                KV(p, Loc.T("Esc / 右键"), Loc.T("关闭 / 右键菜单存盘、识字、复制"));
                Note(p, Loc.T("吸附开关在设置「贴图」页。"));
            }),

            ("\uE8C1", Loc.T("识别文字"), Loc.T("图里的字，变文本。"), p =>
            {
                KV(p, EditorKeysFor("ocr"), Loc.T("识别当前帧 → 结果窗：选哪段复制哪段"));
                KV(p, Loc.T("贴图右键"), Loc.T("也能对贴图识字"));
                Note(p, Loc.T("用 Windows 内置 OCR，离线可用；关「显示结果窗」则直接全文进剪贴板。"));
            }),

            ("\uE713", Loc.T("更多玩法"), Loc.T("藏在设置和托盘里。"), p =>
            {
                KV(p, Loc.T("延时截图"), Loc.T("托盘触发后倒数几秒再截——摆好菜单/悬停再入镜，支持自定义秒数"));
                KV(p, Loc.T("滚动截图"), Loc.T("长网页/长对话自动拼接成一张"));
                KV(p, Loc.T("键位设置"), Loc.T("全局键在手势配置器改，编辑器里的键在设置 · 快捷键直接改"));
                KV(p, Loc.T("托盘菜单"), Loc.T("暂停手势 / 清除范围 / 设置…"));
                KV(p, Loc.T("外观"), Loc.T("主题跟随系统，强调色可自定义"));
                Note(p, Loc.T("本页只是地图，细节都在各处的悬停气泡里——指到哪读到哪。"));
            }),
        };
    }

    void Tools(StackPanel p)
    {
        foreach (var t in ToolCatalog.Tools)
        {
            var g = new Grid { Margin = new Thickness(2, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var sym = new TextBlock { Text = t.Sym, FontSize = 14, Foreground = new SolidColorBrush(Theme.Accent), VerticalAlignment = VerticalAlignment.Center };
            var key = EditorKeysFor("tool." + t.Key);
            // 「未设键」是键位表的哨兵值，比较用原文；显示出来的工具名走取词
            var name = new TextBlock { Text = key == "未设键" ? Loc.T(t.Tip) : $"{Loc.T(t.Tip)} {key}", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center }; // lockeys
            var help = new TextBlock { Text = Loc.T(t.Help), FontSize = 12, Foreground = new SolidColorBrush(Theme.TextDim), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(sym, 0); Grid.SetColumn(name, 1); Grid.SetColumn(help, 2);
            g.Children.Add(sym); g.Children.Add(name); g.Children.Add(help);
            p.Children.Add(g);
        }
        p.Children.Add(new TextBlock { Text = Loc.F("再按一次工具键或 {0} 退出绘制；滚轮调粗细，文字工具调字号；Alt 吸色。", EditorKeysFor("tool.X")), FontSize = 11.5, Foreground = new SolidColorBrush(Theme.TextDim), Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
    }
    #endregion

}
