using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>启动阶段的配置提示：标题、正文与一段可复制的文件路径，等用户确认后关闭。</summary>
/// <remarks>出现在主窗口之前，样式自持：实底窗口 + 系统圆角，正文与按钮用界面同一套颜色与按钮模板。</remarks>
public sealed class ConfigNoticeWindow : Window
{
    const double W = 432;

    /// <summary>弹一条配置提示并等用户按下确认。</summary>
    /// <param name="title">标题，一句话点明发生了什么。</param>
    /// <param name="body">正文，说明这次会怎么处理。</param>
    /// <param name="detail">可选的文件路径，只读但可选中复制。</param>
    public static void Show(string title, string body, string? detail = null)
        => new ConfigNoticeWindow(title, body, detail).ShowDialog();

    ConfigNoticeWindow(string title, string body, string? detail)
    {
        Title = "NekoCapture";
        // 实底窗口而不是透明浮窗：这张提示出现在启动阶段，先于任何主窗口，
        // 它必须无条件看得见；圆角与裁剪交给系统，窗口自身就是那张卡片。
        WindowStyle = WindowStyle.None;
        // 提示可能出现在主题应用之前（配置读不动的那条路）：先把主题落到当前设置或默认值上，
        // 否则卡片拿到的是还没上色的透明画刷，窗口看起来就是一块黑底。
        if (SettingsService.Shared?.Current is { } applied) applied.ApplyPalette();
        else Theme.Apply("system", "");
        Background = Theme.CardBgBrush;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        ShowActivated = true;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SourceInitialized += (_, _) =>
        {
            Native.Win32.SetRoundedCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        };

        var stack = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new TextBlock
        {
            Text = "\uE946", FontFamily = new FontFamily(Fluent.IconFontChain), FontSize = 18,
            Foreground = new SolidColorBrush(Theme.Accent), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        head.Children.Add(new TextBlock
        {
            Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush,
            FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxWidth = W - 90,
        });
        stack.Children.Add(head);
        stack.Children.Add(new TextBlock
        {
            Text = body, FontSize = 13, Foreground = Theme.TextDimBrush, FontFamily = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap, LineHeight = 20, MaxWidth = W - 44,
        });
        if (!string.IsNullOrEmpty(detail))
        {
            var path = Fluent.Ui(new TextBox
            {
                Text = detail, IsReadOnly = true, Margin = new Thickness(0, 12, 0, 0),
                FontSize = 12, Padding = new Thickness(8, 6, 8, 6), BorderThickness = new Thickness(1),
                Background = Theme.PanelBgBrush, Foreground = Theme.TextDimBrush,
                BorderBrush = Theme.StrokeFieldBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = W - 44,
            });
            path.GotKeyboardFocus += (_, _) => path.SelectAll();
            stack.Children.Add(path);
        }
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var ok = Fluent.Ui(new Button
        {
            Content = Loc.T(K.Common.Confirm), Padding = new Thickness(16, 6, 16, 6), FontSize = 13,
            Cursor = Cursors.Hand, BorderThickness = new Thickness(1), IsDefault = true, IsCancel = true,
            Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)),
            Background = new SolidColorBrush(Theme.Accent), BorderBrush = new SolidColorBrush(Theme.Accent),
        });
        Fluent.Springy(ok);
        ok.Template = Fluent.RoundedButtonTemplate(false);
        ok.Click += (_, _) => Close();
        actions.Children.Add(ok);
        stack.Children.Add(actions);

        var card = new Border { Width = W, Child = stack };
        // 先量后定尺寸：WindowStyle=None 的窗口交给 SizeToContent 自己量，
        // 首次呈现会停在未绘制的一帧（整窗黑底）；量出来再写死宽高就正常。
        card.Measure(new Size(W, double.PositiveInfinity));
        Width = W;
        Height = Math.Ceiling(card.DesiredSize.Height);
        Content = card;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Escape or Key.Enter)) return;
            e.Handled = true;
            Close();
        };
    }
}
