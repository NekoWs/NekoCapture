using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>配色预设、实时取色与共享配色标签。</summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>本窗订阅 <see cref="Theme.Changed"/> 的处理器：调色板一动，<see cref="_repaint"/> 里的小段各自重画。
    /// 关窗时摘掉，静态事件不拖住已关的窗。</summary>
    Action? _repaintHandler;

    /// <summary>当前颜色编辑会话的取色弹层。</summary>
    Popup? _palPop;
    ColorPickView? _palView;

    /// <summary>弹层当前在改哪一枚令牌；null = 弹层没在改任何东西。</summary>
    string? _palKey;

    /// <summary>弹层当前在改这一枚的哪一侧主题。</summary>
    bool _palDark;
    private Color _palOriginal;
    private bool _palHadCustom;

    /// <summary>本窗所有「按当前调色板重画自己」的小段：配色页的色块、十六进制、预览、预设色带，
    /// 以及外观页主题芯片的选中态。共享画刷管颜色，这里管「哪一项算选中」这类构造期算出来的判断。</summary>
    readonly List<Action> _repaint = new();

    /// <summary>配色预设、实时预览、强调色与自定义颜色入口。</summary>
    /// <param name="panel">这一页的容器，由 <c>Build</c> 创建并交给分页表。</param>
    void BuildPalette(StackPanel panel)
    {

        var presets = new StackPanel();
        PresetCard(presets);
        Cardify(presets);
        panel.Children.Add(presets);

        var preview = new StackPanel();
        PreviewCard(preview);
        Cardify(preview);
        panel.Children.Add(preview);

        var accent = new StackPanel();
        AccentCard(accent);
        Cardify(accent);
        panel.Children.Add(accent);

        panel.Children.Add(SettingsPageEntry("colors"));
        foreach (var paint in _repaint.ToArray()) paint();
    }

    #region 预设与恢复

    /// <summary>预设卡：每套预设一枚芯片，芯片上是那套配色上下两行色带（上浅色侧、下深色侧），
    /// 色带下面一枚强调色圆点加名字，点了整套套用——颜色与强调色一起换，才是一整套。</summary>
    /// <param name="card">预设那一节的容器。</param>
    void PresetCard(StackPanel card)
    {
        card.Children.Add(SectionHeader(Loc.T(K.Settings.PalettePresets)));
        // 六套预设横着排不下：换行面板让它们按卡片宽度自己折行，窄窗也不会把最后一套挤到卡片外
        var strip = new WrapPanel { Margin = new Thickness(0, 3, 0, 4) };
        foreach (var p in ColorPresets.All)
        {
            // 色带两行：上一行是这套配色的浅色侧，下一行是深色侧。
            // 只摆当前那一侧的话，浅色主题下几套预设看上去几乎一模一样——差别全在另一侧。
            var bands = new List<Border>();
            var bandHost = new StackPanel { Orientation = Orientation.Vertical };
            for (int side = 0; side < 2; side++)
            {
                // 四段均分整条色带的宽度：芯片宽度统一，色带才是一条整齐的带子，而不是左边一块彩色、右边一段留白
                var row = new UniformGrid { Rows = 1, Columns = 4 };
                for (int i = 0; i < 4; i++)
                {
                    var b = new Border { Height = 10, Margin = new Thickness(0, side == 0 ? 0 : 1, i < 3 ? 1 : 0, 0) };
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
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            var name = new TextBlock
            {
                Text = Loc.T(PresetLabel(p.Id)),
                FontSize = 11.5,
                Foreground = Theme.TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            nameRow.Children.Add(dot);
            nameRow.Children.Add(name);
            var el = new Border
            {
                Width = 68,   // 六套要在一行里排开：整行可用宽度按这个数除以 6 定
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(6, 5, 6, 5),
                Margin = new Thickness(0, 0, 5, 0),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Focusable = true,
                ToolTip = Loc.T(PresetLabel(p.Id)),
                Child = new StackPanel { Children = { bandCard, nameRow } },
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
                dot.Fill = new SolidColorBrush(p.AccentColor);
                dot.Stroke = Theme.StrokeSoftBrush;
                dot.StrokeThickness = 1;
            });
            strip.Children.Add(el);
        }
        card.Children.Add(strip);
    }

    /// <summary>套用一整套预设：覆盖表与强调色一起换成这套的值，落盘后把全局样式重烤一次。
    /// 预设自带强调色，套用就该整屏一个整体，而不是留下上一套的强调色配新表面。</summary>
    /// <param name="p">要套的预设；出厂预设的覆盖表是空的、强调色也回到出厂。</param>
    void ApplyPreset(ColorPreset p)
    {
        _settings.Mutate(s =>
        {
            s.ColorPreset = p.Id;
            s.Colors = new Dictionary<string, string>(p.Colors, StringComparer.Ordinal);
            s.Accent = p.Accent;
        });
        _settings.Current.ApplyPalette();
        Fluent.ApplyGlobalStyles();
    }

    /// <summary>恢复出厂配色：清掉全部覆盖、预设标识与自定义强调色。
    /// 整宽一块，圆角、描边与投影都跟卡片走——它挨着卡片摆，样式差一档就看着像另一个家族的东西。</summary>
    /// <returns>整宽的一枚按钮。</returns>
    FrameworkElement RestoreRow()
    {
        var label = new TextBlock
        {
            Text = Loc.T(K.Settings.PaletteRestore),
            FontSize = 12.5,
            Foreground = Theme.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var btn = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.StrokeBrush,
            Background = Theme.RowBgBrush,
            Padding = new Thickness(14, 9, 14, 9),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = Loc.T(K.Settings.PaletteRestore),
            // 探针认路用：这一块要与卡片同一族，样式靠用例盯着
            Tag = "settings-restore",
            Child = label,
        };
        StyleSettingsCard(btn, outerSpacing: false);
        HoverFill(btn, () => Theme.RowBg, () => Theme.RowHover);
        btn.GotKeyboardFocus += (_, _) => btn.BorderBrush = Theme.AccentBrush;
        btn.LostKeyboardFocus += (_, _) => btn.BorderBrush = Theme.StrokeBrush;
        void Restore()
        {
            ClosePalettePickers();
            _settings.Mutate(s =>
            {
                s.Colors.Clear();
                s.ColorPreset = "";
                s.Accent = "";
            });
            Theme.ClearAllCustoms();
            Theme.ResetAccent();
            Fluent.ApplyGlobalStyles();
        }
        btn.MouseLeftButtonUp += (_, e) => { Restore(); e.Handled = true; };
        btn.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Restore(); e.Handled = true; } };
        _repaint.Add(() =>
        {
            label.Foreground = Theme.TextBrush;
            btn.BorderBrush = Theme.StrokeBrush;
            btn.Background = Theme.RowBgBrush;
        });
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
        var chipOff = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), Child = new TextBlock { Text = Loc.T(K.Settings.TokenStroke), FontSize = 12 } };
        // 侧栏选中项那一档：强调色淡底压着文字。左侧导航读不清的正是这一档，预览里必须摆出来看
        var navPill = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 4, 12, 4), Child = new TextBlock { Text = Loc.T(K.Settings.TokenNavBg), FontSize = 12 } };
        chips.Children.Add(chipOn); chips.Children.Add(chipOff); chips.Children.Add(navPill);

        // 逐枚改色是当场生效的，改完这一行替人把四种底都量一遍：读不清的地方有几处
        var ink = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };

        var surface = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 2, 0, 4),
            Child = new StackPanel { Children = { line1, rowPill, dots, chips, ink } },
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
            chipOff.Background = Theme.ChipBgBrush;
            chipOff.BorderBrush = Theme.StrokeFieldBrush;
            ((TextBlock)chipOff.Child!).Foreground = Theme.TextBrush;
            // 侧栏选中档：淡底与文字都算在这块底上，跟左侧导航用的是同一组数
            var navTint = Fluent.NavBacking(Theme.NavBg, selected: true, hovered: false);
            navPill.Background = new SolidColorBrush(navTint);
            ((TextBlock)navPill.Child!).Foreground = new SolidColorBrush(Fluent.NavInk(navTint, true, false));
            int low = InkShortfalls();
            ink.Text = low == 0 ? Loc.T(K.Settings.InkClear) : Loc.F(K.Settings.InkLowOnSurfaces, low);
            ink.Foreground = low == 0 ? Theme.TextDimBrush : new SolidColorBrush(Theme.Warning);
        });
    }

    /// <summary>正文两枚压在窗口 / 面板 / 卡片 / 行四种底色上，数一对读不清的有几对。
    /// 取色器逐帧生效，这一行跟着当场改口；出厂配色下这一数是 0。</summary>
    /// <returns>Text 与 TextDim 在四种底上对比不足 4.5:1 的对数。</returns>
    static int InkShortfalls()
    {
        int n = 0;
        foreach (var key in new[] { "Text", "TextDim" })
        {
            Color ink = Theme.ValueOf(key, Theme.IsDark);
            foreach (var bg in new[] { "WindowBg", "PanelBg", "CardBg", "RowBg" })
                if (Fluent.ContrastRatio(ink, Theme.Opaque(Theme.ValueOf(bg, Theme.IsDark))) < 4.5) n++;
        }
        return n;
    }

    #endregion

    #region 取色

    /// <summary>保存前一编辑会话并在目标元素下方打开取色器。</summary>
    /// <param name="target">色块本身，弹层贴着它下沿展开。</param>
    /// <param name="key">要改的令牌键名。</param>
    /// <param name="dark">改深色还是浅色。</param>
    void OpenSwatchPop(FrameworkElement target, string key, bool dark)
    {
        ClosePalettePickers();
        var view = new ColorPickView();
        var popup = new Popup
        {
            Child = Fluent.PopupCard(view), Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            PlacementTarget = target,
        };
        _palPop = popup; _palView = view;
        _palKey = key; _palDark = dark;
        _palOriginal = Theme.ValueOf(key, dark);
        _palHadCustom = Theme.IsCustom(key, dark);
        view.Color = _palOriginal;
        view.Changed += PreviewSwatch;
        popup.Closed += (_, _) =>
        {
            view.Changed -= PreviewSwatch;
            if (ReferenceEquals(_palPop, popup)) CommitSwatch();
        };
        popup.IsOpen = true;
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
        if (_palView.Color == _palOriginal)
        {
            if (!_palHadCustom) Theme.ClearCustom(key, _palDark);
            return;
        }
        var mk = (_palDark ? "dark." : "light.") + key;
        var hex = HexColor.Format(_palView.Color);
        if (_settings.Current.Colors.TryGetValue(mk, out var old) && old == hex) return;
        _settings.Mutate(s => { s.Colors[mk] = hex; s.ColorPreset = "custom"; });
        Theme.LoadCustoms(_settings.Current.Colors);
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
        "KeycapBg" => K.Settings.TokenKeycap,
        "ChipBg" => K.Settings.TokenChip,
        "Text" => K.Settings.TokenText,
        "TextDim" => K.Settings.TokenTextDim,
        "Stroke" => K.Settings.TokenStroke,
        "StrokeSoft" => K.Settings.TokenStrokeSoft,
        "StrokeField" => K.Settings.TokenStrokeField,
        "Hover" => K.Settings.TokenHover,
        "Press" => K.Settings.TokenPress,
        "RowHover" => K.Settings.TokenRowHover,
        "TrackBg" => K.Settings.TokenTrack,
        "Danger" => K.Settings.TokenDanger,
        "Success" => K.Settings.TokenSuccess,
        "Warning" => K.Settings.TokenWarning,
        _ => new Str("token." + key, key, key),
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
        "forest" => K.Settings.PresetForest,
        _ => new Str("preset." + id, id, id),
    };

    /// <summary>节标题：卡片里一组同类设置行上方那行小字。各页共用这一个，字号与上下留白只有一处定义——同一页上两张卡各写各的字号，看上去就是两张不同的卡。</summary>
    /// <param name="text">已取好词的文字。</param>
    /// <returns>不可聚焦的小标题。</returns>
    static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = Theme.TextDimBrush,
        Margin = new Thickness(0, 4, 0, 6),
    };

    #endregion
}
