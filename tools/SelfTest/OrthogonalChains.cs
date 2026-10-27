using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;
using GestureCore;

static partial class SelfTest
{
    static void OrthogonalChains()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            var imported = new GestureConfig
            {
                Seeded = true,
                Bindings = new()
                {
                    new() { Chord = "F6", Actions = new[] { "scope.select", "capture.stored" } },
                    new() { Chord = "F7", Actions = new[] { "scope.select", "record.toggle" }, OnComplete = new[] { "edit.lastvideo" } },
                    new() { Chord = "F8", Actions = new[] { "record.start" } },
                    new() { Chord = "F9", Actions = new[] { "scope.select", "scroll.capture" }, OnComplete = new[] { "copy.clipboard" } },
                },
            };
            Chk("T270-chain-migrate-still", imported.Migrate()
                && imported.Bindings[0].Actions.SequenceEqual(new[] { "select", "grab.still" }));
            Chk("T270-chain-migrate-record", imported.Bindings[1].Actions.SequenceEqual(new[] { "select", "record.toggle", "edit.lastvideo" })
                && imported.Bindings.All(b => b.OnComplete == null));
            Chk("T270-chain-migrate-direct-start", imported.Bindings[2].Actions.SequenceEqual(new[] { "scope.stored", "record.start" }));
            Chk("T270-chain-migrate-scroll", imported.Bindings[3].Actions.SequenceEqual(new[] { "select", "scroll.capture", "copy.clipboard" }));
            string first = System.Text.Json.JsonSerializer.Serialize(imported);
            Chk("T270-chain-migrate-idempotent", !imported.Migrate() && imported.ChainRevision == GestureConfig.CurrentChainRevision
                && first == System.Text.Json.JsonSerializer.Serialize(imported) && !first.Contains("OnComplete"));
            Chk("T270-chain-legacy-alias", GestureActions.IsValid("scope.select") && GestureActions.IsValid("capture.stored")
                && GestureActions.LabelOf("scope.select") == GestureActions.LabelOf("select")
                && new GestureBindingDto { Chord = "F6", Actions = new[] { "scope.select", "capture.stored" } }
                    .ToBinding()!.Actions.SequenceEqual(new[] { "select", "grab.still" }));
            Chk("T270-chain-editor-verbs", GestureActions.All.Select(a => a.Name).Distinct().Count() == GestureActions.All.Length
                && GestureActions.All.Select(a => a.Name).Contains("grab.window")
                && GestureActions.All.Select(a => a.Name).Contains("save.png")
                && GestureActions.All.All(a => a.Name == GestureActions.Canonical(a.Name)));
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository != null && !File.Exists(Path.Combine(repository.FullName, "FrameCapture.slnx"))) repository = repository.Parent;
            string document = File.ReadAllText(Path.Combine(repository!.FullName, "docs", "PLAN-ORTHOGONAL-CHAINS.md"));
            var documented = System.Text.RegularExpressions.Regex.Matches(document, @"(?m)^\| `([^`]+)` \|")
                .Select(m => m.Groups[1].Value).ToArray();
            Chk("T270-chain-document-catalog", documented.SequenceEqual(GestureActions.All.Select(a => a.Name)));
            Chk("T270-chain-editor-warn", GestureActions.MissingInputs(new[] { "record.start", "save.png" })
                .SequenceEqual(new[] { RunValue.Region, RunValue.Still })
                && GestureActions.MissingInputs(new[] { "select", "grab.still", "record.start", "save.png" })
                    .All(v => v == RunValue.None));
            Chk("T270-chain-switch-policy", GestureActions.FollowsSwitch(new[] { "select" })
                && GestureActions.FollowsSwitch(new[] { "select", "ocr.selection", "copy.clipboard" })
                && GestureActions.FollowsSwitch(new[] { "select", "record.start" })
                && GestureActions.FollowsSwitch(new[] { "select", "grab.window" })
                && !GestureActions.FollowsSwitch(new[] { "help.open" }));

            var order = new List<string>();
            var wait = new TaskCompletionSource<bool>();
            var pending = ActionChain.RunAsync(new[] { "select", "grab.still", "record.start", "edit.lastvideo" }, Array.Empty<string>(),
                FallbackPolicy.Stop, async name => { order.Add(name); if (name == "record.start") return await wait.Task; return true; });
            Chk("T270-chain-await-product", !pending.IsCompleted && order.SequenceEqual(new[] { "select", "grab.still", "record.start" }));
            wait.SetResult(true);
            Chk("T270-chain-resume-product", pending.IsCompletedSuccessfully && pending.Result
                && order.SequenceEqual(new[] { "select", "grab.still", "record.start", "edit.lastvideo" }));
            foreach (var fallback in new[] { FallbackPolicy.Stop, FallbackPolicy.SkipStep })
            {
                order.Clear();
                var failed = ActionChain.RunAsync(new[] { "record.stop", "save.png", "help.open" }, new[] { "notify.toast" }, fallback,
                    name => { order.Add(name); return Task.FromResult(name is "notify.toast" or "help.open"); });
                Chk("T270-chain-missing-precondition-" + fallback, failed.IsCompletedSuccessfully && !failed.Result
                    && order.SequenceEqual(fallback == FallbackPolicy.Stop ? new[] { "record.stop", "notify.toast" }
                        : new[] { "record.stop", "notify.toast", "save.png", "help.open" }));
            }
            order.Clear();
            var cancelled = ActionChain.RunAsync(new[] { "select", "grab.still" }, new[] { "help.open" }, FallbackPolicy.SkipStep,
                name => { order.Add(name); return Task.FromResult(name != "select"); }, () => true);
            Chk("T270-chain-cancel-stops", cancelled.IsCompletedSuccessfully && !cancelled.Result
                && order.SequenceEqual(new[] { "select", "help.open" }));
            order.Clear();
            var thrown = ActionChain.RunAsync(new[] { "select", "save.png" }, new[] { "help.open", "notify.toast" }, FallbackPolicy.Stop,
                name => { order.Add(name); throw new IOException(); });
            Chk("T270-chain-exception-onfail", thrown.IsCompletedSuccessfully && !thrown.Result
                && order.SequenceEqual(new[] { "select", "help.open", "notify.toast" }));

            var editorType = typeof(SettingsWindow).GetNestedType("ChainEditor", BindingFlags.NonPublic)!;
            var sequence = new[] { "select", "grab.still", "record.start" };
            var editor = Activator.CreateInstance(editorType, new Func<string[]>(() => sequence),
                new Func<int, IEnumerable<GestureAction>>(_ => GestureActions.All), new Action<string[]>(v => sequence = v))!;
            editorType.GetMethod("Move", flags)!.Invoke(editor, new object[] { 2, -1 });
            editorType.GetMethod("Move", flags)!.Invoke(editor, new object[] { 0, 1 });
            var dto = new GestureBindingDto { Chord = "F6", Actions = sequence };
            var roundtrip = System.Text.Json.JsonSerializer.Deserialize<GestureBindingDto>(System.Text.Json.JsonSerializer.Serialize(dto))!;
            Chk("T270-chain-editor-roundtrip", sequence.SequenceEqual(new[] { "record.start", "select", "grab.still" })
                && roundtrip.Actions.SequenceEqual(sequence) && roundtrip.ToBinding()!.Actions.SequenceEqual(sequence));
            var root = (StackPanel)editorType.GetProperty("Root")!.GetValue(editor)!;
            var chips = (WrapPanel)root.Children[0];
            Chk("T270-chain-editor-inline-warning", chips.Children[0] is Border b && b.ToolTip is string text
                && text.Contains(Loc.F(K.Hotkeys.MissingValues, Loc.T(K.Hotkeys.ValueRegion))));
            editorType.GetMethod("Move", flags)!.Invoke(editor, new object[] { 0, -1 });
            Chk("T270-chain-editor-boundary", sequence.SequenceEqual(new[] { "record.start", "select", "grab.still" }));

            ChainSettingsBackup();
            ChainControllerProducts();
            var host = new ChainHostProbe();
            var engine = new GestureEngine(host);
            engine.SetBindings(new[] { new GestureBinding
            {
                Chord = new KeyChord(ModifierKeys.None, 0x75), Trigger = TriggerKind.Chord,
                Actions = new() { "select" }, OnFail = new() { "help.open" },
            } });
            engine.OnKeyDown(0x75, ModifierKeys.None, 0);
            Chk("T270-chain-engine-delegates-single", host.Binding?.Actions.SequenceEqual(new[] { "select" }) == true
                && host.Binding.OnFail.SequenceEqual(new[] { "help.open" }) && host.DirectInvocations == 0);
        }
        catch (Exception ex) { Chk("T270-chain-suite", false, ex.ToString()); }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    static void ChainSettingsBackup()
    {
        string? saved = Environment.GetEnvironmentVariable("FC_SETTINGS_DIR");
        try
        {
            string scratch = Path.Combine(SettingsService.Dir, "chain-migration");
            Directory.CreateDirectory(scratch);
            Environment.SetEnvironmentVariable("FC_SETTINGS_DIR", scratch);
            const string json = "{\"Accent\":\"#7F75FF\",\"Gestures\":{\"Seeded\":true,\"Bindings\":[{\"Id\":\"mine\",\"Chord\":\"F6\",\"Actions\":[\"scope.select\",\"record.toggle\"],\"OnComplete\":[\"edit.lastvideo\"]}]}}";
            File.WriteAllText(SettingsService.FilePath, json);
            string backup = SettingsService.FilePath + ".bak.0";
            if (File.Exists(backup)) File.Delete(backup);
            var service = new SettingsService();
            Chk("T270-chain-settings-backup", File.ReadAllText(backup) == json
                && service.Current.Accent == "#7F75FF" && service.Current.Gestures.ChainRevision == GestureConfig.CurrentChainRevision
                && !File.ReadAllText(SettingsService.FilePath).Contains("OnComplete"));
            string contents = File.ReadAllText(SettingsService.FilePath);
            service.Reload();
            Chk("T270-chain-settings-reload-idempotent", File.ReadAllText(SettingsService.FilePath) == contents
                && File.ReadAllText(backup) == json && service.Current.Gestures.Bindings.Single().Id == "mine");
        }
        finally { Environment.SetEnvironmentVariable("FC_SETTINGS_DIR", saved); _ = new SettingsService(); }
    }

    static void ChainControllerProducts()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(CaptureController);
        var service = new SettingsService();
        service.Current.CaptureSound = false;
        service.Current.AutoSaveCopy = false;
        service.Current.RecordNeedsStartClick = false;
        service.Current.UseWgc = false;
        service.Current.ShowRecordingBorder = false;
        service.Current.FreezeOnSelect = true;
        service.Current.FreezePregrabMaxWindows = 0;
        service.Current.AutoSavePath = Path.Combine(SettingsService.Dir, "chain-images");
        var monitor = Monitors.All().First();
        var region = new RectI(monitor.Bounds.X + 4, monitor.Bounds.Y + 4, 32, 32);
        int pickerNumber = 0;
        Task<bool> TaskOf(CaptureController c) => (Task<bool>)type.GetField("_chainTask", flags)!.GetValue(c)!;
        bool WaitUntil(Func<bool> done)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!done() && clock.ElapsedMilliseconds < 4000) PumpMs(20);
            return done();
        }
        void Select(CaptureController controller, bool record, bool expectedInitialRecord)
        {
            var picker = (RegionPickerWindow)type.GetField("_picker", flags)!.GetValue(controller)!;
            int number = pickerNumber++;
            Chk("T270-chain-picker-initial-mode-" + number, picker.RecordMode == expectedInitialRecord);
            Chk("T270-chain-picker-switch-visible-" + number,
                typeof(RegionPickerWindow).GetField("_modeSwitch", flags)!.GetValue(picker) != null);
            picker.SetRecordMode(record);
            var result = (Action<RectI?, IntPtr>)typeof(RegionPickerWindow).GetField("Result", flags)!.GetValue(picker)!;
            result(region, IntPtr.Zero);
            picker.Close();
        }
        using (var c = new CaptureController(service))
        {
            c.BeginChain(new[] { "select" }, Array.Empty<string>());
            Select(c, false, false);
            Chk("T270-chain-no-product-follows-still", WaitUntil(() => TaskOf(c).IsCompleted)
                && TaskOf(c).Result && c.HasLastStill && c.State == CaptureController.Phase.Idle);
            CaptureToast.DismissCurrent();
        }
        using (var c = new CaptureController(service))
        {
            c.BeginChain(new[] { "select" }, Array.Empty<string>());
            Select(c, true, false);
            bool recording = WaitUntil(() => c.State == CaptureController.Phase.Capturing);
            bool suspended = !TaskOf(c).IsCompleted;
            c.StopRecording();
            Chk("T270-chain-no-product-follows-record", recording && suspended && WaitUntil(() => TaskOf(c).IsCompleted)
                && TaskOf(c).Result && c.HasLastVideo && !c.HasLastStill);
        }
        using (var c = new CaptureController(service))
        {
            c.BeginChain(new[] { "select", "record.start" }, Array.Empty<string>());
            Select(c, false, true);
            bool finished = WaitUntil(() => TaskOf(c).IsCompleted);
            Chk("T270-chain-explicit-product-follows-switch", finished
                && TaskOf(c).Result && !c.HasLastVideo && c.HasLastStill && c.State == CaptureController.Phase.Idle);
            CaptureToast.DismissCurrent();
        }
        using (var c = new CaptureController(service))
        {
            c.BeginChain(new[] { "select", "grab.still", "record.start", "save.png" }, Array.Empty<string>());
            Select(c, false, false);
            bool recording = WaitUntil(() => c.State == CaptureController.Phase.Capturing);
            bool still = c.HasLastStill;
            string folder = service.Current.AutoSavePath;
            int before = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.png").Length : 0;
            c.StopRecording();
            Chk("T270-chain-multi-product", recording && still && WaitUntil(() => TaskOf(c).IsCompleted)
                && TaskOf(c).Result && c.HasLastVideo && Directory.GetFiles(folder, "*.png").Length == before + 1);
            Chk("T270-chain-explicit-consumer-no-toast", typeof(CaptureToast).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) == null);
        }
        using (var c = new CaptureController(service))
        {
            var chain = new[] { "select", "grab.still", "edit.result" };
            c.BeginChain(chain, Array.Empty<string>());
            Select(c, true, false);
            bool recording = WaitUntil(() => c.State == CaptureController.Phase.Capturing);
            var original = TaskOf(c);
            c.BeginChain(chain, Array.Empty<string>());
            Chk("T271-switched-screenshot-key-stops-and-edits-video", recording && WaitUntil(() => original.IsCompleted)
                && original.Result && c.State == CaptureController.Phase.Editing && !c.HasLastStill && !c.HasLastVideo);
            type.GetMethod("CloseEditor", flags)!.Invoke(c, null);
        }
        foreach (bool dynamicEditor in new[] { true, false })
        {
            using var c = new CaptureController(service);
            var retained = new CapturedSequence
            {
                Frames = new List<CapturedFrame> { Solid(24, 24, 10, 20, 30) },
                Region = region, MonitorBounds = monitor.Bounds, MonitorDevice = monitor.DeviceName,
            };
            type.GetField("_lastSequence", flags)!.SetValue(c, retained);
            c.BeginChain(new[] { "select", "record.start", dynamicEditor ? "edit.result" : "edit.lastvideo" }, Array.Empty<string>());
            Select(c, false, true);
            bool finished = WaitUntil(() => TaskOf(c).IsCompleted);
            Chk("T271-switched-record-key-editor-" + dynamicEditor, finished && TaskOf(c).Result == dynamicEditor
                && c.HasLastStill && c.HasLastVideo
                && ReferenceEquals(type.GetField("_lastSequence", flags)!.GetValue(c), retained)
                && c.State == (dynamicEditor ? CaptureController.Phase.Editing : CaptureController.Phase.Idle));
            type.GetMethod("CloseEditor", flags)!.Invoke(c, null);
            CaptureToast.DismissCurrent();
        }
        using (var c = new CaptureController(service))
        {
            var chain = new[] { "select", "grab.still", "record.start" };
            c.BeginChain(chain, Array.Empty<string>());
            Select(c, true, false);
            bool first = WaitUntil(() => c.State == CaptureController.Phase.Capturing);
            c.BeginChain(chain, Array.Empty<string>());
            bool second = WaitUntil(() => c.State == CaptureController.Phase.Capturing);
            bool pending = !TaskOf(c).IsCompleted;
            c.BeginChain(chain, Array.Empty<string>());
            Chk("T271-switch-preserves-second-recording", first && second && pending && WaitUntil(() => TaskOf(c).IsCompleted)
                && TaskOf(c).Result && c.HasLastVideo && !c.HasLastStill);
        }
        using (var c = new CaptureController(service))
        {
            c.BeginChain(new[] { "record.start" }, Array.Empty<string>());
            Chk("T270-chain-start-requires-region", TaskOf(c).IsCompletedSuccessfully && !TaskOf(c).Result
                && c.State == CaptureController.Phase.Idle && !c.HasLastVideo);
            c.BeginChain(new[] { "record.stop" }, Array.Empty<string>());
            Chk("T270-chain-stop-requires-recording", TaskOf(c).IsCompletedSuccessfully && !TaskOf(c).Result);
        }
        using (var c = new CaptureController(service))
        {
            var chain = new[] { "select", "record.toggle", "edit.lastvideo" };
            c.BeginChain(chain, Array.Empty<string>());
            Select(c, true, true);
            bool recording = WaitUntil(() => c.State == CaptureController.Phase.Capturing);
            var original = TaskOf(c);
            c.BeginChain(chain, Array.Empty<string>());
            Chk("T270-chain-toggle-resumes-owner", recording && WaitUntil(() => original.IsCompleted)
                && original.Result && c.State == CaptureController.Phase.Editing && !c.HasLastVideo);
            type.GetMethod("CloseEditor", flags)!.Invoke(c, null);
        }
        using (var c = new CaptureController(service))
        {
            c.BeginChain(new[] { "scope.stored", "record.start" }, Array.Empty<string>());
            bool recording = WaitUntil(() => c.State == CaptureController.Phase.Capturing);
            c.BeginChain(new[] { "record.stop", "edit.lastvideo" }, Array.Empty<string>());
            Chk("T270-chain-stop-binding-tail", recording && WaitUntil(() => c.State == CaptureController.Phase.Editing)
                && !c.HasLastVideo);
            type.GetMethod("CloseEditor", flags)!.Invoke(c, null);
        }
        service.Current.RecordNeedsStartClick = true;
        using (var c = new CaptureController(service))
        {
            c.BeginChain(new[] { "select", "record.start", "record.start" }, Array.Empty<string>());
            Select(c, true, true);
            bool armed = WaitUntil(() => RecordPanelWindow.Current != null);
            void StartPanel()
            {
                var blocks = new List<DependencyObject>();
                CollectTyped(RecordPanelWindow.Current!, typeof(Border), blocks);
                var start = blocks.OfType<Border>().Single(b => b.Child is TextBlock t && t.Text == Loc.T(K.Actions.RecordStartLabel));
                start.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                    0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            }
            var firstPanel = RecordPanelWindow.Current;
            if (armed) StartPanel();
            bool firstRecording = WaitUntil(() => c.State == CaptureController.Phase.Capturing);
            c.StopRecording();
            bool secondArmed = WaitUntil(() => RecordPanelWindow.Current != null && !ReferenceEquals(RecordPanelWindow.Current, firstPanel));
            PumpMs(Fluent.Motion.ExitMs + 80);
            bool pending = !TaskOf(c).IsCompleted;
            if (secondArmed) StartPanel();
            bool secondRecording = WaitUntil(() => c.State == CaptureController.Phase.Capturing);
            c.StopRecording();
            Chk("T270-chain-repeated-record-completions", armed && firstRecording && secondArmed && pending && secondRecording
                && WaitUntil(() => TaskOf(c).IsCompleted) && TaskOf(c).Result && c.HasLastVideo);
        }
        using (var c = new CaptureController(service))
        {
            c.BeginChain(new[] { "select", "record.start" }, Array.Empty<string>());
            Select(c, true, true);
            bool armed = WaitUntil(() => RecordPanelWindow.Current != null);
            var panel = RecordPanelWindow.Current;
            var blocks = new List<DependencyObject>();
            if (panel != null) CollectTyped(panel, typeof(Border), blocks);
            var cancel = blocks.OfType<Border>().FirstOrDefault(b => b.Child is TextBlock t && t.Text == Loc.T(K.Common.Cancel));
            cancel?.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            Chk("T270-chain-armed-cancel", armed && cancel?.IsVisible == false && WaitUntil(() => TaskOf(c).IsCompleted)
                && !TaskOf(c).Result && !c.HasLastVideo && c.State == CaptureController.Phase.Idle);
        }
        using (var c = new CaptureController(service))
        {
            var existing = Application.Current.Windows.OfType<PinWindow>().ToHashSet();
            c.BeginChain(new[] { "pin.selection" }, Array.Empty<string>());
            var still = (LastStill)type.GetField("_lastStill", flags)!.GetValue(c)!;
            Chk("T270-chain-pin-uses-current-region", TaskOf(c).IsCompletedSuccessfully && TaskOf(c).Result
                && still.TryGet(out var frame, out var captured, out _) && frame.W == region.W && frame.H == region.H
                && captured.Equals(region));
            foreach (var pin in Application.Current.Windows.OfType<PinWindow>().Where(p => !existing.Contains(p)).ToArray()) pin.Close();
        }
        using (var c = new CaptureController(service))
        {
            c.BeginChain(new[] { "select" }, Array.Empty<string>());
            Select(c, false, false);
            Chk("T270-chain-select-alone-keeps-switch", WaitUntil(() => TaskOf(c).IsCompleted)
                && TaskOf(c).Result && c.HasLastStill && !c.HasLastVideo && c.State == CaptureController.Phase.Idle);
            CaptureToast.DismissCurrent();
        }
    }

    sealed class ChainHostProbe : IChainInvoker
    {
        public GestureBinding? Binding;
        public int DirectInvocations;
        public void BeginBinding(GestureBinding binding) { }
        public bool Invoke(string name) { DirectInvocations++; return false; }
        public bool InvokeChain(GestureBinding binding) { Binding = binding; return true; }
    }
}

