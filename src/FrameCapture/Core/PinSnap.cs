namespace FrameCapture.Core;

/// <summary>
/// 贴图磁边：拖动松手后把贴图吸附到所在屏幕工作区边缘。
/// 纯函数、坐标单位由调用方统一（这里用 DIP），供单测。
/// </summary>
public static class PinSnap
{
    /// <summary>吸附阈值：短边的 8%，夹在 16–40（DIP）。参考线含所有可视窗口边，阈值再大就会频繁吸到远处的线上——
    /// 「贴边才吸」的确定性比「更容易吸」更重要，故取小值。</summary>
    /// <param name="w">贴图宽（DIP）。</param>
    /// <param name="h">贴图高（DIP）。取短边是为了小图不因某一边长而被过度吸附。</param>
    /// <returns>闭区间 [16, 40] 内的距离阈值（DIP）。</returns>
    public static double Threshold(double w, double h) => Math.Clamp(Math.Min(w, h) * 0.08, 16, 40);

    /// <summary>把窗口 (left,top,w,h) 的左右边吸附到容器 [cl,cr]、上下边吸附到 [ct,cb]：
    /// 某条边距对应容器边在阈值内则贴合（左右互斥、上下互斥，先左后右、先上后下）。返回新位置。纯函数。</summary>
    /// <param name="left">当前左边（DIP）。</param>
    /// <param name="top">当前上边（DIP）。</param>
    /// <param name="w">窗口宽（DIP）。</param>
    /// <param name="h">窗口高（DIP）。</param>
    /// <param name="cl">容器左边。</param>
    /// <param name="ct">容器上边。</param>
    /// <param name="cr">容器右边。</param>
    /// <param name="cb">容器下边。</param>
    /// <param name="threshold">吸附距离阈值（DIP），通常取 <see cref="Threshold"/>。</param>
    /// <returns>新位置 (Left, Top)。未命中任何边时原样返回，调用方可据此判断「没吸上」。</returns>
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
    /// 只做边吸附，不做中心/中线锚点：中线吸附是拖动卡顿感与「莫名吸走」的主要来源。
    /// 每轴取全局最小偏移，超阈值则该轴不动。
    /// 坐标统一为同一 DPI 空间的 DIP。纯函数，供单测。
    /// </summary>
    /// <param name="left">当前左边（DIP）。</param>
    /// <param name="top">当前上边（DIP）。</param>
    /// <param name="w">贴图宽（DIP）。</param>
    /// <param name="h">贴图高（DIP）。</param>
    /// <param name="xEdge">屏幕左右边的 x 坐标；与贴图左右边比对。</param>
    /// <param name="xAny">其他窗口/贴图的竖直参考线 x 坐标。</param>
    /// <param name="yEdge">屏幕上下边的 y 坐标。</param>
    /// <param name="yAny">其他窗口/贴图的水平参考线 y 坐标。</param>
    /// <param name="threshold">吸附距离阈值（DIP），通常取 <see cref="Threshold"/>。</param>
    /// <returns>新位置 (Left, Top)；某轴最小偏移超过阈值时该轴原值返回。</returns>
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
    /// <param name="w">贴图宽（DIP）。</param>
    /// <param name="h">贴图高（DIP）。</param>
    /// <returns>闭区间 [12, 22] 内的距离阈值（DIP）。</returns>
    public static double MagnetDist(double w, double h) => Math.Clamp(Math.Min(w, h) * 0.06, 12, 22);

    /// <summary>一条磁吸参考边：Pos=边的坐标，Span0/Span1=该边在垂直方向上的跨度。
    /// 只有本贴图该轴的边与参考边在垂直方向重叠时才会吸附（Slick Window Arrangement 手感核心）。</summary>
    public readonly struct Edge
    {
        /// <summary>边的坐标（DIP）与它在垂直方向上的可见跨度 [Span0, Span1)。</summary>
        public readonly double Pos, Span0, Span1;
        /// <param name="pos">参考边坐标（DIP）。</param>
        /// <param name="s0">垂直跨度起点。</param>
        /// <param name="s1">垂直跨度终点，须大于 s0 才可能命中重叠判定。</param>
        public Edge(double pos, double s0, double s1) { Pos = pos; Span0 = s0; Span1 = s1; }
    }

    /// <summary>单轴磁吸（参考 Slick Window Arrangement）：本贴图该轴近边（左/上）只与 nearTargets
    /// 里的同类边对齐、远边（右/下）只与 farTargets 对齐（同类边对同类边 + 屏幕边），且要求垂直跨度重叠。
    /// 近/远边各取阈值内最近者（互斥）。返回该轴最终位置（near 值）。纯函数，供单测。</summary>
    /// <param name="near">该轴近边坐标（左边的 x 或上边的 y，DIP）。</param>
    /// <param name="far">该轴远边坐标（右边的 x 或下边的 y，DIP）。</param>
    /// <param name="span0">贴图在垂直方向跨度的起点。</param>
    /// <param name="span1">贴图在垂直方向跨度的终点。</param>
    /// <param name="nearTargets">可与近边对齐的参考边（含屏幕边）。</param>
    /// <param name="farTargets">可与远边对齐的参考边（含屏幕边）。</param>
    /// <param name="threshold">吸附距离阈值（DIP），通常取 <see cref="MagnetDist"/>。</param>
    /// <returns>吸附后的近边坐标；无任何命中时原样返回 near。</returns>
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
    /// <param name="lo">待裁剪区间起点（DIP）。</param>
    /// <param name="hi">待裁剪区间终点；不大于 lo 时返回空表。</param>
    /// <param name="covered">覆盖区间集合，无需预先排序、允许互相重叠。</param>
    /// <returns>可见子段列表，按起点升序且互不重叠；坐标为 (s0, s1) 元组。</returns>
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
