namespace FrameCapture.Core;

/// <summary>
/// 文字标注字号：线宽挡位 → 帧像素字号，编辑器预览与烘焙共用同一公式，所见即所得。
/// 细(3)=22px、中(6)=34px、粗(10)=50px @1080p，随帧短边等比缩放。
/// </summary>
public static class AnnFont
{
    /// <summary>1080p 基准字号。</summary>
    /// <param name="thickness">线宽挡位值（细 3 / 中 6 / 粗 10）。</param>
    /// <returns>10 + thickness×4，单位是 1080p 帧上的像素，不是屏幕 DIP。</returns>
    public static double FramePx(float thickness) => 10 + thickness * 4;

    /// <summary>目标帧上的字号，下限 10 防不可读。</summary>
    /// <param name="thickness">线宽挡位值。</param>
    /// <param name="w">帧宽（像素）。</param>
    /// <param name="h">帧高（像素）。</param>
    /// <returns>FramePx × min(w,h)/1080 再取 max(10,·)：按短边缩放，小帧也不会缩到看不清。</returns>
    public static double BakePx(float thickness, int w, int h) =>
        Math.Max(10, FramePx(thickness) * Math.Min(w, h) / 1080.0);
}
