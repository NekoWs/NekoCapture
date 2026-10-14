namespace GestureEditor;

/// <summary>
/// 本库自带的动效时长令牌。数值逐项对齐宿主 <c>FrameCapture.Core.Fluent.Motion</c>；
/// 本库只依赖 GestureCore（宿主反向引用它），因此不能直接引用宿主令牌类——
/// 改这里的数值时同步改宿主，两侧不一致会让手势编辑器的手感与全站脱节。
/// </summary>
public static class Motion
{
    public const int EnterMs = 190;    // 窗口入场
    public const int ExitMs = 150;     // 退场（比入场快 = 更干脆）
    public const int HoverMs = 110;    // hover 反馈
    public const int StateMs = 150;    // 状态切换（开关/选中）
    public const int PressMs = 70;     // 按压下沉：必须短于 hover，按压要跟手
    public const int QuickMs = 120;    // 即时短程过渡：按了就要看到的淡入/回位
}
