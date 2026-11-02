using System.Windows;
using System.Windows.Controls;
using NekoCapture.Core;
using NekoCapture.Services;

namespace NekoCapture.Overlays;

/// <summary>触发列表的新增入口，菜单项各自拥有可编辑的行为。</summary>
public sealed partial class SettingsWindow
{
    private static Button TriggerAddButton(Str label, string tag, Action add)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Fluent.Glyph("\uE710", 12, Theme.AccentBrush));
        content.Children.Add(new TextBlock { Text = Loc.T(label), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0) });
        var button = Fluent.Ui(new Button { Content = content, Tag = tag, MinHeight = 38,
            Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 10, 0),
            BorderThickness = new Thickness(1), FontSize = 12.5 });
        System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(label));
        button.Click += (_, _) => add(); return button;
    }
}
