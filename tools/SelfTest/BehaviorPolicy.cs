using System.Text.Json;
using NekoCapture.Core;

static partial class SelfTest
{
    static void BehaviorPolicy()
    {
        BehaviorPresetPolicy();
        RecordingBranchPolicy();
        try
        {
            Chk("T272-input-clock-wrap", GestureClock.Expand(5, 4294967306) == 4294967301
                && GestureClock.Expand(-1, 4294967306) == 4294967295);
            Chk("T272-input-clock-full-width", GestureClock.Expand(8589934595, 8589934600) == 8589934595);
            var cfg = new GestureConfig { Bindings = GestureConfig.DefaultQqSet("F6", "F7"), Seeded = true };
            cfg.Migrate();
            var graph = cfg.Graph;
            var root = graph.Node(graph.Flow(cfg.Bindings[0].EntryId)!.Steps.Single())!;
            Chk("T272-graph-stock-shared-entry", graph.IsValid() && cfg.Bindings[0].EntryId == cfg.Bindings[1].EntryId
                && !cfg.BindingGraph(cfg.Bindings[0]).Node(root.Id)!.RecordMode && cfg.BindingGraph(cfg.Bindings[1]).Node(root.Id)!.RecordMode);
            Chk("T272-graph-engine-entry", cfg.ToBindings().Count == 2
                && cfg.ToBindings().All(b => b.Actions.SequenceEqual(new[] { GestureActions.CallBehavior })));
            string once = JsonSerializer.Serialize(cfg);
            Chk("T272-graph-import-idempotent", !cfg.Migrate() && once == JsonSerializer.Serialize(cfg));
            var round = JsonSerializer.Deserialize<GestureConfig>(once)!;
            Chk("T272-graph-shared-roundtrip", round.Graph.IsValid() && round.Graph.Flows.Count(f => !f.Id.StartsWith("tray.")) == 1
                && round.Bindings.Where(b => b.Source == TriggerSources.Keyboard).Select(b => b.EntryId).Distinct().Count() == 1);
            var order = new List<string>();
            var run = BehaviorRunner.RunAsync(graph, graph.Flows[0].Id, node =>
            {
                order.Add(node.Action);
                return Task.FromResult(node.Action == GestureActions.Select ? "still" : "done");
            });
            Chk("T272-graph-mode-chooses-whole-branch", run.Result
                && order.SequenceEqual(new[] { "select", "grab.still", "notify.toast" }));
            order.Clear();
            var wait = new TaskCompletionSource<string>();
            run = BehaviorRunner.RunAsync(graph, graph.Flows[0].Id, node =>
            {
                order.Add(node.Action);
                return node.Action == "record.start" ? wait.Task : Task.FromResult(node.Action switch
                { "select" => "record", "edit.lastvideo" => "saved", _ => "done" });
            });
            Chk("T272-graph-recording-awaits-stop", !run.IsCompleted
                && order.SequenceEqual(new[] { "select", "record.start" }));
            wait.SetResult("done");
            Chk("T272-graph-recording-saved-path", run.IsCompletedSuccessfully && run.Result
                && order.SequenceEqual(new[] { "select", "record.start", "edit.lastvideo", "copy.clipboard" }));
            order.Clear();
            run = BehaviorRunner.RunAsync(graph, graph.Flows[0].Id, node =>
            {
                order.Add(node.Action);
                return Task.FromResult(node.Action switch { "select" => "record", "edit.lastvideo" => "closed", _ => "done" });
            });
            Chk("T272-graph-editor-close-is-not-save", run.Result
                && order.SequenceEqual(new[] { "select", "record.start", "edit.lastvideo" }));
            var cancel = graph.Add("scope.clear");
            root.Branches["cancel"].Add(cancel.Id);
            graph.Flows[0].Steps.Add(graph.Add("scope.saved").Id);
            order.Clear();
            run = BehaviorRunner.RunAsync(graph, graph.Flows[0].Id, node =>
            { order.Add(node.Action); return Task.FromResult(node.Action == "select" ? "cancel" : "done"); });
            Chk("T272-graph-cancel-exit-stops-siblings", !run.Result && order.SequenceEqual(new[] { "select", "scope.clear" }));
            root.ContinueOnCancel = true;
            order.Clear();
            run = BehaviorRunner.RunAsync(graph, graph.Flows[0].Id, node =>
            { order.Add(node.Action); return Task.FromResult(node.Action == "select" ? "cancel" : "done"); });
            Chk("T272-graph-cancel-continuation", run.Result && order.Last() == "scope.saved");
            var invalid = graph.Snapshot();
            invalid.Node(root.Id)!.Branches["still"].Add(root.Id);
            Chk("T272-graph-rejects-cycle", !invalid.IsValid());
            var independent = new BehaviorFlow { Steps = new() { invalid.Add("scope.clear").Id } };
            invalid.Flows.Add(independent);
            Chk("T272-graph-invalid-entry-isolated", invalid.IsValid(independent.Id) && !invalid.IsValid());
            invalid = graph.Snapshot(); invalid.Node(root.Id)!.Branches["unknown"] = new();
            Chk("T272-graph-rejects-unknown-event", !invalid.IsValid());
            invalid = graph.Snapshot(); invalid.Flows[0].Steps.Add("missing");
            Chk("T272-graph-rejects-dangling-node", !invalid.IsValid());
            var called = new BehaviorFlow { Steps = new() { graph.Add("scope.clear").Id } };
            graph.Flows.Add(called);
            var call = graph.Add(GestureActions.CallBehavior); call.TargetId = called.Id;
            var caller = new BehaviorFlow { Steps = new() { call.Id, graph.Add("scope.saved").Id } };
            graph.Flows.Add(caller); order.Clear();
            run = BehaviorRunner.RunAsync(graph, caller.Id, node => { order.Add(node.Action); return Task.FromResult("done"); });
            Chk("T272-graph-call-returns-to-caller", run.Result && order.SequenceEqual(new[] { "scope.clear", "scope.saved" }));
            called.Steps.Add(call.Id);
            Chk("T272-graph-call-cycle-rejected", !graph.IsValid(caller.Id));
            var imported = new GestureConfig { Seeded = true, ChainRevision = 3, Bindings = new()
            { new() { Id = "user.multi", Actions = new[] { "select", "grab.still", "record.start", "save.png" } } } };
            imported.Migrate();
            var selected = imported.Graph.Node(imported.Graph.Flows[0].Steps[0])!;
            Chk("T272-graph-import-preserves-multiple-products", imported.Graph.IsValid()
                && selected.Branches["still"].Select(id => imported.Graph.Node(id)!.Action)
                    .SequenceEqual(new[] { "grab.still", "record.start", "save.png" })
                && selected.Branches["record"].Select(id => imported.Graph.Node(id)!.Action)
                    .SequenceEqual(new[] { "record.start", "record.start", "save.png" }));
            var capped = new BehaviorGraph();var leaf = capped.Add("scope.clear");var extra = capped.Add("scope.saved");
            leaf.Branches["done"].Add(extra.Id);
            var cappedFlow = new BehaviorFlow { Steps = Enumerable.Repeat(leaf.Id, 2048).ToList() };capped.Flows.Add(cappedFlow);
            int calls = 0;
            run = BehaviorRunner.RunAsync(capped, cappedFlow.Id, _ => { calls++;return Task.FromResult("done"); });
            Chk("T272-graph-step-budget", !run.Result && calls == 2048);
            var failureGraph = new BehaviorGraph();var failing = failureGraph.Add("scope.saved");
            failing.Branches["failed"].Add(failureGraph.Add("scope.clear").Id);
            var failureFlow = new BehaviorFlow { Steps = new() { failing.Id, failureGraph.Add("grab.still").Id } };
            failureGraph.Flows.Add(failureFlow);order.Clear();
            run = BehaviorRunner.RunAsync(failureGraph, failureFlow.Id, node =>
            { order.Add(node.Action);return Task.FromResult(node.Id == failing.Id ? "failed" : "done"); });
            Chk("T272-graph-failure-runs-once", !run.Result && order.SequenceEqual(new[] { "scope.saved", "scope.clear" }));
            failing.ContinueOnFailure = true;order.Clear();
            run = BehaviorRunner.RunAsync(failureGraph, failureFlow.Id, node =>
            { order.Add(node.Action);return Task.FromResult(node.Id == failing.Id ? "failed" : "done"); });
            Chk("T272-graph-failure-continues-when-configured", run.Result && order.Last() == "grab.still");
            var cancelledImport = new GestureConfig { Seeded = true, Bindings = new()
            { new() { Actions = new[] { "select", "grab.still" }, OnFail = new[] { "help.open" } } } };
            cancelledImport.Migrate();order.Clear();
            run = BehaviorRunner.RunAsync(cancelledImport.Graph, cancelledImport.Graph.Flows[0].Id, node =>
            { order.Add(node.Action);return Task.FromResult(node.Action == "select" ? "cancel" : "done"); });
            Chk("T272-graph-import-preserves-cancel-handler", !run.Result && order.SequenceEqual(new[] { "select", "help.open" }));
            cfg.Graph.Flows.Clear();cfg.Graph.Collect();cfg.Migrate();
            Chk("T272-graph-empty-stays-empty", cfg.Graph.Flows.Count == 0 && cfg.Graph.Nodes.Count == 0
                && cfg.ToBindings().Count == 0);
        }
        catch (Exception ex) { Chk("T272-graph-policy", false, ex.ToString()); }
    }
}
