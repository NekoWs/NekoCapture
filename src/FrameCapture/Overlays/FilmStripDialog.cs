using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>「生成胶片条」小窗——问「抽多少帧」「每多少帧取一次」两个参数，
/// 确定后由编辑器抽样渲染、直接复制剪贴板（不落盘）。参数写回 settings 记忆，下次打开沿用。
/// 取代旧的全局「自动生成 Agent 胶片条」设置：功能从后台默认行为变成编辑器里的显式按钮。</summary>
public sealed class FilmStripDialog : Window
{
    public int Frames { get; private set; }
    public int EveryN { get; private set; }

    public FilmStripDialog(Window owner, int frames, int everyN, int totalFrames)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        InputMethod.SetIsInputMethodEnabled(this, false);

        var card = new Border
        {
            Background = new SolidColorBrush(Theme.PanelBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16, 20, 16),
            Child = Build(frames, everyN, totalFrames),
        };
        Fluent.Elevate(card, blur: 26, depth: 7);
        Content = card;
        Fluent.PopIn(card, 0.9);
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    UIElement Build(int frames, int everyN, int totalFrames)
    {
        var root = new StackPanel { MinWidth = 320 };
        root.Children.Add(new TextBlock { Text = "生成胶片条", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Theme.Text) });
        root.Children.Add(new TextBlock
        {
            Text = $"从当前录制（含裁剪/标注，共 {totalFrames} 帧）抽样拼成一张逐帧图，确定后复制到剪贴板",
            FontSize = 12, Foreground = new SolidColorBrush(Theme.TextDim),
            Margin = new Thickness(0, 3, 0, 12), TextWrapping = TextWrapping.Wrap,
        });

        var fBox = NumBox(frames.ToString());
        var nBox = NumBox(everyN.ToString());
        root.Children.Add(ParamRow("抽帧数", fBox, "帧（2–48：步长取完还多则均匀稀释到此数）"));
        root.Children.Add(ParamRow("每 N 帧取一次", nBox, "帧（1 = 逐帧取；2 = 隔帧取）"));

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = ChipBtn("取消", accent: false);
        cancel.Click += (_, _) => { DialogResult = false; };
        var ok = ChipBtn("生成", accent: true);
        ok.Click += (_, _) => Accept(fBox, nBox, frames, everyN);
        btns.Children.Add(cancel);
        btns.Children.Add(ok);
        root.Children.Add(btns);

        // Enter=生成 / Esc=取消：小窗无需 Tab 到按钮
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Accept(fBox, nBox, frames, everyN); e.Handled = true; }
            else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        };
        Loaded += (_, _) => { fBox.Focus(); fBox.SelectAll(); };
        return root;
    }

    void Accept(TextBox fBox, TextBox nBox, int defFrames, int defEvery)
    {
        Frames = ClampParse(fBox.Text, defFrames, 2, 48);
        EveryN = ClampParse(nBox.Text, defEvery, 1, 60);
        DialogResult = true;
    }

    static int ClampParse(string? s, int fallback, int lo, int hi)
        => int.TryParse((s ?? "").Trim(), out int v) ? Math.Clamp(v, lo, hi) : Math.Clamp(fallback, lo, hi);

    static FrameworkElement ParamRow(string label, TextBox box, string unit)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock { Text = label, FontSize = 13, Foreground = new SolidColorBrush(Theme.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            Background = new SolidColorBrush(Theme.CardBg),
            Padding = new Thickness(6, 3, 6, 3),
            Child = box,
        };
        var unitTb = new TextBlock { Text = unit, FontSize = 11.5, Foreground = new SolidColorBrush(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(lbl, 0); Grid.SetColumn(border, 1); Grid.SetColumn(unitTb, 2);
        g.Children.Add(lbl); g.Children.Add(border); g.Children.Add(unitTb);
        return g;
    }

    static TextBox NumBox(string text) => new()
    {
        Text = text,
        Width = 46,
        FontSize = 13,
        TextAlignment = TextAlignment.Center,   // 数字居中（与设置页自定义输入同款）
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Foreground = new SolidColorBrush(Theme.Text),
        CaretBrush = new SolidColorBrush(Theme.Accent),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    Button ChipBtn(string text, bool accent)
    {
        var b = new Button
        {
            Content = text,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 13,
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(accent ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text),
            Background = new SolidColorBrush(accent ? Theme.Accent : Theme.CardBg),
            BorderBrush = new SolidColorBrush(accent ? Theme.Accent : Theme.Stroke),
        };
        Fluent.Springy(b, 1.05, 0.95);
        b.Template = ChromelessTemplate();
        return b;
    }

    static ControlTemplate ChromelessTemplate()
    {
        string xaml = """
            <ControlTemplate TargetType="Button"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                      BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6"
                      Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate>
            """;
        return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
    }
}
