namespace FrameCapture.Core;

/// <summary>
/// 贴图磁边（R74）：拖动松手后把贴图吸附到所在屏幕工作区边缘（Snipaste 式手感）。
/// 纯函数、坐标单位由调用方统一（这里用 DIP），供单测。
/// </summary>
public static class PinSnap
{
    /// <summary>吸附阈值：短边的 12%，夹在 24–56（R119 放宽：旧版 6%/12–40 太紧，小图仅 12DIP≈18px，
    /// 用户松手稍离边就不触发，被误判为"磁边无效"。放宽后靠近屏幕边/其他贴图更容易吸上）。</summary>
    public static double Threshold(double w, double h) => Math.Clamp(Math.Min(w, h) * 0.12, 24, 56);

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
    /// 贴图对齐（R77）：拖动松手时把本贴图吸附到一组参考线——屏幕边 + 同屏其他贴图的边/中线。
    /// 本贴图的 left/right 可贴 xEdge（屏幕左右边）与 xAny（其他贴图线）；center 只贴 xAny
    /// （贴图中心贴屏幕边会把半张图推出屏外，故屏幕边不接受中心锚点）；y 轴同理。
    /// 每轴取全局最小偏移，超阈值则该轴不动。坐标统一为同一 DPI 空间的 DIP。纯函数，供单测。
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
        foreach (var ln in xAny) { X(ln, left); X(ln, left + w); X(ln, left + w / 2); }
        foreach (var ln in yEdge) { Y(ln, top); Y(ln, top + h); }
        foreach (var ln in yAny) { Y(ln, top); Y(ln, top + h); Y(ln, top + h / 2); }
        if (bx > threshold) nl = left;
        if (by > threshold) nt = top;
        return (nl, nt);
    }
}
