namespace NekoCapture.Core;

/// <summary>
/// 马赛克粒度：线宽挡位 → 块像素，编辑器预览与烘焙共用，所见即所得。
/// 细(3)=6px、中(6)=12px、粗(10)=20px @1080p，随帧短边等比缩放，下限 4px。
/// </summary>
public static class MosaicBlock
{
    /// <summary>1080p 基准块像素，未随帧缩放、未取下限。</summary>
    /// <param name="thickness">线宽挡位值（细 3 / 中 6 / 粗 10）。</param>
    /// <returns>thickness × 2，单位是 1080p 帧上的像素，不是屏幕 DIP。</returns>
    public static double RawPx(float thickness) => thickness * 2.0;

    /// <summary>目标帧上的块像素，下限 4 防糊成一片。</summary>
    /// <param name="thickness">线宽挡位值。</param>
    /// <param name="w">帧宽（像素）。</param>
    /// <param name="h">帧高（像素）。</param>
    /// <returns>RawPx × min(w,h)/1080 四舍五入后取 max(4,·)：按短边缩放，小帧也不至于看不出打码效果。</returns>
    public static int Px(float thickness, int w, int h) =>
        Math.Max(4, (int)Math.Round(RawPx(thickness) * Math.Min(w, h) / 1080.0));
}
