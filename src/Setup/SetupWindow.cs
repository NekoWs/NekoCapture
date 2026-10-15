using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// <summary>目标位置那一行。换过位置要就地改写，用户得看见自己要装去哪。</summary>
    private readonly TextBlock _targetText = new();
    /// <summary>「更改」链接。卸载形态没有可换的东西，因此可空。</summary>
    private TextBlock? _change;
    /// <summary>日志的可视窗口。预留带只有 76 高，进度写到后面必须滚到底，最新一行才看得见。</summary>
    private readonly ScrollViewer _logBox = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        Height = 76,
        Margin = new Thickness(2, 14, 0, 0),
    };
    private readonly Button _primary;
    private readonly Button _secondary;
    private Mode _mode;
    private bool _busy;
    private bool _launchOnFinish;   // 收尾态点主按钮是否顺带启动主程序：卸载完程序已不在，不能启动
    readonly bool _mica;   // 背板在场才透明，缺席必须平涂，否则安装器变成黑窗

    /// <summary>建窗并按模式准备首屏。</summary>
    /// <param name="mode">启动后的行为。</param>
    internal SetupWindow(Mode mode)
    {
        _mode = mode;
        var uninstall = mode == Mode.Uninstall;
        Title = uninstall ? "FrameCapture 卸载" : "FrameCapture 安装";
        Width = 560;
        // 高度跟着内容走：缺运行时的那台机器上多一张卡片，装好了的机器不留一截空白。
        // 宽度固定，进度区高度固定，因此装/卸过程中不会再变。
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        // 背板要在 BorderlessNative 之后申请：后者把 GlassFrameThickness 设回 0，顺序反了背板让位失败。
        Fluent.BorderlessNative(this);
        _mica = Fluent.MicaBackdrop(this);
        Background = _mica ? Brushes.Transparent : new SolidColorBrush(Theme.WindowBg);
        Content = BuildBody(mode == Mode.Uninstall, out var closeBtn, out _primary, out _secondary);
        Fluent.InstallCaptionDrag(this, closeBtn);
        Fluent.FadeIn(this);

        RefreshRuntimeCard();
        // ContentRendered 而不是 Loaded：首帧画完再动手，提权请求与进度文字都排在窗口画出来之后
        ContentRendered += (_, _) =>
        {
            if (_mode == Mode.Install) StartInstall();
            else if (_mode == Mode.Uninstall) StartUninstall();
        };
    }

    /// <summary>组装标题栏、状态卡片、日志区与按钮。</summary>
    /// <param name="uninstall">true = 卸载形态的措辞（标题与「将删除」一行），false = 安装形态。</param>
    /// <param name="closeBtn">标题栏关闭按钮，供拖拽辅助函数排除命中。</param>
    /// <param name="primary">主按钮，交回构造函数做字段赋值。</param>
    /// <param name="secondary">次按钮，交回构造函数做字段赋值。</param>
    /// <returns>窗口根元素。</returns>
    private FrameworkElement BuildBody(bool uninstall, out Button closeBtn, out Button primary, out Button secondary)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 标题条是 ChromeBg（alpha 0xD9），叠在背板上会变成两层半透明互相污染；背板在场时让它透明。
        var bar = new Grid { Background = _mica ? Brushes.Transparent : new SolidColorBrush(Theme.ChromeBg) };
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
            Text = uninstall ? "卸载 FrameCapture" : "安装 FrameCapture",
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
        });
        // 路径按本机现算：写死 C: 会在 Program Files 被重定向的机器上说谎。
        // 安装形态给一个「更改」入口——装到别的盘是常见诉求；卸载形态没有可换的东西。
        var target = new Grid { Margin = new Thickness(0, 6, 0, 16) };
        target.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        target.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _targetText.Text = TargetLine(uninstall);
        _targetText.FontSize = 12;
        _targetText.TextWrapping = TextWrapping.Wrap;
        _targetText.Foreground = new SolidColorBrush(Theme.TextDim);
        Grid.SetColumn(_targetText, 0);
        target.Children.Add(_targetText);
        if (!uninstall)
        {
            var change = new TextBlock
            {
                Text = "更改",
                FontSize = 12,
                Cursor = Cursors.Hand,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = new SolidColorBrush(Fluent.AccentText()),
            };
            // 文字链接 hover 才显下划线：默认干净，悬停才提示这里可点
            var linkStyle = new Style(typeof(TextBlock));
            var linkHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            linkHover.Setters.Add(new Setter(TextBlock.TextDecorationsProperty, TextDecorations.Underline));
            linkStyle.Triggers.Add(linkHover);
            change.Style = linkStyle;
            change.MouseLeftButtonUp += (_, _) => PickTarget();
            _change = change;
            Grid.SetColumn(change, 1);
            target.Children.Add(change);
        }
        body.Children.Add(target);

        _cardText.FontSize = 13;
        _card.Background = new SolidColorBrush(Theme.CardBg);
        _card.CornerRadius = new CornerRadius(10);
        _card.Padding = new Thickness(16, 12, 16, 12);
        _card.Child = _cardText;
        Fluent.Elevate(_card);
        body.Children.Add(_card);

        _log.FontSize = 12;
        _log.TextWrapping = TextWrapping.Wrap;
        _log.Foreground = new SolidColorBrush(Theme.TextDim);
        _logBox.Content = _log;
        // 固定高度而非 MaxHeight：进度文字逐行追加时窗口不跳高。没有进度时整块收起，
        // 免得决策页中间空出一截——卡片也只在缺运行时的那台机器上才占位。
        _logBox.Visibility = Visibility.Collapsed;
        body.Children.Add(_logBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            // 靠右：主按钮落在最右下角，那是鼠标离开窗口前的自然位置，也是 Windows 对话框的惯例落点
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        primary = Fluent.RoundButton(uninstall ? "卸载" : "安装", Theme.Accent, Theme.WindowBg, 22, true);
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

    /// <summary>
    /// 运行时够用就不占版面：卡片只在缺运行时时出现，主按钮同时换成「下载并安装」，
    /// 点下去就能看见这一趟会顺带装 .NET。卸载形态永远不出卡片。
    /// </summary>
    private void RefreshRuntimeCard()
    {
        if (_mode == Mode.Uninstall || DotnetRuntime.Satisfied(out var found))
        {
            _card.Visibility = Visibility.Collapsed;
            if (_mode == Mode.Idle) _primary.Content = "安装";
            return;
        }

        _cardText.Text = found == null
            ? "缺少 .NET Desktop Runtime，需要从微软官网下载（约 56 MB）"
            : "已装的 .NET Desktop Runtime " + found + " 版本过低，需要下载所需版本";
        _cardText.Foreground = new SolidColorBrush(Theme.Accent);
        _card.Visibility = Visibility.Visible;
        if (_mode == Mode.Idle) _primary.Content = "下载并安装";
    }

    /// <summary>目标位置那一行的文案。卸载形态报的是登记里的目录。</summary>
    /// <param name="uninstall">true = 卸载形态措辞。</param>
    /// <returns>一行完整文案。</returns>
    private static string TargetLine(bool uninstall)
    {
        if (uninstall) return "将删除  " + InstallEngine.InstallDir + "、开始菜单快捷方式、开机自启与个人设置";
        var machineWide = string.Equals(InstallEngine.InstallDir, InstallEngine.DefaultInstallDir,
            StringComparison.OrdinalIgnoreCase);
        return "目标位置  " + InstallEngine.InstallDir + (machineWide ? "（所有用户可用）" : "");
    }

    /// <summary>换安装位置。挑的位置装不了就在同一行写明白，界面那行始终说真话。</summary>
    private void PickTarget()
    {
        if (_busy) return;
        var picked = FolderPicker.Pick(this, InstallEngine.InstallDir, "选择安装位置", "安装到此处");
        if (picked == null) return;   // 取消：什么都不改
        if (!InstallEngine.TryTarget(picked, out var full, out var why))
        {
            _targetText.Text = TargetLine(false) + "（装不了：" + why + "）";
            return;
        }
        InstallEngine.InstallDir = full;
        _targetText.Text = TargetLine(false);
    }

    /// <summary>主按钮：空闲态申请提权续跑，提权态直接干活，收尾态负责打开与关闭。</summary>
    private void OnPrimary()
    {
        if (_busy) return;
        if (_mode == Mode.Idle)
        {
            if (App.Elevated) { StartInstall(); return; }
            // 位置随命令行带进提权实例：提权那一趟没有界面可挑
            if (!App.RelaunchElevated(App.ArgInstall + " " + App.ArgDir + " \"" + InstallEngine.InstallDir + "\""))
            {
                Log("已取消：安装需要管理员权限");
                return;
            }
            Log("已在管理员窗口中继续，本窗口即将关闭");
            Close();
            return;
        }
        if (_mode == Mode.Finished)
        {
            if (_launchOnFinish) LaunchApp();
            Close();
        }
    }

    /// <summary>提权实例：先补运行时，再落文件与登记。</summary>
    private async void StartInstall()
    {
        if (_busy) return;
        _busy = true;
        _primary.IsEnabled = false;
        _secondary.IsEnabled = false;   // 半途关窗会留下装了一半的目录，进度期间不给退出入口
        if (_change != null) _change.Visibility = Visibility.Collapsed;   // 位置已定，进度期间锁住这个入口
        // 走到这一步，运行时要么已就位、要么正由下面的下载补上——那条提示的使命已经结束
        _card.Visibility = Visibility.Collapsed;
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
                    BackToIdle();
                    return;
                }
                Log("正在安装运行时…");
                var code = await Task.Run(() => RuntimeSetup.RunInstaller(tmp));
                TryDelete(tmp);
                if (code != 0 && code != 3010)
                {
                    Log("运行时安装失败，退出码 " + code + "。可手动安装：" + RuntimeSetup.DownloadPage);
                    BackToIdle();
                    return;
                }
            }

            Log("正在安装主程序…");
            var ok = await Task.Run(() => InstallEngine.Install(new Progress<string>(Log)));
            if (ok)
            {
                Log("安装完成");
                EnterFinished("打开 FrameCapture", launch: true);
            }
            else BackToIdle();
        }
        catch (Exception ex)
        {
            Log("失败：" + ex.Message);
            BackToIdle();
        }
    }

    /// <summary>
    /// 卸载。不多问一句：入口本身就是意图表达——控制面板里点「卸载」，或双击带 <c>--uninstall</c> 的快捷方式。
    /// 自身要跑到临时副本上，才能删掉安装目录里那份自己。
    /// </summary>
    private async void StartUninstall()
    {
        if (_busy) return;

        if (!App.Elevated)
        {
            if (!App.RelaunchElevated(App.ArgUninstall))
            {
                Log("已取消：卸载需要管理员权限");
                return;
            }
            Close();
            return;
        }

        _busy = true;
        _primary.IsEnabled = false;
        _secondary.IsEnabled = false;
        _primary.Content = "卸载";
        try
        {
            var inside = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)
                         == InstallEngine.InstallDir;
            if (inside)
            {
                var copy = App.CopySelfToTemp();
                if (copy == null)
                {
                    Log("无法准备卸载副本");
                    EnterFinished("关闭", launch: false);
                    return;
                }
                Process.Start(new ProcessStartInfo(copy, App.ArgUninstall) { UseShellExecute = false });
                Close();
                return;
            }

            var ok = await Task.Run(() => InstallEngine.Uninstall(new Progress<string>(Log)));
            if (ok) ScheduleSelfDelete();
            EnterFinished(ok ? "完成" : "关闭", launch: false);
        }
        catch (Exception ex)
        {
            Log("失败：" + ex.Message);
            EnterFinished("关闭", launch: false);
        }
    }

    /// <summary>回到可重试状态：两条按钮重新可点，运行时卡片按当前状态决定要不要亮出来。</summary>
    private void BackToIdle()
    {
        _busy = false;
        _primary.IsEnabled = true;
        _secondary.IsEnabled = true;
        RefreshRuntimeCard();
    }

    /// <summary>
    /// 收尾态：解除忙碌、两条按钮都可点。次按钮措辞换成「完成」——活已经干完，此时「取消」没有可取消的东西。
    /// </summary>
    /// <param name="primaryText">主按钮文案。</param>
    /// <param name="launch">点主按钮是否顺带启动主程序；卸载完程序已不在，只能关闭。</param>
    private void EnterFinished(string primaryText, bool launch)
    {
        _mode = Mode.Finished;
        _busy = false;
        _launchOnFinish = launch;
        _primary.IsEnabled = true;
        _primary.Content = primaryText;
        _secondary.Content = "完成";
        _secondary.IsEnabled = true;
        // 卸载完主按钮本身就是「完成」，再挂一条同义的次按钮只是多一个鼠标落点
        _secondary.Visibility = launch ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>卸载成功后清掉自己这份临时副本：正在运行的 exe 删不掉自己，交给一个等它退出的后台进程。</summary>
    private static void ScheduleSelfDelete()
    {
        var self = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(self) || !File.Exists(self)) return;
        // 只清临时目录里那份副本：安装目录那份随目录一起删，用户下载目录里的引导器归用户自己处置
        var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(self), temp, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            Process.Start(new ProcessStartInfo(self,
                App.ArgPurge + " " + Process.GetCurrentProcess().Id + " \"" + self + "\"")
            { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { /* 副本留在临时目录，交给系统清理临时文件 */ }
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
        Dispatcher.Invoke(() =>
        {
            _logBox.Visibility = Visibility.Visible;
            _log.Text = (_log.Text.Length == 0 ? "" : _log.Text + "\n") + text;
            // 先量一次再滚：刚赋的 Text 还没进布局，直接 ScrollToEnd 滚的是上一行的位置
            _logBox.UpdateLayout();
            _logBox.ScrollToEnd();
        });
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
