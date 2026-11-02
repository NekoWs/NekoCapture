using System.Text.Json;

namespace NekoCapture.Core;

/// <summary>一个可绑定快捷键、可供其他行为调用的入口。</summary>
public sealed class BehaviorFlow
{
    /// <summary>入口的稳定标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>用户名称；空值按入口动作取名。</summary>
    public string Name { get; set; } = "";
    /// <summary>入口内按序执行的节点标识。</summary>
    public List<string> Steps { get; set; } = new();
}

/// <summary>行为图中的动作及其事件出口。节点可被多个列表引用。</summary>
public sealed class BehaviorNode
{
    /// <summary>节点的稳定标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>动作目录中的规范名称。</summary>
    public string Action { get; set; } = GestureActions.GrabStill;
    /// <summary>事件名与对应的有序节点列表。</summary>
    public Dictionary<string, List<string>> Branches { get; set; } = new();
    /// <summary>选区是否显示模式按钮；仅选区动作始终隐藏。</summary>
    public bool ShowModeSwitch { get; set; } = true;
    /// <summary>选区的默认录制档位，入口可覆盖。</summary>
    public bool RecordMode { get; set; }
    /// <summary>失败出口执行后是否继续同级动作。</summary>
    public bool ContinueOnFailure { get; set; }
    /// <summary>取消出口执行后是否继续同级动作。</summary>
    public bool ContinueOnCancel { get; set; }
    /// <summary>调用行为动作的目标入口。</summary>
    public string TargetId { get; set; } = "";
    /// <summary>允许触发调整的参数；空值采用目录默认项及已配置的覆盖。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ExposedParameters { get; set; }
}

/// <summary>共享节点、有序动作列表和入口组成的行为图。</summary>
public sealed class BehaviorGraph
{
    /// <summary>全部可绑定的入口。</summary>
    public List<BehaviorFlow> Flows { get; set; } = new();
    /// <summary>全部动作节点。</summary>
    public List<BehaviorNode> Nodes { get; set; } = new();

    /// <summary>读取节点。</summary>
    /// <param name="id">稳定标识。</param>
    /// <returns>节点，不存在时为空。</returns>
    public BehaviorNode? Node(string id) => Nodes?.FirstOrDefault(n => n != null && n.Id == id);

    /// <summary>读取入口。</summary>
    /// <param name="id">稳定标识。</param>
    /// <returns>入口，不存在时为空。</returns>
    public BehaviorFlow? Flow(string id) => Flows?.FirstOrDefault(f => f != null && f.Id == id);

    /// <summary>创建并登记动作节点。</summary>
    /// <param name="action">规范动作名。</param>
    /// <returns>新节点。</returns>
    public BehaviorNode Add(string action)
    {
        var node = new BehaviorNode { Action = GestureActions.Canonical(action) };
        foreach (string port in GestureActions.PortsOf(node.Action)) node.Branches[port] = new();
        Nodes.Add(node);
        return node;
    }

    /// <summary>制作运行快照，设置编辑不改变已启动的行为。</summary>
    /// <returns>独立的数据副本。</returns>
    public BehaviorGraph Snapshot() => JsonSerializer.Deserialize<BehaviorGraph>(JsonSerializer.Serialize(this))!;

    /// <summary>校验引用、动作、事件出口、重名标识与自动执行回路。</summary>
    /// <param name="entryId">只校验一个入口时提供标识；空值校验全部入口。</param>
    /// <returns>所选入口是否可执行。</returns>
    public bool IsValid(string? entryId = null)
    {
        if (Nodes == null || Flows == null || Nodes.Count > 4096 || Flows.Count > 512
            || Nodes.Any(n => n == null || string.IsNullOrWhiteSpace(n.Id) || n.Branches == null)
            || Flows.Any(f => f == null || string.IsNullOrWhiteSpace(f.Id) || f.Steps == null)
            || Nodes.Select(n => n.Id).Distinct().Count() != Nodes.Count
            || Flows.Select(f => f.Id).Distinct().Count() != Flows.Count) return false;
        var byId = Nodes.ToDictionary(n => n.Id);
        var marks = new Dictionary<string, int>();
        var heights = new Dictionary<string, int>();
        bool Walk(string id, int depth)
        {
            if (depth > 32 || !byId.TryGetValue(id, out var node)) return false;
            if (marks.TryGetValue(id, out int mark)) return mark == 2 && depth + heights[id] <= 32;
            if (!GestureActions.IsValid(node.Action)) return false;
            marks[id] = 1;
            int height = 0;
            var ports = GestureActions.PortsOf(node.Action);
            foreach (var branch in node.Branches)
            {
                if (!ports.Contains(branch.Key) || branch.Value == null || branch.Value.Count > 2048) return false;
                foreach (string child in branch.Value)
                {
                    if (!Walk(child, depth + 1)) return false;
                    height = Math.Max(height, heights[child] + 1);
                }
            }
            if (node.Action == GestureActions.CallBehavior)
            {
                var flow = Flow(node.TargetId);
                if (flow == null) return false;
                foreach (string child in flow.Steps)
                {
                    if (!Walk(child, depth + 1)) return false;
                    height = Math.Max(height, heights[child] + 1);
                }
            }
            marks[id] = 2;
            heights[id] = height;
            return true;
        }
        if (entryId != null && Flow(entryId) == null) return false;
        foreach (var flow in Flows.Where(f => entryId == null || f.Id == entryId))
        {
            if (flow.Steps.Count > 2048) return false;
            foreach (string id in flow.Steps) if (!Walk(id, 0)) return false;
        }
        return true;
    }

    /// <summary>删除无人引用的节点，保留所有入口共享的节点。</summary>
    public void Collect()
    {
        var keep = new HashSet<string>();
        void Visit(string id)
        {
            if (!keep.Add(id) || Node(id) is not { } node) return;
            foreach (var branch in node.Branches.Values) foreach (string child in branch) Visit(child);
        }
        foreach (var flow in Flows) foreach (string id in flow.Steps) Visit(id);
        Nodes.RemoveAll(n => !keep.Contains(n.Id));
    }
}
