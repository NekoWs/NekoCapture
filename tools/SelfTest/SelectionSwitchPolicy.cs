using FrameCapture.Core;
using GestureCore;

static partial class SelfTest
{
    static void SelectionSwitchPolicy()
    {
        var stillThenRecord = new[] { "select", "grab.still", "record.start", "edit.result" };
        Chk("T271-switch-target-first-only", SelectionCapture.TargetIndex(stillThenRecord, 0) == 1
            && SelectionCapture.IsSwitchTarget(stillThenRecord, 1) && !SelectionCapture.IsSwitchTarget(stillThenRecord, 2));
        Chk("T271-switch-record-default", SelectionCapture.IsRecording("record.start")
            && SelectionCapture.IsRecording("record.toggle") && !SelectionCapture.IsRecording("grab.still"));
        Chk("T271-switch-still-to-record", SelectionCapture.Resolve("grab.still", true) == "record.start");
        Chk("T271-switch-record-to-still", SelectionCapture.Resolve("record.start", false) == "grab.still"
            && SelectionCapture.Resolve("record.toggle", false) == "grab.still");
        Chk("T271-switch-same-mode-preserves-operation", SelectionCapture.Resolve("grab.window", false) == "grab.window"
            && SelectionCapture.Resolve("scroll.capture", false) == "scroll.capture"
            && SelectionCapture.Resolve("capture.frame", false) == "capture.frame"
            && SelectionCapture.Resolve("record.toggle", true) == "record.toggle");
        Chk("T271-switch-special-capture", SelectionCapture.Resolve("scroll.capture", true) == "record.start"
            && SelectionCapture.Resolve("capture.delayed", true) == "record.start"
            && SelectionCapture.Resolve("grab.window", true) == "record.start");
        Chk("T271-switch-does-not-target-stop", !SelectionCapture.IsCapture("record.stop")
            && SelectionCapture.TargetIndex(new[] { "select", "record.stop", "grab.still" }, 0) == 2);
        Chk("T271-switch-select-boundary", SelectionCapture.TargetIndex(new[] { "select", "help.open", "select", "record.start" }, 0) == -1
            && SelectionCapture.TargetIndex(new[] { "select", "help.open", "select", "record.start" }, 2) == 3);
        Chk("T271-switch-implicit-capture", SelectionCapture.TargetIndex(new[] { "select", "copy.clipboard" }, 0) == -1);
        Chk("T271-switch-no-selection-no-override", !SelectionCapture.IsSwitchTarget(new[] { "grab.still", "record.start" }, 0)
            && !SelectionCapture.IsSwitchTarget(new[] { "scope.stored", "record.start" }, 1));
        Chk("T271-switch-same-key-stops-recording", SelectionCapture.StopsRecording(stillThenRecord, stillThenRecord)
            && SelectionCapture.StopsRecording(new[] { "select", "grab.still" }, new[] { "select", "grab.still" })
            && SelectionCapture.StopsRecording(new[] { "select" }, new[] { "select" })
            && SelectionCapture.StopsRecording(new[] { "record.stop", "edit.result" }, stillThenRecord)
            && !SelectionCapture.StopsRecording(new[] { "help.open" }, stillThenRecord));

        var runs = new List<string>();
        var targets = new[] { -1, -1 };
        var failure = new[] { "select", "record.start", "help.open" };
        var actions = new[] { "select", "grab.still", "record.start", "bad.step" };
        var completed = ActionChain.RunIndexedAsync(actions, failure, FallbackPolicy.Stop, (index, name) =>
        {
            int origin = index < 0 ? 1 : 0, position = index < 0 ? ~index : index;
            var sequence = origin == 0 ? actions : failure;
            if (name == "select") targets[origin] = SelectionCapture.TargetIndex(sequence, position);
            else if (position == targets[origin]) name = SelectionCapture.Resolve(name, origin == 0);
            runs.Add(name);
            return Task.FromResult(name != "bad.step");
        }).GetAwaiter().GetResult();
        Chk("T271-switch-multiple-products-and-failure-chain", !completed && runs.SequenceEqual(new[]
            { "select", "record.start", "record.start", "bad.step", "select", "grab.still", "help.open" }));

        var wait = new TaskCompletionSource<bool>();
        runs.Clear();
        var pending = ActionChain.RunIndexedAsync(stillThenRecord, Array.Empty<string>(), FallbackPolicy.Stop, async (index, name) =>
        {
            if (index == 1) name = SelectionCapture.Resolve(name, true);
            runs.Add(name);
            return index != 1 || await wait.Task;
        });
        Chk("T271-switch-record-waits", !pending.IsCompleted && runs.SequenceEqual(new[] { "select", "record.start" }));
        wait.SetResult(true);
        Chk("T271-switch-record-resumes-later-products", pending.GetAwaiter().GetResult()
            && runs.SequenceEqual(new[] { "select", "record.start", "record.start", "edit.result" }));

        var stock = new GestureConfig
        {
            ChainRevision = 2, Seeded = true,
            Bindings = new()
            {
                new() { Id = "default.record", Chord = "F7", Actions = new[] { "select", "record.toggle", "edit.lastvideo" } },
                new() { Id = "custom", Chord = "F8", Actions = new[] { "select", "record.start", "edit.lastvideo", "grab.still" } },
            },
        };
        Chk("T271-switch-stock-edit-result-migration", stock.Migrate() && stock.ChainRevision == 3
            && stock.Bindings[0].Actions.SequenceEqual(new[] { "select", "record.toggle", "edit.result" })
            && stock.Bindings[1].Actions.SequenceEqual(new[] { "select", "record.start", "edit.lastvideo", "grab.still" })
            && !stock.Migrate());
        Chk("T271-switch-stock-defaults", GestureConfig.DefaultQqSet("F6", "F7")[1].Actions
            .SequenceEqual(new[] { "select", "record.toggle", "edit.result" }));
        Chk("T271-switch-dynamic-edit-catalog", GestureActions.All.Count(a => a.Name == "edit.result") == 1
            && GestureActions.InfoOf("edit.result").Class == VerbClass.Terminal);
    }
}
