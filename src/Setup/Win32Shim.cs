using System;
using System.Runtime.InteropServices;

namespace FrameCapture.Native;

/// <summary>
/// 引导器用到的 Win32 子集。主程序的 <c>Native/Win32.cs</c> 有 40 KB，会连锁拖进输入布局与日志器，
/// 引导器只需要圆角与分层窗口这几项，故在此单独声明；签名与主程序那份逐字一致。
/// </summary>
internal static class Win32
{
    /// <summary>窗口矩形，屏幕坐标，左上含、右下不含。</summary>
    public struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>设置 DWM 窗口属性（圆角、标题栏颜色等），Win11 起支持。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="attr">属性编号，见 <see cref="DWMWA_WINDOW_CORNER_PREFERENCE"/>。</param>
    /// <param name="pvAttribute">属性值，按值传递。</param>
    /// <param name="cbAttribute">值占用字节数。</param>
    /// <returns>0 表示已生效；非 0 表示系统不支持，调用方自行兜底。</returns>
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int pvAttribute, int cbAttribute);

    /// <summary>DWM 属性编号：窗口圆角偏好。</summary>
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>圆角偏好取值：使用系统默认圆角。</summary>
    public const int DWMWCP_ROUND = 2;

    /// <summary>DWM 属性编号：背板材质与标题栏明暗。
    /// <c>DWMWA_SYSTEMBACKDROP_TYPE</c> 自 Win11 22621 起提供，更早的 22000 只有
    /// <c>DWMWA_MICA_EFFECT</c> 这个布尔开关；两者都失败即当前系统无背板。</summary>
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWA_MICA_EFFECT = 1029;

    /// <summary>背板材质取值。MAINWINDOW 即 Mica，TRANSIENTWINDOW 即 Acrylic。</summary>
    public const int DWMSBT_NONE = 0;
    public const int DWMSBT_AUTO = 1;
    public const int DWMSBT_MAINWINDOW = 2;
    public const int DWMSBT_TRANSIENTWINDOW = 3;
    public const int DWMSBT_TABULARWINDOW = 4;

    /// <summary>读窗口 32 位属性。<b>只用于 GWL_STYLE / GWL_EXSTYLE 这类 32 位索引</b>：
    /// 64 位下指针型索引（GWLP_WNDPROC 等）必须走 GetWindowLongPtr，用这份会截断。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="nIndex">属性索引，如 <see cref="GWL_EXSTYLE"/>。</param>
    /// <returns>该属性的 32 位值。</returns>
    [DllImport("user32.dll")]
    public static extern uint GetWindowLongW(IntPtr hWnd, int nIndex);

    /// <summary>写窗口 32 位属性，索引限制同 <see cref="GetWindowLongW"/>。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="nIndex">属性索引。</param>
    /// <param name="dwNewLong">新值。</param>
    /// <returns>该属性本次写入前的取值。</returns>
    [DllImport("user32.dll")]
    public static extern uint SetWindowLongW(IntPtr hWnd, int nIndex, uint dwNewLong);

    /// <summary>扩展窗口样式位的索引。</summary>
    public const int GWL_EXSTYLE = -20;

    /// <summary>扩展样式：分层窗口，是 <see cref="SetLayeredWindowAttributes"/> 的前置条件。</summary>
    public const int WS_EX_LAYERED = 0x00080000;

    /// <summary>给已带 <see cref="WS_EX_LAYERED"/> 的窗口设整窗透明度。</summary>
    /// <param name="hWnd">已带 WS_EX_LAYERED 扩展样式的窗口句柄。</param>
    /// <param name="crKey">按色键透明时用，本仓传 0。</param>
    /// <param name="bAlpha">整窗不透明度 0–255。</param>
    /// <param name="dwFlags">生效位，本仓只用 <see cref="LWA_ALPHA"/>。</param>
    /// <returns>true = 已生效；false = 窗口未带 WS_EX_LAYERED。</returns>
    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, IntPtr crKey, byte bAlpha, uint dwFlags);

    /// <summary><see cref="SetLayeredWindowAttributes"/> 的 dwFlags 位：bAlpha 生效。</summary>
    public const uint LWA_ALPHA = 0x2;

    /// <summary>取窗口在屏幕坐标系下的外接矩形（含 DWM 不可见边框）。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="lpRect">取到的矩形。</param>
    /// <returns>true = 取到；false = 句柄无效。</returns>
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
