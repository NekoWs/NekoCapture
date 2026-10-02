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

    static void Dispatch(Action a)
    {
        var app = System.Windows.Application.Current;
        if (app == null) { a(); return; }
        app.Dispatcher.Invoke(a);
    }
}
