namespace FrameCapture.Core;

/// <summary>时间轴刻度尺的纯逻辑（SelfTest 覆盖）：秒标步长选择。</summary>
public static class TimelineScale
{
    /// <summary>候选时间步长（秒）。</summary>
    public static readonly double[] TimeSteps = { 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 30 };

    /// <summary>
    /// 秒标步长（帧数）：从最小候选起，选像素间距 ≥ minPx 的第一档；全不够则最大档。
    /// 短片段时间轴拉得再宽也不该只有一枚 "0s"；长片段时间标不能糊成一片。
    /// </summary>
    public static int LabelStepFrames(double pxPerFrame, int fps, double minPx = 90)
    {
        if (pxPerFrame <= 0 || fps <= 0) return 1;
        foreach (var s in TimeSteps)
            if (s * fps * pxPerFrame >= minPx)
                return Math.Max(1, (int)Math.Round(s * fps));
        return Math.Max(1, (int)Math.Round(TimeSteps[^1] * fps));
    }
}
