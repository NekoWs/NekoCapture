using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using NekoCapture.Capture;
using NekoCapture.Core;
using NekoCapture.Services;
using GE = GestureCore.ModifierKeys;

static partial class SelfTest
{
    static void PickerNudgeChecks()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        var bindings = new PickerNudgeBindings(settings);
        Chk("picker-nudge-default-directions", bindings.Resolve(0x25, GE.None) == (-1, 0)
            && bindings.Resolve(0x27, GE.None) == (1, 0) && bindings.Resolve(0x26, GE.None) == (0, -1)
            && bindings.Resolve(0x28, GE.None) == (0, 1));
        Chk("picker-nudge-requires-exact-modifiers", bindings.Resolve(0x25, GE.Control) == (0, 0)
            && bindings.Resolve(0x25, GE.Alt) == (0, 0) && bindings.Resolve(0x25, GE.Shift) == (0, 0));
        settings.PickerNudgeLeftChord = "Ctrl+J"; settings.PickerNudgeRightChord = "Alt+L";
        bindings = new PickerNudgeBindings(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!);
        Chk("picker-nudge-custom-bindings-persist", bindings.Resolve(0x4A, GE.Control) == (-1, 0)
            && bindings.Resolve(0x4C, GE.Alt) == (1, 0) && bindings.Resolve(0x25, GE.None) == (0, 0));
        settings.PickerConfirmChord = "Ctrl+J"; settings.PickerNudgeUpChord = "Esc";
        settings.PickerNudgeDownChord = "Alt+L";
        bindings = new PickerNudgeBindings(settings);
        Chk("picker-nudge-respects-confirm-cancel-and-first-binding", bindings.Resolve(0x4A, GE.Control) == (0, 0)
            && bindings.Resolve(0x1B, GE.None) == (0, 0) && bindings.Resolve(0x4C, GE.Alt) == (1, 0));
        settings.PickerNudgeLeftChord = ""; settings.PickerNudgeRightChord = "invalid-key";
        bindings = new PickerNudgeBindings(settings);
        Chk("picker-nudge-empty-and-invalid-bindings-do-not-claim", bindings.Resolve(0x25, GE.None) == (0, 0)
            && bindings.Resolve(0x4A, GE.Control) == (0, 0));
        var bounds = new RectI(-1920, -300, 1920, 1080);
        Chk("picker-nudge-uses-physical-pixels-and-negative-coordinates",
            PickerNudgeBindings.Offset(-500, 200, -1, 0, bounds) == (-501, 200)
            && PickerNudgeBindings.Offset(-500, 200, 0, 1, bounds) == (-500, 201));
        Chk("picker-nudge-stays-inside-selection-monitor", PickerNudgeBindings.Offset(-1920, -300, -1, -1, bounds) == (-1920, -300)
            && PickerNudgeBindings.Offset(-1, 779, 1, 1, bounds) == (-1, 779));
        Chk("picker-nudge-does-not-warp-from-other-monitor", PickerNudgeBindings.Offset(100, 100, -1, 0, bounds) == (100, 100));

        var priorNudge = HotkeyManager.PickerNudgeKey; bool priorPicker = HotkeyManager.PickerActive;
        var priorCapture = HotkeyManager.CaptureSink;
        var packet = Marshal.AllocHGlobal(24);
        using var manager = new HotkeyManager();
        var handle = typeof(HotkeyManager).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int moves = 0, gestures = 0;
        manager.KeySink = (_, _, _, _) => { gestures++; return false; };
        bool Send(bool down)
        {
            Marshal.Copy(new byte[24], 0, packet, 24);
            Marshal.WriteInt32(packet, 0, 0x25); Marshal.WriteInt32(packet, 8, down ? 0 : 0x80);
            return (bool)handle.Invoke(manager, new object[] { packet })!;
        }
        try
        {
            HotkeyManager.CaptureSink = null;
            HotkeyManager.PickerActive = true;
            HotkeyManager.PickerNudgeKey = (key, _) => { if (key != 0x25) return false; moves++; return true; };
            bool claimed = Send(true);
            Chk("picker-nudge-tap-dispatches-once-before-gestures", claimed && moves == 1 && gestures == 0);
            bool repeated = true;
            for (int i = 0; i < 20; i++) repeated &= Send(true);
            Chk("picker-nudge-system-repeat-keeps-moving", repeated && moves == 21 && gestures == 0);
            Chk("picker-nudge-swallows-paired-release", Send(false) && moves == 21 && gestures == 0);
            Send(true); HotkeyManager.PickerActive = false; HotkeyManager.PickerNudgeKey = null;
            Chk("picker-nudge-close-stops-repeat-and-keeps-release", Send(true) && moves == 22 && Send(false) && gestures == 0);
            Chk("picker-nudge-releases-unowned-input-after-close", !Send(true) && !Send(false) && gestures == 2);
            HotkeyManager.PickerActive = true;
            HotkeyManager.PickerNudgeKey = (_, _) => { moves++; return true; };
            HotkeyManager.CaptureSink = (_, _, _, _) => true;
            Chk("picker-nudge-yields-to-key-recording", Send(true) && Send(false) && moves == 22);
        }
        finally
        {
            HotkeyManager.PickerNudgeKey = priorNudge; HotkeyManager.PickerActive = priorPicker;
            HotkeyManager.CaptureSink = priorCapture; Marshal.FreeHGlobal(packet);
        }
    }
}
