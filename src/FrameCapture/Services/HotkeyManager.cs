using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameCapture.Native;

namespace FrameCapture.Services;

/// <summary>
/// 基于低级键盘钩子的键事件源，向手势层喂键并路由选区、录制准备、倒计时与浮窗的会话按键。
///
/// 回调占着全系统输入链：<b>绝不抛异常、绝不做耗时操作</b>，动作一律经 AppHost.Dispatch 非阻塞投递到 UI 线程。
/// 回调耗时超过 LowLevelHooksTimeout 会被系统<b>静默摘钩</b>——热键无声失效，系统侧不留任何痕迹。
/// 慢回调看门狗把违反该不变量的现场写进 trace（"hook slow Nms"），自检脚本 test-outline-lifecycle 断言其永不出现。
/// </summary>
public sealed partial class HotkeyManager : IDisposable
{
    private IntPtr _hook = IntPtr.Zero;
    private Win32.LowLevelKeyboardProc? _proc;
    /// <summary>滚动截图进行中按 Esc → 取消。控制条不抢焦点，Esc 本会落到目标窗，故全局钩子拦截。</summary>
    public event Action? ScrollCancel;
    /// <summary>滚动截图会话是否活跃（由控制条生命周期置位：Show=true、Closed=false）。
    /// volatile：钩子线程读、UI 线程写。为 true 时全局吞掉 Esc 并触发 ScrollCancel。</summary>
    public static bool ScrollActive { get => _scrollActive; set { _scrollActive = value; SessionsChanged(); } }
    /// <summary>延时截图倒计时活跃时置位：全局吞 Esc 并回调（倒计时窗不抢焦点，键不会进它自己的 HWND）。
    /// 生命周期绑定倒计时窗 Show/Closed，绝不泄漏成"全局 Esc 失灵"。</summary>
    public static bool CountdownActive { get => _countdownActive; set { _countdownActive = value; SessionsChanged(); } }
    /// <value>倒计时期间 Esc 的响应。<b>在钩子回调内被同步调用</b>，赋值方自己投递到 UI 线程。</value>
    public static Action? CountdownEsc;
    /// <summary>截图浮窗（CaptureToast）活跃时置位：全局吞 Enter/Esc 并回调（浮窗不抢焦点，键不会进它自己的
    /// HWND，只能靠全局钩子代劳）。Enter→进编辑器，Esc→关闭。生命周期绑定浮窗 Show/Closed。</summary>
    public static bool ToastActive { get => _toastActive; set { _toastActive = value; SessionsChanged(); } }
    /// <value>Enter 的响应，由浮窗 Show 时赋值、Closed 时清空。<b>在钩子回调内被同步调用</b>，
    /// 赋值方必须自己投递（CaptureToast 走 Dispatcher.BeginInvoke），否则回调耗时直接计入摘钩超时。</value>
    public static Action? ToastEnter;
    /// <value>Esc 的响应，赋值时机与线程要求同 <see cref="ToastEnter"/>。</value>
    public static Action? ToastEsc;

    /// <summary>截图浮窗上「复制」的响应；赋值时机与线程要求同 <see cref="ToastEnter"/>。</summary>
    public static Action? ToastCopy;

    /// <summary>浮窗上那条复制键（键位串；空串＝不设）。由设置写入，钩子里按修饰键与主键逐位比对。
    /// 只在浮窗活着那几秒内生效，且要求修饰键完全一致——别的组合照常留给前台应用。</summary>
    public static string ToastCopyChord = "Ctrl+C";

