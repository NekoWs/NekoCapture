using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>六个功能入口、底部搜索与页内路径。</summary>
public sealed partial class SettingsWindow
{
    private readonly Dictionary<string, Border> _settingsNav = new();
    private readonly List<Border> _navRows = new();
    private Border? _selBar;
    private string? _settingsNavHover;
    private string? _settingsIndicatorRoot;
    private Grid _settingsHost = null!;
    private ScrollViewer _settingsScroller = null!;
    private TextBlock _settingsHeader = null!;
    private WrapPanel _settingsCrumbs = null!;
    private Border _settingsCrumbHost = null!;
    private TextBox _settingsSearchBox = null!;

    private FrameworkElement BuildSettingsShell()
    {
        var dock = new DockPanel();
        var (bar, close) = Fluent.TitleBar("\uE7A8", Loc.T(K.Common.Settings), Loc.T(K.Common.Close));
        close.Click += (_, _) => Fluent.CloseWithFade(this);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        var side = new DockPanel { Margin = new Thickness(12, 10, 8, 12) };
        var search = BuildSettingsSearchBox(); DockPanel.SetDock(search, Dock.Bottom); side.Children.Add(search);
        var nav = new StackPanel();
        foreach (string id in new[] { "hotkeys", "capture", "record", "appearance", "system", "about" })
        {
            var page = _settingsPages[id];
            var button = NavigationButton(page);
            button.MouseLeftButtonDown += (_, e) => { NavigateSettings(id, true); e.Handled = true; };
            button.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { NavigateSettings(id, true); e.Handled = true; } };
            button.MouseEnter += (_, _) => { _settingsNavHover = id; PaintSettingsNavigation(); };
            button.MouseLeave += (_, _) => { _settingsNavHover = null; PaintSettingsNavigation(); };
            _navRows.Add(button);
            _settingsNav[id] = button; nav.Children.Add(button);
        }
        var navLayers = new Grid(); navLayers.Children.Add(nav);
        var indicatorLayer = new Canvas { IsHitTestVisible = false };
        _selBar = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(2), Background = Theme.AccentBrush };
        Canvas.SetLeft(_selBar, 5); indicatorLayer.Children.Add(_selBar); navLayers.Children.Add(indicatorLayer);
        var navFrame = new Border { Child = navLayers, Padding = new Thickness(6), CornerRadius = new CornerRadius(8),
            Background = Theme.NavBgBrush, BorderBrush = Theme.StrokeSoftBrush, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top };
        side.Children.Add(navFrame); layout.Children.Add(side);
        var divider = new Border { Background = Theme.StrokeBrush }; Grid.SetColumn(divider, 1); layout.Children.Add(divider);
        var right = new DockPanel();
        _settingsCrumbs = new WrapPanel { Margin = new Thickness(0, 11, 0, 0) };
        _settingsCrumbHost = new Border { Child = _settingsCrumbs, Margin = new Thickness(20, 0, 20, 0),
            Visibility = Visibility.Collapsed, ClipToBounds = true };
        DockPanel.SetDock(_settingsCrumbHost, Dock.Top); right.Children.Add(_settingsCrumbHost);
        _settingsHeader = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush,
            Margin = new Thickness(22, 16, 22, 4), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(_settingsHeader, Dock.Top); right.Children.Add(_settingsHeader);
        _settingsHost = new Grid();
        _settingsScroller = new ScrollViewer { Content = _settingsHost, Focusable = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 8, 0) };
        Fluent.OverlayScroll(_settingsScroller); right.Children.Add(_settingsScroller);
        Grid.SetColumn(right, 2); layout.Children.Add(right); dock.Children.Add(layout);
        _repaint.Add(() => PaintSettingsNavigation(false));
        return new Border { Background = Theme.WindowBgSolidBrush, BorderBrush = Theme.StrokeBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Child = dock };
    }

    private Border NavigationButton(SettingsPage page)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(15, 0, 10, 0) };
        var icon = Fluent.Glyph(page.Glyph, 15, Theme.TextDimBrush); icon.Width = 22;
        icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon);
        var label = new TextBlock { Text = page.Label, Tag = "settings-page", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0), FontSize = 13 };
        row.Children.Add(label);
        var button = new Border { Child = row, CornerRadius = new CornerRadius(6), Height = 38,
            Margin = new Thickness(0, 1, 0, 1), Tag = page.Id, Focusable = true, Cursor = Cursors.Hand };
        button.GotKeyboardFocus += (_, _) => { button.BorderThickness = new Thickness(1); button.BorderBrush = Theme.AccentBrush; };
        button.LostKeyboardFocus += (_, _) => button.BorderThickness = new Thickness(0);
        return button;
    }

    private static Button SettingsSurfaceButton(object content, double radius = 10)
    {
        var template = Fluent.RoundedButtonTemplate(false);
        template.VisualTree.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        template.VisualTree.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        template.VisualTree.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        template.VisualTree.FirstChild.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        var button = new Button { Content = content, Background = Theme.CardBgBrush, Foreground = Theme.TextBrush,
            BorderBrush = Theme.StrokeSoftBrush, BorderThickness = new Thickness(1), Padding = new Thickness(15),
            Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch, Template = template };
        Fluent.ChipHover(button);
        return button;
    }

    private Button SettingsEntry(string title, string summary, string glyph, Action open)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Fluent.Glyph(glyph, 16, Theme.TextDimBrush); icon.Margin = new Thickness(0, 0, 14, 0);
        icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon);
        var label = new StackPanel();
        label.Children.Add(new TextBlock { Text = title, FontSize = 13, Foreground = Theme.TextBrush, TextWrapping = TextWrapping.Wrap });
        label.Children.Add(new TextBlock { Text = summary, FontSize = 12, Foreground = Theme.TextDimBrush,
            Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(label, 1); row.Children.Add(label);
        var arrow = Fluent.Glyph("\uE76C", 11, Theme.TextDimBrush); arrow.Margin = new Thickness(14, 0, 0, 0);
        arrow.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(arrow, 2); row.Children.Add(arrow);
        var button = SettingsSurfaceButton(row); StyleSettingsCard(button);
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        button.Click += (_, _) => open();
        return button;
    }

    private Button SettingsPageEntry(string id)
    {
        var page = _settingsPages[id];
        return SettingsEntry(page.Label, Loc.T(page.Summary), page.Glyph, () => NavigateSettings(id, true));
    }

    private void BuildSettingsGroup(StackPanel panel, string parent)
    {
        foreach (var page in _settingsPages.Values.Where(page => page.Parent == parent))
            panel.Children.Add(SettingsPageEntry(page.Id));
    }

    private void PaintSettingsNavigation(bool animate = true)
    {
        void Paint(DependencyObject owner, DependencyProperty property, Color target)
        {
            var old = owner.GetValue(property) as SolidColorBrush;
            var brush = new SolidColorBrush(animate && IsLoaded ? old?.Color ?? target : target);
            owner.SetValue(property, brush);
            if (animate && IsLoaded) brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new System.Windows.Media.Animation.ColorAnimation(target, TimeSpan.FromMilliseconds(Fluent.Motion.HoverMs)));
        }
        string root = _settingsTrail.FirstOrDefault() ?? "hotkeys";
        foreach (var (id, button) in _settingsNav)
        {
            bool selected = id == root && string.IsNullOrWhiteSpace(_settingsSearchBox.Text);
            Color backing = Fluent.NavBacking(Theme.NavBg, selected, id == _settingsNavHover);
            Paint(button, Border.BackgroundProperty, backing);
            if (button.Child is StackPanel row)
            {
                Paint(row.Children[0], TextBlock.ForegroundProperty, Fluent.NavInk(backing, selected, true));
                Paint(row.Children[1], TextBlock.ForegroundProperty, Fluent.NavInk(backing, selected, false));
            }
        }
        if (_selBar == null) return;
        int index = _settingsNav.Keys.ToList().IndexOf(root);
        _selBar.Visibility = SettingsSearchActive || index < 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_settingsIndicatorRoot == root) return;
        _settingsIndicatorRoot = root;
        double top = Math.Max(0, index) * 40 + 11;
        if (_selBar.IsLoaded) _selBar.BeginAnimation(Canvas.TopProperty,
            new System.Windows.Media.Animation.DoubleAnimation(top, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
        else Canvas.SetTop(_selBar, top);
    }
}
