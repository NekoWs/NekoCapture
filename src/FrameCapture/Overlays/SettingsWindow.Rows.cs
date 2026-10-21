using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

// 本 partial 职责：设置行控件工厂——滑杆行、开关行、保存位置行、录制格式行，及通用行骨架/卡片/图标小件。
/// <summary>
/// 设置窗的行控件工厂部分：所有 <c>*Row()</c> 返回未挂到父容器的片段，由 <see cref="SettingsWindow.Build"/> 决定归属分页。
/// 滑杆行与开关行把副作用交给调用方的 <c>onChange</c>；保存位置行与录制格式行在回调里自己 <c>Mutate</c> 落盘。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    #region 滑杆行：名称常显 + 整宽滑杆

    FrameworkElement SliderRow(string glyph, string label, string tip, double min, double max, double tick, double val,
        Func<double, string> fmt, Action<double> onChange)
    {
        // 两行式：首行 [图标][名称][当前值]，次行整宽滑杆（名称常显，不依赖悬停）
        var outer = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(IconText(glyph, tip));
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(lbl, 1);
        head.Children.Add(lbl);
        var hud = new TextBlock
        {
            Text = fmt(val),
            FontSize = 11,
            Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(hud, 2);
        head.Children.Add(hud);
        var sl = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = tick,
            IsSnapToTickEnabled = true,
            Value = val,
            Foreground = new SolidColorBrush(Theme.Accent),
            Margin = new Thickness(4, 4, 0, 0),
            // 吸附开启时步长必须=档位：默认 SmallChange=1 会被吸回原档，方向键看似失灵
            SmallChange = tick,
            LargeChange = tick * 2,
        };
        Fluent.StyleSlider(sl);
        sl.ValueChanged += (_, e) =>
        {
            hud.Text = fmt(e.NewValue);
            onChange(e.NewValue);
        };
        outer.Children.Add(head);
        outer.Children.Add(sl);
        return outer;
    }
    #endregion

    #region 开关行与保存位置、录制格式选择行

    FrameworkElement ToggleRow(string glyph, string label, string tip, bool initial, Action<bool> onChange)
    {
        var g = Row();
        // 整行可点，与手势编辑器开关同一命中范围：Grid 底色为 null 时空白处不参与命中测试，
        // 透明画刷不画任何像素，只把图标与文字之间的空隙也变成可点区。
        g.Background = Brushes.Transparent;
        g.Cursor = Cursors.Hand;
        g.ToolTip = tip;
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 开关列
        g.Children.Add(IconText(glyph, tip));
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var track = new Border
        {
            Width = 40,
            Height = 20,
            Margin = new Thickness(12, 0, 0, 0),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(initial ? Theme.Accent : Theme.TrackBg),
            BorderBrush = new SolidColorBrush(initial ? Theme.Accent : Theme.Stroke),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true, // 键盘可达——Tab 聚焦、空格/Enter 切换
        };
        var knob = new Ellipse
        {
            Width = 12,
            Height = 12,
            // 滑钮开态反白、关态恒白：开关状态主要靠轨道底色表达，滑钮只负责在 accent 实底上仍看得见
            Fill = new SolidColorBrush(initial ? Fluent.OnAccentColor(Theme.Accent) : Colors.White),
            Stroke = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(4, 0, 4, 0),
            RenderTransform = new TranslateTransform(initial ? 20 : 0, 0),
        };
        Grid.SetColumn(track, 2);
        track.Child = knob;
        bool on = initial;
        bool focused = false, hovered = false;
        void Flip()
        {
            on = !on;
            Fluent.SlideToggle(knob, track, on, 20);
            onChange(on);
        }
        // 命中区在整行：点在轨道上时事件冒泡到这里，轨道自身不挂处理器，一次点击只翻转一次
        g.MouseLeftButtonDown += (_, e) => { Flip(); e.Handled = true; };
        track.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter) { Flip(); e.Handled = true; }
        };
        // 描边三态：聚焦或开启取 Accent；悬停把 Stroke 的 alpha 提一档（Stroke=0x55 半透明，加亮才看得出来）。
        // 悬停挂在整行而非轨道：命中区已经扩到整行，反馈范围必须跟着走。
        void SetStroke()
        {
            Color s = Theme.Stroke;
            Color t = focused || on ? Theme.Accent
                : hovered ? Color.FromArgb((byte)Math.Min(255, s.A + 0x60), s.R, s.G, s.B)
                : s;
            var b = new SolidColorBrush(track.BorderBrush is SolidColorBrush cur ? cur.Color : t);
            track.BorderBrush = b;
            b.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(t, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.HoverMs))));
        }
        track.GotKeyboardFocus += (_, _) => { focused = true; SetStroke(); };
        track.LostKeyboardFocus += (_, _) => { focused = false; SetStroke(); };
        g.MouseEnter += (_, _) => { hovered = true; SetStroke(); };
        g.MouseLeave += (_, _) => { hovered = false; SetStroke(); };
        g.Children.Add(track);
        return g;
    }

    /// <summary>自动保存位置行：整行可点，弹系统文件夹选择器；空路径显示默认「图片\FrameCapture」。</summary>
    FrameworkElement PathRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 更改列
        g.Children.Add(IconText("\uE838", Loc.T(K.Settings.SaveFolderAutoSavedCopies)));
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            // 整行可点，命中高度就得按可点的东西算：贴着文字只有 17 高，指尖偏一点就点空。
            MinHeight = 28,
            Cursor = Cursors.Hand, Background = Brushes.Transparent,
            ToolTip = Loc.T(K.Settings.ClickToChooseTheSave),
        };
        // 这一行不摆「保存位置」标签：路径本身就是答案，左边那半句「自动保存副本」已经说清
        // 这一行是什么；多一个标签就多占一截宽度，而这一行的宽度全该给路径。
        var path = new TextBlock
        {
            FontSize = 12, Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            // 不给 MaxWidth：这一列是 *，宽度由所在行给。写死 250 会把整行撑到窗口右沿之外，
            // 结果右侧「更改」被裁掉半截——路径该让位，不该把邻居挤出去。
            // 宁可折行也不要省略号：这一行回答的是「存到哪去了」，
            // 裁成「图片\Fram…」等于把问题原样还给用户。
            TextWrapping = TextWrapping.Wrap,
        };
        void Refresh()
        {
            var p = _settings.Current.AutoSavePath;
            var full = string.IsNullOrWhiteSpace(p) ? Loc.T(K.Settings.PicturesFramecaptureDefaultLocation) : p;
            // 中段省略：这行要回答的是「存到哪去了」，答案在末尾；裁掉末尾等于把问题留着
            path.Text = Fluent.TrimPathMiddle(full);
            path.ToolTip = full;   // 省掉的那段交给悬停；「点击选择保存位置」那条提示挂在整行上，两处各说各的
        }
        Refresh();
        stack.Children.Add(path);
        // 整行可点（弹文件夹选择器）：hover 时路径文字提亮，提示这块也能点，与右侧「更改」链接一致
        stack.MouseEnter += (_, _) => path.Foreground = new SolidColorBrush(Theme.Text);
        stack.MouseLeave += (_, _) => path.Foreground = new SolidColorBrush(Theme.TextDim);
        Grid.SetColumn(stack, 1);
        g.Children.Add(stack);
        // 「更改」包一层 Border：裸 TextBlock 的可点区域只有字那么大（24×15），
        // 而且整行被撑爆时它排在最后，最先被裁。内边距给它自己的呼吸，
        // 最小命中高度与其它可点行一致，悬停/按压走 ChipHover 与其它胶囊同一套手感。
        var browseText = new TextBlock
        {
            Text = Loc.T(K.Settings.Change), FontSize = 12, Foreground = new SolidColorBrush(Fluent.AccentText()),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var browse = new Border
        {
            MinHeight = 28,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Thickness(12, 0, 6, 0),
            Child = browseText,
        };
        Fluent.ChipHover(browse);
        Grid.SetColumn(browse, 2);
        g.Children.Add(browse);
        // 文字链接 hover 才显下划线（默认干净、悬停才提示可点），比常驻下划线更克制
        browse.MouseEnter += (_, _) => browseText.TextDecorations = TextDecorations.Underline;
        browse.MouseLeave += (_, _) => browseText.TextDecorations = null;
        void Open()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = Loc.T(K.Settings.ChooseTheSaveFolderAuto);
            dlg.UseDescriptionForTitle = true;
            var cur = _settings.Current.AutoSavePath;
            if (!string.IsNullOrWhiteSpace(cur) && System.IO.Directory.Exists(cur)) dlg.SelectedPath = cur;
            // 对话框须挂到本窗句柄，否则可能落在设置窗之下
            var owner = new Win32.WndOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            if (dlg.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
            {
                // 路径先取出来再交给 Mutate：回调里握着对话框，等于把设置写入绑到它的生命周期上
                string picked = dlg.SelectedPath;
                _settings.Mutate(s => s.AutoSavePath = picked);
            }
            Refresh();
        }
        stack.MouseLeftButtonDown += (_, e) => { Open(); e.Handled = true; };
        browse.MouseLeftButtonDown += (_, e) => { Open(); e.Handled = true; };
        return g;
    }

    FrameworkElement RecordFormatRow()
    {
        var g = ChipRow("\uE786", Loc.T(K.Settings.TheFormatTheEditorS), Loc.T(K.Settings.RecordingFormat));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        var cur = _settings.Current.RecordFormat;
        bool hasFf = Encoding.FfmpegLocator.Find() != null;
        string ffNote = hasFf ? Loc.T(K.Settings.FfmpegDetected) : Loc.T(K.Settings.FfmpegNotFoundInstallFfmpeg);
        var opts = new (RecordFormat id, string text, string tip)[]
        {
            (RecordFormat.Gif, "GIF", Loc.T(K.Settings.AnAnimatedImageThatPlays)),
            (RecordFormat.Mp4, "MP4", Loc.T(K.Settings.TheMostCompatibleVideoFormat) + ffNote),
            (RecordFormat.WebM, "WebM", Loc.T(K.Settings.AnOpenVideoFormatEncoded) + ffNote),
        };
        var chipList = new List<(Border chip, RecordFormat id)>();
        // 边框填充色只在 MP4 下出现：h264 没有透明通道，透明边得拍平到一个颜色才读得出阴影。
        // 进出场原地拉开空地再放内容，退场滑走收高——页面不跳版。
        var matte = Fluent.Revealable(MatteColorRow());
        bool matteOpen = cur == RecordFormat.Mp4;
        if (matteOpen) Fluent.RevealOpen(matte);   // 开窗就选着 MP4：直接摆在展开态，不演入场
        foreach (var (id, text, tip) in opts)
        {
            RecordFormat captured = id;
            var chip = Chip(text, tip, captured == cur);
            chipList.Add((chip, id));
            void Apply()
            {
                _settings.Mutate(s => s.RecordFormat = captured);
                RefreshChips(chipList, captured);
                bool want = captured == RecordFormat.Mp4;
                if (want == matteOpen) return;   // 状态没变就别再演一遍
                matteOpen = want;
                if (want) Fluent.Reveal(matte);
                else Fluent.Conceal(matte);
            }
            WireChip(chip, Apply);
            chips.Children.Add(chip);
        }
        g.Children.Add(chips);
        // 未检测到 ffmpeg 时在行下方直接写明（芯片 tooltip 要悬停才读到，这行一眼可见）；
        // 用绘制的小圆点而非字形，规避未验证字形；只读、开设置时算一次
        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 3, 0, 0) };
        var dotColor = hasFf ? Color.FromRgb(0x4C, 0xAF, 0x50) : Color.FromRgb(0xD1, 0x8B, 0x00);
        status.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = new SolidColorBrush(dotColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        status.Children.Add(new TextBlock
        {
            Text = hasFf ? Loc.T(K.Settings.FfmpegDetectedMp4AndWebm)
                         : Loc.T(K.Settings.FfmpegNotFoundMp4),
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        var wrap = new StackPanel();
        wrap.Children.Add(g);
        wrap.Children.Add(status);
        wrap.Children.Add(matte);
        return wrap;
    }

    Popup? _mattePop;
    ColorPickView? _matteView;

    /// <summary>边框填充色：窗口截图与录制的透明边（圆角、投影）拍平到这个颜色，只有 MP4 用得上。
    /// 默认白——投影是半透明黑，铺在白底上才读得出阴影，铺在黑底上整圈就消失了。</summary>
    FrameworkElement MatteColorRow()
    {
        var g = ChipRow("\uE790", Loc.T(K.Settings.FillsTheShadowBandFor), Loc.T(K.Settings.BorderFillColor));
        var chip = new Border
        {
            Width = 46,
            Height = 20,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(HexColor.Parse(_settings.Current.VideoMatteColor) ?? Colors.White),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = Loc.T(K.Settings.ClickToPickTheFillColor),
        };
        Grid.SetColumn(chip, 2);
        Fluent.ChipHover(chip);   // 悬停只改底色带过渡，不缩放
        void Open()
        {
            if (_mattePop == null)
            {
                _matteView = new ColorPickView();
                _matteView.Changed += c => chip.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                _mattePop = new Popup
                {
                    Child = Fluent.PopupCard(_matteView),
                    Placement = PlacementMode.Top,
                    AllowsTransparency = true,
                    PopupAnimation = PopupAnimation.Fade,
                };
                // 松板才落盘：拖动中只让色片实时跟色，避免每个像素都写一次设置
                _mattePop.Closed += (_, _) =>
                {
                    var c = _matteView!.Color;
                    _settings.Mutate(s => s.VideoMatteColor = HexColor.Format(c));
                };
            }
            _matteView!.Color = chip.Background is SolidColorBrush sb ? sb.Color : Colors.White;
            _mattePop.PlacementTarget = chip;
            Fluent.OpenPopSticky(_mattePop, this);
        }
        chip.MouseLeftButtonDown += (_, e) => { Open(); e.Handled = true; };
        chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Open(); e.Handled = true; } };
        g.Children.Add(chip);
        return g;
    }
    #endregion

    #region 行骨架与卡片小件

    static Grid Row()
    {
        // 整行可点，所以命中区就是这一行的高度：只按内容排出来是 20 逻辑像素，比 24 的可用下限还小，
        // 上下 6 像素的间距又落在命中区外。给个 32 的下限，间距收到 2，行距基本不变而点得着。
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2), MinHeight = 32 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    /// <summary>把一页里平铺的设置行整体归入一张浮起卡片（沿用快捷键页 keyCard 模式），
    /// 让截图/录制/产物…各页与快捷键页用同一套卡片语言，而不是裸行铺在窗体背景上。
    /// 快捷键页自带卡片结构，不调本方法；空页直接返回，不生成空卡片。</summary>
    void Cardify(StackPanel p)
    {
        if (p.Children.Count == 0) return;
        var inner = new StackPanel();
        var kids = new List<UIElement>();
        foreach (UIElement k in p.Children) kids.Add(k);
        p.Children.Clear();
        foreach (var k in kids) inner.Children.Add(k);
        var card = new Border
        {
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(8),   // 卡片 8、行 6：两级圆角差一档，嵌套时外圆包内圆才对得齐
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            Background = new SolidColorBrush(Theme.CardBg),
            Padding = new Thickness(12, 6, 12, 6),
            Child = inner,
        };
        Fluent.Elevate(card, blur: 24, depth: 6);
        p.Children.Add(card);
    }

    static TextBlock IconText(string glyph, string tip) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
        FontSize = 16,
        Foreground = new SolidColorBrush(Theme.Text),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 14, 0),
        ToolTip = tip,
    };

    /// <summary>芯片行的行首：图标 + 标签，并补上第三列（芯片列）的 Auto 宽度声明。
    /// 芯片列用 Auto 而非 Star，芯片才能整体靠右贴齐、宽度随内容伸缩。</summary>
    /// <param name="glyph">Segoe Fluent Icons 的 PUA 字形。</param>
    /// <param name="iconTip">图标 tooltip，写明这一行存在的理由。</param>
    /// <param name="label">行标签文字。</param>
    /// <returns>已放好图标（第 0 列）与标签（第 1 列）的行容器；芯片 StackPanel 由调用方放进第 2 列。</returns>
    static Grid ChipRow(string glyph, string iconTip, string label)
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 芯片列
        g.Children.Add(IconText(glyph, iconTip));
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        return g;
    }

    /// <summary>一枚可选芯片：选中态用强调色底 + 反白文字，未选中用悬停底色 + 正文色。</summary>
    /// <param name="text">芯片显示文字。</param>
    /// <param name="tip">芯片 tooltip。</param>
    /// <param name="selected">当前是否选中，决定底色与文字色。</param>
    /// <returns>尚未接事件的芯片容器，交给 <see cref="WireChip"/> 激活。</returns>
    static Border Chip(string text, string tip, bool selected) => new()
    {
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 5, 10, 5),
        Margin = new Thickness(2, 0, 2, 0),
        Background = new SolidColorBrush(selected ? Theme.Accent : Theme.Hover),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(selected ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text),
        },
        Cursor = Cursors.Hand,
        ToolTip = tip,
    };

    /// <summary>给芯片接上点击与键盘激活、焦点框、悬停反馈。悬停只改底色带过渡、不缩放——缩放会盖住相邻芯片。</summary>
    /// <param name="chip">目标芯片。</param>
    /// <param name="apply">用户点选或按空格/回车时要执行的动作。</param>
    static void WireChip(Border chip, Action apply)
    {
        Fluent.ChipHover(chip);
        chip.MouseLeftButtonDown += (_, e) => { apply(); e.Handled = true; };
        chip.Focusable = true;
        chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { apply(); e.Handled = true; } };
        chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = new SolidColorBrush(Theme.Accent); chip.BorderThickness = new Thickness(1); };
        chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
    }

    /// <summary>按新的选中项刷新整行芯片的选中态配色。</summary>
    /// <param name="chips">本行全部芯片及其标识值。</param>
    /// <param name="selected">新的选中标识值。</param>
    /// <returns>是否存在命中项——调用方据此决定「自定义」输入框要不要高亮。</returns>
    static bool RefreshChips<T>(IReadOnlyList<(Border chip, T id)> chips, T selected)
    {
        bool anySel = false;
        foreach (var (c, cid) in chips)
        {
            bool sel = Equals(cid, selected);
            anySel |= sel;
            c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
            if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text);
        }
        return anySel;
    }
    #endregion

}
