using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>配色预览的命中、高亮与单主题编辑。</summary>
public sealed partial class SettingsWindow
{
    private sealed record PalettePart(FrameworkElement Element, string Key, bool Edge);
    private readonly List<PalettePart> _paletteParts = new();
    private readonly List<Action> _palettePaint = new();
    private bool _palettePreviewDark;
    private Grid? _paletteStage;
    private Border? _paletteHighlight;
    private TextBlock? _paletteSelectedName;
    private Border? _paletteSelectedSwatch;
    private Button? _paletteReset;
    private PalettePart? _paletteSelected;

    private void BuildCustomColors(StackPanel panel)
    {
        _palettePreviewDark = Theme.IsDark;
        var card = new StackPanel();
        var tools = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        tools.ColumnDefinitions.Add(new ColumnDefinition());
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tools.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Palette.Hint), Foreground = Theme.TextDimBrush,
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        });
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(modes, 1); tools.Children.Add(modes);
        foreach (bool dark in new[] { false, true })
        {
            var mode = SettingsSurfaceButton(new TextBlock { Text = Loc.T(dark ? K.Settings.Dark : K.Settings.Light), FontSize = 12 });
            mode.Padding = new Thickness(12, 6, 12, 6); mode.Margin = new Thickness(4, 0, 0, 0);
            mode.Tag = dark ? "palette-mode-dark" : "palette-mode-light";
            mode.Click += (_, _) =>
            {
                if (_palettePreviewDark == dark) return;
                ClosePalettePickers();
                _palettePreviewDark = dark;
                PaintPalettePreview();
                _paletteStage!.BeginAnimation(OpacityProperty, new DoubleAnimation(0.5, 1,
                    TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs)));
            };
            _palettePaint.Add(() =>
            {
                bool selected = _palettePreviewDark == dark;
                mode.Background = selected ? Theme.AccentBrush : Theme.RowBgBrush;
                ((TextBlock)mode.Content).Foreground = selected
                    ? new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)) : Theme.TextBrush;
            });
            modes.Children.Add(mode);
        }
        card.Children.Add(tools);
        _paletteStage = BuildPaletteWindow();
        card.Children.Add(_paletteStage);
        var inspector = new Grid { Margin = new Thickness(0, 14, 0, 0), MinHeight = 36 };
        inspector.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inspector.ColumnDefinitions.Add(new ColumnDefinition());
        inspector.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _paletteSelectedSwatch = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0), BorderBrush = Theme.StrokeBrush, BorderThickness = new Thickness(1) };
        inspector.Children.Add(_paletteSelectedSwatch);
        _paletteSelectedName = new TextBlock { FontSize = 12, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_paletteSelectedName, 1); inspector.Children.Add(_paletteSelectedName);
        _paletteReset = Fluent.IconButton("\uE72C", Loc.T(K.Palette.RestoreColor), 30, 14, Theme.TextDimBrush);
        _paletteReset.Tag = "palette-selected-reset";
        _paletteReset.Click += (_, _) => ResetPaletteSelection();
        Grid.SetColumn(_paletteReset, 2); inspector.Children.Add(_paletteReset);
        card.Children.Add(inspector);
        Cardify(card); panel.Children.Add(card);
        panel.Children.Add(RestoreRow());
        _repaint.Add(PaintPalettePreview);
        _paletteSelected = _paletteParts.First(p => p.Key == "WindowBg");
        PaintPalettePreview();
    }

    private void ClosePalettePickers()
    {
        _paletteOpenGeneration++;
        CommitSwatch();
        CommitPaletteAccent();
        if (_palPop != null) _palPop.IsOpen = false;
        if (_accentPop != null) _accentPop.IsOpen = false;
    }

    private Color PaletteColor(string key) => key == "Accent" ? Theme.Accent : Theme.ValueOf(key, _palettePreviewDark);
    private SolidColorBrush PaletteBrush(string key) => new(PaletteColor(key));

    private void PaintPalettePreview()
    {
        foreach (var paint in _palettePaint) paint();
        if (_paletteSelected == null || _paletteSelectedName == null) return;
        string key = _paletteSelected.Key;
        _paletteSelectedName.Text = Loc.F(K.Common.LabelWithContext,
            Loc.T(key == "Accent" ? K.Settings.Accent : TokenLabel(key)), HexColor.Format(PaletteColor(key)));
        _paletteSelectedSwatch!.Background = PaletteBrush(key);
        bool lowContrast = key is "Text" or "TextDim" && new[] { "WindowBg", "PanelBg", "CardBg", "RowBg" }
            .Any(background => Fluent.ContrastRatio(PaletteColor(key), Theme.Opaque(PaletteColor(background))) < 4.5);
        _paletteSelectedName.Foreground = lowContrast ? Theme.WarningBrush : Theme.TextBrush;
        _paletteSelectedName.ToolTip = lowContrast ? Loc.T(K.Settings.LowContrastOnSurfaces) : null;
        _paletteReset!.IsEnabled = key == "Accent"
            ? _settings.Current.Accent.Length > 0 || Theme.Accent != Theme.DefaultAccent
            : Theme.IsCustom(key, _palettePreviewDark);
        _paletteReset.Opacity = _paletteReset.IsEnabled ? 1 : 0.35;
        _paletteReset.ToolTip = Loc.T(key == "Accent" ? K.Palette.RestoreAccent : K.Palette.RestoreColor);
    }

    private void RegisterPalettePart(FrameworkElement element, string key, bool edge = false)
    {
        var part = new PalettePart(element, key, edge);
        _paletteParts.Add(part);
        if (edge) return;
        element.Focusable = true; element.Cursor = Cursors.Hand;
        element.Tag = "palette-part:" + key;
        System.Windows.Automation.AutomationProperties.SetName(element, Loc.T(key == "Accent" ? K.Settings.Accent : TokenLabel(key)));
        element.GotKeyboardFocus += (_, _) => SelectPalettePart(part);
        element.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            SelectPalettePart(part); QueuePalettePart(part); e.Handled = true;
        };
    }

    private PalettePart? HitPalettePart(Point point, DependencyObject? source)
    {
        foreach (var part in _paletteParts.AsEnumerable().Reverse().Where(p => p.Edge))
        {
            var box = PalettePartBounds(part);
            var inside = box; inside.Inflate(-6, -6); box.Inflate(6, 6);
            if (box.Contains(point) && (inside.IsEmpty || !inside.Contains(point))) return part;
        }
        for (var node = source; node != null && node != _paletteStage; node = SettingsInputParent(node))
        {
            var match = _paletteParts.FirstOrDefault(p => !p.Edge && ReferenceEquals(p.Element, node));
            if (match != null) return match;
        }
        return null;
    }

    private Rect PalettePartBounds(PalettePart part)
        => new(part.Element.TranslatePoint(new Point(), _paletteStage), part.Element.RenderSize);

    private void SelectPalettePart(PalettePart part)
    {
        _paletteSelected = part;
        var box = PalettePartBounds(part); box.Inflate(2, 2);
        var highlight = _paletteHighlight!;
        Canvas.SetLeft(highlight, box.Left);
        Canvas.SetTop(highlight, box.Top);
        highlight.Width = box.Width; highlight.Height = box.Height;
        highlight.Visibility = Visibility.Visible;
        PaintPalettePreview();
    }

    private void OpenPalettePart(PalettePart part)
    {
        if (part.Key == "Accent") OpenPaletteAccent(part.Element);
        else OpenSwatchPop(part.Element, part.Key, _palettePreviewDark);
    }

    private void ResetPaletteSelection()
    {
        if (_paletteSelected == null) return;
        string key = _paletteSelected.Key;
        ClosePalettePickers();
        _settings.Mutate(s =>
        {
            if (key == "Accent") s.Accent = "";
            else s.Colors.Remove((_palettePreviewDark ? "dark." : "light.") + key);
            s.ColorPreset = "custom";
        });
        if (key == "Accent") Theme.ResetAccent();
        else Theme.ClearCustom(key, _palettePreviewDark);
        Fluent.ApplyGlobalStyles(); PaintPalettePreview();
    }
}
