using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Services;

/// <summary>
/// 基于低级键盘钩子的全局快捷键。支持按下/释放事件（长按捕获所需）。
/// 回调内绝不抛异常、不做耗时操作：决策表（<see cref="HookDecision"/>）是纯逻辑，
/// 动作经 AppHost.Dispatch 非阻塞投递到 UI 线程；慢回调看门狗把违反不变量的
/// 现场写进 trace（"hook slow Nms"），回归脚本据此断言。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private IntPtr _hook = IntPtr.Zero;
    private Win32.LowLevelKeyboardProc? _proc;
    private HotkeySpec? _scope, _capture;
    private volatile HookDecision.Key _pressed = HookDecision.Key.None;

    public event Action? ScopeRequested;
    public event Action? CaptureBegin;
    /// <summary>松开捕获键：参数=按住时长 ms（钩子事件时间戳差，不受 UI 线程忙/初始化耗时影响）。</summary>
    public event Action<long>? CaptureEnd;
    /// <summary>滚动截图进行中按 Esc → 取消（R90）。控制条不抢焦点，Esc 本会落到目标窗，故全局钩子拦截。</summary>
    public event Action? ScrollCancel;
    /// <summary>滚动截图会话是否活跃（由控制条生命周期置位：Show=true、Closed=false）。
    /// volatile：钩子线程读、UI 线程写。为 true 时全局吞掉 Esc 并触发 ScrollCancel（QQ 式取消）。</summary>
    public static volatile bool ScrollActive;
    /// <summary>截图浮窗（CaptureToast）活跃时置位：全局吞 Enter/Esc 并回调（浮窗不抢焦点，键不会进它自己的
    /// HWND，只能靠全局钩子代劳——R119 修「浮窗 Enter 无效」）。Enter→进编辑器，Esc→关闭。生命周期绑定浮窗 Show/Closed。</summary>
    public static volatile bool ToastActive;
    public static Action? ToastEnter;
    public static Action? ToastEsc;
    /// <summary>R94 暂停全局热键：为 true 时钩子对所有自有热键放行（不触发、不吞键），
    /// 让键完整落到前台应用——游戏/演示/与其他软件热键冲突时临时避让。托盘「暂停全局热键」切换，
    /// 运行时状态（不持久化，重启自动恢复工作）。滚动会话的 Esc 取消仍优先于本判断。</summary>
    public static volatile bool Paused;
    uint _downTick; // BeginCapture 事件的 GetTickCount 时间戳

    public bool Installed => _hook != IntPtr.Zero;

    /// <summary>R120 B2：手势引擎键事件汇。非 null 时，<see cref="HandleKey"/> 顶部（早于旧热键判定与吞键）
    /// 把每个键事件喂给它，使手势层与旧热键<b>共用同一个低级键盘钩子</b>（不额外挂钩、不重复处理）。
    /// 参数=(vk, 是否按下, 事件时刻修饰键态, 事件时间戳ms)。null=手势层关闭，零开销。Paused 时不喂（用户要求不拦截键）。</summary>
    public Action<uint, bool, GestureCore.ModifierKeys, long>? KeySink;

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

    public void Install(string scopeText, string captureText)
    {
        Uninstall();
        var scope = HotkeySpec.Parse(scopeText);
        var capture = HotkeySpec.Parse(captureText);
        if (scope == null && capture == null) return;

        _scope = scope;
        _capture = capture;
        _proc = HookProc;
        _hook = Win32.SetWindowsHookExW(Win32.WH_KEYBOARD_LL, _proc,
            Win32.GetModuleHandleW(null), 0);
        FrameCapture.Core.Trace.Log($"Install scope={scopeText} capture={captureText} hook={_hook}");
        if (_hook == IntPtr.Zero)
        {
            _proc = null;
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
        _scope = _capture = null;
        _pressed = HookDecision.Key.None;
    }

    private static bool ModifiersMatch(HotkeySpec s)
    {
        bool ctrl = Win32.IsKeyDown(Win32.VK_CONTROL);
        bool alt = Win32.IsKeyDown(Win32.VK_MENU);
        bool shift = Win32.IsKeyDown(Win32.VK_SHIFT);
        bool win = Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN);
        return ctrl == s.Ctrl && alt == s.Alt && shift == s.Shift && win == s.Win;
    }

    /// <summary>当前按下状态是否仍满足捕获组合（允许多余修饰键已松开）。</summary>
    private static bool ModifiersStillSatisfied(HotkeySpec s)
    {
        bool ctrl = Win32.IsKeyDown(Win32.VK_CONTROL);
        bool alt = Win32.IsKeyDown(Win32.VK_MENU);
        bool shift = Win32.IsKeyDown(Win32.VK_SHIFT);
        bool win = Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN);
        return (!s.Ctrl || ctrl) && (!s.Alt || alt) && (!s.Shift || shift) && (!s.Win || win);
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

    /// <summary>查决策表 → 置状态 → 投递动作；返回是否吞掉本键。
    /// 热路径只有几次整数比较：修饰键状态（IsKeyDown）只在可能改变决策时才查。</summary>
    private bool HandleKey(IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
        uint vk = info.vkCode;
        bool isDown = (info.flags & Win32.LLKHF_UP) == 0;
        // 隐私：绝不记录用户按键（全局钩子可见所有键击）。仅调试注入链路时设 FC_TRACE_KEYS=1 才逐键记录。
        if (Environment.GetEnvironmentVariable("FC_TRACE_KEYS") == "1")
            FrameCapture.Core.Trace.Log($"hook key={info.vkCode} down={isDown} installed={Installed}");

        // R120 B2：先把手势引擎喂饱（早于旧热键判定/吞键，保证引擎看到所有键）。手势层关闭时 KeySink=null，零开销。
        // Paused 时不喂：用户暂停全局热键=要求不拦截任何键，手势层同样避让。
        if (!Paused) KeySink?.Invoke(vk, isDown, CurrentMods(), info.time);

        // 滚动截图会话中：Esc 全局取消并吞掉（控制条不抢焦点，否则 Esc 会漏给目标窗）。
        // 热路径仅一次 volatile 读 + 一次整数比较；ScrollActive 绑定控制条生命周期，绝不泄漏成"全局 Esc 失灵"。
        if (isDown && vk == 0x1B && ScrollActive) { ScrollCancel?.Invoke(); return true; }

        // 截图浮窗活跃：Enter=进编辑器、Esc=关闭，全局吞掉（浮窗不抢焦点，键不会进它自己的 HWND，R119）。
        if (isDown && ToastActive && (vk == 0x0D || vk == 0x1B))
        {
            if (vk == 0x0D) ToastEnter?.Invoke(); else ToastEsc?.Invoke();
            return true;
        }

        var cap = _capture;
        var scp = _scope;
        bool primary = cap != null && vk == cap.VirtualKey;
        bool isMod = vk is Win32.VK_CONTROL or Win32.VK_MENU or Win32.VK_SHIFT
            or Win32.VK_LWIN or Win32.VK_RWIN;

        bool captureMatch = isDown && primary && cap != null && ModifiersMatch(cap);
        bool scopeMatch = isDown && !primary && scp != null && vk == scp.VirtualKey && ModifiersMatch(scp);
        bool modsOk = true;
        if (_pressed == HookDecision.Key.CapturePrimary && !isDown && isMod && cap != null)
            modsOk = ModifiersStillSatisfied(cap);

        var o = HookDecision.Decide(_pressed, isDown, vk, captureMatch, scopeMatch,
            cap?.VirtualKey ?? 0, scp?.VirtualKey ?? 0, isMod, modsOk, Paused);
        _pressed = o.Next;
        switch (o.Action)
        {
            case HookDecision.Act.BeginCapture:
                _downTick = info.time;
                CaptureBegin?.Invoke();
                break;
            case HookDecision.Act.EndCapture:
                // info.time=系统事件时间戳：即便 UI 线程正忙（首次 WGC 初始化 ~400ms），
                // 点按/长按判定仍按用户真实按住时长，不会把"点按"误判成"长按"（R42 实测抓到）。
                CaptureEnd?.Invoke(unchecked((int)(info.time - _downTick)));
                break;
            case HookDecision.Act.ScopeSelect: ScopeRequested?.Invoke(); break;
        }
        return o.Swallow;
    }

    public void Dispose() => Uninstall();
}
