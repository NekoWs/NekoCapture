using System;
using System.Runtime.InteropServices;

namespace GestureEditor;

/// <summary>
/// 库自带的极小 Win32 互操作。GestureEditor 不引用宿主 FrameCapture，但入场/退场的
/// 透明度淡入淡出需要把窗口强制成分层窗（WS_EX_LAYERED）——WindowChrome + 非分层窗上，
/// WPF 的 Window.Opacity 动画根本不驱动屏幕合成（实测开合都「啪」地出现/消失，无渐变）。
/// 与宿主 Fluent.PreTransparent 同款配方，只是自带一份、保持库可独立复用。
/// </summary>
internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x00080000;
    public const uint LWA_ALPHA = 0x00000002;

    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    /// <summary>把窗口置为分层窗并把 Win32 alpha 压到 0（消除首帧不透明黑闪，并让后续 WPF Opacity 动画真正生效）。</summary>
    /// <param name="hwnd">目标窗口句柄，须在 <c>SourceInitialized</c> 之后取；<see cref="IntPtr.Zero"/> 时静默返回。</param>
    public static void SetAlphaZero(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED);
        SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);
    }
}
