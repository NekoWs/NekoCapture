using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

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
        window.CornerRadius = new CornerRadius(10); stage.Children.Add(window);

        var title = new Grid();
        title.Children.Add(PaletteText("Text", Loc.T(K.Palette.SampleWindow), 13));
        var close = PaletteText("TextDim", "\uE711", 12);
        close.FontFamily = new FontFamily("Segoe Fluent Icons"); close.HorizontalAlignment = HorizontalAlignment.Right;
        title.Children.Add(close);
        var chrome = PaletteSurface("ChromeBg", title, new Thickness(14, 12, 14, 12));
        chrome.CornerRadius = new CornerRadius(9, 9, 0, 0); main.Children.Add(chrome);

        var workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(workspace, 1); main.Children.Add(workspace);
        var navList = new StackPanel { Margin = new Thickness(9, 14, 9, 14) };
        var nav = PaletteSurface("NavBg", navList, new Thickness(0)); workspace.Children.Add(nav);
        foreach (var label in new[] { K.Settings.Hotkeys, K.Common.Capture, K.Common.Recording })
        {
            var item = PaletteSurface("ChipBg", PaletteText("Text", Loc.T(label), 11.5), new Thickness(8, 8, 8, 8));
            item.Margin = new Thickness(0, 0, 0, 7); navList.Children.Add(item);
        }
        var content = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        Grid.SetColumn(content, 1); workspace.Children.Add(content);
        content.Children.Add(PaletteText("Text", Loc.T(K.Common.Settings), 17));
        var secondary = PaletteText("TextDim", Loc.T(K.Settings.TokenTextDim), 11.5);
        secondary.Margin = new Thickness(0, 5, 0, 12); content.Children.Add(secondary);

        var controls = new StackPanel();
        var card = PaletteSurface("CardBg", controls, new Thickness(12), "StrokeSoft");
        content.Children.Add(card);
        var input = PaletteSurface("RowBg", PaletteText("TextDim", Loc.T(K.Palette.SampleInput), 12), new Thickness(10, 8, 10, 8), "StrokeField");
        input.Margin = new Thickness(0, 0, 0, 12); controls.Children.Add(input);
        var exampleRow = new Grid();
        var rowLabel = PaletteText("Text", Loc.T(K.Settings.TokenRowBg), 12); exampleRow.Children.Add(rowLabel);
        var track = PaletteSurface("TrackBg", new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left }, new Thickness(4));
        track.Width = 34; track.Height = 18; track.CornerRadius = new CornerRadius(9);
        track.HorizontalAlignment = HorizontalAlignment.Right; exampleRow.Children.Add(track);
        var row = PaletteSurface("RowBg", exampleRow, new Thickness(9, 8, 9, 8)); controls.Children.Add(row);

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

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var keys = PaletteSurface("KeycapBg", PaletteText("Text", "Ctrl", 11), new Thickness(8, 5, 8, 5), "StrokeField");
        keys.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(keys);
        var confirm = PaletteSurface("Accent", new TextBlock { Text = Loc.T(K.Common.Capture), FontSize = 12 }, new Thickness(12, 6, 12, 6));
        _palettePaint.Add(() => ((TextBlock)confirm.Child).Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)));
        Grid.SetColumn(confirm, 1); footer.Children.Add(confirm);
        var panel = PaletteSurface("PanelBg", footer, new Thickness(14, 10, 14, 10));
        panel.CornerRadius = new CornerRadius(0, 0, 9, 9); Grid.SetRow(panel, 2); main.Children.Add(panel);

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
        stage.Children.Add(_paletteHighlight);
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
            SelectPalettePart(part); OpenPalettePart(part); e.Handled = true;
        };
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
