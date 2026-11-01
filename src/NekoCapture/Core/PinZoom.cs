namespace NekoCapture.Core;

/// <summary>
/// 钉图滚轮缩放的纯决策——按滚轮方向乘一个固定倍率并夹到 [min,max]。
/// 与 WPF 解耦，SelfTest 可断言（只增不减的缩放手感、上下限锁死）。
/// </summary>
public static class PinZoom
{
    public const double Min = 0.1, Max = 6.0, Step = 1.1;

    /// <summary>滚轮再走一档后的缩放倍率。</summary>
    /// <param name="cur">当前倍率。</param>
    /// <param name="delta">滚轮方向：&gt;0 放大、&lt;0 缩小、0 不变。只看符号，不看滚动格数。</param>
    /// <param name="min">下限倍率，默认 <see cref="Min"/>。</param>
    /// <param name="max">上限倍率，默认 <see cref="Max"/>。</param>
    /// <param name="step">每档倍率，默认 <see cref="Step"/>；缩小档取其倒数，故来回一档近似可逆。</param>
    /// <returns>cur × 档位系数后钳到 [min,max]。</returns>
    public static double Next(double cur, int delta, double min = Min, double max = Max, double step = Step)
    {
        double f = delta > 0 ? step : delta < 0 ? 1.0 / step : 1.0;
        return Math.Clamp(cur * f, min, max);
    }
}
