namespace NekoCapture.Core;

/// <summary>
/// 捕获帧率节流纯逻辑。与 WGC/线程解耦，供单测。
/// 间隔是双精度毫秒且<b>无地板</b>：任何整数毫秒下限（如 16ms）都会把 120/144/240fps 全压回 ~62fps，
/// 而设置里仍显示用户选的帧率——只有画面看起来不流畅，不报错。
/// 用相位累加（下次到期时刻）而不是「距上次保留满一个间隔」：WGC 投递率不一定是目标帧率的整数倍
/// （例如 36fps 投递、30fps 目标），逐帧比较会把采样率砍到一半；累加式每个目标窗口只留一帧。
/// </summary>
public static class FpsThrottle
{
    /// <summary>目标采样间隔(ms)。</summary>
    /// <param name="fps">目标帧率，越界值钳到 AppSettings 的 FpsMin–FpsMax 后再取倒数。</param>
    /// <returns>1000/fps 的双精度毫秒，无 16ms 地板——240fps 低至 ~4.17ms。</returns>
    public static double IntervalMs(int fps) => 1000.0 / Math.Clamp(fps, AppSettings.FpsMin, AppSettings.FpsMax);

    /// <summary>这一帧是否保留。keepAll（目标帧率≥显示器刷新率）全保。</summary>
    /// <param name="nowMs">本帧到达时刻，毫秒，须与上次调用的时钟源一致。</param>
    /// <param name="intervalMs">目标间隔，来自 <see cref="IntervalMs"/>。</param>
    /// <param name="keepAll">true = 节流无意义（目标已不低于刷新率），一律保留。</param>
    /// <param name="dueMs">下次应保留的时刻；调用方持有，初值 &lt;= 0 表示还没保留过帧。</param>
    /// <returns>true = 保留并推进 <paramref name="dueMs"/>。首帧必保；之后到达时刻不早于到期时刻才保，
    /// 到期时刻按目标间隔累加；落后超过一个间隔时从当前时刻重新起算，避免补帧造成突发。
    /// 0.5ms 容差抵消计时抖动。</returns>
    public static bool ShouldKeep(long nowMs, double intervalMs, bool keepAll, ref double dueMs)
    {
        if (keepAll) return true;
        if (dueMs <= 0) { dueMs = nowMs + intervalMs; return true; }
        if (nowMs + 0.5 < dueMs) return false;
        dueMs += intervalMs;
        if (dueMs < nowMs) dueMs = nowMs + intervalMs;
        return true;
    }
}
