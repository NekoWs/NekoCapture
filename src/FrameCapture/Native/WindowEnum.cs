using System.Runtime.InteropServices;

namespace FrameCapture.Native;

/// <summary>窗口枚举（范围吸附用）：Z 序从上到下，带 DWM 扩展边界。</summary>
public static class WindowEnum
{
    /// <summary>
    /// 按 Z 序自顶向下返回可吸附的候选窗口：过滤全在枚举回调内完成，调用方取首个即最上层。
    /// </summary>
    /// <param name="excludeHwnd">要排除的窗口，通常是发起选取的浮窗本身；IntPtr.Zero = 不排除。</param>
    /// <param name="skipIconic">true = 跳过最小化窗口。贴图吸附用它：最小化窗没有可对齐的可见边。</param>
    /// <returns>每项 (Hwnd, Bounds, Client)。Bounds 取 DWM 可见外框、Client 取客户区，
    /// 两者均为物理像素、虚拟桌面绝对坐标；取不到外框或边长不足 16 像素的窗口不进结果。</returns>
    public static IEnumerable<(IntPtr Hwnd, Capture.RectI Bounds, Capture.RectI Client)> ListTopmostCandidates(IntPtr excludeHwnd = default, bool skipIconic = false)
    {
        var list = new List<(IntPtr, Capture.RectI, Capture.RectI)>();
        IntPtr shell = Win32.GetShellWindow();

        bool Callback(IntPtr hwnd, IntPtr _)
        {
            if (hwnd == shell) return true;
            if (!Win32.IsWindowVisible(hwnd)) return true;
            if (skipIconic && Win32.IsIconic(hwnd)) return true;

            uint ex = Win32.GetWindowLongW(hwnd, Win32.GWL_EXSTYLE);
            if ((ex & (Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE)) != 0)
                return true;

            // cloaked：窗口此刻不呈现于当前桌面（虚拟桌面切换、应用被挂起），不能当对齐参考
            if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_CLOAKED, out uint cloaked, 4) == 0 && cloaked != 0)
                return true;

            // DWM 扩展框架边界（物理像素）
            if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out Win32.RECT r, Marshal.SizeOf<Win32.RECT>()) != 0)
                return true;

            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w < 16 || h < 16) return true;

            // 只排除发起选取的浮窗本身；自家编辑器/设置/帮助窗同样允许被吸附
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
            // 客户区矩形（物理像素）：不含边框与标题栏
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
