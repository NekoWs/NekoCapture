namespace FrameCapture.Core;

/// <summary>
/// 钉图滚轮缩放的纯决策——按滚轮方向乘一个固定倍率并夹到 [min,max]。
/// 与 WPF 解耦，SelfTest 可断言（只增不减的缩放手感、上下限锁死）。
/// </summary>
public static class PinZoom
{
    public const double Min = 0.1, Max = 6.0, Step = 1.1;

    /// <summary>delta&gt;0 放大、&lt;0 缩小、0 不变；结果夹在 [min,max]。</summary>
    public static double Next(double cur, int delta, double min = Min, double max = Max, double step = Step)
    {
        double f = delta > 0 ? step : delta < 0 ? 1.0 / step : 1.0;
        return Math.Clamp(cur * f, min, max);
    }
}
