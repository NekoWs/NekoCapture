using System.Text.Json;

namespace NekoCapture.Core;

/// <summary>内置方案切换、自定义快照与编辑识别。</summary>
public sealed partial class GestureConfig
{
    /// <summary>当前选中的行为方案标识。</summary>
    public string ActivePreset { get; set; } = BehaviorPresets.Custom;
    /// <summary>独立保存的用户方案，包含快捷键和行为图。</summary>
    public BehaviorScheme? CustomScheme { get; set; }

    string SchemeJson() => JsonSerializer.Serialize(new BehaviorScheme { Bindings = Bindings, Graph = Graph });

    /// <summary>识别对内置方案的修改，并同步自定义快照；总开关不参与比较。</summary>
    public void NormalizePreset()
    {
        string current = SchemeJson();
        if (!BehaviorPresets.All.Contains(ActivePreset)) ActivePreset = BehaviorPresets.Custom;
        if (ActivePreset != BehaviorPresets.Custom
            && current != JsonSerializer.Serialize(BehaviorPresets.Create(ActivePreset)))
            ActivePreset = BehaviorPresets.Custom;
        if (CustomScheme == null || ActivePreset == BehaviorPresets.Custom && current != JsonSerializer.Serialize(CustomScheme))
            CustomScheme = JsonSerializer.Deserialize<BehaviorScheme>(current)!;
    }

    /// <summary>替换快捷键与行为图，保留总开关和界面键位；预设间切换保留自定义快照。</summary>
    /// <param name="id">内置方案或自定义标识。</param>
    /// <returns>是否切换成功；未知标识或已选中时为 false。</returns>
    public bool SelectPreset(string id)
    {
        if (!BehaviorPresets.All.Contains(id)) return false;
        NormalizePreset();
        if (id == ActivePreset) return false;
        var scheme = id == BehaviorPresets.Custom
            ? JsonSerializer.Deserialize<BehaviorScheme>(JsonSerializer.Serialize(CustomScheme))!
            : BehaviorPresets.Create(id);
        Bindings = scheme.Bindings; Graph = scheme.Graph;
        ActivePreset = id; Seeded = true;
        ChainRevision = CurrentChainRevision; GraphRevision = 1;
        return true;
    }
}