    /// <summary>本键是否正是浮窗那条复制键。</summary>
    /// <param name="vk">钩子给来的虚拟键码。</param>
    /// <returns>主键与修饰键都逐位一致返回 true。</returns>
    static bool MatchToastCopy(uint vk)
    {
        if (Core.EditorChord.Parse(ToastCopyChord) is not { } ch) return false;
        if (System.Windows.Input.KeyInterop.VirtualKeyFromKey(ch.Key) != vk) return false;
        var mods = ch.Mods;
        bool ctrl = (mods & System.Windows.Input.ModifierKeys.Control) != 0;
        bool alt = (mods & System.Windows.Input.ModifierKeys.Alt) != 0;
        bool shift = (mods & System.Windows.Input.ModifierKeys.Shift) != 0;
        bool win = (mods & System.Windows.Input.ModifierKeys.Windows) != 0;
        return Win32.IsKeyDown(Win32.VK_CONTROL) == ctrl
            && Win32.IsKeyDown(Win32.VK_MENU) == alt
            && Win32.IsKeyDown(Win32.VK_SHIFT) == shift
            && (Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN)) == win;
    }
    /// <summary>选区窗（RegionPickerWindow）活跃时置位：全局代收 Esc/Enter/C/F/Tab 并吞掉。
    /// 选区窗是 WS_EX_NOACTIVATE + ShowActivated=false，不夺前台——目标窗（如浏览器）保持焦点，
    /// 其键不会进选区窗自己的 HWND，只能靠全局钩子代劳（与滚动/浮窗同款会话拦截）。
    /// PickerKey 返回 true=本键归选区窗处理（吞掉，前台收不到）；false=放行。生命周期绑定选区窗 Loaded/Closed，
    /// 绝不泄漏成"全局键失灵"。</summary>
    public static bool PickerActive { get => _pickerActive; set { _pickerActive = value; SessionsChanged(); } }
    /// <value>选区窗的键判定，入参 vk，返回 true=本键归选区窗（被吞）。<b>在钩子回调内被同步调用</b>，
    /// 实现方只做判断、把副作用投递出去；选区窗 Closed 时必须置回 null。</value>
    public static Func<uint, bool>? PickerKey;
    /// <summary>准备录制面板的会话按键判定。只匹配确认与取消，副作用投递到 UI 线程；起录或关闭时清空。</summary>
    public static Func<uint, GestureCore.ModifierKeys, bool>? ArmedRecordKey
    { get => _armedRecordKey; set { _armedRecordKey = value; SessionsChanged(); } }
    readonly HashSet<uint> _armedRecordKeyUps = new();
    /// <summary>暂停用户手势并放行其按键；已有捕获会话的确认与取消仍生效。
    /// 由托盘切换，仅影响当前进程。</summary>
    public static bool Paused { get => _paused; set { _paused = value; ResidentBridge.PublishPause(value); } }

    /// <value>钩子句柄非零即已装。<see cref="Paused"/> 只让钩子放行，不改本值——暂停态下钩子仍在。</value>
    public bool Installed => _hook != IntPtr.Zero;

    /// <summary>手势引擎键事件汇，在录键和录制准备的会话按键之后调用，共用同一个低级键盘钩子。
    /// 参数=(vk, 是否按下, 事件时刻修饰键态, 事件时间戳ms)；返回 true=独占吞掉本键。
    /// null=手势层关闭，零开销，且 <see cref="Install"/> 不装钩子。Paused 时不喂（用户要求不拦截键）。</summary>
    public Func<uint, bool, GestureCore.ModifierKeys, long, bool>? KeySink;

    /// <summary>录入态（设置里「按一下录一个键」）的全局键汇：非 null 时优先交给它，先于暂停与手势层。
    /// 走钩子而不是窗口键盘事件，是因为 Win 组合键与 PrtSc 在窗口那条路上根本到不了——
    /// Win+Shift+S 会被系统先接走，中文输入法下的 Shift+字母还会变成 ImeProcessed。
    /// 返回 true 表示这一击归录入框（吞掉，前台与系统都收不到）。
    /// <b>在钩子回调内被同步调用</b>，实现方只做判断与投递，不在这里做重活。</summary>
    public static Func<uint, bool, GestureCore.ModifierKeys, long, bool>? CaptureSink
    { get => _captureSink; set { _captureSink = value; SessionsChanged(); } }

    /// <summary>此刻真实的修饰键状态（读系统键态）。录入框在开始录入时拿它当起点：
    /// 录入开始前就按住的修饰键也要算进第一枚键位里。</summary>
    /// <returns>Ctrl/Shift/Alt/Win 的按位或。</returns>
    public static GestureCore.ModifierKeys ReadMods() => CurrentMods();

    static GestureCore.ModifierKeys CurrentMods()
    {
        GestureCore.ModifierKeys m = 0;
        if (Win32.IsKeyDown(Win32.VK_CONTROL)) m |= GestureCore.ModifierKeys.Control;
        if (Win32.IsKeyDown(Win32.VK_MENU)) m |= GestureCore.ModifierKeys.Alt;
        if (Win32.IsKeyDown(Win32.VK_SHIFT)) m |= GestureCore.ModifierKeys.Shift;
        if (Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN)) m |= GestureCore.ModifierKeys.Win;
        return m;
    }

    /// <summary>慢回调看门狗阈值（ms）：钩子回调占着全系统输入链，超过即记 trace。</summary>
    const double HookSlowWarnMs = 50;

    // 逐键 trace 开关：进程启动时读一次，键鼠钩子回调里不做环境块查找（回调超时会被系统静默摘钩）。
    // 隐私：默认关闭，绝不记录用户按键——仅调试注入链路时设 FC_TRACE_KEYS=1 才逐键记录。
    static readonly bool TraceKeys = Environment.GetEnvironmentVariable("FC_TRACE_KEYS") == "1";

    /// <summary>在独立消息线程安装键盘钩子；重装保留会话已认领的抬起事件。
    /// 无输入源或按需界面无会话时不安装，失败抛 Win32Exception。</summary>
    public void Install()
    {
        StopInputThread();
        if (KeySink == null) return;
        if (ResidentBridge.IsWorker && !HasSessions) return;
        StartInputThread();
    }

    /// <summary>卸载键盘钩子并弃掉委托引用。未装时静默返回，可重复调用。</summary>
    /// <remarks>顺序是<b>先 Unhook 再弃引用</b>：钩子卸掉前系统仍可能回调，托管委托必须一直存活到那一刻，
    /// 否则它的 native thunk 可能被 GC 回收。</remarks>
    public void Uninstall()
    {
        StopInputThread();
        _armedRecordKeyUps.Clear();
        _captureKeyUps.Clear();
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
        bool swallow = false;
        long t0 = Stopwatch.GetTimestamp();
        try { swallow = HandleKey(lParam); }
        catch
        {
            // 钩子回调内绝不抛出
        }
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        if (ms > HookSlowWarnMs) Core.Trace.Log($"hook slow {ms:F0}ms");
        return swallow ? 1 : Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>喂手势引擎 → 处理滚动/浮窗会话键 → 决定是否吞键。返回是否吞掉本键。
    /// 热路径只有几次整数比较：修饰键状态（IsKeyDown）只在手势层需要时查。</summary>
    private bool HandleKey(IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
        uint vk = info.vkCode;
        bool isDown = (info.flags & Win32.LLKHF_UP) == 0;
        var mods = Core.GestureModifiers.AfterKeyboardEvent(CurrentMods(), (int)vk, isDown, Win32.IsKeyDown);
        // 隐私：绝不记录用户按键（全局钩子可见所有键击）。仅调试注入链路时设 FC_TRACE_KEYS=1 才逐键记录。
        if (TraceKeys)
            Core.Trace.Log($"hook key={info.vkCode} down={isDown} installed={Installed}");

        // 录入态最优先：此刻用户在设置里按一个键给我们，任何别的判定都不该先把这一击拿走。
        // 连 Paused 都不看——录入时宿主就是把全局热键暂停掉的，那恰恰是要录的那个键。
        bool readyRelease = !isDown && _armedRecordKeyUps.Count > 0 && _armedRecordKeyUps.Remove(vk);
        if (_captureKeyUps.Contains(vk))
        {
            if (!isDown) { _captureKeyUps.Remove(vk); CaptureSink?.Invoke(vk, false, mods, info.time); SessionsChanged(); }
            return true;
        }
        if (CaptureSink is { } capture)
        {
            if (isDown) _captureKeyUps.Add(vk);
            if (capture(vk, isDown, mods, info.time)) return true;
            if (isDown) { _captureKeyUps.Remove(vk); SessionsChanged(); }
        }
        if (readyRelease) { SessionsChanged(); return true; }
        if ((_armedRecordKeyUps.Count > 0 || ArmedRecordKey != null) && RouteArmedRecordKey(vk, isDown, mods)) return true;

        // 手势引擎喂键（唯一的全局触发路径）。手势层关闭时 KeySink=null，零开销。
        // Paused 时不喂：用户暂停全局热键=要求不拦截任何键，手势层同样避让。
        // 返回值=是否吞键：命中带具体键的组合键即吞，纯修饰键组合与未命中不吞。
        bool gestureSwallow = false;
        if (!Paused && KeySink != null) gestureSwallow = KeySink(vk, isDown, mods, info.time);

        // 滚动截图会话中：Esc 全局取消并吞掉（控制条不抢焦点，否则 Esc 会漏给目标窗）。
        // 热路径仅一次 volatile 读 + 一次整数比较；ScrollActive 绑定控制条生命周期，绝不泄漏成"全局 Esc 失灵"。
        if (isDown && vk == 0x1B && ScrollActive) { ScrollCancel?.Invoke(); return true; }

        // 延时截图倒计时中：Esc 取消这次截图并吞掉（倒计时窗不抢焦点，否则 Esc 会漏给目标窗）。
        if (isDown && vk == 0x1B && CountdownActive) { CountdownEsc?.Invoke(); return true; }

        // 截图浮窗活跃：Enter=进编辑器、Esc=关闭、浮窗那条复制键=复制，全局吞掉
        // （浮窗不抢焦点，键不会进它自己的 HWND）。
        if (isDown && ToastActive)
        {
            if (vk == 0x0D) { ToastEnter?.Invoke(); return true; }
            if (vk == 0x1B) { ToastEsc?.Invoke(); return true; }
            if (ToastCopy != null && MatchToastCopy(vk)) { ToastCopy.Invoke(); return true; }
        }

        // 选区窗活跃：Esc/Enter/C/F/Tab 全局代收并吞掉（选区窗不抢焦点，键不会进它自己的 HWND）。
        // 排在手势喂键与会话键之后、独占吞键判定前面：会话级优先。
        // 选区窗只认裸键（OnPickerKey 内查修饰键，Ctrl/Alt/Win 组合放行给手势与前台）。
        if (isDown && PickerActive && PickerKey != null && PickerKey(vk)) return true;

        // 独占吞键：手势命中「带具体键的组合键」→ 吞掉本键，前台程序收不到（全局独占）。
        // 放在滚动/浮窗判定之后：那两类是会话级优先；且手势只绑具体键(A/R)，不会误吞 Esc/Enter。
        if (gestureSwallow) { Core.Trace.Log("gesture swallow"); return true; }
        return false;
    }

    /// <summary>卸载钩子，等价于 <see cref="Uninstall"/>；宿主 Shutdown 与本类被丢弃都走这里。</summary>
    public void Dispose() { Uninstall(); if (ReferenceEquals(_activeManager, this)) _activeManager = null; }

    bool RouteArmedRecordKey(uint vk, bool down, GestureCore.ModifierKeys mods)
    {
        if (_armedRecordKeyUps.Contains(vk))
        {
            if (!down) _armedRecordKeyUps.Remove(vk);
            return true;
        }
        if (!down || ArmedRecordKey?.Invoke(vk, mods) != true) return false;
        _armedRecordKeyUps.Add(vk); return true;
    }
}
