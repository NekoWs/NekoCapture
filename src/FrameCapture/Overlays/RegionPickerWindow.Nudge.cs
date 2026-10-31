using System.Windows;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>选区内按物理像素微调鼠标；键盘连发逐次投递，关闭后丢弃未执行位移。</summary>
public sealed partial class RegionPickerWindow
{
    private void AttachNudgeKeys()
    {
        var bindings = new PickerNudgeBindings(Services.AppHost.Current?.Settings?.Current ?? new AppSettings());
        Services.HotkeyManager.PickerNudgeKey = (key, mods) =>
        {
            var (dx, dy) = bindings.Resolve(key, mods);
            if (dx == 0 && dy == 0) return false;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => NudgeCursor(dx, dy)));
            return true;
        };
    }

    private void NudgeCursor(int dx, int dy)
    {
        if (_finished || !IsVisible || !Win32.GetCursorPos(out var cursor)) return;
        var (x, y) = PickerNudgeBindings.Offset(cursor.X, cursor.Y, dx, dy, _monitorPhys);
        if (x == cursor.X && y == cursor.Y) return;
        if (!Win32.SetCursorPos(x, y)) return;
        var point = PointFromScreen(new Point(x, y));
        _pointerPending = point;
        if (_snapContent && !_dragging && _grab == GrabKind.None && _sel == null && !_drillLock) ScheduleProbe(point);
    }
}
