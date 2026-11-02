using System.Text.Json;

namespace NekoCapture.Core;

/// <summary>触发来源、参数覆盖与配置导入。</summary>
public sealed partial class GestureConfig
{
    /// <summary>新建具有独立空动作链的托盘菜单项。</summary>
    /// <param name="name">菜单名称。</param>
    /// <returns>已加入配置的菜单触发；其行为可继续编辑。</returns>
    public GestureBindingDto AddTrayMenu(string name)
    {
        var flow = new BehaviorFlow { Name = name };
        Graph.Flows.Add(flow);
        var binding = new GestureBindingDto { Id = "user." + Guid.NewGuid().ToString("N"), Name = name,
            Source = TriggerSources.TrayMenu, EntryId = flow.Id };
        Bindings.Add(binding); return binding;
    }

    /// <summary>触发目录的数据版本，用于一次性加入托盘入口。</summary>
    public int TriggerRevision { get; set; }

    /// <summary>制作触发调用的行为快照，应用每个动作的局部参数。</summary>
    /// <param name="binding">触发配置。</param>
    /// <returns>独立的运行图。</returns>
    public BehaviorGraph BindingGraph(GestureBindingDto binding)
    {
        var graph = Graph.Snapshot();
        foreach (var pair in binding.Overrides)
            if (pair.Value != null && graph.Node(pair.Key) is { } node) BehaviorParameters.Apply(node, pair.Value);
        return graph;
    }

    private bool EnsureTriggers()
    {
        bool changed = false;
        bool Convert(List<GestureBindingDto> bindings, BehaviorGraph graph)
        {
            bool converted = false;
            foreach (var binding in bindings)
            {
                binding.Source ??= TriggerSources.Keyboard; binding.Overrides ??= new();
                if (!string.IsNullOrEmpty(binding.EntryMode) && BehaviorParameters.Reachable(graph, binding.EntryId)
                    .FirstOrDefault(n => n.Action == GestureActions.Select) is { } node)
                {
                    if (!binding.Overrides.TryGetValue(node.Id, out var values)) binding.Overrides[node.Id] = values = new();
                    values.TryAdd(nameof(BehaviorNode.RecordMode), JsonSerializer.SerializeToElement(binding.EntryMode == BehaviorPorts.Record));
                }
                converted |= binding.EntryMode != null;
                binding.EntryMode = null;
                foreach (string id in binding.Overrides.Keys.Where(id => graph.Node(id) == null || binding.Overrides[id] == null).ToArray())
                    binding.Overrides.Remove(id);
            }
            return converted;
        }
        changed |= Convert(Bindings, Graph);
        if (CustomScheme is { } custom) changed |= Convert(custom.Bindings, custom.Graph);
        if (TriggerRevision < 1)
        {
            TrayDefaults.Seed(Bindings, Graph);
            if (CustomScheme is { } saved) TrayDefaults.Seed(saved.Bindings, saved.Graph);
            TriggerRevision = 1; changed = true;
        }
        return changed;
    }
}
