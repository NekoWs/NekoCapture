using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NekoCapture.Core;
using NekoCapture.Services;

namespace NekoCapture.Overlays;

/// <summary>托盘页：图标点击与右键菜单项在同一页内编辑，拖动行即可改菜单顺序。</summary>
public sealed partial class SettingsWindow
{
    private StackPanel? _trayMenuList;
    private string _trayIconOpen = "";
    private string _trayDragId = "";
    private Point _trayDragFrom;
    private bool _trayDragging;

    private void BuildTrayPage(StackPanel panel)
    {
        var clicks = new StackPanel();
        clicks.Children.Add(TrayBehaviorRow(TriggerSources.TrayClick, K.Triggers.TrayClick));
        clicks.Children.Add(TrayBehaviorRow(TriggerSources.TrayDoubleClick, K.Triggers.TrayDoubleClick));
        Cardify(clicks);
        panel.Children.Add(SectionHeader(Loc.T(K.Tray.ClickSection)));
        panel.Children.Add(clicks);

        panel.Children.Add(SectionHeader(Loc.T(K.Tray.MenuSection)));
        var menu = new StackPanel();
        _trayMenuList = new StackPanel();
        menu.Children.Add(_trayMenuList);
        var add = TriggerAddButton(K.Triggers.AddMenuItem, "settings-tray-add", AddTrayMenuItem);
        add.Margin = new Thickness(0, 6, 0, 0);
        menu.Children.Add(add);
        Cardify(menu);
        panel.Children.Add(menu);
        RenderTrayMenu();
        PreviewMouseMove -= TrayDragMove; PreviewMouseMove += TrayDragMove;
        PreviewMouseLeftButtonUp -= TrayDragEnd; PreviewMouseLeftButtonUp += TrayDragEnd;
    }

    /// <summary>单击或双击托盘图标要跑的行为：一个下拉即可，不必进触发详情。</summary>
    /// <param name="source">触发来源，用于找到那一条绑定。</param>
    /// <param name="label">这一行的名称。</param>
    /// <returns>可直接放进卡片的一行。</returns>
    private FrameworkElement TrayBehaviorRow(string source, Str label)
    {
        var binding = _settings.Current.Gestures.Bindings.FirstOrDefault(item => item.Source == source);
        var combo = FlowCombo(binding?.EntryId ?? "", id =>
        {
            if (binding == null) return;
            binding.EntryId = id; CommitHotkeys();
        });
        return LabeledRow(Loc.T(label), combo, Loc.T(K.Triggers.TargetHint));
    }

    private static bool IsTrayMenuItem(GestureBindingDto binding) => binding.Source == TriggerSources.TrayMenu;

    private void RenderTrayMenu()
    {
        if (_trayMenuList == null) return;
        _trayMenuList.Children.Clear();
        var items = _settings.Current.Gestures.Bindings.Where(IsTrayMenuItem).ToList();
        if (items.Count == 0) return;
        var rows = new List<FrameworkElement>();
        foreach (var binding in items)
        {
            var row = TrayMenuItemRow(binding);
            rows.Add(row); _trayMenuList.Children.Add(row);
        }
        Fluent.CascadeIn(rows, risePx: 8, itemMs: Fluent.Motion.EnterMs,
            budgetMs: Fluent.Motion.EnterMs + Fluent.Motion.StaggerMs * 2);
    }

    /// <summary>一条菜单项：拖动手柄（图标）、名称、行为与删除都在这一行里完成。</summary>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    /// <returns>一行。</returns>
    private FrameworkElement TrayMenuItemRow(GestureBindingDto binding)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var handle = TrayIconHandle(binding);
        handle.Margin = new Thickness(0, 0, 8, 0);
        grid.Children.Add(handle);

        var name = TrayMenuName(binding);
        Grid.SetColumn(name, 1); grid.Children.Add(name);

