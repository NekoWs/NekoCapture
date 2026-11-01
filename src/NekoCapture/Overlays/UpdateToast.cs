using System.Windows;
using System.Windows.Controls;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>右下角的更新确认与下载状态，显示期间保持界面服务存活。</summary>
internal sealed class UpdateToast : Window
{
    private readonly TextBlock _title, _detail;
    private readonly CheckBox _skip;
    private readonly ProgressBar _progress;
    private readonly Button _update, _cancel;
    private bool _closed;

    /// <summary>创建更新提示，不抢占当前应用焦点。</summary>
    /// <param name="current">当前版本。</param>
    /// <param name="version">可用版本。</param>
    /// <param name="update">点击更新的回调。</param>
    /// <param name="cancel">取消与跳过版本的回调。</param>
    internal UpdateToast(string current, string version, Action update, Action<bool> cancel)
    {
        Title = "NekoCapture Update";
        Fluent.BorderlessOverlay(this);
        var content = new StackPanel();
        _title = new TextBlock { Text = Loc.F(K.Updates.Available, version), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush };
        _detail = new TextBlock { Text = Loc.F(K.Updates.Versions, current, version), FontSize = 12, Foreground = Theme.TextDimBrush, Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap };
        _skip = Fluent.Ui(new CheckBox { Content = Loc.T(K.Updates.Skip), Foreground = Theme.TextBrush, Margin = new Thickness(0, 0, 0, 12) });
        _progress = Fluent.Ui(new ProgressBar { Foreground = Theme.AccentBrush, Background = Theme.TrackBgBrush, BorderThickness = new Thickness(0), Minimum = 0, Maximum = 100, Height = 5, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 14) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _cancel = Fluent.RoundButton(Loc.T(K.Common.Cancel), Theme.RowBg, Theme.Text, 16, true);
        _cancel.Margin = new Thickness(0, 0, 8, 0);
        _update = Fluent.RoundButton(Loc.T(K.Updates.Update), Theme.Accent, Fluent.OnAccentColor(Theme.Accent), 16, true);
        _update.Click += (_, _) => update();
        _cancel.Click += (_, _) => cancel(_skip.IsChecked == true);
        buttons.Children.Add(_cancel); buttons.Children.Add(_update);
        content.Children.Add(_title); content.Children.Add(_detail); content.Children.Add(_progress); content.Children.Add(_skip); content.Children.Add(buttons);
        var card = new Border { Width = 350, Background = Theme.CardBgBrush, BorderBrush = Fluent.CardEdge(), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = content };
        Content = card;
        void Position()
        {
            var work = SystemParameters.WorkArea;
            Left = Math.Max(work.Left, work.Right - (ActualWidth > 0 ? ActualWidth : 350) - 16);
            Top = Math.Max(work.Top, work.Bottom - (ActualHeight > 0 ? ActualHeight : 190) - 16);
        }
        Position(); SizeChanged += (_, _) => Position();
        SourceInitialized += (_, _) => Fluent.ExcludeFromCapture(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        Closed += (_, _) => _closed = true;
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }

    /// <summary>开始或更新下载进度。</summary>
    /// <param name="done">已下载字节数。</param>
    /// <param name="total">总字节数。</param>
    internal void SetProgress(long done, long total)
    {
        if (_closed) return;
        _title.Text = Loc.T(K.Updates.Downloading);
        _detail.Text = Loc.F(K.Updates.Progress, done / 1048576.0, total / 1048576.0);
        _skip.Visibility = _update.Visibility = Visibility.Collapsed;
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = false;
        _progress.Value = total > 0 ? 100.0 * done / total : 0;
    }

    /// <summary>显示等待或安装阶段。</summary>
    /// <param name="installing">是否正在启动安装器。</param>
    internal void SetWaiting(bool installing)
    {
        if (_closed) return;
        _title.Text = Loc.T(installing ? K.Updates.Installing : K.Updates.Waiting);
        _detail.Text = "";
        _progress.IsIndeterminate = true;
        _cancel.IsEnabled = !installing;
    }

    /// <summary>显示可重试的更新失败状态。</summary>
    internal void SetFailed()
    {
        if (_closed) return;
        _title.Text = Loc.T(K.Updates.Failed); _detail.Text = "";
        _progress.Visibility = Visibility.Collapsed;
        _skip.Visibility = _update.Visibility = Visibility.Visible;
        _update.Content = Loc.T(K.Updates.Retry);
        _cancel.IsEnabled = true;
    }
}
