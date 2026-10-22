using System;
using System.Runtime.InteropServices;

namespace FrameCapture.Native;

/// <summary>鼠标捕获相关的 Win32 调用：捕获与命中测试无关，是点击穿透窗口取得输入的唯一途径。</summary>
internal static partial class Win32
{
    /// <summary>把鼠标输入捕获到指定窗口：捕获之后该窗口收到全部鼠标消息，与命中测试无关。</summary>
    /// <param name="hWnd">要捕获输入的窗口。</param>
    /// <returns>捕获前的窗口，通常为 <c>IntPtr.Zero</c>。</returns>
    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hWnd);

    /// <summary>释放鼠标捕获。</summary>
    /// <returns>成功返回 true。</returns>
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    /// <summary>当前持有捕获的窗口。</summary>
    /// <returns>窗口句柄；没有时为 <c>IntPtr.Zero</c>。</returns>
    [DllImport("user32.dll")]
    public static extern IntPtr GetCapture();
}
