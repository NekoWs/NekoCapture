using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NekoCapture.Capture;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;

static partial class SelfTest
{
    static void ParallelCaptureChecks()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var settingsProperty = typeof(AppHost).GetProperty("Settings")!;
        var previousSettings = settingsProperty.GetValue(AppHost.Current);
        var settings = new SettingsService(); settingsProperty.SetValue(AppHost.Current, settings);
        settings.Current.UseWgc = false; settings.Current.FollowMonitorRefresh = false; settings.Current.Fps = 10;
        settings.Current.RecordNeedsStartClick = true; settings.Current.CaptureSound = false;
        settings.Current.AutoSaveCopy = false; settings.Current.ShowRecordingBorder = false;
        settings.Current.FreezeOnSelect = false; settings.Current.CaptureSelf = false;
        var monitor = Monitors.All().First(); var region = new RectI(monitor.Bounds.X + 20, monitor.Bounds.Y + 20, 48, 48);
        using var root = new CaptureController(settings);
        object? Field(CaptureController controller, string field) => typeof(CaptureController).GetField(field, flags)!.GetValue(controller);
        Task<bool> Running(CaptureController controller) => (Task<bool>)Field(controller, "_chainTask")!;
        bool Wait(Func<bool> condition)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < 5000) PumpMs(10);
            return condition();
        }
        void Choose(CaptureController controller, bool record)
        {
            var picker = (RegionPickerWindow)Field(controller, "_picker")!; picker.SetRecordMode(record);
            var selected = (Action<RectI?, IntPtr>)typeof(RegionPickerWindow).GetField("Result", flags)!.GetValue(picker)!;
            selected(region, IntPtr.Zero); picker.Close();
        }
        RecordPanelWindow Panel(CaptureController controller) => (RecordPanelWindow)Field(controller, "_recordPanel")!;
        var graph = new BehaviorGraph(); var select = graph.Add(GestureActions.Select);
        var record = graph.Add(GestureActions.RecordStart);
        record.Branches[BehaviorPorts.Done].Add(graph.Add(GestureActions.EditLastVideo).Id);
        select.Branches[BehaviorPorts.Record].Add(record.Id);
        select.Branches[BehaviorPorts.Still].Add(graph.Add(GestureActions.GrabStill).Id);
        var entry = new BehaviorFlow { Steps = new() { select.Id } }; graph.Flows.Add(entry);
        try
        {
            Chk("self-capture-default-is-disabled", !new AppSettings().CaptureSelf);
            root.BeginBehavior(BehaviorFixtureMode(graph, entry.Id, BehaviorPorts.Record), entry.Id, "outer");
            Wait(() => Field(root, "_picker") != null); Choose(root, true);
            Wait(() => Field(root, "_recordPanel") != null);
            var firstPanel = Panel(root);
            typeof(RecordPanelWindow).GetMethod("StartRequested", flags)!.Invoke(firstPanel, null);
            Chk("parallel-first-recording-starts", Wait(() => root.State == CaptureController.Phase.Capturing));
            root.BeginBehavior(BehaviorFixtureMode(graph, entry.Id, BehaviorPorts.Record), entry.Id, "inner");
            var second = (CaptureController)Field(root, "_nextCapture")!;
            Wait(() => Field(second, "_picker") != null); Choose(second, true);
            Wait(() => Field(second, "_recordPanel") != null);
            var secondPanel = Panel(second);
            Chk("parallel-recording-keeps-first-panel-and-backend", root.State == CaptureController.Phase.Capturing
                && firstPanel.IsLoaded && !ReferenceEquals(firstPanel, secondPanel));
            typeof(RecordPanelWindow).GetMethod("StartRequested", flags)!.Invoke(secondPanel, null);
            Chk("parallel-two-recordings-run-together", Wait(() => second.State == CaptureController.Phase.Capturing)
                && root.State == CaptureController.Phase.Capturing && firstPanel.IsLoaded && secondPanel.IsLoaded);
            PumpMs(180);
            root.BeginBehavior(BehaviorFixtureMode(graph, entry.Id, BehaviorPorts.Record), entry.Id, "outer");
            Chk("parallel-binding-stops-only-its-recording", Wait(() => root.State == CaptureController.Phase.Editing)
                && second.State == CaptureController.Phase.Capturing && secondPanel.IsLoaded);
            var firstEditor = (EditorWindow)Field(root, "_editor")!;
            typeof(RecordPanelWindow).GetMethod("StopRequested", flags)!.Invoke(secondPanel, null);
            Chk("parallel-panels-stop-their-own-session", Wait(() => second.State == CaptureController.Phase.Editing)
                && firstEditor.IsLoaded && root.State == CaptureController.Phase.Editing);
            var firstContext = (RunContext)Field(root, "_chainContext")!;
            Chk("parallel-editor-keeps-independent-frame-lease", firstContext.Video!.Frames[0].W == region.W);
            root.BeginBehavior(BehaviorFixtureMode(graph, entry.Id, BehaviorPorts.Still), entry.Id, "editor-shot");
            var third = (CaptureController)Field(second, "_nextCapture")!;
            Chk("editor-capture-opens-picker-with-editors-preserved", Wait(() => Field(third, "_picker") != null)
                && firstEditor.IsLoaded && Field(second, "_editor") is EditorWindow { IsLoaded: true });
            Choose(third, false);
            Chk("editor-capture-produces-still-without-ending-edit", Wait(() => Running(third).IsCompleted)
                && Running(third).Result && root.HasLastStill && firstEditor.IsLoaded);
            firstEditor.CloseByController(); Wait(() => Running(root).IsCompleted);
            Chk("parallel-child-editor-blocks-idle-suspension", root.State == CaptureController.Phase.Idle && !root.CanSuspend);
            ((EditorWindow)Field(second, "_editor")!).CloseByController();
            Chk("parallel-all-sessions-can-become-idle", Wait(() => Running(second).IsCompleted) && root.CanSuspend);
            settings.Current.CaptureSelf = true;
            var affinity = typeof(SettingsWindow).GetMethod("ApplyCaptureAffinity", BindingFlags.Static | BindingFlags.NonPublic)!;
            using var outline = new OutlineLease();
            foreach (Window window in Application.Current.Windows) affinity.Invoke(null, new object[] { window });
            Chk("self-capture-includes-outline", !outline.Window.EnsureExcludedFromCapture()
                && GetWindowDisplayAffinity(new WindowInteropHelper(outline.Window).Handle, out uint included) && included == 0);
            settings.Current.CaptureSelf = false;
            affinity.Invoke(null, new object[] { outline.Window });
            Chk("self-capture-off-restores-outline-exclusion", outline.Window.EnsureExcludedFromCapture()
                && GetWindowDisplayAffinity(new WindowInteropHelper(outline.Window).Handle, out uint excluded) && excluded == 0x11);
        }
        catch (Exception error) { Chk("parallel-capture-exception", false, error.ToString()); }
        finally
        {
            settingsProperty.SetValue(AppHost.Current, previousSettings);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed class OutlineLease : IDisposable
    {
        /// <summary>具有捕获策略的真实轮廓窗口。</summary>
        internal readonly RegionOutlineWindow Window = new();
        /// <inheritdoc/>
        public void Dispose() => Window.Close();
    }
}
