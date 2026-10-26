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
/// 拖动即时生效——<see cref="Theme.SetCustom"/> 改的是全局调色板，界面颜色挂在 <see cref="Theme"/> 的共享画刷上，
/// 本窗与其余窗口当场一起变；弹层收起只是把这份改动写进设置，不重建窗口。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>本窗订阅 <see cref="Theme.Changed"/> 的处理器：调色板一动，<see cref="_repaint"/> 里的小段各自重画。
    /// 关窗时摘掉，静态事件不拖住已关的窗。</summary>
    Action? _repaintHandler;

    /// <summary>配色弹层：一枚共享弹层在色块之间挪位，谁被点就挂到谁身上。</summary>
    Popup? _palPop;
    ColorPickView? _palView;

    /// <summary>弹层当前在改哪一枚令牌；null = 弹层没在改任何东西。</summary>
    string? _palKey;

    /// <summary>弹层当前在改这一枚的哪一侧主题。</summary>
    bool _palDark;

    /// <summary>本窗所有「按当前调色板重画自己」的小段：配色页的色块、十六进制、预览、预设色带，
    /// 以及外观页主题芯片的选中态。共享画刷管颜色，这里管「哪一项算选中」这类构造期算出来的判断。</summary>
    readonly List<Action> _repaint = new();

    /// <summary>装配配色分页：预设 → 预览 → 按组分节的令牌行 → 恢复出厂。</summary>
    /// <param name="panel">这一页的容器，由 <c>Build</c> 创建并交给分页表。</param>
    void BuildPalette(StackPanel panel)
    {
        _repaintHandler = () => { foreach (var f in _repaint) f(); };
        Theme.Changed += _repaintHandler;
        Closed += (_, _) => { if (_repaintHandler != null) Theme.Changed -= _repaintHandler; };

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
        foreach (var f in _repaint) f();
    }

    #region 预设与恢复

    /// <summary>预设卡：每套预设一枚芯片，芯片上是那套配色上下两行色带（上浅色侧、下深色侧），点了整套套用。</summary>
    /// <param name="card">预设那一节的容器。</param>
    void PresetCard(StackPanel card)
    {
        card.Children.Add(SectionHeader(Loc.T(K.Settings.PalettePresets)));
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 4) };
        foreach (var p in ColorPresets.All)
        {
            // 色带两行：上一行是这套配色的浅色侧，下一行是深色侧。
            // 只摆当前那一侧的话，浅色主题下几套预设看上去几乎一模一样——差别全在另一侧。
            var bands = new List<Border>();
            var bandHost = new StackPanel { Orientation = Orientation.Vertical };
            for (int side = 0; side < 2; side++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                for (int i = 0; i < 4; i++)
                {
                    var b = new Border { Width = 15, Height = 10, Margin = new Thickness(i == 0 ? 0 : 1, side == 0 ? 0 : 1, 0, 0) };
                    row.Children.Add(b);
                    bands.Add(b);
                }
                bandHost.Children.Add(row);
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
                Foreground = Theme.TextBrush,
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
            _repaint.Add(() =>
            {
                var lt = p.Swatches(false);
                var dk = p.Swatches(true);
                for (int i = 0; i < 4; i++)
                {
                    bands[i].Background = new SolidColorBrush(lt[i]);
                    bands[4 + i].Background = new SolidColorBrush(dk[i]);
                }
                bandCard.BorderBrush = Theme.StrokeSoftBrush;
                // 设置里没写预设标识 = 谁都没套过，亮起来的是出厂那一枚
                var cur = _settings.Current.ColorPreset;
                bool on = string.IsNullOrEmpty(cur) ? p.Id == "default" : cur == p.Id;
                Color back = on ? Fluent.AccentOn(Theme.CardBg, 0.18) : Theme.RowBg;
                el.Background = new SolidColorBrush(back);
                el.BorderBrush = new SolidColorBrush(on ? Theme.Accent : Theme.StrokeSoft);
                // 选中那枚的名字压在强调色淡底上，按这块真正的底判色才读得清
                name.Foreground = new SolidColorBrush(on ? Fluent.AccentTextOn(back) : Theme.Text);
            });
            strip.Children.Add(el);
        }
        card.Children.Add(strip);
    }

    /// <summary>套用一整套预设：覆盖表整体换成预设的值，落盘后把全局样式重烤一次。</summary>
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
        card.Children.Add(SectionHeader(Loc.T(K.Common.Preview)));

        var body = new TextBlock { Text = Loc.T(K.Settings.TokenText), FontSize = 13, Foreground = Theme.TextBrush };
        var dim = new TextBlock { Text = Loc.T(K.Settings.TokenTextDim), FontSize = 12, Foreground = Theme.TextDimBrush, Margin = new Thickness(10, 0, 0, 0) };
        var line1 = new StackPanel { Orientation = Orientation.Horizontal };
        line1.Children.Add(body); line1.Children.Add(dim);

        var rowPill = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 8, 0, 0) };
        var rowText = new TextBlock { Text = Loc.T(K.Settings.TokenRowBg), FontSize = 12.5, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center };
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
            var txt = new TextBlock { Text = Loc.T(label), FontSize = 11.5, Foreground = Theme.TextDimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 16, 0) };
            dotFills.Add(dot);
            dots.Children.Add(dot); dots.Children.Add(txt);
        }

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var chipOn = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), Child = new TextBlock { Text = Loc.T(K.Settings.Accent), FontSize = 12 } };
        var chipOff = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(12, 4, 12, 4), Child = new TextBlock { Text = Loc.T(K.Settings.TokenStroke), FontSize = 12 } };
        // 侧栏选中项那一档：强调色淡底压着文字。左侧导航读不清的正是这一档，预览里必须摆出来看
        var navPill = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), Child = new TextBlock { Text = Loc.T(K.Settings.TokenNavBg), FontSize = 12 } };
        chips.Children.Add(chipOn); chips.Children.Add(chipOff); chips.Children.Add(navPill);

        var surface = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 2, 0, 4),
            Child = new StackPanel { Children = { line1, rowPill, dots, chips } },
        };
        card.Children.Add(surface);

        _repaint.Add(() =>
        {
            body.Foreground = Theme.TextBrush;
            dim.Foreground = Theme.TextDimBrush;
            surface.Background = Theme.CardBgBrush;
            surface.BorderBrush = Theme.StrokeBrush;
            rowPill.Background = Theme.RowBgBrush;
            rowPill.BorderBrush = Theme.StrokeSoftBrush;
            rowText.Foreground = Theme.TextBrush;
            track.Background = Theme.AccentBrush;
            track.BorderBrush = Theme.AccentBrush;
            knob.Fill = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent));
            knob.Stroke = Theme.StrokeSoftBrush;
            dotFills[0].Fill = Theme.DangerBrush;
            dotFills[1].Fill = Theme.SuccessBrush;
            dotFills[2].Fill = Theme.WarningBrush;
            chipOn.Background = Theme.AccentBrush;
            ((TextBlock)chipOn.Child!).Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent));
            chipOff.Background = Theme.RowBgBrush;
            chipOff.BorderBrush = Theme.StrokeSoftBrush;
            ((TextBlock)chipOff.Child!).Foreground = Theme.TextBrush;
            // 侧栏选中档：淡底与文字都算在这块底上，跟左侧导航用的是同一组数
            var navTint = Fluent.AccentOn(Theme.NavBg, 0.26);
            navPill.Background = new SolidColorBrush(navTint);
            ((TextBlock)navPill.Child!).Foreground = new SolidColorBrush(Fluent.AccentTextOn(navTint));
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
            Foreground = Theme.TextDimBrush,
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
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        g.Children.Add(name);

        var reset = Fluent.IconButton("\uE72C", Loc.T(K.Settings.PaletteRestore), 26, 13, Theme.TextDimBrush);
        Grid.SetColumn(reset, 5);
        g.Children.Add(reset);

        _repaint.Add(() =>
        {
            name.Foreground = Theme.TextBrush;
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
            Foreground = Theme.TextDimBrush,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 66,
        };

        _repaint.Add(() =>
        {
            var c = Theme.ValueOf(token.Key, dark);
            fill.Background = new SolidColorBrush(c);
            cell.BorderBrush = Theme.StrokeBrush;
            hex.Text = HexColor.Format(c);
            // 正文两枚要压在窗口 / 面板 / 卡片 / 行四种底上：最差那一对低于 4.5:1 就把十六进制标成注意色。
            // 改色是当场生效的，标出来的人才看得见这一改把字读没了。
            double worst = WorstTextRatio(token.Key, c, dark);
            bool low = worst > 0 && worst < 4.5;
            hex.Foreground = new SolidColorBrush(low ? Theme.Warning : Theme.IsCustom(token.Key, dark) ? Theme.Text : Theme.TextDim);
            cell.ToolTip = low ? Loc.T(K.Settings.LowContrastOnSurfaces) : Loc.T(TokenLabel(token.Key));
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

    /// <summary>正文与次要文字要压在窗口 / 面板 / 卡片 / 行四种底上，取最差那一对的对比度。
    /// 其余令牌是底色与叠加层，没有「读不读得清」这一问，返回 0 表示不适用。</summary>
    /// <param name="key">令牌键。</param>
    /// <param name="ink">这一侧当前的字色。</param>
    /// <param name="dark">算深色侧还是浅色侧。</param>
    /// <returns>四种底上最差的一对对比度；不适用的令牌返回 0。</returns>
    static double WorstTextRatio(string key, Color ink, bool dark)
    {
        if (key != "Text" && key != "TextDim") return 0;
        double worst = double.MaxValue;
        foreach (var bg in new[] { "WindowBg", "PanelBg", "CardBg", "RowBg" })
            worst = Math.Min(worst, Fluent.ContrastRatio(ink, Theme.Opaque(Theme.ValueOf(bg, dark))));
        return worst;
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
                    Background = Theme.PanelBgSolidBrush,
                    BorderBrush = Theme.StrokeBrush,
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
        // 界面颜色挂在共享画刷上，这一句就够全窗跟着变；
        // ToolTip/滚动条/菜单的颜色是烤进 XAML 的，逐帧重烤既贵又没必要，收起弹层时统一来一次。
        Theme.SetCustom(_palKey, _palDark, c);
    }

    /// <summary>弹层收起：把这一枚的自定义色写进设置，并重烤烤进 XAML 的那批颜色。没改动过就不折腾。</summary>
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
        Foreground = Theme.TextDimBrush,
        Margin = new Thickness(2, 6, 0, 0),
    };

    #endregion
}
