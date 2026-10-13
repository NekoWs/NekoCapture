using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Encoding;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

// 布局与工具条：停靠面板/按钮构建 + 主题换肤 + 撤销重做状态 + 颜色/线宽选择
public sealed partial class EditorWindow : Window
{

    // ---------------- 布局 ----------------

    Button IconBtn(string glyph, string tip, double size = 28) =>
        FrameCapture.Core.Fluent.IconButton(glyph, tip, size, 13);

    // 文字按钮（非图标字体）：字号步进器 A−/A+ 用，走同一 IconButton 模板保持悬停/按压手感一致
    Button TextSizeBtn(string text, string tip)
    {
        var b = new Button
        {
            Content = text, ToolTip = tip, Width = 26, Height = 26, Margin = new Thickness(1),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Theme.Text), Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, Template = FrameCapture.Core.Fluent.IconButtonTemplate(),
            Focusable = false,
        };
        return b;
    }

    // R81：钉到屏幕按钮改用实心图钉 E840（招牌动作配招牌图钉，Snipaste/QQ 惯例）；「总在最前」改置顶箭头 E898 以免两图钉混淆
    Button PinBtn()
    {
        var b = IconBtn("\uE840", "钉到屏幕 (Ctrl+P)");
        b.Click += (_, _) => PinCurrentFrame();
        return b;
    }

    // R69：识别文字按钮（E8C1「A字」= 文字识别，与文本工具/复制明显区分）
    Button OcrBtn()
    {
        var b = IconBtn("\uE8C1", "识别文字（复制到剪贴板）  Ctrl+O");
        b.Click += (_, _) => OcrCurrentFrame();
        return b;
    }

    void BuildLayout()
    {
        // 停靠式布局：预览在上，播放面板（录制时常驻）与工具面板依次停靠其下——控件不再悬浮在画面里
        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _viewGrid.Children.Add(_view);
        _viewGrid.Children.Add(_canvas);
        // R69：预览底部居中轻提示浮层（默认隐藏，ShowSnack 淡入淡出）
        _snackText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI"),
            TextAlignment = TextAlignment.Center,
        };
        _snack = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x1F, 0x1F, 0x1F)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 22),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _snackText,
        };
        _viewGrid.Children.Add(_snack);
        var viewHost = new Border
        {
            CornerRadius = new CornerRadius(8, 8, 0, 0), // 上角与 Win11 原生窗口圆角贴合；下角由工具面板收口
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            Child = _viewGrid,
        };
        Grid.SetRow(viewHost, 0);
        root.Children.Add(viewHost);
        Content = root;
        _root = root;

        BuildPanels(root);
    }

    Border Panel(FrameworkElement child) => new()
    {
        Background = new SolidColorBrush(Theme.PanelBg),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
        BorderThickness = new Thickness(0, 1, 0, 0),
        Padding = new Thickness(8, 2, 8, 2),
        Child = child,
    };

    void Editor_OnTheme()
    {
        var bg = Background as System.Windows.Media.SolidColorBrush;
        if (bg != null)
        {
            var anim = new System.Windows.Media.Animation.ColorAnimation(Theme.WindowBg, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ThemeMs));
            var holder = new System.Windows.Media.SolidColorBrush(bg.Color);
            Background = holder;
            holder.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, anim);
        }
        _hud.Foreground = new System.Windows.Media.SolidColorBrush(Theme.Text);
        // 选中语言随行：工具/线宽的前景色随主题（选中项反白、未选中项 Theme.Text），否则换肤后旧色不可读
        for (int i = 0; i < _toolBtns.Count; i++)
            Highlight(_toolBtns[i], ToolOrder[i] == _tool);
        RefreshWidths();
        foreach (var p in new[] { _playPanel, _toolPanel })
            if (p != null)
            {
                p.Background = new System.Windows.Media.SolidColorBrush(Theme.PanelBg);
                p.BorderBrush = new System.Windows.Media.SolidColorBrush(Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B));
            }
        DrawRange();
    }

    bool MultiFrame => _seq.Frames.Count > 1;

    void BuildPanels(Grid root)
    {
        if (MultiFrame)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            int fps = _fps;
            var bPrevSec = IconBtn("\uF8AC", "上一秒 (Shift+←)"); bPrevSec.Click += (_, _) => Step(-fps);
            var bPrev = IconBtn("\uF08D", "上一帧 (←)"); bPrev.Click += (_, _) => Step(-1);
            _bPlay = IconBtn("\uF5B0", "播放/暂停 (空格)"); _bPlay.Click += (_, _) => TogglePlay();
            var bNext = IconBtn("\uF08F", "下一帧 (→)"); bNext.Click += (_, _) => Step(+1);
            var bNextSec = IconBtn("\uF8AD", "下一秒 (Shift+→)"); bNextSec.Click += (_, _) => Step(+fps);
            foreach (var b in new FrameworkElement[] { bPrevSec, bPrev, _bPlay, bNext, bNextSec }) transport.Children.Add(b);
            Grid.SetColumn(transport, 0);
            g.Children.Add(transport);

            if (_rangeHost != null)
            {
                _rangeHost.Background = Brushes.Transparent;
                _rangeHost.VerticalAlignment = VerticalAlignment.Stretch;
                _rangeHost.Margin = new Thickness(8, 0, 8, 0);
                Grid.SetColumn(_rangeHost, 1);
                g.Children.Add(_rangeHost);
            }
            // HUD 定底宽右对齐（R57）：帧数/时间/裁剪段文本变宽不再挤压时间轴列（用户实测：布局整体抖动）。
            // MinWidth 而非 Width：罕见的「⚠丢帧」长警告仍完整可读，只是那种情况下才允许变宽。
            _hud.MinWidth = 210;
            _hud.TextAlignment = TextAlignment.Right;
            _hud.Margin = new Thickness(6, 0, 8, 0);
            Grid.SetColumn(_hud, 2);
            g.Children.Add(_hud);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var bSaveFrame = IconBtn("\uE105", "保存当前帧 (PNG)  Ctrl+S"); bSaveFrame.Click += (_, _) => SaveFrame();
            var bCopyFrame = IconBtn("\uF413", "复制当前帧 (位图)  Ctrl+C"); bCopyFrame.Click += (_, _) => ClipboardService.SetFrame(ExportFrame(_index));
            var bPinFrame = PinBtn();
            var bOcrFrame = OcrBtn();
            var bSaveGif = IconBtn("\uE116", "导出录制 (MP4 / WebM / GIF)  Ctrl+E"); bSaveGif.Click += (_, _) => SaveRecording();
            var bAfter = IconBtn("\uE955", "保存视觉暂留合成图  Ctrl+H"); bAfter.Click += (_, _) => SaveAfterimage();
            // R161：胶片条从「设置里全局自动生成」改为显式功能按钮——小窗问抽帧参数，生成后直接复制剪贴板
            var bStrip = IconBtn("\uE8B9", "生成胶片条（抽帧拼成一张逐帧图 → 剪贴板）"); bStrip.Click += (_, _) => GenerateFilmStrip();
            foreach (var b in new[] { bSaveFrame, bCopyFrame, bPinFrame, bOcrFrame, bSaveGif, bAfter, bStrip }) actions.Children.Add(b);
            actions.Children.Add(Sep());
            var bCancel = IconBtn("\uE711", "取消 (Esc)"); bCancel.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
            var bOk = IconBtn("\uF78C", "确定：GIF 写入剪贴板 (Enter)"); bOk.Click += (_, _) => Commit();
            actions.Children.Add(bCancel);
            actions.Children.Add(bOk);
            FrameCapture.Core.Fluent.HighlightBtn(bOk, true);
            _okBtns.Add(bOk); // 主按钮：accent 实心
            Grid.SetColumn(actions, 3);
            g.Children.Add(actions);

            _playPanel = Panel(g);
            Grid.SetRow(_playPanel, 1);
            root.Children.Add(_playPanel);
        }

        var tools = BuildToolsBar();
        _toolsBar = tools; // R93：留引用，供 FitMinWidthToToolbar 量宽
        if (!MultiFrame)
        {
            tools.Children.Add(Sep());
            var bSaveF = IconBtn("\uE105", "保存当前帧 (PNG)"); bSaveF.Click += (_, _) => SaveFrame();
            var bCopyF = IconBtn("\uF413", "复制当前帧 (位图)"); bCopyF.Click += (_, _) => ClipboardService.SetFrame(ExportFrame(_index));
            var bPinF = PinBtn();
            var bOcrF = OcrBtn();
            var bOkF = IconBtn("\uF78C", "确定：写入剪贴板 (Enter)"); bOkF.Click += (_, _) => Commit();
            var bX = IconBtn("\uE711", "取消 (Esc)"); bX.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
            foreach (var b in new[] { bSaveF, bCopyF, bPinF, bOcrF, bOkF, bX }) tools.Children.Add(b);
            FrameCapture.Core.Fluent.HighlightBtn(bOkF, true);
            _okBtns.Add(bOkF); // 主按钮：accent 实心
        }
        tools.Children.Add(Sep());
        // R81：编辑器总在最前（R44）。改用置顶箭头 E898（向上箭头带顶栏，GlyphProbe 已验证），开关态靠高亮底色区分，不再与「钉到屏幕」图钉混淆
        var bPin = IconBtn("\uE898", "总在最前");
        FrameCapture.Core.Fluent.HighlightBtn(bPin, _settings.Current.EditorTopmost);
        bPin.Click += (_, _) =>
        {
            bool on = !Topmost;
            Topmost = on;
            _settings.Mutate(s => s.EditorTopmost = on);
            FrameCapture.Core.Fluent.HighlightBtn(bPin, on);
            FrameCapture.Core.Trace.Log("editor topmost=" + on);
        };
        tools.Children.Add(bPin);
        var bSet = IconBtn("\uE713", "设置"); bSet.Click += (_, _) => SettingsWindow.ShowOrFocus();
        var bHelp = IconBtn("\uE9CE", "帮助与快捷键"); bHelp.Click += (_, _) => HelpWindow.ShowOrFocus();
        tools.Children.Add(bSet);
        tools.Children.Add(bHelp);
        _toolPanel = Panel(tools);
        _toolPanel.CornerRadius = new CornerRadius(0, 0, 8, 8); // 底角收口，与原生窗口圆角贴合
        // 工具条浮于画布之上：向上投影（Direction=90，与 Elevate 同族但朝上），暗底浓亮底淡。
        // 此前工具条只靠 1px 顶边与画布分隔，截到深色画面时几乎融为一体、显「太平」。
        _toolPanel.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 20,
            ShadowDepth = 6,
            Direction = 90,
            Opacity = Theme.IsDark ? 0.6 : 0.3,
        };
        Grid.SetRow(_toolPanel, 2);
        root.Children.Add(_toolPanel);
    }

    /// <summary>
    /// R93：让窗口 MinWidth 跟随底部工具条的实际宽度。工具条是居中 StackPanel，窗口比它窄时
    /// 两端工具会被裁掉（历史上 MinWidth 是写死的 812，但工具条随功能增长早已超过它）。
    /// 量工具条 DesiredSize（含最近用色，最多 4 枚）+ 面板内边距，夹到 [812, 工作区宽]。
    /// DesiredSize 是内容宽（StackPanel 不受容器约束），故窗口窄时也能量到真实需要。
    /// </summary>
    void FitMinWidthToToolbar()
    {
        if (_toolsBar == null) return;
        _toolsBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double need = Math.Ceiling(_toolsBar.DesiredSize.Width) + 20; // 面板左右内边距 8+8 + 余量
        double cap = SystemParameters.WorkArea.Width;                 // 不超过工作区，避免窗口比屏还宽
        double target = Math.Max(812, Math.Min(need, cap));
        if (Math.Abs(MinWidth - target) <= 0.5) return;               // 收敛：值没变就不再触发重排
        MinWidth = target;
        // 窄图时最小宽会超过当前宽：撑到最小宽并（仅未显示时）重新居中，避免窗口偏右或编辑中跳动
        if (Width < target)
        {
            double grow = target - Width;
            Width = target;
            if (!IsLoaded) Left -= grow / 2;
        }
    }

    static System.Windows.Shapes.Rectangle Sep() => new()
    {
        Width = 1,
        Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
        Margin = new Thickness(5, 5, 5, 5),
    };

    StackPanel BuildToolsBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < ToolOrder.Length; i++)
        {
            var tool = ToolOrder[i];
            var b = IconBtn(FrameCapture.Core.ToolCatalog.Tools[i].Glyph, FrameCapture.Core.ToolCatalog.Tools[i].Tip);
            b.PreviewMouseLeftButtonDown += (sender2, e2) => { e2.Handled = true; SetTool(tool); };
            _toolBtns.Add(b);
            bar.Children.Add(b);
        }
        bar.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)), Margin = new Thickness(8, 6, 8, 6) });
        for (int i = 0; i < Swatches.Length; i++)
        {
            int idx = i;
            var inner = new System.Windows.Shapes.Ellipse
            {
                Width = 14, Height = 14, Fill = new SolidColorBrush(Swatches[idx]),
                // 40% 文字色环：Theme.Stroke 在浅色主题太淡，白色色块会融进白工具条（R28 浅色截图抓到）
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, Theme.Text.R, Theme.Text.G, Theme.Text.B)), StrokeThickness = 1,
            };
            var bd = new Border
            {
                Child = inner,
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(11),
                Background = Brushes.Transparent,
                BorderBrush = i == 0 ? new SolidColorBrush(Acc) : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = "颜色",
            };
            FrameCapture.Core.Fluent.ChipHover(bd);
            bd.MouseLeftButtonDown += (sender2, e2) => { _colorIdx = idx; RefreshSwatches(); RestyleSelected(CurColor, null); e2.Handled = true; };
            _swatchBtns.Add(bd);
            bar.Children.Add(bd);
        }
        // 自定义颜色：调色板入口（ColorDialog）+ 最近自定义色直接点选
        var pal = IconBtn("\uE790", "RGBA 调色板 (K)");
        pal.PreviewMouseLeftButtonDown += (_, e2) => { PickCustomColor(); e2.Handled = true; };
        pal.PreviewKeyDown += (_, ek) => { if (ek.Key is Key.Enter or Key.Space) { PickCustomColor(); ek.Handled = true; } };
        _paletteBtn = pal;
        bar.Children.Add(pal);
        _recentHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(_recentHost);
        RebuildRecentSwatches();
        bar.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)), Margin = new Thickness(8, 6, 8, 6) });
        foreach (var (w, lbl) in new[] { (0, "细"), (1, "中"), (2, "粗") }.Select((t, i) => (t.Item1, t.Item2)))
        {
            int idx = w;
            var b = IconBtn("", lbl + "笔画", 24);
            b.FontSize = 8 + idx * 3;
            b.PreviewMouseLeftButtonDown += (sender2, e2) => { _widthIdx = idx; RefreshWidths(); RestyleSelected(null, CurWidth); e2.Handled = true; };
            _widthBtns.Add(b);
            bar.Children.Add(b);
        }
        // 自定义线宽：⋯ 按钮 = 第 4 个宽度槽位，点开滑杆弹层（1..24px）
        var wMore = IconBtn("\uE712", "自定义线宽… (W)", 24);
        wMore.PreviewMouseLeftButtonDown += (_, e2) => { ShowWidthPopup(wMore); e2.Handled = true; };
        wMore.PreviewKeyDown += (_, ek) => { if (ek.Key is Key.Enter or Key.Space) { ShowWidthPopup(wMore); ek.Handled = true; } };
        _widthMoreBtn = wMore;
        _widthBtns.Add(wMore);
        bar.Children.Add(wMore);
        // 字号步进器（C3）：仅文字工具/选中文字时可见。A−/A+ 调字号——选中文字=改该文字（可撤销），文字工具下=定下次文字字号。
        _fontGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _fontGroup.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)), Margin = new Thickness(8, 6, 8, 6) });
        var fMinus = TextSizeBtn("A\u2212", "字号减小"); fMinus.Click += (_, _) => StepFontSize(-1);
        var fPlus = TextSizeBtn("A+", "字号增大"); fPlus.Click += (_, _) => StepFontSize(+1);
        _fontLabel = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center, MinWidth = 36, TextAlignment = TextAlignment.Center };
        _fontGroup.Children.Add(fMinus);
        _fontGroup.Children.Add(_fontLabel);
        _fontGroup.Children.Add(fPlus);
        bar.Children.Add(_fontGroup);
        bar.Children.Add(new System.Windows.Shapes.Rectangle { Width = 1, Fill = new SolidColorBrush(Color.FromArgb(Theme.Stroke.A, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)), Margin = new Thickness(8, 6, 8, 6) });
        var undo = IconBtn("", "撤销 (Ctrl+Z)");
        undo.Click += (_, _) => UndoAnn();
        _undoBtn = undo;
        bar.Children.Add(undo);
        var redo = IconBtn("", "重做 (Ctrl+Y)");
        redo.Click += (_, _) => RedoAnn();
        _redoBtn = redo;
        bar.Children.Add(redo);
        RefreshUndo();
        RefreshWidths(); // 默认宽度"中"的选中态当场可见（此前要等点击才出现）
        return bar;
    }

    Button? _undoBtn;
    Button? _redoBtn;
    void RefreshUndo()
    {
        // 双流合并能力：标注或裁剪任一可撤销/重做，按钮即亮（次序由 ActionOrder 裁决）
        bool canU = _anns.CanUndo || _order.CanUndoCrop, canR = _anns.CanRedo || _order.CanRedoCrop;
        SetGhost(_undoBtn, canU);
        SetGhost(_redoBtn, canR);
        // 步数进 tooltip（R45）：撤销到哪一步、还能撤几步，悬停即知
        int u = _anns.Count + _order.CropUndoCount, r = _anns.RedoCount + _order.CropRedoCount;
        if (_undoBtn != null) _undoBtn.ToolTip = canU ? $"撤销上一笔（可撤 {u} 步）  Ctrl+Z" : "撤销 (Ctrl+Z)";
        if (_redoBtn != null) _redoBtn.ToolTip = canR ? $"重做上一笔（可重做 {r} 步）  Ctrl+Y" : "重做 (Ctrl+Y)";
    }
    static void SetGhost(Button? b, bool on)
    {
        if (b == null || b.IsEnabled == on) return;
        b.IsEnabled = on;
        b.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(on ? 1.0 : 0.35,
            new System.Windows.Duration(TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.HoverMs)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }

    void RefreshSwatches()
    {
        for (int i = 0; i < _swatchBtns.Count; i++)
            _swatchBtns[i].BorderBrush = i == _colorIdx ? new SolidColorBrush(Acc) : Brushes.Transparent;
        // 自定义槽位选中 = 调色板反白（与宽度/工具同一选中语言）
        if (_paletteBtn != null) FrameCapture.Core.Fluent.HighlightBtn(_paletteBtn, _colorIdx == Swatches.Length);
    }

    void RefreshWidths()
    {
        // 与绘制工具同一选中语言：accent 实心 + 反白图标（初值在 BuildToolsBar 尾部即刷新，默认宽度可见高亮）
        for (int i = 0; i < _widthBtns.Count; i++)
            FrameCapture.Core.Fluent.HighlightBtn(_widthBtns[i], i == _widthIdx);
        UpdateFontStepper();
    }

    // ---------------- 自定义颜色 / 线宽 ----------------

    Button? _paletteBtn;
    Button? _widthMoreBtn;
    StackPanel? _recentHost;
    StackPanel? _fontGroup;   // 字号步进器（C3）：文字工具/选中文字时可见
    TextBlock? _fontLabel;
    Popup? _widthPop;
    Slider? _widthSlider;
    System.Windows.Shapes.Ellipse? _widthPreview;
    TextBlock? _widthVal;

    List<string> RecentColors => _settings.Current.CustomColors ?? new List<string>();

    void RebuildRecentSwatches()
    {
        if (_recentHost == null) return;
        _recentHost.Children.Clear();
        foreach (var hex in RecentColors.Take(4))
        {
            Color col;
            try { col = (Color)ColorConverter.ConvertFromString(hex); } catch { continue; }
            var inner = new System.Windows.Shapes.Ellipse
            {
                Width = 12, Height = 12, Fill = new SolidColorBrush(col),
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, Theme.Text.R, Theme.Text.G, Theme.Text.B)), StrokeThickness = 1,
            };
            var bd = new Border
            {
                Child = inner, Width = 20, Height = 20, Margin = new Thickness(2, 0, 0, 0),
                CornerRadius = new CornerRadius(10), Background = Brushes.Transparent,
                Cursor = Cursors.Hand, ToolTip = $"最近用色 {hex}",
            };
            FrameCapture.Core.Fluent.ChipHover(bd);
            string h = hex;
            bd.MouseLeftButtonDown += (sender2, e2) =>
            {
                _customColor = col; _colorIdx = Swatches.Length; RefreshSwatches(); RestyleSelected(CurColor, null); e2.Handled = true;
            };
            _recentHost.Children.Add(bd);
        }
    }

    void PickCustomColor()
    {
        // R54：系统 ColorDialog 退役 → 自绘 RGBA 调色板弹层（EditorWindow.ColorPick.cs，主题一致+就地+即时生效）
        if (_paletteBtn != null) ShowColorPicker(_paletteBtn);
    }

    void ShowWidthPopup(Button anchor)
    {
        if (_widthPop == null)
        {
            _widthSlider = new Slider { Minimum = 1, Maximum = 24, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 150 };
            FrameCapture.Core.Fluent.StyleSlider(_widthSlider);
            _widthVal = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _widthPreview = new System.Windows.Shapes.Ellipse { Fill = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            _widthSlider.ValueChanged += (_, e) =>
            {
                _customWidth = (float)e.NewValue;
                _widthIdx = Widths.Length; // 自定义槽位
                if (_widthVal != null) _widthVal.Text = $"{(int)e.NewValue}px";
                if (_widthPreview != null) { _widthPreview.Width = _widthPreview.Height = Math.Clamp(e.NewValue, 3, 20); }
                RefreshWidths();
            };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Children = { _widthSlider, _widthVal, _widthPreview } };
            _widthPop = new Popup
            {
                Child = FrameCapture.Core.Fluent.PopupCard(head, new Thickness(12, 9, 12, 9)),
                // Popup 从 PlacementTarget（图标按钮）继承 FontFamily=图标字体 → 数字会变豆腐，_widthVal 显式复位
                PlacementTarget = anchor,
                Placement = PlacementMode.Top,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
            };
            // R76：滑杆拖动只改默认值（实时预览），关闭弹层时一次性把最终粗细应用到选中项（单条撤销）
            _widthPop.Closed += (_, _) => RestyleSelected(null, CurWidth);
        }
        _widthSlider.Value = Math.Clamp(_customWidth, 1, 24);
        if (_widthVal != null) _widthVal.Text = $"{(int)_widthSlider.Value}px";
        if (_widthPreview != null) _widthPreview.Width = _widthPreview.Height = Math.Clamp(_widthSlider.Value, 3, 20);
        OpenPopSticky(_widthPop, this);
    }

    // OpenPopSticky 已上收 Fluent（R59，设置窗调色板共用）
    static void OpenPopSticky(Popup pop, UIElement hostEl) => FrameCapture.Core.Fluent.OpenPopSticky(pop, hostEl);
}
