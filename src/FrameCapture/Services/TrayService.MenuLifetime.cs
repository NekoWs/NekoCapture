using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using FrameCapture.Native;

namespace FrameCapture.Services;

/// <summary>菜单打开期间监测前台变化与窗外按下，不在系统鼠标输入链中安装钩子。</summary>
public sealed partial class TrayService
{
    private static void WatchMenu(ContextMenu menu, Window anchor)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(20) };
        IntPtr foreground = IntPtr.Zero;
        int previousButtons = 0;
        static int Buttons() => (Win32.IsKeyDown(1) ? 1 : 0) | (Win32.IsKeyDown(2) ? 2 : 0) | (Win32.IsKeyDown(4) ? 4 : 0);
        menu.Opened += (_, _) =>
        {
            foreground = Win32.GetForegroundWindow(); previousButtons = Buttons();
            menu.Focus(); timer.Start();
        };
        timer.Tick += (_, _) =>
        {
            if (!menu.IsOpen || !anchor.IsVisible) { timer.Stop(); return; }
            var popup = (PresentationSource.FromVisual(menu) as HwndSource)?.Handle ?? IntPtr.Zero;
            var current = Win32.GetForegroundWindow();
            if (current != foreground && current != popup && current != new WindowInteropHelper(anchor).Handle)
            { menu.IsOpen = false; return; }
            int buttons = Buttons(), pressed = buttons & ~previousButtons;
            previousButtons = buttons;
            if (pressed == 0 || popup == IntPtr.Zero || !Win32.GetWindowRect(popup, out var rect)) return;
            var cursor = System.Windows.Forms.Cursor.Position;
            if (cursor.X < rect.Left || cursor.X >= rect.Right || cursor.Y < rect.Top || cursor.Y >= rect.Bottom)
                menu.IsOpen = false;
        };
        menu.Closed += (_, _) => timer.Stop();
        anchor.Closed += (_, _) => { timer.Stop(); menu.IsOpen = false; };
    }
}
