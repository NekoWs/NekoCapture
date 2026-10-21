using FrameCapture.Core;

namespace FrameCapture.Services;

/// <summary>
/// 应用宿主：组装快捷键、托盘、捕获控制器。所有 UI 相关启动都在 UI 线程。
/// </summary>
public sealed class AppHost
{
    public static AppHost Current { get; } = new();

    // 不变量：以下服务在 Startup 返回时全部就位；Startup 未跑完就访问会拿到 null（声明成 null! 只为过编译检查）。
    // 生命周期归本类，Shutdown 逐个释放。
    public SettingsService Settings { get; private set; } = null!;
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public GestureService Gestures { get; private set; } = null!;
    public TrayService Tray { get; private set; } = null!;
    public CaptureController Controller { get; private set; } = null!;

    private AppHost() { }

    /// <summary>
    /// 装配并启动全部服务：清残留磁盘暂存帧 → 设置与主题 → 控制器 → 手势与键盘钩子 → 托盘 → settings.json 监听。
    /// </summary>
    /// <remarks>必须在 UI 线程调用：托盘、WPF 全局样式、<c>DispatcherTimer</c> 都挂在 UI 线程。
    /// 启动顺序的硬约束见 <see cref="InstallHotkeys"/>。</remarks>
    public void Startup()
    {
        Trace.Log("Startup begin");
        Capture.FrameSpool.CleanupStale(); // 清理上次崩溃/强杀残留的磁盘暂存帧文件（>24h）
        Settings = new SettingsService();
        Theme.Apply(Settings.Current.Theme, Settings.Current.Accent);
        Loc.Apply(Settings.Current.UiLanguage);
        // 设计审查专用：FC_THEME 覆盖主题（不落盘，不影响用户配置）
        var ft = Environment.GetEnvironmentVariable("FC_THEME");
        if (ft == "light" || ft == "dark") Theme.Apply(ft, Settings.Current.Accent);
        // 设计审查专用：FC_LANG 覆盖界面语言，拍英文界面不必先改用户配置
        var fl = Environment.GetEnvironmentVariable("FC_LANG");
        if (fl == Loc.PrefZh || fl == Loc.PrefEn) Loc.Apply(fl);
        Core.Fluent.ApplyGlobalStyles();
        Controller = new CaptureController(Settings);
        Hotkeys = new HotkeyManager();
        Gestures = new GestureService(this);
        Tray = new TrayService(this);

        Hotkeys.ScrollCancel += () => Dispatch(Controller.ScrollCancel); // 滚动截图中 Esc 全局取消
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
        Trace.Log("Startup done");
    }

    /// <summary>按 键盘钩子 → 手势 → 托盘 → 控制器 的顺序释放。每步独立吞异常：一步失败不拖累其余释放。</summary>
    /// <remarks>Startup 只跑了一半时也可调用：未就位的服务是 null，<c>?.</c> 直接跳过。</remarks>
    public void Shutdown()
    {
        try { Hotkeys?.Uninstall(); } catch { }
        try { Gestures?.Dispose(); } catch { }
        try { Tray?.Dispose(); } catch { }
        try { Controller?.Dispose(); } catch { }
    }

    /// <summary>
    /// 把手势配置装进引擎并重挂全局钩子。顺序是硬约束：先 <c>Gestures.Apply</c> 置好 KeySink，
    /// 再 <c>Hotkeys.Install</c>——后者按 KeySink 是否为 null 决定装不装键盘钩子。
    /// </summary>
    /// <remarks>启动、settings.json 外部改动热重载、手势配置器保存、设置窗关闭都走这里；可重复调用。</remarks>
    public void InstallHotkeys()
    {
        // 服务还没就位时什么都不做：设置窗的销毁回调、启动中途失败、离屏建窗都会走到这里，
        // 在窗口销毁回调里抛未处理异常等于把整个进程带走。
        if (Settings is null || Gestures is null || Hotkeys is null) return;
        var s = Settings.Current;
        // 浮窗那条复制键随设置走：改完当场生效，不必等下次启动。
        HotkeyManager.ToastCopyChord = s.ToastCopyChord ?? "";
        // 手势是唯一的全局触发路径。先应用手势（填引擎绑定表 + 挂键事件汇/鼠标钩子，置 KeySink），
        // 再装键盘钩子（Install 据 KeySink 决定装不装）。
        Gestures.Apply(s.Gestures);
        Hotkeys.Install();
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
                Loc.Apply(s.UiLanguage);
                InstallHotkeys();
                Trace.Log($"settings reloaded: gestures={s.Gestures.Enabled}/{s.Gestures.Bindings.Count}");
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
            Trace.Log("relaunch requested: elevated");
            Shutdown();
            System.Windows.Application.Current?.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception) { /* 用户取消了 UAC */ }
        catch (Exception ex) { Trace.Log($"elevate failed: {ex.GetType().Name}"); }
    }

    /// <summary>把钩子动作投递到 UI 线程，绝不阻塞。
    /// 钩子回调占着全系统输入链：阻塞 Invoke 会让整台机器的键盘卡住（等 WGC 初始化可达 ~130ms），
    /// 且超过 LowLevelHooksTimeout 会被系统静默摘钩。
    /// Send 优先级高于 Input、同优先级 FIFO —— Begin/End 的先后顺序因此保持。</summary>
    static void Dispatch(Action a)
    {
        var app = System.Windows.Application.Current;
        if (app == null) { a(); return; }
        try { app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, a); }
        catch { /* UI 线程已在关闭 */ }
    }
}
