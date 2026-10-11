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
    private System.Windows.Controls.ContextMenu? _menu;

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
        // 悬停提示动态反映当前热键/范围/fps（R36）：改键或换范围后无需打开菜单即可确认状态
        _host.Settings.Changed += () => Dispatch(UpdateTip);
        UpdateTip();
    }

    void UpdateTip()
    {
        var s = _host.Settings.Current;
        string region = s.RegionW >= 16 ? $"范围 {s.RegionW}×{s.RegionH} · {s.Fps}fps" : "未设范围 · 双击图标设置";
        string tip = $"FrameCapture\n捕获 {s.CaptureHotkey} · 选区 {s.ScopeHotkey}\n{region}";
        _icon.Text = tip.Length <= 63 ? tip : "FrameCapture"; // NotifyIcon.Text 上限 63 字符
    }

    public void Show() => _icon.Visible = true;

    void ShowMenu()
    {
        Trace.Log("tray menu open");
        if (_menu != null) _menu.IsOpen = false; // 再次右键：先收旧菜单（连带回收旧锚窗）
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
            Item("管理员权限运行", () => { }, checkable: true, isChecked: true); // 勾选标记由 checkable 呈现，文本不再带 ✓（R24 去双勾）
        else
            Item("以管理员身份运行（防遮挡）", () => _host.RestartAsAdmin());
        Sep();
        Item("退出", () =>
        {
            Dispose();
            System.Windows.Application.Current?.Shutdown();
        });

        // 透明锚点窗：保证菜单在鼠标处弹出、并能在关闭时回收。
        // 必须激活（R58）：应用此前无任何激活窗口时，ContextMenu 收不到"失焦"，点别处菜单也不消失。
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

    void OpenSettings() => FrameCapture.Overlays.SettingsWindow.ShowOrFocus();

    void OpenHelp() => FrameCapture.Overlays.HelpWindow.ShowOrFocus();

    static Icon MakeIcon()
    {
        // 取景框图标：四角实心括号 + 中心点。虚线在托盘 16px 缩略下会糊成一团（R24 改实角）
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(255, 0x4C, 0xC2, 0xFF), 3)
        { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
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

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menuAnchor?.Close();
    }
}
