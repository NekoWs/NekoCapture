using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Setup;

/// <summary>静默升级失败后的恢复界面，保留安装包并提供重试与诊断入口。</summary>
internal sealed class UpdateFailureWindow : Window
{
    private readonly TextBlock _reason;
    private readonly Button _retry, _close, _captionClose;
    private bool _busy;

    /// <summary>是否在当前恢复会话中成功完成安装。</summary>
    internal bool Completed { get; private set; }

    /// <summary>创建恢复窗口；重试在后台运行，成功后关闭窗口。</summary>
    /// <param name="failure">当前安装失败原因。</param>
    /// <param name="logPath">安装日志完整路径。</param>
    /// <param name="retry">返回失败原因，成功时返回 null 的安装操作。</param>
    internal UpdateFailureWindow(Exception failure, string logPath, Func<Exception?> retry)
    {
        Title = Loc.T(K.Setup.Failed);
        Width = 600; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Fluent.BorderlessNative(this);
        Background = Theme.WindowBgBrush;
        var body = new StackPanel { Margin = new Thickness(24, 6, 24, 22) };
        var caption = new DockPanel { Margin = new Thickness(10, 4, 4, 0), LastChildFill = true };
        _captionClose = Fluent.IconButton("\uE711", Loc.T(K.Common.Close), 34, 13);
        DockPanel.SetDock(_captionClose, Dock.Right); caption.Children.Add(_captionClose);
        caption.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Setup.UpgradeTitle), Foreground = Theme.TextDimBrush, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
        });
        _captionClose.Click += (_, _) => Close();
        body.Children.Add(new TextBlock { Text = Loc.T(K.Setup.Failed), FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
        _reason = new TextBlock { Foreground = Theme.TextBrush, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 12, 0, 18) };
        body.Children.Add(_reason);
        string installer = typeof(UpdateFailureWindow).Assembly.Location;
        var paths = new StackPanel();
        AddPath(K.Setup.InstallerFile, installer); AddPath(K.Setup.LogFile, logPath);
        body.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Theme.CardBgBrush, Padding = new Thickness(14, 12, 14, 6), Child = paths });
        var links = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        AddLink(K.Setup.OpenInstallerFolder, () => new ProcessStartInfo("explorer.exe", "/select," + InstallEngine.QuoteArgument(installer)) { UseShellExecute = true });
        AddLink(K.Setup.ViewLog, () => new ProcessStartInfo(logPath) { UseShellExecute = true });
        body.Children.Add(links);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        _close = Fluent.RoundButton(Loc.T(K.Common.Close), Theme.RowBg, Theme.Text, 18, true);
        _retry = Fluent.RoundButton(Loc.T(K.Setup.RetryUpgrade), Theme.Accent, Fluent.OnAccentColor(Theme.Accent), 18, true);
        _close.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(_close); actions.Children.Add(_retry); body.Children.Add(actions);
        _close.Click += (_, _) => Close();
        _retry.Click += async (_, _) =>
        {
            if (_busy) return;
            SetBusy(true);
            _reason.Text = Loc.T(K.Setup.Retrying);
            Exception? error;
            try { error = await Task.Run(retry); }
            catch (Exception ex) { error = ex; }
            SetBusy(false);
            if (error == null) { Completed = true; Close(); }
            else ShowReason(error);
        };
        var root = new DockPanel();
        DockPanel.SetDock(caption, Dock.Top); root.Children.Add(caption); root.Children.Add(body);
        Content = new Border { Background = Theme.WindowBgBrush, BorderBrush = Theme.StrokeSoftBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = root };
        Closing += (_, e) => e.Cancel = _busy;
        Fluent.InstallCaptionDrag(this, _captionClose);
        Fluent.FadeIn(this);
        ShowReason(failure);

        void AddPath(Str label, string path)
        {
            paths.Children.Add(new TextBlock { Text = Loc.T(label), Foreground = Theme.TextDimBrush, FontSize = 12 });
            paths.Children.Add(Fluent.Ui(new TextBox
            {
                Text = path, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = Theme.TextBrush, FontSize = 12,
                Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 10),
            }));
        }
        void AddLink(Str text, Func<ProcessStartInfo> start)
        {
            var button = Fluent.RoundButton(Loc.T(text), Theme.RowBg, Theme.Text, 12, true);
            button.Margin = new Thickness(0, 0, 8, 0);
            button.Click += async (_, _) =>
            {
                try { await Task.Run(() => { using var process = Process.Start(start()); }); }
                catch { _reason.Text = Loc.T(K.Setup.OpenFileFailed); }
            };
            links.Children.Add(button);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _retry.IsEnabled = _close.IsEnabled = _captionClose.IsEnabled = !busy;
    }

    private void ShowReason(Exception failure)
    {
        bool locked = false;
        for (Exception? error = failure; error != null; error = error.InnerException)
        {
            int code = error is Win32Exception native ? native.NativeErrorCode : error.HResult & 0xffff;
            if (code is 32 or 33) locked = true;
        }
        _reason.Text = Loc.T(locked ? K.Setup.FilesInUse : K.Setup.RecoveryHint);
    }
}
