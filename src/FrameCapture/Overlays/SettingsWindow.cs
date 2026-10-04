using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Services;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace FrameCapture.Overlays;

/// <summary>紧凑 Fluent 设置面板：图标 + 控件，文字全部进悬停气泡。</summary>
public sealed class SettingsWindow : Window
{
    readonly SettingsService _settings;
    double _downX, _downY;

    public SettingsWindow()
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        _settings = AppHost.Current.Settings;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 420;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "FrameCapture Settings";
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Control)
            {
                try { DragMove(); } catch { }
            }
        };
        FrameCapture.Core.Fluent.PopupEnter(this);
    }

    public static void ShowOrFocus()
    {
        var app = Application.Current;
        var existing = Find();
        if (existing != null) { existing.Activate(); return; }
        var w = new SettingsWindow();
        w.Build();
        w.CenterOnWorkArea();
        w.Show();
        w.Activate();
    }

    static SettingsWindow? Find()
    {
        foreach (Window w in Application.Current.Windows)
            if (w is SettingsWindow s) return s;
        return null;
    }

    void CenterOnWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Top + (wa.Height - ActualHeight) / 2;
    }

    void Build()
    {
        var root = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Theme.WindowBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12, 16, 16),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.5 },
        };
        var panel = new StackPanel();

        // 标题行
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "FrameCapture",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var closeBtn = SmallIcon("\uE711", "关闭 (Esc)");
        closeBtn.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
        Grid.SetColumn(closeBtn, 1);
        titleRow.Children.Add(title);
        titleRow.Children.Add(closeBtn);
        panel.Children.Add(titleRow);
        panel.Children.Add(Rule());

        // 快捷键 ×2
        panel.Children.Add(HotkeyRow("\uE71A", "选区快捷键", "呼出区域选取窗", "scope"));
        panel.Children.Add(HotkeyRow("\uE7F4", "捕获快捷键", "点按 = 单帧 / 长按 = 录屏", "capture"));
        panel.Children.Add(Rule());

        // 滑杆行（名称常显，长说明进悬停提示）
        panel.Children.Add(SliderRow("\uE916", "点按单帧阈值", "按下到放开小于此值算单帧；0 = 全部长按",
            0, 1000, 50, _settings.Current.TapThresholdMs,
            v => $"{v / 1000.0:0.00}s", v => _settings.Mutate(s => s.TapThresholdMs = (int)v)));
        panel.Children.Add(SliderRow("\uEC4A", "录制帧率 (FPS)", "长按录制时每秒捕获的帧数",
            10, 60, 5, _settings.Current.Fps,
            v => $"{v:0} fps", v => _settings.Mutate(s => s.Fps = (int)v)));
        panel.Children.Add(SliderRow("\uE823", "长按录制最长时间", "长按超过此秒数自动停止录制",
            2, 30, 1, _settings.Current.MaxDurationMs / 1000.0,
            v => $"{v:0}s", v => _settings.Mutate(s => s.MaxDurationMs = (int)(v * 1000))));
        panel.Children.Add(SliderRow("\uE8FD", "胶片条抽帧数", "自动生成 Agent 胶片条时从录制帧中抽取的帧数",
            4, 24, 1, _settings.Current.AgentStripFrames,
            v => $"{v:0} 帧", v => _settings.Mutate(s => s.AgentStripFrames = (int)v)));

        // 开关行（名称常显）
        panel.Children.Add(ToggleRow("\uE8B9", "自动生成 Agent 胶片条", "每次捕获后自动生成逐帧胶片条图，便于 AI 理解动作过程",
            _settings.Current.AgentStripEnabled, v => _settings.Mutate(s => s.AgentStripEnabled = v)));
        panel.Children.Add(ToggleRow("\uE71A", "常显捕获范围轮廓", "在屏幕上持续显示当前捕获范围的边框",
            _settings.Current.AlwaysShowRegionOutline, v => _settings.Mutate(s => s.AlwaysShowRegionOutline = v)));
        panel.Children.Add(ToggleRow("\uE7C4", "录制时显示范围边框", "录制期间按真实捕获范围高亮描边，只提示、不录进产物。取代 Windows 11 那条绕整屏的黄色系统捕获边框（WGC 只能整显示器捕获，系统边框必然全屏、与所选范围无关）",
            _settings.Current.ShowRecordingBorder, v => _settings.Mutate(s => s.ShowRecordingBorder = v)));
        panel.Children.Add(ToggleRow("\uE7B5", "选区自动截取", "完成选区后立即截取一帧静态图（PNG，同时复制到剪贴板）",
            _settings.Current.AutoCaptureOnSelect, v => _settings.Mutate(s => s.AutoCaptureOnSelect = v)));
        panel.Children.Add(ToggleRow("\uE722", "点按总是先显示选区", "每次点按捕获快捷键会先弹出选区，选完立即截图写入剪贴板（QQ 式手感）；长按录制不受影响",
            _settings.Current.TapAlwaysShowsRegion, v => _settings.Mutate(s => s.TapAlwaysShowsRegion = v)));
        panel.Children.Add(ToggleRow("\uE740", "吸附到控件", "悬停自动吸附窗口内的具体控件：输入框、文字、图片、分组容器等（Snipaste 式，Tab 可上钻到父容器）。依赖应用无障碍接口，不支持时自动回退整窗吸附",
            _settings.Current.SnapContentArea, v => _settings.Mutate(s => s.SnapContentArea = v)));
        panel.Children.Add(ToggleRow("\uE7E8", "开机自启", "登录 Windows 时自动启动 FrameCapture",
            _settings.Current.RunAtStartup, v =>
            {
                _settings.Mutate(s => s.RunAtStartup = v);
                try { StartupService.Apply(v); } catch { }
            }));

        // 主题芯片（与开关区隔开）
        panel.Children.Add(Rule());
        panel.Children.Add(ThemeRow());

        root.Child = panel;
        Content = root;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInteractive(d)) return;
            _downX = e.GetPosition(this).X; _downY = e.GetPosition(this).Y;
            DragMove();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) FrameCapture.Core.Fluent.CloseWithFade(this); };
        SourceInitialized += (_, _) =>
        {
            Win32.SetRoundedCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            Win32.SetWindowTextW(new System.Windows.Interop.WindowInteropHelper(this).Handle, "FrameCapture Settings");
        };
        Closed += (_, _) => AppHost.Current.InstallHotkeys();
    }

    static bool IsInteractive(DependencyObject d)
    {
        for (DependencyObject? cur = d; cur != null; cur = System.Windows.Media.VisualTreeHelper.GetParent(cur))
            if (cur is Control or System.Windows.Shapes.Shape) return true;
        return false;
    }

    static FrameworkElement Rule() => new Rectangle
    {
        Height = 1,
        Fill = new SolidColorBrush(Theme.Stroke),
        Margin = new Thickness(0, 10, 0, 10),
    };

    // ---------- 快捷键捕获框 ----------

    FrameworkElement HotkeyRow(string glyph, string label, string tip, string which)
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 键位盒列
        g.Children.Add(Icon(glyph, tip));
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var box = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Theme.Hover),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 6, 14, 6),
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand,
            ToolTip = "点击后按下新快捷键（Esc 取消）",
        };
        Grid.SetColumn(box, 2);
        var tb = new TextBlock
        {
            Text = which == "scope" ? _settings.Current.ScopeHotkey : _settings.Current.CaptureHotkey,
            Foreground = new SolidColorBrush(Theme.Text),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        box.Child = tb;
        var hbBase = (SolidColorBrush)box.Background;
        box.MouseEnter += (_, _) =>
        {
            var bgh = new SolidColorBrush(hbBase.Color); box.Background = bgh;
            bgh.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty,
                new System.Windows.Media.Animation.ColorAnimation(Theme.Press, new System.Windows.Duration(TimeSpan.FromMilliseconds(120))));
        };
        box.MouseLeave += (_, _) => { box.Background = new SolidColorBrush(hbBase.Color); };
        g.Children.Add(box);

        bool capturing = false;
        box.MouseLeftButtonDown += (_, e) =>
        {
            capturing = true;
            tb.Text = "按下新键…";
            tb.Foreground = new SolidColorBrush(Theme.TextDim);
            box.BorderBrush = new SolidColorBrush(Theme.Accent);
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (!capturing) return;
            e.Handled = true;
            if (e.Key == Key.Escape) { capturing = false; Refresh(); return; }
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return; // 等主键
            var mods = e.KeyboardDevice.Modifiers;
            if (e.Key is Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 or Key.F6
                or Key.F7 or Key.F8 or Key.F9 or Key.F10 or Key.F11 or Key.F12)
            {
                mods = ModifierKeys.None; // F 键允许裸按
            }
            else if (mods == ModifierKeys.None) return; // 必须带修饰键

            string text = BuildSpec(e.Key, mods);
            if (HotkeySpec.Parse(text) == null) return;
            if (which == "scope") _settings.Mutate(s => s.ScopeHotkey = text);
            else _settings.Mutate(s => s.CaptureHotkey = text);
            capturing = false;
            Refresh();
        };

        void Refresh()
        {
            tb.Foreground = new SolidColorBrush(Theme.Text);
            tb.Text = which == "scope" ? _settings.Current.ScopeHotkey : _settings.Current.CaptureHotkey;
            box.BorderBrush = new SolidColorBrush(Theme.Stroke);
        }
        return g;
    }

    static string BuildSpec(Key key, ModifierKeys mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyNameOf(key));
        return string.Join("+", parts);
    }

    static string KeyNameOf(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.F1 && key <= Key.F24) return key.ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "NumPad" + (key - Key.NumPad0);
        return key switch
        {
            Key.Space => "Space",
            Key.Enter => "Return",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.PageUp => "Prior",
            Key.PageDown => "Next",
            Key.Home => "Home",
            Key.End => "End",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Back => "Back",
            Key.OemPlus => "Oemplus",
            Key.OemMinus => "OemMinus",
            Key.OemQuestion => "Oemquestion",
            Key.OemPeriod => "OemPeriod",
            Key.OemComma => "Oemcomma",
            _ => key.ToString(),
        };
    }

    // ---------- 滑杆行 ----------

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

    // ---------- 开关行 ----------

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
        };
        var knob = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(initial ? Color.FromRgb(0, 0, 0) : Theme.Text),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(4, 0, 4, 0),
            RenderTransform = new System.Windows.Media.TranslateTransform(initial ? 20 : 0, 0),
        };
        Grid.SetColumn(track, 2);
        track.Child = knob;
        bool on = initial;
        track.MouseLeftButtonDown += (_, e) =>
        {
            on = !on;
            FrameCapture.Core.Fluent.SlideToggle(knob, track, on, 20);
            onChange(on);
            e.Handled = true;
        };
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

    // ---------- 主题 ----------

    FrameworkElement ThemeRow()
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 芯片列
        g.Children.Add(Icon("\uE793", "主题（跟随系统 / 浅色 / 深色）"));
        var lbl = new TextBlock
        {
            Text = "外观主题",
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(lbl, 1);
        g.Children.Add(lbl);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(chips, 2);
        string cur = _settings.Current.Theme;
        foreach (var (id, glyph, tip) in new[]
                 { ("system", "\uE977", "跟随系统"), ("light", "\uE706", "浅色"), ("dark", "\uE708", "深色") })
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
                    Foreground = new SolidColorBrush(captured == cur ? Color.FromRgb(0, 0, 0) : Theme.Text),
                },
                Cursor = Cursors.Hand,
                ToolTip = tip,
            };
            var localChip = chip;
            var localText = (TextBlock)chip.Child;
            Color chipBase = captured == cur ? Theme.Accent : Theme.Hover;
            Color chipHover = captured == cur
                ? System.Windows.Media.Color.FromArgb(0xFF, 0x6A, 0xD0, 0xFF)
                : (Theme.IsDark ? System.Windows.Media.Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF) : System.Windows.Media.Color.FromArgb(0x4A, 0x00, 0x00, 0x00));
            localChip.MouseEnter += (_, _) =>
            {
                if (localChip.Background is System.Windows.Media.SolidColorBrush bg0)
                {
                    var anim = new System.Windows.Media.Animation.ColorAnimation(chipHover, new Duration(TimeSpan.FromMilliseconds(120)));
                    var holder = new System.Windows.Media.SolidColorBrush(bg0.Color);
                    localChip.Background = holder;
                    holder.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, anim);
                }
            };
            localChip.MouseLeave += (_, _) =>
            {
                if (localChip.Background is System.Windows.Media.SolidColorBrush bg1)
                {
                    var anim = new System.Windows.Media.Animation.ColorAnimation(chipBase, new Duration(TimeSpan.FromMilliseconds(160)));
                    var holder = new System.Windows.Media.SolidColorBrush(bg1.Color);
                    localChip.Background = holder;
                    holder.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, anim);
                }
            };
            chip.MouseLeftButtonDown += (_, e) =>
            {
                _settings.Mutate(s => s.Theme = captured);
                Theme.Apply(captured, _settings.Current.Accent);
                FrameCapture.Core.Fluent.ApplyGlobalStyles();
                // 无缝过渡：旧窗淡出的同时同位置淡入新窗（Theme.Changed 已让其他窗口即时换色）
                var oldw = this;
                var nw = new SettingsWindow();
                nw.Build();
                nw.Left = oldw.Left; nw.Top = oldw.Top;
                nw.Opacity = 0;
                nw.Show();
                var fo = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ExitMs + 20)); // 交叉淡入的非对称节拍
                fo.Completed += (_, _) => oldw.Close();
                oldw.BeginAnimation(OpacityProperty, fo);
                nw.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ThemeMs)));
                e.Handled = true;
            };
            chips.Children.Add(chip);
        }
        g.Children.Add(chips);
        return g;
    }

    // ---------- 小部件 ----------

    static Grid Row()
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
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

    Button SmallIcon(string glyph, string tip) =>
        FrameCapture.Core.Fluent.IconButton(glyph, tip, 32, 14);
}
