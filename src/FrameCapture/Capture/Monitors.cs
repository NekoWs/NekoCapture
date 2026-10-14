using System.Windows;
using FrameCapture.Native;
using WinForms = System.Windows.Forms;

namespace FrameCapture.Capture;

/// <summary>一台显示器的静态描述。</summary>
/// <param name="DeviceName">Win32 设备名，与设置里存的 RegionMonitorId 同源，用来跨会话认回同一块屏。</param>
/// <param name="Bounds">物理像素、虚拟桌面绝对坐标；主屏原点恒 (0,0)，左侧/上方的副屏坐标可为负。</param>
/// <param name="IsPrimary">是否主显示器。</param>
/// <returns>按给定值构造的不可变记录。</returns>
public sealed record MonitorInfo(string DeviceName, RectI Bounds, bool IsPrimary)
{
    /// <summary>解析本显示器对应的 HMONITOR：取 Bounds 中心的物理坐标做一次 MONITOR_DEFAULTTONEAREST 命中。</summary>
    /// <returns>HMONITOR 句柄；解析失败为 <see cref="IntPtr.Zero"/>，调用方必须自行判零。</returns>
    public IntPtr ResolveHMonitor()
    {
        var pt = new Win32.POINT
        {
            X = Bounds.X + Bounds.W / 2,
            Y = Bounds.Y + Bounds.H / 2,
        };
        return Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
    }
}

/// <summary>显示器枚举。应用清单为 PerMonitorV2，Screen.Bounds 即物理像素。</summary>
public static class Monitors
{
    /// <summary>枚举全部显示器。</summary>
    /// <returns>按 WinForms 的枚举顺序排列。每次调用都重新枚举，热插拔即时可见——代价是一次 Win32 枚举，别放进逐帧路径。</returns>
    public static List<MonitorInfo> All() => WinForms.Screen.AllScreens
        .Select(s => new MonitorInfo(s.DeviceName,
            new RectI(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Primary))
        .ToList();

    /// <summary>按设备名查找。</summary>
    /// <param name="device">设备名，须与枚举结果完全一致，不做大小写或前后缀容错。</param>
    /// <returns>匹配的显示器；null = 该屏已拔出或设备名变了，调用方应回退 <see cref="Nearest"/>。</returns>
    public static MonitorInfo? ByDevice(string device) => All().FirstOrDefault(m => m.DeviceName == device);

    /// <summary>取与给定坐标最相关的显示器：先按命中，再按矩形距离。</summary>
    /// <param name="x">物理像素、虚拟桌面绝对坐标。</param>
    /// <param name="y">同上坐标系。</param>
    /// <returns>命中的一块；都不命中（坐标落在所有屏之外）取距离最近的一块；
    /// 枚举彻底为空时兜底一块 1920x1080 主屏——本方法绝不返回 null，调用方无需判空。</returns>
    public static MonitorInfo Nearest(int x, int y)
    {
        var list = All();
        var hit = list.FirstOrDefault(m => m.Bounds.Contains(x, y));
        if (hit != null) return hit;
        return list.OrderBy(m => DistanceSq(m.Bounds, x, y)).FirstOrDefault()
                   ?? list.FirstOrDefault()
                   ?? new MonitorInfo("DISPLAY1", new RectI(0, 0, 1920, 1080), true);
    }

    /// <summary>取该显示器的有效 DPI 缩放倍率（96 DPI = 1.0）。取不到或异常时返回 1.0——
    /// 布局宁可按 100% 排，也不能因一次 DPI 查询失败而抛到 UI 线程。</summary>
    /// <param name="monitor">目标显示器。</param>
    /// <returns>横竖缩放倍率，均 &gt;= 1.0 之外的正常值；查询失败时为 (1.0, 1.0)。</returns>
    public static (double Sx, double Sy) DpiScaleOf(MonitorInfo monitor)
    {
        double sx = 1.0, sy = 1.0;
        try
        {
            Win32.GetDpiForMonitor(monitor.ResolveHMonitor(), Win32.DPI_EFFECTIVE, out uint dx, out uint dy);
            if (dx > 0 && dy > 0) { sx = dx / 96.0; sy = dy / 96.0; }
        }
        catch { }
        return (sx, sy);
    }

    static long DistanceSq(RectI b, int x, int y)
    {
        long dx = Math.Max(b.X - x, Math.Max(x - b.Right, 0));
        long dy = Math.Max(b.Y - y, Math.Max(y - b.Bottom, 0));
        return dx * dx + dy * dy;
    }
}
