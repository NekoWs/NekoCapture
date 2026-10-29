using FrameCapture.Core;
using FrameCapture.Native;
using GC = GestureCore;

namespace FrameCapture.Services;

/// <summary>
/// 手势运行时：把 <see cref="GestureConfig"/> 装进 <see cref="GC.GestureEngine"/>，
/// 键盘事件经 <see cref="HotkeyManager.KeySink"/>（共用同一低级键盘钩子）、鼠标事件经本类自装的 WH_MOUSE_LL 钩子喂入，
/// 引擎消歧后经 <see cref="GestureInvoker"/> 把动作投递到 UI 线程执行。
///
/// 线程模型：两个低级钩子都在 UI 线程安装 → 回调在 UI 线程；Tick 用 DispatcherTimer 也在 UI 线程。
/// 故引擎无并发、无需加锁。钩子回调占着全系统输入链，动作一律 <b>BeginInvoke 投递</b>、绝不在回调内跑重活。
///
/// 总开关：<see cref="GestureConfig.Enabled"/> 默认 true，快捷键因此是功能触发的<b>唯一</b>全局路径。
/// 关掉它 → KeySink 置空、鼠标钩子卸载、Tick 停止，键盘钩子也随之不装，键原样落到前台。
/// 开着时命中即吞键，前台收不到那一键；只清空绑定而保持开关开着：sink 仍挂着、键盘钩子仍装，只是引擎里没有可命中的绑定。
/// </summary>
public sealed class GestureService : IDisposable
{
    readonly AppHost _host;
    readonly GC.GestureEngine _engine;
    readonly GC.GestureEngine _syntheticEngine;
    readonly object _engineGate = new();
    readonly GestureInvoker _invoker;
    IntPtr _mouseHook;
    Win32.LowLevelMouseProc? _mouseProc;
    System.Windows.Threading.DispatcherTimer? _tick;
    bool _mouseInstalled;
    bool _swallow;  // 命中即吞键：手势层开着就吞，界面不提供关它的开关

    /// <summary>建引擎与动作执行器。构造本身不装钩子、不喂配置——副作用全部在 <see cref="Apply"/>。</summary>
    /// <param name="host">宿主，整个生命周期持有；<see cref="Apply"/> 需要它的 <see cref="HotkeyManager"/> 已就位。</param>
    public GestureService(AppHost host)
    {
        _host = host;
        _invoker = new GestureInvoker(host);
        _engine = new GC.GestureEngine(_invoker);
        _syntheticEngine = new GC.GestureEngine(_invoker);
    }

    /// <summary>应用手势配置（UI 线程调用，来自 AppHost.InstallHotkeys / 设置热重载）。</summary>
    /// <param name="cfg">手势配置。null 与 <c>Enabled=false</c> 等价，都表示快捷键完全不参与：
    /// 不挂键盘 sink，HotkeyManager 因此连键盘钩子都不装，前台键零开销。
    /// 鼠标钩子只在存在鼠标触发绑定时才装，Tick 泵只在存在鼠标触发或 Hold 绑定时才开。</param>
    public void Apply(GestureConfig cfg)
    {
        bool on = cfg != null && cfg.Enabled;
        _swallow = on;
        var bindings = on ? cfg!.ToBindings() : new List<GC.GestureBinding>();
        lock (_engineGate) _engine.SetBindings(bindings);
        _syntheticEngine.SetBindings(bindings);
        Trace.Log($"gesture apply on={on} n={bindings.Count} [" +
            string.Join(", ", bindings.Select(b => $"mods={b.Chord.Mods} key={b.Chord.Key}/{b.Trigger}/oc={b.OnComplete.Count}")) + "]");
        bool hasMouse = !ResidentBridge.IsWorker && bindings.Any(b => IsMouse(b.Trigger));
        // Tick 泵：鼠标连击/长按消歧需要，键盘「按住到阈值触发 Hold」也需要。
        bool needsTick = !ResidentBridge.IsWorker && bindings.Any(b => IsMouse(b.Trigger) || b.Trigger == GC.TriggerKind.Hold);

        // 键盘：手势开启才挂 sink；关闭时置空，HotkeyManager 因此连键盘钩子都不装，前台键零开销
        _host.Hotkeys.KeySink = on || ResidentBridge.IsWorker ? FeedKey : null;

        if (on && hasMouse && !_mouseInstalled) InstallMouseHook();
        if ((!on || !hasMouse) && _mouseInstalled) UninstallMouseHook();

        if (on && needsTick) StartTick(); else StopTick();
    }

