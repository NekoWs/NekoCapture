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
    const string MsgDelay = "FrameCapture_Delay";
    const string MsgPinClip = "FrameCapture_PinClip";
    const string MsgPauseToggle = "FrameCapture_PauseToggle";
    const string MsgOcr = "FrameCapture_Ocr";
    const string MsgGesture = "FrameCapture_Gesture";

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
                Overlays.WelcomeToast.Show(st.Current.Gestures.CaptureKeyText(), st.Current.Gestures.ScopeKeyText());
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
        // R91：延时截图入口（托盘「延时截图」同一入口，也便于脚本/e2e 调用）
        if (e.Args.Any(a => string.Equals(a, "--delay", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.BeginDelayedCapture(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // R92：从剪贴板贴图入口（托盘「从剪贴板贴图」同一入口，也便于脚本/e2e 调用）
        if (e.Args.Any(a => string.Equals(a, "--pin-clip", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.PinFromClipboard(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // R94：切换「暂停全局热键」（托盘同一入口；也便于用户用 Windows 快捷方式/脚本绑一个暂停热键）
        if (e.Args.Any(a => string.Equals(a, "--pause-toggle", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Tray.TogglePause(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // R148：对当前范围 OCR（手势「识别文字」同一入口，也便于脚本/e2e 调用）
        if (e.Args.Any(a => string.Equals(a, "--ocr", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.OcrStoredStill(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // R150：打开手势配置器（手势「手势配置器」/设置窗按钮同一入口，也便于脚本/e2e 调用与截图审查）
        if (e.Args.Any(a => string.Equals(a, "--gesture", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.GestureEditorHost.ShowOrFocus(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 打开托盘右键菜单（同一入口，便于脚本/e2e 调用与截图审查；用户也可绑一个热键唤出）
        if (e.Args.Any(a => string.Equals(a, "--tray-menu", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Tray.ShowMenu(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 打开滚动截图控制条（模拟拼接进度，便于脚本/e2e 调用与截图审查；不启动真实滚动捕获）
        if (e.Args.Any(a => string.Equals(a, "--scroll-hint", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() =>
            {
                var mon = Capture.Monitors.All().FirstOrDefault(m => m.IsPrimary) ?? Capture.Monitors.Nearest(0, 0);
                var region = new Capture.RectI(mon.Bounds.X + mon.Bounds.W / 2 - 220, mon.Bounds.Y + mon.Bounds.H / 2 - 160, 440, 320);
                var bar = new Overlays.ScrollHintWindow(region, mon.DeviceName);
                bar.OnDone = bar.OnCancel = () => bar.CloseSelf();
                Overlays.ScrollHintWindow.Current = bar;
                bar.Show();
                int seg = 1;
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
                t.Tick += (_, _) => { seg++; bar.SetProgress(seg, 320 + seg * 260); };
                t.Start();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 打开首次启动欢迎卡（便于脚本/e2e 调用与截图审查；不改动 FirstRunHintShown，纯查看）
        if (e.Args.Any(a => string.Equals(a, "--welcome", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() =>
            {
                var st = Services.AppHost.Current.Settings;
                Overlays.WelcomeToast.Show(st.Current.Gestures.CaptureKeyText(), st.Current.Gestures.ScopeKeyText());
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // R160：打开选区窗（截图/选区同一入口，便于脚本/e2e 验证「不抢焦点」与全局键代收）
        if (e.Args.Any(a => string.Equals(a, "--picker", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.BeginScopeSelection(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // R165 调试/e2e：按标题子串截取一个窗口（WGC 本体 + 可选投影），存 PNG，便于取证 alpha/圆角/投影。
        //   FrameCapture.exe --wincap <标题子串> <输出png路径> [--noshadow]
        int wi = Array.FindIndex(e.Args, a => string.Equals(a, "--wincap", StringComparison.OrdinalIgnoreCase));
        if (wi >= 0)
        {
            string title = wi + 1 < e.Args.Length ? e.Args[wi + 1] : "";
            string outp = wi + 2 < e.Args.Length ? e.Args[wi + 2]
                : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FrameCapture", "wincap.png");
            bool shadow = !e.Args.Any(a => string.Equals(a, "--noshadow", StringComparison.OrdinalIgnoreCase));
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    IntPtr h = FindWindowByTitle(title);
                    if (h == IntPtr.Zero) { FrameCapture.Core.Trace.Log($"wincap: no window '{title}'"); return; }
                    var (f, reg) = Capture.WindowCapture.Grab(h, shadow);
                    Services.BgraImage.SavePng(f, outp);
                    FrameCapture.Core.Trace.Log($"wincap saved {outp} {f.W}x{f.H} reg={reg}");
                }
                catch (Exception ex) { FrameCapture.Core.Trace.Log("wincap fail: " + ex); }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>按标题子串（不区分大小写）找第一个可见顶层窗（调试/e2e 用）。</summary>
    static IntPtr FindWindowByTitle(string sub)
    {
        if (string.IsNullOrEmpty(sub)) return IntPtr.Zero;
        IntPtr found = IntPtr.Zero;
        var sb = new System.Text.StringBuilder(512);
        Native.Win32.EnumWindows((h, _) =>
        {
            if (!Native.Win32.IsWindowVisible(h)) return true;
            sb.Clear(); Native.Win32.GetWindowTextW(h, sb, 512);
            if (sb.ToString().IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    void StartMessageSink()
    {
        try { _msgSink = new MsgSink(
            () => Dispatcher.BeginInvoke(() => Overlays.SettingsWindow.ShowOrFocus()),
            () => Dispatcher.BeginInvoke(() => Overlays.HelpWindow.ShowOrFocus()),
            () => Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.EditLastStill()),
            () => Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.BeginScrollCapture()),
            () => Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.BeginDelayedCapture()),
            () => Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.PinFromClipboard()),
            () => Dispatcher.BeginInvoke(() => Services.AppHost.Current.Tray.TogglePause()),
            () => Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.OcrStoredStill()),
            () => Dispatcher.BeginInvoke(() => Services.GestureEditorHost.ShowOrFocus())); }
        catch (System.Exception ex) { LogCrash(ex, "MsgSink"); }
    }

    /// <summary>真正的 message-only 窗（HWND_MESSAGE 父）：不可见、不进任务栏、绝不抢前台。
    /// 实测教训：WPF 的 HwndSource 不支持 HWND_MESSAGE 父——会建成带 WS_VISIBLE|WS_OVERLAPPEDWINDOW
    /// 的顶层窗，启动瞬间即抢走前台焦点（GetForegroundWindow 直接变成它）。故用 WinForms NativeWindow
    /// 直接建消息专用窗，仅用于接收二次实例转发的"打开设置/帮助"。</summary>
    sealed class MsgSink : System.Windows.Forms.NativeWindow
    {
        readonly Action _onSettings, _onHelp, _onEditLast, _onScroll, _onDelay, _onPinClip, _onPauseToggle, _onOcr, _onGesture;
        readonly uint _mSet, _mHelp, _mEditLast, _mScroll, _mDelay, _mPinClip, _mPauseToggle, _mOcr, _mGesture;
        public MsgSink(Action onSettings, Action onHelp, Action onEditLast, Action onScroll, Action onDelay, Action onPinClip, Action onPauseToggle, Action onOcr, Action onGesture)
        {
            _onSettings = onSettings; _onHelp = onHelp; _onEditLast = onEditLast; _onScroll = onScroll; _onDelay = onDelay; _onPinClip = onPinClip; _onPauseToggle = onPauseToggle; _onOcr = onOcr; _onGesture = onGesture;
            _mSet = RegisterWindowMessageW(MsgOpenSettings);
            _mHelp = RegisterWindowMessageW(MsgOpenHelp);
            _mEditLast = RegisterWindowMessageW(MsgEditLast);
            _mScroll = RegisterWindowMessageW(MsgScroll);
            _mDelay = RegisterWindowMessageW(MsgDelay);
            _mPinClip = RegisterWindowMessageW(MsgPinClip);
            _mPauseToggle = RegisterWindowMessageW(MsgPauseToggle);
            _mOcr = RegisterWindowMessageW(MsgOcr);
            _mGesture = RegisterWindowMessageW(MsgGesture);
            CreateHandle(new System.Windows.Forms.CreateParams { Caption = SinkName, Parent = HWND_MESSAGE, Style = 0 });
            Core.Trace.Log($"sink created hwnd={Handle}");
        }
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == _mSet) { Core.Trace.Log("sink→open settings"); _onSettings(); }
            else if (m.Msg == _mHelp) { Core.Trace.Log("sink→open help"); _onHelp(); }
            else if (m.Msg == _mEditLast) { Core.Trace.Log("sink→edit last"); _onEditLast(); }
            else if (m.Msg == _mScroll) { Core.Trace.Log("sink→scroll capture"); _onScroll(); }
            else if (m.Msg == _mDelay) { Core.Trace.Log("sink→delay capture"); _onDelay(); }
            else if (m.Msg == _mPinClip) { Core.Trace.Log("sink→pin clipboard"); _onPinClip(); }
            else if (m.Msg == _mPauseToggle) { Core.Trace.Log("sink→pause toggle"); _onPauseToggle(); }
            else if (m.Msg == _mOcr) { Core.Trace.Log("sink→ocr range"); _onOcr(); }
            else if (m.Msg == _mGesture) { Core.Trace.Log("sink→gesture editor"); _onGesture(); }
            base.WndProc(ref m);
        }
    }

    static void ForwardToPrimary(string[] args)
    {
        string? name =
            args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)) ? MsgOpenSettings :
            args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase)) ? MsgOpenHelp :
            args.Any(a => string.Equals(a, "--edit-last", StringComparison.OrdinalIgnoreCase)) ? MsgEditLast :
            args.Any(a => string.Equals(a, "--scroll", StringComparison.OrdinalIgnoreCase)) ? MsgScroll :
            args.Any(a => string.Equals(a, "--delay", StringComparison.OrdinalIgnoreCase)) ? MsgDelay :
            args.Any(a => string.Equals(a, "--pin-clip", StringComparison.OrdinalIgnoreCase)) ? MsgPinClip :
            args.Any(a => string.Equals(a, "--pause-toggle", StringComparison.OrdinalIgnoreCase)) ? MsgPauseToggle :
            args.Any(a => string.Equals(a, "--ocr", StringComparison.OrdinalIgnoreCase)) ? MsgOcr :
            args.Any(a => string.Equals(a, "--gesture", StringComparison.OrdinalIgnoreCase)) ? MsgGesture : null;
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
