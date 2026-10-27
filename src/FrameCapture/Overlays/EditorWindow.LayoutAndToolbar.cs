using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Encoding;

namespace FrameCapture.Overlays;

/// <summary>
/// EditorWindow partial：停靠式布局与面板/按钮构建、主题换肤、撤销重做按钮状态、颜色/线宽选择。
/// </summary>
public sealed partial class EditorWindow : Window
{

    #region 布局

    Button IconBtn(string glyph, string tip, double size = 28) =>
        Fluent.IconButton(glyph, tip, size, 13);

    /// <summary>工具条里的分组分隔线：一根居中的短细线。顶天立地的通高线会把一条工具条切成格子，
    /// 「这几枚是一组」靠断开加留白就说清了；颜色取软描边那一档——成排出现的线不该抢内容。</summary>
    /// <returns>一帧分隔线。</returns>
    static FrameworkElement BarDivider() => new System.Windows.Shapes.Rectangle
    {
        Width = 1,
        Height = 18,
        VerticalAlignment = VerticalAlignment.Center,
        Fill = Theme.StrokeSoftBrush,
        Margin = new Thickness(9, 0, 9, 0),
    };

    // 文字按钮（非图标字体）：字号步进器 A−/A+ 用，走同一 IconButton 模板保持悬停/按压手感一致
    Button TextSizeBtn(string text, string tip)
    {
        var b = new Button
        {
            Content = text, ToolTip = tip, Width = 26, Height = 26, Margin = new Thickness(1),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = Theme.TextBrush, Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, Template = Fluent.IconButtonTemplate(),
            Focusable = false,
        };
        return b;
    }

    // 钉到屏幕按钮用实心图钉 E840（招牌动作配招牌图钉）；「总在最前」用置顶箭头 E898，两枚字形不混淆
    Button PinBtn()
    {
        var b = IconBtn("\uE840", Loc.T(K.Editor.PinToScreen));
        b.Click += (_, _) => PinCurrentFrame();
        return b;
    }

    // 识别文字按钮（E8C1「A字」= 文字识别，与文本工具/复制明显区分）
    Button OcrBtn()
    {
        var b = IconBtn("\uE8C1", Loc.T(K.Common.RecognizeText));
        b.Click += (_, _) => OcrCurrentFrame();
        return b;
    }

    void BuildLayout()
    {
        // 停靠式布局：预览在上，播放面板（录制时常驻）与工具面板依次停靠其下——控件一律在画面外，不悬浮在画面上
        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _viewGrid.Children.Add(_view);
        _viewGrid.Children.Add(_canvas);
        // 预览底部居中轻提示浮层（默认隐藏，ShowSnack 淡入淡出）
        _snackText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI"),
            TextAlignment = TextAlignment.Center,
        };
        _snack = new Border
        {
            // 轻提示浮在图上而不是浮在界面上：底与字色固定成一组反色对，压在白文档还是黑终端上都读得清。
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
            // 画布四周固定中性暗底，明暗两套都一样：截图十张里六张是白底文档，
            // 底一亮，图的边界就化在背景里；工具条跟着主题走，画布跟着「图要看得清边界」走。
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
        Background = _mica ? Brushes.Transparent : Theme.PanelBgBrush,
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
        BorderThickness = new Thickness(0, 1, 0, 0),
        Padding = new Thickness(8, 2, 8, 2),
        Child = child,
    };

    /// <summary>上一次看到的深浅档：只有深浅互换才需要幕布，改强调色之类不必挡视线。</summary>
    bool _maskDark = Theme.IsDark;

    /// <summary>换色那一刻画面上的窗口底：幕布拿它当退场色，落新调色板之后就取不到了。</summary>
    Color _maskFrom = Theme.WindowBgSolid;

    /// <summary>主题切换前保存退场颜色。</summary>
    /// <param name="dark">重算之后的深浅档。</param>
    void OnPaletteChanging(bool dark)
    {
        if (dark != _maskDark) _maskFrom = Theme.WindowBgSolid;
    }

