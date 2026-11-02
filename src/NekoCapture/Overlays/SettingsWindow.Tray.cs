using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NekoCapture.Core;
using NekoCapture.Services;

namespace NekoCapture.Overlays;

/// <summary>托盘页：图标点击与右键菜单项在同一页内编辑，按住行体拖动即可改菜单顺序。</summary>
public sealed partial class SettingsWindow
{
    private StackPanel? _trayMenuList;
    private Popup? _trayIconPopup;
    private Border? _trayDragRow;
    private string _trayDragId = "";
    private Point _trayDragFrom;
    private int _trayDragTarget = -1;
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
        CloseTrayIconPopup();
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
            Tag = "settings-tray-item:" + binding.Id,
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
        card.MouseLeftButtonDown += (_, e) => BeginTrayDrag(card, binding, e);
        card.MouseMove += (_, e) => MoveTrayDrag(e);
        card.MouseLeftButtonUp += (_, e) => EndTrayDrag(e);
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

    private void PickTrayIcon(GestureBindingDto binding, string id)
    {
        CloseTrayIconPopup();
        binding.MenuIcon = id; CommitHotkeys(); RenderTrayMenu();
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

    /// <summary>按住一行：只记起点，不抢鼠标——图标、下拉与删除按钮都还要能点。</summary>
    /// <param name="row">按下的那一行。</param>
    /// <param name="binding">这一条托盘菜单绑定。</param>
    /// <param name="e">鼠标参数。</param>
    private void BeginTrayDrag(Border row, GestureBindingDto binding, MouseButtonEventArgs e)
    {
        if (_trayMenuList == null || IsInteractiveSource(e.OriginalSource as DependencyObject, row)) return;
        _trayDragRow = row; _trayDragId = binding.Id; _trayDragging = false; _trayDragTarget = -1;
        _trayDragFrom = e.GetPosition(_trayMenuList);
    }

    /// <summary>指针从按钮、下拉、输入框里出来的不算拖动：那些控件自己要吃这一下。</summary>
    /// <param name="node">命中的元素。</param>
    /// <param name="row">这一行的容器，走到它就停。</param>
    /// <returns>落在交互控件里返回 true。</returns>
    private static bool IsInteractiveSource(DependencyObject? node, DependencyObject row)
    {
        for (var at = node; at != null && !ReferenceEquals(at, row); at = VisualTreeHelper.GetParent(at))
            if (at is ButtonBase or ComboBox or TextBox or PasswordBox or ScrollBar) return true;
        return false;
    }

    /// <summary>拖动中：被拖的行跟着指针浮起来，其余行按目标槽位错开并滑过去。</summary>
    /// <param name="e">鼠标参数。</param>
    private void MoveTrayDrag(MouseEventArgs e)
    {
        if (_trayDragRow == null || _trayMenuList == null || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(_trayMenuList);
        if (!_trayDragging)
        {
            if (Math.Abs(at.Y - _trayDragFrom.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _trayDragging = true;
            _trayDragRow.CaptureMouse();
            _trayDragRow.Effect = new System.Windows.Media.Effects.DropShadowEffect
            { BlurRadius = 22, ShadowDepth = 5, Opacity = 0.45, Color = Colors.Black };
            _trayDragRow.Opacity = 0.96;
            Panel.SetZIndex(_trayDragRow, 20);
            _trayDragTarget = _trayMenuList.Children.IndexOf(_trayDragRow);
        }
        double offset = at.Y - _trayDragFrom.Y;
        _trayDragRow.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1.02, 1.02), new TranslateTransform(0, offset) },
        };
        int target = TrayDropIndex(at.Y);
        if (target != _trayDragTarget)
        {
            _trayDragTarget = target;
            ShiftTrayRows();
        }
    }

    /// <summary>目标槽位：按行高算，指针越过多少行就落到第几格。</summary>
    /// <param name="y">相对行容器的指针纵坐标。</param>
    /// <returns>目标下标。</returns>
    private int TrayDropIndex(double y)
    {
        if (_trayMenuList == null || _trayMenuList.Children.Count == 0) return 0;
        double step = Math.Max(1, _trayDragRow != null ? _trayDragRow.ActualHeight + 4 : 40);
        int index = (int)Math.Round(y / step);
        return Math.Clamp(index, 0, _trayMenuList.Children.Count - 1);
    }

    /// <summary>其余行按「被拖的那一行挪到目标位之后」的相对位移滑开，位移本身带动画。</summary>
    private void ShiftTrayRows()
    {
        if (_trayMenuList == null || _trayDragRow == null) return;
        int from = _trayMenuList.Children.IndexOf(_trayDragRow);
        double step = Math.Max(1, _trayDragRow.ActualHeight + 4);
        for (int i = 0; i < _trayMenuList.Children.Count; i++)
        {
            if (_trayMenuList.Children[i] is not FrameworkElement row || ReferenceEquals(row, _trayDragRow)) continue;
            double shift = 0;
            if (from < _trayDragTarget && i > from && i <= _trayDragTarget) shift = -step;
            else if (from > _trayDragTarget && i >= _trayDragTarget && i < from) shift = step;
            var transform = row.RenderTransform as TranslateTransform ?? new TranslateTransform();
            row.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(
                shift, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        }
    }

    /// <summary>松手：真拖过就按目标槽位落盘，其余情况原样收场。</summary>
    /// <param name="e">鼠标参数。</param>
    private void EndTrayDrag(MouseButtonEventArgs e)
    {
        var row = _trayDragRow;
        bool dragged = _trayDragging;
        int target = _trayDragTarget;
        _trayDragRow = null; _trayDragging = false; _trayDragTarget = -1;
        if (row == null) return;
        if (row.IsMouseCaptured) row.ReleaseMouseCapture();
        row.Opacity = 1; row.Effect = null; Panel.SetZIndex(row, 0);
        if (!dragged || _trayMenuList == null) { row.RenderTransform = null; return; }
        foreach (FrameworkElement other in _trayMenuList.Children)
        {
            if (ReferenceEquals(other, row)) continue;
            other.BeginAnimation(UIElement.RenderTransformProperty, null);
            other.RenderTransform = null;
        }
        row.RenderTransform = null;
        var tags = _trayMenuList.Children.OfType<FrameworkElement>().Select(item => item.Tag as string ?? "").ToList();
        string moving = row.Tag as string ?? "";
        if (target >= 0)
        {
            tags.Remove(moving);
            tags.Insert(Math.Clamp(target, 0, tags.Count), moving);
        }
        var ids = tags.Select(tag => tag.Replace("settings-tray-item:", "")).ToList();
        _settings.Mutate(settings =>
        {
            var all = settings.Gestures.Bindings;
            var ordered = ids.Select(id => all.FirstOrDefault(item => item.Id == id))
                .Where(item => item != null).Select(item => item!).ToList();
            var rest = all.Where(item => item.Source != TriggerSources.TrayMenu).ToList();
            all.Clear(); all.AddRange(ordered); all.AddRange(rest);
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
}