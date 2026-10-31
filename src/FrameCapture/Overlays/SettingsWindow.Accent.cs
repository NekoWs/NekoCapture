using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>强调色的即时取色、系统跟随与恢复默认。</summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>当前强调色编辑会话的弹层。</summary>
    Popup? _accentPop;

    /// <summary>弹层里的取色板。</summary>
    ColorPickView? _accentView;
    private Color _accentOriginal;
    private bool _accentEditing;

    #region 强调色

    /// <summary>强调色色块、颜色值与恢复按钮；右键色块跟随系统。</summary>
    /// <param name="card">强调色那一节的容器。</param>
    void AccentCard(StackPanel card)
    {
        card.Children.Add(SectionHeader(Loc.T(K.Settings.Accent)));
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 色块
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 十六进制
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 恢复
        g.Children.Add(IconText("\uE790", Loc.T(K.Settings.AccentColorEditorSelectionsSwitches)));
        var lbl = new TextBlock
        {
            Text = Loc.T(K.Settings.Accent),
            FontSize = 13,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var fill = new Border { CornerRadius = new CornerRadius(4) };
        var cell = new Border
        {
            Width = 34,
            Height = 24,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Cursor = Cursors.Hand,
            Focusable = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            // 探针认路用：版面审计要确认强调色的入口摆在「配色」页上，而不是只存在于代码里
            Tag = "settings-accent-swatch",
            ToolTip = Loc.T(K.Settings.ClickToPickAnAccent),
            // 色块就是取色入口，铺在棋盘上才看得出强调色本身带不带 alpha
            Child = new Grid { Children = { new Rectangle { Fill = ColorPickView.Checker(), RadiusX = 4, RadiusY = 4 }, fill } },
        };
        Grid.SetColumn(cell, 2);
        g.Children.Add(cell);
        Fluent.ChipHover(cell);
        var hex = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
            FontSize = 11,
            Foreground = Theme.TextDimBrush,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 66,
            Tag = "settings-accent-hex",
        };
        Grid.SetColumn(hex, 3);
        g.Children.Add(hex);
        var reset = Fluent.IconButton("\uE72C", Loc.T(K.Palette.RestoreAccent), 26, 13, Theme.TextDimBrush);
        Grid.SetColumn(reset, 4);
        g.Children.Add(reset);

        card.Children.Add(g);
        var hint = new TextBlock
        {
            Text = Loc.T(K.Settings.ClickToPickAnAccent),
            FontSize = 11.5,
            Foreground = Theme.TextDimBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(32, 2, 0, 4),
        };
        card.Children.Add(hint);

        cell.MouseLeftButtonUp += (_, e) => { OpenPaletteAccent(cell); e.Handled = true; };
        cell.MouseRightButtonUp += (_, e) =>
        {
            ClosePalettePickers();
            _settings.Mutate(s => { s.Accent = "system"; s.ColorPreset = "custom"; });
            Theme.Apply(_settings.Current.Theme, "system");
            Fluent.ApplyGlobalStyles();
            _repaintHandler?.Invoke();
            e.Handled = true;
        };
        cell.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { OpenPaletteAccent(cell); e.Handled = true; } };
        cell.GotKeyboardFocus += (_, _) => cell.BorderBrush = Theme.AccentBrush;
        cell.LostKeyboardFocus += (_, _) => cell.BorderBrush = Theme.StrokeFieldBrush;
        reset.Tag = "settings-accent-reset";
        reset.Click += (_, _) =>
        {
            ClosePalettePickers();
            _settings.Mutate(s => { s.Accent = ""; s.ColorPreset = "custom"; });
            Theme.ResetAccent();
            Fluent.ApplyGlobalStyles();
            _repaintHandler?.Invoke();
        };

        _repaint.Add(() =>
        {
            fill.Background = Theme.AccentBrush;
            cell.BorderBrush = Theme.StrokeFieldBrush;
            hex.Text = HexColor.Format(Theme.Accent);
            hint.Text = Loc.T(_settings.Current.Accent == "system" ? K.Settings.FollowSystem : K.Settings.ClickToPickAnAccent);
            hex.Foreground = Theme.TextBrush;
            lbl.Foreground = Theme.TextBrush;
            bool changed = _settings.Current.Accent.Length > 0 || Theme.Accent != Theme.DefaultAccent;
            reset.Opacity = changed ? 1 : 0.35;
            reset.IsEnabled = changed;
        });
    }

    private void OpenPaletteAccent(FrameworkElement target)
    {
        ClosePalettePickers();
        var view = new ColorPickView();
        var popup = new Popup
        {
            Child = Fluent.PopupCard(view), Placement = PlacementMode.Bottom,
            AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            PlacementTarget = target, StaysOpen = false,
        };
        _accentView = view; _accentPop = popup;
        _accentOriginal = Theme.Accent; _accentEditing = true;
        view.Color = _accentOriginal;
        view.Changed += c => Theme.PreviewAccent(Color.FromRgb(c.R, c.G, c.B));
        popup.Closed += (_, _) => { if (ReferenceEquals(_accentPop, popup)) CommitPaletteAccent(); };
        popup.IsOpen = true;
    }

    private void CommitPaletteAccent()
    {
        if (!_accentEditing || _accentView == null) return;
        _accentEditing = false;
        var c = _accentView.Color;
        if (c == _accentOriginal) return;
        var value = HexColor.Format(Color.FromRgb(c.R, c.G, c.B));
        _settings.Mutate(s => { s.Accent = value; s.ColorPreset = "custom"; });
        Theme.Apply(_settings.Current.Theme, value);
        Fluent.ApplyGlobalStyles();
    }

    #endregion
}
