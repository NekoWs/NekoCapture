namespace FrameCapture.Services;

/// <summary>选区微调优先于用户手势，认领的按键直到抬起都不传给前台。</summary>
public sealed partial class HotkeyManager
{
    /// <summary>选区的微调按下与系统连发回调；只做匹配与投递，选区关闭时清空。</summary>
    public static Func<uint, GestureCore.ModifierKeys, bool>? PickerNudgeKey;
    readonly HashSet<uint> _pickerNudgeKeyUps = new();

    private bool RoutePickerNudge(uint key, bool down, GestureCore.ModifierKeys mods)
    {
        if (!down) return false;
        bool owned = _pickerNudgeKeyUps.Contains(key);
        if (PickerActive && PickerNudgeKey?.Invoke(key, mods) == true)
        {
            _pickerNudgeKeyUps.Add(key);
            return true;
        }
        return owned;
    }
}
