using System.Windows;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Services;

/// <summary>
/// 应用宿主：组装快捷键、托盘、捕获控制器。所有 UI 相关启动都在 UI 线程。
/// </summary>
public sealed class AppHost
{
    public static AppHost Current { get; } = new();

    public SettingsService Settings { get; private set; } = null!;
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public TrayService Tray { get; private set; } = null!;
    public CaptureController Controller { get; private set; } = null!;

    private AppHost() { }

    public void Startup()
    {
        FrameCapture.Core.Trace.Log("Startup begin");
        Settings = new SettingsService();
        Theme.Apply(Settings.Current.Theme, Settings.Current.Accent);
        // 设计审查专用：FC_THEME 覆盖主题（不落盘，不影响用户配置）
        var ft = Environment.GetEnvironmentVariable("FC_THEME");
        if (ft == "light" || ft == "dark") Theme.Apply(ft, Settings.Current.Accent);
        Core.Fluent.ApplyGlobalStyles();
        Controller = new CaptureController(Settings);
        Hotkeys = new HotkeyManager();
        Tray = new TrayService(this);

        Hotkeys.ScopeRequested += () => Dispatch(Controller.BeginScopeSelection);
        Hotkeys.CaptureBegin += () => Dispatch(Controller.BeginCapture);
        Hotkeys.CaptureEnd += () => Dispatch(Controller.EndCapture);
        InstallHotkeys();

        Controller.Initialize();
        Tray.Show();
        WatchSettingsFile();
        // 设计审查专用：FC_UIPREVIEW=settings|help 启动后自动打开对应窗口（仅调试，不影响正常流）
        var preview = Environment.GetEnvironmentVariable("FC_UIPREVIEW");
        if (preview is "settings" or "help")
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (preview == "settings") Overlays.SettingsWindow.ShowOrFocus();
                else Overlays.HelpWindow.ShowOrFocus();
            };
            t.Start();
        }
        FrameCapture.Core.Trace.Log("Startup done");
    }

    public void Shutdown()
    {
        try { Hotkeys?.Uninstall(); } catch { }
        try { Tray?.Dispose(); } catch { }
        try { Controller?.Dispose(); } catch { }
    }

    public void InstallHotkeys()
    {
        var s = Settings.Current;
        Hotkeys.Install(s.ScopeHotkey, s.CaptureHotkey);
    }

    System.IO.FileSystemWatcher? _settingsWatcher;
    System.Windows.Threading.DispatcherTimer? _reloadDebounce;

    /// <summary>监听 settings.json 外部改动（用户手动编辑/脚本切换）：热重载设置并重挂热键。</summary>
    void WatchSettingsFile()
    {
        try
        {
            _reloadDebounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _reloadDebounce.Tick += (_, _) =>
            {
                _reloadDebounce!.Stop();
                if (Settings.RecentlySelfWritten) return; // 忽略本进程自己的落盘
                Settings.Reload();
                var s = Settings.Current;
                Theme.Apply(s.Theme, s.Accent);
                InstallHotkeys();
                FrameCapture.Core.Trace.Log($"settings reloaded: scope={s.ScopeHotkey} capture={s.CaptureHotkey}");
            };
            _settingsWatcher = new System.IO.FileSystemWatcher(System.IO.Path.GetDirectoryName(SettingsService.FilePath)!, "settings.json")
            { NotifyFilter = System.IO.NotifyFilters.LastWrite, EnableRaisingEvents = true };
            _settingsWatcher.Changed += (_, _) =>
            {
                // 投递而非阻塞：watcher 线程不该等 UI（同 Dispatch 的理由，只是危害小）
                try { _reloadDebounce?.Dispatcher.BeginInvoke(() => { _reloadDebounce!.Stop(); _reloadDebounce.Start(); }); }
                catch { }
            };
        }
        catch { }
    }

    /// <summary>当前是否以管理员权限运行。</summary>
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>以管理员权限重启本程序（UAC 确认一次）；取消则维持现状。</summary>
    public void RestartAsAdmin()
    {
        try
        {
            var exe = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            { UseShellExecute = true, Verb = "runas" });
            FrameCapture.Core.Trace.Log("relaunch requested: elevated");
            Shutdown();
            System.Windows.Application.Current?.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception) { /* 用户取消了 UAC */ }
        catch (Exception ex) { FrameCapture.Core.Trace.Log($"elevate failed: {ex.GetType().Name}"); }
    }

    /// <summary>把钩子动作投递到 UI 线程。绝不阻塞：钩子回调占着全系统输入链，
    /// 曾经的阻塞 Invoke 让整台机器键盘卡住 ~130ms（等 WGC 初始化），且超过
    /// LowLevelHooksTimeout 会被系统静默摘钩。Send 优先级排在输入之前，
    /// 同优先级 FIFO —— Begin/End 的先后顺序因此保持。</summary>
    static void Dispatch(Action a)
    {
        var app = System.Windows.Application.Current;
        if (app == null) { a(); return; }
        try { app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, a); }
        catch { /* UI 线程已在关闭 */ }
    }
}
