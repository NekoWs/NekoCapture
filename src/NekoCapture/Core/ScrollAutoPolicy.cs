namespace NekoCapture.Core;

/// <summary>自动滚动一步的观测结果：拼接循环对当前帧判到了哪一档。</summary>
public enum ScrollAutoObservation
{
    /// <summary>本帧与上一帧不一致：画面还在动，这一步没落定。</summary>
    Unsettled,
    /// <summary>已停稳，重叠覆盖整帧：内容没再滚（到底了，或这一步没生效）。</summary>
    NoNewContent,
    /// <summary>已停稳，找不到重叠：一步滚过了整屏，重叠区已被推出画面。</summary>
    NoOverlap,
    /// <summary>已停稳，新增行数不足 3：一步太小，或只是惯性滚动的抖动。</summary>
    TinyStep,
    /// <summary>已停稳，新内容已拼入。</summary>
    Appended,
}

/// <summary>一次观测得出的步进决定：要不要发下一步、要不要收工，以及三个状态字段该写成的新值。</summary>
public readonly struct ScrollAutoDecision
{
    /// <value>true = 立刻注入一步滚轮。</value>
    public bool Inject { get; }
    /// <value>true = 判定已到内容底部，结束自动滚动（停在原地，出图仍由用户点「完成」）。</value>
    public bool Stop { get; }
    /// <value>「等这一步落定」标志的新值。</value>
    public bool Waiting { get; }
    /// <value>连续「停稳且画面没变」计数的新值。</value>
    public int NoNew { get; }
    /// <value>步长（滚轮格数）的新值，恒在 <see cref="ScrollAutoPolicy.NotchMin"/> 到 NotchMax 之间。</value>
    public int Notch { get; }

    /// <summary>组一次步进决定，由 <see cref="ScrollAutoPolicy.Next"/> 产出。</summary>
    /// <param name="inject">是否注入下一步。</param>
    /// <param name="stop">是否结束自动滚动。</param>
    /// <param name="waiting">「等这一步落定」新值。</param>
    /// <param name="noNew">连续无新内容计数新值。</param>
    /// <param name="notch">步长新值。</param>
    public ScrollAutoDecision(bool inject, bool stop, bool waiting, int noNew, int notch)
    {
        Inject = inject; Stop = stop; Waiting = waiting; NoNew = noNew; Notch = notch;
    }
}

/// <summary>
/// 自动滚动的步进策略：发一步、采样拼接、停稳后决定下一步。
/// 宿主按实际时间限制注轮间隔与停稳等待；等待期间继续拼接，不发出下一步滚轮。
/// 步长自适应：滚过头（找不到重叠）减半重试，一步太小（新增不足 3 行）加大一档，恒夹在 1..6 格。
/// 连续 <see cref="BottomStreak"/> 次「停稳且画面没变」判定到底：停止注入，但不自动出图。
/// </summary>
public static class ScrollAutoPolicy
{
    /// <summary>进入自动滚动时的初始步长（滚轮格数），约 6 行内容。</summary>
    public const int NotchStart = 2;
    /// <summary>步长下限：再小就滚不动内容，且减半重试不能减到 0（0 格等于不发轮）。</summary>
    public const int NotchMin = 1;
    /// <summary>步长上限：再大容易一步越过整屏，重叠区直接找不到。</summary>
    public const int NotchMax = 6;
    /// <summary>连续多少次「停稳且画面没变」判定为已到内容底部：给惯性滚动和懒加载留余量。</summary>
    public const int BottomStreak = 4;

    /// <summary>按本帧观测算出下一步决定。</summary>
    /// <param name="waiting">当前是否处于「已发一步、等它落定」。</param>
    /// <param name="noNew">当前连续「停稳且画面没变」次数，负数按 0 处理。</param>
    /// <param name="notch">当前步长（滚轮格数），越界会被夹到 1..6。</param>
    /// <param name="obs">本帧观测结果。</param>
    /// <returns>要不要发下一步 / 是否收工，以及三个状态字段的新值。</returns>
    public static ScrollAutoDecision Next(bool waiting, int noNew, int notch, ScrollAutoObservation obs)
    {
        int n = Math.Clamp(notch, NotchMin, NotchMax);
        int k = Math.Max(0, noNew);
        return obs switch
        {
            // 还在滚：三个状态原样保持，等下一帧再判
            ScrollAutoObservation.Unsettled => new ScrollAutoDecision(false, false, waiting, k, n),
            // 画面没变：数到底就收工，没数满就按同步长再发一步（惯性滚动、懒加载会先空几拍）
            ScrollAutoObservation.NoNewContent => k + 1 >= BottomStreak
                ? new ScrollAutoDecision(false, true, false, k + 1, n)
                : new ScrollAutoDecision(true, false, true, k + 1, n),
            // 滚过头：减半重试，这一步算重试，不计入到底
            ScrollAutoObservation.NoOverlap => new ScrollAutoDecision(true, false, true, 0, Math.Max(NotchMin, n / 2)),
            // 一步太小：加大一档再发，画面没变计数不动
            ScrollAutoObservation.TinyStep => new ScrollAutoDecision(true, false, true, k, Math.Min(NotchMax, n + 1)),
            // 拼上了：计数清零，按同步长接着发
            _ => new ScrollAutoDecision(true, false, true, 0, n),
        };
    }
}
