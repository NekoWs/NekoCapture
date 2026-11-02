namespace NekoCapture.Core;

/// <summary>动作的简短执行位置，避免把完整分支路径作为设置名称。</summary>
public static partial class BehaviorParameters
{
    /// <summary>读取入口内动作所在的阶段。</summary>
    /// <param name="graph">运行图。</param>
    /// <param name="entryId">入口标识。</param>
    /// <returns>动作标识与本地化执行位置。</returns>
    public static IReadOnlyDictionary<string, string> Locations(BehaviorGraph graph, string entryId)
    {
        var locations = new Dictionary<string, string>();
        string After(BehaviorNode node) => Loc.F(K.Triggers.AfterAction, GestureActions.LabelOf(node.Action));
        string Branch(BehaviorNode node, string port) => port switch
        {
            BehaviorPorts.Still => Loc.T(K.Triggers.AfterStillChoice),
            BehaviorPorts.Record => Loc.T(K.Triggers.AfterRecordChoice),
            BehaviorPorts.Done when node.Action is GestureActions.RecordStart or GestureActions.RecordToggle => Loc.T(K.Triggers.AfterRecording),
            BehaviorPorts.Done => After(node),
            _ => Loc.F(K.Common.LabelWithContext, GestureActions.LabelOf(node.Action), GestureActions.PortLabel(node.Action, port)),
        };
        void Visit(IEnumerable<string> ids, string location, int depth)
        {
            if (depth > 32) return;
            foreach (string id in ids)
            {
                if (locations.ContainsKey(id) || graph.Node(id) is not { } node) continue;
                locations[id] = location;
                if (node.Action == GestureActions.CallBehavior && graph.Flow(node.TargetId) is { } target)
                {
                    string name = target.Name.Length > 0 ? target.Name : target.Steps.FirstOrDefault() is { } first
                        && graph.Node(first) is { } start ? GestureActions.LabelOf(start.Action) : Loc.T(K.Behaviors.Title);
                    Visit(target.Steps, Loc.F(K.Triggers.InBehavior, name), depth + 1);
                }
                foreach (var branch in node.Branches) Visit(branch.Value, Branch(node, branch.Key), depth + 1);
                location = After(node);
            }
        }
        if (graph.Flow(entryId) is { } flow) Visit(flow.Steps, Loc.T(K.Triggers.AtStart), 0);
        return locations;
    }
}
