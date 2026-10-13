using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using GestureCore;
using GcMods = GestureCore.ModifierKeys;

namespace GestureEditor;

/// <summary>
/// 可复用手势配置器窗口（R122 方案 C）。只依赖 GestureCore + WPF，通过四个注入点接入任意宿主：
/// <see cref="IActionCatalog"/>（动作清单）、<see cref="IGestureConfigStore"/>（配置读写）、
/// <see cref="IKeyNamer"/>（键名显示）、<see cref="EditorPalette"/>（配色）。
/// 主从布局：左=绑定列表，右=选中绑定的详情编辑；底部实时校验 + 保存/取消。
/// 组合键捕获期间触发 <see cref="ChordCaptureBegan"/>/<see cref="ChordCaptureEnded"/>，宿主据此暂停/恢复全局钩子
/// （否则要捕获的组合键会被宿主自己的钩子吞掉，编辑器收不到）。
/// </summary>
public sealed class GestureEditorWindow : Window
{
    /// <summary>图标字体链，与宿主 Fluent.IconFontChain 同款（库不引用宿主，故自带一份常量）。</summary>
    const string IconFontChain = "Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol";

    readonly IActionCatalog _catalog;
    readonly IGestureConfigStore _store;
    readonly IKeyNamer _namer;
    readonly EditorPalette _pal;

    GestureDoc _doc;
    int _selected = -1;
    bool _capturing;
    bool _suppress;   // 重建列表时抑制 SelectionChanged，避免编辑后丢选中（用户实测反馈）

    ListBox _list = null!;
    StackPanel _detail = null!;
    TextBlock _valText = null!;
    Button _saveBtn = null!;
    Border _chordBox = null!;
    TextBlock _chordText = null!;
    TextBlock _enableState = null!;
    TextBlock _exclusiveState = null!;

    // R161：列表卡片化（选中/问题红框/禁用置灰逐行自绘）+「截图后」条件显示 + 输入框/按钮过渡动画
    static readonly Color Danger = Color.FromRgb(0xFF, 0x6B, 0x6B);
    readonly HashSet<string> _problemIds = new();
    readonly List<BindingRow> _rows = new();
    StackPanel _afterSec = null!;
    SolidColorBrush _chordStroke = null!;
    bool _chordHover, _chordFocus;

    /// <summary>R161：左侧一条绑定的卡片行——选中=accent 底+明度自适应字色，问题=红框，禁用=muted 灰字。</summary>
    sealed class BindingRow
    {
        public Border Card = null!;
        public TextBlock Head = null!;
        public TextBlock Body = null!;
        public bool Problem, Enabled = true;
    }

    /// <summary>捕获组合键开始/结束：宿主应在此暂停/恢复全局键盘钩子。</summary>
    public event Action? ChordCaptureBegan;
    public event Action? ChordCaptureEnded;

    public GestureEditorWindow(IActionCatalog catalog, IGestureConfigStore store, IKeyNamer namer, EditorPalette palette)
    {
        _catalog = catalog; _store = store; _namer = namer; _pal = palette;
        _doc = store.Load();

        InputMethod.SetIsInputMethodEnabled(this, false); // 覆盖层禁输入法，避免拼音拦截按键
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        // 原生无边框 + 圆角 + DWM 阴影（WindowChrome 是 WPF 内建，非宿主件，故可复用）
        var chrome = new System.Windows.Shell.WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false };
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
        Background = _pal.B(_pal.WindowBg);
        // R162：窗口级细滚动条（列表/详情/下拉弹层全覆盖）——此前吃的是 WPF 默认 17px 经典滚动条
        Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = EditorStyles.ScrollBarStyle(_pal);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 760; Height = 560; MinWidth = 640; MinHeight = 440;
        ShowInTaskbar = true;
        Title = "手势配置";

