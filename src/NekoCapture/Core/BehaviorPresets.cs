namespace NekoCapture.Core;

/// <summary>预设方案包含的触发与共享行为图。</summary>
public sealed class BehaviorScheme
{
    /// <summary>方案的有序触发。</summary>
    public List<GestureBindingDto> Bindings { get; set; } = new();
    /// <summary>方案的完整行为图。</summary>
    public BehaviorGraph Graph { get; set; } = new();
}

/// <summary>可重复创建且标识稳定的捕获方案。</summary>
public static class BehaviorPresets
{
    /// <summary>用户自定义方案。</summary>
    public const string Custom = "custom";
    /// <summary>截图与录制使用独立快捷键。</summary>
    public const string Dual = "dual";
    /// <summary>先选择区域，另一键轻点截图、按住录制、松开停止。</summary>
    public const string Separate = "separate";
    /// <summary>单快捷键进入选区，由模式按钮选择分支。</summary>
    public const string Single = "single";
    /// <summary>界面中的方案顺序。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Dual, Separate, Single, Custom });

    /// <summary>方案名称。</summary>
    /// <param name="id">方案标识。</param>
    /// <returns>对应的界面文案。</returns>
    public static Str Label(string id) => id switch
    {
        Dual => K.Hotkeys.PresetDual, Separate => K.Hotkeys.PresetSeparate,
        Single => K.Hotkeys.PresetSingle, _ => K.Hotkeys.PresetCustom,
    };

    /// <summary>预设绑定的显示名；用户名称由调用方优先处理。</summary>
    /// <param name="id">绑定标识。</param>
    /// <returns>预设绑定文案，其他绑定使用新配置名称。</returns>
    public static Str BindingLabel(string id) => id switch
    {
        "preset.dual.still" => K.Common.Capture,
        "preset.dual.record" => K.Common.Recording,
        "preset.separate.select" => K.Behaviors.SelectOnly,
        "preset.separate.tap" => K.Hotkeys.PresetTapCapture,
        "preset.separate.hold" => K.Hotkeys.PresetHoldRecord,
        "preset.separate.release" => K.Hotkeys.PresetReleaseStop,
        "preset.single.capture" => K.Behaviors.CaptureFlow,
        _ => K.Hotkeys.NewPresetName,
    };

    /// <summary>以已存区域开始的预设入口名称。</summary>
    /// <param name="id">行为入口标识。</param>
    /// <returns>入口名称，不匹配时为空。</returns>
    public static Str FlowLabel(string id) => id switch
    {
        "preset.separate.still" => K.Common.Capture,
        "preset.separate.record" => K.Common.Recording,
        _ => Str.Empty,
    };

    /// <summary>创建不与其他方案共享可变数据的内置方案。</summary>
    /// <param name="id">内置方案标识。</param>
    /// <returns>独立的快捷键与行为图。</returns>
    public static BehaviorScheme Create(string id)
    {
        if (id is not (Dual or Separate or Single)) throw new ArgumentOutOfRangeException(nameof(id));
        var scheme = new BehaviorScheme();
        var graph = scheme.Graph;
        int serial = 0;
        BehaviorNode Node(string action)
        {
            var node = graph.Add(action); node.Id = $"preset.{id}.node.{++serial}"; return node;
        }
        BehaviorFlow Flow(string name, params BehaviorNode[] steps)
        {
            var flow = new BehaviorFlow { Id = $"preset.{id}.{name}", Steps = steps.Select(node => node.Id).ToList() };
            graph.Flows.Add(flow); return flow;
        }
        void Bind(string name, string chord, string trigger, BehaviorFlow flow, string mode = "")
        {
            var binding = new GestureBindingDto { Id = $"preset.{id}.{name}", Chord = chord, Trigger = trigger, EntryId = flow.Id };
            if (mode.Length > 0 && graph.Node(flow.Steps[0]) is { } first)
                binding.Overrides[first.Id] = new() { [nameof(BehaviorNode.RecordMode)] = System.Text.Json.JsonSerializer.SerializeToElement(mode == BehaviorPorts.Record) };
            scheme.Bindings.Add(binding);
        }
        BehaviorNode EditVideo()
        {
            var edit = Node(GestureActions.EditLastVideo);
            edit.Branches[BehaviorPorts.Saved].Add(Node(GestureActions.CopyLast).Id); return edit;
        }
        BehaviorNode RecordVideo()
        {
            var record = Node(GestureActions.RecordStart);
            record.Branches[BehaviorPorts.Done].Add(EditVideo().Id);
            return record;
        }
        if (id == Separate)
        {
            var select = Node(GestureActions.SelectOnly); select.ShowModeSwitch = false;
            Bind("select", GestureConfig.DefaultCaptureKey, "Chord", Flow("select", select));
            Bind("tap", GestureConfig.DefaultRecordKey, "Tap", Flow("still", Node(GestureActions.RequireStored),
                Node(GestureActions.GrabStill), Node(GestureActions.NotifyToast)));
            Bind("hold", GestureConfig.DefaultRecordKey, "Hold", Flow("record", Node(GestureActions.RequireStored),
                RecordVideo()));
            Bind("release", GestureConfig.DefaultRecordKey, "HoldRelease", Flow("stop", Node(GestureActions.RecordStop)));
        }
        else
        {
            var select = Node(GestureActions.Select);
            select.Branches[BehaviorPorts.Still] = new() { Node(GestureActions.GrabStill).Id, Node(GestureActions.NotifyToast).Id };
            select.Branches[BehaviorPorts.Record] = new() { RecordVideo().Id };
            var flow = Flow("capture", select);
            Bind(id == Dual ? "still" : "capture", GestureConfig.DefaultCaptureKey, "Chord", flow,
                id == Dual ? BehaviorPorts.Still : "");
            if (id == Dual) Bind("record", GestureConfig.DefaultRecordKey, "Chord", flow, BehaviorPorts.Record);
        }
        TrayDefaults.Seed(scheme.Bindings, scheme.Graph);
        return scheme;
    }
}
