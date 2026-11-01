namespace NekoCapture.Core;

/// <summary>动作生命周期中的稳定事件名。</summary>
public static class BehaviorPorts
{
    /// <summary>动作完成。</summary>
    public const string Done = "done";
    /// <summary>录制成功开始。</summary>
    public const string Started = "started";
    /// <summary>确认截图模式。</summary>
    public const string Still = "still";
    /// <summary>确认录制模式。</summary>
    public const string Record = "record";
    /// <summary>交互取消。</summary>
    public const string Cancel = "cancel";
    /// <summary>动作失败。</summary>
    public const string Failed = "failed";
    /// <summary>编辑结果已保存或确认输出。</summary>
    public const string Saved = "saved";
    /// <summary>编辑器未输出便关闭。</summary>
    public const string Closed = "closed";
}

/// <summary>按事件选择子列表，等待子列表完成后继续同级列表。</summary>
public static class BehaviorRunner
{
    /// <summary>执行一个入口的快照；无效图与超预算图不执行额外步骤。</summary>
    /// <param name="graph">运行快照。</param>
    /// <param name="entryId">入口标识。</param>
    /// <param name="execute">执行节点并返回事件；调用行为由运行器解释。</param>
    /// <param name="cancelled">宿主是否结束。</param>
    /// <returns>入口是否完成。</returns>
    public static Task<bool> RunAsync(BehaviorGraph graph, string entryId,
        Func<BehaviorNode, Task<string>> execute, Func<bool>? cancelled = null)
        => RunAsync(graph, entryId, (node, _) => execute(node), cancelled);

    /// <summary>执行入口，允许录制节点在等待停止期间派发开始事件。</summary>
    /// <param name="graph">运行快照。</param>
    /// <param name="entryId">入口标识。</param>
    /// <param name="execute">执行节点；事件回调等待对应子列表完成。</param>
    /// <param name="cancelled">宿主是否结束。</param>
    /// <returns>入口是否完成。</returns>
    public static async Task<bool> RunAsync(BehaviorGraph graph, string entryId,
        Func<BehaviorNode, Func<string, Task<bool>>, Task<string>> execute, Func<bool>? cancelled = null)
    {
        if (!graph.IsValid(entryId) || graph.Flow(entryId) is not { } entry) return false;
        int budget = 2048;
        async Task<bool> Run(IReadOnlyList<string> steps)
        {
            foreach (string id in steps)
            {
                if (cancelled?.Invoke() == true || --budget < 0) return false;
                var node = graph.Node(id)!;
                string port;
                try
                {
                    port = node.Action == GestureActions.CallBehavior
                        ? await Run(graph.Flow(node.TargetId)!.Steps) ? BehaviorPorts.Done : BehaviorPorts.Failed
                        : await execute(node, port => port == BehaviorPorts.Started
                            && GestureActions.PortsOf(node.Action).Contains(port)
                            ? node.Branches.TryGetValue(port, out var active) ? Run(active) : Task.FromResult(true)
                            : Task.FromResult(false));
                }
                catch (OperationCanceledException) { port = BehaviorPorts.Cancel; }
                catch (Exception ex) { Trace.Log($"behavior {node.Action}: {ex.Message}"); port = BehaviorPorts.Failed; }
                if (!GestureActions.PortsOf(node.Action).Contains(port)) return false;
                if (node.Branches.TryGetValue(port, out var children) && !await Run(children)) return false;
                if (port == BehaviorPorts.Failed && !node.ContinueOnFailure) return false;
                if (port == BehaviorPorts.Cancel && !node.ContinueOnCancel) return false;
            }
            return true;
        }
        return await Run(entry.Steps);
    }
}
