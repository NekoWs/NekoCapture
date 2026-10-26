using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

// 本 partial 职责：配色分页里的强调色那一张卡——色块入口、十六进制、恢复出厂，以及那枚取色弹层的状态。
/// <summary>
/// 设置窗的强调色部分。强调色不逐主题成对，四组令牌都收不下它，所以在「配色」页上单独一张卡：
/// 色块点开取色弹层，拖动即改全局调色板（界面颜色挂在 <see cref="Theme"/> 的共享画刷上，当场全窗跟着变），
/// 收起弹层才把这一枚写进设置并重烤烤进 XAML 的那批颜色。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>强调色弹层：整页只有一枚，挂在强调色那行的色块上。</summary>
    Popup? _accentPop;

    /// <summary>弹层里的取色板。</summary>
    ColorPickView? _accentView;

    #region 强调色

    /// <summary>强调色卡：一行「色块 + 十六进制 + 恢复出厂」，色块点开取色弹层，拖动即时生效。
    /// 强调色不在令牌表里——它不逐主题成对，四个令牌分节都收不下它，所以单独一张卡摆在这一页最上面一段。
    /// 色块右边写着当前值、右下角写着怎么用，这一项才找得到也用得上。</summary>
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
        var reset = Fluent.IconButton("\uE72C", Loc.T(K.Settings.PaletteRestore), 26, 13, Theme.TextDimBrush);
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

        void Open()
        {
            if (_accentPop == null)
            {
                _accentView = new ColorPickView();
                _accentView.Changed += c =>
                {
                    // 拖动中只改全局调色板：界面颜色挂在共享画刷上，当场就跟着走，不落盘也不重烤 XAML
                    Theme.PreviewAccent(Color.FromRgb(c.R, c.G, c.B));
                };
                _accentPop = new Popup
                {
                    Child = Fluent.PopupCard(_accentView),
                    Placement = PlacementMode.Bottom,
                    AllowsTransparency = true,
                    PopupAnimation = PopupAnimation.Fade,
                };
                _accentPop.Closed += (_, _) =>
                {
                    var c = _accentView!.Color;
                    var value = HexColor.Format(Color.FromRgb(c.R, c.G, c.B));
                    _settings.Mutate(s => s.Accent = value);
                    Theme.Apply(_settings.Current.Theme, value);
                    Fluent.ApplyGlobalStyles();   // ToolTip/滚动条/菜单的颜色烤在 XAML 里，收起弹层统一重烤一次
                };
            }
            _accentView!.Color = Theme.Accent;
            _accentPop.PlacementTarget = cell;
            Fluent.OpenPopSticky(_accentPop, this);
        }
        cell.MouseLeftButtonUp += (_, e) => { Open(); e.Handled = true; };
        cell.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Open(); e.Handled = true; } };
        cell.GotKeyboardFocus += (_, _) => cell.BorderBrush = Theme.AccentBrush;
        cell.LostKeyboardFocus += (_, _) => cell.BorderBrush = Theme.StrokeFieldBrush;
        reset.MouseLeftButtonUp += (_, e) =>
        {
            _settings.Mutate(s => s.Accent = "");
            Theme.ResetAccent();
            Fluent.ApplyGlobalStyles();
            e.Handled = true;
        };

        _repaint.Add(() =>
        {
            fill.Background = Theme.AccentBrush;
            cell.BorderBrush = Theme.StrokeFieldBrush;
            hex.Text = HexColor.Format(Theme.Accent);
            hex.Foreground = Theme.TextBrush;
            lbl.Foreground = Theme.TextBrush;
            reset.Opacity = Theme.Accent == Theme.DefaultAccent ? 0.35 : 1;
            reset.IsHitTestVisible = Theme.Accent != Theme.DefaultAccent;
        });
    }

    #endregion
}
