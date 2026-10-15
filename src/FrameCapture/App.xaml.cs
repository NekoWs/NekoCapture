using System.IO;
using System.Windows;

namespace FrameCapture;

/// <summary>应用入口：单实例、全局快捷键、托盘常驻。</summary>
public partial class App : Application
{
    public static App Instance { get; private set; } = null!;

    private Mutex? _mutex;
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
    static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string? cls, string? window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);

    protected override void OnStartup(StartupEventArgs e)
    {
        Core.Trace.Log("OnStartup enter");
        base.OnStartup(e);

        // 单实例
        _mutex = new Mutex(true, @"Local\FrameCapture.SingleInstance", out bool owned);
        if (!owned)
        {
            // 已有实例：--settings/--help 转发给它再退——从快捷方式再开一次必须看得见效果，不能静默退出。
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

        // 首次启动弹一张欢迎卡，点破两个热键 + 关键手感（新用户装完只见托盘图标、不知按什么键是头号上手摩擦）。
        // FirstRunHintShown 置位后本条件即消失；非激活浮窗不打断输入，ApplicationIdle 时再弹避免和启动抢焦点。
        if (!Services.AppHost.Current.Settings.Current.FirstRunHintShown)
            Dispatcher.BeginInvoke(() =>
            {
                var st = Services.AppHost.Current.Settings;
                if (st.Current.FirstRunHintShown) return;
                st.Mutate(s => s.FirstRunHintShown = true);
                Overlays.WelcomeToast.Show(st.Current.Gestures.CaptureKeyText(), st.Current.Gestures.RecordKeyText());
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        else if (Services.AppHost.Current.Settings.Current.StartupToast)
            // 之后每次启动只报一句「已启动」。本程序没有主窗口，缺这一下容易被当成没启动成功。
            // 与欢迎卡互斥：同一次启动只出现一条，两条气泡落在同一个角落只会互相压住。
            Dispatcher.BeginInvoke(() =>
            {
                var s = Services.AppHost.Current.Settings.Current;
                Overlays.NoticeToast.Show("FrameCapture 已启动",
                    $"已在托盘待命 · 截图 {s.Gestures.CaptureKeyText()} · 录制 {s.Gestures.RecordKeyText()}",
                    "\uE73E");
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // 调试/常驻入口：FrameCapture.exe --settings | --help | --edit-last
        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Overlays.SettingsWindow.ShowOrFocus(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (e.Args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Overlays.HelpWindow.ShowOrFocus(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 把最近一次静态截图重新拉回编辑器（与托盘「编辑上次截图」同一入口，也便于脚本调用）
        if (e.Args.Any(a => string.Equals(a, "--edit-last", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.EditLastStill(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 滚动截图入口（托盘「滚动截图」同一入口，也便于脚本/e2e 调用）
        if (e.Args.Any(a => string.Equals(a, "--scroll", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.BeginScrollCapture(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 延时截图入口（托盘「延时截图」同一入口，也便于脚本/e2e 调用）
        if (e.Args.Any(a => string.Equals(a, "--delay", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.BeginDelayedCapture(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 从剪贴板贴图入口（托盘「从剪贴板贴图」同一入口，也便于脚本/e2e 调用）
        if (e.Args.Any(a => string.Equals(a, "--pin-clip", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.PinFromClipboard(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 切换「暂停全局热键」（托盘同一入口；也便于用户用 Windows 快捷方式/脚本绑一个暂停热键）
        if (e.Args.Any(a => string.Equals(a, "--pause-toggle", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Tray.TogglePause(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 对当前范围 OCR（手势「识别文字」同一入口，也便于脚本/e2e 调用）
        if (e.Args.Any(a => string.Equals(a, "--ocr", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.OcrStoredStill(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 打开手势配置器（手势「手势配置器」/设置窗按钮同一入口，也便于脚本/e2e 调用与截图审查）
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
                Overlays.WelcomeToast.Show(st.Current.Gestures.CaptureKeyText(), st.Current.Gestures.RecordKeyText());
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 弹一条启动气泡（便于脚本/e2e 调用与截图审查；措辞与真实启动路径同源，热键取当前设置）
        if (e.Args.Any(a => string.Equals(a, "--notice", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() =>
            {
                var s = Services.AppHost.Current.Settings.Current;
                Overlays.NoticeToast.Show("FrameCapture 已启动",
                    $"已在托盘待命 · 截图 {s.Gestures.CaptureKeyText()} · 录制 {s.Gestures.RecordKeyText()}",
                    "\uE73E");
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 打开选区窗（截图/选区同一入口，便于脚本/e2e 验证「不抢焦点」与全局键代收）
        if (e.Args.Any(a => string.Equals(a, "--picker", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.BeginScopeSelection(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // 调试/e2e：以一张 PNG 当静图产物直接开编辑器，供 UICap 审查编辑器版面（不走真实捕获链路）。
        // FrameCapture.exe --editor <png 路径>
        int edi = Array.FindIndex(e.Args, a => string.Equals(a, "--editor", StringComparison.OrdinalIgnoreCase));
        if (edi >= 0)
        {
            string png = edi + 1 < e.Args.Length ? e.Args[edi + 1] : "";
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.OpenEditorWithPng(png),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        // 调试/e2e：以一张 PNG 当静图产物直接钉成贴图，供 UICap 审查贴图窗口版面。
        // FrameCapture.exe --pin <png 路径>
        int pni = Array.FindIndex(e.Args, a => string.Equals(a, "--pin", StringComparison.OrdinalIgnoreCase));
        if (pni >= 0)
        {
            string png = pni + 1 < e.Args.Length ? e.Args[pni + 1] : "";
            Dispatcher.BeginInvoke(() => Services.AppHost.Current.Controller.PinWithPng(png),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        // 调试/e2e：摆出滚动截图的取景层 + 实时预览 + 控制条（喂一张合成长图，不启动真实滚动捕获），
        // 供 UICap desk 截图审查。FrameCapture.exe --scrollpreview <合成长图输出png>
        int spi = Array.FindIndex(e.Args, a => string.Equals(a, "--scrollpreview", StringComparison.OrdinalIgnoreCase));
        if (spi >= 0)
        {
            string spOut = spi + 1 < e.Args.Length ? e.Args[spi + 1]
                : Path.Combine(Path.GetTempPath(), "FrameCapture", "scrollpreview.png");
            Dispatcher.BeginInvoke(() => ShowScrollPreviewScene(spOut),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        // 调试/e2e：按标题子串截取一个窗口（WGC 本体 + 可选投影），存 PNG，便于取证 alpha/圆角/投影。
        // FrameCapture.exe --wincap <标题子串> <输出png路径> [--noshadow]
        int wi = Array.FindIndex(e.Args, a => string.Equals(a, "--wincap", StringComparison.OrdinalIgnoreCase));
        if (wi >= 0)
        {
            string title = wi + 1 < e.Args.Length ? e.Args[wi + 1] : "";
            string outp = wi + 2 < e.Args.Length ? e.Args[wi + 2]
                : Path.Combine(Path.GetTempPath(), "FrameCapture", "wincap.png");
            bool shadow = !e.Args.Any(a => string.Equals(a, "--noshadow", StringComparison.OrdinalIgnoreCase));
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    IntPtr h = FindWindowByTitle(title);
                    if (h == IntPtr.Zero) { Core.Trace.Log($"wincap: no window '{title}'"); return; }
                    var (f, reg) = Capture.WindowCapture.Grab(h, shadow);
                    Services.BgraImage.SavePng(f, outp);
                    Core.Trace.Log($"wincap saved {outp} {f.W}x{f.H} reg={reg}");
                }
                catch (Exception ex) { Core.Trace.Log("wincap fail: " + ex); }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        // 调试/e2e：定格 vs 实时取证。先抓全屏定格帧并裁出区域 R（＝定格截图会得到的内容），
        // 再在屏幕上盖一层不透明品红窗改变画面，实时重抓 R（＝实时截图会得到的内容）。
        // 输出并排图 [定格裁出 | 实时]：左＝定格帧内容，右＝盖品红后的实时画面；二者不同即证明定格裁的是定格帧、不是重抓。
        int ffi = Array.FindIndex(e.Args, a => string.Equals(a, "--freezetest", StringComparison.OrdinalIgnoreCase));
        if (ffi >= 0)
        {
            string outp = ffi + 1 < e.Args.Length ? e.Args[ffi + 1]
                : Path.Combine(Path.GetTempPath(), "FrameCapture", "freezetest.png");
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var mons = Capture.Monitors.All();
                    var mon = mons.FirstOrDefault(m => m.IsPrimary) ?? mons[0];
                    var full = Capture.LiveCapture.Grab(mon.Bounds);
                    if (full == null) { Core.Trace.Log("freezetest: grab full null"); return; }
                    var R = new Capture.RectI(mon.Bounds.X + 240, mon.Bounds.Y + 240, 360, 240);
                    var frozenCrop = Services.BgraImage.Crop(full, R.X - mon.Bounds.X, R.Y - mon.Bounds.Y, R.W, R.H);
                    if (frozenCrop == null) { Core.Trace.Log("freezetest: crop null"); return; }
                    var liveBefore = Capture.LiveCapture.Grab(R);
                    bool staticMatch = liveBefore != null && liveBefore.Bgra.AsSpan().SequenceEqual(frozenCrop.Bgra);
                    using (var w = new System.Windows.Forms.Form
                    {
                        TopMost = true, FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
                        StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                        Bounds = new System.Drawing.Rectangle(mon.Bounds.X, mon.Bounds.Y, mon.Bounds.W, mon.Bounds.H),
                        BackColor = System.Drawing.Color.Magenta, ShowInTaskbar = false,
                    })
                    {
                        w.Show();
                        Thread.Sleep(220);
                        var liveAfter = Capture.LiveCapture.Grab(R);
                        w.Close();
                        bool changed = liveAfter != null && !liveAfter.Bgra.AsSpan().SequenceEqual(frozenCrop.Bgra);
                        int gap = 12, cw = frozenCrop.W, chh = Math.Max(frozenCrop.H, liveAfter?.H ?? 0), stride = cw * 2 + gap;
                        var comp = new byte[(long)stride * chh * 4];
                        for (int y = 0; y < frozenCrop.H; y++)
                            Buffer.BlockCopy(frozenCrop.Bgra, y * cw * 4, comp, y * stride * 4, cw * 4);
                        if (liveAfter != null)
                            for (int y = 0; y < liveAfter.H; y++)
                                Buffer.BlockCopy(liveAfter.Bgra, y * liveAfter.W * 4, comp, y * stride * 4 + (cw + gap) * 4, liveAfter.W * 4);
                        Services.BgraImage.SavePng(new Capture.CapturedFrame { Bgra = comp, W = stride, H = chh, TimeMs = 0 }, outp);
                        Core.Trace.Log($"freezetest saved {outp} staticMatch={staticMatch} changed={changed}");
                    }
                }
                catch (Exception ex) { Core.Trace.Log("freezetest fail: " + ex); }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>摆出滚动截图三件套（取景层 / 实时预览卡 / 控制条）并喂一张合成长图，供截图审查；不启动真实滚动捕获。</summary>
    /// <param name="outp">合成长图的落盘路径，用来与预览卡片逐行对照。</param>
    static void ShowScrollPreviewScene(string outp)
    {
        try
        {
            Native.Win32.GetCursorPos(out var pt);
            var mon = Capture.Monitors.Nearest(pt.X, pt.Y);
            int rw = Math.Min(900, mon.Bounds.W - 40), rh = Math.Min(520, mon.Bounds.H - 40);
            var region = new Capture.RectI(mon.Bounds.X + (mon.Bounds.W - rw) / 2,
                mon.Bounds.Y + (mon.Bounds.H - rh) / 2, rw, rh);
            const int accH = 6000;
            var acc = new byte[(long)rw * accH * 4];
            for (int i = 0; i < acc.Length; i += 4) { acc[i] = 255; acc[i + 1] = 255; acc[i + 2] = 255; acc[i + 3] = 255; }
            // 白底 + 每 22 行一条深色文字带（带宽随段号变化）：缩放对不对，一眼可辨
            for (int y = 0, k = 0; y < accH; y += 22, k++)
            {
                int xEnd = rw - 24 - (k % 9) * 60;
                for (int yy = y; yy < Math.Min(y + 8, accH); yy++)
                    for (int x = 24; x < xEnd; x++)
                    {
                        int o = (yy * rw + x) * 4;
                        acc[o] = 0x33; acc[o + 1] = 0x36; acc[o + 2] = 0x3A; acc[o + 3] = 255;
                    }
            }
            // 末 4 行铺强调色：预览底部看得见这条色带，说明抽样取到了最后一行
            var ac = Core.Theme.Accent;
            for (int yy = accH - 4; yy < accH; yy++)
                for (int x = 0; x < rw; x++)
                {
                    int o = (yy * rw + x) * 4;
                    acc[o] = ac.B; acc[o + 1] = ac.G; acc[o + 2] = ac.R; acc[o + 3] = 255;
                }
            Services.BgraImage.SavePng(new Capture.CapturedFrame { Bgra = acc, W = rw, H = accH, TimeMs = 0 }, outp);

            // 与真实会话同一顺序：取景层 → 预览卡 → 控制条（同为 Topmost，Show 顺序决定 Z 序）
            var stage = new Overlays.ScrollStageWindow(region, mon.DeviceName);
            stage.Show();
            var preview = new Overlays.ScrollPreviewWindow(region, mon.DeviceName);
            preview.SetImage(acc, rw, accH);
            preview.Show();
            var bar = new Overlays.ScrollHintWindow(region, mon.DeviceName);
            bool auto = false;
            bar.OnDone = bar.OnCancel = () => { stage.CloseSelf(); preview.CloseSelf(); bar.CloseSelf(); };
            bar.OnToggleAutoScroll = () => { auto = !auto; bar.SetAutoScrolling(auto); };
            bar.SetProgress(1, accH);
            Overlays.ScrollHintWindow.Current = bar;
            bar.Show();
            Core.Trace.Log($"scrollpreview shown region={region} accH={accH} out={outp}");
        }
        catch (Exception ex) { Core.Trace.Log("scrollpreview fail: " + ex); }
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
        catch (Exception ex) { LogCrash(ex, "MsgSink"); }
    }

    /// <summary>真正的 message-only 窗（HWND_MESSAGE 父）：不可见、不进任务栏、绝不抢前台。
    /// WPF 的 HwndSource 不支持 HWND_MESSAGE 父——会建成带 WS_VISIBLE|WS_OVERLAPPEDWINDOW
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
            var sink = FindWindowExW(HWND_MESSAGE, IntPtr.Zero, null, SinkName);
            if (sink != IntPtr.Zero) PostMessageW(sink, RegisterWindowMessageW(name), IntPtr.Zero, IntPtr.Zero);
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
