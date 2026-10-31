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
        BuildPickerNudgeKeys(panel);
        BuildEditorKeys(panel);
    }

    private FrameworkElement HotkeyOverviewRow(GestureBindingDto binding) => BuildHotkeyOverviewEntry(binding);

    private void OpenSettingsBinding(GestureBindingDto binding)
    {
        string id = "binding:" + binding.Id;
        _settingsPages[id] = new(id, "hotkeys", "hotkeys", K.Hotkeys.Shortcut, Str.Empty, "\uE765", 0,
            panel => BuildSettingsBindingDetail(panel, binding), () => HotkeyDisplayName(binding));
        NavigateSettings(id, true, canonical: true);
    }

    private void BuildSettingsBindingDetail(StackPanel panel, GestureBindingDto binding)
    {
        panel.Children.Add(BuildBindingInputCard(binding));
        panel.Children.Add(BuildBindingBehaviorCard(binding));
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