        var behavior = FlowCombo(binding.EntryId, id => { binding.EntryId = id; CommitHotkeys(); });
        behavior.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(behavior, 2); grid.Children.Add(behavior);

        var remove = TrayDeleteButton(binding);
        Grid.SetColumn(remove, 3); grid.Children.Add(remove);

        var card = new Border
        {
            Child = grid,
            Tag = "settings-tray-item:" + binding.Id,
            Background = Theme.RowBgBrush,
            BorderBrush = Theme.StrokeSoftBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 6, 3),
            Cursor = Cursors.SizeAll,
        };
        System.Windows.Automation.AutomationProperties.SetName(card, HotkeyDisplayName(binding));
        var host = new StackPanel { Margin = new Thickness(0, 2, 0, 2), Tag = "settings-tray-host:" + binding.Id };
        host.Children.Add(card);
        if (_trayIconOpen == binding.Id) host.Children.Add(TrayIconPalette(binding));
        return host;
    }

    /// <summary>行首的图标按钮：点一下展开图标清单，再点选一枚；按住拖动则改菜单顺序。</summary>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    /// <returns>可点可拖的图标方块。</returns>
    private Border TrayIconHandle(GestureBindingDto binding)
    {
        var glyph = new TextBlock
        {
            Text = TrayMenuIcons.Glyph(_settings.Current.Gestures, binding), FontSize = 15, Foreground = Theme.TextBrush,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(6), Background = Theme.CardBgBrush,
            BorderBrush = Theme.StrokeSoftBrush, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, Focusable = true, Child = glyph,
            ToolTip = Loc.T(K.Triggers.MenuIcon),
        };
        Fluent.ChipHover(button);
        button.MouseLeftButtonDown += (_, e) =>
        {
            _trayDragId = binding.Id; _trayDragFrom = e.GetPosition(this); _trayDragging = false;
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_trayDragging) { _trayDragId = ""; return; }
            _trayIconOpen = _trayIconOpen == binding.Id ? "" : binding.Id;
            RenderTrayMenu();
        };
        return button;
    }

    /// <summary>展开的图标清单：跟随行为与目录里每一枚图标，选完收起。</summary>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    /// <returns>图标网格。</returns>
    private FrameworkElement TrayIconPalette(GestureBindingDto binding)
    {
        var grid = new UniformGrid { Columns = 8, Margin = new Thickness(38, 4, 0, 6) };
        void Pick(string id)
        {
            binding.MenuIcon = id; CommitHotkeys(); _trayIconOpen = ""; RenderTrayMenu();
        }
        Button Cell(string id, string text, Str tip)
        {
            var button = Fluent.Ui(new Button
            {
                Content = text, ToolTip = Loc.T(tip), Tag = "settings-tray-icon:" + id,
                MinWidth = 34, MinHeight = 34, Margin = new Thickness(2), Padding = new Thickness(6),
                BorderThickness = new Thickness(1),
                BorderBrush = id == binding.MenuIcon ? Theme.AccentBrush : Theme.StrokeSoftBrush,
                Background = id == binding.MenuIcon ? Theme.RowBgBrush : Brushes.Transparent,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                FontSize = 15,
            });
            System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(tip));
            button.Click += (_, _) => Pick(id);
            return button;
        }
        var auto = Cell("", Loc.T(K.Triggers.AutoIcon), K.Triggers.AutoIcon);
        auto.FontSize = 11.5; auto.FontFamily = new FontFamily("Segoe UI");
        grid.Children.Add(auto);
        foreach (var icon in TrayMenuIcons.All) grid.Children.Add(Cell(icon.Id, icon.Glyph, icon.Label));
        return grid;
    }

    /// <summary>菜单项名称：点铅笔就地改名，回车或失焦落盘。</summary>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    /// <returns>名称那一格。</returns>
    private FrameworkElement TrayMenuName(GestureBindingDto binding)
    {
        var host = new ContentControl { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 240 };
        void Show()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = HotkeyDisplayName(binding), FontSize = 13, Foreground = Theme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 190,
            });
            row.Children.Add(RenameIconButton(() =>
            {
                host.Content = RenameBox(HotkeyDisplayName(binding), value =>
                {
                    binding.Name = value.Trim(); CommitHotkeys(); Show();
                }, Show);
            }));
            host.Content = row;
        }
        Show();
        return host;
    }

    /// <summary>删除一枚菜单项并立刻重排，右键菜单下次打开就是新内容。</summary>
    /// <param name="binding">要删的那一条。</param>
    /// <returns>图标按钮。</returns>
    private Border TrayDeleteButton(GestureBindingDto binding)
    {
        var button = RenameIconButton(() =>
        {
            _settings.Mutate(settings => settings.Gestures.Bindings.RemoveAll(item => item.Id == binding.Id));
            CommitHotkeys(); RenderTrayMenu();
        });
        ((TextBlock)button.Child).Text = "\uE74D";
        button.ToolTip = Loc.T(K.Settings.DeleteThisHotkey);
        button.Width = 24; button.Height = 22;
        button.Margin = new Thickness(8, 0, 0, 0);
        return button;
    }

    /// <summary>拖动中：指针压到别的一行上就换位，松手前一直可拖。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">鼠标参数。</param>
    private void TrayDragMove(object sender, MouseEventArgs e)
    {
        if (_trayDragId.Length == 0 || _trayMenuList == null || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(this);
        if (!_trayDragging)
        {
            if (Math.Abs(at.Y - _trayDragFrom.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
            _trayDragging = true;
        }
        var rows = _trayMenuList.Children.OfType<FrameworkElement>().ToList();
        int from = rows.FindIndex(row => (row.Tag as string) == "settings-tray-host:" + _trayDragId);
        for (int i = 0; i < rows.Count; i++)
        {
            var bounds = rows[i].TransformToAncestor(this).TransformBounds(
                new Rect(0, 0, rows[i].ActualWidth, rows[i].ActualHeight));
            if (at.Y < bounds.Top || at.Y > bounds.Bottom) continue;
            if (i == from || from < 0) return;
            _settings.Mutate(settings =>
            {
                var all = settings.Gestures.Bindings;
                int moveFrom = all.FindIndex(item => item.Id == _trayDragId);
                var target = all.Where(IsTrayMenuItem).ToList();
                if (moveFrom < 0 || i >= target.Count) return;
                var moving = all[moveFrom];
                all.RemoveAt(moveFrom);
                all.Insert(all.IndexOf(target[i]), moving);
            });
            RenderTrayMenu();
            return;
        }
    }

    /// <summary>松手：结束这次拖动。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">鼠标参数。</param>
    private void TrayDragEnd(object sender, MouseButtonEventArgs e)
    {
        _trayDragId = ""; _trayDragging = false;
    }

    private void AddTrayMenuItem()
    {
        _settings.Mutate(settings => settings.Gestures.AddTrayMenu(Loc.T(K.Triggers.NewMenuItem)));
        AppHost.Current.InstallHotkeys(); RenderTrayMenu();
    }

    /// <summary>行为下拉：只列共享行为入口，空串表示不执行。</summary>
    /// <param name="selected">当前行为标识。</param>
    /// <param name="save">选中后落盘。</param>
    /// <returns>可直接放进一行右侧的下拉框。</returns>
    private ComboBox FlowCombo(string selected, Action<string> save)
    {
        var cfg = _settings.Current.Gestures;
        var combo = Fluent.Ui(new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 130 });
        combo.Items.Add(new ComboBoxItem { Content = Loc.T(K.Behaviors.Empty), Tag = "" });
        foreach (var flow in cfg.Graph.Flows)
            combo.Items.Add(new ComboBoxItem { Content = cfg.FlowName(flow), Tag = flow.Id });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? combo.Items[0];
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string id }) save(id);
        };
        return combo;
    }
}
