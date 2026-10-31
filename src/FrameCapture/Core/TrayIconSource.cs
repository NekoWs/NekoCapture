using System.Runtime.InteropServices;

namespace FrameCapture.Runtime;

/// <summary>按任务栏实际 DPI 选择通知区域图标尺寸，返回独立拥有的图标句柄。</summary>
internal static class TrayIconSource
{
    /// <summary>任务栏所在屏幕的小图标物理尺寸。</summary>
    /// <returns>图标宽度与高度，单位为物理像素。</returns>
    internal static (int Width, int Height) PreferredSize()
    {
        uint dpi = GetDpiForWindow(FindWindow("Shell_TrayWnd", null));
        if (dpi == 0) dpi = GetDpiForSystem();
        if (dpi == 0) dpi = 96;
        int width = GetSystemMetricsForDpi(49, dpi), height = GetSystemMetricsForDpi(50, dpi);
        return (Math.Clamp(width, 16, 64), Math.Clamp(height, 16, 64));
    }

    /// <summary>从 ICO 文件加载匹配任务栏尺寸的图层。</summary>
    /// <param name="path">ICO 文件绝对路径。</param>
    /// <returns>需由调用方 DestroyIcon 的句柄，失败时为零。</returns>
    internal static IntPtr LoadFile(string path)
    {
        var size = PreferredSize();
        return LoadImage(IntPtr.Zero, path, 1, size.Width, size.Height, 0x10);
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string name, string? title);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
}
