namespace FrameCapture.Core;

/// <summary>
/// R66：贴图不透明度滚轮调节的纯决策——按滚轮方向增减一档并夹到 [min,max]。
/// 与 WPF 解耦，SelfTest 可断言（上下限锁死、delta=0 不变）。
/// </summary>
public static class PinOpacity
{
    public const double Min = 0.2, Max = 1.0, Step = 0.1;

    /// <summary>delta&gt;0 更不透明、&lt;0 更透明、0 不变；结果夹在 [min,max]。</summary>
    public static double Next(double cur, int delta, double min = Min, double max = Max, double step = Step)
        => Math.Clamp(cur + (delta > 0 ? step : delta < 0 ? -step : 0), min, max);
}
