using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;

namespace FrameCapture.Services;

// 本 partial 职责：把窗口推到前台的 Win32 前台锁绕行链，供各捕获入口共用。
public sealed partial class CaptureController : IDisposable
{
    /// <summary>把窗口推到前台并拿键盘焦点。后台进程直发 SetForegroundWindow 会被前台锁拒绝
    /// （注入按键也不授予前台权限），用 AttachThreadInput 经典链绕开；最后兜底 SwitchToThisWindow。</summary>
    private static void ForegroundNow(System.IntPtr h)
    {
        if (h == System.IntPtr.Zero) return;
        Native.Win32.BringWindowToTop(h);
        if (Native.Win32.SetForegroundWindow(h)) return;
        IntPtr fg = Native.Win32.GetForegroundWindow();
        uint fgTid = Native.Win32.GetWindowThreadProcessId(fg, out _);
        uint curTid = Native.Win32.GetCurrentThreadId();
        if (fgTid != 0 && fgTid != curTid && Native.Win32.AttachThreadInput(curTid, fgTid, true))
        {
            try
            {
                Native.Win32.SetForegroundWindow(h);
                Native.Win32.BringWindowToTop(h);
            }
            finally { Native.Win32.AttachThreadInput(curTid, fgTid, false); }
        }
        if (Native.Win32.GetForegroundWindow() != h)
        {
            Native.Win32.SwitchToThisWindow(h, true);
            if (Native.Win32.GetForegroundWindow() != h)
                FrameCapture.Core.Trace.Log($"foreground denied hwnd={h}");
        }
    }

    private static readonly bool TestNoForeground =
        Environment.GetEnvironmentVariable("FC_TEST_NO_FG") == "1"; // 测试专用：不抢前台（前台机制单独取证）

    private static void ForceForeground(System.Windows.Window w)
    {
        try
        {
            if (TestNoForeground) return;
            w.Activate();
            var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            ForegroundNow(h);
            // 开档 1 秒内有限重试：部分游戏/应用会在窗口弹出后立刻抢回前台；
            // 之后不再强抢（Topmost 仍在视觉上置顶，用户可点击获取焦点），避免焦点战。
            int tries = 3;
            var rt = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            rt.Tick += (_, _) =>
            {
                rt.Stop();
                if (!w.IsLoaded) return;
                if (Native.Win32.GetForegroundWindow() != h)
                {
                    FrameCapture.Core.Trace.Log("foreground re-steal detected, retry");
                    ForegroundNow(h);
                }
                if (--tries > 0 && w.IsLoaded)
                {
                    rt.Start();
                }
                // 重试次数用尽即止
            };
            rt.Start();
        }
        catch { }
    }

}
