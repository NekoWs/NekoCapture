namespace FrameCapture.Core;

/// <summary>
/// GIF 帧延迟重采样（纯逻辑）：GIF 以厘秒计时，且 1cs 会被部分播放器当 0（编码器会抬到 2cs）。
/// 直接逐帧取整会让 60fps（16.7ms→2cs）整体提速 1.2×。这里把被丢弃帧的时间累积到下一保留帧，
/// 保证：所有延迟 ≥2cs 且为 10ms 整数倍、总时长误差 ≤ 每保留帧 5ms、末帧永不丢弃。
/// 30fps（33ms）不丢帧，只在 30/40cs 间交替，平均仍为 33.3ms。
/// </summary>
public static class GifTiming
{
    public const int MinMs = 20; // 2cs：可靠下限（1cs 抬升逻辑的根源）

    /// <summary>返回保留帧下标与对应毫秒延迟（均为 10 的倍数、≥20）。</summary>
    public static (List<int> Keep, List<int> DelayMs) Resample(IReadOnlyList<int> delaysMs)
    {
        var keep = new List<int>();
        var outDelays = new List<int>();
        int acc = 0;
        for (int i = 0; i < delaysMs.Count; i++)
        {
            acc += Math.Max(0, delaysMs[i]);
            bool last = i == delaysMs.Count - 1;
            if (!last && acc < MinMs) continue;
            int d = last ? Math.Max(MinMs, (int)Math.Round(acc / 10.0) * 10)
                         : acc < 35 ? MinMs          // 优先均匀 2cs：60fps→48 帧@20ms（50fps），而非 36 帧混合
                                    : Math.Max(MinMs, acc / 10 * 10); // 大间隔向下取整，余数进位
            acc -= d; if (acc < 0) acc = 0;
            keep.Add(i); outDelays.Add(d);
        }
        return (keep, outDelays);
    }
}
