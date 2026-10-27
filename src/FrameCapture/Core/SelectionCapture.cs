namespace FrameCapture.Core;

/// <summary>选区开关与其后捕获步骤的对应关系。</summary>
public static class SelectionCapture
{
    /// <summary>定位本次选区控制的第一个捕获步骤，不跨越下一次选区。</summary>
    /// <param name="chain">动作链。</param>
    /// <param name="selectionIndex">选区步骤下标。</param>
    /// <returns>捕获步骤下标；没有显式捕获时为 -1。</returns>
    public static int TargetIndex(IReadOnlyList<string> chain, int selectionIndex)
    {
        for (int i = selectionIndex + 1; i < chain.Count; i++)
        {
            string name = GestureActions.Canonical(chain[i]);
            if (name == GestureActions.Select) break;
            if (IsCapture(name)) return i;
        }
        return -1;
    }

    /// <summary>动作是否发起一次捕获。</summary>
    /// <param name="name">动作名。</param>
    /// <returns>截图、录制、滚动与延时捕获为真；停止录制为假。</returns>
    public static bool IsCapture(string name) => GestureActions.InfoOf(name).Class == VerbClass.Product
        && name != GestureActions.RecordStop;

    /// <summary>捕获步骤是否默认录制。</summary>
    /// <param name="name">动作名。</param>
    /// <returns>开始录制与录制切换为真。</returns>
    public static bool IsRecording(string name) => name is GestureActions.RecordStart or GestureActions.RecordToggle;

    /// <summary>根据选区档位决定当前捕获步骤；同档保留具体捕获方式。</summary>
    /// <param name="name">配置的捕获动作。</param>
    /// <param name="record">选区开关是否选择录制。</param>
    /// <returns>同档返回配置动作；切换档位返回开始录制或区域截图。</returns>
    public static string Resolve(string name, bool record)
        => IsRecording(name) == record ? name : record ? GestureActions.RecordStart : GestureActions.GrabStill;

    /// <summary>请求是否结束当前录制。</summary>
    /// <param name="request">触发的动作链。</param>
    /// <param name="active">正在等待录制完成的主链。</param>
    /// <returns>再次触发主链，或请求含录制切换与停止时为真。</returns>
    public static bool StopsRecording(IReadOnlyList<string> request, IReadOnlyList<string> active)
        => request.SequenceEqual(active) || request.Any(n => n is GestureActions.RecordToggle or GestureActions.RecordStop);

    /// <summary>步骤是否由前面的选区开关控制。</summary>
    /// <param name="chain">动作链。</param>
    /// <param name="index">步骤下标。</param>
    /// <returns>本步骤是最近一次选区之后的第一个捕获时为真。</returns>
    public static bool IsSwitchTarget(IReadOnlyList<string> chain, int index)
    {
        for (int i = index - 1; i >= 0; i--)
            if (GestureActions.Canonical(chain[i]) == GestureActions.Select) return TargetIndex(chain, i) == index;
        return false;
    }
}
