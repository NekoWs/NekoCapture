using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Services;

/// <summary>
/// 基于低级键盘钩子的全局快捷键。支持按下/释放事件（长按捕获所需）。
/// 回调内绝不抛异常、不做耗时操作（仅置状态 + 投递到 UI 线程）。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    public enum Hot { None, Scope, CapturePrimary }

    private IntPtr _hook = IntPtr.Zero;
    private Win32.LowLevelKeyboardProc? _proc;
    private HotkeySpec? _scope, _capture;
    private volatile Hot _pressed = Hot.None;

    public event Action? ScopeRequested;
    public event Action? CaptureBegin;
    public event Action? CaptureEnd;

    public bool Installed => _hook != IntPtr.Zero;

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
        _pressed = Hot.None;
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
        try
        {
            var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            uint vk = info.vkCode;
            bool isDown = (info.flags & Win32.LLKHF_UP) == 0;
            // 隐私：绝不记录用户按键（全局钩子可见所有键击）。仅调试注入链路时设 FC_TRACE_KEYS=1 才逐键记录。
            if (Environment.GetEnvironmentVariable("FC_TRACE_KEYS") == "1")
                FrameCapture.Core.Trace.Log($"hook key={info.vkCode} down={isDown} installed={Installed}");

            // 捕获进行中：主键松开，或任一所需修饰键松开 → 结束
            if (_pressed == Hot.CapturePrimary && _capture != null)
            {
                bool primaryUp = vk == _capture.VirtualKey;
                bool modUp = vk is Win32.VK_CONTROL or Win32.VK_MENU or Win32.VK_SHIFT
                    or Win32.VK_LWIN or Win32.VK_RWIN;
                if (!isDown && (primaryUp || (modUp && !ModifiersStillSatisfied(_capture))))
                {
                    _pressed = Hot.None;
                    CaptureEnd?.Invoke();
                    return 1; // 吞掉
                }
                if (vk == _capture.VirtualKey) return 1; // 捕获期间吞掉主键（防止重复按下干扰）
                return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            if (isDown)
            {
                if (_capture != null && vk == _capture.VirtualKey && ModifiersMatch(_capture))
                {
                    _pressed = Hot.CapturePrimary;
                    CaptureBegin?.Invoke();
                    return 1;
                }
                if (_scope != null && vk == _scope.VirtualKey && ModifiersMatch(_scope))
                {
                    _pressed = Hot.Scope;
                    ScopeRequested?.Invoke();
                    return 1;
                }
            }
            else if (_pressed == Hot.Scope)
            {
                // 范围键在按下时已触发，释放只负责复位并吞掉主键
                _pressed = Hot.None;
                if (_scope != null && vk == _scope.VirtualKey) return 1;
            }
        }
        catch
        {
            // 钩子回调内绝不抛出
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
