namespace FrameCapture.Core;

/// <summary>共享行为图的配置、名称与动作链导入。</summary>
public sealed partial class GestureConfig
{
    bool HasRunnableEntry(GestureBindingDto binding) => binding.EntryId.Length > 0
        ? Graph.IsValid(binding.EntryId)
        : binding.Actions.Any(GestureActions.IsValid) || binding.OnComplete?.Any(GestureActions.IsValid) == true;

    /// <summary>行为图的数据版本；零表示使用绑定内动作链。</summary>
    public int GraphRevision { get; set; }
    /// <summary>所有快捷键共用的行为图。</summary>
    public BehaviorGraph Graph { get; set; } = new();

    /// <summary>将尚未连接入口的配置导入一次；空图与已删除的入口保持原值。</summary>
    /// <returns>是否需要保存。</returns>
    public bool EnsureGraph()
    {
        Graph ??= new BehaviorGraph();
        Graph.Flows ??= new(); Graph.Nodes ??= new();
        Graph.Flows.RemoveAll(flow => flow == null); Graph.Nodes.RemoveAll(node => node == null);
        foreach (var flow in Graph.Flows) { flow.Name ??= ""; flow.Steps ??= new(); }
        foreach (var node in Graph.Nodes)
        {
            node.Action ??= ""; node.TargetId ??= ""; node.Branches ??= new();
            foreach (string key in node.Branches.Keys.ToArray()) node.Branches[key] ??= new();
        }
        if (GraphRevision >= 1) return false;
        BehaviorFlow? stock = null;
        foreach (var binding in Bindings)
        {
            if (binding.EntryId.Length > 0) continue;
            bool capture = binding.Id == "default.capture" && binding.Actions.SequenceEqual(new[]
                { GestureActions.Select, GestureActions.GrabStill });
            bool record = binding.Id == "default.record" && binding.Actions.SequenceEqual(new[]
                { GestureActions.Select, GestureActions.RecordToggle, GestureActions.EditResult });
            if ((capture || record) && binding.OnFail.Length == 0)
            {
                if (stock == null)
                {
                    var select = Graph.Add(GestureActions.Select);
                    select.Branches[BehaviorPorts.Still] = new()
                        { Graph.Add(GestureActions.GrabStill).Id, Graph.Add(GestureActions.NotifyToast).Id };
                    var edit = Graph.Add(GestureActions.EditLastVideo);
                    edit.Branches[BehaviorPorts.Saved].Add(Graph.Add(GestureActions.CopyLast).Id);
                    select.Branches[BehaviorPorts.Record] = new() { Graph.Add(GestureActions.RecordStart).Id, edit.Id };
                    stock = new BehaviorFlow { Steps = new() { select.Id } };
                    Graph.Flows.Add(stock);
                }
                binding.EntryId = stock.Id;
                binding.EntryMode = record ? BehaviorPorts.Record : BehaviorPorts.Still;
                continue;
            }
            var flow = new BehaviorFlow { Name = binding.Name, Steps = ImportActions(binding.Actions) };
            Graph.Flows.Add(flow);
            binding.EntryId = flow.Id;
            var failures = ImportActions(binding.OnFail);
            void SetFailure(IEnumerable<string> ids, HashSet<string> seen)
            {
                foreach (string id in ids)
                {
                    if (!seen.Add(id) || Graph.Node(id) is not { } node) continue;
                    foreach (var branch in node.Branches.Values.ToArray()) SetFailure(branch, seen);
                    node.Branches[BehaviorPorts.Failed] = failures.ToList();
                    if (GestureActions.PortsOf(node.Action).Contains(BehaviorPorts.Cancel))
                        node.Branches[BehaviorPorts.Cancel] = failures.ToList();
                    node.ContinueOnFailure = binding.Fallback == "SkipStep";
                }
            }
            SetFailure(flow.Steps, new());
            var firstCapture = binding.Actions.FirstOrDefault(SelectionCapture.IsCapture);
            binding.EntryMode = firstCapture != null && SelectionCapture.IsRecording(firstCapture)
                ? BehaviorPorts.Record : BehaviorPorts.Still;
        }
        GraphRevision = 1;
        return true;
    }

    List<string> ImportActions(IReadOnlyList<string> actions)
    {
        var result = new List<string>();
        for (int i = 0; i < actions.Count; i++)
        {
            string action = GestureActions.Canonical(actions[i]);
            var node = Graph.Add(action);
            result.Add(node.Id);
            if (action is GestureActions.EditLastStill or GestureActions.EditLastVideo or GestureActions.EditResult)
                node.Branches[BehaviorPorts.Saved].Add(Graph.Add(GestureActions.CopyLast).Id);
            if (action != GestureActions.Select) continue;
            int target = SelectionCapture.TargetIndex(actions, i);
            int end = target >= 0 ? target : i;
            var prefix = ImportActions(actions.Skip(i + 1).Take(end - i - 1).ToArray());
            var tail = ImportActions(actions.Skip(end + 1).ToArray());
            string nominal = target >= 0 ? actions[target] : GestureActions.GrabStill;
            node.RecordMode = SelectionCapture.IsRecording(nominal);
            node.Branches[BehaviorPorts.Still] = prefix.Concat(new[]
                { Graph.Add(SelectionCapture.Resolve(nominal, false)).Id }).Concat(tail).ToList();
            node.Branches[BehaviorPorts.Record] = prefix.Concat(new[]
                { Graph.Add(SelectionCapture.Resolve(nominal, true)).Id }).Concat(tail).ToList();
            break;
        }
        return result;
    }

    /// <summary>入口的界面名称，未命名时按首动作取名。</summary>
    /// <param name="flow">行为入口。</param>
    /// <returns>用户名称或动作名称。</returns>
    public string FlowName(BehaviorFlow flow) => flow.Name.Length > 0 ? flow.Name
        : flow.Steps.Count > 0 && Graph.Node(flow.Steps[0]) is { } first
            ? first.Action == GestureActions.Select ? Loc.T(K.Behaviors.CaptureFlow) : GestureActions.LabelOf(first.Action)
        : Loc.T(K.Hotkeys.NewPresetName);

    /// <summary>入口及其事件分支包含的动作。</summary>
    /// <param name="entryId">入口标识。</param>
    /// <param name="initialMode">首个选区的模式覆盖；空值遍历全部出口。</param>
    /// <returns>按图遍历、去重的动作名。</returns>
    public IEnumerable<string> FlowActions(string entryId, string initialMode = "")
    {
        var seen = new HashSet<string>();
        var names = new List<string>();
        void Visit(IEnumerable<string> ids)
        {
            foreach (string id in ids)
            {
                if (!seen.Add(id) || Graph.Node(id) is not { } node) continue;
                names.Add(node.Action);
                if (node.Action == GestureActions.Select && initialMode.Length > 0)
                {
                    if (node.Branches.TryGetValue(initialMode, out var selected)) Visit(selected);
                }
                else foreach (var branch in node.Branches.Values) Visit(branch);
                if (node.Action == GestureActions.CallBehavior && Graph.Flow(node.TargetId) is { } flow) Visit(flow.Steps);
            }
        }
        if (Graph.Flow(entryId) is { } entry) Visit(entry.Steps);
        return names;
    }
}
