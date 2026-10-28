using System.Text.Json;
using FrameCapture.Core;
using GestureCore;

static partial class SelfTest
{
    static void BehaviorPresetPolicy()
    {
        try
        {
            foreach (string id in BehaviorPresets.All.Where(id => id != BehaviorPresets.Custom))
            {
                var scheme = BehaviorPresets.Create(id);
                Chk($"preset-{id}-valid-entries", scheme.Graph.IsValid() && scheme.Bindings.All(binding =>
                    binding.ToBinding() != null && scheme.Graph.Flow(binding.EntryId) != null));
                Chk($"preset-{id}-stable-definition", JsonSerializer.Serialize(scheme) == JsonSerializer.Serialize(BehaviorPresets.Create(id)));
            }
            var dual = BehaviorPresets.Create(BehaviorPresets.Dual);
            var root = dual.Graph.Node(dual.Graph.Flows.Single().Steps.Single())!;
            Chk("preset-dual-shares-selection-and-mode-exits", dual.Bindings.Count == 2
                && dual.Bindings[0].EntryId == dual.Bindings[1].EntryId && root.ShowModeSwitch
                && dual.Bindings[0].EntryMode == BehaviorPorts.Still && dual.Bindings[1].EntryMode == BehaviorPorts.Record);
            var separate = BehaviorPresets.Create(BehaviorPresets.Separate);
            Chk("preset-separate-stores-without-capture", separate.Graph.Node(separate.Graph.Flows[0].Steps.Single()) is
                { Action: GestureActions.SelectOnly, ShowModeSwitch: false } select && select.Branches.Values.All(branch => branch.Count == 0));
            Chk("preset-separate-capture-requires-region", separate.Graph.Flows.Skip(1).Take(2)
                .All(flow => separate.Graph.Node(flow.Steps[0])?.Action == GestureActions.RequireStored));
            var host = new ChainHostProbe();
            var engine = new GestureEngine(host);
            engine.SetBindings(separate.Bindings.Select(binding => binding.ToBinding()!));
            var mods = ModifierKeys.Control | ModifierKeys.Alt;
            engine.OnKeyDown(0x52, mods, 0); engine.OnKeyUp(0x52, mods, 100);
            Chk("preset-separate-tap-enters-still", host.Binding?.Id == "preset.separate.tap");
            engine.OnKeyDown(0x52, mods, 1000); engine.Tick(1510);
            Chk("preset-separate-hold-enters-record", host.Binding?.Id == "preset.separate.hold");
            engine.OnKeyUp(0x52, mods, 1600);
            Chk("preset-separate-release-enters-stop", host.Binding?.Id == "preset.separate.release");
            var single = BehaviorPresets.Create(BehaviorPresets.Single);
            Chk("preset-single-keeps-both-mode-branches", single.Bindings.Count == 1
                && single.Graph.Node(single.Graph.Flows.Single().Steps.Single()) is { ShowModeSwitch: true } node
                && node.Branches[BehaviorPorts.Still].Count > 0 && node.Branches[BehaviorPorts.Record].Count > 0);
            var settings = new AppSettings(); settings.Normalize();
            var cfg = settings.Gestures;
            cfg.Bindings[0].Chord = "F9"; settings.Normalize();
            string original = JsonSerializer.Serialize(cfg.CustomScheme);
            cfg.Enabled = false; cfg.SelectPreset(BehaviorPresets.Dual); settings.Normalize();
            Chk("preset-application-preserves-master-and-custom", !cfg.Enabled && cfg.ActivePreset == BehaviorPresets.Dual
                && JsonSerializer.Serialize(cfg.CustomScheme) == original);
            cfg.SelectPreset(BehaviorPresets.Separate); settings.Normalize();
            Chk("preset-switch-keeps-custom-snapshot", cfg.ActivePreset == BehaviorPresets.Separate
                && JsonSerializer.Serialize(cfg.CustomScheme) == original);
            cfg.SelectPreset(BehaviorPresets.Custom); settings.Normalize();
            Chk("preset-custom-restores-keys-and-graph", cfg.Bindings[0].Chord == "F9"
                && JsonSerializer.Serialize(new BehaviorScheme { Bindings = cfg.Bindings, Graph = cfg.Graph }) == original);
            cfg.SelectPreset(BehaviorPresets.Single); settings.Normalize();
            cfg.Bindings[0].Chord = "F8"; settings.Normalize();
            Chk("preset-rebinding-becomes-custom", cfg.ActivePreset == BehaviorPresets.Custom && cfg.CustomScheme!.Bindings[0].Chord == "F8");
            cfg.SelectPreset(BehaviorPresets.Dual); cfg.SelectPreset(BehaviorPresets.Custom);
            Chk("preset-modified-custom-replaces-saved-scheme", cfg.Bindings[0].Chord == "F8" && cfg.Bindings.Count == 1);
            cfg.SelectPreset(BehaviorPresets.Single); settings.Normalize();
            cfg.Graph.Nodes.First(node => node.Action == GestureActions.Select).ShowModeSwitch = false; settings.Normalize();
            Chk("preset-graph-edit-becomes-custom", cfg.ActivePreset == BehaviorPresets.Custom
                && !cfg.CustomScheme!.Graph.Nodes.First(node => node.Action == GestureActions.Select).ShowModeSwitch);
            cfg.SelectPreset(BehaviorPresets.Separate); settings.Normalize();
            settings.ToastSeconds = 7; settings.RegionW = 200; settings.RegionH = 100;
            cfg.Enabled = true; settings.Normalize();
            Chk("preset-unrelated-settings-keep-selection", cfg.ActivePreset == BehaviorPresets.Separate);
            var round = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!; round.Normalize();
            Chk("preset-selection-and-custom-survive-reload", round.Gestures.ActivePreset == BehaviorPresets.Separate
                && JsonSerializer.Serialize(round.Gestures.CustomScheme) == JsonSerializer.Serialize(cfg.CustomScheme));
            cfg.SelectPreset(BehaviorPresets.Custom);
            Chk("preset-custom-restoration-is-independent", !ReferenceEquals(cfg.Graph, cfg.CustomScheme!.Graph)
                && !ReferenceEquals(cfg.Bindings[0], cfg.CustomScheme.Bindings[0]));
            cfg.Bindings.Clear(); cfg.Graph.Flows.Clear(); cfg.Graph.Collect(); settings.Normalize();
            cfg.SelectPreset(BehaviorPresets.Single); cfg.SelectPreset(BehaviorPresets.Custom); settings.Normalize();
            Chk("preset-empty-custom-is-not-reseeded", cfg.Bindings.Count == 0 && cfg.Graph.Flows.Count == 0);
            string unchanged = JsonSerializer.Serialize(cfg);
            Chk("preset-unknown-selection-does-not-mutate", !cfg.SelectPreset("unknown") && JsonSerializer.Serialize(cfg) == unchanged);
        }
        catch (Exception ex) { Chk("preset-policy", false, ex.ToString()); }
    }
}
