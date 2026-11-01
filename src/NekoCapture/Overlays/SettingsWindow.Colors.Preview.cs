using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>用模拟窗口展示可编辑的表面、文字、边框与交互颜色。</summary>
public sealed partial class SettingsWindow
{
    private Grid BuildPaletteWindow()
    {
        var stage = new Grid { Background = Brushes.Transparent, Margin = new Thickness(6), MinHeight = 360, Tag = "palette-window" };
        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition());
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var window = PaletteSurface("WindowBg", main, new Thickness(0), "Stroke");
        window.CornerRadius = new CornerRadius(12); stage.Children.Add(window);
        _palettePaint.Add(() => window.Background = new SolidColorBrush(Theme.Opaque(PaletteColor("WindowBg"))));

        var (chrome, close) = Fluent.TitleBar("\uE7A8", Loc.T(K.Palette.SampleWindow), Loc.T(K.Common.Close));
        main.Children.Add(chrome); RegisterPalettePart(chrome, "ChromeBg");
        var title = (Grid)chrome.Child;
        RegisterPalettePart((FrameworkElement)title.Children[0], "Accent");
        RegisterPalettePart((FrameworkElement)title.Children[1], "Text");
        RegisterPalettePart(close, "Text");
        _palettePaint.Add(() =>
        {
            chrome.Background = PaletteBrush("ChromeBg");
            ((TextBlock)title.Children[0]).Foreground = PaletteBrush("Accent");
            ((TextBlock)title.Children[1]).Foreground = close.Foreground = PaletteBrush("Text");
        });

