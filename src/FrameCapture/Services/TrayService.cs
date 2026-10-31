using System.Drawing;
using System.Windows.Controls;
using FrameCapture.Core;
using WinForms = System.Windows.Forms;

namespace FrameCapture.Services;

/// <summary>托盘常驻：图标 + WPF Fluent 右键菜单（随主题着色、悬停高亮、可勾选）。</summary>
public sealed partial class TrayService : IDisposable
{
    private readonly AppHost _host;
    private readonly WinForms.NotifyIcon _icon;
    private System.Windows.Window? _menuAnchor;
    private ContextMenu? _menu;
    // 两枚图标一次画好备用：暂停切换时只是换个引用，不反复造 GDI 句柄。
    // 强调色改动会重画它们——图标是位图，画出来就定死了，不重画会停在构造时的强调色上。
    private Icon _iconOn, _iconPaused;
    private readonly Action _onPalette;
    private System.Windows.ResourceDictionary? _menuResources;

    /// <summary>建托盘图标并订阅设置变更以刷新悬停提示。图标此刻 <c>Visible=false</c>，由 <see cref="Show"/> 点亮。</summary>
    /// <param name="host">宿主，用于把菜单项动作转给 <see cref="CaptureController"/> 与设置服务；须已完成装配。</param>
    public TrayService(AppHost host)
    {
        _host = host;
        _menuResources = Fluent.MenuResources();
        _iconOn = MakeIcon(paused: false);
        _iconPaused = MakeIcon(paused: true);

        _icon = new WinForms.NotifyIcon
        {
            Icon = _iconOn,
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
        _onPalette = () => Dispatch(RebuildIcons);
        Theme.Changed += _onPalette;
        UpdateTip();
    }

    /// <summary>按当前强调色重画两枚托盘图标，再把托盘切到新引用上。</summary>
    void RebuildIcons()
    {
        _menuResources = null;
        var prevOn = _iconOn;
        var prevPaused = _iconPaused;
        _iconOn = MakeIcon(paused: false);
        _iconPaused = MakeIcon(paused: true);
        UpdateTip();          // 先切换引用，再释放替换下来的图标
        ResidentBridge.RefreshTray();
        prevOn.Dispose();
        prevPaused.Dispose();
    }

    void UpdateTip()
    {
        var s = _host.Settings.Current;
        if (Services.HotkeyManager.Paused) // 暂停态一眼可见，避免用户以为热键失灵
        {
            // 图标整枚转灰：余光扫过托盘就知道热键让位了，不用把鼠标停上去读提示
            _icon.Icon = _iconPaused;
            _icon.Text = Loc.T(K.Tray.FramecapturePausedHotkeysReleased);
            return;
        }
        _icon.Icon = _iconOn;
        string tip = ActiveTip(s);
        _icon.Text = tip.Length <= 63 ? tip : "FrameCapture"; // NotifyIcon.Text 上限 63 字符
    }

    /// <summary>按当前配置生成常规托盘提示。</summary>
    /// <param name="settings">当前设置。</param>
    /// <returns>包含快捷键数、范围和帧率的提示。</returns>
    internal static string ActiveTip(AppSettings settings)
    {
        string region = settings.RegionW >= 16 ? Loc.F(K.Tray.RegionFps, settings.RegionW, settings.RegionH, settings.Fps) : Loc.T(K.Tray.NoRegionDoubleClick);
        return Loc.F(K.Tray.FramecaptureGestures, settings.Gestures.Bindings.Count, region);
    }

    /// <summary>让托盘图标可见。构造时故意不可见，宿主装配完再调本方法——避免图标先出现、菜单还点不动的窗口期。</summary>
    public void Show() { if (!ResidentBridge.IsWorker) _icon.Visible = true; }

    /// <summary>
    /// 提示气泡：托盘动作的轻量反馈（如「剪贴板里没有图片」）。走 <see cref="Overlays.NoticeToast"/>，
    /// 与截屏浮窗同一套样式、圆角与动效；系统托盘气泡的样式不受主题控制，也不在程序自己的掌控里。
    /// </summary>
    /// <param name="msg">气泡正文，单行短文本；停留 4 秒后自动消失，不阻塞调用。</param>
    public void Notify(string msg) => Dispatch(() => Overlays.NoticeToast.Show(msg));

    /// <summary>通过 `--pause-toggle` IPC 切换全局热键暂停状态。</summary>
    /// <returns>切换后的暂停态：true = 已暂停（钩子不喂手势、不吞任何键），false = 恢复正常工作。</returns>
    public bool TogglePause()
    {
        bool on = !Services.HotkeyManager.Paused;
        Services.HotkeyManager.Paused = on;
        UpdateTip();
        Notify(on ? Loc.T(K.Tray.PausedCaptureHotkeysAreReleased) : Loc.T(K.Tray.ResumedCaptureHotkeysAreActive));
        return on;
    }

    /// <summary>
    /// 在鼠标处弹出捕获、设置与退出菜单。
    /// 同一时刻只保留一份菜单与其锚窗：再次右键会先关掉上一份。
    /// </summary>
    public void ShowMenu()
    {
        Trace.Log("tray menu open");
        if (_menu != null) _menu.IsOpen = false; // 再次右键：先关掉已开着的菜单，连带回收它的锚窗
        var menu = Fluent.Ui(new ContextMenu { Resources = _menuResources ??= Fluent.MenuResources(), StaysOpen = false });
        _menu = menu;

        void Item(string text, string glyph, Action act)
        {
            var mi = new MenuItem { Header = text, Icon = Fluent.Glyph(glyph, 15, Theme.TextDimBrush) };
            mi.Click += (_, _) => { menu.IsOpen = false; act(); };
            menu.Items.Add(mi);
        }
        void Sep() => menu.Items.Add(new Separator());

        Item(Loc.T(K.Tray.CaptureNow), "\uE7A8", () => _host.Controller.CaptureStoredStill());
        Item(Loc.T(K.Common.ScrollingCapture), "\uE8CB", () => _host.Controller.BeginScrollCapture());
        Item(Loc.T(K.Common.DelayedCapture), "\uE823", () => _host.Controller.BeginDelayedCapture());
        Item(Loc.T(K.Tray.PinFromClipboard), "\uE718", async () =>
        {
            if (!await _host.Controller.PinFromClipboardAsync()) Notify(Loc.T(K.Tray.NoImageOnTheClipboard));
        });
        Sep();
        Item(Loc.T(K.Common.Settings), "\uE713", OpenSettings);
        Item(Loc.T(K.Tray.Exit), "\uE8BB", () =>
        {
            ResidentBridge.RetireHost(); Dispose(); System.Windows.Application.Current?.Shutdown();
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
        WatchMenu(menu, anchor);
        anchor.Deactivated += (_, _) => { menu.IsOpen = false; };
        anchor.Show();
        anchor.Activate();
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


    /// <summary>
    /// 隐藏并释放托盘图标，同时关闭还开着的菜单锚窗。宿主 Shutdown 与托盘「退出」菜单项都走这里。
    /// </summary>
    /// <remarks>调用后实例不可再用：<see cref="WinForms.NotifyIcon"/> 已释放，再调 <see cref="Show"/> 会抛异常。</remarks>
    public void Dispose()
    {
        if (_menu != null) _menu.IsOpen = false;
        Theme.Changed -= _onPalette;
        _icon.Visible = false;
        _icon.Dispose();
        _iconOn.Dispose(); _iconPaused.Dispose();
        _menuAnchor?.Close();
    }
}