    static bool IsMouse(GC.TriggerKind t) =>
        t is GC.TriggerKind.Click or GC.TriggerKind.DoubleClick or GC.TriggerKind.TripleClick or GC.TriggerKind.LongPress;

    /// <summary>喂键给引擎；返回是否吞掉本键。快捷键开着且引擎判定命中带具体键的组合键时吞键，
    /// 前台收不到这一键；纯修饰键组合与未命中的键照常放行。</summary>
    bool FeedKey(uint vk, bool down, GC.ModifierKeys mods, long time)
    {
        if (ResidentBridge.IsWorker) return false;
        long tick = GestureClock.Expand(time, Environment.TickCount64);
        bool candidate;
        lock (_engineGate) candidate = down ? _engine.OnKeyDown((int)vk, mods, tick) : _engine.OnKeyUp((int)vk, mods, tick);
        return _swallow && candidate;
    }

    /// <summary>将测试输入送入独立手势状态，复用同一配置和动作执行器。</summary>
    /// <param name="vk">主键虚拟键码。</param>
    /// <param name="down">是否按下。</param>
    /// <param name="mods">合成输入的修饰键。</param>
    /// <param name="time">完整的单调毫秒数。</param>
    /// <returns>测试输入是否匹配绑定。</returns>
    public bool FeedSyntheticKey(uint vk, bool down, GC.ModifierKeys mods, long time)
    {
        if (Environment.GetEnvironmentVariable("FC_TEST_NO_FG") != "1") return false;
        return down ? _syntheticEngine.OnKeyDown((int)vk, mods, time) : _syntheticEngine.OnKeyUp((int)vk, mods, time);
    }

    #region 鼠标钩子 / Tick 泵
    void InstallMouseHook()
    {
        _mouseProc = MouseProc;
        _mouseHook = Win32.SetWindowsHookExW(Win32.WH_MOUSE_LL, _mouseProc, Win32.GetModuleHandleW(null), 0);
        _mouseInstalled = _mouseHook != IntPtr.Zero;
        Trace.Log($"gesture mouse hook={_mouseHook}");
    }
    void UninstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero; _mouseProc = null; _mouseInstalled = false;
    }

    IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        try
        {
            int msg = wParam.ToInt32();
            bool down = msg is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN;
            bool up = msg is Win32.WM_LBUTTONUP or Win32.WM_RBUTTONUP or Win32.WM_MBUTTONUP;
            if (down || up)
            {
                var info = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                var mods = CurrentMods();
                long time = GestureClock.Expand(info.time, Environment.TickCount64);
                lock (_engineGate)
                {
                    if (down) _engine.OnMouseDown(msg, mods, time);
                    else _engine.OnMouseUp(msg, mods, time);
                }
            }
        }
        catch { /* 钩子回调内绝不抛出 */ }
        // 手势层不吞鼠标事件（放行给前台应用）——只观察、不拦截
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    static GC.ModifierKeys CurrentMods()
    {
        GC.ModifierKeys m = 0;
        if (Win32.IsKeyDown(Win32.VK_CONTROL)) m |= GC.ModifierKeys.Control;
        if (Win32.IsKeyDown(Win32.VK_MENU)) m |= GC.ModifierKeys.Alt;
        if (Win32.IsKeyDown(Win32.VK_SHIFT)) m |= GC.ModifierKeys.Shift;
        if (Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN)) m |= GC.ModifierKeys.Win;
        return m;
    }

    void StartTick()
    {
        if (_tick != null) return;
        _tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _tick.Tick += (_, _) => { long now = Environment.TickCount64; lock (_engineGate) _engine.Tick(now); _syntheticEngine.Tick(now); };
        _tick.Start();
    }
    void StopTick() { _tick?.Stop(); _tick = null; }

    /// <summary>卸载鼠标钩子并停掉 Tick 泵。键盘钩子不归本类，由 <see cref="HotkeyManager"/> 自己卸。</summary>
    public void Dispose() { UninstallMouseHook(); StopTick(); }
    #endregion

}
