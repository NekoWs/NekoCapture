using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    static void BehaviorIntegration()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var controllerType = typeof(CaptureController);
        var sync = SynchronizationContext.Current;
        var service = new SettingsService();
        var hostSettings = typeof(AppHost).GetProperty("Settings")!;
        var previousSettings = hostSettings.GetValue(AppHost.Current);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            service.Current.CaptureSound = false; service.Current.AutoSaveCopy = false;
            service.Current.RecordNeedsStartClick = false; service.Current.UseWgc = false;
            service.Current.ShowRecordingBorder = false; service.Current.FreezeOnSelect = true;
            service.Current.AlwaysShowRegionOutline = false; service.Current.OutlineFadeSeconds = 0.3;
            service.Current.FreezePregrabMaxWindows = 0;
            service.Current.AutoSavePath = Path.Combine(SettingsService.Dir, "behavior-products-" + Guid.NewGuid().ToString("N"));
            var mon = Monitors.All().First();
            var bounds = new RectI(mon.Bounds.X + 4, mon.Bounds.Y + 4, 32, 32);
            bool Wait(Func<bool> done)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!done() && watch.ElapsedMilliseconds < 5000) PumpMs(20);
                return done();
            }
            Task<bool> Running(CaptureController c) => (Task<bool>)controllerType.GetField("_chainTask", flags)!.GetValue(c)!;
            RegionPickerWindow Picker(CaptureController c) => (RegionPickerWindow)controllerType.GetField("_picker", flags)!.GetValue(c)!;
            void Choose(CaptureController c, bool recording)
            {
                var picker = Picker(c);picker.SetRecordMode(recording);
                var emit = (Action<RectI?, IntPtr>)typeof(RegionPickerWindow).GetField("Result", flags)!.GetValue(picker)!;
                emit(bounds, IntPtr.Zero);picker.Close();
            }
            var graph = new BehaviorGraph();var selection = graph.Add(GestureActions.Select);
            var save = graph.Add(GestureActions.SavePng);
            selection.Branches["still"] = new() { graph.Add(GestureActions.GrabStill).Id, save.Id };
            selection.Branches["record"] = new() { graph.Add(GestureActions.RecordStart).Id };
            var entry = new BehaviorFlow { Steps = new() { selection.Id } };graph.Flows.Add(entry);
            using (var c = new CaptureController(service))
            {
                c.BeginBehavior(graph.Snapshot(), entry.Id, "screenshot", "still");
                Chk("T273-graph-picker-initial-still", !Picker(c).RecordMode);
                Choose(c, true);
                bool recording = Wait(() => c.State == CaptureController.Phase.Capturing);
                c.BeginBehavior(graph.Snapshot(), entry.Id, "screenshot", "still");
                Chk("T273-graph-switched-key-stops-owner", recording && Wait(() => Running(c).IsCompleted)
                    && Running(c).Result && c.HasLastVideo && !c.HasLastStill);
            }
            using (var c = new CaptureController(service))
            {
                c.BeginBehavior(graph.Snapshot(), entry.Id, "record", "record");
                Chk("T273-graph-picker-initial-record", Picker(c).RecordMode);
                Choose(c, false);
                Chk("T273-graph-record-key-selects-screenshot-branch", Wait(() => Running(c).IsCompleted)
                    && Running(c).Result && c.HasLastStill && !c.HasLastVideo
                    && Directory.GetFiles(service.Current.AutoSavePath, "*.png").Length == 1);
                var outline = (RegionOutlineWindow?)controllerType.GetField("_outline", flags)!.GetValue(c);
                Chk("still-capture-shows-excluded-outline", outline is { IsVisible: true } && outline.EnsureExcludedFromCapture());
                PumpMs((int)(service.Current.OutlineFadeSeconds * 1000) + Fluent.Motion.DissolveMs + 100);
                Chk("still-capture-outline-retires", outline is { IsVisible: false },
                    $"visible={outline?.IsVisible}, opacity={outline?.Opacity}, seconds={service.Current.OutlineFadeSeconds}");
                service.Current.AlwaysShowRegionOutline = true;
                c.BeginBehavior(graph.Snapshot(), entry.Id, "record", "still"); Choose(c, false);
                bool ready = Wait(() => Running(c).IsCompleted);
                PumpMs(Fluent.Motion.DissolveMs + 100);
                Chk("still-capture-outline-respects-persistent-setting", ready && outline is { IsVisible: true },
                    $"ready={ready}, phase={c.State}, visible={outline?.IsVisible}, opacity={outline?.Opacity}");
                service.Current.AlwaysShowRegionOutline = false;
            }
            var only = graph.Add(GestureActions.SelectOnly);
            var onlyEntry = new BehaviorFlow { Steps = new() { only.Id } };graph.Flows.Add(onlyEntry);
            using (var c = new CaptureController(service))
            {
                c.BeginBehavior(graph.Snapshot(), onlyEntry.Id, "region");
                Chk("T273-graph-select-only-hides-switch", typeof(RegionPickerWindow).GetField("_modeSwitch", flags)!.GetValue(Picker(c)) == null);
                Choose(c, false);
                Chk("T273-graph-select-only-no-product", Wait(() => Running(c).IsCompleted) && Running(c).Result
                    && !c.HasLastStill && !c.HasLastVideo && c.StoredRegion()?.Bounds.Equals(bounds) == true);
                var direct = new BehaviorFlow { Steps = new() { graph.Add(GestureActions.RequireStored).Id, graph.Add(GestureActions.GrabStill).Id } };
                graph.Flows.Add(direct);
                c.BeginBehavior(graph.Snapshot(), direct.Id, "tap");
                Chk("T273-graph-tap-reuses-region", Running(c).IsCompletedSuccessfully && Running(c).Result && c.HasLastStill && c.StoredRegion()?.Bounds.Equals(bounds) == true);
                var hold = new BehaviorFlow { Steps = new() { graph.Add(GestureActions.RequireStored).Id, graph.Add(GestureActions.RecordStart).Id } };
                var release = new BehaviorFlow { Steps = new() { graph.Add(GestureActions.RecordStop).Id, graph.Add(GestureActions.ClearRegion).Id } };
                graph.Flows.Add(hold);graph.Flows.Add(release);
                c.BeginBehavior(graph.Snapshot(), hold.Id, "hold");
                bool recording = Wait(() => c.State == CaptureController.Phase.Capturing);
                c.BeginBehavior(graph.Snapshot(), release.Id, "release");
                Chk("T273-graph-hold-release-keeps-own-tail", recording && Wait(() => c.StoredRegion() == null)
                    && c.HasLastVideo && Wait(() => Running(c).IsCompleted) && Running(c).Result);
            }
            var edit = graph.Add(GestureActions.EditLastStill);
            edit.Branches["saved"].Add(graph.Add(GestureActions.SavePng).Id);
            edit.Branches["closed"].Add(graph.Add(GestureActions.ClearRegion).Id);
            var editing = new BehaviorFlow { Steps = new() { graph.Add(GestureActions.GrabStill).Id, edit.Id } };
            graph.Flows.Add(editing);
            using (var c = new CaptureController(service))
            {
                service.Mutate(s => { s.RegionMonitorId = mon.DeviceName;s.RegionX = bounds.X;s.RegionY = bounds.Y;s.RegionW = bounds.W;s.RegionH = bounds.H; });
                int count = Directory.GetFiles(service.Current.AutoSavePath, "*.png").Length;
                c.BeginBehavior(graph.Snapshot(), editing.Id, "edit");
                Chk("T273-graph-editor-waits", c.State == CaptureController.Phase.Editing && !Running(c).IsCompleted);
                controllerType.GetMethod("CloseEditor", flags)!.Invoke(c, null);
                Chk("T273-graph-editor-close-branch", Wait(() => Running(c).IsCompleted) && Running(c).Result
                    && c.StoredRegion() == null && Directory.GetFiles(service.Current.AutoSavePath, "*.png").Length == count);
                c.BeginBehavior(graph.Snapshot(), editing.Id, "edit");
                var window = (EditorWindow)controllerType.GetField("_editor", flags)!.GetValue(c)!;
                typeof(EditorWindow).GetMethod("Commit", flags)!.Invoke(window, null);
                Chk("T273-graph-editor-save-branch", Wait(() => Running(c).IsCompleted) && Running(c).Result
                    && Directory.GetFiles(service.Current.AutoSavePath, "*.png").Length == count + 1);
                edit.Branches["failed"].Add(graph.Add(GestureActions.ClearRegion).Id);
                service.Mutate(s => { s.RegionMonitorId = mon.DeviceName;s.RegionX = bounds.X;s.RegionY = bounds.Y;s.RegionW = bounds.W;s.RegionH = bounds.H; });
                string? forced = Environment.GetEnvironmentVariable("FC_TEST_ENCODE_FAIL");
                try
                {
                    Environment.SetEnvironmentVariable("FC_TEST_ENCODE_FAIL", "1");
                    c.BeginBehavior(graph.Snapshot(), editing.Id, "failed-edit");
                    window = (EditorWindow)controllerType.GetField("_editor", flags)!.GetValue(c)!;
                    typeof(EditorWindow).GetMethod("Commit", flags)!.Invoke(window, null);
                    Chk("T273-graph-editor-failure-branch", Wait(() => Running(c).IsCompleted) && !Running(c).Result
                        && c.StoredRegion() == null && c.State == CaptureController.Phase.Idle);
                }
                finally { Environment.SetEnvironmentVariable("FC_TEST_ENCODE_FAIL", forced); }
                service.Mutate(s => { s.RegionMonitorId = mon.DeviceName;s.RegionX = bounds.X;s.RegionY = bounds.Y;s.RegionW = bounds.W;s.RegionH = bounds.H; });
                var multiple = new BehaviorFlow { Steps = new() { graph.Add(GestureActions.GrabStill).Id,
                    graph.Add(GestureActions.RecordStart).Id, graph.Add(GestureActions.SavePng).Id } };
                graph.Flows.Add(multiple);
                count = Directory.GetFiles(service.Current.AutoSavePath, "*.png").Length;
                c.BeginBehavior(graph.Snapshot(), multiple.Id, "multi");
                bool started = Wait(() => c.State == CaptureController.Phase.Capturing);
                c.BeginBehavior(graph.Snapshot(), multiple.Id, "multi");
                Chk("T273-graph-multiple-products-stay-in-order", started && Wait(() => Running(c).IsCompleted)
                    && Running(c).Result && c.HasLastStill && c.HasLastVideo
                    && Directory.GetFiles(service.Current.AutoSavePath, "*.png").Length == count + 1);
            }
            hostSettings.SetValue(AppHost.Current, service);
            RecordingBranchIntegration(service);
            BehaviorInputIsolation(service);
            service.Current.Gestures.Graph = graph;service.Current.Gestures.GraphRevision = 1;
            var settings = new SettingsWindow();
            typeof(SettingsWindow).GetField("_restoreCat", flags)!.SetValue(settings, 1);
            typeof(SettingsWindow).GetMethod("Build", flags)!.Invoke(settings, null);
            settings.Left = -20000;settings.Top = -20000;settings.ShowInTaskbar = false;settings.Show();PumpMs(100);
            var editors = (System.Collections.IEnumerable)typeof(SettingsWindow).GetField("_behaviorEditors", flags)!.GetValue(settings)!;
            Chk("T273-graph-ui-uses-native-chain-editor", editors.Cast<object>().Any(e => e.GetType().Name == "ChainEditor"));
            var boxes = new List<DependencyObject>();CollectTyped((DependencyObject)typeof(SettingsWindow).GetField("_behaviorPanel", flags)!.GetValue(settings)!, typeof(ChordCaptureBox), boxes);
            Chk("T273-graph-ui-key-entry-stays-on-hotkeys", boxes.Count == 0);
            BehaviorNavigationChecks(settings, selection);
            BehaviorMotionChecks(settings, selection);
            ChainMotionChecks();
            typeof(SettingsWindow).GetField("_behaviorFlow", flags)!.SetValue(settings, editing.Id);
            ((List<string>)typeof(SettingsWindow).GetField("_behaviorPath", flags)!.GetValue(settings)!).Clear();
            typeof(SettingsWindow).GetMethod("RenderBehaviors", flags)!.Invoke(settings, null);
            var native = typeof(SettingsWindow).GetField("_behaviorRootEditor", flags)!.GetValue(settings)!;
            var nativeType = native.GetType();
            var originalIds = editing.Steps.ToArray();
            nativeType.GetMethod("Move", flags)!.Invoke(native, new object[] { 1, -1 });
            Chk("T273-graph-ui-reorder-preserves-node-branches", editing.Steps.SequenceEqual(originalIds.Reverse())
                && graph.Node(edit.Id)!.Branches["saved"].Count == 1 && graph.Node(edit.Id)!.Branches["closed"].Count == 1);
            var open = (Action<int>)nativeType.GetField("_openNode", flags)!.GetValue(native)!;
            open(0);
            var visiblePath = (List<string>)typeof(SettingsWindow).GetField("_behaviorPath", flags)!.GetValue(settings)!;
            Chk("T273-graph-ui-parent-navigation", visiblePath.SequenceEqual(new[] { edit.Id }));
            BehaviorPresetUiChecks(settings, service);
            SettingsPageMotionChecks(settings);
            SettingsEditingChecks(settings, service);
            RecordingReadyChecks(service);
            settings.Close();
        }
        catch (Exception ex) { Chk("T273-graph-integration", false, ex.ToString()); }
        finally
        {
            hostSettings.SetValue(AppHost.Current, previousSettings);
            SynchronizationContext.SetSynchronizationContext(sync);
            foreach (Window window in Application.Current.Windows.Cast<Window>().Where(w => w is EditorWindow or RegionPickerWindow).ToArray()) window.Close();
        }
    }

    static void BehaviorInputIsolation(SettingsService service)
    {
        var host = AppHost.Current;
        var hotkeyProperty = typeof(AppHost).GetProperty("Hotkeys")!;
        var controllerProperty = typeof(AppHost).GetProperty("Controller")!;
        var previousHotkeys = hotkeyProperty.GetValue(host);
        var previousController = controllerProperty.GetValue(host);
        var previousConfig = service.Current.Gestures;
        string? previousTestFlag = Environment.GetEnvironmentVariable("FC_TEST_NO_FG");
        using var hotkeys = new HotkeyManager();
        using var controller = new CaptureController(service);
        using var gestures = new GestureService(host);
        try
        {
            Environment.SetEnvironmentVariable("FC_TEST_NO_FG", "1");
            hotkeyProperty.SetValue(host, hotkeys);controllerProperty.SetValue(host, controller);
            var graph = new BehaviorGraph();
            var tap = new BehaviorFlow { Steps = new() { graph.Add(GestureActions.ClearRegion).Id } };
            var release = new BehaviorFlow { Steps = new() { graph.Add(GestureActions.RequireStored).Id } };
            graph.Flows.Add(tap);graph.Flows.Add(release);
            var config = new GestureConfig { Seeded = true, GraphRevision = 1, ChainRevision = 3, Graph = graph,
                Bindings = new() { new() { Id = "synthetic.tap", Chord = "Ctrl+Shift+G", Trigger = "tap", EntryId = tap.Id, LongPressMs = 250 },
                    new() { Id = "synthetic.release", Chord = "Ctrl+Shift+G", Trigger = "holdrelease", EntryId = release.Id, LongPressMs = 250 } } };
            service.Current.Gestures = config;
            gestures.Apply(config);
            var mods = GestureCore.ModifierKeys.Control | GestureCore.ModifierKeys.Shift;
            gestures.FeedSyntheticKey(0x47, true, mods, Environment.TickCount64);
            hotkeys.KeySink!(0x41, true, GestureCore.ModifierKeys.None, unchecked((uint)Environment.TickCount));
            PumpMs(80);
            Chk("T273-synthetic-input-ignores-physical-modifier-change", controller.StoredRegion() != null);
            PumpMs(500);
            gestures.FeedSyntheticKey(0x47, false, mods, Environment.TickCount64);
            PumpMs(80);
            var pending = typeof(CaptureController).GetField("_chainTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller) as Task<bool>;
            Chk("T273-synthetic-input-keeps-release-gesture", controller.StoredRegion() != null && pending?.IsCompletedSuccessfully == true && pending.Result);
        }
        finally
        {
            service.Current.Gestures = previousConfig;
            hotkeyProperty.SetValue(host, previousHotkeys);controllerProperty.SetValue(host, previousController);
            Environment.SetEnvironmentVariable("FC_TEST_NO_FG", previousTestFlag);
        }
    }
}
