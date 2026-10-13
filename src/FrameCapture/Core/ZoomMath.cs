namespace FrameCapture.Core;

/// <summary>
/// 编辑器视图缩放/平移决策数学（纯逻辑，可单测）。
/// 坐标约定：viewport=视图区尺寸（WPF 逻辑像素），frame=帧像素尺寸；
/// zoom 为相对"适配"的倍率（1=适配，1/sFit=1:1）；pan=图像中心相对视口中心的偏移。
/// 不变量：图像不大于视口时 pan 恒 (0,0)（适配视图居中）；放大后 pan 钳制使图像始终铺满视口（无黑边露出）。
/// </summary>
public static class ZoomMath
{
    /// <summary>最小倍率：1.0，即「整帧放进视口」的适配视图——不允许比适配更小。</summary>
    public const double MinZoom = 1.0;
    /// <summary>最大倍率：适配的 16 倍。再往上只是把像素摊成色块，没有可读信息。</summary>
    public const double MaxZoom = 16.0;

    /// <summary>
    /// 适配倍率：把整帧放进视口，但**封顶 1.0**——小图绝不放大填充（放大只会糊）。
    /// 图小于视口 → 1.0（逐像素 1:1 居中，QQ/Snipaste 式清晰）；图大于视口 → 缩小到刚好放下。
    /// 这是编辑器默认视图与「适配」键的基准；放大仍靠 Ctrl+滚轮 / 1:1 键显式进行。
    /// </summary>
    /// <param name="vw">视口宽（逻辑像素）。</param>
    /// <param name="vh">视口高（逻辑像素）。</param>
    /// <param name="fw">帧宽（像素）。</param>
    /// <param name="fh">帧高（像素）。</param>
    /// <returns>倍率，恒在 (0, 1.0]；任一尺寸非正时返回 1.0（无帧可放，按 1:1 处理）。</returns>
    public static double FitScale(double vw, double vh, double fw, double fh)
        => fw <= 0 || fh <= 0 || vw <= 0 || vh <= 0 ? 1.0 : Math.Min(1.0, Math.Min(vw / fw, vh / fh));

    /// <summary>把任意倍率夹进 [<see cref="MinZoom"/>, <see cref="MaxZoom"/>]。</summary>
    /// <param name="z">待夹的倍率，NaN 之外的任意值。</param>
    /// <returns>合法区间内的倍率。</returns>
    public static double ClampZoom(double z) => Math.Clamp(z, MinZoom, MaxZoom);

    /// <summary>1:1 视图（1 帧像素 = 1 逻辑像素）对应的适配倍率。</summary>
    /// <param name="sFit">当前 <see cref="FitScale"/> 结果。</param>
    /// <returns>1/sFit 夹进合法区间；sFit 非正时退回 MinZoom。</returns>
    public static double OneToOneZoom(double sFit) => sFit <= 0 ? MinZoom : ClampZoom(1.0 / sFit);

    /// <summary>平移钳制：图像小于视口方向强制居中（pan=0）；大于视口时边缘不得内缩。</summary>
    /// <param name="panX">期望的横向偏移（逻辑像素）。</param>
    /// <param name="panY">期望的纵向偏移（逻辑像素）。</param>
    /// <param name="vw">视口宽（逻辑像素）。</param>
    /// <param name="vh">视口高（逻辑像素）。</param>
    /// <param name="iw">图像显示宽（逻辑像素，= 帧宽 × 总倍率）。</param>
    /// <param name="ih">图像显示高（逻辑像素）。</param>
    /// <returns>钳制后的 (X, Y)；某方向图像不大于视口时该方向恒 0。</returns>
    public static (double X, double Y) ClampPan(double panX, double panY, double vw, double vh, double iw, double ih)
    {
        double mx = Math.Max(0, (iw - vw) / 2), my = Math.Max(0, (ih - vh) / 2);
        return (Math.Clamp(panX, -mx, mx), Math.Clamp(panY, -my, my));
    }

