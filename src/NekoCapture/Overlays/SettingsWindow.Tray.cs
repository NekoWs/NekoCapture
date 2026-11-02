using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NekoCapture.Core;
using NekoCapture.Services;

namespace NekoCapture.Overlays;

/// <summary>托盘页：图标点击与右键菜单项在同一页内编辑，菜单行按住行体即可拖动排序。</summary>
public sealed partial class SettingsWindow
{
    /// <summary>菜单行的标记前缀：拖动排序按它从视觉顺序反查绑定。</summary>
    private const string TrayRowTag = "settings-tray-item:";

    /// <summary>菜单行逐项入场的间隔：一行接一行弹，整列走完仍落在一次页入场之内。</summary>
    private static readonly TimeSpan TrayRowStep = TimeSpan.FromMilliseconds(Fluent.Motion.StaggerMs * 0.7);

    private StackPanel? _trayMenuList;
    private Popup? _trayIconPopup;

    private void BuildTrayPage(StackPanel panel)
    {
        var clicks = new StackPanel();
        clicks.Children.Add(TrayBehaviorRow(TriggerSources.TrayClick, K.Triggers.TrayClick));
        clicks.Children.Add(TrayBehaviorRow(TriggerSources.TrayDoubleClick, K.Triggers.TrayDoubleClick));
        panel.Children.Add(SectionHeader(Loc.T(K.Tray.ClickSection)));
        panel.Children.Add(SettingsCard(clicks));

        panel.Children.Add(SectionHeader(Loc.T(K.Tray.MenuSection)));
        _trayMenuList = new StackPanel();
        var menu = new StackPanel();
        menu.Children.Add(_trayMenuList);
        var add = TriggerAddButton(K.Triggers.AddMenuItem, "settings-tray-add", AddTrayMenuItem);
        add.Margin = new Thickness(0, 6, 0, 0);
        menu.Children.Add(add);
        // 这张卡片自带入场：行要逐枚弹出，整块再叠一层逐项入场会一起淡出来，看不出逐项
        var card = SettingsCard(menu);
        card.Tag = SettingsEntranceManualTag;
        panel.Children.Add(card);
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

    /// <summary>按设置重建菜单行：增删项与换图标顺序都走这里，行本身不带入场，节奏由页切换给。</summary>
    private void RenderTrayMenu()
    {
        CloseTrayIconPopup();
        if (_trayMenuList == null) return;
        _trayMenuList.Children.Clear();
        foreach (var binding in _settings.Current.Gestures.Bindings.Where(IsTrayMenuItem))
            _trayMenuList.Children.Add(TrayMenuItemRow(binding));
    }

    /// <summary>菜单行逐项入场：先等卡片随页入场落位，再让每一行自己从 0.86 倍弹回原大。</summary>
    /// <param name="animate">false = 直接就位，用于不带动画的首次装配。</param>
    private void PlayTrayMenuEntrance(bool animate)
    {
        if (_trayMenuList == null) return;
        var rows = _trayMenuList.Children.OfType<FrameworkElement>().ToArray();
        for (int i = 0; i < rows.Length; i++)
        {
            if (!animate) { PopReset(rows[i]); continue; }
            PopIn(rows[i], TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs) + TrayRowStep * i);
        }
    }

