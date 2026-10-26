using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

// 本 partial 职责：外观分组——主题三档行、界面语言行（含取色弹层状态字段），以及换语言后的原地重建。
/// <summary>
/// 设置窗的外观分组部分：主题三档改的是全局调色板，本窗颜色挂在 <see cref="Theme"/> 的共享画刷上，当场跟上；
/// 界面语言改的是文案，文案在构造期取值，只有它需要 <c>RebuildInPlace</c> 换窗。
/// 两者都再跟一次 <c>Fluent.ApplyGlobalStyles</c>——ToolTip / 滚动条 / 菜单的颜色烤在生成的 XAML 里。
/// 强调色行与逐枚配色的编辑在 <c>SettingsWindow.Palette.cs</c>，共用这里的 <c>RebuildInPlace</c> 与取色弹层字段。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>
    /// 界面语言变更后原地重建：新窗在原位置淡入，本窗同时淡出后关闭，两者节拍非对称。
    /// 只有文案跟着换的语言才需要重建——颜色挂在 <see cref="Theme"/> 的共享画刷上，换色不必换窗。
    /// 必须在 UI 线程调用；重建期间两个实例短暂共存，分页索引由 <c>_restoreCat</c> 交给新窗。
    /// </summary>
    void RebuildInPlace()
    {
        var oldw = this;
        var nw = new SettingsWindow();
        nw._restoreCat = oldw._cat;   // 重建后停在原分页，不跳回首页
        nw.Build();
        // 构造里是 CenterScreen，Show 时会覆盖下面手设的 Left/Top，窗口会弹回屏幕中心。
        // 重建必须走 Manual，才尊重原位置。
        nw.WindowStartupLocation = WindowStartupLocation.Manual;
        nw.Left = oldw.Left; nw.Top = oldw.Top;
        nw.Opacity = 0;
        nw.Show();
        var fo = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ExitMs + 20));
        fo.Completed += (_, _) => oldw.Close();
        oldw.BeginAnimation(OpacityProperty, fo);
        nw.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ThemeMs)));
    }

    /// <summary>上一次看到的深浅档：只有深浅互换才需要幕布，逐枚改色不必挡视线。</summary>
    bool _maskDark = Theme.IsDark;

    /// <summary>换色那一刻画面上的窗口底：幕布拿它当退场色，落新调色板之后就取不到了。</summary>
    Color _maskFrom = Theme.WindowBgSolid;

    /// <summary>调色板即将重算：这一次要翻深浅档，就趁画面还是旧调色板记下退场色。</summary>
    /// <param name="dark">重算之后的深浅档。</param>
    void OnPaletteChanging(bool dark)
    {
        if (dark != _maskDark) _maskFrom = Theme.WindowBgSolid;
    }

    /// <summary>调色板变更：深浅档换了就整片盖一层退场色再淡开，铺完自己摘；逐枚改色直接落色。</summary>
    void OnPaletteChanged()
    {
        bool dark = Theme.IsDark;
        if (dark != _maskDark) Fluent.MaskFade(this, _maskFrom);
        _maskDark = dark;
    }

    /// <summary>
    /// 界面语言行：三档文字选项，每档用各自语言写自己的名字，只有「系统默认」跟着当前语言走。
    /// 三档装在一条轨道里：选中那块是落在轨道内侧的强调色胶囊，三段的上下沿由同一个数说话，
    /// 不靠半透明底色分选中——那种底在暗色主题下几乎看不见，看着就像其中一颗比别的小一圈。
    /// 选中即落盘、即取词、原地重建本窗——本窗文案是构造期取值的，重建才看得到新语言；
    /// 托盘菜单每次右键重建，编辑器与帮助窗下次打开自然跟上。
    /// </summary>
    /// <returns>可直接加进分页的整行。</returns>
    FrameworkElement LanguageRow()
    {
        var g = ChipRow("\uF2B7", Loc.T(K.Settings.Language), Loc.T(K.Settings.Language));
        string cur = _settings.Current.UiLanguage;
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        var track = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(7),
            Background = Theme.TrackBgBrush,
            BorderBrush = Theme.StrokeBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = strip,
        };
        Grid.SetColumn(track, 2);
        bool first = true;
        foreach (var (id, label) in new[]
                 { (Loc.PrefSystem, Loc.T(K.Settings.System2)), (Loc.PrefZh, "中文"), (Loc.PrefEn, "English") }) // copyok 语言名用本语言自称，两种界面下都显示「中文 / English」
        {
            string captured = id;
            bool sel = captured == cur;
            var seg = new Border
            {
                Height = 24,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(first ? 0 : 2, 0, 0, 0),
                Background = new SolidColorBrush(sel ? Theme.Accent : Colors.Transparent),
                Cursor = Cursors.Hand,
                Focusable = true,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(sel ? Fluent.OnAccentColor(Theme.Accent) : Theme.TextDim),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
            first = false;
            Fluent.ChipHover(seg);   // 悬停只改底色带过渡，不缩放
            // 三段是构造期取色建的私有画刷：不登记重画，深浅档一翻，轨道跟着换了而字还停在旧档的灰度上
            _repaint.Add(() =>
            {
                ((TextBlock)seg.Child).Foreground = new SolidColorBrush(sel ? Fluent.OnAccentColor(Theme.Accent) : Theme.TextDim);
                seg.Background = sel ? Theme.AccentBrush : Brushes.Transparent;   // 悬停可能换成私有副本，重新指回去才继续跟色
            });
            void Pick()
            {
                if (_settings.Current.UiLanguage == captured) return;
                _settings.Mutate(s => s.UiLanguage = captured);
                Loc.Apply(captured);
                RebuildInPlace();
            }
            seg.MouseLeftButtonDown += (_, e) => { Pick(); e.Handled = true; };
            // 键盘可达——Tab 聚焦、空格/Enter 切换（与点击同一路径）
            seg.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Pick(); e.Handled = true; } };
            strip.Children.Add(seg);
        }
        g.Children.Add(track);
        return g;
    }

    #region 强调色：色片入口与取色弹层

    Popup? _accentPop;
    ColorPickView? _accentView;

    FrameworkElement AccentRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 色片列
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
        var chip = new Border
        {
            Width = 34,
            // 28 高：这块点开的是取色窗，20 高的命中面指尖偏一点就点空。
            // 色片本来就是一枚按钮，长高一点反而更像它能按。
            Height = 28,
            CornerRadius = new CornerRadius(5),
            Background = Theme.AccentBrush,
            BorderBrush = Theme.StrokeBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = Loc.T(K.Settings.ClickToPickAnAccent),
        };
        Grid.SetColumn(chip, 2);
        Fluent.ChipHover(chip);   // 悬停只改底色带过渡，不缩放
        void Open()
        {
            if (_accentPop == null)
            {
                _accentView = new ColorPickView();
                _accentView.Changed += c =>
                {
                    // 拖动中只改全局调色板：界面颜色挂在共享画刷上，当场就跟着走，不落盘也不重烤 XAML
                    Theme.PreviewAccent(Color.FromRgb(c.R, c.G, c.B));
                    chip.Background = Theme.AccentBrush; // 悬停可能已把色片换成私有副本，重新指回共享刷才继续跟色
                };
                _accentPop = new Popup
                {
                    Child = Fluent.PopupCard(_accentView),
                    Placement = PlacementMode.Top,
                    AllowsTransparency = true,
                    PopupAnimation = PopupAnimation.Fade,
                };
                _accentPop.Closed += (_, _) =>
                {
                    var c = _accentView!.Color;
                    var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                    _settings.Mutate(s => s.Accent = hex);
                    Theme.Apply(_settings.Current.Theme, hex);
                    Fluent.ApplyGlobalStyles();   // ToolTip/滚动条/菜单的颜色烤在 XAML 里，收起弹层统一重烤一次
                };
            }
            _accentView!.Color = Theme.Accent;
            _accentPop.PlacementTarget = chip;
            Fluent.OpenPopSticky(_accentPop, this);
        }
        chip.MouseLeftButtonDown += (_, e) => { Open(); e.Handled = true; };
        chip.MouseRightButtonUp += (_, _) =>
        {
            _settings.Mutate(s => s.Accent = "");
            Theme.ResetAccent();
            Fluent.ApplyGlobalStyles();
        };
        chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Open(); e.Handled = true; } };
        // 键盘焦点环：与「试听」按钮同款——聚焦时描边转强调色，失焦回描边底色
        chip.GotKeyboardFocus += (_, _) => chip.BorderBrush = Theme.AccentBrush;
        chip.LostKeyboardFocus += (_, _) => chip.BorderBrush = Theme.StrokeBrush;
        g.Children.Add(chip);
        return g;
    }
    #endregion

    #region 主题

    FrameworkElement ThemeRow()
    {
        var g = ChipRow("\uE793", Loc.T(K.Settings.Theme), Loc.T(K.Settings.AppearanceTheme));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        var cells = new List<(Border el, TextBlock icon, string id)>();
        foreach (var (id, glyph, tip) in new[]
                 { ("system", "\uE977", Loc.T(K.Settings.FollowSystem)), ("light", "\uE706", Loc.T(K.Settings.Light)), ("dark", "\uE708", Loc.T(K.Settings.Dark)) })
        {
            string captured = id;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(2, 0, 2, 0),
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                    FontSize = 14,
                    // 芯片被行高拉伸（行有 32 的下限），图标不居中就会贴着上沿
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            var icon = (TextBlock)chip.Child;
            cells.Add((chip, icon, captured));
            // 芯片坐在卡片上，底色是实心色：未选中是行底，选中是强调色。
            // 拿半透明叠加层当底色，透出来的是什么取决于卡片背后压着什么，明暗两套读出来是两个颜色；
            // 悬停也只是同一块底再深一档，不再往上盖第二层半透明。
            Color Base() => captured == _settings.Current.Theme ? Theme.Accent : Theme.RowBg;
            void AnimateTo(Color to, int ms)
            {
                var holder = new SolidColorBrush(chip.Background is SolidColorBrush b ? b.Color : Base());
                chip.Background = holder;
                holder.BeginAnimation(SolidColorBrush.ColorProperty,
                    new System.Windows.Media.Animation.ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms))));
            }
            chip.MouseEnter += (_, _) => AnimateTo(Fluent.HoverOn(Base()), Fluent.Motion.QuickMs);
            chip.MouseLeave += (_, _) => AnimateTo(Base(), Fluent.Motion.StateMs);
            void ApplyTheme()
            {
                _settings.Mutate(s => s.Theme = captured);
                Theme.Apply(captured, _settings.Current.Accent);
                Fluent.ApplyGlobalStyles();
                Paint();    // 选中态跟着点中的那一档走；深浅互换由换色幕布整片接住
            }
            chip.MouseLeftButtonDown += (_, e) => { ApplyTheme(); e.Handled = true; };
            // 键盘可达——Tab 聚焦、空格/Enter 切换主题（与点击同一路径）
            chip.Focusable = true;
            chip.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Space or Key.Enter) { ApplyTheme(); e.Handled = true; }
            };
            chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = Theme.AccentBrush; chip.BorderThickness = new Thickness(1); };
            chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
            chips.Children.Add(chip);
        }

        // 选中态是「哪一档被点中」的判断，画刷管不到：调色板一动就按当前主题与强调色重排一遍。
        void Paint()
        {
            string cur = _settings.Current.Theme;
            foreach (var (el, ic, id) in cells)
            {
                bool on = id == cur;
                el.Background = on ? Theme.AccentBrush : Theme.RowBgBrush;
                ic.Foreground = on ? new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)) : Theme.TextBrush;
            }
        }
        _repaint.Add(Paint);
        Paint();
        g.Children.Add(chips);
        return g;
    }
    #endregion

}
