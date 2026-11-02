using System.Text.Json;
using NekoCapture.Core;
using NekoCapture.Runtime;
using NekoCapture.Services;

static partial class SelfTest
{
    private static BehaviorGraph BehaviorFixtureMode(BehaviorGraph graph, string entry, string mode)
    {
        var config = new GestureConfig { Graph = graph };
        var node = BehaviorParameters.Reachable(graph, entry).First(n => n.Action is GestureActions.Select or GestureActions.SelectOnly);
        var binding = new GestureBindingDto { EntryId = entry };
        binding.Overrides[node.Id] = new() { [nameof(BehaviorNode.RecordMode)] = JsonSerializer.SerializeToElement(mode == BehaviorPorts.Record) };
        return config.BindingGraph(binding);
    }

    private static void TriggerChecks()
    {
        var config = GestureConfig.CreateCurrentDefaults();
        Chk("triggers-default-graph-valid", config.Graph.IsValid());
        Chk("triggers-keys-only-enter-key-engine", config.ToBindings().Count == 2
            && config.Bindings.Count(b => b.Source == TriggerSources.TrayMenu) == 4
            && config.Bindings.Count(b => b.Source == TriggerSources.TrayClick) == 1
            && config.Bindings.Count(b => b.Source == TriggerSources.TrayDoubleClick) == 1);
        var gate = new TrayClickGate();
        gate.Configure("single", "double", false);
        Chk("tray-single-waits-for-distinct-double", gate.Release() == "" && gate.Pending);
        Chk("tray-double-suppresses-both-single-releases", gate.DoubleClick() == "double" && gate.Release() == "" && gate.Flush() == "");
        gate.Release(); Chk("tray-single-runs-once-after-timeout", gate.Flush() == "single" && gate.Flush() == "");
        gate.Configure("single", "double", true);
        Chk("tray-same-behavior-keeps-immediate-click", gate.Release() == "single" && !gate.Pending
            && gate.DoubleClick() == "" && gate.Release() == "");
        gate.Configure("", "double", false);
        Chk("tray-double-without-single", gate.Release() == "" && !gate.Pending && gate.DoubleClick() == "double" && gate.Release() == "");
        gate.Configure("single", "", false);
        Chk("tray-single-without-double-is-immediate", gate.Release() == "single" && !gate.Pending);
        var clicks = TriggerExecution.TrayClicks(config);
        Chk("tray-default-clicks-share-capture", clicks.Single.Length > 0 && clicks.Double.Length > 0 && clicks.Equivalent);
        config.Enabled = false;
        Chk("trigger-master-switch-disables-tray-clicks", TriggerExecution.TrayClicks(config) == ("", "", false));
        config.Enabled = true;
        var migrated = new GestureConfig { Graph = new BehaviorGraph(), GraphRevision = 1, ChainRevision = 3 };
        var select = migrated.Graph.Add(GestureActions.Select);
        var flow = new BehaviorFlow { Steps = new() { select.Id } }; migrated.Graph.Flows.Add(flow);
        var binding = new GestureBindingDto { EntryId = flow.Id, EntryMode = BehaviorPorts.Record };
        migrated.Bindings.Add(binding); migrated.Migrate();
        Chk("trigger-mode-import-preserves-local-override", binding.EntryMode == null
            && migrated.BindingGraph(binding).Node(select.Id)!.RecordMode && !select.RecordMode);
        string once = JsonSerializer.Serialize(migrated);
        Chk("trigger-migration-is-idempotent", !migrated.Migrate() && JsonSerializer.Serialize(migrated) == once);
        migrated.Bindings.RemoveAll(b => b.Source != TriggerSources.Keyboard); migrated.Graph.Flows.RemoveAll(f => f.Id.StartsWith("tray."));
        migrated.Graph.Collect(); migrated.Migrate();
        Chk("deleted-tray-entries-stay-deleted", migrated.Bindings.Count == 1 && migrated.Graph.Flows.Count == 1);
        var save = migrated.Graph.Add(GestureActions.SaveResult); flow.Steps.Add(save.Id);
        binding.Overrides[save.Id] = new() { [nameof(BehaviorNode.ContinueOnFailure)] = JsonSerializer.SerializeToElement(true) };
        var snapshot = migrated.BindingGraph(binding);
        Chk("trigger-overrides-support-non-selection-actions", snapshot.Node(save.Id)!.ContinueOnFailure && !save.ContinueOnFailure);
        binding.Overrides[save.Id][nameof(BehaviorNode.ContinueOnFailure)] = JsonSerializer.SerializeToElement(false);
        Chk("trigger-running-snapshot-is-independent", snapshot.Node(save.Id)!.ContinueOnFailure);
        var menu = config.Bindings.First(b => b.Source == TriggerSources.TrayMenu);
        menu.Name = "custom menu"; menu.Enabled = false;
        var restored = JsonSerializer.Deserialize<GestureConfig>(JsonSerializer.Serialize(config))!;
        Chk("trigger-menu-name-and-disabled-state-persist", restored.Bindings.Single(b => b.Id == menu.Id) is { Name: "custom menu", Enabled: false });
        var secondSelect = migrated.Graph.Add(GestureActions.Select); flow.Steps.Insert(0, secondSelect.Id);
        Chk("trigger-override-targets-specific-selection", !migrated.BindingGraph(binding).Node(secondSelect.Id)!.RecordMode
            && migrated.BindingGraph(binding).Node(select.Id)!.RecordMode);
        var single = config.Bindings.Single(b => b.Source == TriggerSources.TrayClick);
        var captureNode = config.Graph.Node(config.Graph.Flow(single.EntryId)!.Steps[0])!;
        single.Overrides[captureNode.Id] = new() { [nameof(BehaviorNode.RecordMode)] = JsonSerializer.SerializeToElement(false) };
        Chk("tray-inherited-and-explicit-defaults-are-equivalent", TriggerExecution.TrayClicks(config).Equivalent);
        var clipboard = config.Graph.Flow(TrayDefaults.Clipboard)!;
        Chk("tray-clipboard-chain-separates-input-and-pin", clipboard.Steps.Select(id => config.Graph.Node(id)!.Action)
            .SequenceEqual(new[] { GestureActions.ReadClipboardImage, GestureActions.PinSelection }));
    }
}
