using System.Runtime.InteropServices;

namespace FrameCapture.Core;

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
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    /// <summary>取指定设备（如 "\\.\DISPLAY1"）当前刷新率；未知/失败返回 0。</summary>
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
