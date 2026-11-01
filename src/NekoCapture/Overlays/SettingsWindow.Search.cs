using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NekoCapture.Core;
using NekoCapture.Services;

namespace NekoCapture.Overlays;

/// <summary>本地双语设置搜索、近义词匹配与定位反馈。</summary>
public sealed partial class SettingsWindow
{
    private sealed record SettingsSearchItem(string Page, Str Title, Str[] Aliases);
    private readonly List<SettingsSearchItem> _settingsSearchIndex = new();
    private bool _settingsSearchSync;
    private bool? _settingsSearchPaused;
    private string? _settingsSearchReturn;
    private string _settingsSearchResultSignature = "";
    private bool SettingsSearchActive => !string.IsNullOrWhiteSpace(_settingsSearchBox?.Text);

    private FrameworkElement BuildSettingsSearchBox()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Fluent.Glyph("\uE721", 14, Theme.TextDimBrush); icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(icon);
        var field = new Grid(); Grid.SetColumn(field, 1); grid.Children.Add(field);
        _settingsSearchBox = Fluent.Ui(new TextBox { Background = Brushes.Transparent, Foreground = Theme.TextBrush,
            CaretBrush = Theme.TextBrush, SelectionBrush = Theme.AccentBrush, SelectionOpacity = 0.3, SelectionTextBrush = Theme.TextBrush,
            BorderThickness = new Thickness(0), FontSize = 12, Padding = new Thickness(0, 3, 0, 3), VerticalContentAlignment = VerticalAlignment.Center });
        InputMethod.SetIsInputMethodEnabled(_settingsSearchBox, true);
        System.Windows.Automation.AutomationProperties.SetName(_settingsSearchBox, Loc.T(K.SettingsNavigation.Search));
        var placeholder = new TextBlock { Text = Loc.T(K.SettingsNavigation.Search), Foreground = Theme.TextDimBrush,
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        field.Children.Add(_settingsSearchBox); field.Children.Add(placeholder);
        var clear = AboutButton(K.SettingsNavigation.ClearSearch); clear.Content = Fluent.Glyph("\uE711", 11, Theme.TextDimBrush);
        clear.Padding = new Thickness(4); clear.ToolTip = Loc.T(K.SettingsNavigation.ClearSearch);
        var clearHost = new Border { Child = clear, ClipToBounds = true, Width = 0, Opacity = 0, Visibility = Visibility.Collapsed };
        Grid.SetColumn(clearHost, 2); grid.Children.Add(clearHost);
        var frame = new Border { Child = grid, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 5, 6, 5), Margin = new Thickness(0, 20, 0, 0), Tag = "settings-search-field" };
        var showTextActions = WireSettingsSearchMotion(frame, icon, placeholder, clearHost);
        frame.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!SettingsInputContains(clear, e.OriginalSource as DependencyObject)) _settingsSearchBox.Focus();
        };
        clear.Click += (_, _) => { _settingsSearchBox.Clear(); _settingsSearchBox.Focus(); };
        void ReleaseSearchKeys()
        { if (_settingsSearchPaused is bool paused) { _settingsSearchPaused = null; HotkeyManager.Paused = paused; } }
        frame.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (frame.IsKeyboardFocusWithin) { _settingsSearchPaused ??= HotkeyManager.Paused; HotkeyManager.Paused = true; }
            else ReleaseSearchKeys();
        };
        Closed += (_, _) => ReleaseSearchKeys();
        _settingsSearchBox.TextChanged += (_, _) =>
        {
            showTextActions(_settingsSearchBox.Text.Length > 0);
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
        return frame;
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
        bool entering = _currentSettingsPage != "search";
        var body = GetSettingsBody("search");
        var matches = FindSettingsMatches().ToArray();
        string signature = string.Join("|", matches.Select(item => item.Page + ":" + item.Title.Id));
        if (!entering && signature == _settingsSearchResultSignature) return;
        _settingsSearchResultSignature = signature;
        void RenderResults()
        {
            body.Children.Clear();
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
        }
        if (entering) RenderResults(); else TransitionContent(body, RenderResults, direction: 0);
        _currentSettingsPage = "search"; PaintSettingsNavigation();
        if (!entering) { _settingsScroller.ScrollToTop(); return; }
        var bodies = _settingsBodies.Values.ToArray();
        SwitchSettingsPage(bodies, Array.IndexOf(bodies, body), true, _settingsScroller, _settingsHeader,
            Loc.T(K.SettingsNavigation.Search), prepareChrome: () => RenderSettingsBreadcrumbs(true));
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
