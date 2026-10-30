using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        var button = Fluent.RoundButton(Loc.T(text), accent ? Theme.Accent : Theme.RowBg, Theme.Text, 12, true);
        button.Background = accent ? Theme.AccentBrush : Theme.RowBgBrush;
        button.Foreground = accent ? new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)) : Theme.TextBrush;
        if (accent) _repaint.Add(() => button.Foreground = new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent)));
        return button;
    }

    private void BuildAbout(StackPanel panel)
    {
        var product = new StackPanel { Margin = new Thickness(8, 12, 8, 14) };
        product.Children.Add(new TextBlock { Text = Loc.T(K.About.Product), FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
        product.Children.Add(new TextBlock { Text = Loc.F(K.About.Version, UpdateService.CurrentVersion), Foreground = Theme.TextDimBrush, FontSize = 12, Margin = new Thickness(0, 5, 0, 12) });
        product.Children.Add(new TextBlock { Text = Loc.T(K.About.Description), Foreground = Theme.TextBrush, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(product);
        panel.Children.Add(AboutUpdateOptions());
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 16, 8, 4) };
        var status = UiNote(""); status.Margin = new Thickness(8, 0, 8, 6); status.Visibility = Visibility.Collapsed;
        void Link(Str text, string suffix)
        {
            var button = AboutButton(text);
            button.Margin = new Thickness(0, 0, 8, 0);
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
        Link(K.About.Source, "");
        Link(K.About.License, "/blob/main/LICENSE");
        panel.Children.Add(links); panel.Children.Add(status);
        Cardify(panel);
    }
}
