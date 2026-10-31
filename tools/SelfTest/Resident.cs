using System.IO;
using System.Reflection;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Services;
using GE = GestureCore;

static partial class SelfTest
{
    static void ResidentChecks()
    {
        try
        {
            Chk("resident-modifier-press-includes-current-event", GestureModifiers.AfterKeyboardEvent(GE.ModifierKeys.None, 0xA2, true, _ => false) == GE.ModifierKeys.Control);
            Chk("resident-modifier-release-clears-stale-async-state", GestureModifiers.AfterKeyboardEvent(GE.ModifierKeys.Control, 0xA2, false, _ => false) == GE.ModifierKeys.None);
            Chk("resident-modifier-release-keeps-other-side", GestureModifiers.AfterKeyboardEvent(GE.ModifierKeys.Control, 0xA2, false, key => key == 0xA3) == GE.ModifierKeys.Control);
            Chk("resident-main-key-keeps-modifier-snapshot", GestureModifiers.AfterKeyboardEvent(GE.ModifierKeys.Alt, 0x41, false, _ => false) == GE.ModifierKeys.Alt);
            using (var input = new HotkeyManager { KeySink = (_, _, _, _) => false })
            {
                var claimed = (HashSet<uint>)typeof(HotkeyManager).GetField("_captureKeyUps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
                claimed.Add(135); input.Install(); input.Install();
                Chk("resident-input-reinstall-preserves-captured-release", claimed.Contains(135));
                input.Uninstall();
                Chk("resident-input-uninstall-releases-captured-state", claimed.Count == 0 && !input.Installed);
            }
            var source = new CapturedFrame { W = 2, H = 1, TimeMs = 123, Bgra = new byte[] { 0, 0, 255, 255, 255, 0, 0, 0 } };
            var preview = BgraImage.Preview(source, 1, 1);
            Chk("resident-preview-alpha-weighted", preview.Bgra.SequenceEqual(new byte[] { 0, 0, 255, 128 }) && preview.TimeMs == 123);
            var small = BgraImage.Preview(source, 56, 44);
            Chk("resident-preview-no-upscale-or-retained-array", small.W == 2 && small.H == 1 && !ReferenceEquals(small.Bgra, source.Bgra));
            var large = new CapturedFrame { W = 3840, H = 2160, Bgra = new byte[3840 * 2160 * 4] };
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var tiny = BgraImage.Preview(large, 56, 44);
            long cost = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Chk("resident-preview-bounded-allocation", tiny.W <= 56 && tiny.H <= 44 && cost < 32 * 1024, $"allocated={cost}");

            var last = new LastStill(); int reads = 0;
            var region = new RectI(-300, 20, 2, 1);
            last.Restore(() => { reads++; return source; }, region, "display", true);
            Chk("resident-still-lazy-metadata", !last.Empty && reads == 0 && last.LoadedFrame == null && last.WindowCapture);
            last.TryGetMetadata(out var metadata, out string device);
            Chk("resident-still-metadata-no-decode", metadata.Equals(region) && device == "display" && reads == 0);
            last.TryGet(out var loaded, out _, out _); last.TryGet(out _, out _, out _);
            Chk("resident-still-decode-once", reads == 1 && ReferenceEquals(loaded, source));
            last.Clear();
            Chk("resident-still-clear-forgets-loader", last.Empty && !last.WindowCapture);

            var settings = new SettingsService();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            LastStill Still(CaptureController controller) => (LastStill)typeof(CaptureController).GetField("_lastStill", flags)!.GetValue(controller)!;
            CapturedSequence? Video(CaptureController controller) => (CapturedSequence?)typeof(CaptureController).GetField("_lastSequence", flags)!.GetValue(controller);
            string path;
            using (var spool = new FrameSpool())
            {
                spool.Add(source);
                spool.Add(new CapturedFrame { W = 2, H = 1, TimeMs = 223, Bgra = source.Bgra });
                var frames = spool.OpenReader(); path = frames.Path;
                var sequence = new CapturedSequence { Frames = frames, Region = region, MonitorBounds = region, MonitorDevice = "display", Fps = 10 };
                allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) _ = sequence.TotalMs;
                Chk("resident-duration-no-pixel-allocation", GC.GetAllocatedBytesForCurrentThread() - allocated < 1024);
                using var controller = new CaptureController(settings);
                Still(controller).Set(source, region, "display", true);
                typeof(CaptureController).GetField("_lastSequence", flags)!.SetValue(controller, sequence);
                controller.SaveResidentSessionAsync().GetAwaiter().GetResult();
                Chk("resident-session-cache-written", File.Exists(Path.Combine(SettingsService.Dir, "resident-session.json")) && File.Exists(path));
                string blocked = Path.Combine(SettingsService.Dir, "resident-session.json.tmp");
                Directory.CreateDirectory(blocked); bool failed = false;
                int cached = Directory.GetFiles(SettingsService.Dir, "resident-still-*.fcf").Length;
                try { controller.SaveResidentSessionAsync().GetAwaiter().GetResult(); }
                catch (UnauthorizedAccessException) { failed = true; }
                finally { Directory.Delete(blocked); }
                Chk("resident-session-save-failure-keeps-product", failed && controller.HasLastStill && controller.HasLastVideo
                    && Directory.GetFiles(SettingsService.Dir, "resident-still-*.fcf").Length == cached);
            }
            Chk("resident-session-video-retained-after-dispose", File.Exists(path));
            using (var restored = new CaptureController(settings))
            {
                restored.RestoreResidentSession();
                Chk("resident-session-restore-does-not-load-still", restored.HasLastStill && Still(restored).LoadedFrame == null);
                Still(restored).TryGet(out var actual, out var actualRegion, out var actualDevice);
                Chk("resident-session-still-roundtrip", actual.Bgra.SequenceEqual(source.Bgra) && actual.TimeMs == source.TimeMs
                    && actualRegion.Equals(region) && actualDevice == "display" && Still(restored).WindowCapture);
                var video = Video(restored)!;
                Chk("resident-session-video-index-roundtrip", video.Frames is FrameSpoolList && video.Frames.Count == 2 && video.Fps == 10 && video.Region.Equals(region));
                Chk("resident-session-video-pixels-roundtrip", video.Frames[1].Bgra.SequenceEqual(source.Bgra) && video.Frames[1].TimeMs == 223);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
                TempFiles.SweepStale(CaptureController.ResidentFiles()); FrameSpool.CleanupStale(CaptureController.ResidentFiles());
                Chk("resident-session-preserves-referenced-expired-video", File.Exists(path));
            }
            string[] oldStill = Directory.GetFiles(SettingsService.Dir, "resident-still-*.fcf");
            using (var empty = new CaptureController(settings)) empty.SaveResidentSessionAsync().GetAwaiter().GetResult();
            Chk("resident-session-replacement-cleans-owned-cache", !File.Exists(path) && oldStill.All(file => !File.Exists(file)));
            using (var empty = new CaptureController(settings))
            {
                empty.RestoreResidentSession();
                Chk("resident-session-cleared-products-stay-empty", !empty.HasLastStill && !empty.HasLastVideo);
            }
            File.WriteAllText(Path.Combine(SettingsService.Dir, "resident-session.json"), "bad json");
            using (var damaged = new CaptureController(settings))
            {
                damaged.RestoreResidentSession();
                Chk("resident-session-damaged-metadata-safe", damaged.CanSuspend && !damaged.HasLastStill && !damaged.HasLastVideo);
            }
            using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var task = BackgroundWork.Run(() => { started.Set(); release.Wait(); return 7; });
            started.Wait(); Chk("resident-background-keeps-worker-alive", BackgroundWork.Busy);
            release.Set(); Chk("resident-background-releases-on-success", task.GetAwaiter().GetResult() == 7 && !BackgroundWork.Busy);
            try { BackgroundWork.Run<int>(() => throw new IOException("test")).GetAwaiter().GetResult(); } catch (IOException) { }
            Chk("resident-background-releases-on-failure", !BackgroundWork.Busy);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { BackgroundWork.Run(() => 1, cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            Chk("resident-background-releases-before-start-cancellation", !BackgroundWork.Busy);
            var first = BackgroundWork.Hold(); var second = BackgroundWork.Hold();
            first.Dispose(); first.Dispose(); bool pending = BackgroundWork.Busy; second.Dispose();
            Chk("resident-background-nested-leases-release-once", pending && !BackgroundWork.Busy);
        }
        catch (Exception error) { Chk("resident-checks-exception", false, error.ToString()); }
    }

    static int ResidentFixture(string directory)
    {
        Environment.SetEnvironmentVariable("FC_SETTINGS_DIR", Path.GetFullPath(directory));
        var settings = new SettingsService();
        var monitor = Monitors.All().First();
        var graph = new BehaviorGraph();
        BehaviorFlow Flow(params string[] actions)
        {
            var flow = new BehaviorFlow { Steps = actions.Select(action => graph.Add(action).Id).ToList() };
            graph.Flows.Add(flow); return flow;
        }
        GestureBindingDto Binding(string id, string chord, GE.TriggerKind trigger, params string[] actions) => new()
        {
            Id = id, Name = id, Chord = chord, Trigger = trigger.ToString(), LongPressMs = 600, DoubleClickMs = 150,
            EntryId = Flow(actions).Id
        };
        var bindings = new List<GestureBindingDto>
        {
            Binding("still", "Ctrl+Alt+Shift+F24", GE.TriggerKind.Tap, GestureActions.RequireStored, GestureActions.GrabStill, GestureActions.SavePng),
            Binding("hold", "Ctrl+Alt+Shift+F24", GE.TriggerKind.Hold, GestureActions.RequireStored, GestureActions.RecordStart),
            Binding("release", "Ctrl+Alt+Shift+F24", GE.TriggerKind.HoldRelease, GestureActions.RecordStop),
            Binding("save", "Ctrl+Alt+Shift+F23", GE.TriggerKind.Chord, GestureActions.RequireStored, GestureActions.GrabStill, GestureActions.SavePng),
            Binding("edit-video", "Ctrl+Alt+Shift+F22", GE.TriggerKind.Chord, GestureActions.EditLastVideo)
        };
        settings.Mutate(value =>
        {
            value.Gestures = new GestureConfig { Enabled = true, Seeded = true, ChainRevision = GestureConfig.CurrentChainRevision,
                Bindings = bindings, Graph = graph };
            value.StartupToast = false; value.FirstRunHintShown = true; value.RunAtStartup = false; value.RunAsAdmin = false;
            value.CaptureSound = false; value.AlwaysShowRegionOutline = false; value.ShowRecordingBorder = false;
            value.RegionMonitorId = monitor.DeviceName; value.RegionX = monitor.Bounds.X + 20; value.RegionY = monitor.Bounds.Y + 20;
            value.RegionW = 64; value.RegionH = 48; value.RecordNeedsStartClick = false;
            value.UseWgc = false; value.Fps = 20; value.FollowMonitorRefresh = false;
            value.AutoSaveCopy = false; value.AutoSavePath = Path.Combine(directory, "products");
        });
        if (settings.Current.Gestures.ToBindings().Count != bindings.Count) throw new InvalidOperationException("Invalid resident fixture bindings");
        BgraImage.SavePng(new CapturedFrame { W = 64, H = 48, Bgra = MockScreen(64, 48) }, Path.Combine(directory, "input.png"));
        Console.WriteLine("RESIDENT-FIXTURE PASS"); return 0;
    }
}