    /// <summary>
    /// 以锚点 (cx,cy)（视口坐标）缩放：锚点下的图像点在缩放前后保持不动。
    /// 返回新 (zoom, panX, panY)，pan 已钳制。
    /// </summary>
    /// <param name="zoom0">缩放前的倍率。</param>
    /// <param name="panX0">缩放前的横向偏移（逻辑像素）。</param>
    /// <param name="panY0">缩放前的纵向偏移（逻辑像素）。</param>
    /// <param name="zoom1">期望的新倍率，内部先夹进合法区间。</param>
    /// <param name="cx">锚点 X（视口坐标，逻辑像素）。</param>
    /// <param name="cy">锚点 Y（视口坐标，逻辑像素）。</param>
    /// <param name="vw">视口宽（逻辑像素）。</param>
    /// <param name="vh">视口高（逻辑像素）。</param>
    /// <param name="fw">帧宽（像素），用于算 sFit 与钳制边界。</param>
    /// <param name="fh">帧高（像素）。</param>
    /// <returns>新状态 (Zoom, PanX, PanY)：Zoom 已夹进合法区间，Pan 已按新尺寸钳制。</returns>
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
    /// <param name="panX0">拖拽起点的横向偏移（逻辑像素）。</param>
    /// <param name="panY0">拖拽起点的纵向偏移（逻辑像素）。</param>
    /// <param name="dx">本次拖拽的横向累计位移（逻辑像素）。</param>
    /// <param name="dy">本次拖拽的纵向累计位移（逻辑像素）。</param>
    /// <param name="vw">视口宽（逻辑像素）。</param>
    /// <param name="vh">视口高（逻辑像素）。</param>
    /// <param name="iw">图像显示宽（逻辑像素）。</param>
    /// <param name="ih">图像显示高（逻辑像素）。</param>
    /// <returns>钳制后的 (X, Y)。</returns>
    public static (double X, double Y) PanBy(double panX0, double panY0, double dx, double dy,
        double vw, double vh, double iw, double ih)
        => ClampPan(panX0 + dx, panY0 + dy, vw, vh, iw, ih);

    /// <summary>
    /// 裁剪视图矩形：整帧（frameW×frameH 像素）按缩放 s 绘制后的显示矩形，
    /// 使「裁剪子区（归一化 crop*）的中心」恰落在视口中心 + pan。
    /// 不变量：X + (cropX+cropW/2)*W == vw/2 + panX（Y 同理）——非居中裁剪也不会把画面甩出视口。
    /// </summary>
    /// <param name="vw">视口宽（逻辑像素）。</param>
    /// <param name="vh">视口高（逻辑像素）。</param>
    /// <param name="frameW">帧宽（像素）。</param>
    /// <param name="frameH">帧高（像素）。</param>
    /// <param name="cropX">裁剪子区左上角 X，全帧归一化比例。</param>
    /// <param name="cropY">裁剪子区左上角 Y，全帧归一化比例。</param>
    /// <param name="cropW">裁剪子区宽，归一化比例。</param>
    /// <param name="cropH">裁剪子区高，归一化比例。</param>
    /// <param name="s">整帧的总显示倍率（逻辑像素 / 帧像素）。</param>
    /// <param name="panX">横向偏移（逻辑像素）。</param>
    /// <param name="panY">纵向偏移（逻辑像素）。</param>
    /// <returns>整帧的显示矩形 (X, Y, W, H)，逻辑像素；W/H = 帧尺寸 × s，X/Y 可为负（画面被推出视口）。</returns>
    public static (double X, double Y, double W, double H) CropViewRect(
        double vw, double vh, double frameW, double frameH,
        double cropX, double cropY, double cropW, double cropH, double s, double panX, double panY)
    {
        double w = frameW * s, h = frameH * s;
        return (vw / 2 + panX - (cropX + cropW / 2) * w,
                vh / 2 + panY - (cropY + cropH / 2) * h,
                w, h);
    }
}
