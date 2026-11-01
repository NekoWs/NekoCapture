namespace NekoCapture.Core;

/// <summary>
/// Shift 约束绘制的纯几何：正方形/正圆 与 45° 角度吸附。
/// 与 WPF/鼠标解耦，只吃锚点 + 当前点，供单测；UI 侧在拖拽预览与松手提交两处调用同一函数保证所见即所得。
/// </summary>
public static class ShapeConstraint
{
    /// <summary>把当前点相对锚点的偏移约束成正方/正圆：取 |dx|、|dy| 较小者作边长，保留拖拽方向的符号。
    /// 沿轴纯水平/纯垂直拖（一边为 0）会退化为 0 尺寸，交由上层「太短丢弃」处理，不特殊化。</summary>
    /// <param name="ax">锚点（按下点）横坐标，画布 DIP。</param>
    /// <param name="ay">锚点纵坐标，画布 DIP。</param>
    /// <param name="x">当前点横坐标，画布 DIP。</param>
    /// <param name="y">当前点纵坐标，画布 DIP。</param>
    /// <returns>约束后的对角点 (X,Y)（DIP）：与锚点围成正方形，且落在原拖拽所在的象限。</returns>
    public static (double X, double Y) Square(double ax, double ay, double x, double y)
    {
        double dx = x - ax, dy = y - ay;
        double s = Math.Min(Math.Abs(dx), Math.Abs(dy));
        return (ax + Math.Sign(dx) * s, ay + Math.Sign(dy) * s);
    }

    /// <summary>把锚点到当前点的方向吸附到最近的 45° 倍数（0/45/90/…），长度保持不变。</summary>
    /// <param name="ax">锚点横坐标，画布 DIP。</param>
    /// <param name="ay">锚点纵坐标，画布 DIP。</param>
    /// <param name="x">当前点横坐标，画布 DIP。</param>
    /// <param name="y">当前点纵坐标，画布 DIP。</param>
    /// <returns>吸附后的点：与锚点距离不变，方向取 Atan2 的最近 45° 倍数；两点重合（距离≈0）时原样返回。</returns>
    public static (double X, double Y) Angle45(double ax, double ay, double x, double y)
    {
        double dx = x - ax, dy = y - ay;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return (x, y);
        double step = Math.PI / 4;
        double ang = Math.Round(Math.Atan2(dy, dx) / step) * step;
        return (ax + len * Math.Cos(ang), ay + len * Math.Sin(ang));
    }
}
