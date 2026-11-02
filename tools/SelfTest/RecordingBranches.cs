using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using NekoCapture.Capture;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;

static partial class SelfTest
{
    static void RecordingBranchPolicy()
    {
        var graph = new BehaviorGraph();
        var record = graph.Add(GestureActions.RecordStart);
        var start = graph.Add(GestureActions.GrabStill);
        var stop = graph.Add(GestureActions.SavePng);
        var cancel = graph.Add(GestureActions.ClearRegion);
        var sibling = graph.Add(GestureActions.CopyLast);
        record.Branches[BehaviorPorts.Started].Add(start.Id);
        record.Branches[BehaviorPorts.Done].Add(stop.Id);
        record.Branches[BehaviorPorts.Cancel].Add(cancel.Id);
        var flow = new BehaviorFlow { Steps = new() { record.Id, sibling.Id } }; graph.Flows.Add(flow);
        var order = new List<string>();
        var stopped = new TaskCompletionSource<string>();
        var run = BehaviorRunner.RunAsync(graph, flow.Id, async (node, emit) =>
        {
            order.Add(node.Id);
            if (node != record) return BehaviorPorts.Done;
            await emit(BehaviorPorts.Started);
            return await stopped.Task;
        });
        Chk("record-events-start-runs-before-stop", !run.IsCompleted && order.SequenceEqual(new[] { record.Id, start.Id }));
        stopped.SetResult(BehaviorPorts.Done);
        Chk("record-events-stop-precedes-siblings", run.Result
            && order.SequenceEqual(new[] { record.Id, start.Id, stop.Id, sibling.Id }));
        order.Clear();
        run = BehaviorRunner.RunAsync(graph, flow.Id, node =>
        {
            order.Add(node.Id); return Task.FromResult(node == record ? BehaviorPorts.Cancel : BehaviorPorts.Done);
        });
        Chk("record-events-cancel-has-own-actions", !run.Result && order.SequenceEqual(new[] { record.Id, cancel.Id }));
        Chk("record-events-catalog-and-preset-stop-tail", GestureActions.PortsOf(GestureActions.RecordStart)
            .SequenceEqual(new[] { BehaviorPorts.Started, BehaviorPorts.Cancel, BehaviorPorts.Done, BehaviorPorts.Failed })
            && BehaviorPresets.Create(BehaviorPresets.Single).Graph.Nodes.First(n => n.Action == GestureActions.RecordStart)
                .Branches[BehaviorPorts.Done].Count == 1);
    }

    static void RecordingBranchIntegration(SettingsService settings)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var saved = settings.Current.Clone();
        try
        {
            settings.Current.RecordNeedsStartClick = true;
            var graph = new BehaviorGraph();
            var select = graph.Add(GestureActions.Select);
            var record = graph.Add(GestureActions.RecordStart);
            select.Branches[BehaviorPorts.Record].Add(record.Id);
            record.Branches[BehaviorPorts.Started].Add(graph.Add(GestureActions.SavePng).Id);
            record.Branches[BehaviorPorts.Done].Add(graph.Add(GestureActions.ClearRegion).Id);
            record.Branches[BehaviorPorts.Cancel].Add(graph.Add(GestureActions.ClearRegion).Id);
            var flow = new BehaviorFlow { Steps = new() { graph.Add(GestureActions.GrabStill).Id, select.Id } }; graph.Flows.Add(flow);
            bool Wait(Func<bool> test)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!test() && watch.ElapsedMilliseconds < 5000) PumpMs(20);
                return test();
            }
            Task<bool> Running(CaptureController c) => (Task<bool>)typeof(CaptureController).GetField("_chainTask", flags)!.GetValue(c)!;
            void Open(CaptureController c)
            {
                c.BeginBehavior(BehaviorFixtureMode(graph, flow.Id, BehaviorPorts.Record), flow.Id, "record-events");
                var picker = (RegionPickerWindow)typeof(CaptureController).GetField("_picker", flags)!.GetValue(c)!;
                var mon = Monitors.All().First();
                var emit = (Action<RectI?, IntPtr>)typeof(RegionPickerWindow).GetField("Result", flags)!.GetValue(picker)!;
                emit(new RectI(mon.Bounds.X + 16, mon.Bounds.Y + 16, 64, 48), IntPtr.Zero); picker.Close();
                Wait(() => RecordPanelWindow.Current != null);
            }
            using (var c = new CaptureController(settings))
            {
                int files = System.IO.Directory.GetFiles(settings.Current.AutoSavePath, "*.png").Length;
                Open(c);
                Chk("record-panel-waits-before-start-branch", RecordPanelWindow.Current != null && c.HasLastStill && !Running(c).IsCompleted
                    && System.IO.Directory.GetFiles(settings.Current.AutoSavePath, "*.png").Length == files);
                typeof(RecordPanelWindow).GetMethod("StartRequested", flags)!.Invoke(RecordPanelWindow.Current, null);
                Chk("record-panel-start-executes-configured-actions", Wait(() => System.IO.Directory.GetFiles(settings.Current.AutoSavePath, "*.png").Length == files + 1)
                    && c.State == CaptureController.Phase.Capturing && !Running(c).IsCompleted
                    && c.StoredRegion() != null);
                typeof(RecordPanelWindow).GetMethod("StopRequested", flags)!.Invoke(RecordPanelWindow.Current, null);
                Chk("record-panel-stop-executes-configured-tail", Wait(() => Running(c).IsCompleted) && Running(c).Result
                    && c.HasLastVideo && c.StoredRegion() == null
                    && System.IO.Directory.GetFiles(settings.Current.AutoSavePath, "*.png").Length == files + 1);
            }
            using (var c = new CaptureController(settings))
            {
                var mon = Monitors.All().First();
                settings.Current.RegionMonitorId = mon.DeviceName; settings.Current.RegionX = mon.Bounds.X + 16;
                settings.Current.RegionY = mon.Bounds.Y + 16; settings.Current.RegionW = 64; settings.Current.RegionH = 48;
                int files = System.IO.Directory.GetFiles(settings.Current.AutoSavePath, "*.png").Length;
                Open(c);
                typeof(RecordPanelWindow).GetMethod("StopRequested", flags)!.Invoke(RecordPanelWindow.Current, null);
                Chk("record-panel-cancel-executes-only-cancel-branch", Wait(() => Running(c).IsCompleted) && !Running(c).Result
                    && c.HasLastStill && !c.HasLastVideo && c.StoredRegion() == null
                    && System.IO.Directory.GetFiles(settings.Current.AutoSavePath, "*.png").Length == files);
            }
            record.Branches[BehaviorPorts.Failed].Add(graph.Add(GestureActions.ClearRegion).Id);
            flow.Steps.RemoveAt(0);
            using (var c = new CaptureController(settings))
            {
                Open(c);
                typeof(RecordPanelWindow).GetMethod("StartRequested", flags)!.Invoke(RecordPanelWindow.Current, null);
                Chk("record-start-branch-failure-stops-session-and-runs-failure-tail", Wait(() => Running(c).IsCompleted)
                    && !Running(c).Result && c.State == CaptureController.Phase.Idle
                    && RecordPanelWindow.Current == null && c.StoredRegion() == null);
            }
        }
        finally { RecordPanelWindow.Dismiss(); settings.Save(saved); }
    }
}
