using NekoCapture.Core;

namespace NekoCapture.Services;

/// <summary>按来源匹配托盘触发并执行独立的行为快照。</summary>
internal static class TriggerExecution
{
    /// <summary>执行已启用的托盘触发，总开关关闭时不执行。</summary>
    /// <param name="host">运行宿主。</param>
    /// <param name="id">触发标识。</param>
    internal static void Run(AppHost host, string id)
    {
        var config = host.Settings.Current.Gestures;
        if (!config.Enabled) return;
        var binding = config.Bindings.FirstOrDefault(b => b.Id == id && b.Enabled && b.Source != TriggerSources.Keyboard);
        if (binding == null || !TriggerSources.All.Contains(binding.Source)) return;
        var graph = config.BindingGraph(binding);
        host.Controller.BeginBehavior(graph, binding.EntryId, binding.Id);
    }

    /// <summary>读取单击与双击的可执行配置，顺序与优先级共同决定首项。</summary>
    /// <param name="config">当前触发配置。</param>
    /// <returns>单击、双击标识及相同行为标记。</returns>
    internal static (string Single, string Double, bool Equivalent) TrayClicks(GestureConfig config)
    {
        GestureBindingDto? First(string source) => config.Enabled ? config.Bindings.OrderBy(b => b.Priority)
            .FirstOrDefault(b => b.Enabled && b.Source == source && config.Graph.IsValid(b.EntryId) && config.Graph.Flow(b.EntryId) != null) : null;
        var single = First(TriggerSources.TrayClick); var twice = First(TriggerSources.TrayDoubleClick);
        bool same = single != null && twice != null && single.EntryId == twice.EntryId;
        if (same)
        foreach (string id in single!.Overrides.Keys.Concat(twice!.Overrides.Keys).Distinct())
        {
            if (config.Graph.Node(id) is not { } node) continue;
            foreach (var parameter in BehaviorParameters.For(node))
            {
                System.Text.Json.JsonElement Read(GestureBindingDto binding)
                {
                    if (binding.Overrides.TryGetValue(id, out var values) && values.TryGetValue(parameter.Id, out var value)
                        && (parameter.Kind == BehaviorParameterKind.Behavior ? value.ValueKind == System.Text.Json.JsonValueKind.String
                            : value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)) return value;
                    return BehaviorParameters.Read(node, parameter.Id);
                }
                if (Read(single).GetRawText() != Read(twice).GetRawText()) { same = false; break; }
            }
            if (!same) break;
        }
        return (single?.Id ?? "", twice?.Id ?? "", same);
    }
}
