using System.Drawing;
using System.Windows.Controls;
using FrameCapture.Core;
using WinForms = System.Windows.Forms;

namespace FrameCapture.Services;

/// <summary>托盘常驻：图标 + WPF Fluent 右键菜单（随主题着色、悬停高亮、可勾选）。</summary>
public sealed class TrayService : IDisposable
{
    private readonly AppHost _host;
    private readonly WinForms.NotifyIcon _icon;
    private System.Windows.Window? _menuAnchor;
    private ContextMenu? _menu;

    /// <summary>建托盘图标并订阅设置变更以刷新悬停提示。图标此刻 <c>Visible=false</c>，由 <see cref="Show"/> 点亮。</summary>
    /// <param name="host">宿主，用于把菜单项动作转给 <see cref="CaptureController"/> 与设置服务；须已完成装配。</param>
    public TrayService(AppHost host)
    {
        _host = host;

        _icon = new WinForms.NotifyIcon
        {
            Icon = MakeIcon(),
            Visible = false,
            Text = "FrameCapture",
        };
        _icon.MouseUp += (_, e) =>
        {
            // 左键单击=选区截图，右键=菜单。
            // 双击会连发两次 MouseUp，但 BeginScopeSelection 有重入保护（已在选区只顶前不重开），不会截两次。
            if (e.Button == WinForms.MouseButtons.Left) Dispatch(() => _host.Controller.BeginScopeSelection());
            else if (e.Button == WinForms.MouseButtons.Right) Dispatch(ShowMenu);
        };
        _icon.DoubleClick += (_, _) => Dispatch(() => _host.Controller.BeginScopeSelection());
        // 悬停提示动态反映当前热键/范围/fps：改键或换范围后无需打开菜单即可确认状态
        _host.Settings.Changed += () => Dispatch(UpdateTip);
        UpdateTip();
    }

    void UpdateTip()
    {
        var s = _host.Settings.Current;
        if (Services.HotkeyManager.Paused) // 暂停态一眼可见，避免用户以为热键失灵
        {
            _icon.Text = "FrameCapture · 已暂停，热键让位其他软件";
            return;
        }
        string region = s.RegionW >= 16 ? $"范围 {s.RegionW}×{s.RegionH} · {s.Fps}fps" : "未设范围 · 双击图标设置";
        string tip = $"FrameCapture\n手势 {s.Gestures.Bindings.Count} 条 · {region}";
        _icon.Text = tip.Length <= 63 ? tip : "FrameCapture"; // NotifyIcon.Text 上限 63 字符
    }

    /// <summary>让托盘图标可见。构造时故意不可见，宿主装配完再调本方法——避免图标先出现、菜单还点不动的窗口期。</summary>
    public void Show() => _icon.Visible = true;

    /// <summary>
    /// 提示气泡：托盘动作的轻量反馈（如「剪贴板里没有图片」）。走 <see cref="Overlays.NoticeToast"/>，
    /// 与截屏浮窗同一套样式、圆角与动效；系统托盘气泡的样式不受主题控制，也不在程序自己的掌控里。
    /// </summary>
    /// <param name="msg">气泡正文，单行短文本；停留 4 秒后自动消失，不阻塞调用。</param>
    public void Notify(string msg) => Dispatch(() => Overlays.NoticeToast.Show(msg));

    /// <summary>切换「暂停全局热键」。托盘菜单项与 `--pause-toggle` IPC 共用；返回切换后的状态。</summary>
    /// <returns>切换后的暂停态：true = 已暂停（钩子不喂手势、不吞任何键），false = 恢复正常工作。</returns>
    public bool TogglePause()
    {
        bool on = !Services.HotkeyManager.Paused;
        Services.HotkeyManager.Paused = on;
        UpdateTip();
        Notify(on ? "已暂停：截图热键暂时让位给其他软件，回到这里可恢复" : "已恢复：截图热键重新生效");
        return on;
    }

