namespace FrameCapture.Core;

/// <summary>
/// 「连按两下捕获键 = 开始/结束录制」纯状态机（R60）。与「按住录制」并存：
///
///   Idle ──Down──▶ Hold（引擎随即起录，保住按住即录的零丢帧）
///   Hold ──Up(短)──▶ PendingTap：**引擎继续跑**（零间隙），开点按窗
///   PendingTap ──第二下 Down（窗内）──▶ Toggle（免按住录制，徽标常驻，松手不停）
///   PendingTap ──窗到期──▶ Idle + FinishTap（原「点按=截图」语义，仅延后一窗）
///   Toggle ──Down──▶ StopArmed（开结束窗）──第二下 Down──▶ Idle + FinishToggleStop
///   Toggle ──Up(长)──▶ Idle + FinishHold（录制中按住=停，符合按住直觉）
///   StopArmed ──窗到期──▶ Toggle（录制中单按一下 = 无操作）
///
/// 关闭开关（enabled=false）时 Up(短) 直接 FinishHold，由控制器走旧点按分支——旧语义零漂移。
/// 点按截图因此比原来晚 DblMs（默认 300ms）落地；这是"双击判定避开 tap 语义"的必然代价，
/// 换来免按住录制（用户明确要的能力）。
/// </summary>
public static class ToggleRecord
{
    public enum St { Idle, Hold, PendingTap, Toggle, StopArmed }
    public enum Ev { Down, Up, TapWindowElapsed, StopWindowElapsed }
    public enum Act { None, StartCapture, EnterToggle, ArmStop, FinishTap, FinishHold, FinishToggleStop }

    public readonly struct Step
    {
        public St Next { get; }
        public Act Action { get; }
        public Step(St s, Act a) { Next = s; Action = a; }
    }

    /// <summary>点按窗/结束窗时长(ms)：盖住手速下限，又短到不被感知为"卡"。</summary>
    public const int WindowMs = 300;

    /// <summary>推进一步。holdMs=本次按住时长（-1=无钩子来源，按长按处理）。</summary>
    public static Step Next(St s, Ev e, long holdMs, int tapMs, bool enabled)
    {
        switch (s)
        {
            case St.Idle:
                return e == Ev.Down ? new Step(St.Hold, Act.StartCapture) : new Step(s, Act.None);

            case St.Hold:
                if (e == Ev.Up)
                {
                    bool tap = enabled && tapMs > 0 && holdMs >= 0 && holdMs < tapMs;
                    return tap ? new Step(St.PendingTap, Act.None) : new Step(St.Idle, Act.FinishHold);
                }
                return new Step(s, Act.None);

            case St.PendingTap:
                if (e == Ev.Down) return new Step(St.Toggle, Act.EnterToggle);
                if (e == Ev.TapWindowElapsed) return new Step(St.Idle, Act.FinishTap);
                return new Step(s, Act.None);

            case St.Toggle:
                if (e == Ev.Down) return new Step(St.StopArmed, Act.ArmStop);
                if (e == Ev.Up && tapMs > 0 && holdMs >= tapMs) return new Step(St.Idle, Act.FinishHold);
                return new Step(s, Act.None);

            case St.StopArmed:
                if (e == Ev.Down) return new Step(St.Idle, Act.FinishToggleStop);
                if (e == Ev.StopWindowElapsed) return new Step(St.Toggle, Act.None);
                return new Step(s, Act.None);
        }
        return new Step(s, Act.None);
    }
}
