using System.Diagnostics;
using System.Windows;

namespace FrameCapture.Capture;

/// <summary>简单整数矩形（物理像素）。</summary>
public struct RectI : IEquatable<RectI>
{
    public int X, Y, W, H;
    public RectI(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
    public int Right => X + W;
    public int Bottom => Y + H;
    public bool Contains(int px, int py) => px >= X && px < Right && py >= Y && py < Bottom;
    public bool Equals(RectI o) => X == o.X && Y == o.Y && W == o.W && H == o.H;
    public override bool Equals(object? obj) => obj is RectI o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(X, Y, W, H);
    public override string ToString() => $"{X},{Y} {W}x{H}";

    /// <summary>规范化（允许负宽高拖框）。</summary>
    public static RectI FromCorners(int x1, int y1, int x2, int y2)
    {
        int x = Math.Min(x1, x2), y = Math.Min(y1, y2);
        return new RectI(x, y, Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }
}

/// <summary>捕获范围：某台显示器上的物理矩形（虚拟桌面绝对坐标）。</summary>
public sealed record ScreenRegion(string DeviceName, RectI Bounds);

/// <summary>单帧：紧凑 BGRA（无 padding，长度 = W*H*4）。</summary>
public sealed class CapturedFrame
{
    public required byte[] Bgra { get; init; }
    public required int W { get; init; }
    public required int H { get; init; }
    /// <summary>相对捕获开始的毫秒时间戳。</summary>
    public double TimeMs { get; set; }
}

/// <summary>一次捕获的完整序列。</summary>
public sealed class CapturedSequence
{
    public required List<CapturedFrame> Frames { get; set; }
    /// <summary>捕获区域（虚拟桌面物理坐标）。</summary>
    public required RectI Region { get; set; }
    /// <summary>区域所在显示器原点，用于编辑器定位。</summary>
    public required RectI MonitorBounds { get; init; }
    public string MonitorDevice { get; init; } = "";
    /// <summary>因内存预算被丢弃的帧数（0 = 完整捕获）。</summary>
    public int DroppedFrames { get; init; }
    public Stopwatch StartedAt { get; init; } = Stopwatch.StartNew();

    public double TotalMs => Frames.Count > 0 ? Frames[^1].TimeMs : 0;
    public bool IsSingle => Frames.Count <= 1;
}
