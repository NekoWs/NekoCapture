using System.Runtime.InteropServices;

namespace NekoCapture.Core;

/// <summary>
/// 显示器当前刷新率检测。EnumDisplaySettings 取 DEVMODE.dmDisplayFrequency（整数 Hz，
/// 如 160；59.94→60、143.98→144 由驱动取整）。失败返回 0，调用方回退手动帧率。
/// 供「跟随显示器刷新率」一键录满高刷。
/// </summary>
public static class DisplayRefresh
{
    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        // 以下字段顺序与长度必须与 winuser.h 的 DEVMODEW 逐字节一致：改一个字段就等于换一套布局。
        /// <summary>设备名（32 个 WCHAR 定长缓冲），即传入 EnumDisplaySettings 的那个名字。</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        /// <summary>结构版本与尺寸；dmSize 必须填 Marshal.SizeOf 的结果，否则 API 直接拒绝。</summary>
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        /// <summary>位掩码，声明本次要读写哪些字段；只读当前设置时不置位。</summary>
        public int dmFields;
        /// <summary>桌面虚拟屏坐标与显示方向；刷新率检测不读，仅为布局占位。</summary>
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        /// <summary>打印相关字段（色彩/双面/分辨率方向/字体处理/校对）；显示模式检测不涉及。</summary>
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        /// <summary>纸张名，定长 WCHAR 缓冲；显示模式检测不读它，调用后内容无意义（初值 dmFormName=""）。</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        /// <summary>逻辑 DPI（96/120/144…）；本类不读，DPI 一律走 WPF 的 TransformToDevice。</summary>
        public short dmLogPixels;
        /// <summary>位深与模式像素宽高；录制取帧尺寸由帧管线给出，这里不读。</summary>
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags;
        /// <summary>当前刷新率，整数 Hz（59.94→60、143.98→144 由驱动取整）。本类唯一真正读取的字段。</summary>
        public int dmDisplayFrequency;
        /// <summary>ICM 方法/意图、介质类型、抖动类型；本类不读。</summary>
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        /// <summary>保留字段与平移宽高；必须存在，否则结构总长与原生 DEVMODEW 不符。</summary>
        public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    /// <summary>取指定设备（如 "\\.\DISPLAY1"）当前刷新率；未知/失败返回 0。</summary>
    /// <param name="deviceName">设备名，来自 EnumDisplayDevices；null 或空串直接返回 0。</param>
    /// <returns>整数 Hz；设备名为空、API 调用失败、异常或返回值非正都归一成 0，调用方据此回退手动帧率。</returns>
    public static int RateOf(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return 0;
        try
        {
            var dm = new DEVMODE { dmDeviceName = "", dmFormName = "" };
            dm.dmSize = (short)Marshal.SizeOf<DEVMODE>();
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm)) return 0;
            return dm.dmDisplayFrequency > 0 ? dm.dmDisplayFrequency : 0;
        }
        catch { return 0; }
    }
}
