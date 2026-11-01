using GestureCore;

namespace NekoCapture.Core;

/// <summary>低级键盘回调发生在异步键态更新前，按当前事件修正修饰键。</summary>
internal static class GestureModifiers
{
    /// <summary>合并当前按下或抬起事件，并保留另一侧仍按住的修饰键。</summary>
    /// <param name="snapshot">异步键态快照。</param>
    /// <param name="key">当前虚拟键码。</param>
    /// <param name="down">是否按下。</param>
    /// <param name="isDown">查询另一侧修饰键的状态。</param>
    /// <returns>事件之后的修饰键状态。</returns>
    internal static ModifierKeys AfterKeyboardEvent(ModifierKeys snapshot, int key, bool down, Func<int, bool> isDown)
    {
        var (flag, other) = key switch
        {
            0x10 => (ModifierKeys.Shift, 0), 0x11 => (ModifierKeys.Control, 0), 0x12 => (ModifierKeys.Alt, 0),
            0xA0 => (ModifierKeys.Shift, 0xA1), 0xA1 => (ModifierKeys.Shift, 0xA0),
            0xA2 => (ModifierKeys.Control, 0xA3), 0xA3 => (ModifierKeys.Control, 0xA2),
            0xA4 => (ModifierKeys.Alt, 0xA5), 0xA5 => (ModifierKeys.Alt, 0xA4),
            0x5B => (ModifierKeys.Win, 0x5C), 0x5C => (ModifierKeys.Win, 0x5B),
            _ => (ModifierKeys.None, 0)
        };
        if (flag == ModifierKeys.None) return snapshot;
        return down || (other != 0 && isDown(other)) ? snapshot | flag : snapshot & ~flag;
    }
}
