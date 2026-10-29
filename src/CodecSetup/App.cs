using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Encoding;

namespace FrameCapture.CodecSetup;

/// <summary>需用户确认后才下载独立视频组件的安装窗口。</summary>
internal sealed class App : Application
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Loc.Apply(Loc.PrefSystem);
        Theme.Apply("system", "#4CC2FF");
        Fluent.ApplyGlobalStyles();
        if (args.Length == 4 && args[0] == "--preview")
        {
            Loc.Apply(args[2]);
            Theme.Apply(args[3], "#4CC2FF");
            var window = new ComponentWindow();
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(560, double.PositiveInfinity));
            content.Arrange(new Rect(content.DesiredSize));
            content.UpdateLayout();
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap(560,
                (int)Math.Ceiling(content.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(window.Background, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                context.DrawRectangle(new VisualBrush(content), null, new Rect(content.RenderSize));
            }
            image.Render(drawing);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using (var stream = File.Create(args[1])) png.Save(stream);
            window.Close();
            return;
        }
        app.Run(new ComponentWindow());
    }
}

/// <summary>展示组件来源、安装位置与可取消的下载进度。</summary>
internal sealed class ComponentWindow : Window
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 48 };
    private readonly ProgressBar _progress = new() { Height = 4, Minimum = 0, Maximum = 100 };
    private readonly Button _install;
    private readonly Button _close;
    private bool _busy;
    private bool _finished;
    private bool _closeWhenIdle;

    /// <summary>创建使用主程序主题与控件样式的组件安装窗口。</summary>
    internal ComponentWindow()
    {
        Title = "FrameCapture · " + Loc.T(K.Common.VideoComponent);
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Theme.WindowBg);
        Foreground = new SolidColorBrush(Theme.Text);
        Fluent.BorderlessNative(this);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Common.VideoComponentDescription), FontSize = 14, Margin = new Thickness(0, 12, 0, 12),
        });
        body.Children.Add(new TextBlock
        {
            Text = FfmpegComponentInfo.InstallDirectory, TextWrapping = TextWrapping.Wrap,
            FontSize = 12, Margin = new Thickness(0, 0, 0, 16),
        });
        _status.Text = Loc.F(K.CodecSetup.Notice, Math.Round(FfmpegComponentInfo.DownloadBytes / 1048576d));
        body.Children.Add(_status);
        body.Children.Add(_progress);
        var links = new WrapPanel { Margin = new Thickness(0, 14, 0, 12) };
        Link(links, Loc.T(K.CodecSetup.Source), FfmpegComponentInfo.SourceUrl);
        Link(links, Loc.T(K.CodecSetup.Build), FfmpegComponentInfo.BuildUrl);
        Link(links, Loc.T(K.CodecSetup.License), "https://www.gnu.org/licenses/lgpl-3.0.html");
        body.Children.Add(links);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _install = Fluent.RoundButton(Loc.T(K.Common.DownloadAndInstall), Theme.Accent, Fluent.InkOn(Theme.Accent, Theme.Text), 22, true);
        _install.Click += async (_, _) => await Install();
        _close = Fluent.RoundButton(Loc.T(K.Common.Cancel), Theme.CardBg, Theme.Text, 18, true);
        _close.Margin = new Thickness(10, 0, 0, 0);
        _close.Click += (_, _) => Close();
        buttons.Children.Add(_install);
        buttons.Children.Add(_close);
        body.Children.Add(buttons);
        Content = new Border { Background = Background, Padding = new Thickness(28), Child = body };
        Fluent.InstallCaptionDrag(this, _close);
        Fluent.FadeIn(this);
        Closing += (_, e) =>
        {
            if (!_busy) return;
            e.Cancel = true;
            _closeWhenIdle = true;
            _cancel.Cancel();
        };
        Closed += (_, _) => _cancel.Dispose();
    }

    private static void Link(Panel panel, string text, string url)
    {
        var button = Fluent.RoundButton(text, Theme.CardBg, Theme.Text, 10, true);
        button.Margin = new Thickness(0, 0, 6, 0);
        button.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        };
        panel.Children.Add(button);
    }

    private async Task Install()
    {
        if (_finished) { Close(); return; }
        if (_busy) return;
        _busy = true;
        _install.IsEnabled = false;
        try
        {
            var progress = new Progress<ComponentProgress>(value =>
            {
                _progress.Value = value.Percent;
                _status.Text = value.Verifying ? Loc.T(K.Common.ComponentVerifying)
                    : Loc.F(K.CodecSetup.Downloading, (int)value.Percent);
            });
            await ComponentInstaller.InstallAsync(progress, _cancel.Token);
            _status.Text = Loc.T(K.CodecSetup.Ready);
            _install.Content = Loc.T(K.Common.Close);
            _close.Visibility = Visibility.Collapsed;
            _finished = true;
        }
        catch (OperationCanceledException) { _status.Text = Loc.T(K.Common.ComponentCancelled); }
        catch (Exception ex)
        {
            _status.Text = Loc.T(K.CodecSetup.Failed);
            _status.ToolTip = ex.Message;
        }
        finally
        {
            _busy = false;
            _install.IsEnabled = true;
            if (_closeWhenIdle) Close();
        }
    }
}
