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
            if (e.Button == WinForms.MouseButtons.Right) Dispatch(ShowMenu);
        };
        _icon.DoubleClick += (_, _) => Dispatch(() => _host.Controller.BeginScopeSelection());
    }

    public void Show() => _icon.Visible = true;

    void ShowMenu()
    {
        Trace.Log("tray menu open");
        var menu = new ContextMenu { Resources = Fluent.MenuResources() };

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
        Item("立即捕获（按当前范围）", () =>
        {
            _host.Controller.BeginCapture();
            // 托盘触发没有“松手”：600ms 后自动结束
            var t = new System.Timers.Timer(600);
            t.Elapsed += (_, _) => { t.Stop(); t.Dispose(); Dispatch(() => _host.Controller.EndCapture()); };
            t.Start();
        });
        Sep();
        Item("快捷键设置…", OpenSettings);
        Item("帮助", OpenHelp);
        Item("开机自启", () =>
        {
            _host.Settings.Mutate(s => s.RunAtStartup = !s.RunAtStartup);
            StartupService.Apply(_host.Settings.Current.RunAtStartup);
        }, checkable: true, isChecked: _host.Settings.Current.RunAtStartup);
        // 按需提权：解决被更高权限窗口（管理员终端/游戏）压住、快捷键盲区的问题
        if (AppHost.IsElevated)
            Item("管理员权限运行 ✓", () => { }, checkable: true, isChecked: true);
        else
            Item("以管理员身份运行（防遮挡）", () => _host.RestartAsAdmin());
        Sep();
        Item("退出", () =>
        {
            Dispose();
            System.Windows.Application.Current?.Shutdown();
        });

        // 透明锚点窗：保证菜单在鼠标处弹出、并能在关闭时回收
        _menuAnchor?.Close();
        _menuAnchor = new System.Windows.Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = System.Windows.WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Left = WinForms.Cursor.Position.X,
            Top = WinForms.Cursor.Position.Y,
        };
        _menuAnchor.Show();
        menu.PlacementTarget = _menuAnchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.Closed += (_, _) =>
        {
            if (_menuAnchor != null) { _menuAnchor.Close(); _menuAnchor = null; }
        };
        menu.IsOpen = true;
    }

    static void Dispatch(Action a)
    {
        var app = System.Windows.Application.Current;
        if (app == null) { a(); return; }
        app.Dispatcher.Invoke(a);
    }

    void OpenSettings() => FrameCapture.Overlays.SettingsWindow.ShowOrFocus();

    void OpenHelp() => FrameCapture.Overlays.HelpWindow.ShowOrFocus();

    static Icon MakeIcon()
    {
        // 程序化生成一个取景框图标
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var pen = new Pen(Color.FromArgb(255, 0x4C, 0xC2, 0xFF), 3) { DashStyle = System.Drawing.Drawing2D.DashStyle.Custom, DashPattern = new float[] { 5, 3 } };
        g.DrawRectangle(pen, 4, 4, 23, 23);
        using var dot = new SolidBrush(Color.FromArgb(255, 0x4C, 0xC2, 0xFF));
        g.FillEllipse(dot, 13, 13, 6, 6);
        IntPtr hicon = bmp.GetHicon();
        return Icon.FromHandle(hicon);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menuAnchor?.Close();
    }
}
