using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>界面键位与选区微调共用平面键位行和录键组件。</summary>
public sealed partial class SettingsWindow : Window
{
    private FrameworkElement KeyRow(string label, string tip, Func<string> get, Action<string> set, bool reinstall = false)
    {
        var text = new TextBlock
        {
            Text = label, FontSize = 13, Foreground = Theme.TextBrush, TextWrapping = TextWrapping.Wrap,
        };
        var box = Fluent.UiKeyBinding(get());
        box.EmptyHint = Loc.T(K.Hotkeys.ClickToSet);
        bool wasPaused = false;
        box.CaptureBegan += () => { wasPaused = HotkeyManager.Paused; HotkeyManager.Paused = true; };
        box.CaptureEnded += () => HotkeyManager.Paused = wasPaused;
        box.Picked += chord =>
        {
            set(chord);
            if (reinstall) AppHost.Current.InstallHotkeys();
        };
        var row = Fluent.UiKeyRow(text, box);
        row.ToolTip = tip;
        return row;
    }

    // 确认选区：可调整的框出现后，按这枚键才算数
    FrameworkElement ConfirmKeyRow() =>
        KeyRow(Loc.T(K.Capture.ConfirmSelection), Loc.T(K.Settings.ConfirmCancelKeysTip),
            () => _settings.Current.PickerConfirmChord,
            c => _settings.Mutate(s => s.PickerConfirmChord = c));

    // 取消选区：可调整的框出现后，按这枚键把这一框作废
    FrameworkElement CancelKeyRow() =>
        KeyRow(Loc.T(K.Common.CancelSelection), Loc.T(K.Settings.ConfirmCancelKeysTip),
            () => _settings.Current.PickerCancelChord,
            c => _settings.Mutate(s => s.PickerCancelChord = c));

    // 截图浮窗上「复制」的键位：浮窗活着的那几秒由全局钩子代收，改完当场生效
    FrameworkElement ToastCopyRow() =>
        KeyRow(Loc.T(K.Settings.CopyKey), Loc.T(K.Settings.TheToastAlsoAccepts),
            () => _settings.Current.ToastCopyChord,
            c => _settings.Mutate(s => s.ToastCopyChord = c), reinstall: true);

    private void BuildPickerNudgeKeys(StackPanel panel)
    {
        var keys = new StackPanel();
        keys.Children.Add(SectionHeader(Loc.T(K.Hotkeys.PickerNudge)));
        string tip = Loc.T(K.Hotkeys.PickerNudgeTip);
        keys.Children.Add(KeyRow(Loc.T(K.Hotkeys.NudgeLeft), tip, () => _settings.Current.PickerNudgeLeftChord,
            chord => _settings.Mutate(s => s.PickerNudgeLeftChord = chord)));
        keys.Children.Add(KeyRow(Loc.T(K.Hotkeys.NudgeRight), tip, () => _settings.Current.PickerNudgeRightChord,
            chord => _settings.Mutate(s => s.PickerNudgeRightChord = chord)));
        keys.Children.Add(KeyRow(Loc.T(K.Hotkeys.NudgeUp), tip, () => _settings.Current.PickerNudgeUpChord,
            chord => _settings.Mutate(s => s.PickerNudgeUpChord = chord)));
        keys.Children.Add(KeyRow(Loc.T(K.Hotkeys.NudgeDown), tip, () => _settings.Current.PickerNudgeDownChord,
            chord => _settings.Mutate(s => s.PickerNudgeDownChord = chord)));
        Cardify(keys); panel.Children.Add(keys);
    }
}
