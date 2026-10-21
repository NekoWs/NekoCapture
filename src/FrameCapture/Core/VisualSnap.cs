using FrameCapture.Capture;

namespace FrameCapture.Core;

/// <summary>
/// 视觉吸附：不看无障碍树，只看像素。在光标周围取一小块画面，按列/行统计边缘强度，
/// 从光标向四个方向各找最近的一条强线，四条线围成一个闭合矩形就当成控件边界。
///
/// 存在的理由：有些界面根本不暴露无障碍元素（游戏、canvas 自绘、部分自绘 UI），
/// 那时 UIA 只能给到整窗。像素里只要有边界，这里就能吸。
/// 代价是它只看得到「长得像框的东西」——装饰线也会被当成边，所以它排在 UIA 之后，
/// 并且要求四条边都强、尺寸不过大不过小，找不到就返回 null 交给整窗吸附。
/// </summary>
public static class VisualSnap
{
    /// <summary>取景窗口的边长（物理像素）：以光标为中心取这么多，够罩住常见控件。</summary>
    public const int WindowSide = 400;
    /// <summary>控件的最小边长（物理像素）。</summary>
    public const int MinSide = 16;
    /// <summary>强线门限：该列/行的边缘强度要达到最强那条的这个比例。</summary>
    const double StrongRatio = 0.35;
    /// <summary>单像素判定为边缘的梯度门限。</summary>
    const int EdgeThreshold = 24;

    /// <summary>在光标附近找一块矩形控件。</summary>
    /// <param name="frozen">冻结的整屏帧；null 或没覆盖到光标时现场抓一小块。</param>
    /// <param name="monitor">光标所在显示器的边界（物理像素），用来夹住取景窗口。</param>
    /// <param name="physX">光标横坐标（物理像素）。</param>
    /// <param name="physY">光标纵坐标（物理像素）。</param>
    /// <returns>吸到的矩形；边界不闭合、尺寸不合规、像素抓不到时为 null。</returns>
    public static RectI? At(CapturedFrame? frozen, RectI monitor, int physX, int physY)
    {
        int half = WindowSide / 2;
        var view = new RectI(
            Math.Max(monitor.X, physX - half),
            Math.Max(monitor.Y, physY - half),
            Math.Min(WindowSide, monitor.W),
            Math.Min(WindowSide, monitor.H));
        if (view.W < MinSide || view.H < MinSide) return null;

        byte[]? bgra; int w, h;
        if (TrySlice(frozen, view, out bgra, out w, out h))
        {
            // 用冻结帧：与用户看到的那一帧一致，也不会因为窗口在动而量歪
        }
        else
        {
            var grab = LiveCapture.Grab(view);
            if (grab == null) return null;
            bgra = grab.Bgra; w = grab.W; h = grab.H;
        }
        return Best(bgra, w, h, view.X, view.Y, physX, physY);
    }

    /// <summary>纯几何部分：从一块 BGRA 里找出光标所在的矩形控件。</summary>
    /// <param name="bgra">像素，BGRA 每像素 4 字节，行优先。</param>
    /// <param name="w">宽（像素）。</param>
    /// <param name="h">高（像素）。</param>
    /// <param name="originX">这块像素左上角的桌面物理横坐标。</param>
    /// <param name="originY">这块像素左上角的桌面物理纵坐标。</param>
    /// <param name="physX">光标桌面物理横坐标。</param>
    /// <param name="physY">光标桌面物理纵坐标。</param>
    /// <returns>吸到的矩形；没有闭合的四条强线时为 null。</returns>
    public static RectI? Best(byte[] bgra, int w, int h, int originX, int originY, int physX, int physY)
    {
        int cx = physX - originX, cy = physY - originY;
        if (w < MinSide || h < MinSide || cx < 1 || cy < 1 || cx >= w - 1 || cy >= h - 1) return null;
        if (bgra.Length < w * h * 4) return null;

        // 逐列、逐行累加边缘强度：竖线在列上尖、横线在行上尖
        var col = new long[w];
        var row = new long[h];
        for (int y = 1; y < h - 1; y++)
        {
            int o = y * w * 4;
            for (int x = 1; x < w - 1; x++)
            {
                int i = o + x * 4;
                int gx = Math.Abs(Bgra(bgra, i + 4) - Bgra(bgra, i - 4));       // 横向差分 = 竖边
                int gy = Math.Abs(Bgra(bgra, i + w * 4) - Bgra(bgra, i - w * 4)); // 纵向差分 = 横边
                int s = gx + gy;
                if (s < EdgeThreshold) continue;
                col[x] += s;
                row[y] += s;
            }
        }
        long colMax = col.Max(), rowMax = row.Max();
        if (colMax <= 0 || rowMax <= 0) return null;   // 纯色区域：没有任何边
        long colNeed = (long)(colMax * StrongRatio), rowNeed = (long)(rowMax * StrongRatio);

        int left = -1, right = -1, top = -1, bottom = -1;
        for (int x = cx; x >= 1; x--) if (col[x] >= colNeed) { left = x; break; }
        for (int x = cx; x < w - 1; x++) if (col[x] >= colNeed) { right = x; break; }
        for (int y = cy; y >= 1; y--) if (row[y] >= rowNeed) { top = y; break; }
        for (int y = cy; y < h - 1; y++) if (row[y] >= rowNeed) { bottom = y; break; }

        // 四条边缺一条就不是闭合的框：宁可交回整窗吸附，也不吸半截
        if (left < 0 || right < 0 || top < 0 || bottom < 0) return null;
        int bw = right - left, bh = bottom - top;
        if (bw < MinSide || bh < MinSide) return null;
        // 贴着取景窗口边缘的框是不完整的（真实控件比窗口还大），不采用
        if (left <= 1 || top <= 1 || right >= w - 2 || bottom >= h - 2) return null;
        return new RectI(originX + left, originY + top, bw, bh);
    }

    static int Bgra(byte[] p, int i) => (p[i] * 30 + p[i + 1] * 59 + p[i + 2] * 11) / 100;

    /// <summary>从整屏冻结帧里切出取景窗口那一块；没覆盖到就返回 false。</summary>
    static bool TrySlice(CapturedFrame? frame, RectI view, out byte[] bgra, out int w, out int h)
    {
        bgra = Array.Empty<byte>(); w = 0; h = 0;
        if (frame == null) return false;
        // 冻结帧是整屏的，坐标从 0 起：越界就交回现抓
        if (view.X < 0 || view.Y < 0 || view.X + view.W > frame.W || view.Y + view.H > frame.H) return false;
        var src = frame.Bgra;
        w = view.W; h = view.H;
        bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Array.Copy(src, ((view.Y + y) * frame.W + view.X) * 4, bgra, y * w * 4, w * 4);
        return true;
    }
}
