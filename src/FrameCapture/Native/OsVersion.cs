using System;
using System.Runtime.InteropServices;

namespace FrameCapture.Native;

/// <summary>
/// 系统版本号探针。能力门控不能取 <see cref="Environment.OSVersion"/>：它在 .NET Framework
/// 一侧谎报 6.2.9200，而视觉语言源文件同时被 net8 主程序与 net48 引导器编译，
/// 拿谎报值当门控会让 Win11 特性在引导器里整体静默消失。ntdll 的 RtlGetVersion 不受影响。
/// </summary>
internal static class OsVersion
{
    /// <summary>OSVERSIONINFOEX：前五个整型字段决定布局，其余按原结构补齐尺寸。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct OsVersionInfoEx
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    /// <summary>向 ntdll 直接取系统版本号。</summary>
    /// <param name="info">调用前须填好 dwOSVersionInfoSize。</param>
    /// <returns>0 表示取到。</returns>
    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref OsVersionInfoEx info);

    /// <summary>系统内部版本号，Win11 起 ≥ 22000。</summary>
    public static int BuildNumber { get; } = Probe();

    /// <summary>取一次内部版本号。</summary>
    /// <returns>RtlGetVersion 给的值；调用失败或给回 0 时落到 <see cref="Environment.OSVersion"/>。</returns>
    private static int Probe()
    {
        var v = new OsVersionInfoEx { dwOSVersionInfoSize = Marshal.SizeOf(typeof(OsVersionInfoEx)) };
        return RtlGetVersion(ref v) == 0 && v.dwBuildNumber > 0
            ? v.dwBuildNumber
            : Environment.OSVersion.Version.Build;
    }
}
