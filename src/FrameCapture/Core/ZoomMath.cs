namespace FrameCapture.Core;

/// <summary>
/// 编辑器视图缩放/平移决策数学（纯逻辑，SelfTest T79）。
/// 坐标约定：viewport=视图区尺寸（WPF 逻辑像素），frame=帧像素尺寸；
/// zoom 为相对"适配"的倍率（1=适配，1/sFit=1:1）；pan=图像中心相对视口中心的偏移。
/// 不变量：图像不大于视口时 pan 恒 (0,0)（适配视图居中）；放大后 pan 钳制使图像始终铺满视口（无黑边露出）。
/// </summary>
public static class ZoomMath
{
    public const double MinZoom = 1.0;
    public const double MaxZoom = 16.0;

    public static double FitScale(double vw, double vh, double fw, double fh)
        => fw <= 0 || fh <= 0 || vw <= 0 || vh <= 0 ? 1.0 : Math.Min(vw / fw, vh / fh);

    public static double ClampZoom(double z) => Math.Clamp(z, MinZoom, MaxZoom);

    /// <summary>1:1 视图（1 帧像素 = 1 逻辑像素）对应的适配倍率。</summary>
    public static double OneToOneZoom(double sFit) => sFit <= 0 ? MinZoom : ClampZoom(1.0 / sFit);

    /// <summary>平移钳制：图像小于视口方向强制居中（pan=0）；大于视口时边缘不得内缩。</summary>
    public static (double X, double Y) ClampPan(double panX, double panY, double vw, double vh, double iw, double ih)
    {
        double mx = Math.Max(0, (iw - vw) / 2), my = Math.Max(0, (ih - vh) / 2);
        return (Math.Clamp(panX, -mx, mx), Math.Clamp(panY, -my, my));
    }

    /// <summary>
    /// 以锚点 (cx,cy)（视口坐标）缩放：锚点下的图像点在缩放前后保持不动。
    /// 返回新 (zoom, panX, panY)，pan 已钳制。
    /// </summary>
    public static (double Zoom, double PanX, double PanY) ZoomAt(
        double zoom0, double panX0, double panY0, double zoom1,
        double cx, double cy, double vw, double vh, double fw, double fh)
    {
        double z = ClampZoom(zoom1);
        double r = z / ClampZoom(zoom0);
        double px = (cx - vw / 2) - ((cx - vw / 2) - panX0) * r;
        double py = (cy - vh / 2) - ((cy - vh / 2) - panY0) * r;
        double sFit = FitScale(vw, vh, fw, fh);
        var (x, y) = ClampPan(px, py, vw, vh, fw * sFit * z, fh * sFit * z);
        return (z, x, y);
    }

    /// <summary>拖拽平移：从起始 pan 加总位移（绝对增量），钳制返回。</summary>
    public static (double X, double Y) PanBy(double panX0, double panY0, double dx, double dy,
        double vw, double vh, double iw, double ih)
        => ClampPan(panX0 + dx, panY0 + dy, vw, vh, iw, ih);
}
