using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Core;
using FrameCapture.Services;
using FrameCapture.Updates;
using Trace = FrameCapture.Core.Trace;

namespace FrameCapture.Overlays;

/// <summary>关于页面展示产品版本、许可与项目链接。</summary>
public sealed partial class SettingsWindow
{
    private Button AboutButton(Str text, bool accent = false)
    {
        var button = Fluent.RoundButton(Loc.T(text), accent ? Theme.Accent : Theme.RowBg, Theme.Text, 12, false);
        button.Background = accent ? Theme.AccentBrush : Theme.RowBgBrush;
        button.Foreground = accent ? new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)) : Theme.TextBrush;
        if (accent) _repaint.Add(() => button.Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)));
        Fluent.ChipHover(button);
        return button;
    }

    private void BuildAbout(StackPanel panel)
    {
        var product = new StackPanel { Margin = new Thickness(8, 12, 8, 22) };
        var identity = new StackPanel { Orientation = Orientation.Horizontal };
        identity.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/FrameCapture;component/Assets/FrameCapture.png")),
            Width = 28, Height = 28, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        identity.Children.Add(new TextBlock { Text = Loc.T(K.About.Product), FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
        product.Children.Add(identity);
        product.Children.Add(new TextBlock { Text = Loc.T(K.About.Description), Foreground = Theme.TextBrush, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(38, 6, 0, 0) });
        product.Children.Add(new TextBlock { Text = Loc.F(K.About.Version, UpdateService.CurrentVersion), Foreground = Theme.TextDimBrush, FontSize = 12, Margin = new Thickness(38, 5, 0, 0) });
        panel.Children.Add(product);
        panel.Children.Add(AboutCard(AboutUpdateOptions()));
        var links = new StackPanel();
        links.Children.Add(new TextBlock
        {
            Text = Loc.T(K.About.Project), FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush, Margin = new Thickness(2, 0, 2, 8),
        });
        var status = UiNote(""); status.Margin = new Thickness(2, 8, 2, 0); status.Visibility = Visibility.Collapsed;
        void Link(Str text, string detail, string suffix)
        {
            var button = AboutButton(text);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(12, 10, 12, 10);
            button.Margin = new Thickness(0, 3, 0, 3);
            var template = Fluent.RoundedButtonTemplate(false);
            template.VisualTree.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            template.VisualTree.FirstChild.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            button.Template = template;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = Loc.T(text), Foreground = Theme.TextBrush, FontSize = 13 });
            label.Children.Add(new TextBlock { Text = detail, Foreground = Theme.TextDimBrush, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(label);
            var arrow = Fluent.Glyph("\uE76C", 12, Theme.TextDimBrush);
            arrow.VerticalAlignment = VerticalAlignment.Center; arrow.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(arrow, 1); row.Children.Add(arrow);
            button.Content = row;
            System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(text));
            button.Click += async (_, _) =>
            {
                try
                {
                    await BackgroundWork.Run(() =>
                    {
                        using var process = Process.Start(new ProcessStartInfo("https://github.com/" + UpdateRelease.Repository + suffix) { UseShellExecute = true });
                        return true;
                    });
                    status.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex) { Trace.Log("about link: " + ex.Message); status.Text = Loc.T(K.About.LinkFailed); status.Visibility = Visibility.Visible; }
            };
            links.Children.Add(button);
        }
        Link(K.About.Source, UpdateRelease.Repository, "");
        Link(K.About.OpenSourceLicense, Loc.T(K.About.License), "/blob/main/LICENSE");
        links.Children.Add(status);
        panel.Children.Add(AboutCard(links));
    }

    private static Border AboutCard(FrameworkElement content) => new()
    {
        Background = Theme.CardBgBrush, BorderBrush = Theme.StrokeBrush, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Child = content,
    };
}
