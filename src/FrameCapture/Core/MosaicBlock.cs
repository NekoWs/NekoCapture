namespace FrameCapture.Core;

/// <summary>
/// 马赛克粒度（R47）：线宽挡位 → 块像素，编辑器预览与烘焙共用，所见即所得。
/// 细(3)=6px、中(6)=12px、粗(10)=20px @1080p，随帧短边等比缩放，下限 4px。
/// </summary>
public static class MosaicBlock
{
    public static double RawPx(float thickness) => thickness * 2.0;

    public static int Px(float thickness, int w, int h) =>
        Math.Max(4, (int)Math.Round(RawPx(thickness) * Math.Min(w, h) / 1080.0));
}
