using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>本地双语设置搜索、近义词匹配与定位反馈。</summary>
public sealed partial class SettingsWindow
{
    private sealed record SettingsSearchItem(string Page, Str Title, Str[] Aliases);
    private readonly List<SettingsSearchItem> _settingsSearchIndex = new();
    private bool _settingsSearchSync;
    private bool? _settingsSearchPaused;
    private string? _settingsSearchReturn;
    private bool SettingsSearchActive => !string.IsNullOrWhiteSpace(_settingsSearchBox?.Text);

    private FrameworkElement BuildSettingsSearchBox()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Fluent.Glyph("\uE721", 14, Theme.TextDimBrush); icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(icon);
        var field = new Grid(); Grid.SetColumn(field, 1); grid.Children.Add(field);
        _settingsSearchBox = new TextBox { Background = Brushes.Transparent, Foreground = Theme.TextBrush,
            BorderThickness = new Thickness(0), FontSize = 12, Padding = new Thickness(0, 3, 0, 3), VerticalContentAlignment = VerticalAlignment.Center };
        InputMethod.SetIsInputMethodEnabled(_settingsSearchBox, true);
        System.Windows.Automation.AutomationProperties.SetName(_settingsSearchBox, Loc.T(K.SettingsNavigation.Search));
        var placeholder = new TextBlock { Text = Loc.T(K.SettingsNavigation.Search), Foreground = Theme.TextDimBrush,
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        field.Children.Add(_settingsSearchBox); field.Children.Add(placeholder);
        var clear = AboutButton(K.SettingsNavigation.ClearSearch); clear.Content = Fluent.Glyph("\uE711", 11, Theme.TextDimBrush);
        clear.Padding = new Thickness(4); clear.Visibility = Visibility.Collapsed; clear.ToolTip = Loc.T(K.SettingsNavigation.ClearSearch);
        Grid.SetColumn(clear, 2); grid.Children.Add(clear);
        clear.Click += (_, _) => { _settingsSearchBox.Clear(); _settingsSearchBox.Focus(); };
        _settingsSearchBox.GotKeyboardFocus += (_, _) =>
        { _settingsSearchPaused ??= HotkeyManager.Paused; HotkeyManager.Paused = true; };
        void ReleaseSearchKeys()
        { if (_settingsSearchPaused is bool paused) { _settingsSearchPaused = null; HotkeyManager.Paused = paused; } }
        _settingsSearchBox.LostKeyboardFocus += (_, _) => ReleaseSearchKeys();
        Closed += (_, _) => ReleaseSearchKeys();
        _settingsSearchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _settingsSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = _settingsSearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (!_settingsSearchSync) RenderSettingsSearch();
        };
        _settingsSearchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { _settingsSearchBox.Clear(); e.Handled = true; }
            else if (e.Key == Key.Enter && SettingsSearchActive)
            {
                var item = FindSettingsMatches().FirstOrDefault();
                if (item != null) OpenSettingsSearchResult(item);
                e.Handled = true;
            }
        };
        return new Border { Child = grid, Background = Theme.CardBgBrush, BorderBrush = Theme.StrokeFieldBrush,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 5, 6, 5), Margin = new Thickness(0, 20, 0, 0) };
    }

    private void ClearSettingsSearchText()
    {
        _settingsSearchSync = true;
        try { _settingsSearchBox?.Clear(); }
        finally { _settingsSearchSync = false; }
    }

    private static string NormalizeSettingsQuery(string text)
        => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private IEnumerable<SettingsSearchItem> FindSettingsMatches()
    {
        string Both(Str value) => value.Zh + " " + value.En;
        string raw = _settingsSearchBox.Text.Trim();
        foreach (string verb in Both(K.SettingsNavigation.SearchVerbs).Split(' '))
            if (raw.StartsWith(verb, StringComparison.OrdinalIgnoreCase) && raw.Length > verb.Length)
            { raw = raw[verb.Length..].TrimStart(); break; }
        string[] queries = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(NormalizeSettingsQuery).Where(q => q.Length > 0).ToArray();
        if (queries.Length == 0) return Array.Empty<SettingsSearchItem>();
        int Score(SettingsSearchItem item)
        {
            var page = _settingsPages[item.Page];
            string title = NormalizeSettingsQuery(Both(item.Title));
            string location = NormalizeSettingsQuery(string.Join(" ", CanonicalSettingsPath(item.Page).Select(id => Both(_settingsPages[id].Title))) + Both(page.Summary));
            int total = 0;
            foreach (string query in queries)
            {
                if (title.Contains(query)) total += 100;
                else if (item.Aliases.Any(alias => Both(alias).Split(' ').Any(word => NormalizeSettingsQuery(word).Contains(query)))) total += 70;
                else if (location.Contains(query)) total += 25;
                else return 0;
            }
            return total;
        }
        return _settingsSearchIndex.Select(item => (Item: item, Score: Score(item))).Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score).Select(item => item.Item).DistinctBy(item => (item.Page, item.Title.Id));
    }

    private void RenderSettingsSearch()
    {
        if (!SettingsSearchActive)
        {
            if (_currentSettingsPage == "search") NavigateSettings(_settingsSearchReturn ?? "hotkeys", true, canonical: true);
            return;
        }
        if (_currentSettingsPage != "search")
        {
            _settingsSearchReturn = _currentSettingsPage;
            _settingsScroll[_currentSettingsPage] = _settingsScroller.VerticalOffset;
        }
        _settingsPages["search"] = new("search", null, "search", K.SettingsNavigation.Search, Str.Empty, "\uE721", _cat, _ => { });
        var body = GetSettingsBody("search"); body.Children.Clear();
        var matches = FindSettingsMatches().ToArray();
        foreach (var item in matches)
        {
            var page = _settingsPages[item.Page];
            string path = string.Join(" › ", CanonicalSettingsPath(item.Page).Select(id => _settingsPages[id].Label));
            body.Children.Add(SettingsEntry(Loc.T(item.Title), path, page.Glyph, () => OpenSettingsSearchResult(item)));
        }
        if (matches.Length == 0)
        {
            body.Children.Add(SectionHeader(Loc.T(K.SettingsNavigation.NoResults)));
            body.Children.Add(UiNote(Loc.T(K.SettingsNavigation.TryAnother)));
        }
        bool entering = _currentSettingsPage != "search";
        _currentSettingsPage = "search"; _settingsCrumbs.Visibility = Visibility.Collapsed; PaintSettingsNavigation();
        if (!entering) { _settingsScroller.ScrollToTop(); return; }
        var bodies = _settingsBodies.Values.ToArray();
        SwitchSettingsPage(bodies, Array.IndexOf(bodies, body), false, _settingsScroller, _settingsHeader, Loc.T(K.SettingsNavigation.Search));
    }

    private void OpenSettingsSearchResult(SettingsSearchItem item)
    {
        NavigateSettings(item.Page, true, canonical: true);
        Keyboard.ClearFocus();
        int generation = _pageMotionGeneration;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs + Fluent.Motion.EnterMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (generation != _pageMotionGeneration || !IsLoaded) return;
            string title = Loc.T(item.Title);
            TextBlock? Find(DependencyObject node)
            {
                if (node is TextBlock text && text.Text == title) return text;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    if (Find(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
                return null;
            }
            if (!_settingsBodies.TryGetValue(item.Page, out var body) || Find(body) is not { } label) return;
            label.BringIntoView();
            var brush = new SolidColorBrush(Theme.Accent); label.Foreground = brush;
            var fade = new ColorAnimation(Theme.Accent, Theme.Text, TimeSpan.FromMilliseconds(Fluent.Motion.StateMs * 5));
            fade.Completed += (_, _) => label.Foreground = Theme.TextBrush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, fade);
        };
        timer.Start();
    }
}
