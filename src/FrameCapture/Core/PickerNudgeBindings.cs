using System.Windows.Input;
using FrameCapture.Capture;
using GE = GestureCore.ModifierKeys;

namespace FrameCapture.Core;

/// <summary>选区微调的只读键位快照，确认与取消键优先，重复绑定仅首项生效。</summary>
internal sealed class PickerNudgeBindings
{
    readonly List<(uint Key, GE Mods, int X, int Y)> _bindings = new();

    /// <summary>解析当前选区的四向键位，不在键盘回调中分配或解析字符串。</summary>
    /// <param name="settings">打开选区时的设置。</param>
    internal PickerNudgeBindings(AppSettings settings)
    {
        var confirm = EditorChord.Parse(settings.PickerConfirmChord);
        var cancel = EditorChord.Parse(settings.PickerCancelChord);
        void Add(string chord, int x, int y)
        {
            if (EditorChord.Parse(chord) is not { } parsed || parsed == confirm || parsed == cancel
                || parsed.Key is Key.Escape or Key.Return) return;
            GE mods = GE.None;
            if (parsed.Mods.HasFlag(ModifierKeys.Control)) mods |= GE.Control;
            if (parsed.Mods.HasFlag(ModifierKeys.Alt)) mods |= GE.Alt;
            if (parsed.Mods.HasFlag(ModifierKeys.Shift)) mods |= GE.Shift;
            if (parsed.Mods.HasFlag(ModifierKeys.Windows)) mods |= GE.Win;
            uint key = (uint)KeyInterop.VirtualKeyFromKey(parsed.Key);
            if (_bindings.Any(binding => binding.Key == key && binding.Mods == mods)) return;
            _bindings.Add((key, mods, x, y));
        }
        Add(settings.PickerNudgeLeftChord, -1, 0); Add(settings.PickerNudgeRightChord, 1, 0);
        Add(settings.PickerNudgeUpChord, 0, -1); Add(settings.PickerNudgeDownChord, 0, 1);
    }

    /// <summary>匹配事件的主键及完整修饰键。</summary>
    /// <param name="key">虚拟键码。</param>
    /// <param name="mods">事件修饰键。</param>
    /// <returns>一个物理像素的位移，未命中返回零。</returns>
    internal (int X, int Y) Resolve(uint key, GE mods)
    {
        foreach (var binding in _bindings)
            if (binding.Key == key && binding.Mods == mods) return (binding.X, binding.Y);
        return (0, 0);
    }

    /// <summary>计算屏幕范围内的新指针位置，不改变位移的物理像素单位。</summary>
    /// <param name="x">指针的虚拟桌面横坐标。</param>
    /// <param name="y">指针的虚拟桌面纵坐标。</param>
    /// <param name="dx">横向位移。</param>
    /// <param name="dy">纵向位移。</param>
    /// <param name="bounds">选区所在屏幕的物理范围。</param>
    /// <returns>屏幕内的物理坐标，指针位于范围外时保持原位。</returns>
    internal static (int X, int Y) Offset(int x, int y, int dx, int dy, RectI bounds)
    {
        if (!bounds.Contains(x, y)) return (x, y);
        return (Math.Clamp(x + dx, bounds.X, bounds.Right - 1), Math.Clamp(y + dy, bounds.Y, bounds.Bottom - 1));
    }
}
