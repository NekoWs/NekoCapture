
namespace NekoCapture.Services;

/// <summary>按系统前台权限激活窗口，不绑定其他进程的输入队列。</summary>
public sealed partial class CaptureController : IDisposable
{
    private static void ForegroundNow(IntPtr h)
    {
        if (h == IntPtr.Zero) return;
        if (Native.Win32.GetForegroundWindow() == h) return;
        if (!Native.Win32.SetForegroundWindow(h)) Core.Trace.Log($"foreground denied hwnd={h}");
    }

    private static readonly bool TestNoForeground =
        Environment.GetEnvironmentVariable("NC_TEST_NO_FG") == "1"; // 测试专用：不抢前台（前台机制单独取证）

    private static void ForceForeground(System.Windows.Window w)
    {
        try
        {
            if (TestNoForeground) return;
            w.Activate();
            var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            ForegroundNow(h);
        }
        catch { }
    }

}
