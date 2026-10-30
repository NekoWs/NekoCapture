using System.Windows;
using System.Windows.Controls;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>自动更新开关与手动检查入口。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement UpdateOptions()
    {
        var panel = new StackPanel();
        panel.Children.Add(ToggleRow("\uE946", Loc.T(K.Updates.AutoCheck), Loc.T(K.Updates.AutoCheckHint),
            _settings.Current.AutoCheckUpdates, async enabled => await _settings.MutateAsync(s => s.AutoCheckUpdates = enabled)));
        var row = ChipRow("\uE946", Loc.T(K.Updates.Check), Loc.T(K.Updates.Software));
        var button = Fluent.RoundButton(Loc.T(K.Updates.Check), Theme.RowBg, Theme.Text, 12, true);
        Grid.SetColumn(button, 2);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { if (AppHost.Current.Updates != null) await AppHost.Current.Updates.CheckAsync(); }
            finally { button.IsEnabled = true; }
        };
        row.Children.Add(button); panel.Children.Add(row);
        return panel;
    }
}
