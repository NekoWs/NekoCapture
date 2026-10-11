namespace FrameCapture.Core;

/// <summary>
/// 裁剪拖框引导（R45）：把"拖到哪"换算成引导层几何——纯逻辑，SelfTest T89 覆盖。
/// 坐标全部是画布 DIP；提示气泡放不下时翻到框上方，再不行钳进画布。
/// </summary>
public static class CropGuide
{
    /// <summary>拖框两点对角 → 矩形（任意拖拽方向）。</summary>
    public static (double X, double Y, double W, double H) Rect(double ax, double ay, double bx, double by)
        => (Math.Min(ax, bx), Math.Min(ay, by), Math.Abs(bx - ax), Math.Abs(by - ay));

    /// <summary>
    /// 「松开应用」气泡左上角：默认框下方 gap；下方放不下翻到框上方；仍越界则纵向钳进画布，横向钳边。
    /// </summary>
    public static (double X, double Y) HintPos(
        double rx, double ry, double rw, double rh,
        double hintW, double hintH, double canvasW, double canvasH, double gap = 8)
    {
        double y = ry + rh + gap;
        if (y + hintH > canvasH) y = ry - gap - hintH;          // 翻到上方
        if (y < 0) y = Math.Clamp(ry + rh - hintH - gap, 0, Math.Max(0, canvasH - hintH)); // 上下都挤→贴框内下缘
        double x = Math.Clamp(rx + (rw - hintW) / 2, 4, Math.Max(4, canvasW - hintW - 4)); // 水平居中于框，钳边
        return (x, y);
    }
}
