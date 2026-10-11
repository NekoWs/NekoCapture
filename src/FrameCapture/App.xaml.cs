using System.IO;
using System.Linq;
using System.Windows;

namespace FrameCapture;

/// <summary>应用入口：单实例、全局快捷键、托盘常驻。</summary>
public partial class App : System.Windows.Application
{
    public static App Instance { get; private set; } = null!;

    private System.Threading.Mutex? _mutex;
    private bool _ownsMutex;
    private System.Windows.Interop.HwndSource? _msgSink;

    // 二次实例 → 主实例的转发通道：消息专用窗（HWND_MESSAGE），按窗口名互找
    static readonly IntPtr HWND_MESSAGE = new(-3);
    const string SinkName = "FrameCapture.MessageSink";
    const string MsgOpenSettings = "FrameCapture_OpenSettings";
    const string MsgOpenHelp = "FrameCapture_OpenHelp";

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern uint RegisterWindowMessageW(string lpString);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern System.IntPtr FindWindowExW(System.IntPtr parent, System.IntPtr childAfter, string? cls, string? window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PostMessageW(System.IntPtr h, uint msg, System.IntPtr w, System.IntPtr l);

    protected override void OnStartup(StartupEventArgs e)
    {
        Core.Trace.Log("OnStartup enter");
        base.OnStartup(e);

        // 单实例
        _mutex = new System.Threading.Mutex(true, @"Local\FrameCapture.SingleInstance", out bool owned);
        if (!owned)
        {
            // 已有实例：--settings/--help 转发给它再退（此前静默失效——用户从快捷方式再开一次等于没点）。
            // 注意绝不能 ReleaseMutex：本进程从未持有，释放会抛 ApplicationException 炸出 WER 崩溃弹窗。
            ForwardToPrimary(e.Args);
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }
        _ownsMutex = true;

        Instance = this;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 全局异常日志
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception, "AppDomain");
        DispatcherUnhandledException += (_, e) =>
        {
            LogCrash(e.Exception, "Dispatcher");
            e.Handled = true; // 单点异常不拖垮整个应用
        };

        Services.AppHost.Current.Startup();
        StartMessageSink();

        // 调试/常驻入口：FrameCapture.exe --settings | --help
        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Overlays.SettingsWindow.ShowOrFocus(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (e.Args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Overlays.HelpWindow.ShowOrFocus(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    void StartMessageSink()
    {
        try
        {
            var np = new System.Windows.Interop.HwndSourceParameters(SinkName, 0, 0) { ParentWindow = HWND_MESSAGE };
            _msgSink = new System.Windows.Interop.HwndSource(np);
            uint mSet = RegisterWindowMessageW(MsgOpenSettings), mHelp = RegisterWindowMessageW(MsgOpenHelp);
            _msgSink.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool _) =>
            {
                if (msg == mSet) Dispatcher.BeginInvoke(() => Overlays.SettingsWindow.ShowOrFocus());
                else if (msg == mHelp) Dispatcher.BeginInvoke(() => Overlays.HelpWindow.ShowOrFocus());
                return System.IntPtr.Zero;
            });
        }
        catch (System.Exception ex) { LogCrash(ex, "MsgSink"); }
    }

    static void ForwardToPrimary(string[] args)
    {
        string? name =
            args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)) ? MsgOpenSettings :
            args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase)) ? MsgOpenHelp : null;
        if (name == null) return;
        try
        {
            var sink = FindWindowExW(HWND_MESSAGE, System.IntPtr.Zero, null, SinkName);
            if (sink != System.IntPtr.Zero) PostMessageW(sink, RegisterWindowMessageW(name), System.IntPtr.Zero, System.IntPtr.Zero);
        }
        catch { }
    }

    static void LogCrash(Exception? ex, string source)
    {
        try
        {
            Directory.CreateDirectory(Core.SettingsService.Dir);
            File.AppendAllText(
                Path.Combine(Core.SettingsService.Dir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source}) {ex}\n\n");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Services.AppHost.Current.Shutdown(); } catch { }
        // 只有主实例（真正持有者）能释放；二次实例释放会抛"unsynchronized block"炸掉退出路径
        if (_ownsMutex) { try { _mutex?.ReleaseMutex(); } catch { } }
        _mutex?.Dispose();
        _msgSink?.Dispose();
        base.OnExit(e);
    }
}
