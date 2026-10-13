namespace FrameCapture.Core;

/// <summary>
/// 贴图磁边：拖动松手后把贴图吸附到所在屏幕工作区边缘（Snipaste 式手感）。
/// 纯函数、坐标单位由调用方统一（这里用 DIP），供单测。
/// </summary>
public static class PinSnap
{
    /// <summary>吸附阈值：短边的 8%，夹在 16–40。曾放宽到 12%/24–56（小图吸不上被误判无效）；
    /// 参考线扩展到所有窗口后回调到 8%/16–40——线多了阈值再大就会频繁吸到远处的线上，
    /// 「贴边才吸」的确定性比「更容易吸」更重要（用户点名阈值调低）。</summary>
    public static double Threshold(double w, double h) => Math.Clamp(Math.Min(w, h) * 0.08, 16, 40);

    /// <summary>把窗口 (left,top,w,h) 的左右边吸附到容器 [cl,cr]、上下边吸附到 [ct,cb]：
    /// 某条边距对应容器边在阈值内则贴合（左右互斥、上下互斥，先左后右、先上后下）。返回新位置。纯函数。</summary>
    public static (double Left, double Top) Snap(double left, double top, double w, double h,
        double cl, double ct, double cr, double cb, double threshold)
    {
        double l = left, t = top;
        if (Math.Abs(l - cl) <= threshold) l = cl;
        else if (Math.Abs(l + w - cr) <= threshold) l = cr - w;
        if (Math.Abs(t - ct) <= threshold) t = ct;
        else if (Math.Abs(t + h - cb) <= threshold) t = cb - h;
        return (l, t);
    }

    /// <summary>
    /// 贴图对齐：拖动松手时把本贴图吸附到一组参考线——屏幕边 + 同屏其他窗口/贴图的边。
    /// 本贴图的 left/right 贴 xEdge（屏幕左右边）与 xAny（其他窗口线）；y 轴同理。
    /// 去掉中心锚点——用户点名「只要吸附屏幕边缘、可视窗口边缘就够了」，中线吸附是
    /// 拖动卡顿感与「莫名吸走」的主要来源。每轴取全局最小偏移，超阈值则该轴不动。
    /// 坐标统一为同一 DPI 空间的 DIP。纯函数，供单测。
    /// </summary>
    public static (double Left, double Top) SnapToLines(double left, double top, double w, double h,
        IReadOnlyList<double> xEdge, IReadOnlyList<double> xAny,
        IReadOnlyList<double> yEdge, IReadOnlyList<double> yAny, double threshold)
    {
        double nl = left, nt = top, bx = double.MaxValue, by = double.MaxValue;
        void X(double line, double anchor)
        {
            double d = Math.Abs(line - anchor);
            if (d < bx) { bx = d; nl = left + (line - anchor); }
        }
        void Y(double line, double anchor)
        {
            double d = Math.Abs(line - anchor);
            if (d < by) { by = d; nt = top + (line - anchor); }
        }
        foreach (var ln in xEdge) { X(ln, left); X(ln, left + w); }
        foreach (var ln in xAny) { X(ln, left); X(ln, left + w); }
        foreach (var ln in yEdge) { Y(ln, top); Y(ln, top + h); }
        foreach (var ln in yAny) { Y(ln, top); Y(ln, top + h); }
        if (bx > threshold) nl = left;
        if (by > threshold) nt = top;
        return (nl, nt);
    }

    /// <summary>拖动实时磁吸阈值（DIP）：参考 Slick Window Arrangement 的固定吸附距离，
    /// 按贴图尺寸微调并夹在 12–22。有了「垂直跨度重叠才吸」的判定，阈值不必很大也不会乱吸。</summary>
    public static double MagnetDist(double w, double h) => Math.Clamp(Math.Min(w, h) * 0.06, 12, 22);

    /// <summary>一条磁吸参考边：Pos=边的坐标，Span0/Span1=该边在垂直方向上的跨度。
    /// 只有本贴图该轴的边与参考边在垂直方向重叠时才会吸附（Slick Window Arrangement 手感核心）。</summary>
    public readonly struct Edge
    {
        public readonly double Pos, Span0, Span1;
        public Edge(double pos, double s0, double s1) { Pos = pos; Span0 = s0; Span1 = s1; }
    }

    /// <summary>单轴磁吸（参考 Slick Window Arrangement）：本贴图该轴近边（左/上）只与 nearTargets
    /// 里的同类边对齐、远边（右/下）只与 farTargets 对齐（同类边对同类边 + 屏幕边），且要求垂直跨度重叠。
    /// 近/远边各取阈值内最近者（互斥）。返回该轴最终位置（near 值）。纯函数，供单测。</summary>
    public static double Magnet(double near, double far, double span0, double span1,
        IReadOnlyList<Edge> nearTargets, IReadOnlyList<Edge> farTargets, double threshold)
    {
        double best = double.MaxValue, shift = 0; bool found = false;
        foreach (var e in nearTargets)
        {
            if (span0 >= e.Span1 || span1 <= e.Span0) continue;   // 垂直跨度不重叠 → 不吸
            double d = Math.Abs(e.Pos - near);
            if (d <= threshold && d < best) { best = d; shift = e.Pos - near; found = true; }
        }
        foreach (var e in farTargets)
        {
            if (span0 >= e.Span1 || span1 <= e.Span0) continue;
            double d = Math.Abs(e.Pos - far);
            if (d <= threshold && d < best) { best = d; shift = e.Pos - far; found = true; }
        }
        return found ? near + shift : near;
    }

    /// <summary>从 [lo,hi] 中减去 covered 覆盖区间，返回可见子段（起点升序）。贴图磁吸的「遮挡裁剪」用——
    /// 一条窗口边被更靠上（Z 序更前）的窗口盖住的部分不算参考边，只吸「看得见的边」（Slick RemoveOverlappedTargets 思路）。
    /// 空 covered → 整段可见；完全覆盖 → 空。纯函数，供单测。</summary>
    public static List<(double s0, double s1)> VisibleSegments(double lo, double hi, IReadOnlyList<(double a, double b)> covered)
    {
        var res = new List<(double, double)>();
        if (hi <= lo) return res;
        if (covered.Count == 0) { res.Add((lo, hi)); return res; }
        var c = new List<(double a, double b)>(covered);
        c.Sort((x, y) => x.a.CompareTo(y.a));
        double cur = lo;
        foreach (var (a, b) in c)
        {
            if (b <= cur) continue;
            if (a > cur) res.Add((cur, Math.Min(a, hi)));
            cur = Math.Max(cur, b);
            if (cur >= hi) break;
        }
        if (cur < hi) res.Add((cur, hi));
        return res;
    }
}
