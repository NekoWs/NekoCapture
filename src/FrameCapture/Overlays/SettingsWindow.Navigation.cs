using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>页内路径、滚动位置与共享行为入口。</summary>
public sealed partial class SettingsWindow
{
    private StackPanel GetSettingsBody(string id)
    {
        if (_settingsBodies.TryGetValue(id, out var existing)) return existing;
        var body = new StackPanel { Margin = new Thickness(20, 10, 20, 16), Visibility = Visibility.Collapsed, Tag = id };
        _settingsPages[id].Build(body);
        _settingsBodies[id] = body; _settingsHost.Children.Add(body);
        return body;
    }

    private List<string> CanonicalSettingsPath(string id)
    {
        var path = new List<string>();
        for (string? next = id; next != null && _settingsPages.TryGetValue(next, out var page); next = page.Parent)
        { if (path.Contains(next)) break; path.Insert(0, next); }
        return path;
    }

    private void NavigateSettings(string id, bool animate, bool canonical = false)
    {
        id = ResolveSettingsPage(id);
        if (id == _currentSettingsPage && !SettingsSearchActive) return;
        if (_currentSettingsPage.Length > 0) _settingsScroll[_currentSettingsPage] = _settingsScroller.VerticalOffset;
        ClearSettingsSearchText();
        var page = _settingsPages[id];
        int at = _settingsTrail.IndexOf(id);
        if (canonical || page.Parent == null || _settingsTrail.Count == 0)
        { _settingsTrail.Clear(); _settingsTrail.AddRange(CanonicalSettingsPath(id)); }
        else if (at >= 0) _settingsTrail.RemoveRange(at + 1, _settingsTrail.Count - at - 1);
        else _settingsTrail.Add(id);
        _currentSettingsPage = id; _cat = page.Category;
        bool cached = _settingsBodies.ContainsKey(id);
        var incoming = GetSettingsBody(id);
        if (id == "hotkeys") RebuildHotkeys();
        else if (cached && id.StartsWith("binding:", StringComparison.Ordinal)) RefreshSettingsBindingDetail();
        if (id == "behaviors") { incoming.Children.Clear(); BuildSharedBehaviorList(incoming); }
        PaintSettingsNavigation();
        var bodies = _settingsBodies.Values.ToArray();
        SwitchSettingsPage(bodies, Array.IndexOf(bodies, incoming), animate, _settingsScroller, _settingsHeader, page.Label,
            _settingsScroll.GetValueOrDefault(id), () => RenderSettingsBreadcrumbs(animate));
    }

    private void RenderSettingsBreadcrumbs(bool animate = false)
    {
        _settingsAddBehavior.Visibility = _currentSettingsPage == "behaviors" && !SettingsSearchActive ? Visibility.Visible : Visibility.Collapsed;
        double fromHeight = _settingsCrumbHost.Visibility == Visibility.Visible ? _settingsCrumbHost.ActualHeight : 0;
        _settingsCrumbs.Children.Clear();
        bool visible = _settingsTrail.Count > 1 && !SettingsSearchActive;
        if (!visible) { AnimateSettingsBreadcrumbs(false, fromHeight, animate); return; }
        void Link(string title, string id, bool back = false)
        {
            var button = Fluent.RoundButton(title, Colors.Transparent, Theme.TextDim, 5, false);
            button.Background = Brushes.Transparent; button.Foreground = Theme.TextDimBrush;
            if (back)
            {
                button.Content = Fluent.Glyph("\uE72B", 14, Theme.TextDimBrush);
                button.ToolTip = title; button.Padding = new Thickness(6);
                System.Windows.Automation.AutomationProperties.SetName(button, title);
            }
            button.Click += (_, _) => NavigateSettings(id, true);
            _settingsCrumbs.Children.Add(button);
        }
        Link(Loc.T(K.SettingsNavigation.Back), _settingsTrail[^2], back: true);
        for (int i = 0; i < _settingsTrail.Count; i++)
        {
            if (i > 0)
            {
                var arrow = Fluent.Glyph("\uE76C", 10, Theme.TextDimBrush);
                arrow.Margin = new Thickness(5, 0, 5, 0); arrow.VerticalAlignment = VerticalAlignment.Center;
                _settingsCrumbs.Children.Add(arrow);
            }
            string id = _settingsTrail[i];
            if (i < _settingsTrail.Count - 1) Link(_settingsPages[id].Label, id);
            else _settingsCrumbs.Children.Add(new TextBlock { Text = _settingsPages[id].Label, Foreground = Theme.TextBrush,
                FontSize = 12, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        AnimateSettingsBreadcrumbs(true, fromHeight, animate);
    }

    private void OpenSettingsFlow(string flowId)
    {
        if (_settings.Current.Gestures.Graph.Flow(flowId) == null) return;
        _behaviorFlow = flowId; _behaviorPath.Clear();
        RenderBehaviors();
        NavigateSettings("flow", true);
    }

    private void BuildSharedBehaviorList(StackPanel panel)
    {
        foreach (var flow in _settings.Current.Gestures.Graph.Flows)
            panel.Children.Add(BuildBehaviorOverviewEntry(flow));
    }

    private void AddSettingsBehavior()
    {
        var flow = new BehaviorFlow(); _settings.Current.Gestures.Graph.Flows.Add(flow);
        CommitBehaviorGraph(); OpenSettingsFlow(flow.Id);
    }
}
