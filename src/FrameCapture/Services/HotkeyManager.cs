using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Services;

/// <summary>
/// 基于低级键盘钩子的全局键事件源。本类自身不做热键决策，钩子只承载三件事——手势层喂键
/// （<see cref="KeySink"/>，唯一的全局触发路径）、滚动截图 Esc 全局取消、截图浮窗 Enter/Esc。
///
/// 回调占着全系统输入链：<b>绝不抛异常、绝不做耗时操作</b>，动作一律经 AppHost.Dispatch 非阻塞投递到 UI 线程。
/// 回调耗时超过 LowLevelHooksTimeout 会被系统<b>静默摘钩</b>——热键无声失效，系统侧不留任何痕迹。
/// 慢回调看门狗把违反该不变量的现场写进 trace（"hook slow Nms"），自检脚本 test-outline-lifecycle 断言其永不出现。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private IntPtr _hook = IntPtr.Zero;
    private Win32.LowLevelKeyboardProc? _proc;
    /// <summary>滚动截图进行中按 Esc → 取消。控制条不抢焦点，Esc 本会落到目标窗，故全局钩子拦截。</summary>
    public event Action? ScrollCancel;
    /// <summary>滚动截图会话是否活跃（由控制条生命周期置位：Show=true、Closed=false）。
    /// volatile：钩子线程读、UI 线程写。为 true 时全局吞掉 Esc 并触发 ScrollCancel（QQ 式取消）。</summary>
    public static volatile bool ScrollActive;
    /// <summary>截图浮窗（CaptureToast）活跃时置位：全局吞 Enter/Esc 并回调（浮窗不抢焦点，键不会进它自己的
    /// HWND，只能靠全局钩子代劳）。Enter→进编辑器，Esc→关闭。生命周期绑定浮窗 Show/Closed。</summary>
    public static volatile bool ToastActive;
    /// <value>Enter 的响应，由浮窗 Show 时赋值、Closed 时清空。<b>在钩子回调内被同步调用</b>，
    /// 赋值方必须自己投递（CaptureToast 走 Dispatcher.BeginInvoke），否则回调耗时直接计入摘钩超时。</value>
    public static Action? ToastEnter;
    /// <value>Esc 的响应，赋值时机与线程要求同 <see cref="ToastEnter"/>。</value>
    public static Action? ToastEsc;
    /// <summary>选区窗（RegionPickerWindow）活跃时置位：全局代收 Esc/Enter/C/F/Tab 并吞掉。
    /// 选区窗是 WS_EX_NOACTIVATE + ShowActivated=false，不夺前台——目标窗（如浏览器）保持焦点，
    /// 其键不会进选区窗自己的 HWND，只能靠全局钩子代劳（与滚动/浮窗同款会话拦截）。
    /// PickerKey 返回 true=本键归选区窗处理（吞掉，前台收不到）；false=放行。生命周期绑定选区窗 Loaded/Closed，
    /// 绝不泄漏成"全局键失灵"。</summary>
    public static volatile bool PickerActive;
    /// <value>选区窗的键判定，入参 vk，返回 true=本键归选区窗（被吞）。<b>在钩子回调内被同步调用</b>，
    /// 实现方只做判断、把副作用投递出去；选区窗 Closed 时必须置回 null。</value>
    public static Func<uint, bool>? PickerKey;
    /// <summary>暂停全局热键：为 true 时不喂手势、不吞任何键，
    /// 让键完整落到前台应用——游戏/演示/与其他软件热键冲突时临时避让。托盘「暂停全局热键」切换，
    /// 运行时状态（不持久化，重启自动恢复工作）。滚动会话的 Esc 取消仍优先于本判断。</summary>
    public static volatile bool Paused;

    /// <value>钩子句柄非零即已装。<see cref="Paused"/> 只让钩子放行，不改本值——暂停态下钩子仍在。</value>
    public bool Installed => _hook != IntPtr.Zero;

    /// <summary>手势引擎键事件汇。非 null 时 <see cref="HandleKey"/> 第一件事就是喂它，先于全部吞键判定，
    /// 手势层因此与本类<b>共用同一个低级键盘钩子</b>（不额外挂钩、不重复处理）。
    /// 参数=(vk, 是否按下, 事件时刻修饰键态, 事件时间戳ms)；返回 true=独占吞掉本键。
    /// null=手势层关闭，零开销，且 <see cref="Install"/> 不装钩子。Paused 时不喂（用户要求不拦截键）。</summary>
    public Func<uint, bool, GestureCore.ModifierKeys, long, bool>? KeySink;

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

    /// <summary>装全局低级键盘钩子。钩子承载三件事：手势喂键（<see cref="KeySink"/>）、滚动截图 Esc 全局取消、
    /// 截图浮窗 Enter/Esc。<c>KeySink=null</c> 时不装——滚动/浮窗会话只在手势触发时才活跃，无 KeySink 即无会话需求。
    /// 幂等：内部先卸后装。装失败抛 <see cref="System.ComponentModel.Win32Exception"/>，由调用方决定怎么呈现。</summary>
    public void Install()
    {
        Uninstall();
        if (KeySink == null) return;
        _proc = HookProc;
        _hook = Win32.SetWindowsHookExW(Win32.WH_KEYBOARD_LL, _proc,
            Win32.GetModuleHandleW(null), 0);
        FrameCapture.Core.Trace.Log($"Install hook={_hook}");
        if (_hook == IntPtr.Zero)
        {
            _proc = null;
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    /// <summary>卸载键盘钩子并弃掉委托引用。未装时静默返回，可重复调用。</summary>
    /// <remarks>顺序是<b>先 Unhook 再弃引用</b>：钩子卸掉前系统仍可能回调，托管委托必须一直存活到那一刻，
    /// 否则它的 native thunk 可能被 GC 回收。</remarks>
    public void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
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
        if (ms > HookSlowWarnMs) FrameCapture.Core.Trace.Log($"hook slow {ms:F0}ms");
        return swallow ? 1 : Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>喂手势引擎 → 处理滚动/浮窗会话键 → 决定是否吞键。返回是否吞掉本键。
    /// 热路径只有几次整数比较：修饰键状态（IsKeyDown）只在手势层需要时查。</summary>
    private bool HandleKey(IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
        uint vk = info.vkCode;
        bool isDown = (info.flags & Win32.LLKHF_UP) == 0;
        // 隐私：绝不记录用户按键（全局钩子可见所有键击）。仅调试注入链路时设 FC_TRACE_KEYS=1 才逐键记录。
        if (Environment.GetEnvironmentVariable("FC_TRACE_KEYS") == "1")
            FrameCapture.Core.Trace.Log($"hook key={info.vkCode} down={isDown} installed={Installed}");

        // 手势引擎喂键（唯一的全局触发路径）。手势层关闭时 KeySink=null，零开销。
        // Paused 时不喂：用户暂停全局热键=要求不拦截任何键，手势层同样避让。
        // 返回值=是否独占吞键（命中带具体键的组合键且开启 Exclusive）。
        bool gestureSwallow = false;
        if (!Paused && KeySink != null) gestureSwallow = KeySink(vk, isDown, CurrentMods(), info.time);

        // 滚动截图会话中：Esc 全局取消并吞掉（控制条不抢焦点，否则 Esc 会漏给目标窗）。
        // 热路径仅一次 volatile 读 + 一次整数比较；ScrollActive 绑定控制条生命周期，绝不泄漏成"全局 Esc 失灵"。
        if (isDown && vk == 0x1B && ScrollActive) { ScrollCancel?.Invoke(); return true; }

        // 截图浮窗活跃：Enter=进编辑器、Esc=关闭，全局吞掉（浮窗不抢焦点，键不会进它自己的 HWND）。
        if (isDown && ToastActive && (vk == 0x0D || vk == 0x1B))
        {
            if (vk == 0x0D) ToastEnter?.Invoke(); else ToastEsc?.Invoke();
            return true;
        }

        // 选区窗活跃：Esc/Enter/C/F/Tab 全局代收并吞掉（选区窗不抢焦点，键不会进它自己的 HWND）。
        // 排在手势喂键与会话键之后、独占吞键判定前面：会话级优先。
        // 选区窗只认裸键（OnPickerKey 内查修饰键，Ctrl/Alt/Win 组合放行给手势与前台）。
        if (isDown && PickerActive && PickerKey != null && PickerKey(vk)) return true;

        // 独占吞键：手势命中「带具体键的组合键」→ 吞掉本键，前台程序收不到（QQ 式全局独占）。
        // 放在滚动/浮窗判定之后：那两类是会话级优先；且手势只绑具体键(A/R)，不会误吞 Esc/Enter。
        if (gestureSwallow) { FrameCapture.Core.Trace.Log("gesture swallow"); return true; }
        return false;
    }

    /// <summary>卸载钩子，等价于 <see cref="Uninstall"/>；宿主 Shutdown 与本类被丢弃都走这里。</summary>
    public void Dispose() => Uninstall();
}