        var workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(workspace, 1); main.Children.Add(workspace);
        var navList = new StackPanel();
        var nav = PaletteSurface("NavBg", navList, new Thickness(6), "StrokeSoft");
        nav.CornerRadius = new CornerRadius(8); nav.Margin = new Thickness(12, 10, 8, 12);
        nav.VerticalAlignment = VerticalAlignment.Top; workspace.Children.Add(nav);
        foreach (var (label, glyph, selected) in new[]
        {
            (K.Settings.Hotkeys, "\uE765", false), (K.Common.Capture, "\uE7A8", true), (K.Common.Recording, "\uE714", false),
        })
        {
            var row = new Grid { Margin = new Thickness(12, 0, 8, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = PaletteText("TextDim", glyph, 15); icon.FontFamily = new FontFamily("Segoe Fluent Icons"); row.Children.Add(icon);
            var text = PaletteText("Text", Loc.T(label), 13); Grid.SetColumn(text, 1); row.Children.Add(text);
            var item = new Border { Child = row, Height = 38, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 1, 0, 1) };
            RegisterPalettePart(item, selected ? "Accent" : "NavBg");
            _palettePaint.Add(() =>
            {
                var backing = Fluent.NavBacking(PaletteColor("NavBg"), selected, false);
                item.Background = new SolidColorBrush(backing);
                text.Foreground = new SolidColorBrush(selected ? Fluent.AccentTextOn(backing) : Fluent.InkOn(backing, PaletteColor("Text")));
                icon.Foreground = new SolidColorBrush(selected ? Fluent.AccentTextOn(backing) : Fluent.InkOn(backing, PaletteColor("TextDim")));
            });
            if (selected)
            {
                item.Child = null;
                var layers = new Grid(); layers.Children.Add(row);
                var indicator = PaletteSurface("Accent", new Grid(), new Thickness(0));
                indicator.Width = 3; indicator.Height = 18; indicator.CornerRadius = new CornerRadius(2);
                indicator.HorizontalAlignment = HorizontalAlignment.Left; indicator.Margin = new Thickness(0, 0, 0, 0);
                layers.Children.Add(indicator); item.Child = layers;
            }
            navList.Children.Add(item);
        }
        var divider = PaletteSurface("Stroke", new Grid(), new Thickness(0));
        divider.CornerRadius = new CornerRadius(0); Grid.SetColumn(divider, 1); workspace.Children.Add(divider);
        var content = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        Grid.SetColumn(content, 2); workspace.Children.Add(content);
        var heading = PaletteText("Text", Loc.T(K.Common.Settings), 17); heading.FontWeight = FontWeights.SemiBold; content.Children.Add(heading);
        var secondary = PaletteText("TextDim", Loc.T(K.Settings.TokenTextDim), 11.5);
        secondary.Margin = new Thickness(0, 5, 0, 12); content.Children.Add(secondary);

        var controls = new StackPanel();
        var card = PaletteSurface("CardBg", controls, new Thickness(12, 6, 12, 6), "Stroke");
        card.CornerRadius = new CornerRadius(8); StyleSettingsCard(card, outerSpacing: false);
        content.Children.Add(card);
        var input = PaletteSurface("RowBg", PaletteText("TextDim", Loc.T(K.Palette.SampleInput), 12), new Thickness(10, 7, 10, 7), "StrokeField");
        input.CornerRadius = new CornerRadius(10); input.Margin = new Thickness(0, 6, 0, 12); controls.Children.Add(input);
        var exampleRow = new Grid();
        var rowLabel = PaletteText("Text", Loc.T(K.Settings.TokenRowBg), 12); exampleRow.Children.Add(rowLabel);
        var track = PaletteSurface("TrackBg", new Ellipse { Width = 12, Height = 12, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left }, new Thickness(4, 3, 4, 3), "StrokeField");
        track.Width = 40; track.Height = 20; track.CornerRadius = new CornerRadius(10);
        track.HorizontalAlignment = HorizontalAlignment.Right; exampleRow.Children.Add(track);
        var toggleRow = new Border { Child = exampleRow, Padding = new Thickness(0, 8, 0, 8), Background = Brushes.Transparent };
        controls.Children.Add(toggleRow);

        var states = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        for (int i = 0; i < 3; i++) states.ColumnDefinitions.Add(new ColumnDefinition());
        int column = 0;
        foreach (string key in new[] { "Hover", "Press", "RowHover" })
        {
            var overlay = PaletteSurface(key, PaletteText("Text", Loc.T(TokenLabel(key)), 10.5), new Thickness(5, 8, 5, 8));
            overlay.Margin = new Thickness(column == 0 ? 0 : 5, 0, 0, 0);
            var backing = new Border { Background = PaletteBrush("CardBg"), CornerRadius = new CornerRadius(6), Child = overlay };
            _palettePaint.Add(() => backing.Background = PaletteBrush("CardBg"));
            Grid.SetColumn(backing, column++); states.Children.Add(backing);
        }
        content.Children.Add(states);
        var chip = PaletteSurface("ChipBg", PaletteText("Text", Loc.T(K.Settings.TokenChip), 12), new Thickness(10, 5, 10, 5), "StrokeField");
        chip.CornerRadius = new CornerRadius(10); chip.HorizontalAlignment = HorizontalAlignment.Left;
        chip.Margin = new Thickness(0, 10, 0, 0); content.Children.Add(chip);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var keys = PaletteSurface("KeycapBg", PaletteText("Text", "Ctrl", 11), new Thickness(7, 2, 7, 2), "StrokeField");
        keys.BorderThickness = new Thickness(1, 1, 1, 2);
        ((TextBlock)keys.Child).FontFamily = new FontFamily(Keycaps.MonoFont); ((TextBlock)keys.Child).FontWeight = FontWeights.SemiBold;
        keys.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(keys);
        var confirm = PaletteSurface("Accent", new TextBlock { Text = Loc.T(K.Common.Capture), FontSize = 12 }, new Thickness(12, 6, 12, 6));
        _palettePaint.Add(() => ((TextBlock)confirm.Child).Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)));
        Grid.SetColumn(confirm, 1); footer.Children.Add(confirm);
        var panel = PaletteSurface("PanelBg", footer, new Thickness(14, 10, 14, 10));
        panel.CornerRadius = new CornerRadius(10); panel.Margin = new Thickness(12, 0, 12, 12);
        panel.BorderThickness = new Thickness(1); RegisterPalettePart(panel, "Stroke", edge: true);
        _palettePaint.Add(() => { panel.Background = new SolidColorBrush(Theme.Opaque(PaletteColor("PanelBg"))); panel.BorderBrush = PaletteBrush("Stroke"); });
        Grid.SetRow(panel, 2); main.Children.Add(panel);

        var statuses = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        foreach (string key in new[] { "Danger", "Success", "Warning" })
        {
            var label = PaletteText(key, Loc.T(TokenLabel(key)), 11.5);
            label.Margin = new Thickness(0, 0, 16, 0); statuses.Children.Add(label);
        }
        content.Children.Add(statuses);

        _paletteHighlight = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)), BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.5 },
        };
        var highlights = new Canvas { IsHitTestVisible = false };
        highlights.Children.Add(_paletteHighlight); stage.Children.Add(highlights);
        stage.PreviewMouseMove += (_, e) =>
        {
            var part = HitPalettePart(e.GetPosition(stage), e.OriginalSource as DependencyObject);
            if (part != null && (part != _paletteSelected || _paletteHighlight.Visibility != Visibility.Visible)) SelectPalettePart(part);
            stage.Cursor = part != null ? Cursors.Hand : Cursors.Arrow;
        };
        stage.MouseLeave += (_, _) => _paletteHighlight.Visibility = Visibility.Collapsed;
        stage.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var part = HitPalettePart(e.GetPosition(stage), e.OriginalSource as DependencyObject);
            if (part == null) return;
            BeginPalettePointer(part); e.Handled = true;
        };
        stage.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_palettePressed == null) return;
            Point point = e.GetPosition(stage);
            EndPalettePointer(HitPalettePart(point, stage.InputHitTest(point) as DependencyObject));
            e.Handled = true;
        };
        stage.LostMouseCapture += (_, _) => _palettePressed = null;
        stage.SizeChanged += (_, _) =>
        {
            if (_paletteSelected != null && _paletteHighlight.Visibility == Visibility.Visible) SelectPalettePart(_paletteSelected);
        };
        return stage;
    }

    private Border PaletteSurface(string key, UIElement child, Thickness padding, string? stroke = null)
    {
        var surface = new Border { Child = child, Padding = padding, CornerRadius = new CornerRadius(6) };
        RegisterPalettePart(surface, key);
        _palettePaint.Add(() => surface.Background = PaletteBrush(key));
        if (stroke != null)
        {
            surface.BorderThickness = new Thickness(1);
            RegisterPalettePart(surface, stroke, edge: true);
            _palettePaint.Add(() => surface.BorderBrush = PaletteBrush(stroke));
        }
        return surface;
    }

    private TextBlock PaletteText(string key, string text, double size)
    {
        var label = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap };
        RegisterPalettePart(label, key);
        _palettePaint.Add(() => label.Foreground = PaletteBrush(key));
        return label;
    }
}
