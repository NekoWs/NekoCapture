using System.Windows;
using System.Windows.Controls;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>快捷键概要、独立详情和共享行为跳转。</summary>
public sealed partial class SettingsWindow
{
    private bool _rebuildingSettingsBinding;

    private void BuildInterfaceKeys(StackPanel panel)
    {
        var ui = new StackPanel();
        ui.Children.Add(UiKeyPill(ConfirmKeyRow())); ui.Children.Add(UiKeyPill(CancelKeyRow()));
        ui.Children.Add(UiKeyPill(ToastCopyRow())); Cardify(ui); panel.Children.Add(ui);
        BuildEditorKeys(panel);
    }

    private FrameworkElement HotkeyOverviewRow(GestureBindingDto binding)
    {
        var cfg = _settings.Current.Gestures;
        var flow = cfg.Graph.Flow(binding.EntryId);
        string summary = flow == null ? Loc.T(K.SettingsNavigation.ExecuteHint)
            : string.Join(" → ", flow.Steps.Select(id => cfg.Graph.Node(id)).Where(node => node != null).Select(node => GestureActions.LabelOf(node!.Action)));
        var entry = SettingsEntry(HotkeyDisplayName(binding), summary, "\uE765", () => OpenSettingsBinding(binding));
        if (entry.Content is Grid grid)
        {
            grid.ColumnDefinitions.Insert(2, new ColumnDefinition { Width = GridLength.Auto });
            var arrow = grid.Children[^1]; Grid.SetColumn(arrow, 3);
            var caps = Keycaps.Row(binding.Chord, new KeycapTheme { Face = Theme.RowBg, Text = Theme.Text,
                Stroke = Theme.StrokeSoft, Dim = Theme.TextDim, Size = 10 });
            caps.Margin = new Thickness(10, 0, 0, 0); caps.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(caps, 2); grid.Children.Add(caps);
            void RepaintCaps()
            {
                grid.Children.Remove(caps);
                caps = Keycaps.Row(binding.Chord, new KeycapTheme { Face = Theme.RowBg, Text = Theme.Text,
                    Stroke = Theme.StrokeSoft, Dim = Theme.TextDim, Size = 10 });
                caps.Margin = new Thickness(10, 0, 0, 0); caps.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(caps, 2); grid.Children.Add(caps);
            }
            entry.Loaded += (_, _) => { Theme.Changed -= RepaintCaps; Theme.Changed += RepaintCaps; RepaintCaps(); };
            entry.Unloaded += (_, _) => Theme.Changed -= RepaintCaps;
        }
        entry.Opacity = binding.Enabled ? 1 : 0.6; _hotRows[binding.Id] = entry;
        return entry;
    }

    private void OpenSettingsBinding(GestureBindingDto binding)
    {
        string id = "binding:" + binding.Id;
        _settingsPages[id] = new(id, "hotkeys", "hotkeys", K.Hotkeys.Shortcut, Str.Empty, "\uE765", 0,
            panel => BuildSettingsBindingDetail(panel, binding), () => HotkeyDisplayName(binding));
        NavigateSettings(id, true);
    }

    private void BuildSettingsBindingDetail(StackPanel panel, GestureBindingDto binding)
    {
        panel.Children.Add(HotkeyRow(binding, detail: true));
        panel.Children.Add(SettingsEntry(Loc.T(K.SettingsNavigation.Execute), Loc.T(K.SettingsNavigation.ExecuteHint), "\uE7A8",
            () =>
            {
                if (_settings.Current.Gestures.Graph.Flow(binding.EntryId) == null) NavigateSettings("behaviors", true);
                else OpenSettingsFlow(binding.EntryId);
            }));
    }

    private void RefreshSettingsBindingDetail()
    {
        if (_rebuildingSettingsBinding || !_currentSettingsPage.StartsWith("binding:", StringComparison.Ordinal)) return;
        var binding = _settings.Current.Gestures.Bindings.FirstOrDefault(b => "binding:" + b.Id == _currentSettingsPage);
        if (binding == null) return;
        if (!_settingsBodies.TryGetValue(_currentSettingsPage, out var panel)) return;
        _rebuildingSettingsBinding = true;
        try { _hotClashSync.Clear(); _hotBoxes.Clear(); panel.Children.Clear(); BuildSettingsBindingDetail(panel, binding); }
        finally { _rebuildingSettingsBinding = false; }
    }

    private void InvalidateSettingsBindings()
    {
        foreach (string id in _settingsPages.Keys.Where(id => id.StartsWith("binding:", StringComparison.Ordinal)).ToArray())
        {
            if (_settingsBodies.Remove(id, out var body)) _settingsHost.Children.Remove(body);
            _settingsPages.Remove(id); _settingsScroll.Remove(id);
        }
    }
}
