using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

// 本 partial 职责：外观分组——外观主题行、强调色行（含取色弹层状态字段），以及主题/强调色变更后的原地重建。
/// <summary>
/// 设置窗的外观分组部分：主题三档与强调色都在变更瞬间生效——先 <c>Theme.Apply</c> +
/// <c>Fluent.ApplyGlobalStyles</c> 刷全局，再整窗重建，因为本窗颜色是构造期取值的静态画刷。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>
    /// 主题/强调色变更后原地重建：新窗在原位置淡入，本窗同时淡出后关闭，两者节拍非对称。
    /// 必须在 UI 线程调用；重建期间两个实例短暂共存，分页索引由 <c>_restoreCat</c> 交给新窗。
    /// </summary>
    void RebuildInPlace()
    {
        var oldw = this;
        var nw = new SettingsWindow();
        nw._restoreCat = oldw._cat;   // 改主题/强调色重建后停在原分页，不跳回首页
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
            Background = new SolidColorBrush(Theme.TrackBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
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
            Foreground = new SolidColorBrush(Theme.Text),
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
            Background = new SolidColorBrush(Theme.Accent),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
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
                Color previewed = Theme.Accent;
                _accentView.Changed += c =>
                {
                    // 拖动中只预览（改静态色+全局样式），松板才落盘重建——避免每像素重建窗口
                    previewed = c;
                    Theme.Accent = Color.FromRgb(c.R, c.G, c.B);
                    chip.Background = new SolidColorBrush(Theme.Accent); // 色片实时跟色，所见即所得
                    Fluent.ApplyGlobalStyles();
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
                    Fluent.ApplyGlobalStyles();
                    RebuildInPlace(); // 本窗静态刷色需重建才跟上
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
            Theme.Apply(_settings.Current.Theme, "");
            Fluent.ApplyGlobalStyles();
            RebuildInPlace();
        };
        chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Open(); e.Handled = true; } };
        // 键盘焦点环：与「试听」按钮同款——聚焦时描边转强调色，失焦回描边底色
        chip.GotKeyboardFocus += (_, _) => chip.BorderBrush = new SolidColorBrush(Theme.Accent);
        chip.LostKeyboardFocus += (_, _) => chip.BorderBrush = new SolidColorBrush(Theme.Stroke);
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
        string cur = _settings.Current.Theme;
        foreach (var (id, glyph, tip) in new[]
                 { ("system", "\uE977", Loc.T(K.Settings.FollowSystem)), ("light", "\uE706", Loc.T(K.Settings.Light)), ("dark", "\uE708", Loc.T(K.Settings.Dark)) })
        {
            string captured = id;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(captured == cur ? Theme.Accent : Theme.Hover),
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(captured == cur ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            var localChip = chip;
            var localText = (TextBlock)chip.Child;
            Color chipBase = captured == cur ? Theme.Accent : Theme.Hover;
            Color chipHover = captured == cur
                ? Color.FromArgb(0xFF, 0x6A, 0xD0, 0xFF)
                : (Theme.IsDark ? Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x4A, 0x00, 0x00, 0x00));
            localChip.MouseEnter += (_, _) =>
            {
                if (localChip.Background is SolidColorBrush bg0)
                {
                    var anim = new System.Windows.Media.Animation.ColorAnimation(chipHover, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs)));
                    var holder = new SolidColorBrush(bg0.Color);
                    localChip.Background = holder;
                    holder.BeginAnimation(SolidColorBrush.ColorProperty, anim);
                }
            };
            localChip.MouseLeave += (_, _) =>
            {
                if (localChip.Background is SolidColorBrush bg1)
                {
                    var anim = new System.Windows.Media.Animation.ColorAnimation(chipBase, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
                    var holder = new SolidColorBrush(bg1.Color);
                    localChip.Background = holder;
                    holder.BeginAnimation(SolidColorBrush.ColorProperty, anim);
                }
            };
            void ApplyTheme()
            {
                _settings.Mutate(s => s.Theme = captured);
                Theme.Apply(captured, _settings.Current.Accent);
                Fluent.ApplyGlobalStyles();
                // 无缝过渡：本窗淡出的同时，同位置淡入新窗（Theme.Changed 已让其他窗口即时换色）
                RebuildInPlace();
            }
            chip.MouseLeftButtonDown += (_, e) => { ApplyTheme(); e.Handled = true; };
            // 键盘可达——Tab 聚焦、空格/Enter 切换主题（与点击同一路径）
            chip.Focusable = true;
            chip.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Space or Key.Enter) { ApplyTheme(); e.Handled = true; }
            };
            chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = new SolidColorBrush(Theme.Accent); chip.BorderThickness = new Thickness(1); };
            chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
            chips.Children.Add(chip);
        }
        g.Children.Add(chips);
        return g;
    }
    #endregion

}
