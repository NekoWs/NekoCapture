namespace FrameCapture.Core;

/// <summary>
/// 捕获帧率节流纯逻辑（R100）。与 WGC/线程解耦，供单测。
/// 旧实现用 <c>Math.Max(16, 1000/fps)</c> 的整数毫秒地板，把帧率硬卡在 ~62fps；
/// 这里改为无地板的双精度间隔 + 逐帧时间判定，配合事件驱动 FrameArrived 才能真正跑满高刷。
/// </summary>
public static class FpsThrottle
{
    /// <summary>目标采样间隔(ms)，无 16ms 地板——240fps 低至 ~4.17ms。</summary>
    public static double IntervalMs(int fps) => 1000.0 / Math.Clamp(fps, AppSettings.FpsMin, AppSettings.FpsMax);

    /// <summary>这一帧是否保留。keepAll（目标帧率≥显示器刷新率）全保；
    /// 否则距上次保留满一个间隔才保（-0.5ms 容差抵消计时抖动，避免 60fps 被误降）。
    /// lastKeptMs &lt; 0 表示尚未保留过任何帧，首帧必保。</summary>
    public static bool ShouldKeep(long nowMs, long lastKeptMs, double intervalMs, bool keepAll)
        => keepAll || lastKeptMs < 0 || nowMs - lastKeptMs >= intervalMs - 0.5;
}
