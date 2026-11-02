using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>托盘菜单图标的预览与可视化选择。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement BindingMenuIcon(GestureBindingDto binding)
    {
        var root = new StackPanel { Tag = "settings-menu-icon" };
        var palette = new StackPanel();
        var current = Fluent.Ui(new Button { Tag = "settings-menu-icon-open", Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right });
        bool expanded = false;
        var choices = new List<(string Id, Button Button)>();
        void Refresh()
        {
            var selected = TrayMenuIcons.All.FirstOrDefault(icon => icon.Id == binding.MenuIcon);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Fluent.Glyph(TrayMenuIcons.Glyph(_settings.Current.Gestures, binding), 16, Theme.TextBrush));
            content.Children.Add(new TextBlock { Text = Loc.T(selected?.Label ?? K.Triggers.AutoIcon), Foreground = Theme.TextBrush,
                FontSize = 12.5, Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(Fluent.Glyph(expanded ? "\uE70E" : "\uE70D", 10, Theme.TextDimBrush));
            current.Content = content;
            System.Windows.Automation.AutomationProperties.SetName(current, Loc.T(K.Triggers.MenuIcon));
            foreach (var (id, button) in choices)
            {
                bool active = id == (selected?.Id ?? "");
                button.BorderBrush = active ? Theme.AccentBrush : Theme.StrokeSoftBrush;
                button.Background = active ? Theme.RowBgBrush : Brushes.Transparent;
            }
        }
        Button Choice(string id, string glyph, Str label, bool automatic = false)
        {
            var button = Fluent.Ui(new Button { Content = automatic ? null : Fluent.Glyph(glyph, 18, Theme.TextBrush),
                ToolTip = Loc.T(label), Tag = "settings-menu-icon-choice:" + id, MinWidth = 40, MinHeight = 40,
                Margin = new Thickness(3), Padding = new Thickness(8), BorderThickness = new Thickness(1) });
            if (automatic)
            {
                button.Content = Loc.T(label); button.FontSize = 12.5;
                button.HorizontalAlignment = HorizontalAlignment.Right;
            }
            System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(label));
            button.Click += (_, _) =>
            {
                binding.MenuIcon = id; CommitHotkeys(); expanded = false; Refresh();
                TransitionContent(palette, () => { palette.Children.Clear(); choices.Clear(); });
            };
            choices.Add((id, button)); return button;
        }
        current.Click += (_, _) =>
        {
            expanded = !expanded; Refresh();
            TransitionContent(palette, () =>
            {
                palette.Children.Clear(); choices.Clear();
                if (!expanded) return;
                palette.Children.Add(Choice("", "", K.Triggers.AutoIcon, true));
                var icons = new UniformGrid { Columns = 6, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 6) };
                foreach (var icon in TrayMenuIcons.All) icons.Children.Add(Choice(icon.Id, icon.Glyph, icon.Label));
                palette.Children.Add(icons); Refresh();
            });
        };
        root.Children.Add(LabeledRow(Loc.T(K.Triggers.MenuIcon), current, Loc.T(K.Triggers.MenuIcon)));
        root.Children.Add(palette); Refresh();
        root.Loaded += (_, _) => { _settings.Changed += Refresh; Theme.Changed += Refresh; Refresh(); };
        root.Unloaded += (_, _) => { _settings.Changed -= Refresh; Theme.Changed -= Refresh; };
        return root;
    }
}
