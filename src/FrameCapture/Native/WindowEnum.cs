using System.Runtime.InteropServices;

namespace FrameCapture.Native;

/// <summary>窗口枚举（范围吸附用）：Z 序从上到下，带 DWM 扩展边界。</summary>
public static class WindowEnum
{
    /// <summary>返回按 Z 序排列的可见候选窗口（物理像素边界），跳过桌面/壳层/工具窗口。</summary>
    public static IEnumerable<(IntPtr Hwnd, Capture.RectI Bounds, Capture.RectI Client)> ListTopmostCandidates(IntPtr excludeHwnd = default)
    {
        var list = new List<(IntPtr, Capture.RectI, Capture.RectI)>();
        IntPtr shell = Win32.GetShellWindow();

        bool Callback(IntPtr hwnd, IntPtr _)
        {
            if (hwnd == shell) return true;
            if (!Win32.IsWindowVisible(hwnd)) return true;

            uint ex = Win32.GetWindowLongW(hwnd, Win32.GWL_EXSTYLE);
            if ((ex & (Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE)) != 0)
                return true;

            // 已披风（虚拟桌面其他会话）
            if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_CLOAKED, out uint cloaked, 4) == 0 && cloaked != 0)
                return true;

            // DWM 扩展框架边界（物理像素）
            if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out Win32.RECT r, Marshal.SizeOf<Win32.RECT>()) != 0)
                return true;

            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w < 16 || h < 16) return true;

            // 跳过自己的进程
            // allow own content windows (editor/settings/help) to be snapped; only skip the caller's picker itself
            if (hwnd == excludeHwnd) return true;

            // 跳过整个桌面（Progman/WorkerW）与壳层
            var sb = new System.Text.StringBuilder(256);
            Win32.GetWindowTextW(hwnd, sb, 256);
            var title = sb.ToString();
            if (title.Length == 0)
            {
                // 无标题的顶层大窗口多半是桌面/任务视图壳层
                if (w >= 1920 && h >= 1080) return true;
            }

            var frame = new Capture.RectI(r.Left, r.Top, w, h);
            // client-area rect (physical px): excludes window frame/title bar
            var client = frame;
            if (Win32.GetClientRect(hwnd, out var rcC) && rcC.Right > 8 && rcC.Bottom > 8)
            {
                var tl = new Win32.POINT { X = rcC.Left, Y = rcC.Top };
                var br = new Win32.POINT { X = rcC.Right, Y = rcC.Bottom };
                if (Win32.ClientToScreen(hwnd, ref tl) && Win32.ClientToScreen(hwnd, ref br))
                    client = new Capture.RectI(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
            }
            list.Add((hwnd, frame, client));
            return true;
        }

        Win32.EnumWindows(Callback, IntPtr.Zero);
        return list;
    }
}
