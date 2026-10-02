using System.Windows;
using FrameCapture.Native;
using WinForms = System.Windows.Forms;

namespace FrameCapture.Capture;

public sealed record MonitorInfo(string DeviceName, RectI Bounds, bool IsPrimary)
{
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
    public static List<MonitorInfo> All() => WinForms.Screen.AllScreens
        .Select(s => new MonitorInfo(s.DeviceName,
            new RectI(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Primary))
        .ToList();

    public static MonitorInfo? ByDevice(string device) => All().FirstOrDefault(m => m.DeviceName == device);

    public static MonitorInfo Nearest(int x, int y)
    {
        var list = All();
        var hit = list.FirstOrDefault(m => m.Bounds.Contains(x, y));
        if (hit != null) return hit;
        return list.OrderBy(m => DistanceSq(m.Bounds, x, y)).FirstOrDefault()
                   ?? list.FirstOrDefault()
                   ?? new MonitorInfo("DISPLAY1", new RectI(0, 0, 1920, 1080), true);
    }

    static long DistanceSq(RectI b, int x, int y)
    {
        long dx = Math.Max(b.X - x, Math.Max(x - b.Right, 0));
        long dy = Math.Max(b.Y - y, Math.Max(y - b.Bottom, 0));
        return dx * dx + dy * dy;
    }

    /// <summary>把矩形裁剪到显示器内。</summary>
    public static RectI ClipTo(this RectI r, MonitorInfo m)
    {
        int x1 = Math.Max(r.X, m.Bounds.X), y1 = Math.Max(r.Y, m.Bounds.Y);
        int x2 = Math.Min(r.Right, m.Bounds.Right), y2 = Math.Min(r.Bottom, m.Bounds.Bottom);
        if (x2 - x1 < 16 || y2 - y1 < 16) return m.Bounds;
        return new RectI(x1, y1, x2 - x1, y2 - y1);
    }
}
