using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>编辑器颜色、线宽、字号及撤销状态。</summary>
public sealed partial class EditorWindow : Window
{
    StackPanel? _colorOptions, _strokeOptions;

    void BuildStyleOptions(WrapPanel bar)
    {
        _colorOptions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 12, 0) };
        _strokeOptions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 8, 0) };
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
                Child = inner, Focusable = true,
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
            bd.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                _colorIdx = idx; RefreshSwatches(); RestyleSelected(CurColor, null); e.Handled = true;
            };
            bd.GotKeyboardFocus += (_, _) => bd.BorderBrush = new SolidColorBrush(Acc);
            bd.LostKeyboardFocus += (_, _) => RefreshSwatches();
            _swatchBtns.Add(bd);
            _colorOptions.Children.Add(bd);
        }
        // 自定义颜色：RGBA 调色板弹层入口 + 最近自定义色直接点选
        var pal = IconBtn("\uE790", Loc.T(K.Editor.RgbaPalette));
        pal.Click += (_, _) => PickCustomColor();
        _paletteBtn = pal;
        _colorOptions.Children.Add(pal);
        _recentHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _colorOptions.Children.Add(_recentHost);
        RebuildRecentSwatches();
        foreach (var (w, lbl) in new[] { (0, Loc.T(K.Editor.Thin)), (1, Loc.T(K.Editor.Medium)), (2, Loc.T(K.Editor.Bold)) }.Select((t, i) => (t.Item1, t.Item2)))
        {
            int idx = w;
            var b = IconBtn("", Loc.F(K.Editor.Stroke, lbl), 24);
            b.FontSize = 8 + idx * 3;
            b.Width = b.Height = 30;
            b.Click += (_, _) => { _widthIdx = idx; RefreshWidths(); RestyleSelected(null, CurWidth); };
            _widthBtns.Add(b);
            _strokeOptions.Children.Add(b);
        }
        // 自定义线宽：⋯ 按钮 = 第 4 个宽度槽位，点开滑杆弹层（1..24px）
        var wMore = IconBtn("\uE712", Loc.T(K.Editor.CustomStrokeWidth), 24);
        wMore.Width = wMore.Height = 30;
        wMore.Click += (_, _) => ShowWidthPopup(wMore);
        _widthMoreBtn = wMore;
        _widthBtns.Add(wMore);
        _strokeOptions.Children.Add(wMore);
        // 字号步进器：仅文字工具/选中文字时可见。A−/A+ 调字号——选中文字=改该文字（可撤销），文字工具下=定下次文字字号。
        _fontGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var fMinus = TextSizeBtn("A\u2212", Loc.T(K.Editor.SmallerText)); fMinus.Click += (_, _) => StepFontSize(-1);
        var fPlus = TextSizeBtn("A+", Loc.T(K.Editor.LargerText)); fPlus.Click += (_, _) => StepFontSize(+1);
        _fontLabel = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center, MinWidth = 36, TextAlignment = TextAlignment.Center };
        _fontGroup.Children.Add(fMinus);
        _fontGroup.Children.Add(_fontLabel);
        _fontGroup.Children.Add(fPlus);
        bar.Children.Add(_colorOptions);
        bar.Children.Add(_strokeOptions);
        bar.Children.Add(_fontGroup);
        RefreshWidths();
    }

    Button? _undoBtn;
    Button? _redoBtn;
    void RefreshUndo()
    {
        // 双流合并能力：标注或裁剪任一可撤销/重做，按钮即亮（次序由 ActionOrder 裁决）
        bool canU = !_encoding && (_anns.CanUndo || _order.CanUndoCrop), canR = !_encoding && (_anns.CanRedo || _order.CanRedoCrop);
        SetGhost(_undoBtn, canU);
        SetGhost(_redoBtn, canR);
        if (_redoBtn != null) _redoBtn.ToolTip = CommandTip("redo", K.Editor.Redo);
        if (_undoBtn != null) _undoBtn.ToolTip = CommandTip("undo", K.Editor.Undo);
        UpdateEditorStatus();
        RefreshToolOptions();
    }
    static void SetGhost(Button? button, bool enabled)
    {
        if (button != null) button.IsEnabled = enabled;
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
        // 工具与粗细使用相同的强调色选中态。
        for (int i = 0; i < _widthBtns.Count; i++)
            Fluent.HighlightBtn(_widthBtns[i], i == _widthIdx);
        UpdateFontStepper();
    }


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
                Child = inner, Focusable = true, Width = 26, Height = 26, Margin = new Thickness(2, 0, 0, 0),
                CornerRadius = new CornerRadius(13), Background = Brushes.Transparent,
                Cursor = Cursors.Hand, ToolTip = Loc.F(K.Editor.RecentColor, hex),
            };
            Fluent.ChipHover(bd);
            string h = hex;
            bd.MouseLeftButtonDown += (sender2, e2) =>
            {
                _customColor = col; _colorIdx = Swatches.Length; RefreshSwatches(); RestyleSelected(CurColor, null); e2.Handled = true;
            };
            bd.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                _customColor = col; _colorIdx = Swatches.Length; RefreshSwatches(); RestyleSelected(CurColor, null); e.Handled = true;
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
            _widthSlider = Fluent.Ui(new Slider { Minimum = 1, Maximum = 24, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 150 });
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
                Placement = PlacementMode.Bottom,
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

}
