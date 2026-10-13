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
        ContentRendered += (_, _) =>
        {
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
        _list.ItemTemplate = (System.Windows.DataTemplate)System.Windows.Markup.XamlReader.Parse(
            @"<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">"
            + @"<TextBlock Text=""{Binding}"" TextWrapping=""Wrap""/></DataTemplate>");
        _list.SelectionChanged += (_, _) => { if (_suppress) return; _selected = _list.SelectedIndex; ShowDetail(); };
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

    // ---------- 列表 ----------
    void RefreshList()
    {
        _suppress = true;
        _list.Items.Clear();
        foreach (var b in _doc.Bindings)
            _list.Items.Add(Summary(b));
        if (_selected >= _doc.Bindings.Count) _selected = _doc.Bindings.Count - 1;
        _list.SelectedIndex = _selected;
        _suppress = false;
    }

    string Summary(BindingEdit b)
    {
        var chord = b.Chord.Format(_namer);
        var acts = b.Actions.Count == 0 ? "（无动作）" : string.Join(" → ", b.Actions.Select(ActionLabel));
        if (b.OnComplete.Count > 0) acts += "  ⟶ 完成后: " + string.Join(" → ", b.OnComplete.Select(ActionLabel));
        var head = (b.Enabled ? "" : "已禁用 · ") + (string.IsNullOrEmpty(chord) ? "（未设键）" : chord);
        return $"{head}  ·  {TriggerLabel(b.Trigger)}\n{acts}";
    }

    string ActionLabel(string actionName) => _catalog.Actions().FirstOrDefault(a => a.Name == actionName)?.Label ?? actionName;

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
        _detail.Children.Add(Label("组合键（点击后按下新组合）"));
        _chordBox = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = _pal.B(_pal.Stroke), Background = _pal.B(_pal.CardBg), Padding = new Thickness(12, 9, 12, 9), Cursor = Cursors.Hand, Focusable = true };
        _chordText = new TextBlock { Foreground = _pal.B(_pal.Text), FontSize = 14 };
        _chordBox.Child = _chordText;
        UpdateChordText();
        _chordBox.MouseLeftButtonDown += (_, _) => BeginCapture();
        _chordBox.KeyDown += (_, e) => { if (!_capturing && e.Key is Key.Space or Key.Enter) { BeginCapture(); e.Handled = true; } };
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
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var txt = new TextBlock { Text = (i + 1) + ". " + ActionLabel(list[i]), Foreground = _pal.B(_pal.Text), VerticalAlignment = VerticalAlignment.Center };
                    var ctl = new StackPanel { Orientation = Orientation.Horizontal };
                    var up = MiniBtn("\uE70E", () => { if (idx > 0) { (list[idx - 1], list[idx]) = (list[idx], list[idx - 1]); Rebuild(); RefreshList(); } });
                    var dn = MiniBtn("\uE70D", () => { if (idx < list.Count - 1) { (list[idx + 1], list[idx]) = (list[idx], list[idx + 1]); Rebuild(); RefreshList(); } });
                    var rm = MiniBtn("\uE74D", () => { list.RemoveAt(idx); Rebuild(); RefreshList(); RefreshValidation(); });
                    ctl.Children.Add(up); ctl.Children.Add(dn); ctl.Children.Add(rm);
                    Grid.SetColumn(txt, 0); Grid.SetColumn(ctl, 1);
                    row.Children.Add(txt); row.Children.Add(ctl);
                    panel.Children.Add(row);
                }
            }
            Rebuild();
            root.Children.Add(panel);
            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var actCombo = new ComboBox { Width = 200 };
            EditorStyles.ApplyCombo(actCombo, _pal);
            foreach (var a in _catalog.Actions()) actCombo.Items.Add(new ComboBoxItem { Content = a.Label, Tag = a.Name, ToolTip = a.Description });
            if (actCombo.Items.Count > 0) actCombo.SelectedIndex = 0;
            var addAct = MakeButton(addLabel, ghost: true);
            addAct.Click += (_, _) => { if (actCombo.SelectedItem is ComboBoxItem it && it.Tag is string n) { list.Add(n); Rebuild(); RefreshList(); RefreshValidation(); } };
            addRow.Children.Add(actCombo); addRow.Children.Add(addAct);
            root.Children.Add(addRow);
            return root;
        }
        _detail.Children.Add(BuildActionList(b.Actions, "动作序列（按顺序执行）", "添加动作"));
        _detail.Children.Add(BuildActionList(b.OnComplete, "完成后序列（主动作成功后再跑，可留空）", "添加完成动作"));

        // 失败策略 + 优先级
        var two = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fbCol = new StackPanel(); fbCol.Children.Add(Label("失败策略"));
        fbCol.Children.Add(MakeCombo(Enum.GetValues<FallbackPolicy>().Cast<object>(), FallbackLabel, b.Fallback, v => b.Fallback = (FallbackPolicy)v));
        var prCol = new StackPanel(); prCol.Children.Add(Label("优先级（小者先）"));
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

    void UpdateChordText()
    {
        if (_selected < 0 || _selected >= _doc.Bindings.Count) return;
        var b = _doc.Bindings[_selected];
        var s = b.Chord.Format(_namer);
        _chordText.Text = _capturing ? "按下新键…（Esc 取消）" : (string.IsNullOrEmpty(s) ? "（未设置，点击捕获）" : s);
        _chordText.Foreground = _pal.B(_capturing ? _pal.Accent : _pal.Text);
        _chordBox.BorderBrush = _pal.B(_capturing ? _pal.Accent : _pal.Stroke);
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
        if (problems.Count == 0)
        {
            _valText.Text = _doc.Enabled ? "✓ 配置有效" : "手势层当前关闭（保存后不生效，直到启用）";
            _valText.Foreground = _pal.B(_pal.TextDim);
        }
        else
        {
            _valText.Text = string.Join("；", problems.Select(p => p.Message).Distinct().Take(3));
            _valText.Foreground = _pal.B(errs ? Color.FromRgb(0xFF, 0x6B, 0x6B) : _pal.TextDim);
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
    public static string TriggerLabel(object t) => t switch
    {
        TriggerKind.KeyDown => "组合键按下",
        TriggerKind.KeyUp => "组合键抬起",
        TriggerKind.Click => "激活时单击",
        TriggerKind.DoubleClick => "激活时双击",
        TriggerKind.TripleClick => "激活时三击",
        TriggerKind.LongPress => "激活时长按",
        TriggerKind.Tap => "轻点（短按即松）",
        TriggerKind.Hold => "按住（达阈值触发）",
        TriggerKind.HoldRelease => "松手（按住后释放）",
        _ => "组合键（同时按下）",
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

    TextBox MakeNum(string val, Action<string> onChange)
    {
        var t = new TextBox { Text = val, Background = _pal.B(_pal.CardBg), Foreground = _pal.B(_pal.Text), BorderBrush = _pal.B(_pal.Stroke), Padding = new Thickness(8, 6, 8, 6), CaretBrush = _pal.B(_pal.Text) };
        t.LostFocus += (_, _) => onChange(t.Text);
        t.TextChanged += (_, _) => onChange(t.Text);
        return t;
    }

    Button MakeButton(string text, bool accent = false, bool ghost = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 7, 14, 7), Cursor = Cursors.Hand, BorderThickness = new Thickness(1), FontSize = 13 };
        b.Background = _pal.B(accent ? _pal.Accent : _pal.CardBg);
        b.Foreground = _pal.B(accent ? _pal.OnAccent() : _pal.Text);
        b.BorderBrush = _pal.B(ghost ? _pal.Stroke : (accent ? _pal.Accent : _pal.Stroke));
        b.Template = SimpleButtonTemplate();
        return b;
    }

    Button MiniBtn(string glyph, Action onClick)
    {
        var b = new Button { Content = glyph, FontFamily = new FontFamily(IconFontChain), FontSize = 12, Width = 26, Height = 24, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = _pal.B(_pal.TextDim), Cursor = Cursors.Hand };
        StyleChromeButton(b);
        b.Click += (_, _) => onClick();
        return b;
    }

    void StyleChromeButton(Button b)
    {
        b.Template = SimpleButtonTemplate();
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

    StackPanel MakeToggle(string text, Action<bool> onToggle, out TextBlock state, bool initial)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 20, 0), Cursor = Cursors.Hand };
        bool on = initial;
        var track = new Border { Width = 40, Height = 20, CornerRadius = new CornerRadius(10), Background = _pal.B(on ? _pal.Accent : _pal.TrackBg), VerticalAlignment = VerticalAlignment.Center };
        var knob = new Ellipse { Width = 14, Height = 14, Fill = _pal.B(_pal.Text), HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(on ? 0 : 3, 0, on ? 3 : 0, 0) };
        var inner = new Grid(); inner.Children.Add(knob); track.Child = inner;
        state = new TextBlock { Text = (on ? "  " : "  ") + text, Foreground = _pal.B(_pal.Text), VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(track); sp.Children.Add(state);
        void Flip() { on = !on; track.Background = _pal.B(on ? _pal.Accent : _pal.TrackBg); knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left; knob.Margin = new Thickness(on ? 0 : 3, 0, on ? 3 : 0, 0); onToggle(on); }
        sp.MouseLeftButtonDown += (_, _) => Flip();
        return sp;
    }
}
