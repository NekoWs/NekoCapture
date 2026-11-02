using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NekoCapture.Core;
using NekoCapture.Services;

namespace NekoCapture.Overlays;

/// <summary>托盘页：图标点击与右键菜单项在同一页内编辑，顺序即菜单顺序。</summary>
public sealed partial class SettingsWindow
{
    private StackPanel? _trayMenuList;

    private void BuildTrayPage(StackPanel panel)
    {
        panel.Children.Add(SectionHeader(Loc.T(K.Tray.ClickSection)));
        var clicks = new StackPanel();
        clicks.Children.Add(TrayBehaviorRow(TriggerSources.TrayClick, K.Triggers.TrayClick));
        clicks.Children.Add(TrayBehaviorRow(TriggerSources.TrayDoubleClick, K.Triggers.TrayDoubleClick));
        Cardify(clicks);
        panel.Children.Add(clicks);
        panel.Children.Add(UiNote(Loc.T(K.Tray.ClickHint)));

        panel.Children.Add(SectionHeader(Loc.T(K.Tray.MenuSection)));
        var menu = new StackPanel();
        _trayMenuList = new StackPanel();
        menu.Children.Add(_trayMenuList);
        var add = TriggerAddButton(K.Triggers.AddMenuItem, "settings-tray-add", AddTrayMenuItem);
        add.Margin = new Thickness(0, 6, 0, 0);
        menu.Children.Add(add);
        Cardify(menu);
        panel.Children.Add(menu);
        panel.Children.Add(UiNote(Loc.T(K.Tray.MenuHint)));
        RenderTrayMenu();
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
        if (items.Count == 0)
        {
            _trayMenuList.Children.Add(UiNote(Loc.T(K.Tray.MenuEmpty)));
            return;
        }
        foreach (var binding in items) _trayMenuList.Children.Add(TrayMenuItemRow(binding, items.Count));
    }

    /// <summary>一条菜单项：图标、名称、行为、顺序与删除都在这一行里完成。</summary>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    /// <param name="total">当前菜单项总数，用来禁用越界的移动按钮。</param>
    /// <returns>一行。</returns>
    private FrameworkElement TrayMenuItemRow(GestureBindingDto binding, int total)
    {
        int index = _settings.Current.Gestures.Bindings.IndexOf(binding);
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = TrayIconButton(binding);
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        var name = TrayMenuName(binding);
        Grid.SetColumn(name, 1); grid.Children.Add(name);

        var behavior = FlowCombo(binding.EntryId, id => { binding.EntryId = id; CommitHotkeys(); });
        behavior.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(behavior, 2); grid.Children.Add(behavior);

        var order = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        order.Children.Add(TrayOrderButton("\uE74A", K.Tray.MoveUp, index <= 0, () => MoveTrayMenuItem(binding, -1)));
        order.Children.Add(TrayOrderButton("\uE74B", K.Tray.MoveDown, index < 0 || index >= total - 1, () => MoveTrayMenuItem(binding, 1)));
        Grid.SetColumn(order, 3); grid.Children.Add(order);

        var remove = TrayDeleteButton(binding);
        Grid.SetColumn(remove, 4); grid.Children.Add(remove);

        return new Border
        {
            Child = grid,
            Tag = "settings-tray-item:" + binding.Id,
            Background = Theme.RowBgBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 6, 3),
            Margin = new Thickness(0, 2, 0, 2),
        };
    }

    /// <summary>菜单项名称：点铅笔就地改名，回车或失焦落盘。</summary>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    /// <returns>名称那一格。</returns>
    private FrameworkElement TrayMenuName(GestureBindingDto binding)
    {
        var host = new ContentControl { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 210 };
        void Show()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = HotkeyDisplayName(binding), FontSize = 13, Foreground = Theme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 170,
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

    private Border TrayOrderButton(string glyph, Str tooltip, bool disabled, Action move)
    {
        var button = RenameIconButton(move);
        ((TextBlock)button.Child).Text = glyph;
        button.ToolTip = Loc.T(tooltip);
        button.IsEnabled = !disabled;
        button.Opacity = disabled ? 0.35 : 1;
        button.Width = 24; button.Height = 22;
        return button;
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
        return button;
    }

    private void MoveTrayMenuItem(GestureBindingDto binding, int delta)
    {
        _settings.Mutate(settings =>
        {
            var list = settings.Gestures.Bindings;
            int from = list.FindIndex(item => item.Id == binding.Id), to = from + delta;
            if (from < 0 || to < 0 || to >= list.Count || list[to].Source != TriggerSources.TrayMenu) return;
            list.RemoveAt(from); list.Insert(to, binding);
        });
        CommitHotkeys(); RenderTrayMenu();
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

    /// <summary>一枚菜单项图标：点一下在图标目录里换下一个，「自动」也算一项。
    /// 比展开一整块图标面板省地方，一屏能看全整个菜单。</summary>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    /// <returns>可点的图标方块。</returns>
    private Border TrayIconButton(GestureBindingDto binding)
    {
        var glyph = new TextBlock
        {
            Text = TrayMenuIcons.Glyph(_settings.Current.Gestures, binding), FontSize = 15, Foreground = Theme.TextBrush,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(6), Background = Theme.RowBgBrush,
            BorderBrush = Theme.StrokeSoftBrush, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, Focusable = true, Child = glyph,
        };
        Fluent.ChipHover(button);
        void Paint()
        {
            glyph.Text = TrayMenuIcons.Glyph(_settings.Current.Gestures, binding);
            var selected = TrayMenuIcons.All.FirstOrDefault(item => item.Id == binding.MenuIcon);
            button.ToolTip = Loc.T(K.Triggers.MenuIcon) + " · " + Loc.T(selected?.Label ?? K.Triggers.AutoIcon);
        }
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var ids = new List<string> { "" };
            ids.AddRange(TrayMenuIcons.All.Select(item => item.Id));
            int at = ids.IndexOf(binding.MenuIcon);
            binding.MenuIcon = ids[(at + 1) % ids.Count];
            CommitHotkeys(); Paint();
        };
        System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(K.Triggers.MenuIcon));
        Paint();
        return button;
    }
}
