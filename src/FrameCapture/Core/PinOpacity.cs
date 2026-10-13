namespace FrameCapture.Core;

/// <summary>
/// 贴图不透明度滚轮调节的纯决策——按滚轮方向增减一档并夹到 [min,max]。
/// 与 WPF 解耦，SelfTest 可断言（上下限锁死、delta=0 不变）。
/// </summary>
public static class PinOpacity
{
    public const double Min = 0.2, Max = 1.0, Step = 0.1;

    /// <summary>滚轮再走一档后的不透明度。</summary>
    /// <param name="cur">当前不透明度。</param>
    /// <param name="delta">滚轮方向：&gt;0 更不透明、&lt;0 更透明、0 不变。只看符号，不看滚动格数。</param>
    /// <param name="min">下限，默认 <see cref="Min"/>：再透也留 0.2，免得贴图整窗点不到。</param>
    /// <param name="max">上限，默认 <see cref="Max"/>。</param>
    /// <param name="step">每档增量，默认 <see cref="Step"/>。</param>
    /// <returns>cur ± step 后钳到 [min,max]；已在边界时继续同向滚动就停在边界值。</returns>
    public static double Next(double cur, int delta, double min = Min, double max = Max, double step = Step)
        => Math.Clamp(cur + (delta > 0 ? step : delta < 0 ? -step : 0), min, max);
}
