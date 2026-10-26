using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    // 可调整选区（默认关）：开着时松手先给一个可拖边角改大小的框，确认才出图
    FrameworkElement EditableSelectionRow() =>
        ToggleRow("\uE7C8", Loc.T(K.Settings.EditableSelection), Loc.T(K.Settings.EditableSelectionDesc),
            _settings.Current.EditableSelection, v => _settings.Mutate(s => s.EditableSelection = v));

    // 取色复制格式：选区放大镜按 C 复制颜色时的文本形式，HEX / RGB / HSL 三选
    FrameworkElement ColorFormatRow()
    {
        var g = ChipRow("\uE790", Loc.T(K.Settings.TheTextFormThePicker), Loc.T(K.Settings.ColorFormat));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        string cur = _settings.Current.ColorCopyFormat;
        var opts = new[]
        {
            ("hex", "HEX", Loc.T(K.Settings.RrggbbTheCommonForm)),
            ("rgb", "RGB", Loc.T(K.Settings.RgbRGBPastes)),
            ("hsl", "HSL", Loc.T(K.Settings.HslHSLCommon)),
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
        // 芯片的选中态是构造期算的配色：改配色时按当前值重排一遍，不然选中那颗会停在旧强调色上。
        _repaint.Add(() => RefreshChips(chipList, _settings.Current.ColorCopyFormat));
        return g;
    }

    #region 时长：一枚可输入下拉框

    /// <summary>时长行的骨架：一行「图标 + 名字 + 下拉框」。</summary>
    /// <param name="glyph">Segoe Fluent Icons 的 PUA 字形。</param>
    /// <param name="tip">图标 tooltip，写明这一行管什么。</param>
    /// <param name="label">行标签。</param>
    /// <param name="combo">已经装好候选档的下拉框。</param>
    /// <returns>可直接加进分页的一行。</returns>
    FrameworkElement DurationRow(string glyph, string tip, string label, ComboBox combo)
    {
        var g = ChipRow(glyph, tip, label);
        Grid.SetColumn(combo, 2);
        g.Children.Add(combo);
        return g;
    }

    /// <summary>一枚时长下拉框：候选档就是那几个常用值，框里直接打数字也认。
    /// 键位表与「打出来的值」共用一种写法（<c>3 秒</c>），挑档与自己打看起来是同一件事。
    /// 选档、回车、移开焦点都落盘；Esc 把框里的字放回当前值。</summary>
    /// <param name="tag">探针认路用的标记。</param>
    /// <param name="presets">候选档：值、档位文案、那一档的说明。</param>
    /// <param name="get">读当前值。落盘会被归一化夹取，回显一律以它为准。</param>
    /// <param name="max">允许输入的上限（秒）。</param>
    /// <param name="tip">框的 tooltip，写明这个数管什么、能打到多大。</param>
    /// <param name="onChange">落定后的写回动作，参数是夹取到 0..<paramref name="max"/> 的值。</param>
    /// <returns>已接好事件的框；宽度按四五个汉字取，够放「30 秒」这类三字档。</returns>
    ComboBox DurationCombo(string tag, (double Value, Str Text, Str Tip)[] presets,
        Func<double> get, double max, string tip, Action<double> onChange)
    {
        string Fmt(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        string TextOf(double v)
        {
            foreach (var p in presets)
                if (Math.Abs(p.Value - v) < 0.0001) return Loc.T(p.Text);
            return Loc.F(K.Settings.SecondsValue, Fmt(v));
        }
        // 框里的字认三种写法：档位文案（「常驻」「关」）、带单位的秒数（「3 秒」「0.5秒」）、光秃秃的数字。
        // 取数字走一遍前缀扫描，中英文单位与夹在中间的空格都不必各自列一条规则。
        double? Parse(string s)
        {
            var text = (s ?? "").Trim();
            if (text.Length == 0) return null;
            foreach (var p in presets)
                if (string.Equals(Loc.T(p.Text), text, StringComparison.OrdinalIgnoreCase)) return p.Value;
            int end = 0;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == ',')) end++;
            if (end == 0) return null;
            var num = text.Substring(0, end).Replace(',', '.');
            return double.TryParse(num, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        var combo = new ComboBox
        {
            Width = 104,
            Tag = tag,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = tip,
        };
        Fluent.StyleComboEditable(combo);
        foreach (var p in presets)
            combo.Items.Add(new ComboBoxItem { Content = Loc.T(p.Text), ToolTip = Loc.T(p.Tip), Tag = p.Value });

        bool sync = false;   // 回显时挂起提交，免得程序写字把自己当成用户挑档
        void Show(double v)
        {
            sync = true;
            // 先把选中项摆到对的档上，再写框里的字：可编辑下拉框一旦把选中项设为空，
            // 会顺手把 Text 清成空串——顺序反过来，「7 秒」这种不在档位上的值当场从框里消失。
            combo.SelectedItem = null;
            foreach (ComboBoxItem it in combo.Items)
                if (it.Tag is double d && Math.Abs(d - v) < 0.0001) { combo.SelectedItem = it; break; }
            combo.Text = TextOf(v);
            sync = false;
        }
        void Apply(double v)
        {
            v = Math.Max(0, Math.Min(max, v));
            if (Math.Abs(v - get()) > 0.0001) onChange(v);
            Show(get());
        }
        void ApplyTyped()
        {
            if (Parse(combo.Text) is not double v) { Show(get()); return; }
            Apply(v);
        }

        Show(get());
        combo.DropDownClosed += (_, _) => ApplyTyped();   // 关弹层即落定：挑完档不必再点一下别的
        combo.SelectionChanged += (_, _) =>
        {
            if (sync) return;
            if (combo.SelectedItem is ComboBoxItem { Tag: double d }) Apply(d);
        };
        combo.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ApplyTyped(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Show(get()); combo.IsDropDownOpen = false; e.Handled = true; }
        };
        combo.LostKeyboardFocus += (_, _) => ApplyTyped();
        // 下拉箭头与圆角描边是拼进模板的字符串：改配色后重新套一遍才跟着换色
        _repaint.Add(() => { Fluent.StyleComboEditable(combo); Show(get()); });
        return combo;
    }

    /// <summary>延时截图：托盘触发后倒数这么久再截，留时间打开右键菜单这类一动就消失的界面。
    /// 候选档是「关 / 1 / 3 / 5 / 10 / 30 / 60 秒」，其余秒数直接打进框里。</summary>
    FrameworkElement DelayRow()
    {
        var combo = DurationCombo("settings-duration-delay", new[]
        {
            (0.0, K.Settings.Off, K.Settings.NoDelayCapturesAsSoon),
            (1.0, K.Settings.S3, K.Settings.QuickEnoughToMove),
            (3.0, K.Settings.S5, K.Settings.UsualEnoughToOpen),
            (5.0, K.Settings.S6, K.Settings.ComfortableEnoughToExpand),
            (10.0, K.Settings.S2, K.Settings.PlentyEnoughToLineUp),
            (30.0, K.Settings.S8, K.Settings.PlentyEnoughToLineUp),
            (60.0, K.Settings.S9, K.Settings.PlentyEnoughToLineUp),
        }, () => _settings.Current.DelayCaptureSeconds, 120, Loc.T(K.Settings.CustomAnyNumberOfSeconds),
            v => _settings.Mutate(s => s.DelayCaptureSeconds = (int)Math.Round(v)));
        return DurationRow("\uE916", Loc.T(K.Settings.AfterTheTraySDelayed), Loc.T(K.Common.DelayedCapture), combo);
    }

    /// <summary>截图后悬浮窗存在时间：截完弹出的「已复制」缩略浮窗停留多久。
    /// 候选档是「常驻 / 1 / 2 / 5 / 10 / 30 秒」，其余秒数直接打进框里。</summary>
    FrameworkElement ToastRow()
    {
        var combo = DurationCombo("settings-duration-toast", new[]
        {
            (0.0, K.Settings.Resident, K.Settings.NeverDisappearsOnItsOwn),
            (1.0, K.Settings.S3, K.Settings.AFlashConfirmsIt),
            (2.0, K.Settings.S4, K.Settings.BriskAGlanceFits),
            (5.0, K.Settings.S6, K.Settings.ComfortableEnoughToMove),
            (10.0, K.Settings.S2, K.Settings.ComfortableEnoughToMove),
            (30.0, K.Settings.S8, K.Settings.ComfortableEnoughToMove),
        }, () => _settings.Current.ToastSeconds, 60, Loc.T(K.Settings.CustomAnythingFromToSeconds),
            v => _settings.Mutate(s => s.ToastSeconds = v));
        return DurationRow("\uE916", Loc.T(K.Settings.HowLongTheCopiedThumbnail), Loc.T(K.Settings.ToastLifetime), combo);
    }

    #endregion

    FrameworkElement CaptureSoundRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 控件列
        g.Children.Add(IconText("\uE767", Loc.T(K.Settings.TheSoundThatConfirms)));
        var lbl = new TextBlock
        {
            Text = Loc.T(K.Settings.CaptureSound),
            FontSize = 13,
            Foreground = Theme.TextBrush,
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
            // 28 高：文字加上下 4 的内边距只有 24，试听是要点准的小按钮，命中面按可点的东西算。
            MinHeight = 28,
            Margin = new Thickness(0, 0, 12, 0),
            // 垂直居中——横向 StackPanel 里 Border 被拉满行高，文字贴左上「错位」真凶
            VerticalAlignment = VerticalAlignment.Center,
            Background = Theme.RowBgBrush,
            Child = new TextBlock { Text = Loc.T(K.Settings.Preview), FontSize = 12.5, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center },
            Cursor = Cursors.Hand,
            ToolTip = Loc.T(K.Settings.ClickToHearTheCurrent),
            Focusable = true,
        };
        void DoPreview() => CaptureSound.Play(
            _settings.Current.CaptureSound ? _settings.Current.CustomSoundPath : "");
        preview.MouseLeftButtonDown += (_, e) => { DoPreview(); e.Handled = true; };
        preview.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { DoPreview(); e.Handled = true; } };
        preview.GotKeyboardFocus += (_, _) => { preview.BorderBrush = Theme.AccentBrush; preview.BorderThickness = new Thickness(1); };
        preview.LostKeyboardFocus += (_, _) => preview.BorderThickness = new Thickness(0);
        Fluent.ChipHover(preview);   // 与普通按钮一致——悬停只改底色带过渡，不放大
        right.Children.Add(preview);

        // 开关+固定音 → 三选下拉「关闭 / 轻弹 / 自定义…」。选「自定义…」弹文件选择窗（ogg/wav）；
        // 取消则回退上一个选项，绝不出现「选了自定义却没文件」的悬空态。
        var combo = new ComboBox { Width = 118, VerticalAlignment = VerticalAlignment.Center };
        Fluent.StyleCombo(combo);
        combo.Items.Add(Loc.T(K.Settings.NoSound));
        combo.Items.Add(Loc.T(K.Settings.Chime));
        combo.Items.Add(Loc.T(K.Settings.Custom));
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
                    Title = Loc.T(K.Settings.ChooseANotificationSound),
                    Filter = Loc.T(K.Settings.SoundsOggWavOggWav),
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
        0 => Loc.T(K.Settings.NoSoundAFinishedCapture),
        1 => Loc.T(K.Settings.ASoftBuiltInBeep),
        _ => Loc.T(K.Settings.ACustomOggWavFile),
    };
}
