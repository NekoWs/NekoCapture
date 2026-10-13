using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>OCR 结果预览窗（可选，设置「识别后显示结果窗」开启时弹出）。
/// 显示识别文本（可编辑、可部分选中复制），底部「复制全部」+ 字数统计。
/// 非模态——编辑器仍可继续操作；Esc / 关闭钮淡出关闭。</summary>
public sealed class OcrResultWindow : Window
{
    /// <summary>弹出识别结果窗（非模态）：新建一窗、显示并激活；不阻塞调用方。</summary>
    /// <param name="text">识别文本，原样进可编辑框（用户可编辑、部分选中复制）。</param>
    public static void Show(string text)
    {
        var w = new OcrResultWindow(text);
        w.Show();
        w.Activate();
    }

    /// <summary>消息模式：无结果窗可展示时（未识别到文字 / 未装语言包 / 已复制轻确认）用同一窗体给一句可读反馈。
    /// 与结果窗共用外壳与淡入淡出，保证「识别文字」功能只有一个可截图审查的反馈面。</summary>
    /// <param name="msg">消息文本，居中一句展示，无编辑框与复制钮。</param>
    public static void ShowMessage(string msg)
    {
        var w = new OcrResultWindow(msg, message: true);
        w.Show();
        w.Activate();
    }

    OcrResultWindow(string text, bool message = false)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "FrameCapture OCR";
        Width = message ? 400 : 500;
        Height = message ? 210 : 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Theme.WindowBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.5 },
        };
        var dock = new DockPanel { LastChildFill = true };

        var (bar, close) = Fluent.TitleBar("\uE8C1", "识别文字");
        close.Click += (_, _) => Fluent.CloseWithFade(this);
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);

        if (message)
        {
            // 消息模式：居中一句提示，无编辑框/字数/复制钮。
            dock.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 14,
                Foreground = new SolidColorBrush(Theme.Text),
                FontFamily = new FontFamily("Segoe UI"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(28, 10, 28, 18),
            });
            root.Child = dock;
            Content = root;
            Fluent.InstallCaptionDrag(this, close);
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Fluent.CloseWithFade(this); };
            Fluent.PopupEnter(this);
            return;
        }

        // 底部：字数（左）+ 复制全部（右）
        var bottom = new Grid { Margin = new Thickness(14, 6, 14, 12) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var count = new TextBlock
        {
            Text = $"{text.Length} 字",
            FontSize = 12,
            Foreground = new SolidColorBrush(Theme.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
        };
        bottom.Children.Add(count);

        var copyBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Theme.Accent),
            Padding = new Thickness(14, 6, 14, 6),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var copyLabel = new TextBlock
        {
            Text = "复制全部",
            FontSize = 13,
            Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        copyBox.Child = copyLabel;
        Fluent.ChipHover(copyBox, 1.05);   // 悬停只改底色带过渡（与全站按钮同款）
        Grid.SetColumn(copyBox, 1);
        bottom.Children.Add(copyBox);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);

        var box = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Theme.PanelBg),
            Foreground = new SolidColorBrush(Theme.Text),
            CaretBrush = new SolidColorBrush(Theme.Text),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            FontSize = 13,
            Padding = new Thickness(10, 8, 6, 8),
            Margin = new Thickness(14, 10, 14, 4),
        };
        dock.Children.Add(box); // 填充剩余空间

        root.Child = dock;
        Content = root;
        Fluent.InstallCaptionDrag(this, close);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Fluent.CloseWithFade(this); };

        copyBox.MouseLeftButtonUp += (_, _) =>
        {
            try { Clipboard.SetText(box.Text); } catch { }
            copyLabel.Text = "已复制";
        };
        Loaded += (_, _) => { box.Focus(); box.SelectAll(); box.ScrollToHome(); };
        Fluent.PopupEnter(this);   // 入场淡入（与退场 CloseWithFade 对称；分层窗=淡入，同 HelpWindow）
    }
}
