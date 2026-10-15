using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

// 本 partial 职责：设置 · 快捷键页——总开关 + 逐条快捷键的内联编辑。
/// <summary>
/// 设置窗的快捷键页：一条快捷键一行，折叠时只有名字与键帽，展开后逐步可改。
/// 每次改动当场写盘并热重载引擎与键盘钩子，页面上没有「保存」按钮。
/// 一条快捷键就是 <c>[选区]? → 动作 → 动作…</c>：「选区」只能排在第一步，它是整条序列唯一等待用户的地方，
/// 后面的动作等范围落地才执行。「完成后」「失败时」是可选覆盖，不填就走产品的默认落点，
/// 简单形态根本不出现，切到高级才长出来。
/// 行首那颗胶囊是这条快捷键的全部入口：名字、删除、启用开关、键位。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>展开着的那条快捷键的 Id，空串表示全部折叠。列表重建后据此把展开位放回去。</summary>
    /// <value>由行首的点击维护；删除展开中的那条时清空。</value>
    string _hotOpen = "";

    /// <summary>「完成后」「失败时」是否处于高级形态：简单形态这两行不出现，高级形态才长出来。
    /// 整页一个开关，切语言或重建列表不重置。</summary>
    /// <value>默认 false。</value>
    bool _hotAdvanced;

    /// <summary>快捷键行的容器，重建时清空重铺。</summary>
    StackPanel? _hotList;

    /// <summary>每条快捷键的键位录入框，按绑定 Id 索引。新增后据此让新行当场进入录入态。</summary>
    readonly Dictionary<string, ChordCaptureBox> _hotBoxes = new();

    /// <summary>把界面上的一小块按 <see cref="_hotAdvanced"/> 重铺的回调，简单/高级切换时统一走一遍。</summary>
    readonly List<Action> _hotModeSync = new();

    /// <summary>每行重画「撞车」描边的回调：改组合键、改触发、增删条目之后都要重新比一遍。</summary>
    readonly List<Action> _hotClashSync = new();

    /// <summary>开始录入组合键那一刻键盘钩子的状态，录入结束原样还回去。</summary>
    bool _hotHookWasPaused;

    /// <summary>装配快捷键页：总开关在最上，其下是逐条快捷键。</summary>
    /// <param name="panel">本页的内容容器，编辑器键位一节由调用方接在后面。</param>
    void BuildHotkeys(StackPanel panel)
    {
        panel.Children.Add(ToggleRow("\uE765", Loc.T(K.Hotkeys.EnableHotkeys),
            Loc.T(K.Hotkeys.OffStepsTheWholeHotkey),
            _settings.Current.Gestures.Enabled, v =>
            {
                _settings.Mutate(s => s.Gestures.Enabled = v);
                // 总开关当场生效：自写盘会被文件 watcher 跳过，等它就要到下次启动才重载。
                AppHost.Current.InstallHotkeys();
            }));

        _hotList = new StackPanel();
        var card = new Border
        {
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            Background = new SolidColorBrush(Theme.CardBg),
            Padding = new Thickness(8, 6, 8, 8),
            Child = _hotList,
        };
        Fluent.Elevate(card, blur: 24, depth: 6);
        panel.Children.Add(card);
        RebuildHotkeys();
    }

    /// <summary>按当前配置重铺快捷键列表：逐条一行，末尾一枚「添加快捷键」。
    /// 增删与改触发方式走整表重建；行内改名字、改序列由各自的编辑器就地更新。</summary>
    void RebuildHotkeys()
    {
        if (_hotList == null) return;
        _hotList.Children.Clear();
        _hotBoxes.Clear();
        _hotModeSync.Clear();
        _hotClashSync.Clear();
        foreach (var b in _settings.Current.Gestures.Bindings)
            _hotList.Children.Add(HotkeyRow(b));
        _hotList.Children.Add(AddHotkeyButton());
    }

    /// <summary>列表末尾的「添加快捷键」。</summary>
    /// <returns>尚未接事件的按钮。</returns>
    FrameworkElement AddHotkeyButton()
    {
        var add = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 4, 0, 4),
            BorderBrush = new SolidColorBrush(Theme.StrokeSoft),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = "\uE710",
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Fluent.AccentText()),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 7, 0),
                    },
                    new TextBlock
                    {
                        Text = Loc.T(K.Hotkeys.AddAHotkey),
                        FontSize = 12.5,
                        Foreground = new SolidColorBrush(Theme.Text),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
            ToolTip = Loc.T(K.Hotkeys.AddsOnePressAKey),
        };
        HoverFill(add);
        add.MouseLeftButtonUp += (_, _) => AddHotkey();
        return add;
    }

    /// <summary>追加一条空白快捷键：默认序列「选区 → 截图」，键位留空等用户按，新行当场进入录入态。</summary>
    void AddHotkey()
    {
        var b = new GestureBindingDto
        {
            Id = "user." + Guid.NewGuid().ToString("N")[..8],
            Chord = "",
            Trigger = "Chord",
            Actions = new[] { GestureActions.ScopeSelect, GestureActions.CaptureStored },
        };
        _settings.Mutate(s => s.Gestures.Bindings.Add(b));
        AppHost.Current.InstallHotkeys();
        _hotOpen = b.Id;
        RebuildHotkeys();
        if (_hotBoxes.TryGetValue(b.Id, out var box))
            // 新行刚进树，焦点要等布局走完才落得住。
            Dispatcher.BeginInvoke(new Action(box.BeginCapture), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>一条快捷键的一行。折叠态是那颗胶囊：名字、删除、启用开关、键位；
    /// 展开态在胶囊下面长出正文，高度动画走完再放开限高。</summary>
    /// <param name="b">这条绑定，就地改它再落盘。</param>
    /// <returns>行容器，可直接放进列表。</returns>
    FrameworkElement HotkeyRow(GestureBindingDto b)
    {
        bool open = _hotOpen == b.Id;
        var chevron = new TextBlock
        {
            Text = open ? "\uE70E" : "\uE70D",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 18,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var name = new TextBlock
        {
            Text = HotkeyDisplayName(b),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var suffix = new TextBlock
        {
            Text = Loc.T(GestureConfig.TriggerLabel(b.Trigger)),
            FontSize = 12,
            Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var box = NewChordBox(b);
        _hotBoxes[b.Id] = box;

        // 关掉的这条压淡：只压名字、触发后缀与键帽，开关本身保持全亮，否则看不出是谁被关掉。
        void PaintEnabled()
        {
            double o = b.Enabled ? 1 : 0.55;
            name.Opacity = o;
            suffix.Opacity = o;
            box.Opacity = o;
        }
        PaintEnabled();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var del = DeleteIconButton(b);
        var sw = EnableSwitch(b, PaintEnabled);
        Grid.SetColumn(chevron, 0); Grid.SetColumn(name, 1); Grid.SetColumn(suffix, 2);
        Grid.SetColumn(del, 3); Grid.SetColumn(sw, 4); Grid.SetColumn(box, 5);
        head.Children.Add(chevron); head.Children.Add(name); head.Children.Add(suffix);
        head.Children.Add(del); head.Children.Add(sw); head.Children.Add(box);

        // 名字与键帽坐在一条描了边的胶囊上：文字直接浮在卡片里没有落点。静止时不填底色——
        // 一列条目各自垫一块灰底会把列表切成格子；指针进来才浮出一点底色，仍然说明这一整条可点。
        var pill = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Theme.StrokeSoft),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 8, 7),
            Cursor = Cursors.Hand,
            Child = head,
        };
        HoverFill(pill);
        // 撞了车的那条描边发红：同一个组合键与触发上挂着多条，引擎按优先级与顺序只走第一条，这条永远不响。
        var pillStroke = (SolidColorBrush)pill.BorderBrush;
        void PaintClash()
        {
            bool hit = _settings.Current.Gestures.ConflictIds().Contains(b.Id);
            pill.ToolTip = hit
                ? Loc.T(K.Hotkeys.AnotherHotkeyAlreadyUsesThis)
                : null;
            pillStroke.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(
                hit ? Theme.Danger : Theme.StrokeSoft,
                new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs))));
        }
        _hotClashSync.Add(PaintClash);
        PaintClash();

        var body = HotkeyBody(b, () => name.Text = HotkeyDisplayName(b));
        body.MaxHeight = open ? double.PositiveInfinity : 0;
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        var stack = new StackPanel();
        stack.Children.Add(pill);
        stack.Children.Add(body);
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 4, 0, 4),
            Background = Brushes.Transparent,
            Child = stack,
        };
        // 整条胶囊可点展开；点在键位框上时不跟着展开——那一击是去改键的。
        pill.MouseLeftButtonUp += (_, e) =>
        {
            if (InBox(e.OriginalSource as DependencyObject)) return;
            open = !open;
            _hotOpen = open ? b.Id : "";
            chevron.Text = open ? "\uE70E" : "\uE70D";
            double w = Math.Max(240, row.ActualWidth - 28);
            if (open) PlayExpand(body, (Panel)body.Child, w);
            else PlayCollapse(body);
            e.Handled = true;
        };
        return row;
    }

    /// <summary>折叠行右侧的键位录入框：改键即落盘。录入期间暂停全局键盘钩子，
    /// 否则正要录的那个组合键会被自家钩子先吞掉。</summary>
    /// <param name="b">这条绑定。</param>
    /// <returns>已接好事件的录入框。</returns>
    ChordCaptureBox NewChordBox(GestureBindingDto b)
    {
        var box = new ChordCaptureBox
        {
            Chord = b.Chord,
            EmptyHint = Loc.T(K.Hotkeys.ClickThenPressAKey),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0),
        };
        box.CaptureBegan += () => { _hotHookWasPaused = HotkeyManager.Paused; HotkeyManager.Paused = true; };
        box.CaptureEnded += () => HotkeyManager.Paused = _hotHookWasPaused;
        box.Picked += chord => { b.Chord = chord; CommitHotkeys(); };
        return box;
    }

    /// <summary>展开态的正文：名字、触发方式、触发时，其下是简单/高级切换，
    /// 完成后与失败时只在高级形态里出现。</summary>
    /// <param name="b">这条绑定。</param>
    /// <param name="onName">名字改动时同步折叠行标题。</param>
    /// <returns>正文容器，收起状态由调用方定。</returns>
    Border HotkeyBody(GestureBindingDto b, Action onName)
    {
        var rows = new StackPanel();
        var body = new Border
        {
            ClipToBounds = true,
            Margin = new Thickness(24, 2, 4, 8),
            Child = rows,
        };
        double Width() => Math.Max(240, body.ActualWidth);

        rows.Children.Add(NameRow(b, onName));
        rows.Children.Add(TriggerRow(b));

        string[] chain = b.Actions;
        rows.Children.Add(ChainRow(Loc.T(K.Hotkeys.OnTrigger),
            () => chain,
            i => ActionCatalog(step: true, first: i == 0),
            list => { chain = list; b.Actions = list; CommitHotkeys(); }));

        rows.Children.Add(AdvancedSwitch());

        string[] done = b.OnComplete, fail = b.OnFail;
        rows.Children.Add(OverrideRow(Loc.T(K.Hotkeys.OnCompletion), () => done, Loc.T(K.Hotkeys.DefaultToast),
            list => { done = list; b.OnComplete = list; CommitHotkeys(); }, Width));
        rows.Children.Add(OverrideRow(Loc.T(K.Hotkeys.OnFailure), () => fail, Loc.T(K.Hotkeys.DefaultNothing),
            list => { fail = list; b.OnFail = list; CommitHotkeys(); }, Width));
        return body;
    }

    /// <summary>名字行：一个输入框。留空时折叠行按序列自动显示，写过之后存的就是用户原文。</summary>
    /// <param name="b">这条绑定。</param>
    /// <param name="onName">名字改动时同步折叠行标题。</param>
    /// <returns>一行容器。</returns>
    FrameworkElement NameRow(GestureBindingDto b, Action onName)
    {
        var box = new TextBox
        {
            Text = b.Name,
            FontSize = 12.5,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Theme.Text),
            CaretBrush = new SolidColorBrush(Theme.Accent),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var hint = new TextBlock
        {
            Text = Loc.T(K.Hotkeys.LeaveEmptyToNameIt),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Theme.TextDim),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        };
        var field = new Grid();
        field.Children.Add(hint);
        field.Children.Add(box);
        hint.Visibility = string.IsNullOrEmpty(b.Name) ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) =>
        {
            b.Name = box.Text;
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            onName();
            CommitHotkeys();
        };
        return LabeledRow(Loc.T(K.Hotkeys.Name), field,
            Loc.T(K.Hotkeys.TheNameThisHotkeyShows));
    }

    /// <summary>触发方式行：同一个组合键配多条不同触发，这一列就是那些变化所在。</summary>
    /// <param name="b">这条绑定。</param>
    /// <returns>一行容器。</returns>
    FrameworkElement TriggerRow(GestureBindingDto b)
    {
        var combo = new ComboBox { Width = 132, HorizontalAlignment = HorizontalAlignment.Left };
        Fluent.StyleCombo(combo);
        string[] kinds =
        {
            "Chord", "KeyDown", "KeyUp", "Tap", "Hold", "HoldRelease", "LongPress", "Click", "DoubleClick", "TripleClick",
        };
        foreach (var k in kinds)
            combo.Items.Add(new ComboBoxItem
            {
                Content = k == "Chord" ? Loc.T(K.Hotkeys.Shortcut) : Loc.T(GestureConfig.TriggerLabel(k)),
                Tag = k,
            });
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(kinds, b.Trigger));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string k } && k != b.Trigger)
            {
                b.Trigger = k;
                CommitHotkeys();
                RebuildHotkeys();   // 折叠行的触发后缀跟着改
            }
        };
        return LabeledRow(Loc.T(K.Hotkeys.Trigger), combo,
            Loc.T(K.Hotkeys.OneKeyCombinationCanCarry));
    }

    /// <summary>「触发时」一行：横向芯片序列，末尾一枚加号。</summary>
    /// <param name="label">行标签。</param>
    /// <param name="get">取当前序列。</param>
    /// <param name="candidates">给定插入位返回可挑的动作，规则见 <see cref="ActionCatalog"/>。</param>
    /// <param name="set">改定后的写回动作，内部负责落盘。</param>
    /// <returns>一行容器。</returns>
    FrameworkElement ChainRow(string label, Func<string[]> get,
        Func<int, IEnumerable<GestureAction>> candidates, Action<string[]> set) =>
        LabeledRow(label, new ChainEditor(get, candidates, set).Root,
            Loc.T(K.Hotkeys.RunsInOrderClick));

    /// <summary>「完成后」「失败时」一行：简单形态整行不出现，高级形态整行长出来，
    /// 长出来之后显示的是可编辑的序列。</summary>
    /// <param name="label">行标签。</param>
    /// <param name="get">取当前覆盖序列，空数组表示不覆盖。</param>
    /// <param name="defaultText">不覆盖时显示的默认落点。</param>
    /// <param name="set">改定后的写回动作，内部负责落盘。</param>
    /// <param name="width">这一行的排版宽度，长出来要先按它量出目标高度。</param>
    /// <returns>一行容器。</returns>
    FrameworkElement OverrideRow(string label, Func<string[]> get, string defaultText,
        Action<string[]> set, Func<double> width)
    {
        var summary = new TextBlock
        {
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var editor = new ChainEditor(get, _ => ActionCatalog(step: false, first: false), list =>
        {
            set(list);
            Paint();
        });
        void Paint()
        {
            var list = get();
            summary.Text = OverrideSummary(list, defaultText);
            summary.Foreground = new SolidColorBrush(list.Length == 0 ? Theme.TextDim : Theme.Text);
        }
        var host = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        host.Children.Add(summary);
        host.Children.Add(editor.Root);

        var g = Row();
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 14, 0),
        };
        Grid.SetColumn(host, 1);
        g.Children.Add(lbl);
        g.Children.Add(host);
        g.Margin = new Thickness(0, 5, 0, 5);

        void ApplyMode()
        {
            if (_hotAdvanced)
            {
                if (g.Visibility != Visibility.Visible)
                {
                    if (g.IsLoaded) RevealRow(g, width());
                    else { g.MaxHeight = double.PositiveInfinity; g.Visibility = Visibility.Visible; }
                }
            }
            else if (g.Visibility == Visibility.Visible)
            {
                if (g.IsLoaded) PlayCollapse(g);
                else { g.BeginAnimation(MaxHeightProperty, null); g.MaxHeight = 0; g.Visibility = Visibility.Collapsed; }
            }
            editor.Render();
            Paint();
        }
        _hotModeSync.Add(ApplyMode);
        ApplyMode();
        return g;
    }

    /// <summary>一行的通用骨架：左侧固定标签 + 右侧内容。</summary>
    /// <param name="label">标签文字。</param>
    /// <param name="content">右侧内容。</param>
    /// <param name="tip">标签 tooltip，写明这一行管的是什么。</param>
    /// <returns>一行容器。</returns>
    static FrameworkElement LabeledRow(string label, FrameworkElement content, string tip)
    {
        var g = Row();
        g.ToolTip = tip;
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };
        Grid.SetColumn(content, 1);
        g.Children.Add(lbl);
        g.Children.Add(content);
        g.Margin = new Thickness(0, 5, 0, 5);
        return g;
    }

    /// <summary>可选动作清单。</summary>
    /// <param name="step">true = 「触发时」的某一步，false = 完成后/失败时的序列。</param>
    /// <param name="first">true = 这一步排在序列第一位。</param>
    /// <returns>可挑的动作条目。「选区」只在「触发时」的第一步出现：它是整条序列唯一的前置任务，
    /// 排在中间等于让后面的动作各等一次框选，出现在完成后更是无的放矢。</returns>
    static IEnumerable<GestureAction> ActionCatalog(bool step, bool first) =>
        GestureActions.All.Where(a => a.Name != GestureActions.ScopeSelect || (step && first));

    /// <summary>覆盖序列的显示文案：空 = 走默认落点，非空 = 动作短名以间隔号相连。</summary>
    /// <param name="list">覆盖序列。</param>
    /// <param name="defaultText">空序列时显示的默认落点。</param>
    /// <returns>一行文本。</returns>
    static string OverrideSummary(string[] list, string defaultText) =>
        list.Length == 0 ? defaultText : string.Join(" · ", list.Select(GestureActions.LabelOf));

    /// <summary>折叠行的名字：用户写过就用他的原文，没写过就按序列推导，界面语言切换时跟着换。</summary>
    /// <param name="b">这条绑定。</param>
    /// <returns>显示名。「选区」是范围而不是要做的事，推导时跳过它。</returns>
    static string HotkeyDisplayName(GestureBindingDto b)
    {
        if (!string.IsNullOrWhiteSpace(b.Name)) return b.Name;
        var verbs = b.Actions.Where(a => a != GestureActions.ScopeSelect).Select(GestureActions.LabelOf).ToList();
        return verbs.Count == 0 ? GestureActions.LabelOf(GestureActions.ScopeSelect) : string.Join(" · ", verbs);
    }

    /// <summary>把当前快捷键配置写盘，并让引擎与键盘钩子当场重载。
    /// 自写盘会被文件 watcher 因 RecentlySelfWritten 跳过，不自己触发就要等下次启动才生效。
    /// 改完顺手重画一遍撞车描边：撞不撞车取决于别的条目现在长什么样，只有整表比一次才知道。</summary>
    void CommitHotkeys()
    {
        var cfg = _settings.Current.Gestures;
        _settings.Mutate(s => s.Gestures = cfg);
        AppHost.Current.InstallHotkeys();
        foreach (var f in _hotClashSync) f();
    }

    /// <summary>事件源是否落在某个键位录入框内部，用于把这一击让给录入框而不是整行。</summary>
    /// <param name="src">事件的 OriginalSource，可能为 null。</param>
    /// <returns>命中录入框返回 true。</returns>
    static bool InBox(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is ChordCaptureBox) return true;
            src = VisualTreeHelper.GetParent(src) ?? LogicalTreeHelper.GetParent(src);
        }
        return false;
    }
}
