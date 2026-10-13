using System.Diagnostics;
using System.Windows;

namespace FrameCapture.Capture;

/// <summary>简单整数矩形（物理像素）。</summary>
public struct RectI : IEquatable<RectI>
{
    public int X, Y, W, H;
    /// <summary>构造矩形。</summary>
    /// <param name="x">左上角 X，物理像素、虚拟桌面绝对坐标。</param>
    /// <param name="y">左上角 Y，同上坐标系。</param>
    /// <param name="w">宽（像素数），允许 0 表示空矩形，不做负值校验。</param>
    /// <param name="h">高（像素数），同上。</param>
    public RectI(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
    public int Right => X + W;
    public int Bottom => Y + H;
    /// <summary>点是否落在矩形内。</summary>
    /// <param name="px">点的 X，物理像素、虚拟桌面绝对坐标。</param>
    /// <param name="py">点的 Y，同上坐标系。</param>
    /// <returns>左上角算进、右下角算出；零宽高矩形不包含任何点。</returns>
    public bool Contains(int px, int py) => px >= X && px < Right && py >= Y && py < Bottom;
    /// <summary>逐字段比较 X/Y/W/H。</summary>
    /// <param name="o">另一个矩形。</param>
    /// <returns>四字段全等为 true；不做"覆盖同一片像素"之类的等价判断。</returns>
    public bool Equals(RectI o) => X == o.X && Y == o.Y && W == o.W && H == o.H;
    /// <summary>按 <see cref="RectI"/> 四字段比较，类型不符一律 false。</summary>
    /// <param name="obj">任意对象。</param>
    /// <returns>是 <see cref="RectI"/> 且四字段全等为 true。</returns>
    public override bool Equals(object? obj) => obj is RectI o && Equals(o);
    /// <summary>四字段组合哈希，与 <see cref="Equals(RectI)"/> 一致：相等的矩形哈希必相等。</summary>
    /// <returns>可直接当字典键用的哈希码。</returns>
    public override int GetHashCode() => HashCode.Combine(X, Y, W, H);
    /// <summary>诊断用文本形式。</summary>
    /// <returns>形如 <c>X,Y WxH</c>，物理像素，无单位后缀。</returns>
    public override string ToString() => $"{X},{Y} {W}x{H}";

    /// <summary>由两个拖框角点规范化出矩形（拖框方向任意，等价于允许负宽高）。</summary>
    /// <param name="x1">第一个角的 X，物理像素、虚拟桌面绝对坐标。</param>
    /// <param name="y1">第一个角的 Y。</param>
    /// <param name="x2">第二个角的 X。</param>
    /// <param name="y2">第二个角的 Y。</param>
    /// <returns>左上角取两角的最小值、宽高取绝对值；两角重合时得到零面积矩形。</returns>
    public static RectI FromCorners(int x1, int y1, int x2, int y2)
    {
        int x = Math.Min(x1, x2), y = Math.Min(y1, y2);
        return new RectI(x, y, Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }
}

/// <summary>捕获范围：某台显示器上的物理矩形（虚拟桌面绝对坐标）。</summary>
/// <param name="DeviceName">显示器设备名，用来把矩形解释回正确的那块屏。</param>
/// <param name="Bounds">物理像素、虚拟桌面绝对坐标，通常落在该显示器的范围内。</param>
/// <returns>按给定值构造的不可变记录。</returns>
public sealed record ScreenRegion(string DeviceName, RectI Bounds);

/// <summary>单帧：紧凑 BGRA（无 padding，长度 = W*H*4）。</summary>
public sealed class CapturedFrame
{
    /// <summary>像素本体。</summary>
    /// <value>行主序 BGRA，长度恒等于 W*H*4，行间无 padding；帧本身不可变，要改先拷。</value>
    public required byte[] Bgra { get; init; }
    public required int W { get; init; }
    public required int H { get; init; }
    /// <summary>相对捕获开始的毫秒时间戳。</summary>
    public double TimeMs { get; set; }
}

/// <summary>一次捕获的完整序列。Frames 可能是内存里的列表（单帧、零帧兜底），也可能是磁盘暂存的
/// 惰性列表（<see cref="FrameSpoolList"/>，逐帧按需回读）——消费方只按索引/顺序访问，
/// 勿假设可写、可全量驻留内存，也勿假设随机访问廉价。</summary>
public sealed class CapturedSequence
{
    public required IReadOnlyList<CapturedFrame> Frames { get; set; }
    /// <summary>捕获区域（虚拟桌面物理坐标）。</summary>
    public required RectI Region { get; set; }
    /// <summary>区域所在显示器原点，用于编辑器定位。</summary>
    public required RectI MonitorBounds { get; init; }
    /// <summary>区域所在显示器设备名。</summary>
    /// <value>空串 = 未知，此时只能靠 <see cref="MonitorBounds"/> 定位。</value>
    public string MonitorDevice { get; init; } = "";
    /// <summary>因内存预算被丢弃的帧数（0 = 完整捕获）。</summary>
    public int DroppedFrames { get; init; }
    /// <summary>本次捕获的实际帧率：跟随刷新率时=显示器刷新率，否则=手动值。0=未知，消费方回退设置值。
    /// 编辑器时间轴/时长必须用它——否则「跟随刷新率」录制（如 160fps）时按手动 30fps 算时间会全错。</summary>
    public int Fps { get; init; }
    /// <summary>构造这条序列那一刻启动的计时器。</summary>
    /// <value>与帧的 <c>TimeMs</c> 无关：帧时间戳相对捕获时钟，不是相对这里。</value>
    public Stopwatch StartedAt { get; init; } = Stopwatch.StartNew();

    /// <summary>序列时长（毫秒）。</summary>
    /// <value>末帧的 <c>TimeMs</c>，不是 帧数/Fps——节流丢帧后两者并不相等；空序列为 0。</value>
    public double TotalMs => Frames.Count > 0 ? Frames[^1].TimeMs : 0;
    /// <summary>帧数不超过 1。</summary>
    /// <value>空序列也算单帧，所以它不等于"恰好一张静图"。</value>
    public bool IsSingle => Frames.Count <= 1;
}