        Build();
        RefreshList();
        if (_doc.Bindings.Count > 0) { _selected = 0; _list.SelectedIndex = 0; ShowDetail(); }
        RefreshValidation();
        SetupMotion();
    }

    // ---------- 入场/退场动效（自包含：库不引用宿主 Fluent，但手感与其 PopupEnter/CloseWithFade 同款）----------
    const int EnterMs = 190, ExitMs = 150;   // 对齐宿主 Fluent.Motion.EnterMs/ExitMs
    bool _closing;
    void SetupMotion()
    {
        Opacity = 0;
        // R163：本窗是 WindowChrome + 非分层窗，WPF 的 Opacity 动画不驱动屏幕合成（实测开合无淡入淡出）。
        // 与宿主 Fluent.PreTransparent 同款：SourceInitialized 起强制 WS_EX_LAYERED+alpha0，首帧前多次布局逐次补回，
        // ContentRendered 放行——强制分层后下面的 Opacity 动画才真正在屏幕上渐变。
        bool released = false;
        void Apply()
        {
            if (released) return;
            Native.SetAlphaZero(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }
        SourceInitialized += (_, _) => Apply();
        LayoutUpdated += (_, _) => Apply();
        ContentRendered += (_, _) =>
        {
            released = true;
            var dur = new Duration(TimeSpan.FromMilliseconds(EnterMs));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            if (Content is FrameworkElement fe && fe.RenderTransform == null)
            {
                fe.RenderTransformOrigin = new Point(0.5, 0.5);
                var st = new ScaleTransform(0.96, 0.96);
                fe.RenderTransform = st;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            }
        };
        // 退场：任何关闭路径（关闭钮/Esc/宿主 Close）先淡出再真关；淡出中再来的 Closing 一律放行。
        Closing += (_, e) =>
        {
            if (_closing) return;
            e.Cancel = true;
            _closing = true;
            var cur = Opacity;
            var a = new DoubleAnimation(cur, 0.0, new Duration(TimeSpan.FromMilliseconds(ExitMs))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, null);
            Opacity = cur;
            BeginAnimation(OpacityProperty, a);
        };
    }

    // ---------- 布局 ----------
    void Build()
    {
        var root = new Border { CornerRadius = new CornerRadius(8), Background = _pal.B(_pal.WindowBg), BorderBrush = _pal.B(_pal.Stroke), BorderThickness = new Thickness(1) };
        var dock = new DockPanel { LastChildFill = true };

        // 标题栏
        var bar = new Border { Height = 36, Background = _pal.B(_pal.PanelBg), CornerRadius = new CornerRadius(8, 8, 0, 0) };
        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new TextBlock { Text = "\uE713", FontFamily = new FontFamily(IconFontChain), FontSize = 15, Foreground = _pal.B(_pal.Accent), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        var title = new TextBlock { Text = "手势配置", Foreground = _pal.B(_pal.Text), VerticalAlignment = VerticalAlignment.Center, FontSize = 13, FontWeight = FontWeights.SemiBold };
        var closeBtn = new Button { Content = "\uE711", FontFamily = new FontFamily(IconFontChain), Width = 40, Height = 36, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = _pal.B(_pal.TextDim), Cursor = Cursors.Hand };
        StyleChromeButton(closeBtn);
        closeBtn.Click += (_, _) => Close();
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
        Grid.SetColumn(icon, 0); Grid.SetColumn(title, 1); Grid.SetColumn(closeBtn, 2);
        barGrid.Children.Add(icon); barGrid.Children.Add(title); barGrid.Children.Add(closeBtn);
        bar.Child = barGrid;
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);

        // 底部：校验 + 保存/取消
        var bottom = new Border { Padding = new Thickness(16, 10, 16, 12), Background = _pal.B(_pal.PanelBg), CornerRadius = new CornerRadius(0, 0, 8, 8) };
        var bottomGrid = new Grid();
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _valText = new TextBlock { Foreground = _pal.B(_pal.TextDim), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = MakeButton("取消", ghost: true); cancel.Click += (_, _) => Close();
        _saveBtn = MakeButton("保存", accent: true); _saveBtn.Click += (_, _) => Save();
        btns.Children.Add(cancel); btns.Children.Add(_saveBtn);
        Grid.SetColumn(_valText, 0); Grid.SetColumn(btns, 1);
        bottomGrid.Children.Add(_valText); bottomGrid.Children.Add(btns);
        bottom.Child = bottomGrid;
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);

        // 中部：顶部开关 + 主从
        var center = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        toggles.Children.Add(MakeToggle("启用手势层", v => { _doc.Enabled = v; RefreshValidation(); }, out _enableState, _doc.Enabled));
        toggles.Children.Add(MakeToggle("独占吞键", v => { _doc.Exclusive = v; }, out _exclusiveState, _doc.Exclusive));
        Grid.SetRow(toggles, 0);
        center.Children.Add(toggles);

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左：列表 + 增删
        var left = new DockPanel();
        var listBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var addBtn = MakeButton("＋ 添加绑定", ghost: true); addBtn.Click += (_, _) => AddBinding();
        var delBtn = MakeButton("删除", ghost: true); delBtn.Click += (_, _) => DeleteSelected();
        listBtns.Children.Add(addBtn); listBtns.Children.Add(delBtn);
        DockPanel.SetDock(listBtns, Dock.Bottom);
        left.Children.Add(listBtns);
        _list = new ListBox { BorderThickness = new Thickness(1) };
        EditorStyles.ApplyList(_list, _pal);
        // 列表项文本换行：长摘要（多动作 / 带「完成后」序列）折行显示，而非按面板宽度裁切（R153）
        // 须禁用横向滚动——否则 ScrollViewer 以无限宽测量，TextBlock 永不换行、改为横向滚动条
        System.Windows.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(_list, System.Windows.Controls.ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) => { if (_suppress) return; _selected = _list.SelectedIndex; PaintRows(); ShowDetail(); };
        left.Children.Add(_list);
        Grid.SetColumn(left, 0);
        split.Children.Add(left);

        // 右：详情
        var detailScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _detail = new StackPanel();
        detailScroll.Content = _detail;
        Grid.SetColumn(detailScroll, 2);
        split.Children.Add(detailScroll);

        Grid.SetRow(split, 1);
        center.Children.Add(split);

        // DockPanel：bar 顶、bottom 底、center 最后加入 → 填充剩余（LastChildFill=true）
        dock.Children.Add(center);

        root.Child = dock;
        Content = root;
    }

    // ---------- 列表（R161 卡片化：边框+底色，选中/问题/禁用逐行自绘） ----------
    void RefreshList()
    {
        _suppress = true;
        _list.Items.Clear();
        _rows.Clear();
        foreach (var b in _doc.Bindings)
        {
            var row = BuildRow(b);
            _rows.Add(row);
            _list.Items.Add(row.Card);
        }
        if (_selected >= _doc.Bindings.Count) _selected = _doc.Bindings.Count - 1;
        _list.SelectedIndex = _selected;
        _suppress = false;
        PaintRows();
    }

    BindingRow BuildRow(BindingEdit b)
    {
        var head = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var body = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var chord = b.Chord.Format(_namer);
        head.Text = (string.IsNullOrEmpty(chord) ? "未设键" : chord) + " · " + TriggerLabel(b.Trigger);
        var acts = b.Actions.Count == 0 ? "无动作" : string.Join(" → ", b.Actions.Select(ActionLabel));
        if (b.OnComplete.Count > 0) acts += "  ⟶ 完成后: " + string.Join(" → ", b.OnComplete.Select(ActionLabel));
        body.Text = acts;
        var sp = new StackPanel(); sp.Children.Add(head); sp.Children.Add(body);
        var card = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(1, 3, 3, 3), Child = sp };
        return new BindingRow { Card = card, Head = head, Body = body, Enabled = b.Enabled };
    }

    /// <summary>逐行刷三态：选中（accent 底 + OnAccent 字色）、问题（红框）、禁用（muted 灰字，不再加「已禁用 ·」前缀）。
    /// R162：三态切换走 140ms 颜色过渡（此前直接换刷子，选中/取消选中颜色硬跳）。</summary>
    void PaintRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            bool sel = i == _list.SelectedIndex;
            bool has = i < _doc.Bindings.Count;
            r.Problem = has && _problemIds.Contains(_doc.Bindings[i].Id);
            r.Enabled = !has || _doc.Bindings[i].Enabled;
            AnimBrush(r.Card.Background as SolidColorBrush, sel ? _pal.Accent : _pal.CardBg, br => r.Card.Background = br);
            AnimBrush(r.Card.BorderBrush as SolidColorBrush, r.Problem ? Danger : (sel ? _pal.Accent : _pal.Stroke), br => r.Card.BorderBrush = br);
            AnimBrush(r.Head.Foreground as SolidColorBrush, sel ? _pal.OnAccent() : (r.Enabled ? _pal.Text : _pal.TextDim), br => r.Head.Foreground = br);
            AnimBrush(r.Body.Foreground as SolidColorBrush, sel ? _pal.OnAccent() : _pal.TextDim, br => r.Body.Foreground = br);
            r.Body.Opacity = r.Enabled || sel ? 1.0 : 0.6;
        }
    }

    /// <summary>把画刷属性过渡到目标色：冻结画刷（_pal.B）先换成当前色的可动画副本再动。</summary>
    void AnimBrush(SolidColorBrush? cur, Color to, Action<SolidColorBrush> set)
    {
        if (cur == null || cur.IsFrozen)
        {
            var nb = new SolidColorBrush(cur?.Color ?? to);
            set(nb);
            cur = nb;
        }
        AnimateColor(cur, to, 140);
    }

    string ActionLabel(string actionName) => _catalog.Actions().FirstOrDefault(a => a.Name == actionName)?.Label ?? actionName;

    // R157：截图后行为的中文标签（取值与宿主 AfterCapture 一致；空=跟随默认弹提示）
    static string AfterCaptureLabel(string v) => v switch
    {
        "" => "跟随默认",
        "editor" => "进编辑器",
        "copy" => "只复制",
        "pin" => "贴图",
        _ => "弹提示",
    };

    void AddBinding()
    {
        _doc.Bindings.Add(new BindingEdit { Id = "b" + Guid.NewGuid().ToString("N").Substring(0, 6), Trigger = TriggerKind.Chord });
        _selected = _doc.Bindings.Count - 1;
        RefreshList(); _list.SelectedIndex = _selected; ShowDetail(); RefreshValidation();
    }

    void DeleteSelected()
    {
        if (_selected < 0 || _selected >= _doc.Bindings.Count) return;
        _doc.Bindings.RemoveAt(_selected);
        _selected = Math.Min(_selected, _doc.Bindings.Count - 1);
        RefreshList(); if (_selected >= 0) _list.SelectedIndex = _selected; ShowDetail(); RefreshValidation();
    }

    // ---------- 详情 ----------
    void ShowDetail()
    {
        _detail.Children.Clear();
        if (_selected < 0 || _selected >= _doc.Bindings.Count)
        {
            _detail.Children.Add(new TextBlock { Text = "选择左侧一条绑定进行编辑，或点「添加绑定」。", Foreground = _pal.B(_pal.TextDim), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            return;
        }
        var b = _doc.Bindings[_selected];

        // 启用/禁用此绑定（A1）：禁用=保留配置但不触发，无需删除
        _detail.Children.Add(MakeToggle("启用此绑定", v => { b.Enabled = v; RefreshList(); RefreshValidation(); }, out _, b.Enabled));

        // 组合键捕获
        _detail.Children.Add(Label("组合键"));
        _chordBox = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Background = _pal.B(_pal.CardBg), Padding = new Thickness(12, 9, 12, 9), Cursor = Cursors.Hand, Focusable = true };
        _chordStroke = new SolidColorBrush(_pal.Stroke); _chordBox.BorderBrush = _chordStroke;
        _chordHover = _chordFocus = false;
        _chordText = new TextBlock { Foreground = _pal.B(_pal.Text), FontSize = 14 };
        _chordBox.Child = _chordText;
        UpdateChordText();
        _chordBox.MouseLeftButtonDown += (_, _) => BeginCapture();
        _chordBox.KeyDown += (_, e) => { if (!_capturing && e.Key is Key.Space or Key.Enter) { BeginCapture(); e.Handled = true; } };
        // hover 与键盘焦点共用一套描边刷新：聚焦或悬停任一成立即转 accent；R161 改颜色过渡（此前硬切换生硬）
        void RefreshChordBorder() { if (!_capturing) AnimateColor(_chordStroke, (_chordHover || _chordFocus) ? _pal.Accent : _pal.Stroke, 140); }
        _chordBox.MouseEnter += (_, _) => { _chordHover = true; RefreshChordBorder(); };
        _chordBox.MouseLeave += (_, _) => { _chordHover = false; RefreshChordBorder(); };
        _chordBox.GotKeyboardFocus += (_, _) => { _chordFocus = true; RefreshChordBorder(); };
        _chordBox.LostKeyboardFocus += (_, _) => { _chordFocus = false; RefreshChordBorder(); };
        _detail.Children.Add(_chordBox);

        // 触发方式
        _detail.Children.Add(Label("触发方式"));
        var trig = MakeCombo(Enum.GetValues<TriggerKind>().Cast<object>(), TriggerLabel, b.Trigger, v => { b.Trigger = (TriggerKind)v; RefreshList(); RefreshValidation(); });
        _detail.Children.Add(trig);

        // 动作序列 + 完成后序列（A3）：同一套列表编辑器，复用于两处
        StackPanel BuildActionList(List<string> list, string title, string addLabel)
        {
            var root = new StackPanel();
            root.Children.Add(Label(title));
            var panel = new StackPanel();
            void Rebuild()
            {
                panel.Children.Clear();
                for (int i = 0; i < list.Count; i++)
                {
                    int idx = i;
                    // R161：已配置动作行卡片化——与旁边动作下拉等高（MinHeight 34）、序号做成独立徽标、
                    // 加边框+底色；此前是裸文本行「1. xxx」，太不显眼且序号和内容糊在一起。
                    var row = new Border
                    {
                        Margin = new Thickness(0, 2, 0, 2),
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        BorderBrush = _pal.B(_pal.Stroke),
                        Background = _pal.B(_pal.CardBg),
                        Padding = new Thickness(8, 0, 6, 0),
                        MinHeight = 34,
                    };
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var badge = new Border
                    {
                        Width = 20, Height = 20, CornerRadius = new CornerRadius(5),
                        Background = _pal.B(_pal.TrackBg), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, Foreground = _pal.B(_pal.TextDim), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    };
                    var txt = new TextBlock { Text = ActionLabel(list[i]), Foreground = _pal.B(_pal.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                    var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var up = MiniBtn("\uE70E", () => { if (idx > 0) { (list[idx - 1], list[idx]) = (list[idx], list[idx - 1]); Rebuild(); RefreshList(); } });
                    var dn = MiniBtn("\uE70D", () => { if (idx < list.Count - 1) { (list[idx + 1], list[idx]) = (list[idx], list[idx + 1]); Rebuild(); RefreshList(); } });
                    var rm = MiniBtn("\uE74D", () => { list.RemoveAt(idx); Rebuild(); RefreshList(); RefreshValidation(); });
                    ctl.Children.Add(up); ctl.Children.Add(dn); ctl.Children.Add(rm);
                    Grid.SetColumn(badge, 0); Grid.SetColumn(txt, 1); Grid.SetColumn(ctl, 2);
                    g.Children.Add(badge); g.Children.Add(txt); g.Children.Add(ctl);
                    row.Child = g;
                    panel.Children.Add(row);
                }
                UpdateAfterSec();   // R161：序列增删/换序后重判「截图后」是否还该显示
            }
            Rebuild();
            root.Children.Add(panel);
            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var actCombo = new ComboBox { Width = 200 };
            EditorStyles.ApplyCombo(actCombo, _pal);
            foreach (var a in _catalog.Actions()) actCombo.Items.Add(new ComboBoxItem { Content = a.Label, Tag = a.Name, ToolTip = a.Description });
            if (actCombo.Items.Count > 0) actCombo.SelectedIndex = 0;
            var addAct = MakeButton(addLabel, ghost: true);
            addAct.Margin = new Thickness(8, 0, 0, 0);   // R161：与下拉隔开边距（此前右外边距，按钮直接黏住下拉框）
            addAct.Click += (_, _) => { if (actCombo.SelectedItem is ComboBoxItem it && it.Tag is string n) { list.Add(n); Rebuild(); RefreshList(); RefreshValidation(); } };
            addRow.Children.Add(actCombo); addRow.Children.Add(addAct);
            root.Children.Add(addRow);
            return root;
        }
        _detail.Children.Add(BuildActionList(b.Actions, "动作序列", "添加动作"));
        _detail.Children.Add(BuildActionList(b.OnComplete, "完成后序列", "添加完成动作"));

        // 截图后（R157）：本条手势触发截图完成后的行为；空=跟随默认（弹提示）。
        // R161：序列里没有截图类动作时整段隐藏（录制/编辑器/OCR 之外的手势配它没意义，徒增困惑）。
        _afterSec = new StackPanel();
        _afterSec.Children.Add(Label("截图后"));
        var afterOpts = new object[] { "", "toast", "editor", "copy", "pin" };
        _afterSec.Children.Add(MakeCombo(afterOpts, v => AfterCaptureLabel((string)v), b.AfterCapture, v => { b.AfterCapture = (string)v; RefreshList(); }));
        _detail.Children.Add(_afterSec);
        UpdateAfterSec();

        // 失败策略 + 优先级
        var two = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fbCol = new StackPanel(); fbCol.Children.Add(Label("失败策略"));
        fbCol.Children.Add(MakeCombo(Enum.GetValues<FallbackPolicy>().Cast<object>(), FallbackLabel, b.Fallback, v => b.Fallback = (FallbackPolicy)v));
        var prCol = new StackPanel(); prCol.Children.Add(Label("优先级"));
        prCol.Children.Add(MakeNum(b.Priority.ToString(), s => { if (int.TryParse(s, out var p)) b.Priority = p; }));
        Grid.SetColumn(fbCol, 0); Grid.SetColumn(prCol, 2);
        two.Children.Add(fbCol); two.Children.Add(prCol);
        _detail.Children.Add(two);

        // 时间参数
        var two2 = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        two2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        two2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        two2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dcCol = new StackPanel(); dcCol.Children.Add(Label("双击间隔 ms"));
        dcCol.Children.Add(MakeNum(b.DoubleClickMs.ToString(), s => { if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) b.DoubleClickMs = v; RefreshValidation(); }));
        var lpCol = new StackPanel(); lpCol.Children.Add(Label("长按阈值 ms"));
        lpCol.Children.Add(MakeNum(b.LongPressMs.ToString(), s => { if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) b.LongPressMs = v; RefreshValidation(); }));
        Grid.SetColumn(dcCol, 0); Grid.SetColumn(lpCol, 2);
        two2.Children.Add(dcCol); two2.Children.Add(lpCol);
        _detail.Children.Add(two2);
    }

    /// <summary>R161：序列（动作+完成后）是否含截图类动作——由宿主目录的 IsCapture 标记决定，库不硬编码动作名。</summary>
    bool HasCaptureAction(BindingEdit b)
    {
        var caps = new HashSet<string>(_catalog.Actions().Where(a => a.IsCapture).Select(a => a.Name));
        return b.Actions.Any(caps.Contains) || b.OnComplete.Any(caps.Contains);
    }

    void UpdateAfterSec()
    {
        if (_afterSec == null) return;   // ShowDetail 首建期间（动作列表先于本段构建）
        bool show = _selected >= 0 && _selected < _doc.Bindings.Count && HasCaptureAction(_doc.Bindings[_selected]);
        _afterSec.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateChordText()
    {
        if (_selected < 0 || _selected >= _doc.Bindings.Count) return;
        var b = _doc.Bindings[_selected];
        var s = b.Chord.Format(_namer);
        _chordText.Text = _capturing ? "按下新键…（Esc 取消）" : (string.IsNullOrEmpty(s) ? "点击设置" : s);
        _chordText.Foreground = _pal.B(_capturing ? _pal.Accent : _pal.Text);
        if (_chordStroke != null)
            AnimateColor(_chordStroke, _capturing || _chordHover || _chordFocus ? _pal.Accent : _pal.Stroke, 140);
    }

    // ---------- 组合键捕获 ----------
    void BeginCapture()
    {
        EndCapture();
        _capturing = true;
        ChordCaptureBegan?.Invoke();   // 宿主暂停全局钩子，否则组合键被吞、编辑器收不到
        UpdateChordText();
        _chordBox.Focus();
    }

    void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        ChordCaptureEnded?.Invoke();   // 宿主恢复全局钩子
        UpdateChordText();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturing)
        {
            e.Handled = true;
            if (e.Key == Key.Escape) { EndCapture(); return; }
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return; // 等主键
            var mods = e.KeyboardDevice.Modifiers;
            bool isF = e.Key >= Key.F1 && e.Key <= Key.F24;
            if (!isF && mods == System.Windows.Input.ModifierKeys.None) return; // 非 F 键须带修饰键
            if (isF) mods = System.Windows.Input.ModifierKeys.None;
            if (_selected >= 0 && _selected < _doc.Bindings.Count)
            {
                var b = _doc.Bindings[_selected];
                b.Mods = ToGcMods(mods);
                b.Key = KeyInterop.VirtualKeyFromKey(e.Key);
            }
            EndCapture();
            RefreshList(); ShowDetail(); RefreshValidation();
            return;
        }
        // 非录键态：Esc = 取消关闭（走 Closing 淡出退场），与「取消」钮及退场注释一致（R154）
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    static GcMods ToGcMods(System.Windows.Input.ModifierKeys m)
    {
        GcMods r = GcMods.None;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Control)) r |= GcMods.Control;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Alt)) r |= GcMods.Alt;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Shift)) r |= GcMods.Shift;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Windows)) r |= GcMods.Win;
        return r;
    }

    // ---------- 校验 + 保存 ----------
    void RefreshValidation()
    {
        var problems = GestureValidation.Validate(_doc, _catalog, _namer);
        bool errs = GestureValidation.HasErrors(problems);
        _saveBtn.IsEnabled = !errs;
        // R161：左下角不再显示「✓ 配置有效」——有问题的那条绑定整卡红框标出，无问题即静默。
        _problemIds.Clear();
        foreach (var p in problems) _problemIds.Add(p.BindingId);
        PaintRows();
        if (problems.Count == 0)
        {
            _valText.Text = _doc.Enabled ? "" : "手势层当前关闭";
            _valText.Foreground = _pal.B(_pal.TextDim);
        }
        else
        {
            _valText.Text = string.Join("；", problems.Select(p => p.Message).Distinct().Take(3));
            _valText.Foreground = _pal.B(errs ? Danger : _pal.TextDim);
        }
    }

    void Save()
    {
        var problems = GestureValidation.Validate(_doc, _catalog, _namer);
        if (GestureValidation.HasErrors(problems)) return;
        _store.Save(_doc);
        Close();
    }

    // ---------- 控件小工具 ----------
    TextBlock Label(string t) => new() { Text = t, Foreground = _pal.B(_pal.TextDim), FontSize = 12, Margin = new Thickness(0, 12, 0, 4) };

    // R150：补 Tap/Hold/HoldRelease——旧版这三项落到默认分支与 Chord 同名「组合键（同时按下）」，
    // 触发下拉（列 Enum.GetValues<TriggerKind>() 全 10 值）出现 4 个同名项，用户无法区分轻点/按住/松手/组合键。
    // 改 public static 供 SelfTest 锁「10 项标签两两不同」不变量（纯函数，不触 WPF）。
    // R155：KeyUp 核心词「抬起」→「松开」对齐宿主 GestureConfig.TriggerLabel。
    // R161：全部改短词（「组合键按下」→「按下」等），与宿主 GestureConfig.TriggerLabel 完全同词——
    // 下拉/列表/帮助同一触发方式同一说法；T151 锁两两不同、T155 锁含宿主核心词（同词即含）。
    public static string TriggerLabel(object t) => t switch
    {
        TriggerKind.KeyDown => "按下",
        TriggerKind.KeyUp => "松开",
        TriggerKind.Click => "单击",
        TriggerKind.DoubleClick => "双击",
        TriggerKind.TripleClick => "三击",
        TriggerKind.LongPress => "长按",
        TriggerKind.Tap => "轻点",
        TriggerKind.Hold => "按住",
        TriggerKind.HoldRelease => "松手",
        _ => "组合键",
    };

    static string FallbackLabel(object f) => f switch
    {
        FallbackPolicy.SkipStep => "跳过该步",
        FallbackPolicy.NextBinding => "顺延下一条",
        _ => "失败即停",
    };

    ComboBox MakeCombo(IEnumerable<object> items, Func<object, string> disp, object sel, Action<object> onSel)
    {
        var c = new ComboBox();
        EditorStyles.ApplyCombo(c, _pal);
        foreach (var it in items) c.Items.Add(new ComboBoxItem { Content = disp(it), Tag = it });
        c.SelectedIndex = items.ToList().IndexOf(sel);
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem it && it.Tag != null) onSel(it.Tag); };
        return c;
    }

    // R161：数字输入框卡片化——圆角 + hover/聚焦边框色过渡动画（此前直角、描边硬切换）。
    // 返回外层 Border（TextBox 本体去边框透明，嵌在圆角 Border 里）。
    FrameworkElement MakeNum(string val, Action<string> onChange)
    {
        var t = new TextBox { Text = val, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = _pal.B(_pal.Text), CaretBrush = _pal.B(_pal.Text), Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
        var stroke = new SolidColorBrush(_pal.Stroke);
        var border = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = stroke, Background = _pal.B(_pal.CardBg), Padding = new Thickness(8, 6, 8, 6), Child = t, Focusable = false };
        bool hov = false, foc = false;
        void Target() => AnimateColor(stroke, hov || foc ? _pal.Accent : _pal.Stroke, 140);
        border.MouseEnter += (_, _) => { hov = true; Target(); };
        border.MouseLeave += (_, _) => { hov = false; Target(); };
        t.GotKeyboardFocus += (_, _) => { foc = true; Target(); };
        t.LostKeyboardFocus += (_, _) => { foc = false; Target(); };
        border.MouseLeftButtonDown += (_, _) => t.Focus();
        t.LostFocus += (_, _) => onChange(t.Text);
        t.TextChanged += (_, _) => onChange(t.Text);
        return border;
    }

    Button MakeButton(string text, bool accent = false, bool ghost = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 7, 14, 7), Cursor = Cursors.Hand, BorderThickness = new Thickness(1), FontSize = 13 };
        b.Foreground = _pal.B(accent ? _pal.OnAccent() : _pal.Text);
        b.BorderBrush = _pal.B(ghost ? _pal.Stroke : (accent ? _pal.Accent : _pal.Stroke));
        b.Template = SimpleButtonTemplate();
        // R161：悬停/按住只改底色（带过渡），不再缩放——放大动画会压到旁边的下拉框上重叠一下，很难看
        HoverBg(b, accent ? _pal.Accent : _pal.CardBg);
        return b;
    }

    Button MiniBtn(string glyph, Action onClick)
    {
        var b = new Button { Content = glyph, FontFamily = new FontFamily(IconFontChain), FontSize = 12, Width = 26, Height = 24, Margin = new Thickness(2, 0, 0, 0), BorderThickness = new Thickness(1), Foreground = _pal.B(_pal.TextDim), Cursor = Cursors.Hand };
        b.Template = SimpleButtonTemplate();
        // R161：迷你按钮加边框 + 悬停底色过渡（此前全透明无反馈，藏在卡片里几乎不可见）
        var stroke = new SolidColorBrush(_pal.Stroke); b.BorderBrush = stroke;
        var brush = new SolidColorBrush(_pal.CardBg); b.Background = brush;   // 与所在卡片同色=视觉透明
        b.MouseEnter += (_, _) => { AnimateColor(brush, Blend(_pal.CardBg, _pal.Hover), 140); AnimateColor(stroke, _pal.Accent, 140); };
        b.MouseLeave += (_, _) => { AnimateColor(brush, _pal.CardBg, 160); AnimateColor(stroke, _pal.Stroke, 160); };
        b.PreviewMouseLeftButtonDown += (_, _) => AnimateColor(brush, Blend(_pal.CardBg, _pal.Press), 70);
        b.PreviewMouseLeftButtonUp += (_, _) => AnimateColor(brush, Blend(_pal.CardBg, _pal.Hover), 120);
        b.Click += (_, _) => onClick();
        return b;
    }

    void StyleChromeButton(Button b)
    {
        b.Template = SimpleButtonTemplate();
        HoverBg(b, _pal.PanelBg);   // 标题栏关闭钮：底色与标题栏同，悬停/按住改底色过渡（R161，不再缩放）
    }

    static ControlTemplate? _tpl;
    static ControlTemplate SimpleButtonTemplate()
    {
        if (_tpl != null) return _tpl;
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        t.VisualTree = border;
        _tpl = t;
        return t;
    }

    /// <summary>R161：按钮悬停/按住=底色过渡动画（悬停提亮、按住回落、松手回悬停态），不缩放——
    /// 缩放会压到相邻控件上重叠（用户点名「悬停后变大的动画贴在下拉框上」）。底色须传不透明实色。</summary>
    void HoverBg(Control b, Color baseC)
    {
        var brush = new SolidColorBrush(baseC); b.Background = brush;
        var hover = Blend(baseC, _pal.Hover);
        var press = Blend(baseC, _pal.Press);
        b.MouseEnter += (_, _) => AnimateColor(brush, hover, 140);
        b.MouseLeave += (_, _) => AnimateColor(brush, baseC, 160);
        b.PreviewMouseLeftButtonDown += (_, _) => AnimateColor(brush, press, 70);
        b.PreviewMouseLeftButtonUp += (_, _) => AnimateColor(brush, hover, 120);
    }

    static void AnimateColor(SolidColorBrush br, Color to, int ms) =>
        br.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    /// <summary>半透明 over 叠在不透明 src 上 → 不透明混合色（算 hover/press 底色用）。</summary>
    static Color Blend(Color src, Color over)
    {
        double t = over.A / 255.0;
        return Color.FromRgb(
            (byte)(src.R * (1 - t) + over.R * t),
            (byte)(src.G * (1 - t) + over.G * t),
            (byte)(src.B * (1 - t) + over.B * t));
    }

    StackPanel MakeToggle(string text, Action<bool> onToggle, out TextBlock state, bool initial)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 20, 0), Cursor = Cursors.Hand };
        bool on = initial, focused = false;
        var track = new Border
        {
            Width = 40, Height = 20, CornerRadius = new CornerRadius(10),
            Background = _pal.B(on ? _pal.Accent : _pal.TrackBg),
            BorderBrush = _pal.B(on ? _pal.Accent : _pal.Stroke),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true, // 键盘可达：Tab 聚焦、空格/Enter 切换（与设置窗开关同款焦点环，R69 补此前鼠标专属的缺口）
        };
        // R162：滑块恒白 + 极淡描边（QQ 式）——旧版亮色主题下黑滑块像没做适配
        var knob = new Ellipse { Width = 14, Height = 14, Fill = _pal.B(Colors.White), Stroke = _pal.B(Color.FromArgb(0x26, 0, 0, 0)), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 3, 0) };
        var knobT = new TranslateTransform { X = on ? 20 : 0 };
        knob.RenderTransform = knobT;
        var inner = new Grid(); inner.Children.Add(knob); track.Child = inner;
        state = new TextBlock { Text = (on ? "  " : "  ") + text, Foreground = _pal.B(_pal.Text), VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(track); sp.Children.Add(state);
        void SetBorder() => AnimBrush(track.BorderBrush as SolidColorBrush, focused || on ? _pal.Accent : _pal.Stroke, br => track.BorderBrush = br);
        void Flip()
        {
            on = !on;
            AnimBrush(track.Background as SolidColorBrush, on ? _pal.Accent : _pal.TrackBg, br => track.Background = br);   // R162：开关换色带过渡，不再硬切
            SetBorder();
            knobT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 20 : 0, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
            onToggle(on);
        }
        sp.MouseLeftButtonDown += (_, _) => Flip();
        track.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Flip(); e.Handled = true; } };
        track.GotKeyboardFocus += (_, _) => { focused = true; SetBorder(); };
        track.LostKeyboardFocus += (_, _) => { focused = false; SetBorder(); };
        return sp;
    }
}
