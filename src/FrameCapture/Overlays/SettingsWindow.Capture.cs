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
        // 芯片的选中态是构造期算的配色：配色变化时重排选中态。
        _repaint.Add(() => RefreshChips(chipList, _settings.Current.ColorCopyFormat));
        return g;
    }

    #region 时长：一枚可输入下拉框

    /// <summary>时长行的骨架：一行「图标 + 名字 + 数值框 + 单位」。</summary>
    /// <param name="glyph">Segoe Fluent Icons 的 PUA 字形。</param>
    /// <param name="tip">图标 tooltip，写明这一行管什么。</param>
    /// <param name="label">行标签。</param>
    /// <param name="field">数值框与后面那个单位，见 <see cref="DurationField"/>。</param>
    /// <returns>可直接加进分页的一行。</returns>
    FrameworkElement DurationRow(string glyph, string tip, string label, FrameworkElement field)
    {
        var g = ChipRow(glyph, tip, label);
        Grid.SetColumn(field, 2);
        g.Children.Add(field);
        return g;
    }

    /// <summary>时长输入：一枚只装数字的下拉框，后面跟一枚单位。
    /// 框里只放数字，单位摆在外面，框宽就能收到三位数刚好——「秒」写在框里时那一格总要给三个字留位，
    /// 数字只有一两位，右边空出一大片。特殊档（关 / 常驻）由单位那一格改口，框里始终是数字。
    /// 选档、回车、移开焦点都落盘；Esc 把框里的字放回当前值。</summary>
    /// <param name="tag">探针认路用的标记。</param>
    /// <param name="presets">候选档：值 + 那一档的说明（框里显示的就是数字）。</param>
    /// <param name="get">读当前值。落盘会被归一化夹取，回显一律以它为准。</param>
    /// <param name="max">允许输入的上限。</param>
    /// <param name="special">特殊档的值（0 = 关 / 常驻）。</param>
    /// <param name="specialWord">特殊档时单位那一格改成的字。</param>
    /// <param name="tip">框的 tooltip，写明这个数管什么、能打到多大。</param>
    /// <param name="onChange">落定后的写回动作，参数是夹取到 0..<paramref name="max"/> 的值。</param>
    /// <param name="unitText">常规单位；为空时使用秒。</param>
    /// <returns>已接好事件的「框 + 单位」小组合。</returns>
    FrameworkElement DurationField(string tag, (double Value, Str Tip)[] presets,
        Func<double> get, double max, double special, string specialWord, string tip, Action<double> onChange, string? unitText = null)
    {
        string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        // 框里的字认三种写法：光秃秃的数字、「3 秒」这种带单位的、以及「关」「常驻」这两个词。
        // 取数字走一遍前缀扫描，中英文单位与夹在中间的空格都不必各自列一条规则。
        double? Parse(string s)
        {
            var text = (s ?? "").Trim();
            if (text.Length == 0) return null;
            if (string.Equals(text, specialWord, StringComparison.OrdinalIgnoreCase)) return special;
            if (string.Equals(text, Loc.T(K.Settings.Off), StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, Loc.T(K.Settings.Resident), StringComparison.OrdinalIgnoreCase)) return special;
            int end = 0;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == ',')) end++;
            if (end == 0) return null;
            var num = text.Substring(0, end).Replace(',', '.');
            return double.TryParse(num, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        var combo = new ComboBox
        {
            Tag = tag,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tip,
        };
        Fluent.StyleComboEditable(combo);
        foreach (var p in presets)
            combo.Items.Add(new ComboBoxItem { Content = Num(p.Value), ToolTip = Loc.T(p.Tip), Tag = p.Value });

        var unit = new TextBlock
        {
            FontSize = 12.5,
            Foreground = Theme.TextDimBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0),
            Tag = tag + "-unit",
        };

        void PaintUnit(double v) => unit.Text = Math.Abs(v - special) < 0.0001 ? specialWord : unitText ?? Loc.T(K.Settings.SecondsUnit);
        var show = BindNumericField(combo, get, value => onChange(Math.Clamp(value, 0, max)), Parse, Num, PaintUnit);
        _repaint.Add(() =>
        {
            Fluent.StyleComboEditable(combo);
            unit.Foreground = Theme.TextDimBrush;
            show();
        });
        var wrap = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { combo, unit },
        };
        return wrap;
    }

    /// <summary>延时截图：托盘触发后倒数这么久再截，留时间打开右键菜单这类一动就消失的界面。
    /// 候选档是 0 / 1 / 3 / 5 / 10 / 30 / 60 秒，其余秒数直接打进框里；0 的单位那一格改口成「关」。</summary>
    FrameworkElement DelayRow()
    {
        var field = DurationField("settings-duration-delay", new[]
        {
            (0.0, K.Settings.NoDelayCapturesAsSoon),
            (1.0, K.Settings.QuickEnoughToMove),
            (3.0, K.Settings.UsualEnoughToOpen),
            (5.0, K.Settings.ComfortableEnoughToExpand),
            (10.0, K.Settings.PlentyEnoughToLineUp),
            (30.0, K.Settings.PlentyEnoughToLineUp),
            (60.0, K.Settings.PlentyEnoughToLineUp),
        }, () => _settings.Current.DelayCaptureSeconds, AppSettings.DelayCaptureSecondsMax, 0, Loc.T(K.Settings.Off),
            Loc.T(K.Settings.CustomAnyNumberOfSeconds),
            v => _settings.Mutate(s => s.DelayCaptureSeconds = (int)Math.Round(v)));
        return DurationRow("\uE916", Loc.T(K.Settings.AfterTheTraySDelayed), Loc.T(K.Common.DelayedCapture), field);
    }

    private FrameworkElement SnapDwellRow()
    {
        var hint = K.Settings.SnapDwellHint;
        var unit = Loc.T(K.Common.Milliseconds);
        var field = DurationField("settings-duration-snap", new[]
        {
            (0.0, hint), (25.0, hint), (50.0, hint), (100.0, hint), (200.0, hint), (500.0, hint), (1000.0, hint)
        }, () => _settings.Current.SnapDwellMs, 5000, 0, unit, Loc.T(hint),
            value => _settings.Mutate(settings => settings.SnapDwellMs = (int)Math.Round(value)), unit);
        return DurationRow("\uE916", Loc.T(hint), Loc.T(K.Settings.SnapDwell), field);
    }

    /// <summary>截图后悬浮窗存在时间：截完弹出的「已复制」缩略浮窗停留多久。
    /// 候选档是 0 / 1 / 2 / 5 / 10 / 30 秒，其余秒数直接打进框里；0 的单位那一格改口成「常驻」。</summary>
    FrameworkElement ToastRow()
    {
        var field = DurationField("settings-duration-toast", new[]
        {
            (0.0, K.Settings.NeverDisappearsOnItsOwn),
            (1.0, K.Settings.AFlashConfirmsIt),
            (2.0, K.Settings.BriskAGlanceFits),
            (5.0, K.Settings.ComfortableEnoughToMove),
            (10.0, K.Settings.ComfortableEnoughToMove),
            (30.0, K.Settings.ComfortableEnoughToMove),
        }, () => _settings.Current.ToastSeconds, 60, 0, Loc.T(K.Settings.Resident),
            Loc.T(K.Settings.CustomAnythingFromToSeconds),
            v => _settings.Mutate(s => s.ToastSeconds = v));
        return DurationRow("\uE916", Loc.T(K.Settings.HowLongTheCopiedThumbnail), Loc.T(K.Settings.ToastLifetime), field);
    }

    /// <summary>截图后那圈边框亮多久：截完一圈描边慢慢退掉，这一行是它的时长。
    /// 候选档 0.2 / 0.5 / 1 / 1.5 / 2 / 3 秒，0.1–5 秒之间直接打进框里。</summary>
    FrameworkElement OutlineFadeRow()
    {
        var field = DurationField("settings-duration-outline", new[]
        {
            (0.2, K.Settings.TheMomentACaptureFinishes),
            (0.5, K.Settings.TheMomentACaptureFinishes),
            (1.0, K.Settings.TheMomentACaptureFinishes),
            (1.5, K.Settings.TheMomentACaptureFinishes),
            (2.0, K.Settings.TheMomentACaptureFinishes),
            (3.0, K.Settings.TheMomentACaptureFinishes),
        }, () => _settings.Current.OutlineFadeSeconds, 5, -1, "", Loc.T(K.Settings.TheMomentACaptureFinishes),
            v => _settings.Mutate(s => s.OutlineFadeSeconds = v));
        return DurationRow("\uE7C4", Loc.T(K.Settings.BorderFlashAfterACapture), Loc.T(K.Settings.BorderFlashAfterACapture), field);
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

        // 试听遵循当前音效设置。
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
        void DoPreview()
        {
            if (_settings.Current.CaptureSound) CaptureSound.Play(_settings.Current.CustomSoundPath);
        }
        void SyncPreview()
        {
            preview.IsEnabled = _settings.Current.CaptureSound;
            preview.Opacity = preview.IsEnabled ? 1 : 0.45;
            preview.Cursor = preview.IsEnabled ? Cursors.Hand : Cursors.Arrow;
        }
        SyncPreview();
        preview.MouseLeftButtonDown += (_, e) => { DoPreview(); e.Handled = true; };
        preview.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { DoPreview(); e.Handled = true; } };
        preview.GotKeyboardFocus += (_, _) => { preview.BorderBrush = Theme.AccentBrush; preview.BorderThickness = new Thickness(1); };
        preview.LostKeyboardFocus += (_, _) => preview.BorderThickness = new Thickness(0);
        Fluent.ChipHover(preview);   // 与普通按钮一致——悬停只改底色带过渡，不放大
        right.Children.Add(preview);

        // 开关+固定音 → 三选下拉「关闭 / 轻弹 / 自定义…」。选「自定义…」弹文件选择窗（ogg/wav）；
        // 取消则回退上一个选项，绝不出现「选了自定义却没文件」的悬空态。
        var combo = new ComboBox { VerticalAlignment = VerticalAlignment.Center };
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
            SyncPreview();
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
