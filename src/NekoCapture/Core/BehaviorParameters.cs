using System.Text.Json;

namespace NekoCapture.Core;

/// <summary>动作参数的编辑类型。</summary>
public enum BehaviorParameterKind
{
    /// <summary>开关。</summary>
    Toggle,
    /// <summary>截图或录制模式。</summary>
    CaptureMode,
    /// <summary>共享行为入口。</summary>
    Behavior,
}

/// <summary>动作参数的名称与编辑类型。</summary>
/// <param name="Id">节点属性的稳定标识。</param>
/// <param name="Label">界面名称。</param>
/// <param name="Kind">编辑类型。</param>
/// <param name="Hint">此设置的具体作用。</param>
/// <param name="ExposedByDefault">是否默认允许触发调整。</param>
public sealed record BehaviorParameter(string Id, Str Label, BehaviorParameterKind Kind, Str Hint, bool ExposedByDefault = false);

/// <summary>可覆盖的动作参数目录；覆盖只作用于运行快照，不改变共享行为。</summary>
public static partial class BehaviorParameters
{
    /// <summary>读取动作支持的参数。</summary>
    /// <param name="node">动作节点。</param>
    /// <returns>参数的稳定顺序。</returns>
    public static IEnumerable<BehaviorParameter> For(BehaviorNode node)
    {
        if (node.Action == GestureActions.Select)
        {
            yield return new(nameof(BehaviorNode.RecordMode), K.Triggers.CaptureMode, BehaviorParameterKind.CaptureMode, K.Triggers.CaptureModeHint, true);
            yield return new(nameof(BehaviorNode.ShowModeSwitch), K.Behaviors.ShowSwitch, BehaviorParameterKind.Toggle, K.Triggers.ModeSwitchHint, true);
        }
        if (node.Action == GestureActions.CallBehavior)
            yield return new(nameof(BehaviorNode.TargetId), K.SettingsNavigation.Execute, BehaviorParameterKind.Behavior, K.Triggers.TargetHint, true);
        yield return new(nameof(BehaviorNode.ContinueOnFailure), K.Triggers.ContinueFailure, BehaviorParameterKind.Toggle, K.Triggers.FailureHint);
        if (GestureActions.PortsOf(node.Action).Contains(BehaviorPorts.Cancel))
            yield return new(nameof(BehaviorNode.ContinueOnCancel), K.Triggers.ContinueCancel, BehaviorParameterKind.Toggle, K.Triggers.CancelHint);
    }

    /// <summary>读取参数的 JSON 值。</summary>
    /// <param name="node">动作节点。</param>
    /// <param name="id">参数标识。</param>
    /// <returns>独立的参数值。</returns>
    public static JsonElement Read(BehaviorNode node, string id) => id switch
    {
        nameof(BehaviorNode.RecordMode) => JsonSerializer.SerializeToElement(node.RecordMode),
        nameof(BehaviorNode.ShowModeSwitch) => JsonSerializer.SerializeToElement(node.ShowModeSwitch),
        nameof(BehaviorNode.ContinueOnFailure) => JsonSerializer.SerializeToElement(node.ContinueOnFailure),
        nameof(BehaviorNode.ContinueOnCancel) => JsonSerializer.SerializeToElement(node.ContinueOnCancel),
        nameof(BehaviorNode.TargetId) => JsonSerializer.SerializeToElement(node.TargetId),
        _ => throw new ArgumentException("Unknown behavior parameter", nameof(id)),
    };

    /// <summary>应用节点支持且类型正确的参数；未知字段保持原值。</summary>
    /// <param name="node">运行副本中的动作节点。</param>
    /// <param name="values">参数覆盖。</param>
    public static void Apply(BehaviorNode node, IReadOnlyDictionary<string, JsonElement> values)
    {
        foreach (var parameter in For(node))
        {
            if (node.ExposedParameters is { } exposed && !exposed.Contains(parameter.Id)) continue;
            if (!values.TryGetValue(parameter.Id, out var value)) continue;
            if (parameter.Kind == BehaviorParameterKind.Behavior)
            { if (value.ValueKind == JsonValueKind.String) node.TargetId = value.GetString() ?? ""; continue; }
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
            bool enabled = value.GetBoolean();
            switch (parameter.Id)
            {
                case nameof(BehaviorNode.RecordMode): node.RecordMode = enabled; break;
                case nameof(BehaviorNode.ShowModeSwitch): node.ShowModeSwitch = enabled; break;
                case nameof(BehaviorNode.ContinueOnFailure): node.ContinueOnFailure = enabled; break;
                case nameof(BehaviorNode.ContinueOnCancel): node.ContinueOnCancel = enabled; break;
            }
        }
    }

    /// <summary>遍历入口、调用与分支可达的动作，循环引用只访问一次。</summary>
    /// <param name="graph">共享行为图。</param>
    /// <param name="entryId">入口标识。</param>
    /// <returns>按执行结构排列的动作。</returns>
    public static IEnumerable<BehaviorNode> Reachable(BehaviorGraph graph, string entryId)
    {
        var nodes = new List<BehaviorNode>(); var seen = new HashSet<string>();
        void Visit(IEnumerable<string> ids, int depth = 0)
        {
            if (depth > 32) return;
            foreach (string id in ids)
            {
                if (!seen.Add(id) || graph.Node(id) is not { } node) continue;
                nodes.Add(node);
                if (node.Action == GestureActions.CallBehavior && graph.Flow(node.TargetId) is { } target) Visit(target.Steps, depth + 1);
                foreach (var branch in node.Branches.Values) Visit(branch, depth + 1);
            }
        }
        if (graph.Flow(entryId) is { } flow) Visit(flow.Steps);
        return nodes;
    }

}
