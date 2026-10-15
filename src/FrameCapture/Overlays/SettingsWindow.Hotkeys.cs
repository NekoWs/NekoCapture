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
/// 后面的动作等范围落地才执行。「完成后」「失败时」是可选覆盖，不填就走产品的默认落点。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>展开着的那条快捷键的 Id，空串表示全部折叠。列表重建后据此把展开位放回去。</summary>
    /// <value>由行首的点击维护；删除展开中的那条时清空。</value>
    string _hotOpen = "";

    /// <summary>「完成后」「失败时」是否处于高级形态：简单形态只显示落点，高级形态铺出可编辑的序列。
    /// 整页一个开关，切语言或重建列表不重置。</summary>
    /// <value>默认 false，即折叠到只显示默认落点。</value>
    bool _hotAdvanced;

    /// <summary>快捷键行的容器，重建时清空重铺。</summary>
    StackPanel? _hotList;

    /// <summary>每条快捷键的键位录入框，按绑定 Id 索引。新增后据此让新行当场进入录入态。</summary>
    readonly Dictionary<string, ChordCaptureBox> _hotBoxes = new();

    /// <summary>把界面上的一小块按 <see cref="_hotAdvanced"/> 重铺的回调，简单/高级切换时统一走一遍。</summary>
    readonly List<Action> _hotModeSync = new();

    /// <summary>开始录入组合键那一刻键盘钩子的状态，录入结束原样还回去。</summary>
    bool _hotHookWasPaused;

    /// <summary>装配快捷键页：总开关在最上，其下是逐条快捷键。</summary>
    /// <param name="panel">本页的内容容器，编辑器键位一节由调用方接在后面。</param>
    void BuildHotkeys(StackPanel panel)
    {
        panel.Children.Add(ToggleRow("\uE765", Loc.T("启用快捷键"),
            Loc.T("关掉后快捷键整体不参与：不吞键、不触发，按键照常交给当前窗口"),
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
            Margin = new Thickness(2, 4, 2, 0),
            Background = new SolidColorBrush(Theme.Hover),
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
                        Text = Loc.T("添加快捷键"),
                        FontSize = 12.5,
                        Foreground = new SolidColorBrush(Theme.Text),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
            ToolTip = Loc.T("新增一条：先给它按一个组合键，再挑要执行的动作"),
        };
        Fluent.ChipHover(add);
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

    /// <summary>一条快捷键的一行：折叠态是「名字 + 键帽」，展开态补上启用、名字、触发方式与三段序列。</summary>
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
            Width = 20,
            TextAlignment = TextAlignment.Center,
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

        var head = new Grid { Margin = new Thickness(4, 7, 4, 7), Cursor = Cursors.Hand };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(chevron, 0); Grid.SetColumn(name, 1); Grid.SetColumn(suffix, 2); Grid.SetColumn(box, 3);
        head.Children.Add(chevron); head.Children.Add(name); head.Children.Add(suffix); head.Children.Add(box);

        var body = HotkeyBody(b, () => name.Text = HotkeyDisplayName(b));
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 1, 0, 1),
            Background = Brushes.Transparent,
            Child = stack,
            Opacity = b.Enabled ? 1 : 0.55,   // 关掉的整行压淡，折叠态也看得出它不触发
        };
        // 整行可点展开；点在键位框上时不跟着展开——那一击是去改键的。
        head.MouseLeftButtonUp += (_, e) =>
        {
            if (InBox(e.OriginalSource as DependencyObject)) return;
            open = !open;
            _hotOpen = open ? b.Id : "";
            chevron.Text = open ? "\uE70E" : "\uE70D";
            if (open)
            {
                body.Visibility = Visibility.Visible;
                body.Opacity = 0;
                body.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1,
                    new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else body.Visibility = Visibility.Collapsed;
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
            EmptyHint = Loc.T("点击按键"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0),
        };
        box.CaptureBegan += () => { _hotHookWasPaused = HotkeyManager.Paused; HotkeyManager.Paused = true; };
        box.CaptureEnded += () => HotkeyManager.Paused = _hotHookWasPaused;
        box.Picked += chord => { b.Chord = chord; CommitHotkeys(); };
        return box;
    }

    /// <summary>展开态的内容：启用、名字、触发方式、触发时，以及按简单/高级铺开的完成后与失败时。</summary>
    /// <param name="b">这条绑定。</param>
    /// <param name="onName">名字改动时同步折叠行标题。</param>
    /// <returns>尚未展开的正文容器。</returns>
    FrameworkElement HotkeyBody(GestureBindingDto b, Action onName)
    {
        var body = new StackPanel { Margin = new Thickness(24, 0, 4, 8) };

        body.Children.Add(ToggleRow("\uE7B5", Loc.T("启用这条"),
            Loc.T("关掉即保留配置但不触发，无需删除"), b.Enabled, v =>
            {
                b.Enabled = v;
                CommitHotkeys();
                RebuildHotkeys();   // 折叠行的淡与不淡跟着状态走
            }));

        body.Children.Add(NameRow(b, onName));
        body.Children.Add(TriggerRow(b));

        string[] chain = b.Actions;
        body.Children.Add(ChainRow(Loc.T("触发时"),
            () => chain,
            i => ActionCatalog(step: true, first: i == 0),
            list => { chain = list; b.Actions = list; CommitHotkeys(); }));

        var switchRow = AdvancedSwitch();
        string[] done = b.OnComplete, fail = b.OnFail;
        body.Children.Add(OverrideRow(Loc.T("完成后"), () => done, Loc.T("默认 · 弹提示"),
            list => { done = list; b.OnComplete = list; CommitHotkeys(); }, switchRow));
        body.Children.Add(OverrideRow(Loc.T("失败时"), () => fail, Loc.T("默认 · 什么都不做"),
            list => { fail = list; b.OnFail = list; CommitHotkeys(); }, null));

        body.Children.Add(DeleteButton(b));
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
            Text = Loc.T("留空则按序列自动取名"),
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
        return LabeledRow(Loc.T("名字"), field,
            Loc.T("这条快捷键在列表里的名字；留空时按它执行的动作自动显示"));
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
                Content = k == "Chord" ? Loc.T("组合键") : Loc.T(GestureConfig.TriggerLabel(k)),
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
        return LabeledRow(Loc.T("触发方式"), combo,
            Loc.T("同一个组合键可以配多条不同触发：组合键即按即触发，轻点与按住用来让一个键分出两件事"));
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
            Loc.T("按顺序执行；点芯片换这一步，点加号在后面加一步"));

    /// <summary>「完成后」「失败时」一行：简单形态只显示落点，高级形态铺出可编辑的序列。</summary>
    /// <param name="label">行标签。</param>
    /// <param name="get">取当前覆盖序列，空数组表示不覆盖。</param>
    /// <param name="defaultText">不覆盖时显示的默认落点。</param>
    /// <param name="set">改定后的写回动作，内部负责落盘。</param>
    /// <param name="trailing">行尾控件，只有「完成后」行带简单/高级开关；传 null 则行尾留空。</param>
    /// <returns>一行容器。</returns>
    FrameworkElement OverrideRow(string label, Func<string[]> get, string defaultText,
        Action<string[]> set, FrameworkElement? trailing)
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

        void ApplyMode()
        {
            summary.Visibility = _hotAdvanced ? Visibility.Collapsed : Visibility.Visible;
            editor.Root.Visibility = _hotAdvanced ? Visibility.Visible : Visibility.Collapsed;
            editor.Render();
            Paint();
        }
        _hotModeSync.Add(ApplyMode);
        ApplyMode();

        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 行尾控件列
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
        if (trailing != null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Top;
            trailing.Margin = new Thickness(10, 3, 0, 0);
            Grid.SetColumn(trailing, 2);
            g.Children.Add(trailing);
        }
        g.Margin = new Thickness(0, 5, 0, 5);
        return g;
    }

    /// <summary>简单/高级切换：两枚芯片一组，选中那枚用强调色底 + 反白字。切的是完成后与失败时的形态。</summary>
    /// <returns>切换控件。</returns>
    FrameworkElement AdvancedSwitch()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var chips = new List<(Border chip, bool adv)>
        {
            (Chip(Loc.T("简单"), Loc.T("只显示默认落点，用不到的不必展开"), !_hotAdvanced), false),
            (Chip(Loc.T("高级"), Loc.T("逐条编辑完成后与失败时要跑的动作"), _hotAdvanced), true),
        };
        void Pick(bool to)
        {
            _hotAdvanced = to;
            RefreshChips(chips, to);
            foreach (var sync in _hotModeSync) sync();
        }
        foreach (var (chip, adv) in chips) WireChip(chip, () => Pick(adv));
        row.Children.Add(chips[0].chip);
        row.Children.Add(chips[1].chip);
        _hotModeSync.Add(() => RefreshChips(chips, _hotAdvanced));
        return row;
    }

    /// <summary>正文末尾的「删除这条快捷键」：图标取强调色，文字取正文色——浅色主题下强调色是亮蓝，
    /// 整句都用它会糊在卡片上。</summary>
    /// <param name="b">这条绑定。</param>
    /// <returns>按钮控件。</returns>
    FrameworkElement DeleteButton(GestureBindingDto b)
    {
        var btn = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Cursor = Cursors.Hand,
            Children =
            {
                new TextBlock
                {
                    Text = "\uE74D",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Fluent.AccentText()),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 7, 0),
                },
                new TextBlock
                {
                    Text = Loc.T("删除这条快捷键"),
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(Theme.Text),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };
        btn.MouseLeftButtonUp += (_, _) =>
        {
            _settings.Mutate(s => s.Gestures.Bindings.RemoveAll(x => ReferenceEquals(x, b)));
            AppHost.Current.InstallHotkeys();
            if (_hotOpen == b.Id) _hotOpen = "";
            RebuildHotkeys();
        };
        return btn;
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
    /// 自写盘会被文件 watcher 因 RecentlySelfWritten 跳过，不自己触发就要等下次启动才生效。</summary>
    void CommitHotkeys()
    {
        var cfg = _settings.Current.Gestures;
        _settings.Mutate(s => s.Gestures = cfg);
        AppHost.Current.InstallHotkeys();
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
