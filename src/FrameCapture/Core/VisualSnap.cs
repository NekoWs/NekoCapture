using FrameCapture.Capture;

namespace FrameCapture.Core;

/// <summary>
/// 视觉吸附：不看无障碍树，只看像素。在光标周围取一小块画面，找**长直线**围成的闭合框——
/// 竖边要求「同一列上有足够长的连续边」，横边同理。
///
/// 为什么强调「长」：早一版按列/行累加边缘强度，结果文字笔画被当成边，吸出来的全是不相干的小框。
/// 真实控件的边框是一条连续的直线（几十像素不断），而一个字的竖笔画只有十几像素。
/// 这条差别就是判据：连续段长度不够的，一律不算边。
///
/// 存在的理由：有些界面根本不暴露无障碍元素（游戏、canvas 自绘、部分 Electron），
/// 而 Chromium 系的内容树又只走 UIA 的 FromPoint 那条路——选区遮罩一挡就没了。
/// 那一层拿不到时，这里按像素兜底；都找不到就交回整窗吸附，绝不给一个错的框。
/// </summary>
public static class VisualSnap
{
    /// <summary>取景窗口的边长（物理像素）：以光标为中心取这么多。</summary>
    public const int WindowSide = 400;
    /// <summary>控件的最小边长（物理像素）。</summary>
    public const int MinSide = 16;
    /// <summary>直线的最小连续长度：比它还短的连续边是文字笔画，不是边框。</summary>
    public const int MinLine = 20;
    /// <summary>一条边在框的范围内的覆盖率下限：真边框基本是连续的，散点不算。</summary>
    const double MinCoverage = 0.5;
    /// <summary>单像素判定为边缘的梯度门限。</summary>
    const int EdgeThreshold = 24;

    // 同一块像素内的连续悬停：按 4 像素量化后缓存，避免每移动一像素就重算一遍
    private static int _cacheX = int.MinValue, _cacheY;
    private static RectI? _cache;

    /// <summary>在光标附近找一块矩形控件。</summary>
    /// <param name="frozen">冻结的整屏帧；null 或没覆盖到光标时现场抓一小块。</param>
    /// <param name="monitor">光标所在显示器的边界（物理像素），用来夹住取景窗口。</param>
    /// <param name="physX">光标横坐标（物理像素）。</param>
    /// <param name="physY">光标纵坐标（物理像素）。</param>
    /// <returns>吸到的矩形；四条长直线不闭合、尺寸不合规、像素抓不到时为 null。</returns>
    public static RectI? At(CapturedFrame? frozen, RectI monitor, int physX, int physY)
    {
        if (_cacheX != int.MinValue && (physX >> 2) == (_cacheX >> 2) && (physY >> 2) == (_cacheY >> 2))
            return _cache;

        // 取景窗口要整块落在显示器里：只夹左上角会让右边超出，切片读到越界、坐标也算错。
        int vw = Math.Min(WindowSide, monitor.W), vh = Math.Min(WindowSide, monitor.H);
        var view = new RectI(
            Math.Clamp(physX - vw / 2, monitor.X, monitor.X + monitor.W - vw),
            Math.Clamp(physY - vh / 2, monitor.Y, monitor.Y + monitor.H - vh),
            vw, vh);
        RectI? got = null;
        if (view.W >= MinSide && view.H >= MinSide)
        {
            if (TrySlice(frozen, view, out var bgra, out int w, out int h))
                got = Best(bgra, w, h, view.X, view.Y, physX, physY);
            else
            {
                var grab = LiveCapture.Grab(view);
                if (grab != null) got = Best(grab.Bgra, grab.W, grab.H, view.X, view.Y, physX, physY);
            }
        }
        _cacheX = physX; _cacheY = physY; _cache = got;
        return got;
    }

