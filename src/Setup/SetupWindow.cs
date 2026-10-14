using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Setup;

/// <summary>
/// 引导器唯一窗口。深浅色、圆角、动效全部来自主程序那套源文件，没有第二份样式定义。
/// </summary>
internal sealed class SetupWindow : Window
{
    /// <summary>窗口启动后的行为。</summary>
    internal enum Mode { Idle, Install, Uninstall, Finished }

    private readonly Border _card = new();
    private readonly TextBlock _cardText = new();
    private readonly TextBlock _log = new();
    private readonly Button _primary;
    private readonly Button _secondary;
    private Mode _mode;
    private bool _busy;

    /// <summary>建窗并按模式准备首屏。</summary>
    /// <param name="mode">启动后的行为。</param>
    internal SetupWindow(Mode mode)
    {
        _mode = mode;
        Title = "FrameCapture 安装";
        Width = 560;
        Height = 336;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Theme.WindowBg);
        Content = BuildBody(out var closeBtn, out _primary, out _secondary);
        Fluent.BorderlessNative(this);
        Fluent.InstallCaptionDrag(this, closeBtn);
        Fluent.FadeIn(this);

        RefreshRuntimeCard();
        Loaded += (_, _) =>
        {
            if (_mode == Mode.Install) StartInstall();
            else if (_mode == Mode.Uninstall) StartUninstall();
        };
    }

    /// <summary>组装标题栏、状态卡片、日志区与按钮。</summary>
    /// <param name="closeBtn">标题栏关闭按钮，供拖拽辅助函数排除命中。</param>
    /// <param name="primary">主按钮，交回构造函数做字段赋值。</param>
    /// <param name="secondary">次按钮，交回构造函数做字段赋值。</param>
    /// <returns>窗口根元素。</returns>
    private FrameworkElement BuildBody(out Button closeBtn, out Button primary, out Button secondary)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var bar = new Grid { Background = new SolidColorBrush(Theme.ChromeBg) };
        bar.Children.Add(new TextBlock
        {
            Text = "FrameCapture " + VersionText(),
            FontSize = 12,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Theme.TextDim),
        });
        closeBtn = Fluent.IconButton("\uE711", "关闭", 34, 13);
        closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
        closeBtn.Click += (_, _) => Close();
        bar.Children.Add(closeBtn);
        Grid.SetRow(bar, 0);
        root.Children.Add(bar);

        var body = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
        body.Children.Add(new TextBlock
        {
            Text = "安装 FrameCapture",
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
        });
        body.Children.Add(new TextBlock
        {
            Text = @"目标位置  C:\Program Files\FrameCapture（所有用户可用）",
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 16),
            Foreground = new SolidColorBrush(Theme.TextDim),
        });

        _cardText.FontSize = 13;
        _card.Background = new SolidColorBrush(Theme.CardBg);
        _card.CornerRadius = new CornerRadius(10);
        _card.Padding = new Thickness(16, 12, 16, 12);
        _card.Child = _cardText;
        Fluent.Elevate(_card);
        body.Children.Add(_card);

        _log.FontSize = 12;
        _log.TextWrapping = TextWrapping.Wrap;
        _log.Margin = new Thickness(2, 14, 0, 0);
        // 固定高度而非 MaxHeight：进度文字出现时窗口不跳高，预留带就摆在卡片与按钮之间
        _log.Height = 76;
        _log.VerticalAlignment = VerticalAlignment.Top;
        _log.Foreground = new SolidColorBrush(Theme.TextDim);
        body.Children.Add(_log);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 18, 0, 0),
        };
        primary = Fluent.RoundButton("安装", Theme.Accent, Theme.WindowBg, 22, true);
        primary.Click += (_, _) => OnPrimary();
        buttons.Children.Add(primary);

        secondary = Fluent.RoundButton("取消", Theme.CardBg, Theme.Text, 18, true);
        secondary.Margin = new Thickness(10, 0, 0, 0);
        secondary.Click += (_, _) => Close();
        buttons.Children.Add(secondary);
        body.Children.Add(buttons);

        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    /// <summary>按本机运行时状态刷新卡片文案与主按钮措辞。</summary>
    private void RefreshRuntimeCard()
    {
        var ok = DotnetRuntime.Satisfied(out var found);
        if (ok)
        {
            _cardText.Text = "已检测到 .NET Desktop Runtime " + found;
            _cardText.Foreground = new SolidColorBrush(Theme.Text);
        }
        else
        {
            _cardText.Text = found == null
                ? "未检测到 .NET Desktop Runtime，安装时会从微软官网下载（约 56 MB）"
                : "已装的 .NET Desktop Runtime " + found + " 版本过低，安装时会下载所需版本";
            _cardText.Foreground = new SolidColorBrush(Theme.Accent);
        }
    }

    /// <summary>主按钮：空闲态申请提权续跑，提权态直接干活。</summary>
    private void OnPrimary()
    {
        if (_busy) return;
        if (_mode == Mode.Idle)
        {
            if (App.Elevated) { StartInstall(); return; }
            if (!App.RelaunchElevated("--install"))
            {
                Log("已取消：安装需要管理员权限");
                return;
            }
            Log("已在管理员窗口中继续，本窗口即将关闭");
            Close();
            return;
        }
        if (_mode == Mode.Finished) { LaunchApp(); Close(); }
    }

    /// <summary>提权实例：先补运行时，再落文件与登记。</summary>
    private async void StartInstall()
    {
        if (_busy) return;
        _busy = true;
        _primary.IsEnabled = false;
        try
        {
            if (!DotnetRuntime.Satisfied(out _))
            {
                Log("正在下载 .NET Desktop Runtime…");
                var tmp = Path.Combine(Path.GetTempPath(), "fc-runtime.exe");
                var prog = new Progress<(long Done, long Total)>(p =>
                {
                    var text = p.Total > 0
                        ? "正在下载 .NET Desktop Runtime… " + (p.Done / 1048576) + " / " + (p.Total / 1048576) + " MB"
                        : "正在下载 .NET Desktop Runtime… " + (p.Done / 1048576) + " MB";
                    Log(text);
                });
                var got = await Task.Run(() => RuntimeSetup.DownloadAsync(tmp, prog, CancellationToken.None).GetAwaiter().GetResult());
                if (!got)
                {
                    Log("下载失败。手动下载后重试：" + RuntimeSetup.DownloadPage);
                    _primary.IsEnabled = true;
                    _busy = false;
                    return;
                }
                Log("正在安装运行时…");
                var code = await Task.Run(() => RuntimeSetup.RunInstaller(tmp));
                TryDelete(tmp);
                if (code != 0 && code != 3010)
                {
                    Log("运行时安装失败，退出码 " + code + "。可手动安装：" + RuntimeSetup.DownloadPage);
                    _primary.IsEnabled = true;
                    _busy = false;
                    return;
                }
                RefreshRuntimeCard();
            }

            Log("正在安装主程序…");
            var ok = await Task.Run(() => InstallEngine.Install(new Progress<string>(Log)));
            if (ok)
            {
                Log("安装完成");
                _primary.IsEnabled = true;
                _primary.Content = "启动 FrameCapture";
                _mode = Mode.Finished;
            }
            else
            {
                _primary.IsEnabled = true;
                _busy = false;
            }
        }
        catch (Exception ex)
        {
            Log("失败：" + ex.Message);
            _primary.IsEnabled = true;
            _busy = false;
        }
    }

    /// <summary>提权实例：卸载。自身在临时副本上运行，才能删掉安装目录里那份。</summary>
    private async void StartUninstall()
    {
        if (_busy) return;
        _busy = true;
        _primary.IsEnabled = false;
        _primary.Content = "卸载";
        try
        {
            if (!App.Elevated)
            {
                if (!App.RelaunchElevated("--uninstall")) { Log("已取消：卸载需要管理员权限"); return; }
                Close();
                return;
            }

            var inside = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)
                         == InstallEngine.InstallDir;
            if (inside)
            {
                var copy = App.CopySelfToTemp();
                if (copy == null) { Log("无法准备卸载副本"); return; }
                Process.Start(new ProcessStartInfo(copy, "--uninstall --from-temp") { UseShellExecute = false });
                Close();
                return;
            }

            var ok = await Task.Run(() => InstallEngine.Uninstall(new Progress<string>(Log)));
            _primary.IsEnabled = true;
            _primary.Content = ok ? "完成" : "关闭";
            if (ok) _mode = Mode.Finished;
        }
        catch (Exception ex)
        {
            Log("失败：" + ex.Message);
            _primary.IsEnabled = true;
        }
    }

    /// <summary>启动装好的主程序。经 explorer 转一手，避免主程序继承安装器的管理员令牌。</summary>
    private static void LaunchApp()
    {
        var exe = Path.Combine(InstallEngine.InstallDir, InstallEngine.AppName + ".exe");
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = true }); }
        catch { /* 启动失败不影响安装结果 */ }
    }

    /// <summary>追加一行日志，跨线程安全。</summary>
    /// <param name="text">要显示的一行。</param>
    private void Log(string text)
    {
        Dispatcher.Invoke(() => _log.Text = (_log.Text.Length == 0 ? "" : _log.Text + "\n") + text);
    }

    /// <summary>程序集版本，三段，与 csproj 的 Version 同源。</summary>
    /// <returns>形如 0.9.0 的版本文本。</returns>
    private static string VersionText()
    {
        var v = typeof(SetupWindow).Assembly.GetName().Version;
        return v == null ? "0.0.0" : v.ToString(3);
    }

    /// <summary>尽力删除文件。</summary>
    /// <param name="path">目标路径。</param>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 残留交给系统临时目录清理 */ }
    }
}
