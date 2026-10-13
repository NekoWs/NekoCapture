namespace FrameCapture.Core;

/// <summary>
/// 裁剪拖框引导：把"拖到哪"换算成引导层几何——纯逻辑，SelfTest 覆盖。
/// 坐标全部是画布 DIP；提示气泡放不下时翻到框上方，再不行钳进画布。
/// </summary>
public static class CropGuide
{
    /// <summary>拖框两点对角 → 矩形（任意拖拽方向）。</summary>
    /// <param name="ax">按下点横坐标（画布 DIP）。</param>
    /// <param name="ay">按下点纵坐标（画布 DIP）。</param>
    /// <param name="bx">当前点横坐标（画布 DIP）。</param>
    /// <param name="by">当前点纵坐标（画布 DIP）。</param>
    /// <returns>(X,Y,W,H)（DIP）：左上角取两点较小值、W/H 非负，故四向拖框都成立。</returns>
    public static (double X, double Y, double W, double H) Rect(double ax, double ay, double bx, double by)
        => (Math.Min(ax, bx), Math.Min(ay, by), Math.Abs(bx - ax), Math.Abs(by - ay));

    /// <summary>
    /// 「松开应用」气泡左上角：默认框下方 gap；下方放不下翻到框上方；仍越界则纵向钳进画布，横向钳边。
    /// </summary>
    /// <param name="rx">裁剪框左上角横坐标（画布 DIP）。</param>
    /// <param name="ry">裁剪框左上角纵坐标（画布 DIP）。</param>
    /// <param name="rw">裁剪框宽（DIP），用于水平居中。</param>
    /// <param name="rh">裁剪框高（DIP），决定气泡默认落在框下缘。</param>
    /// <param name="hintW">气泡宽（DIP）。</param>
    /// <param name="hintH">气泡高（DIP），参与上下翻转判定。</param>
    /// <param name="canvasW">画布宽（DIP），横向各留 4px 边距。</param>
    /// <param name="canvasH">画布高（DIP），纵向钳制的下边界。</param>
    /// <param name="gap">气泡与框边的间距（DIP）。</param>
    /// <returns>气泡左上角 (X,Y)（DIP）：水平居中于框并钳边；上下都挤时贴框内下缘。</returns>
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