    /// <summary>纯几何部分：从一块 BGRA 里找出光标所在的矩形控件。</summary>
    /// <param name="bgra">像素，BGRA 每像素 4 字节，行优先。</param>
    /// <param name="w">宽（像素）。</param>
    /// <param name="h">高（像素）。</param>
    /// <param name="originX">这块像素左上角的桌面物理横坐标。</param>
    /// <param name="originY">这块像素左上角的桌面物理纵坐标。</param>
    /// <param name="physX">光标桌面物理横坐标。</param>
    /// <param name="physY">光标桌面物理纵坐标。</param>
    /// <param name="minLine">直线的最小连续长度；越小越容易凑出框，也越容易误吸。默认 <see cref="MinLine"/>。</param>
    /// <param name="edgeThreshold">单像素的边缘门限；越小越能看见浅色细边框。默认 <see cref="EdgeThreshold"/>。</param>
    /// <returns>吸到的矩形；没有长直线围成的闭合框时为 null。</returns>
    public static RectI? Best(byte[] bgra, int w, int h, int originX, int originY, int physX, int physY,
        int minLine = MinLine, int edgeThreshold = EdgeThreshold)
    {
        int cx = physX - originX, cy = physY - originY;
        if (w < MinSide || h < MinSide || cx < 1 || cy < 1 || cx >= w - 1 || cy >= h - 1) return null;
        if (bgra.Length < w * h * 4) return null;

        // 第一遍：每列最长「连续竖边」、每行最长「连续横边」——长度才是「边框 vs 笔画」的分水岭
        var colRun = new int[w];
        var rowRun = new int[h];
        for (int x = 1; x < w - 1; x++)
        {
            int run = 0, best = 0;
            for (int y = 1; y < h - 1; y++)
            {
                int i = (y * w + x) * 4;
                if (Math.Abs(Gray(bgra, i + 4) - Gray(bgra, i - 4)) >= edgeThreshold) { run++; if (run > best) best = run; }
                else run = 0;
            }
            colRun[x] = best;
        }
        for (int y = 1; y < h - 1; y++)
        {
            int run = 0, best = 0;
            int o = y * w * 4;
            for (int x = 1; x < w - 1; x++)
            {
                int i = o + x * 4;
                if (Math.Abs(Gray(bgra, i + w * 4) - Gray(bgra, i - w * 4)) >= edgeThreshold) { run++; if (run > best) best = run; }
                else run = 0;
            }
            rowRun[y] = best;
        }

        // 第二遍：从光标向四个方向各取最近的一条「够长的线」
        int left = -1, right = -1, top = -1, bottom = -1;
        for (int x = cx; x >= 1; x--) if (colRun[x] >= minLine) { left = x; break; }
        for (int x = cx; x < w - 1; x++) if (colRun[x] >= minLine) { right = x; break; }
        for (int y = cy; y >= 1; y--) if (rowRun[y] >= minLine) { top = y; break; }
        for (int y = cy; y < h - 1; y++) if (rowRun[y] >= minLine) { bottom = y; break; }
        if (left < 0 || right < 0 || top < 0 || bottom < 0) return null;

        int bw = right - left, bh = bottom - top;
        if (bw < MinSide || bh < MinSide) return null;
        // 贴着取景窗口边缘的框是不完整的（真实控件比窗口还大），不采用
        if (left <= 1 || top <= 1 || right >= w - 2 || bottom >= h - 2) return null;
        // 四条边在框范围内要基本连续：散点凑出来的「边」不算
        if (CoverageV(bgra, w, left, top, bottom, edgeThreshold) < MinCoverage) return null;
        if (CoverageV(bgra, w, right, top, bottom, edgeThreshold) < MinCoverage) return null;
        if (CoverageH(bgra, w, top, left, right, edgeThreshold) < MinCoverage) return null;
        if (CoverageH(bgra, w, bottom, left, right, edgeThreshold) < MinCoverage) return null;
        return new RectI(originX + left, originY + top, bw, bh);
    }

    /// <summary>竖边在 [top,bottom] 范围内的覆盖率。</summary>
    /// <param name="p">像素。</param>
    /// <param name="w">宽（像素）。</param>
    /// <param name="x">列号。</param>
    /// <param name="top">范围上界。</param>
    /// <param name="bottom">范围下界。</param>
    /// <param name="edgeThreshold">边缘门限。</param>
    /// <returns>0..1 的比例。</returns>
    static double CoverageV(byte[] p, int w, int x, int top, int bottom, int edgeThreshold)
    {
        int hit = 0, all = Math.Max(1, bottom - top);
        for (int y = top; y < bottom; y++)
        {
            int i = (y * w + x) * 4;
            if (Math.Abs(Gray(p, i + 4) - Gray(p, i - 4)) >= edgeThreshold) hit++;
        }
        return (double)hit / all;
    }

    /// <summary>横边在 [left,right] 范围内的覆盖率。</summary>
    /// <param name="p">像素。</param>
    /// <param name="w">宽（像素）。</param>
    /// <param name="y">行号。</param>
    /// <param name="left">范围左界。</param>
    /// <param name="right">范围右界。</param>
    /// <param name="edgeThreshold">边缘门限。</param>
    /// <returns>0..1 的比例。</returns>
    static double CoverageH(byte[] p, int w, int y, int left, int right, int edgeThreshold)
    {
        int hit = 0, all = Math.Max(1, right - left);
        for (int x = left; x < right; x++)
        {
            int i = (y * w + x) * 4;
            if (Math.Abs(Gray(p, i + w * 4) - Gray(p, i - w * 4)) >= edgeThreshold) hit++;
        }
        return (double)hit / all;
    }

    static int Gray(byte[] p, int i) => (p[i] * 30 + p[i + 1] * 59 + p[i + 2] * 11) / 100;

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