    /// <summary>
    /// 在鼠标处弹出托盘菜单。菜单项每次重建，因此始终反映当前设置（延时秒数、自启勾选、是否已提权）。
    /// 同一时刻只保留一份菜单与其锚窗：再次右键会先关掉上一份。
    /// </summary>
    public void ShowMenu()
    {
        Trace.Log("tray menu open");
        if (_menu != null) _menu.IsOpen = false; // 再次右键：先关掉已开着的菜单，连带回收它的锚窗
        var menu = new ContextMenu { Resources = Fluent.MenuResources() };
        _menu = menu;

        void Item(string text, Action act, bool checkable = false, bool isChecked = false)
        {
            var mi = new MenuItem { Header = text, IsCheckable = checkable, IsChecked = isChecked };
            mi.Click += (_, _) => { menu.IsOpen = false; act(); };
            menu.Items.Add(mi);
        }
        void Sep() => menu.Items.Add(new Separator());

        Item("设置捕获范围", () => _host.Controller.BeginScopeSelection());
        Item("清除捕获范围", () => _host.Controller.ClearStoredRegion());
        Sep();
        Item("立即捕获", () => _host.Controller.CaptureStoredStill());
        // 静态截图的确认浮窗会自动消失，留一个入口把最近那张重新拉回编辑器标注
        if (_host.Controller.HasLastStill)
            Item("编辑上次截图", () => _host.Controller.EditLastStill());
        Item("滚动截图", () => _host.Controller.BeginScrollCapture());
        Item("延时截图", () => _host.Controller.BeginDelayedCapture());
        Item("从剪贴板贴图", () =>
        {
            if (!_host.Controller.PinFromClipboard())
                Notify("剪贴板里没有图片——先复制一张截图、图片或网页图再试");
        });
        // 开了自动保存后，用户常想知道文件落到哪——直接打开保存目录
        if (_host.Settings.Current.AutoSaveCopy)
            Item("打开保存文件夹", () =>
            {
                try
                {
                    var dir = SaveService.ResolveFolder();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
                catch (Exception ex) { Trace.Log($"open folder failed: {ex.Message}"); Notify("打开保存文件夹失败"); }
            });
        Sep();
        Item("设置…", OpenSettings);
        Item("帮助", OpenHelp);
        Item("开机自启", () =>
        {
            _host.Settings.Mutate(s => s.RunAtStartup = !s.RunAtStartup);
            StartupService.Apply(_host.Settings.Current.RunAtStartup);
        }, checkable: true, isChecked: _host.Settings.Current.RunAtStartup);
        // 暂停全局热键——游戏/演示/与其他软件热键冲突时临时让位，键完整落到前台应用。
        // 运行时状态（不持久化），重启自动恢复工作。
        Item("暂停全局热键", () => TogglePause(),
            checkable: true, isChecked: Services.HotkeyManager.Paused);
        // 按需提权：解决被更高权限窗口（管理员终端/游戏）压住、快捷键盲区的问题
        if (AppHost.IsElevated)
            Item("管理员权限运行", () => { }, checkable: true, isChecked: true); // 勾选标记由 checkable 呈现，文本里不重复打勾
        else
            Item("以管理员身份运行", () => _host.RestartAsAdmin());
        Sep();
        Item("退出", () =>
        {
            Dispose();
            System.Windows.Application.Current?.Shutdown();
        });

        // 透明锚点窗：保证菜单在鼠标处弹出、并能在关闭时回收。
        // 必须激活：应用没有任何激活窗口时，ContextMenu 收不到"失焦"，点别处菜单也不消失。
        _menuAnchor?.Close();
        var anchor = new System.Windows.Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = System.Windows.WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            Left = WinForms.Cursor.Position.X,
            Top = WinForms.Cursor.Position.Y,
        };
        _menuAnchor = anchor;
        anchor.Show();
        anchor.Activate();
        anchor.Deactivated += (_, _) => { menu.IsOpen = false; }; // 点其他应用/切窗 → 菜单即收
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menu, menu)) _menu = null;
            if (ReferenceEquals(_menuAnchor, anchor)) _menuAnchor = null;
            anchor.Close();
        };
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    static void Dispatch(Action a)
    {
        var app = System.Windows.Application.Current;
        if (app == null) { a(); return; }
        app.Dispatcher.Invoke(a);
    }

    void OpenSettings() => Overlays.SettingsWindow.ShowOrFocus();

    void OpenHelp() => Overlays.HelpWindow.ShowOrFocus();

    static Icon MakeIcon()
    {
        // 取景框图标：四角实心括号 + 中心点。虚线在托盘 16px 缩略下会糊成一团，故用实角
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(255, 0x4C, 0xC2, 0xFF), 3);
        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round; pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
        int m = 5, e = 11, r = 31 - m; // 边距/臂长/右边界
        g.DrawLines(pen, new[] { new Point(m, m + e), new Point(m, m), new Point(m + e, m) });           // 左上
        g.DrawLines(pen, new[] { new Point(r - e, m), new Point(r, m), new Point(r, m + e) });           // 右上
        g.DrawLines(pen, new[] { new Point(m, r - e), new Point(m, r), new Point(m + e, r) });           // 左下
        g.DrawLines(pen, new[] { new Point(r - e, r), new Point(r, r), new Point(r, r - e) });           // 右下
        using var dot = new SolidBrush(Color.FromArgb(255, 0x4C, 0xC2, 0xFF));
        g.FillEllipse(dot, 13, 13, 6, 6);
        IntPtr hicon = bmp.GetHicon();
        return Icon.FromHandle(hicon);
    }

    /// <summary>
    /// 隐藏并释放托盘图标，同时关闭还开着的菜单锚窗。宿主 Shutdown 与托盘「退出」菜单项都走这里。
    /// </summary>
    /// <remarks>调用后实例不可再用：<see cref="WinForms.NotifyIcon"/> 已释放，再调 <see cref="Show"/> 会抛异常。</remarks>
    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menuAnchor?.Close();
    }
}