    /// <summary>一条菜单项：图标、名称、行为与删除都在这一行里完成；按住行体可拖动改顺序。</summary>
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
            Tag = TrayRowTag + binding.Id,
            Background = Theme.RowBgBrush,
            BorderBrush = Theme.StrokeSoftBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 6, 3),
            Cursor = Cursors.SizeAll,
            Margin = new Thickness(0, 2, 0, 2),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        System.Windows.Automation.AutomationProperties.SetName(card, HotkeyDisplayName(binding));
        card.MouseLeftButtonDown += (_, e) => BeginTrayDrag(card, e);
        card.MouseMove += (_, e) => MoveTrayDrag(e);
        card.MouseLeftButtonUp += (_, e) => EndTrayDrag(e);
        card.LostMouseCapture += (_, _) => AbortTrayDrag(card);
        return card;
    }

    /// <summary>行首的图标按钮：点一下在指针下方弹出图标清单，选一枚即换。</summary>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    /// <returns>可点的图标方块。</returns>
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
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowTrayIconPopup(button, binding);
        };
        return button;
    }

    /// <summary>图标清单的小窗：贴在图标按钮下方，一格里一枚字形，首行是「跟随行为」。</summary>
    /// <param name="target">挂在哪个按钮下面。</param>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    private void ShowTrayIconPopup(FrameworkElement target, GestureBindingDto binding)
    {
        CloseTrayIconPopup();
        var body = new StackPanel();
        var auto = Fluent.Ui(new Button
        {
            Content = Loc.T(K.Triggers.AutoIcon), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(2, 2, 2, 6), BorderThickness = new Thickness(1),
            BorderBrush = binding.MenuIcon.Length == 0 ? Theme.AccentBrush : Theme.StrokeSoftBrush,
            Tag = "settings-tray-icon:auto",
        });
        auto.Click += (_, _) => PickTrayIcon(binding, "");
        body.Children.Add(auto);
        var grid = new UniformGrid { Columns = 6 };
        foreach (var icon in TrayMenuIcons.All)
        {
            var cell = Fluent.Ui(new Button
            {
                Content = icon.Glyph, ToolTip = Loc.T(icon.Label), Tag = "settings-tray-icon:" + icon.Id,
                Width = 36, Height = 36, Margin = new Thickness(2), Padding = new Thickness(4),
                BorderThickness = new Thickness(1), FontSize = 16,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                BorderBrush = icon.Id == binding.MenuIcon ? Theme.AccentBrush : Brushes.Transparent,
                Background = icon.Id == binding.MenuIcon ? Theme.RowBgBrush : Brushes.Transparent,
            });
            System.Windows.Automation.AutomationProperties.SetName(cell, Loc.T(icon.Label));
            cell.Click += (_, _) => PickTrayIcon(binding, icon.Id);
            grid.Children.Add(cell);
        }
        body.Children.Add(grid);
        var card = new Border
        {
            Background = Theme.CardBgBrush, BorderBrush = Theme.StrokeBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8),
            Child = body,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black },
        };
        var popup = new Popup
        {
            Child = card, PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false,
            AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
        };
        popup.Closed += (_, _) => { if (ReferenceEquals(_trayIconPopup, popup)) _trayIconPopup = null; };
        _trayIconPopup = popup;
        popup.IsOpen = true;
    }

    /// <summary>选中一枚图标：只改这一行的字形，整页不重建，列表因此不会闪。</summary>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    /// <param name="id">图标标识；空串选「跟随行为」。</param>
    private void PickTrayIcon(GestureBindingDto binding, string id)
    {
        CloseTrayIconPopup();
        binding.MenuIcon = id;
        CommitHotkeys();
        var row = _trayMenuList?.Children.OfType<Border>()
            .FirstOrDefault(item => (item.Tag as string) == TrayRowTag + binding.Id);
        if (row?.Child is Grid grid && grid.Children.OfType<Border>().FirstOrDefault() is { Child: TextBlock glyph })
            glyph.Text = TrayMenuIcons.Glyph(_settings.Current.Gestures, binding);
    }

    private void CloseTrayIconPopup()
    {
        var popup = _trayIconPopup;
        _trayIconPopup = null;
        if (popup != null) popup.IsOpen = false;
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

    /// <summary>新增一枚菜单项：重建列表后让新行自己弹一下，不重播整列的入场。</summary>
    private void AddTrayMenuItem()
    {
        _settings.Mutate(settings => settings.Gestures.AddTrayMenu(Loc.T(K.Triggers.NewMenuItem)));
        AppHost.Current.InstallHotkeys(); RenderTrayMenu();
        if (_trayMenuList?.Children.Count > 0 && _trayMenuList.Children[^1] is FrameworkElement last)
            PopIn(last, TimeSpan.Zero);
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
