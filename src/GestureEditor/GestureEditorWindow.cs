using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using GestureCore;

namespace GestureEditor;

/// <summary>
/// 可复用手势配置器窗口（方案 C）。只依赖 GestureCore + WPF，通过四个注入点接入任意宿主：
/// <see cref="IActionCatalog"/>（动作清单）、<see cref="IGestureConfigStore"/>（配置读写）、
/// <see cref="IKeyNamer"/>（键名显示）、<see cref="EditorPalette"/>（配色）。
/// 主从布局：左=绑定列表，右=选中绑定的详情编辑；底部实时校验 + 保存/取消。
/// 组合键捕获期间触发 <see cref="ChordCaptureBegan"/>/<see cref="ChordCaptureEnded"/>，宿主据此暂停/恢复全局钩子
/// （否则要捕获的组合键会被宿主自己的钩子吞掉，编辑器收不到）。
/// </summary>
public sealed partial class GestureEditorWindow : Window
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
    bool _suppress;   // 重建列表期间屏蔽 SelectionChanged，否则重建过程会把选中位冲掉

    ListBox _list = null!;
    StackPanel _detail = null!;
    TextBlock _valText = null!;
    Button _saveBtn = null!;
    Border _chordBox = null!;
    TextBlock _chordText = null!;

    // 列表卡片化（选中/问题红框/禁用置灰逐行自绘）+「截图后」条件显示 + 输入框/按钮过渡动画
    static readonly Color Danger = Color.FromRgb(0xFF, 0x6B, 0x6B);
    readonly HashSet<string> _problemIds = new();
    readonly List<BindingRow> _rows = new();
    StackPanel _afterSec = null!;
    SolidColorBrush _chordStroke = null!;
    bool _chordHover, _chordFocus;

    /// <summary>左侧一条绑定的卡片行——选中=accent 底+明度自适应字色，问题=红框，禁用=muted 灰字。</summary>
    sealed class BindingRow
    {
        public Border Card = null!;
        public TextBlock Head = null!;
        public TextBlock Body = null!;
        public bool Problem, Enabled = true;
    }

    /// <summary>开始捕获组合键时触发：宿主必须在此暂停全局键盘钩子，否则要录的组合键会被宿主自己的钩子吞掉。</summary>
    public event Action? ChordCaptureBegan;

    /// <summary>组合键捕获结束（录成新键或 Esc 取消）时触发：宿主在此恢复全局键盘钩子。</summary>
    public event Action? ChordCaptureEnded;

    /// <summary>构造配置器窗口：读入配置、按注入配色搭出全部界面，并直接展示首条绑定的详情。
    /// 构造期即调用 <see cref="IGestureConfigStore.Load"/>，此后编辑只改内存副本，点「保存」且无错误才落盘。</summary>
    /// <param name="catalog">动作清单来源，供「添加动作」下拉与动作名校验。</param>
    /// <param name="store">配置读写：构造时 Load，仅在保存且无错误时 Save。</param>
    /// <param name="namer">键标识 → 显示名，只用于界面回显，不参与匹配。</param>
    /// <param name="palette">配色，由宿主从自身主题注入；构造期一次性取色，之后改调色板不会回灌已建好的界面。</param>
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
        // 窗口级细滚动条：列表/详情/下拉弹层全覆盖，压掉 WPF 默认的 17px 经典滚动条
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

    #region 入场/退场动效（自包含，不引用宿主 Fluent）
    const int EnterMs = 190, ExitMs = 150;   // 对齐宿主 Fluent.Motion.EnterMs/ExitMs
    bool _closing;
    /// <summary>淡入 + 轻微缩放进场，淡出后再真关。时长与缓动对齐宿主 Fluent.Motion。</summary>
    void SetupMotion()
    {
        Opacity = 0;
        // 本窗是 WindowChrome + 非分层窗，WPF 的 Opacity 动画不驱动屏幕合成（实测开合无淡入淡出）。
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
    #endregion

    #region 布局
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
        toggles.Children.Add(MakeToggle("启用手势层", v => { _doc.Enabled = v; RefreshValidation(); }, out _, _doc.Enabled));
        toggles.Children.Add(MakeToggle("独占吞键", v => { _doc.Exclusive = v; }, out _, _doc.Exclusive));
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
        // 列表项文本换行：长摘要（多动作 / 带「完成后」序列）折行显示，而非按面板宽度裁切
        // 须禁用横向滚动——否则 ScrollViewer 以无限宽测量，TextBlock 永不换行，只出横向滚动条
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
    #endregion

    #region 控件小工具
    TextBlock Label(string t) => new() { Text = t, Foreground = _pal.B(_pal.TextDim), FontSize = 12, Margin = new Thickness(0, 12, 0, 4) };

    /// <summary>触发方式 → 中文短词，用于触发下拉、列表行与摘要。
    /// 不变量：10 个触发两两不同词，且与宿主 <c>GestureConfig.TriggerLabel</c> 同词——
    /// 下拉、列表、帮助对同一触发方式必须同一说法，否则用户无法把三处对上号。未知值落到「组合键」。
    /// 纯函数、不触 WPF，供 SelfTest 直接锁这两条不变量。</summary>
    /// <param name="t"><see cref="TriggerKind"/> 值；下拉以 object 传入。</param>
    /// <returns>两两不同的中文短词；非 <see cref="TriggerKind"/> 入参返回「组合键」。</returns>
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

    // 数字输入框卡片化：圆角 + hover/聚焦时描边色 140ms 过渡。
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
        // 悬停/按住只改底色（带过渡），不缩放——放大动画会压到旁边的下拉框上重叠一下，很难看
        HoverBg(b, accent ? _pal.Accent : _pal.CardBg);
        return b;
    }

    Button MiniBtn(string glyph, Action onClick)
    {
        var b = new Button { Content = glyph, FontFamily = new FontFamily(IconFontChain), FontSize = 12, Width = 26, Height = 24, Margin = new Thickness(2, 0, 0, 0), BorderThickness = new Thickness(1), Foreground = _pal.B(_pal.TextDim), Cursor = Cursors.Hand };
        b.Template = SimpleButtonTemplate();
        // 迷你按钮带边框 + 悬停底色过渡：底色与所在卡片同色（视觉透明），靠描边和 hover 提亮才看得见
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
        HoverBg(b, _pal.PanelBg);   // 标题栏关闭钮：底色与标题栏同，悬停/按住只改底色过渡，不缩放
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

    /// <summary>按钮悬停/按住=底色过渡动画（悬停提亮、按住回落、松手回悬停态），不缩放——
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

    /// <summary>造一个开关（轨道 + 滑块 + 右侧文字）。整行可点，轨道可聚焦并响应空格/Enter。</summary>
    /// <param name="text">开关右侧文字。</param>
    /// <param name="onToggle">状态翻转回调，参数为翻转后的开关值。</param>
    /// <param name="state">输出：右侧文字元素，调用方可改文案；开头两个空格是它与轨道之间的间隙。</param>
    /// <param name="initial">初始状态。</param>
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
            Focusable = true, // 键盘可达：Tab 聚焦、空格/Enter 切换，与设置窗开关同款焦点环
        };
        // 滑块恒白 + 极淡描边：明暗主题下都保持可辨，不随 Accent 或主题色走
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
            AnimBrush(track.Background as SolidColorBrush, on ? _pal.Accent : _pal.TrackBg, br => track.Background = br);   // 开关换色走 140ms 颜色过渡
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
    #endregion

}
