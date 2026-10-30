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
/// <summary>快捷键页：绑定、触发方式、行为入口与界面键位。编辑后立即保存并应用。</summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>展开着的那条快捷键的 Id，空串表示全部折叠。列表重建后据此把展开位放回去。</summary>
    /// <value>由行首的点击维护；删除展开中的那条时清空。</value>
    string _hotOpen = "";


    /// <summary>快捷键行的容器，重建时清空重铺。</summary>
    StackPanel? _hotList;

    /// <summary>每条快捷键的键位录入框，按绑定 Id 索引。新增后据此让新行当场进入录入态。</summary>
    readonly Dictionary<string, ChordCaptureBox> _hotBoxes = new();


    /// <summary>每行重画「撞车」描边的回调：改组合键、改触发、增删条目之后都要重新比一遍。</summary>
    readonly List<Action> _hotClashSync = new();

    /// <summary>开始录入组合键那一刻键盘钩子的状态，录入结束原样还回去。</summary>
    bool _hotHookWasPaused;

    /// <summary>装配快捷键页：总开关在最上，其下是逐条快捷键。</summary>
    /// <param name="panel">快捷键概要与下级入口的容器。</param>
    void BuildHotkeys(StackPanel panel)
    {
        var enabled = new StackPanel();
        enabled.Children.Add(ToggleRow("\uE765", Loc.T(K.Hotkeys.EnableHotkeys), Loc.T(K.Hotkeys.OffStepsTheWholeHotkey),
            _settings.Current.Gestures.Enabled, value =>
            {
                _settings.Mutate(s => s.Gestures.Enabled = value);
                AppHost.Current.InstallHotkeys();
            }));
        Cardify(enabled); panel.Children.Add(enabled);
        panel.Children.Add(SectionHeader(Loc.T(K.SettingsNavigation.MyHotkeys)));
        _hotList = new StackPanel(); panel.Children.Add(_hotList); RebuildHotkeys();
        panel.Children.Add(SectionHeader(Loc.T(K.SettingsNavigation.More)));
        panel.Children.Add(SettingsPageEntry("presets"));
        panel.Children.Add(SettingsPageEntry("ui-keys"));
        panel.Children.Add(SettingsPageEntry("behaviors"));
    }

    /// <summary>按当前配置重铺快捷键列表：逐条一行，末尾一枚「添加快捷键」。
    /// 增删与改触发方式走整表重建；行内改名字、改序列由各自的编辑器就地更新。</summary>
    void RebuildHotkeys()
    {
        if (_hotList == null) return;
        _hotList.Children.Clear();
        _hotRows.Clear();
        _hotBoxes.Clear();
        _hotClashSync.Clear();
        _folds.Clear();   // 行的容器整个换了一批，上一批容器的记账跟着一起丢
        foreach (var b in _settings.Current.Gestures.Bindings)
            _hotList.Children.Add(HotkeyOverviewRow(b));
        _hotList.Children.Add(AddHotkeyButton());
        RefreshSettingsBindingDetail();
    }

    /// <summary>列表末尾的「添加快捷键」。</summary>
    /// <returns>尚未接事件的按钮。</returns>
    FrameworkElement AddHotkeyButton()
    {
        var add = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 2, 0, 2),
            BorderBrush = Theme.StrokeSoftBrush,
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
                        Foreground = Theme.TextBrush,
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

    /// <summary>追加一条快捷键，连接首个行为入口并立即进入录键态。</summary>
    void AddHotkey()
    {
        var b = new GestureBindingDto
        {
            Id = "user." + Guid.NewGuid().ToString("N")[..8],
            Chord = "",
            Trigger = "Chord",
            EntryId = _settings.Current.Gestures.Graph.Flows.FirstOrDefault()?.Id ?? "",
        };
        _settings.Mutate(s => s.Gestures.Bindings.Add(b));
        AppHost.Current.InstallHotkeys();
        _hotOpen = b.Id;
        InsertHotkeyAnimated(b);
        OpenSettingsBinding(b);
        if (_hotBoxes.TryGetValue(b.Id, out var box))
        {
            var timer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs + Fluent.Motion.EnterMs) };
            timer.Tick += (_, _) => { timer.Stop(); if (box.IsVisible && _currentSettingsPage == "binding:" + b.Id) box.BeginCapture(); };
            timer.Start();
        }
    }

    /// <summary>一条快捷键的一行。折叠态是那颗胶囊：名字、删除、启用开关、键位；
    /// 展开态在胶囊下面长出正文，高度动画走完再放开限高。</summary>
    /// <param name="b">这条绑定，就地改它再落盘。</param>
    /// <param name="detail">是否在独立详情页保持展开。</param>
    /// <returns>行容器，可直接放进列表。</returns>
    FrameworkElement HotkeyRow(GestureBindingDto b, bool detail = false)
    {
        bool open = detail || _hotOpen == b.Id;
        var chevron = new TextBlock
        {
            Text = open ? "\uE70E" : "\uE70D",
            Visibility = detail ? Visibility.Collapsed : Visibility.Visible,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
            FontSize = 11,
            Foreground = Theme.TextDimBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 18,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var nameText = new TextBlock
        {
            Text = HotkeyDisplayName(b),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var suffix = new TextBlock
        {
            Text = Loc.T(GestureConfig.TriggerLabel(b.Trigger)), FontSize = 12,
            Foreground = Theme.TextDimBrush, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        // 名称按可用宽度省略，触发方式与改名按钮紧随名称。
        var nameCell = new Grid { VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
        nameCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameCell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameCell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Border edit = null!;
        void ShowName()
        {
            nameCell.Children.Clear();
            nameCell.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(nameText, 0);
            Grid.SetColumn(suffix, 1); Grid.SetColumn(edit, 2);
            nameCell.Children.Add(nameText);
            nameCell.Children.Add(suffix);
            nameCell.Children.Add(edit);
        }
        void BeginRename()
        {
            nameCell.Children.Clear();
            nameCell.HorizontalAlignment = HorizontalAlignment.Stretch;
            var rb = RenameBox(nameText.Text, v =>
            {
                b.Name = v.Trim();
                nameText.Text = HotkeyDisplayName(b);
                CommitHotkeys();
                ShowName();
            }, ShowName);
            Grid.SetColumn(rb, 0);
            Grid.SetColumnSpan(rb, 3);
            rb.SetBinding(MaxWidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = nameCell });
            nameCell.Children.Add(rb);
        }
        edit = RenameIconButton(BeginRename);
        ShowName();
        var box = NewChordBox(b);
        _hotBoxes[b.Id] = box;

        // 关掉的这条压淡：只压名字、触发后缀与键帽，开关本身保持全亮，否则看不出是谁被关掉。
        void PaintEnabled()
        {
            double o = b.Enabled ? 1 : 0.55;
            nameText.Opacity = o;
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
        var del = DeleteIconButton(b);
        var sw = EnableSwitch(b, PaintEnabled);
        Grid.SetColumn(chevron, 0); Grid.SetColumn(nameCell, 1);
        Grid.SetColumn(del, 2); Grid.SetColumn(sw, 3); Grid.SetColumn(box, 4);
        head.Children.Add(chevron); head.Children.Add(nameCell);
        head.Children.Add(del); head.Children.Add(sw); head.Children.Add(box);

        // 名字与键帽坐在一条胶囊上：文字直接浮在卡片里没有落点。胶囊比卡片亮一档（暗色）/暗一档（亮色），
        // 边界先由明度说话，描边只留一根细线；台阶跨得太大会把列表切成一格一格，一档刚好分得出层又不像格子。
        // 指针进来再浮出一点底色，仍然说明这一整条可点。
        var pill = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = Theme.StrokeSoftBrush,
            BorderThickness = new Thickness(1),
            Background = Theme.RowBgBrush,
            Padding = new Thickness(10, 4, 8, 4),
            Cursor = Cursors.Hand,
            Child = head,
        };
        HoverFill(pill, () => Theme.RowBg, () => Theme.RowHover);
        // 撞了车的那条描边发红：同一个组合键与触发上挂着多条，引擎按优先级与顺序只走第一条，这条永远不响。
        // 描边用这一行自己的画刷：共享实例上挂动画等于让全窗所有软描边一起发红。
        var pillStroke = new SolidColorBrush(Theme.StrokeSoft);
        pill.BorderBrush = pillStroke;
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

        var body = HotkeyBody(b);
        body.MaxHeight = open ? double.PositiveInfinity : 0;
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        var stack = new StackPanel();
        stack.Children.Add(pill);
        stack.Children.Add(body);
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 2, 0, 2),
            Background = Brushes.Transparent,
            Child = stack,
        };
        // 整条胶囊可点展开；点在键位框上时不跟着展开——那一击是去改键的。
        pill.MouseLeftButtonUp += (_, e) =>
        {
            if (detail || InBox(e.OriginalSource as DependencyObject)) return;
            open = !open;
            _hotOpen = open ? b.Id : "";
            chevron.Text = open ? "\uE70E" : "\uE70D";
            double w = Math.Max(240, row.ActualWidth - 28);
            if (open) PlayExpand(body, (Panel)body.Child, w);
            else PlayCollapse(body);
            e.Handled = true;
        };
        _hotRows[b.Id] = row;
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

    /// <summary>绑定正文：触发方式、时长与共享行为入口。</summary>
    /// <param name="b">绑定。</param>
    /// <param name="includeEntry">是否包含共享行为入口选择。</param>
    /// <returns>正文容器。</returns>
    Border HotkeyBody(GestureBindingDto b, bool includeEntry = true)
    {
        var rows = new StackPanel();
        var body = new Border
        {
            ClipToBounds = true,
            Margin = new Thickness(24, 2, 4, 8),
            Child = rows,
        };

        rows.Children.Add(TriggerRow(b));

        if (b.Trigger is "Tap" or "Hold" or "HoldRelease" or "LongPress")
            rows.Children.Add(GestureTimingRow(K.Behaviors.HoldMs, b.LongPressMs, 100, 5000, value =>
            {
                var chord = b.ToBinding()?.Chord;
                foreach (var peer in _settings.Current.Gestures.Bindings.Where(x => x.ToBinding()?.Chord.Equals(chord) == true))
                    peer.LongPressMs = value;
                b.LongPressMs = value; CommitHotkeys();
            }));
        if (b.Trigger is "Click" or "DoubleClick" or "TripleClick")
            rows.Children.Add(GestureTimingRow(K.Behaviors.ClickMs, b.DoubleClickMs, 50, 2000,
                value => { b.DoubleClickMs = value; CommitHotkeys(); }));
        if (includeEntry) rows.Children.Add(BehaviorEntryRow(b.EntryId, id =>
        {
            b.EntryId = id;
            if (id.Length == 0) { b.Actions = b.OnFail = Array.Empty<string>(); b.OnComplete = null; }
            CommitHotkeys();
        }));
        return body;
    }

    FrameworkElement GestureTimingRow(Str label, int current, int min, int max, Action<int> save)
    {
        var combo = new ComboBox { IsEditable = true, Text = current.ToString(),
            HorizontalAlignment = HorizontalAlignment.Left, ToolTip = Loc.T(K.Behaviors.TimingHint) };
        foreach (int value in new[] { 150, 300, 400, 500, 750, 1000 }) combo.Items.Add(value.ToString());
        Fluent.StyleComboEditable(combo);
        combo.LostKeyboardFocus += (_, _) =>
        {
            if (!int.TryParse(combo.Text, out int value)) value = current;
            value = Math.Clamp(value, min, max); combo.Text = value.ToString(); save(value);
        };
        return LabeledRow(Loc.T(label), combo, Loc.T(K.Behaviors.TimingHint));
    }

    /// <summary>名字右侧那枚小编辑按钮：只有图标，「改名」这句话交给 tooltip。</summary>
    /// <param name="onClick">点下要跑的动作，即把名字换成输入框。</param>
    /// <returns>图标按钮。</returns>
    static Border RenameIconButton(Action onClick)
    {
        var btn = new Border
        {
            Width = 22,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "\uE70F",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                FontSize = 11,
                Foreground = Theme.TextDimBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            ToolTip = Loc.T(K.Hotkeys.RenameThisHotkey),
        };
        Fluent.ChipHover(btn);
        // 改名不是展开这一行：这一击停在按钮上，别冒到胶囊去
        btn.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
        return btn;
    }

    /// <summary>触发方式行：同一个组合键配多条不同触发，这一列就是那些变化所在。</summary>
    /// <param name="b">这条绑定。</param>
    /// <returns>一行容器。</returns>
    FrameworkElement TriggerRow(GestureBindingDto b)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left };
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
            Loc.T(K.Hotkeys.RunsInOrderDragToReorder));

    /// <summary>失败链编辑行；空链不执行失败动作。</summary>
    /// <param name="label">行标签。</param>
    /// <param name="get">读取序列。</param>
    /// <param name="defaultText">空链提示。</param>
    /// <param name="set">保存序列。</param>
    /// <returns>编辑行。</returns>
    FrameworkElement OverrideRow(string label, Func<string[]> get, string defaultText, Action<string[]> set)
    {
        var host = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        var editor = new ChainEditor(get, _ => ActionCatalog(), list =>
        {
            set(list);
            Paint();
        });
        // 不覆盖时落在哪交给提示——多摆一行字会把「这里能放几步」的那点版面挤没。
        void Paint() => host.ToolTip = get().Length == 0 ? defaultText : null;
        host.Children.Add(editor.Root);

        var g = Row();
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 14, 0),
        };
        Grid.SetColumn(host, 1);
        g.Children.Add(lbl);
        g.Children.Add(host);
        g.Margin = new Thickness(0, 5, 0, 5);
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
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };
        Grid.SetColumn(content, 1);
        g.Children.Add(lbl);
        g.Children.Add(content);
        g.Margin = new Thickness(0, 5, 0, 5);
        return g;
    }

    /// <summary>动作编辑器的完整目录。</summary>
    /// <returns>按稳定顺序排列的规范动作。</returns>
    static IEnumerable<GestureAction> ActionCatalog() =>
        GestureActions.All;

    /// <summary>折叠行的名字：用户写过就是他的原文；没写过时，出厂那两条按界面语言取自己的名字，
    /// 用户新建的取「新配置」。名字是用户数据，他改过之后就固定成他写下的那串，语言切换动不了它。</summary>
    /// <param name="b">这条绑定。</param>
    /// <returns>显示名。</returns>
    static string HotkeyDisplayName(GestureBindingDto b)
    {
        if (!string.IsNullOrWhiteSpace(b.Name)) return b.Name;
        return b.Id switch
        {
            "default.capture" => Loc.T(K.Common.Capture),
            "default.record" => Loc.T(K.Common.Recording),
            _ => Loc.T(BehaviorPresets.BindingLabel(b.Id)),
        };
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
        if (_settingsPages.TryGetValue(_currentSettingsPage, out var page))
        {
            _settingsHeader.Text = page.Label;
            if (_settingsCrumbs.Children.OfType<TextBlock>().LastOrDefault() is { } label) label.Text = page.Label;
        }
    }

    /// <summary>事件源是否落在某个正在等键的键位录入框里。窗口的 Esc 关窗挂在前置事件上，
    /// 比录入框先收到按键，据此把「解绑这个键位」那一记让给框。</summary>
    /// <param name="src">事件的 OriginalSource，可能为 null。</param>
    /// <returns>命中正在等键的录入框返回 true。</returns>
    static bool InCapturingBox(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is ChordCaptureBox box && box.Capturing) return true;
            src = VisualTreeHelper.GetParent(src) ?? LogicalTreeHelper.GetParent(src);
        }
        return false;
    }

    /// <summary>事件源是否落在某个键位录入框或输入框内部，用于把这一击让给控件而不是整行。</summary>
    /// <param name="src">事件的 OriginalSource，可能为 null。</param>
    /// <returns>命中录入框或输入框返回 true。</returns>
    static bool InBox(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is ChordCaptureBox or TextBox) return true;
            src = VisualTreeHelper.GetParent(src) ?? LogicalTreeHelper.GetParent(src);
        }
        return false;
    }
}
