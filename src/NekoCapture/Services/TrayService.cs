using System.Drawing;
using System.Windows.Controls;
using NekoCapture.Core;
using WinForms = System.Windows.Forms;

namespace NekoCapture.Services;

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
    private readonly Action _onSettingsChanged;
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
            Text = "NekoCapture",
        };
        InitializeTrayClicks();
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) Dispatch(TrayRelease);
            else if (e.Button == WinForms.MouseButtons.Right) Dispatch(() => { _trayClickTimer.Stop(); _trayClicks.Flush(); ShowMenu(); });
        };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) Dispatch(TrayDoubleClick); };
        // 悬停提示动态反映当前热键/范围/fps：改键或换范围后无需打开菜单即可确认状态
        _onSettingsChanged = () => Dispatch(() => { if (!_disposed) { UpdateTip(); RefreshTrayClicks(); _menuTriggersDirty = true; QueueMenuWarmup(); } });
        _host.Settings.Changed += _onSettingsChanged;
        _onPalette = () => Dispatch(RebuildIcons);
        Theme.Changed += _onPalette;
        UpdateTip(); QueueMenuWarmup();
    }

    /// <summary>按当前强调色重画两枚托盘图标，再把托盘切到新引用上。</summary>
    void RebuildIcons()
    {
        if (_disposed) return;
        _menuResources = null;
        ReleaseMenu();
        var prevOn = _iconOn;
        var prevPaused = _iconPaused;
        _iconOn = MakeIcon(paused: false);
        _iconPaused = MakeIcon(paused: true);
        UpdateTip();          // 先切换引用，再释放替换下来的图标
        ResidentBridge.RefreshTray();
        prevOn.Dispose();
        prevPaused.Dispose();
        QueueMenuWarmup();
    }

    void UpdateTip()
    {
        var s = _host.Settings.Current;
        if (Services.HotkeyManager.Paused) // 暂停态一眼可见，避免用户以为热键失灵
        {
            // 图标整枚转灰：余光扫过托盘就知道热键让位了，不用把鼠标停上去读提示
            _icon.Icon = _iconPaused;
            _icon.Text = Loc.T(K.Tray.NekoCapturePausedHotkeysReleased);
            return;
        }
        _icon.Icon = _iconOn;
        string tip = ActiveTip(s);
        _icon.Text = tip.Length <= 63 ? tip : "NekoCapture"; // NotifyIcon.Text 上限 63 字符
    }

    /// <summary>按当前配置生成常规托盘提示。</summary>
    /// <param name="settings">当前设置。</param>
    /// <returns>包含快捷键数、范围和帧率的提示。</returns>
    internal static string ActiveTip(AppSettings settings)
    {
        string region = settings.RegionW >= 16 ? Loc.F(K.Tray.RegionFps, settings.RegionW, settings.RegionH, settings.Fps) : Loc.T(K.Tray.NoRegionDoubleClick);
        return Loc.F(K.Tray.NekoCaptureGestures, settings.Gestures.Bindings.Count, region);
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
        if (_disposed) return;
        _disposed = true; _trayClickTimer.Stop(); ReleaseMenu();
        _host.Settings.Changed -= _onSettingsChanged;
        Theme.Changed -= _onPalette;
        _icon.Visible = false;
        _icon.Dispose();
        _iconOn.Dispose(); _iconPaused.Dispose();
    }
}
