namespace FrameCapture.Core;

/// <summary>
/// 捕获帧率节流纯逻辑。与 WGC/线程解耦，供单测。
/// 间隔是双精度毫秒且<b>无地板</b>：任何整数毫秒下限（如 16ms）都会把 120/144/240fps 全压回 ~62fps，
/// 而设置里仍显示用户选的帧率——只有画面看起来不流畅，不报错。
/// 判定按逐帧时间戳比较，配合事件驱动 FrameArrived 才能真正跑满高刷。
/// </summary>
public static class FpsThrottle
{
    /// <summary>目标采样间隔(ms)。</summary>
    /// <param name="fps">目标帧率，越界值钳到 AppSettings 的 FpsMin–FpsMax 后再取倒数。</param>
    /// <returns>1000/fps 的双精度毫秒，无 16ms 地板——240fps 低至 ~4.17ms。</returns>
    public static double IntervalMs(int fps) => 1000.0 / Math.Clamp(fps, AppSettings.FpsMin, AppSettings.FpsMax);

    /// <summary>这一帧是否保留。keepAll（目标帧率≥显示器刷新率）全保。</summary>
    /// <param name="nowMs">本帧到达时刻，毫秒，须与 <paramref name="lastKeptMs"/> 同一时钟源。</param>
    /// <param name="lastKeptMs">上次保留帧的时刻；&lt;0 表示尚未保留过任何帧。</param>
    /// <param name="intervalMs">目标间隔，来自 <see cref="IntervalMs"/>。</param>
    /// <param name="keepAll">true = 节流无意义（目标已不低于刷新率），一律保留。</param>
    /// <returns>true = 保留并更新 lastKeptMs。首帧必保；其余距上次满一个间隔才保
    /// （-0.5ms 容差抵消计时抖动，避免 60fps 被误降）。</returns>
    public static bool ShouldKeep(long nowMs, long lastKeptMs, double intervalMs, bool keepAll)
        => keepAll || lastKeptMs < 0 || nowMs - lastKeptMs >= intervalMs - 0.5;
}