    void Editor_OnTheme()
    {
        // 深浅互换整片盖一层退场色再淡开：几十处颜色同帧跳变由这一层接着过渡。
        // 这一档下窗底直接落定——幕布已经盖住整窗，窗底再自己淡一遍只是白跑一段动画。
        bool flip = Theme.IsDark != _maskDark;
        _maskDark = Theme.IsDark;
        if (flip) Fluent.MaskFade(this, _maskFrom);

        // 背板在场时窗底保持透明：把 WindowBg 动画上去等于用一层平涂盖掉 Mica。
        // 背板色调由 Fluent.MicaBackdrop 挂在 Theme.Changed 上同步，这里无需参与。
        var bg = _mica ? null : Background as SolidColorBrush;
        if (bg != null)
        {
            if (flip) Background = new SolidColorBrush(Theme.WindowBg);
            else
            {
                var anim = new System.Windows.Media.Animation.ColorAnimation(Theme.WindowBg, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ThemeMs));
                var holder = new SolidColorBrush(bg.Color);
                Background = holder;
                holder.BeginAnimation(SolidColorBrush.ColorProperty, anim);
            }
        }
        _hud.Foreground = Theme.TextBrush;
        // 选中语言随行：工具/线宽的前景色随主题（选中项反白、未选中项 Theme.Text），否则换肤后文字不可读
        RefreshToolHighlight();
        RefreshWidths();
        foreach (var p in new[] { _playPanel, _toolPanel })
            if (p != null)
            {
                p.Background = _mica ? Brushes.Transparent : Theme.PanelBgBrush;
                p.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B));
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
            var bPrevSec = IconBtn("\uF8AC", Loc.T(K.Editor.BackOneSecond)); bPrevSec.Click += (_, _) => Step(-fps);
            var bPrev = IconBtn("\uF08D", Loc.T(K.Editor.PreviousFrame)); bPrev.Click += (_, _) => Step(-1);
            _bPlay = IconBtn("\uF5B0", Loc.T(K.Editor.PlayPauseSpace)); _bPlay.Click += (_, _) => TogglePlay();
            var bNext = IconBtn("\uF08F", Loc.T(K.Editor.NextFrame)); bNext.Click += (_, _) => Step(+1);
            var bNextSec = IconBtn("\uF8AD", Loc.T(K.Editor.ForwardOneSecond)); bNextSec.Click += (_, _) => Step(+fps);
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
            // HUD 定底宽右对齐：帧数/时间/裁剪段文本变宽不挤压时间轴列（否则布局整体抖动）。
            // MinWidth 而非 Width：罕见的「⚠丢帧」长警告仍完整可读，只是那种情况下才允许变宽。
            _hud.MinWidth = 210;
            _hud.TextAlignment = TextAlignment.Right;
            _hud.Margin = new Thickness(6, 0, 8, 0);
            Grid.SetColumn(_hud, 2);
            g.Children.Add(_hud);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var bSaveFrame = IconBtn("\uE105", Loc.T(K.Editor.SaveCurrentFrame)); bSaveFrame.Click += (_, _) => SaveFrame();
            var bCopyFrame = IconBtn("\uF413", Loc.T(K.Editor.CopyCurrentFrame)); bCopyFrame.Click += (_, _) => ClipboardService.SetFrame(ExportFrame(_index));
            var bPinFrame = PinBtn();
            var bOcrFrame = OcrBtn();
            var bSaveGif = IconBtn("\uE116", Loc.T(K.Editor.ExportRecording)); bSaveGif.Click += (_, _) => SaveRecording();
            var bAfter = IconBtn("\uE955", Loc.T(K.Editor.SaveAfterimageComposite)); bAfter.Click += (_, _) => SaveAfterimage();
            // 胶片条是显式功能按钮——小窗问抽帧参数，生成后直接复制剪贴板
            var bStrip = IconBtn("\uE8B9", Loc.T(K.Editor.FilmStrip)); bStrip.Click += (_, _) => GenerateFilmStrip();
            foreach (var b in new[] { bSaveFrame, bCopyFrame, bPinFrame, bOcrFrame, bSaveGif, bAfter, bStrip }) actions.Children.Add(b);
            actions.Children.Add(Sep());
            var bCancel = IconBtn("\uE711", Loc.T(K.Common.Cancel)); bCancel.Click += (_, _) => Fluent.CloseWithFade(this);
            var bOk = IconBtn("\uF78C", Loc.T(K.Editor.Done)); bOk.Click += (_, _) => Commit();
            actions.Children.Add(bCancel);
            actions.Children.Add(bOk);
            Fluent.HighlightBtn(bOk, true);
            _okBtns.Add(bOk); // 主按钮：accent 实心
            Grid.SetColumn(actions, 3);
            g.Children.Add(actions);

            _playPanel = Panel(g);
            Grid.SetRow(_playPanel, 1);
            root.Children.Add(_playPanel);
        }

        var tools = BuildToolsBar();
        _toolsBar = tools; // 留引用，供 FitMinWidthToToolbar 量宽
        if (!MultiFrame)
        {
            tools.Children.Add(Sep());
            var bSaveF = IconBtn("\uE105", Loc.T(K.Editor.SaveCurrentFrame)); bSaveF.Click += (_, _) => SaveFrame();
            var bCopyF = IconBtn("\uF413", Loc.T(K.Editor.CopyCurrentFrame)); bCopyF.Click += (_, _) => ClipboardService.SetFrame(ExportFrame(_index));
            var bPinF = PinBtn();
            var bOcrF = OcrBtn();
            var bOkF = IconBtn("\uF78C", Loc.T(K.Editor.Done)); bOkF.Click += (_, _) => Commit();
            var bX = IconBtn("\uE711", Loc.T(K.Common.Cancel)); bX.Click += (_, _) => Fluent.CloseWithFade(this);
            foreach (var b in new[] { bSaveF, bCopyF, bPinF, bOcrF, bOkF, bX }) tools.Children.Add(b);
            Fluent.HighlightBtn(bOkF, true);
            _okBtns.Add(bOkF); // 主按钮：accent 实心
        }
        tools.Children.Add(Sep());
        // 编辑器总在最前：置顶箭头 E898（向上箭头带顶栏，GlyphProbe 已验证），开关态靠高亮底色区分，与「钉到屏幕」图钉是两枚字形
        var bPin = IconBtn("\uE898", Loc.T(K.Editor.AlwaysOnTop));
        Fluent.HighlightBtn(bPin, _settings.Current.EditorTopmost);
        bPin.Click += (_, _) =>
        {
            bool on = !Topmost;
            Topmost = on;
            _settings.Mutate(s => s.EditorTopmost = on);
            Fluent.HighlightBtn(bPin, on);
            Trace.Log("editor topmost=" + on);
        };
        tools.Children.Add(bPin);
        var bSet = IconBtn("\uE713", Loc.T(K.Common.Settings)); bSet.Click += (_, _) => SettingsWindow.ShowOrFocus();
        var bHelp = IconBtn("\uE9CE", Loc.T(K.Editor.HelpAndShortcuts)); bHelp.Click += (_, _) => HelpWindow.ShowOrFocus();
        tools.Children.Add(bSet);
        tools.Children.Add(bHelp);
        _toolPanel = Panel(tools);
        _toolPanel.CornerRadius = new CornerRadius(0, 0, 8, 8); // 底角收口，与原生窗口圆角贴合
        // 工具条浮于画布之上：向上投影（Direction=90，与 Elevate 同族但朝上），暗底浓亮底淡。
        // 1px 顶边在深色画面上几乎融为一体，层次靠投影拉开。
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
    /// 让窗口 MinWidth 跟随底部工具条的实际宽度。工具条是居中 StackPanel，窗口比它窄时
    /// 两端工具会被裁掉。
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
        if (Math.Abs(MinWidth - target) <= 0.5) return;               // 收敛：值没变直接返回，不触发重排
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
            var b = IconBtn(ToolCatalog.Tools[i].Glyph, Loc.T(ToolCatalog.Tools[i].Tip));
            b.PreviewMouseLeftButtonDown += (sender2, e2) => { e2.Handled = true; SetTool(tool); };
            _toolBtns.Add(b);
            bar.Children.Add(b);
        }
        RefreshToolHighlight();   // 建完就刷一次：浏览态没人被刷，选中的是恢复来的上一把时立刻看得出
        bar.Children.Add(BarDivider());
        for (int i = 0; i < Swatches.Length; i++)
        {
            int idx = i;
            var inner = new System.Windows.Shapes.Ellipse
            {
                Width = 16, Height = 16, Fill = new SolidColorBrush(Swatches[idx]),
                // 40% 文字色环：Theme.Stroke 在浅色主题太淡，白色色块会融进白工具条
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, Theme.Text.R, Theme.Text.G, Theme.Text.B)), StrokeThickness = 1,
            };
            var bd = new Border
            {
                Child = inner,
                // 26 见方：22 的命中区低于 24 的目标尺寸下限，选色这种要点准的动作尤其吃力。
                // 色点跟着从 14 长到 16，选中环才继续贴着色点而不是浮在一圈空白外。
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(13),
                Background = Brushes.Transparent,
                BorderBrush = i == 0 ? new SolidColorBrush(Acc) : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = Loc.T(K.Editor.Color),
            };
            Fluent.ChipHover(bd);
            bd.MouseLeftButtonDown += (sender2, e2) => { _colorIdx = idx; RefreshSwatches(); RestyleSelected(CurColor, null); e2.Handled = true; };
            _swatchBtns.Add(bd);
            bar.Children.Add(bd);
        }
        // 自定义颜色：RGBA 调色板弹层入口 + 最近自定义色直接点选
        var pal = IconBtn("\uE790", Loc.T(K.Editor.RgbaPalette));
        pal.PreviewMouseLeftButtonDown += (_, e2) => { PickCustomColor(); e2.Handled = true; };
        pal.PreviewKeyDown += (_, ek) => { if (ek.Key is Key.Enter or Key.Space) { PickCustomColor(); ek.Handled = true; } };
        _paletteBtn = pal;
        bar.Children.Add(pal);
        _recentHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(_recentHost);
        RebuildRecentSwatches();
        bar.Children.Add(BarDivider());
        foreach (var (w, lbl) in new[] { (0, Loc.T(K.Editor.Thin)), (1, Loc.T(K.Editor.Medium)), (2, Loc.T(K.Editor.Bold)) }.Select((t, i) => (t.Item1, t.Item2)))
        {
            int idx = w;
            var b = IconBtn("", Loc.F(K.Editor.Stroke, lbl), 24);
            b.FontSize = 8 + idx * 3;
            b.PreviewMouseLeftButtonDown += (sender2, e2) => { _widthIdx = idx; RefreshWidths(); RestyleSelected(null, CurWidth); e2.Handled = true; };
            _widthBtns.Add(b);
            bar.Children.Add(b);
        }
        // 自定义线宽：⋯ 按钮 = 第 4 个宽度槽位，点开滑杆弹层（1..24px）
        var wMore = IconBtn("\uE712", Loc.T(K.Editor.CustomStrokeWidth), 24);
        wMore.PreviewMouseLeftButtonDown += (_, e2) => { ShowWidthPopup(wMore); e2.Handled = true; };
        wMore.PreviewKeyDown += (_, ek) => { if (ek.Key is Key.Enter or Key.Space) { ShowWidthPopup(wMore); ek.Handled = true; } };
        _widthMoreBtn = wMore;
        _widthBtns.Add(wMore);
        bar.Children.Add(wMore);
        // 字号步进器：仅文字工具/选中文字时可见。A−/A+ 调字号——选中文字=改该文字（可撤销），文字工具下=定下次文字字号。
        _fontGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _fontGroup.Children.Add(BarDivider());
        var fMinus = TextSizeBtn("A\u2212", Loc.T(K.Editor.SmallerText)); fMinus.Click += (_, _) => StepFontSize(-1);
        var fPlus = TextSizeBtn("A+", Loc.T(K.Editor.LargerText)); fPlus.Click += (_, _) => StepFontSize(+1);
        _fontLabel = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center, MinWidth = 36, TextAlignment = TextAlignment.Center };
        _fontGroup.Children.Add(fMinus);
        _fontGroup.Children.Add(_fontLabel);
        _fontGroup.Children.Add(fPlus);
        bar.Children.Add(_fontGroup);
        bar.Children.Add(BarDivider());
        var undo = IconBtn("", Loc.T(K.Editor.Undo));
        undo.Click += (_, _) => UndoAnn();
        _undoBtn = undo;
        bar.Children.Add(undo);
        var redo = IconBtn("", Loc.T(K.Editor.Redo));
        redo.Click += (_, _) => RedoAnn();
        _redoBtn = redo;
        bar.Children.Add(redo);
        RefreshUndo();
        RefreshWidths(); // 默认线宽挡位的选中态当场可见
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
        // 步数进 tooltip：撤销到哪一步、还能撤几步，悬停即知
        int u = _anns.Count + _order.CropUndoCount, r = _anns.RedoCount + _order.CropRedoCount;
        if (_undoBtn != null) _undoBtn.ToolTip = canU ? Loc.F(K.Editor.UndoTheLastEditSteps, u) : Loc.T(K.Editor.Undo);
        if (_redoBtn != null) _redoBtn.ToolTip = canR ? Loc.F(K.Editor.RedoTheLastEditSteps, r) : Loc.T(K.Editor.Redo);
    }
    static void SetGhost(Button? b, bool on)
    {
        if (b == null || b.IsEnabled == on) return;
        b.IsEnabled = on;
        b.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(on ? 1.0 : 0.35,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.HoverMs)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }

    void RefreshSwatches()
    {
        for (int i = 0; i < _swatchBtns.Count; i++)
            _swatchBtns[i].BorderBrush = i == _colorIdx ? new SolidColorBrush(Acc) : Brushes.Transparent;
        // 自定义槽位选中 = 调色板反白（与宽度/工具同一选中语言）
        if (_paletteBtn != null) Fluent.HighlightBtn(_paletteBtn, _colorIdx == Swatches.Length);
    }

    void RefreshWidths()
    {
        // 与绘制工具同一选中语言：accent 实心 + 反白图标（初值在 BuildToolsBar 尾部即刷新，默认宽度可见高亮）
        for (int i = 0; i < _widthBtns.Count; i++)
            Fluent.HighlightBtn(_widthBtns[i], i == _widthIdx);
        UpdateFontStepper();
    }
    #endregion

    #region 自定义颜色 / 线宽

    Button? _paletteBtn;
    Button? _widthMoreBtn;
    StackPanel? _recentHost;
    StackPanel? _fontGroup;   // 字号步进器：文字工具/选中文字时可见
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
                Cursor = Cursors.Hand, ToolTip = Loc.F(K.Editor.RecentColor, hex),
            };
            Fluent.ChipHover(bd);
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
        // 自绘 RGBA 调色板弹层（EditorWindow.ColorPick.cs，主题一致+就地+即时生效），不用系统 ColorDialog
        if (_paletteBtn != null) ShowColorPicker(_paletteBtn);
    }

    void ShowWidthPopup(Button anchor)
    {
        if (_widthPop == null)
        {
            _widthSlider = new Slider { Minimum = 1, Maximum = 24, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 150 };
            Fluent.StyleSlider(_widthSlider);
            _widthVal = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _widthPreview = new System.Windows.Shapes.Ellipse { Fill = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
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
                Child = Fluent.PopupCard(head, new Thickness(12, 9, 12, 9)),
                // Popup 从 PlacementTarget（图标按钮）继承 FontFamily=图标字体 → 数字会变豆腐，_widthVal 显式复位
                PlacementTarget = anchor,
                Placement = PlacementMode.Top,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
            };
            // 滑杆拖动只改默认值（实时预览），关闭弹层时一次性把最终粗细应用到选中项（单条撤销）
            _widthPop.Closed += (_, _) => RestyleSelected(null, CurWidth);
        }
        // 弹层元素是懒搭的；取成局部量，下面几处判空才真正覆盖到滑杆本身
        var slider = _widthSlider;
        if (slider == null) return;
        slider.Value = Math.Clamp(_customWidth, 1, 24);
        if (_widthVal != null) _widthVal.Text = $"{(int)slider.Value}px";
        if (_widthPreview != null) _widthPreview.Width = _widthPreview.Height = Math.Clamp(slider.Value, 3, 20);
        OpenPopSticky(_widthPop, this);
    }

    // OpenPopSticky 已上收 Fluent，设置窗调色板共用
    static void OpenPopSticky(Popup pop, UIElement hostEl) => Fluent.OpenPopSticky(pop, hostEl);
    #endregion

}
