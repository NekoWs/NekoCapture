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
    // 截图浮窗上「复制」的键位：浮窗活着的那几秒由全局钩子代收，改完当场生效。
    FrameworkElement ToastCopyRow()
    {
        var g = ChipRow("\uE8C8", Loc.T(K.Settings.TheToastAlsoAccepts), Loc.T(K.Settings.CopyKey));
        var box = new ChordCaptureBox
        {
            Chord = _settings.Current.ToastCopyChord,
            EmptyHint = Loc.T(K.Hotkeys.ClickToSet),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        // 录入期间暂停全局钩子，否则正要录的那个组合键会被自家钩子先吞掉（与快捷键页同一套做法）
        bool wasPaused = false;
        box.CaptureBegan += () => { wasPaused = Services.HotkeyManager.Paused; Services.HotkeyManager.Paused = true; };
        box.CaptureEnded += () => Services.HotkeyManager.Paused = wasPaused;
        box.Picked += c =>
        {
            _settings.Mutate(s => s.ToastCopyChord = c);
            Services.AppHost.Current.InstallHotkeys();   // 钩子里那份键位串跟着换，不必重启
        };
        Grid.SetColumn(box, 2);
        g.Children.Add(box);
        return g;
    }

    // 可调整选区（默认关）：开着时松手先给一个可拖边角改大小的框，确认才出图
    FrameworkElement EditableSelectionRow() =>
        ToggleRow("\uE7C8", Loc.T(K.Settings.EditableSelection), Loc.T(K.Settings.EditableSelectionDesc),
            _settings.Current.EditableSelection, v => _settings.Mutate(s => s.EditableSelection = v));

    // 确认/取消选区：可调整的框出现后生效，右侧两个键位框点一下就能录
    FrameworkElement PickerKeysRow()
    {
        var g = ChipRow("\uE8C8", Loc.T(K.Settings.ConfirmCancelKeysTip), Loc.T(K.Settings.ConfirmCancelKeysHint));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(row, 2);
        row.Children.Add(PickerKeyBox(_settings.Current.PickerConfirmChord, "confirm"));
        row.Children.Add(PickerKeyBox(_settings.Current.PickerCancelChord, "cancel"));
        g.Children.Add(row);
        return g;
    }

    // 单个键位框：录入期间暂停全局钩子（与快捷键页同一套做法），改完当场落盘
    ChordCaptureBox PickerKeyBox(string chord, string which)
    {
        var box = new ChordCaptureBox
        {
            Chord = chord,
            EmptyHint = Loc.T(K.Hotkeys.ClickToSet),
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        bool wasPaused = false;
        box.CaptureBegan += () => { wasPaused = Services.HotkeyManager.Paused; Services.HotkeyManager.Paused = true; };
        box.CaptureEnded += () => Services.HotkeyManager.Paused = wasPaused;
        box.Picked += c => _settings.Mutate(s =>
        {
            if (which == "confirm") s.PickerConfirmChord = c; else s.PickerCancelChord = c;
        });
        return box;
    }
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
        return g;
    }

    FrameworkElement DelayRow()
    {
        var g = ChipRow("\uE916", Loc.T(K.Settings.AfterTheTraySDelayed), Loc.T(K.Common.DelayedCapture));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        int cur = _settings.Current.DelayCaptureSeconds;
        var opts = new (int secs, string text, string tip)[]
        {
            (0, Loc.T(K.Settings.Off), Loc.T(K.Settings.NoDelayCapturesAsSoon)),
            (1, Loc.T(K.Settings.S3), Loc.T(K.Settings.QuickEnoughToMove)),
            (3, Loc.T(K.Settings.S5), Loc.T(K.Settings.UsualEnoughToOpen)),
            (5, Loc.T(K.Settings.S6), Loc.T(K.Settings.ComfortableEnoughToExpand)),
            (10, Loc.T(K.Settings.S2), Loc.T(K.Settings.PlentyEnoughToLineUp)),
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
            Foreground = Theme.TextBrush,
            CaretBrush = Theme.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var unit = new TextBlock { Text = Loc.T(K.Settings.S7), FontSize = 12.5, Foreground = Theme.TextDimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        var customBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8, 0, 2, 0),
            Background = Theme.CardBgBrush,
            BorderBrush = Theme.StrokeBrush,
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, unit } },
            ToolTip = Loc.T(K.Settings.CustomAnyNumberOfSeconds),
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
        var g = ChipRow("\uE916", Loc.T(K.Settings.HowLongTheCopiedThumbnail), Loc.T(K.Settings.ToastLifetime));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        double cur = _settings.Current.ToastSeconds;
        // 预设只留 常驻/1秒/2秒/5秒 四档（0 = 常驻单独成档），其余秒数走自定义输入
        var opts = new (double secs, string text, string tip)[]
        {
            (0, Loc.T(K.Settings.Resident), Loc.T(K.Settings.NeverDisappearsOnItsOwn)),
            (1, Loc.T(K.Settings.S3), Loc.T(K.Settings.AFlashConfirmsIt)),
            (2, Loc.T(K.Settings.S4), Loc.T(K.Settings.BriskAGlanceFits)),
            (5, Loc.T(K.Settings.S6), Loc.T(K.Settings.ComfortableEnoughToMove)),
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
            Foreground = Theme.TextBrush,
            CaretBrush = Theme.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var unit = new TextBlock { Text = Loc.T(K.Settings.S7), FontSize = 12.5, Foreground = Theme.TextDimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        var customBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8, 0, 2, 0),
            Background = Theme.CardBgBrush,
            BorderBrush = Theme.StrokeBrush,
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, unit } },
            ToolTip = Loc.T(K.Settings.CustomAnythingFromToSeconds),
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
