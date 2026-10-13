namespace FrameCapture.Core;

/// <summary>
/// 文字标注字号：线宽挡位 → 帧像素字号，编辑器预览与烘焙共用同一公式，所见即所得。
/// 细(3)=22px、中(6)=34px、粗(10)=50px @1080p，随帧短边等比缩放。
/// </summary>
public static class AnnFont
{
    /// <summary>1080p 基准字号（帧像素）。</summary>
    public static double FramePx(float thickness) => 10 + thickness * 4;

    /// <summary>目标帧上的字号（帧像素），下限 10 防不可读。</summary>
    public static double BakePx(float thickness, int w, int h) =>
        Math.Max(10, FramePx(thickness) * Math.Min(w, h) / 1080.0);
}
