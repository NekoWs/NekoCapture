using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Services;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace FrameCapture.Overlays;

// 本 partial 职责：设置行控件工厂——滑杆行、开关行、保存位置行、录制格式行，及通用行骨架/卡片/图标小件。
public sealed partial class SettingsWindow : Window
{
    #region 滑杆行

    FrameworkElement SliderRow(string glyph, string label, string tip, double min, double max, double tick, double val,
        Func<double, string> fmt, Action<double> onChange)
    {
        // 两行式：首行 [图标][名称][当前值]，次行整宽滑杆（名称常显，不依赖悬停）
        var outer = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Icon(glyph, tip));
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
        FrameCapture.Core.Fluent.StyleSlider(sl);
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

    #region 开关行

    FrameworkElement ToggleRow(string glyph, string label, string tip, bool initial, Action<bool> onChange)
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 开关列
        g.Children.Add(Icon(glyph, tip));
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
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Focusable = true, // 键盘可达——Tab 聚焦、空格/Enter 切换
        };
        var knob = new Ellipse
        {
            Width = 12,
            Height = 12,
            // 滑块恒白 + 极淡描边（QQ 式）——旧版亮色主题下黑滑块/黑字压 accent，适配感缺失
            Fill = new SolidColorBrush(Colors.White),
            Stroke = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(4, 0, 4, 0),
            RenderTransform = new System.Windows.Media.TranslateTransform(initial ? 20 : 0, 0),
        };
        Grid.SetColumn(track, 2);
        track.Child = knob;
        bool on = initial;
        void Flip()
        {
            on = !on;
            FrameCapture.Core.Fluent.SlideToggle(knob, track, on, 20);
            onChange(on);
        }
        track.MouseLeftButtonDown += (_, e) => { Flip(); e.Handled = true; };
        track.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter) { Flip(); e.Handled = true; }
        };
        track.GotKeyboardFocus += (_, _) => { track.BorderBrush = new SolidColorBrush(Theme.Accent); };
        track.LostKeyboardFocus += (_, _) => { track.BorderBrush = new SolidColorBrush(on ? Theme.Accent : Theme.Stroke); };
        track.MouseEnter += (_, _) =>
        {
            if (track.BorderBrush is System.Windows.Media.SolidColorBrush bb && bb.CanFreeze)
            {
                var h = new System.Windows.Media.SolidColorBrush(bb.Color);
                track.BorderBrush = h;
                h.Color = System.Windows.Media.Color.FromArgb((byte)Math.Min(255, bb.Color.A + 0x60), bb.Color.R, bb.Color.G, bb.Color.B);
            }
        };
        track.MouseLeave += (_, _) =>
        {
            if (track.BorderBrush is System.Windows.Media.SolidColorBrush bb && bb.CanFreeze)
            {
                var h = new System.Windows.Media.SolidColorBrush(bb.Color);
                track.BorderBrush = h;
                h.Color = on ? Theme.Accent : Theme.Stroke;
            }
        };
        g.Children.Add(track);
        return g;
    }

    /// <summary>自动保存位置行：整行可点，弹系统文件夹选择器；空路径显示默认「图片\FrameCapture」。</summary>
    FrameworkElement PathRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 更改列
        g.Children.Add(Icon("\uE838", "保存文件夹：自动保存副本存到这里，编辑器/贴图「另存为」对话框也默认打开这里"));
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand, Background = Brushes.Transparent,
            ToolTip = "点击选择保存文件夹（自动保存副本 + 导出对话框默认目录）",
        };
        var lbl = new TextBlock { Text = "保存位置", FontSize = 13, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center };
        var path = new TextBlock
        {
            FontSize = 12, Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 250,
        };
        void Refresh()
        {
            var p = _settings.Current.AutoSavePath;
            path.Text = string.IsNullOrWhiteSpace(p) ? @"图片\FrameCapture（默认）" : p;
        }
        Refresh();
        stack.Children.Add(lbl);
        stack.Children.Add(path);
        // 整行可点（弹文件夹选择器）：hover 时路径文字提亮，提示这块也能点，与右侧「更改」链接一致
        stack.MouseEnter += (_, _) => path.Foreground = new SolidColorBrush(Theme.Text);
        stack.MouseLeave += (_, _) => path.Foreground = new SolidColorBrush(Theme.TextDim);
        Grid.SetColumn(stack, 1);
        g.Children.Add(stack);
        var browse = new TextBlock
        {
            Text = "更改", FontSize = 12, Foreground = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Margin = new Thickness(12, 0, 6, 0),
        };
        Grid.SetColumn(browse, 2);
        g.Children.Add(browse);
        // 文字链接 hover 才显下划线（默认干净、悬停才提示可点），比常驻下划线更克制
        browse.MouseEnter += (_, _) => browse.TextDecorations = System.Windows.TextDecorations.Underline;
        browse.MouseLeave += (_, _) => browse.TextDecorations = null;
        void Open()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            { Description = "选择保存文件夹（自动保存副本 + 导出对话框默认目录）", UseDescriptionForTitle = true };
            var cur = _settings.Current.AutoSavePath;
            if (!string.IsNullOrWhiteSpace(cur) && System.IO.Directory.Exists(cur)) dlg.SelectedPath = cur;
            // 设置窗 Topmost：对话框须挂到本窗句柄，否则可能盖在设置窗之下
            var owner = new Win32.WndOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            if (dlg.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
                _settings.Mutate(s => s.AutoSavePath = dlg.SelectedPath);
            Refresh();
        }
        stack.MouseLeftButtonDown += (_, e) => { Open(); e.Handled = true; };
        browse.MouseLeftButtonDown += (_, e) => { Open(); e.Handled = true; };
        return g;
    }

    // 全局「截图后」四选一已移除——改由每条手势在「手势配置器」里各自设置。
    // 连「见手势配置器」的占位行也删了——截图页顶部就有手势配置器入口，不必再留指路行。

    // 取色复制格式：选区放大镜按 C 复制颜色时的文本形式，HEX/RGB/HSL 三选（QQ 只有 HEX）
    FrameworkElement RecordFormatRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 芯片列
        g.Children.Add(Icon("\uE786", "录制完成后编辑器「导出录制」默认用的格式。MP4/WebM 需本机装有 ffmpeg（自动探测 PATH 与常见安装目录）；未装则视频项会提示安装，GIF 始终可用"));
        var lbl = new TextBlock
        {
            Text = "录制格式",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        var cur = _settings.Current.RecordFormat;
        bool hasFf = FrameCapture.Encoding.FfmpegLocator.Find() != null;
        string ffNote = hasFf ? "（已检测到 ffmpeg）" : "（未检测到 ffmpeg，装 FFmpeg 后可选）";
        var opts = new (RecordFormat id, string text, string tip)[]
        {
            (RecordFormat.Gif, "GIF", "通用动图，任何地方都能放；受格式封顶约 50fps、体积大"),
            (RecordFormat.Mp4, "MP4", "兼容性最好的视频格式（libx264），微信/浏览器/剪辑软件通吃 " + ffNote),
            (RecordFormat.WebM, "WebM", "开源视频格式（VP9），同画质体积更小，网页/上传首选 " + ffNote),
        };
        var chipList = new System.Collections.Generic.List<(Border chip, RecordFormat id)>();
        foreach (var (id, text, tip) in opts)
        {
            RecordFormat captured = id;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(captured == cur ? Theme.Accent : Theme.Hover),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(captured == cur ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            chipList.Add((chip, id));
            Fluent.ChipHover(chip, 1.06);   // 分段芯片补 hover 缩放弹性（此前仅手型光标、零反馈，与主题芯片不一致）
            void Apply()
            {
                _settings.Mutate(s => s.RecordFormat = captured);
                foreach (var (c, cid) in chipList)
                {
                    bool sel = cid == captured;
                    c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
                    if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text);
                }
            }
            chip.MouseLeftButtonDown += (_, e) => { Apply(); e.Handled = true; };
            chip.Focusable = true;
            chip.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Apply(); e.Handled = true; } };
            chip.GotKeyboardFocus += (_, _) => { chip.BorderBrush = new SolidColorBrush(Theme.Accent); chip.BorderThickness = new Thickness(1); };
            chip.LostKeyboardFocus += (_, _) => { chip.BorderThickness = new Thickness(0); };
            chips.Children.Add(chip);
        }
        g.Children.Add(chips);
        // ffmpeg 检测状态即时可见（原来只在 MP4/WebM 芯片 tooltip 里，选格式时看不到）；
        // 用绘制的小圆点而非字形，规避未验证字形；只读、开设置时算一次
        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 3, 0, 0) };
        var dotColor = hasFf ? Color.FromRgb(0x4C, 0xAF, 0x50) : Color.FromRgb(0xD1, 0x8B, 0x00);
        status.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = new SolidColorBrush(dotColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        status.Children.Add(new TextBlock
        {
            Text = hasFf ? "已检测到 ffmpeg · 可导出 MP4 / WebM"
                         : "未检测到 ffmpeg · 导出 MP4/WebM 需先安装（见帮助 F1），GIF 不受影响",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        var wrap = new StackPanel();
        wrap.Children.Add(g);
        wrap.Children.Add(status);
        return wrap;
    }
    #endregion

    #region 小部件

    static Grid Row()
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    /// <summary>把一页里平铺的设置行整体归入一张浮起卡片（沿用快捷键页 keyCard 模式），
    /// 让截图/录制/产物…各页与快捷键页视觉一致、不再裸铺在深色背景上（治「太平」）。快捷键页自带卡片，不调本方法。</summary>
    void Cardify(StackPanel p)
    {
        if (p.Children.Count == 0) return;
        var inner = new StackPanel();
        var kids = new System.Collections.Generic.List<UIElement>();
        foreach (UIElement k in p.Children) kids.Add(k);
        p.Children.Clear();
        foreach (var k in kids) inner.Children.Add(k);
        var card = new Border
        {
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            Background = new SolidColorBrush(Theme.CardBg),
            Padding = new Thickness(12, 6, 12, 6),
            Child = inner,
        };
        FrameCapture.Core.Fluent.Elevate(card, blur: 24, depth: 6);
        p.Children.Add(card);
    }

    static TextBlock Icon(string glyph, string tip) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
        FontSize = 16,
        Foreground = new SolidColorBrush(Theme.Text),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 14, 0),
        ToolTip = tip,
    };
    #endregion

}
