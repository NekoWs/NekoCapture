namespace NekoCapture.Core;

/// <summary>动作对触发开放的设置及其覆盖范围。</summary>
public static partial class BehaviorParameters
{
    /// <summary>按目录顺序读取允许触发调整的参数，保留已配置覆盖的可见性。</summary>
    /// <param name="node">共享动作节点。</param>
    /// <param name="bindings">同一行为图的触发配置。</param>
    /// <returns>此动作开放的设置。</returns>
    public static IEnumerable<BehaviorParameter> Exposed(BehaviorNode node, IEnumerable<GestureBindingDto> bindings)
    {
        var stored = bindings.Where(binding => binding.Overrides.ContainsKey(node.Id))
            .SelectMany(binding => binding.Overrides[node.Id].Keys).ToHashSet();
        return For(node).Where(parameter => node.ExposedParameters is { } exposed
            ? exposed.Contains(parameter.Id) : parameter.ExposedByDefault || stored.Contains(parameter.Id));
    }

    /// <summary>设置动作开放项；收起权限时清理各触发对应的覆盖。</summary>
    /// <param name="config">当前行为与触发配置。</param>
    /// <param name="node">共享动作节点。</param>
    /// <param name="id">目录内的参数标识。</param>
    /// <param name="exposed">是否允许触发调整。</param>
    public static void SetExposure(GestureConfig config, BehaviorNode node, string id, bool exposed)
    {
        if (!For(node).Any(parameter => parameter.Id == id)) return;
        node.ExposedParameters ??= Exposed(node, config.Bindings).Select(parameter => parameter.Id).ToList();
        if (exposed)
        {
            if (!node.ExposedParameters.Contains(id)) node.ExposedParameters.Add(id);
            return;
        }
        node.ExposedParameters.RemoveAll(value => value == id);
        foreach (var binding in config.Bindings)
            if (binding.Overrides.TryGetValue(node.Id, out var values))
            {
                values.Remove(id);
                if (values.Count == 0) binding.Overrides.Remove(node.Id);
            }
    }
}
