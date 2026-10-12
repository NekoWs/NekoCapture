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
    private MsgSink? _msgSink;

    // 二次实例 → 主实例的转发通道：消息专用窗（HWND_MESSAGE），按窗口名互找
    static readonly IntPtr HWND_MESSAGE = new(-3);
    const string SinkName = "FrameCapture.MessageSink";
    const string MsgOpenSettings = "FrameCapture_OpenSettings";
    const string MsgOpenHelp = "FrameCapture_OpenHelp";
    const string MsgEditLast = "FrameCapture_EditLast";
    const string MsgScroll = "FrameCapture_Scroll";

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

        // R82：首次启动弹一张欢迎卡，点破两个热键 + 关键手感（新用户装完只见托盘图标、不知按什么键是头号上手摩擦）。
        // 看过一次（FirstRunHintShown）永不再打扰；非激活浮窗不打断输入，ApplicationIdle 时再弹避免和启动抢焦点。
        if (!Services.AppHost.Current.Settings.Current.FirstRunHintShown)
            Dispatcher.BeginInvoke(() =>
            {
                var st = Services.AppHost.Current.Settings;
                if (st.Current.FirstRunHintShown) return;
                st.Mutate(s => s.FirstRunHintShown = true);
                Overlays.WelcomeToast.Show(st.Current.CaptureHotkey, st.Current.ScopeHotkey);
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // 调试/常驻入口：FrameCapture.exe --settings | --help | --edit-last
        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Overlays.SettingsWindow.ShowOrFocus(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (e.Args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Overlays.HelpWindow.ShowOrFocus(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // R78：把最近一次静态截图重新拉回编辑器（与托盘「编辑上次截图」同一入口，也便于脚本调用）
        if (e.Args.Any(a => string.Equals(a, "--edit-last", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.EditLastStill(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // R88：滚动截图入口（托盘「滚动截图」同一入口，也便于脚本/e2e 调用）
        if (e.Args.Any(a => string.Equals(a, "--scroll", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.BeginScrollCapture(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    void StartMessageSink()
    {
        try { _msgSink = new MsgSink(
            () => Dispatcher.BeginInvoke(() => Overlays.SettingsWindow.ShowOrFocus()),
            () => Dispatcher.BeginInvoke(() => Overlays.HelpWindow.ShowOrFocus()),
            () => Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.EditLastStill()),
            () => Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.BeginScrollCapture())); }
        catch (System.Exception ex) { LogCrash(ex, "MsgSink"); }
    }

    /// <summary>真正的 message-only 窗（HWND_MESSAGE 父）：不可见、不进任务栏、绝不抢前台。
    /// 实测教训：WPF 的 HwndSource 不支持 HWND_MESSAGE 父——会建成带 WS_VISIBLE|WS_OVERLAPPEDWINDOW
    /// 的顶层窗，启动瞬间即抢走前台焦点（GetForegroundWindow 直接变成它）。故用 WinForms NativeWindow
    /// 直接建消息专用窗，仅用于接收二次实例转发的"打开设置/帮助"。</summary>
    sealed class MsgSink : System.Windows.Forms.NativeWindow
    {
        readonly Action _onSettings, _onHelp, _onEditLast, _onScroll;
        readonly uint _mSet, _mHelp, _mEditLast, _mScroll;
        public MsgSink(Action onSettings, Action onHelp, Action onEditLast, Action onScroll)
        {
            _onSettings = onSettings; _onHelp = onHelp; _onEditLast = onEditLast; _onScroll = onScroll;
            _mSet = RegisterWindowMessageW(MsgOpenSettings);
            _mHelp = RegisterWindowMessageW(MsgOpenHelp);
            _mEditLast = RegisterWindowMessageW(MsgEditLast);
            _mScroll = RegisterWindowMessageW(MsgScroll);
            CreateHandle(new System.Windows.Forms.CreateParams { Caption = SinkName, Parent = HWND_MESSAGE, Style = 0 });
            Core.Trace.Log($"sink created hwnd={Handle}");
        }
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == _mSet) { Core.Trace.Log("sink→open settings"); _onSettings(); }
            else if (m.Msg == _mHelp) { Core.Trace.Log("sink→open help"); _onHelp(); }
            else if (m.Msg == _mEditLast) { Core.Trace.Log("sink→edit last"); _onEditLast(); }
            else if (m.Msg == _mScroll) { Core.Trace.Log("sink→scroll capture"); _onScroll(); }
            base.WndProc(ref m);
        }
    }

    static void ForwardToPrimary(string[] args)
    {
        string? name =
            args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)) ? MsgOpenSettings :
            args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase)) ? MsgOpenHelp :
            args.Any(a => string.Equals(a, "--edit-last", StringComparison.OrdinalIgnoreCase)) ? MsgEditLast :
            args.Any(a => string.Equals(a, "--scroll", StringComparison.OrdinalIgnoreCase)) ? MsgScroll : null;
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
        _msgSink?.DestroyHandle(); // NativeWindow 无 Dispose，销毁句柄即可
        base.OnExit(e);
    }
}
