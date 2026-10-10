namespace FrameCapture.Core;

/// <summary>
/// 低级键盘钩子的决策表（纯逻辑，可单测）。钩子回调只做三件事：
/// 查表 → 置状态 → 投递动作，然后返回"吞/放行"。回调内绝不做耗时操作
/// （曾经的教训：在回调里阻塞等 UI 线程执行 BeginCapture，全系统键盘跟着卡，
///  且超过 LowLevelHooksTimeout 会被系统静默摘钩——快捷键无声失效）。
/// 语义要点（逐条对应 HotkeyManager 原实现，行为不变）：
///  - 捕获进行中：主键松开 → 结束；任一"所需"修饰键松开（多余修饰键不算）→ 结束；
///    主键在捕获期间的重复按下/松开一律吞掉但不重复触发；其余键放行。
///  - 空闲按下：捕获键（修饰键精确匹配）→ 开始；范围键 → 选区。范围键按住时
///    再按捕获键，允许直接进捕获（原行为）。
///  - 范围键的松开只负责复位并吞掉主键（按下时已触发）。
/// </summary>
public static class HookDecision
{
    public enum Key { None, Scope, CapturePrimary }
    public enum Act { Pass, BeginCapture, EndCapture, ScopeSelect }
    public readonly record struct Outcome(Act Action, bool Swallow, Key Next);

    /// <param name="pressed">当前按下状态机。</param>
    /// <param name="isDown">本事件是按下（true）还是松开（false）。</param>
    /// <param name="vk">本事件的虚拟键码。</param>
    /// <param name="captureMatch">按下捕获组合键（修饰键精确匹配）。</param>
    /// <param name="scopeMatch">按下范围组合键（修饰键精确匹配）。</param>
    /// <param name="captureVk">捕获主键键码（无热键时传 0）。</param>
    /// <param name="scopeVk">范围主键键码（无热键时传 0）。</param>
    /// <param name="isModifierKey">本键是修饰键（Ctrl/Alt/Shift/Win）。</param>
    /// <param name="modsStillSatisfied">松开修饰键后，捕获组合所需的修饰键是否仍全部按住。</param>
    public static Outcome Decide(Key pressed, bool isDown, uint vk,
        bool captureMatch, bool scopeMatch, uint captureVk, uint scopeVk,
        bool isModifierKey, bool modsStillSatisfied)
    {
        if (pressed == Key.CapturePrimary)
        {
            bool primary = vk == captureVk;
            if (!isDown && (primary || (isModifierKey && !modsStillSatisfied)))
                return new Outcome(Act.EndCapture, true, Key.None);
            if (primary)
                return new Outcome(Act.Pass, true, Key.CapturePrimary); // 捕获期间吞掉主键，防重复触发
            return new Outcome(Act.Pass, false, Key.CapturePrimary);
        }
        if (isDown)
        {
            if (captureMatch) return new Outcome(Act.BeginCapture, true, Key.CapturePrimary);
            if (scopeMatch) return new Outcome(Act.ScopeSelect, true, Key.Scope);
        }
        else if (pressed == Key.Scope && vk == scopeVk)
        {
            return new Outcome(Act.Pass, true, Key.None); // 范围键松开：复位 + 吞掉
        }
        return new Outcome(Act.Pass, false, pressed);
    }
}
