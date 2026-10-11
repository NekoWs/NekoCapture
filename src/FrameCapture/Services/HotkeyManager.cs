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
    uint _downTick; // BeginCapture 事件的 GetTickCount 时间戳

    public bool Installed => _hook != IntPtr.Zero;

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
            cap?.VirtualKey ?? 0, scp?.VirtualKey ?? 0, isMod, modsOk);
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
