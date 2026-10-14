using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

// 本 partial 职责：截图分组行控件构建——取色格式、延时截图、悬浮窗时长、截图提示音（含其提示文案与下拉配色）。
/// <summary>
/// 设置窗的截图分组部分：每个 <c>*Row()</c> 产出一行控件，初值取自 <c>SettingsService.Current</c>，
/// 交互回调里就地 <c>Mutate</c> 落盘，无需「应用」按钮。全部方法只在 <see cref="SettingsWindow.Build"/> 期间、UI 线程调用。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    // 取色复制格式：选区放大镜按 C 复制颜色时的文本形式，HEX / RGB / HSL 三选
    FrameworkElement ColorFormatRow()
    {
        var g = ChipRow("\uE790", "选区放大镜按 C 复制颜色时的文本形式：#RRGGBB / rgb(r, g, b) / hsl(h, s%, l%)", "取色格式");
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        string cur = _settings.Current.ColorCopyFormat;
        var opts = new[]
        {
            ("hex", "HEX", "#RRGGBB（通用格式）"),
            ("rgb", "RGB", "rgb(r, g, b)（直接贴进 CSS）"),
            ("hsl", "HSL", "hsl(h, s%, l%)（调色/设计常用）"),
        };
        var chipList = new List<(Border chip, string id)>();
        foreach (var (id, text, tip) in opts)
        {
            string captured = id;
            var chip = Chip(text, tip, captured == cur);
            chipList.Add((chip, id));
            void Apply()
            {
                _settings.Mutate(s => s.ColorCopyFormat = captured);
                RefreshChips(chipList, captured);
            }
            WireChip(chip, Apply);
            chips.Children.Add(chip);
        }
        g.Children.Add(chips);
        return g;
    }

    FrameworkElement DelayRow()
    {
        var g = ChipRow("\uE916", "托盘「延时截图」触发后倒数这么久再截当前范围，留时间打开右键菜单/悬停提示/下拉框这类一动就消失的界面", "延时截图");
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        int cur = _settings.Current.DelayCaptureSeconds;
        var opts = new (int secs, string text, string tip)[]
        {
            (0, "关", "不延时，触发即截"),
            (1, "1秒", "快速：够移一下鼠标"),
            (3, "3秒", "常用：够打开一个菜单"),
            (5, "5秒", "从容：够展开下拉/悬停"),
            (10, "10秒", "充裕：够摆好几个界面元素"),
        };
        var chipList = new List<(Border chip, int secs)>();
        // 预设 5 档之外还有「自定义」输入框：1–120 秒任意值都调得了；文字居中，与悬浮窗时长同款交互
        var box = new TextBox
        {
            Text = cur.ToString(),
            Width = 30,
            FontSize = 12.5,
            TextAlignment = TextAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Theme.Text),
            CaretBrush = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var unit = new TextBlock { Text = "秒", FontSize = 12.5, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        var customBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8, 0, 2, 0),
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, unit } },
            ToolTip = "自定义：输入任意秒数（1–120），回车或移开焦点生效",
            Cursor = Cursors.Hand,
        };
        customBox.MouseLeftButtonUp += (_, _) => { box.Focus(); box.SelectAll(); };
        void RefreshSel(int v)
        {
            bool anySel = RefreshChips(chipList, v);
            customBox.Background = new SolidColorBrush(anySel ? Theme.CardBg : Theme.Accent);
            customBox.BorderBrush = new SolidColorBrush(anySel ? Theme.Stroke : Theme.Accent);
            box.Foreground = new SolidColorBrush(anySel ? Theme.Text : Fluent.OnAccentColor(Theme.Accent));
            unit.Foreground = new SolidColorBrush(anySel ? Theme.TextDim : Fluent.OnAccentColor(Theme.Accent));
            box.Text = v.ToString();
        }
        void Commit()
        {
            var raw = (box.Text ?? "").Trim();
            if (!int.TryParse(raw, out int v) || v < 1) { box.Text = _settings.Current.DelayCaptureSeconds.ToString(); return; }
            v = Math.Clamp(v, 1, 120);
            _settings.Mutate(s => s.DelayCaptureSeconds = v);
            RefreshSel(v);
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; } };
        foreach (var (secs, text, tip) in opts)
        {
            int captured = secs;
            var chip = Chip(text, tip, captured == cur);
            chipList.Add((chip, secs));
            void Apply()
            {
                _settings.Mutate(s => s.DelayCaptureSeconds = captured);
                RefreshSel(captured);
            }
            WireChip(chip, Apply);
            chips.Children.Add(chip);
        }
        chips.Children.Add(customBox);
        RefreshSel(cur);
        g.Children.Add(chips);
        return g;
    }

    // 截图后悬浮窗存在时间——文字芯片（常驻/秒数），点击就地更新选中态
    FrameworkElement ToastRow()
    {
        var g = ChipRow("\uE916", "截完弹出的「已复制」缩略浮窗停留多久自动消失。悬停其上会暂停计时、移开再给满时长。想拍完即走调小、想留着慢慢点进编辑器调大或常驻。", "悬浮窗存在时间");
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        double cur = _settings.Current.ToastSeconds;
        // 预设只留 常驻/1秒/2秒/5秒 四档（0 = 常驻单独成档），其余秒数走自定义输入
        var opts = new (double secs, string text, string tip)[]
        {
            (0, "常驻", "不自动消失，点一下或 Esc 才关"),
            (1, "1秒", "快闪：确认一下就走"),
            (2, "2秒", "利落：来得及看一眼"),
            (5, "5秒", "从容：够移动鼠标去点"),
        };
        var chipList = new List<(Border chip, double secs)>();
        string Fmt(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var box = new TextBox
        {
            Text = Fmt(cur),
            Width = 34,
            FontSize = 12.5,
            TextAlignment = TextAlignment.Center,   // 数字居中：34px 窄框里贴边数字像被裁了一半
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Theme.Text),
            CaretBrush = new SolidColorBrush(Theme.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var unit = new TextBlock { Text = "秒", FontSize = 12.5, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        var customBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8, 0, 2, 0),
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, unit } },
            ToolTip = "自定义：输入任意秒数（0.5–60，0=常驻），回车或移开焦点生效",
            Cursor = Cursors.Hand,
        };
        customBox.MouseLeftButtonUp += (_, _) => { box.Focus(); box.SelectAll(); };
        void RefreshSel(double v)
        {
            // 预设档匹配用 0.01 容差而非精确相等：自定义框可输入任意小数，近似值仍算命中预设档
            bool anySel = false;
            foreach (var (c, cs) in chipList)
            {
                bool sel = Math.Abs(cs - v) < 0.01;
                anySel |= sel;
                c.Background = new SolidColorBrush(sel ? Theme.Accent : Theme.Hover);
                if (c.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(sel ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text);
            }
            // 值不落在任何预设档 = 自定义生效，自定义框高亮成选中态，一眼看出当前用的是哪个值
            customBox.Background = new SolidColorBrush(anySel ? Theme.CardBg : Theme.Accent);
            customBox.BorderBrush = new SolidColorBrush(anySel ? Theme.Stroke : Theme.Accent);
            box.Foreground = new SolidColorBrush(anySel ? Theme.Text : Fluent.OnAccentColor(Theme.Accent));
            unit.Foreground = new SolidColorBrush(anySel ? Theme.TextDim : Fluent.OnAccentColor(Theme.Accent));
            box.Text = Fmt(v);
        }
        void Commit()
        {
            var raw = (box.Text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || v < 0)
            { box.Text = Fmt(_settings.Current.ToastSeconds); return; }   // 非法输入回退当前值
            _settings.Mutate(s => s.ToastSeconds = v);                     // Normalize 夹 0.5–60，0=常驻
            RefreshSel(_settings.Current.ToastSeconds);                    // 用夹取后的实际值回显
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; } };
        foreach (var (secs, text, tip) in opts)
        {
            double captured = secs;
            bool sel0 = Math.Abs(captured - cur) < 0.01;
            var chip = Chip(text, tip, sel0);
            chipList.Add((chip, secs));
            void Apply()
            {
                _settings.Mutate(s => s.ToastSeconds = captured);
                RefreshSel(captured);
            }
            WireChip(chip, Apply);
            chips.Children.Add(chip);
        }
        chips.Children.Add(customBox);
        RefreshSel(cur);   // 初始回显：值不在预设档时自定义框即刻高亮
        g.Children.Add(chips);
        return g;
    }

    FrameworkElement CaptureSoundRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 控件列
        g.Children.Add(IconText("\uE767", "静态截图完成时的听觉确认。关闭=静音；轻弹=内置柔和「嘀」；自定义…=选一个 ogg/wav 文件（wav 最稳，ogg 需系统解码器，播不了会自动回退轻弹）。随时可「试听」"));
        var lbl = new TextBlock
        {
            Text = "截图提示音",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 2);

        // 试听：按当前选择（含自定义文件）立刻放一声
        var preview = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 12, 0),
            // 垂直居中——横向 StackPanel 里 Border 被拉满行高，文字贴左上「错位」真凶
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Theme.Hover),
            Child = new TextBlock { Text = "试听", FontSize = 12.5, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center },
            Cursor = Cursors.Hand,
            ToolTip = "点一下试听当前提示音（选「自定义…」后也会立刻放一声让你确认）",
            Focusable = true,
        };
        void DoPreview() => CaptureSound.Play(
            _settings.Current.CaptureSound ? _settings.Current.CustomSoundPath : "");
        preview.MouseLeftButtonDown += (_, e) => { DoPreview(); e.Handled = true; };
        preview.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { DoPreview(); e.Handled = true; } };
        preview.GotKeyboardFocus += (_, _) => { preview.BorderBrush = new SolidColorBrush(Theme.Accent); preview.BorderThickness = new Thickness(1); };
        preview.LostKeyboardFocus += (_, _) => preview.BorderThickness = new Thickness(0);
        Fluent.ChipHover(preview);   // 与普通按钮一致——悬停只改底色带过渡，不放大
        right.Children.Add(preview);

        // 开关+固定音 → 三选下拉「关闭 / 轻弹 / 自定义…」。选「自定义…」弹文件选择窗（ogg/wav）；
        // 取消则回退上一个选项，绝不出现「选了自定义却没文件」的悬空态。
        var combo = new ComboBox { Width = 118, VerticalAlignment = VerticalAlignment.Center };
        GestureEditor.EditorStyles.ApplyCombo(combo, EditorPal());
        combo.Items.Add("关闭");
        combo.Items.Add("轻弹");
        combo.Items.Add("自定义…");
        int last = !_settings.Current.CaptureSound ? 0 : (string.IsNullOrWhiteSpace(_settings.Current.CustomSoundPath) ? 1 : 2);
        combo.SelectedIndex = last;
        combo.ToolTip = SoundTip(combo.SelectedIndex);
        bool suppress = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (suppress) return;
            int i = combo.SelectedIndex;
            if (i == 2)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择提示音（ogg / wav）",
                    Filter = "提示音 (*.ogg;*.wav)|*.ogg;*.wav|所有文件 (*.*)|*.*",
                    CheckFileExists = true,
                };
                if (dlg.ShowDialog(this) == true)
                {
                    _settings.Mutate(s => { s.CaptureSound = true; s.CustomSoundPath = dlg.FileName; });
                    CaptureSound.Play(dlg.FileName);   // 选完立刻放一声确认效果
                }
                else { suppress = true; combo.SelectedIndex = last; suppress = false; }   // 取消→回退原选项
            }
            else if (i == 0) _settings.Mutate(s => s.CaptureSound = false);
            else _settings.Mutate(s => { s.CaptureSound = true; s.CustomSoundPath = ""; });
            last = combo.SelectedIndex;
            combo.ToolTip = SoundTip(last);
        };
        right.Children.Add(combo);

        g.Children.Add(right);
        return g;
    }

    static string SoundTip(int i) => i switch
    {
        0 => "静音：截图完成不放声音",
        1 => "内置柔和「嘀」声（听觉确认）",
        _ => "自定义提示音文件（ogg/wav）；点击可重新选择",
    };

    /// <summary>手势编辑器同款下拉/输入样式的配色（lib 不引用宿主，配色由宿主注入）。</summary>
    static GestureEditor.EditorPalette EditorPal() => new()
    {
        WindowBg = Theme.WindowBg, PanelBg = Theme.PanelBg, CardBg = Theme.CardBg,
        Text = Theme.Text, TextDim = Theme.TextDim, Stroke = Theme.Stroke,
        Accent = Theme.Accent, Hover = Theme.Hover, Press = Theme.Press, TrackBg = Theme.TrackBg,
        IsDark = Theme.IsDark,
    };
}
