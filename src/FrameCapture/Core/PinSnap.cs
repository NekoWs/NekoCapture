namespace FrameCapture.Core;

/// <summary>
/// 贴图磁边（R74）：拖动松手后把贴图吸附到所在屏幕工作区边缘（Snipaste 式手感）。
/// 纯函数、坐标单位由调用方统一（这里用 DIP），供单测。
/// </summary>
public static class PinSnap
{
    /// <summary>吸附阈值：短边的 6%，夹在 12–40（小图更灵敏、大图别误吸）。</summary>
    public static double Threshold(double w, double h) => Math.Clamp(Math.Min(w, h) * 0.06, 12, 40);

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
}
