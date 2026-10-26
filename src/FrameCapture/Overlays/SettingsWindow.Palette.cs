using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

// 本 partial 职责：配色分页——预设整套、逐枚令牌的明暗两色、强调色、恢复出厂配色，以及一块跟着改动实时变的预览。
/// <summary>
/// 设置窗的配色分页。每一枚 <see cref="ColorToken"/> 一行，行内明暗两枚色块各开各的取色弹层：
/// 拖动即时生效（<see cref="Theme.SetCustom"/> 广播 <see cref="Theme.Changed"/>，本页与其余窗口一起变色），
/// 弹层收起才落盘并原地重建本窗——本窗的颜色是构造期取值的静态画刷。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>本页订阅 <see cref="Theme.Changed"/> 的处理器；关窗时摘掉，静态事件不拖住已关的窗。</summary>
    Action? _palHandler;

    /// <summary>配色弹层：一枚共享弹层在色块之间挪位，谁被点就挂到谁身上。</summary>
    Popup? _palPop;
    ColorPickView? _palView;

    /// <summary>弹层当前在改哪一枚令牌；null = 弹层没在改任何东西。</summary>
    string? _palKey;

    /// <summary>弹层当前在改这一枚的哪一侧主题。</summary>
    bool _palDark;

    /// <summary>配色页上所有「按当前调色板重画自己」的小段：色块、十六进制、预览、预设色带。</summary>
    readonly List<Action> _palRepaint = new();

    /// <summary>装配配色分页：预设 → 预览 → 按组分节的令牌行 → 恢复出厂。</summary>
    /// <param name="panel">这一页的容器，由 <c>Build</c> 创建并交给分页表。</param>
    void BuildPalette(StackPanel panel)
    {
        _palHandler = () => { foreach (var f in _palRepaint) f(); };
        Theme.Changed += _palHandler;
        Closed += (_, _) => { if (_palHandler != null) Theme.Changed -= _palHandler; };

        var presets = new StackPanel();
        PresetCard(presets);
        Cardify(presets);
        panel.Children.Add(presets);

        var preview = new StackPanel();
        PreviewCard(preview);
        Cardify(preview);
        panel.Children.Add(preview);

        foreach (var group in (ColorGroup[])Enum.GetValues(typeof(ColorGroup)))
        {
            var tokens = Theme.Tokens.Where(t => t.Group == group).ToList();
            if (tokens.Count == 0) continue;
            var sec = new StackPanel();
            sec.Children.Add(SectionHeader(Loc.T(GroupLabel(group))));
            sec.Children.Add(TokenHeaderRow());
            foreach (var t in tokens) sec.Children.Add(TokenRow(t));
            Cardify(sec);
            panel.Children.Add(sec);
        }

        var restore = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
        restore.Children.Add(RestoreRow());
        panel.Children.Add(restore);

        // 每段重绘只在 Theme.Changed 上挂；建完页先跑一遍，色块与十六进制才有出厂值，不必等第一次改动。
        foreach (var f in _palRepaint) f();
    }

    #region 预设与恢复

    /// <summary>预设卡：每套预设一枚芯片，芯片上是那套配色的四段色带，点了整套套用。</summary>
    /// <param name="card">预设那一节的容器。</param>
    void PresetCard(StackPanel card)
    {
        card.Children.Add(SectionHeader(Loc.T(K.Settings.PalettePresets)));
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 4) };
        foreach (var p in ColorPresets.All)
        {
            var bands = new List<Border>();
            var bandHost = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < 4; i++)
            {
                var b = new Border { Width = 15, Height = 20, Margin = i == 0 ? new Thickness(0) : new Thickness(1, 0, 0, 0) };
                bandHost.Children.Add(b);
                bands.Add(b);
            }
            // 浅色那几段和芯片底色差不到一档，描一圈细边才看得出这是一条带子
            var bandCard = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Child = bandHost,
            };
            var name = new TextBlock
            {
                Text = Loc.T(PresetLabel(p.Id)),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Theme.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            };
            var el = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Focusable = true,
                ToolTip = Loc.T(PresetLabel(p.Id)),
                Child = new StackPanel { Children = { bandCard, name } },
            };
            el.MouseLeftButtonUp += (_, e) => { ApplyPreset(p); e.Handled = true; };
            el.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { ApplyPreset(p); e.Handled = true; } };
            Fluent.ChipHover(el);
            _palRepaint.Add(() =>
            {
                var sw = p.Swatches(Theme.IsDark);
                for (int i = 0; i < bands.Count; i++) bands[i].Background = new SolidColorBrush(sw[i]);
                bandCard.BorderBrush = new SolidColorBrush(Theme.StrokeSoft);
                // 设置里没写预设标识 = 谁都没套过，亮起来的是出厂那一枚
                var cur = _settings.Current.ColorPreset;
                bool on = string.IsNullOrEmpty(cur) ? p.Id == "default" : cur == p.Id;
                el.Background = new SolidColorBrush(on ? Fluent.AccentOn(Theme.CardBg, 0.18) : Theme.RowBg);
                el.BorderBrush = new SolidColorBrush(on ? Theme.Accent : Theme.StrokeSoft);
                name.Foreground = new SolidColorBrush(on ? Fluent.AccentText() : Theme.Text);
            });
            strip.Children.Add(el);
        }
        card.Children.Add(strip);
    }

    /// <summary>套用一整套预设：覆盖表整体换成预设的值，落盘后原地重建本窗。</summary>
    /// <param name="p">要套的预设；出厂预设的覆盖表是空的。</param>
    void ApplyPreset(ColorPreset p)
    {
        _settings.Mutate(s =>
        {
            s.ColorPreset = p.Id;
            s.Colors = new Dictionary<string, string>(p.Colors, StringComparer.Ordinal);
        });
        _settings.Current.ApplyPalette();
        Fluent.ApplyGlobalStyles();
        RebuildInPlace();
    }

    /// <summary>恢复出厂配色：清掉全部覆盖、预设标识与自定义强调色。</summary>
    /// <returns>整宽的一枚按钮。</returns>
    FrameworkElement RestoreRow()
    {
        var btn = Fluent.RoundButton(Loc.T(K.Settings.PaletteRestore), Theme.RowBg, Theme.Text, 14, true);
        btn.HorizontalAlignment = HorizontalAlignment.Stretch;
        btn.HorizontalContentAlignment = HorizontalAlignment.Center;
        btn.ToolTip = Loc.T(K.Settings.PaletteRestore);
        btn.MouseLeftButtonUp += (_, e) =>
        {
            _settings.Mutate(s =>
            {
                s.Colors.Clear();
                s.ColorPreset = "";
                s.Accent = "";
            });
            Theme.ClearAllCustoms();
            Theme.ResetAccent();
            Fluent.ApplyGlobalStyles();
            RebuildInPlace();
            e.Handled = true;
        };
        return btn;
    }

    #endregion

    #region 预览

    /// <summary>预览卡：一小块按当前调色板画出来的界面，改哪一枚色，这里当场跟着变。</summary>
    /// <param name="card">预览那一节的容器。</param>
    void PreviewCard(StackPanel card)
    {
        card.Children.Add(SectionHeader(Loc.T(K.Settings.PalettePreview)));

        var body = new TextBlock { Text = Loc.T(K.Settings.TokenText), FontSize = 13, Foreground = new SolidColorBrush(Theme.Text) };
        var dim = new TextBlock { Text = Loc.T(K.Settings.TokenTextDim), FontSize = 12, Foreground = new SolidColorBrush(Theme.TextDim), Margin = new Thickness(10, 0, 0, 0) };
        var line1 = new StackPanel { Orientation = Orientation.Horizontal };
        line1.Children.Add(body); line1.Children.Add(dim);

        var rowPill = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 8, 0, 0) };
        var rowText = new TextBlock { Text = Loc.T(K.Settings.TokenRowBg), FontSize = 12.5, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Width = 32, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var knob = new Ellipse { Width = 10, Height = 10, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
        track.Child = knob;
        var rowGrid = new Grid();
        rowGrid.Children.Add(rowText); rowGrid.Children.Add(track);
        rowPill.Child = rowGrid;

        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var dotLabels = new[] { K.Settings.TokenDanger, K.Settings.TokenSuccess, K.Settings.TokenWarning };
        var dotFills = new List<Ellipse>();
        foreach (var label in dotLabels)
        {
            var dot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
            var txt = new TextBlock { Text = Loc.T(label), FontSize = 11.5, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 16, 0) };
            dotFills.Add(dot);
            dots.Children.Add(dot); dots.Children.Add(txt);
        }

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var chipOn = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), Child = new TextBlock { Text = Loc.T(K.Settings.Accent), FontSize = 12 } };
        var chipOff = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(12, 4, 12, 4), Child = new TextBlock { Text = Loc.T(K.Settings.TokenStroke), FontSize = 12 } };
        chips.Children.Add(chipOn); chips.Children.Add(chipOff);

        var surface = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 2, 0, 4),
            Child = new StackPanel { Children = { line1, rowPill, dots, chips } },
        };
        card.Children.Add(surface);

        _palRepaint.Add(() =>
        {
            body.Foreground = new SolidColorBrush(Theme.Text);
            dim.Foreground = new SolidColorBrush(Theme.TextDim);
            surface.Background = new SolidColorBrush(Theme.CardBg);
            surface.BorderBrush = new SolidColorBrush(Theme.Stroke);
            rowPill.Background = new SolidColorBrush(Theme.RowBg);
            rowPill.BorderBrush = new SolidColorBrush(Theme.StrokeSoft);
            rowText.Foreground = new SolidColorBrush(Theme.Text);
            track.Background = new SolidColorBrush(Theme.Accent);
            track.BorderBrush = new SolidColorBrush(Theme.Accent);
            knob.Fill = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent));
            knob.Stroke = new SolidColorBrush(Theme.StrokeSoft);
            dotFills[0].Fill = new SolidColorBrush(Theme.Danger);
            dotFills[1].Fill = new SolidColorBrush(Theme.Success);
            dotFills[2].Fill = new SolidColorBrush(Theme.Warning);
            chipOn.Background = new SolidColorBrush(Theme.Accent);
            ((TextBlock)chipOn.Child!).Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent));
            chipOff.Background = new SolidColorBrush(Theme.RowBg);
            chipOff.BorderBrush = new SolidColorBrush(Theme.StrokeSoft);
            ((TextBlock)chipOff.Child!).Foreground = new SolidColorBrush(Theme.Text);
        });
    }

    #endregion

    #region 令牌行

    /// <summary>令牌表头：两列色块各自说明是明还是暗，行里就不必再重复一遍。</summary>
    /// <returns>与令牌行同列宽的一行小字。</returns>
    FrameworkElement TokenHeaderRow()
    {
        var g = TokenGrid();
        g.Children.Add(HeaderCell(Loc.T(K.Settings.Light), 1));
        g.Children.Add(HeaderCell(Loc.T(K.Settings.Dark), 3));
        g.Margin = new Thickness(0, 2, 0, 0);
        return g;
    }

    /// <summary>表头的一格：与 <see cref="SwatchCell"/> 同构的「34 宽色块位 + 66 最小宽十六进制位」，
    /// 两个 Grid 的 Auto 列宽才会一致，小字才正好落在自己那列色块上方。</summary>
    /// <param name="text">已取好词的文字。</param>
    /// <param name="col">落在第几列（1 = 浅色，3 = 深色）。</param>
    /// <returns>跨两列的一格表头。</returns>
    static FrameworkElement HeaderCell(string text, int col)
    {
        var box = new Border { Width = 34, Margin = new Thickness(0, 0, 6, 0) };
        var lbl = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = new SolidColorBrush(Theme.TextDim),
            MinWidth = 66,
        };
        var pair = new StackPanel { Orientation = Orientation.Horizontal };
        pair.Children.Add(box); pair.Children.Add(lbl);
        Grid.SetColumn(pair, col);
        Grid.SetColumnSpan(pair, 2);
        return pair;
    }

    /// <summary>一枚令牌的一行：名字 + 明色块与十六进制 + 暗色块与十六进制 + 恢复出厂。</summary>
    /// <param name="token">要编辑的令牌。</param>
    /// <returns>可直接加进分节的行。</returns>
    FrameworkElement TokenRow(ColorToken token)
    {
        var g = TokenGrid();
        var name = new TextBlock
        {
            Text = Loc.T(TokenLabel(token.Key)),
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };
        g.Children.Add(name);

        var reset = Fluent.IconButton("\uE72C", Loc.T(K.Settings.PaletteRestore), 26, 13, new SolidColorBrush(Theme.TextDim));
        Grid.SetColumn(reset, 5);
        g.Children.Add(reset);

        _palRepaint.Add(() =>
        {
            name.Foreground = new SolidColorBrush(Theme.Text);
            bool any = Theme.IsCustom(token.Key, true) || Theme.IsCustom(token.Key, false);
            reset.Opacity = any ? 1 : 0.35;
            reset.IsHitTestVisible = any;
        });
        reset.MouseLeftButtonUp += (_, e) =>
        {
            _settings.Mutate(s =>
            {
                s.Colors.Remove("dark." + token.Key);
                s.Colors.Remove("light." + token.Key);
            });
            Theme.ClearCustom(token.Key, true);
            Theme.ClearCustom(token.Key, false);
            Fluent.ApplyGlobalStyles();
            RebuildInPlace();
            e.Handled = true;
        };

        g.Children.Add(SwatchCell(token, false));
        g.Children.Add(SwatchCell(token, true));
        return g;
    }

    /// <summary>一枚令牌在一侧主题下的色块与十六进制：点色块开取色弹层，拖动即时生效。</summary>
    /// <param name="token">要编辑的令牌。</param>
    /// <param name="dark">编辑深色还是浅色。</param>
    /// <returns>已挂到第 1/2（浅色）或第 3/4（深色）列的一对元素。</returns>
    FrameworkElement SwatchCell(ColorToken token, bool dark)
    {
        int col = dark ? 3 : 1;
        var fill = new Border { CornerRadius = new CornerRadius(4) };
        var cell = new Border
        {
            Width = 34,
            Height = 24,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = Loc.T(TokenLabel(token.Key)),
            // 半透明令牌（描边、悬停叠加）铺在棋盘上才看得出透明度，铺在卡片上永远像不透明
            Child = new Grid { Children = { new Rectangle { Fill = ColorPickView.Checker(), RadiusX = 4, RadiusY = 4 }, fill } },
        };
        var hex = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 66,
        };

        _palRepaint.Add(() =>
        {
            var c = Theme.ValueOf(token.Key, dark);
            fill.Background = new SolidColorBrush(c);
            cell.BorderBrush = new SolidColorBrush(Theme.Stroke);
            hex.Text = HexColor.Format(c);
            hex.Foreground = new SolidColorBrush(Theme.IsCustom(token.Key, dark) ? Theme.Text : Theme.TextDim);
        });

        cell.MouseLeftButtonUp += (_, e) => { OpenSwatchPop(cell, token.Key, dark); e.Handled = true; };
        cell.Focusable = true;
        cell.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { OpenSwatchPop(cell, token.Key, dark); e.Handled = true; } };
        Fluent.ChipHover(cell);

        var pair = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pair.Children.Add(cell); pair.Children.Add(hex);
        Grid.SetColumn(pair, col);
        Grid.SetColumnSpan(pair, 2);
        return pair;
    }

    /// <summary>令牌行的列骨架：名字占满剩余宽度，两对「色块 + 十六进制」与恢复按钮各自贴右。</summary>
    /// <returns>六列的 <c>Grid</c>；色块那两列由 <see cref="SwatchCell"/> 跨列填入。</returns>
    static Grid TokenGrid()
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2), MinHeight = 32 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return g;
    }

    /// <summary>打开（或挪动）那枚共享的取色弹层，挂到点下来的色块上。</summary>
    /// <param name="target">色块本身，弹层贴着它下沿展开。</param>
    /// <param name="key">要改的令牌键名。</param>
    /// <param name="dark">改深色还是浅色。</param>
    void OpenSwatchPop(FrameworkElement target, string key, bool dark)
    {
        if (_palPop == null)
        {
            _palView = new ColorPickView();
            _palPop = new Popup
            {
                Child = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Theme.PanelBgSolid),
                    BorderBrush = new SolidColorBrush(Theme.Stroke),
                    Padding = new Thickness(10),
                    Child = _palView,
                },
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
            };
            _palPop.Closed += (_, _) =>
            {
                if (_palView != null) _palView.Changed -= PreviewSwatch;
                CommitSwatch();
            };
        }
        _palKey = key;
        _palDark = dark;
        _palPop.PlacementTarget = target;
        _palView!.Color = Theme.ValueOf(key, dark);
        _palView.Changed += PreviewSwatch;
        _palPop.IsOpen = true;
    }

    /// <summary>拖动中的即时生效：只改全局调色板，不落盘、不重建窗口。</summary>
    /// <param name="c">取色板当前的颜色。</param>
    void PreviewSwatch(Color c)
    {
        if (_palKey == null) return;
        Theme.SetCustom(_palKey, _palDark, c);
        Fluent.ApplyGlobalStyles();
    }

    /// <summary>弹层收起：把这一枚的自定义色写进设置并原地重建本窗。没改动过就不折腾窗口。</summary>
    void CommitSwatch()
    {
        var key = _palKey;
        if (key == null || _palView == null) return;
        _palKey = null;
        var mk = (_palDark ? "dark." : "light.") + key;
        var hex = HexColor.Format(_palView.Color);
        if (_settings.Current.Colors.TryGetValue(mk, out var old) && old == hex) return;
        _settings.Mutate(s => s.Colors[mk] = hex);
        Fluent.ApplyGlobalStyles();
        RebuildInPlace();
    }

    #endregion

    #region 文案落点

    /// <summary>令牌在界面上的名字。</summary>
    /// <param name="key">令牌键名。</param>
    /// <returns>对应的文案；没登记的令牌拿键名自己当名字，界面上看得见，不至于显示成别的东西。</returns>
    static Str TokenLabel(string key) => key switch
    {
        "WindowBg" => K.Settings.TokenWindowBg,
        "PanelBg" => K.Settings.TokenPanelBg,
        "ChromeBg" => K.Settings.TokenChromeBg,
        "CardBg" => K.Settings.TokenCardBg,
        "RowBg" => K.Settings.TokenRowBg,
        "NavBg" => K.Settings.TokenNavBg,
        "Text" => K.Settings.TokenText,
        "TextDim" => K.Settings.TokenTextDim,
        "Stroke" => K.Settings.TokenStroke,
        "StrokeSoft" => K.Settings.TokenStrokeSoft,
        "Hover" => K.Settings.TokenHover,
        "Press" => K.Settings.TokenPress,
        "RowHover" => K.Settings.TokenRowHover,
        "TrackBg" => K.Settings.TokenTrack,
        "Danger" => K.Settings.TokenDanger,
        "Success" => K.Settings.TokenSuccess,
        "Warning" => K.Settings.TokenWarning,
        _ => new Str("token." + key, key, key),
    };

    /// <summary>分节标题。</summary>
    /// <param name="group">分节。</param>
    /// <returns>对应的文案。</returns>
    static Str GroupLabel(ColorGroup group) => group switch
    {
        ColorGroup.Surface => K.Settings.GroupSurface,
        ColorGroup.Text => K.Settings.GroupText,
        ColorGroup.Line => K.Settings.GroupLine,
        ColorGroup.Interaction => K.Settings.GroupInteraction,
        _ => K.Settings.GroupSemantic,
    };

    /// <summary>预设名。</summary>
    /// <param name="id">预设标识。</param>
    /// <returns>对应的文案；认不出的标识拿标识自己当名字。</returns>
    static Str PresetLabel(string id) => id switch
    {
        "default" => K.Settings.PresetDefault,
        "contrast" => K.Settings.PresetContrast,
        "graphite" => K.Settings.PresetGraphite,
        "midnight" => K.Settings.PresetMidnight,
        "sand" => K.Settings.PresetSand,
        _ => new Str("preset." + id, id, id),
    };

    /// <summary>节标题：卡片里一组同类设置行上方那行小字。</summary>
    /// <param name="text">已取好词的文字。</param>
    /// <returns>不可聚焦的小标题。</returns>
    static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Theme.TextDim),
        Margin = new Thickness(2, 6, 0, 0),
    };

    #endregion
}
