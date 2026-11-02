using NekoCapture.Capture;
using NekoCapture.Core;
using NekoCapture.Overlays;

namespace NekoCapture.Services;

/// <summary>共享行为图的捕获、事件等待与运行隔离。</summary>
public sealed partial class CaptureController
{
    string _behaviorBinding = "";
    TaskCompletionSource<string>? _editorEvent;
    bool? _consumeStop;

    /// <summary>执行默认捕获菜单项当前配置的行为；菜单项已删除或禁用时不执行。</summary>
    public void BeginImmediateCapture()
    {
        var config = _settings.Current.Gestures;
        var binding = config.Bindings.FirstOrDefault(b => b.Id == "tray.menu." + TrayDefaults.Capture && b.Enabled);
        if (config.Enabled && binding != null) BeginBehavior(config.BindingGraph(binding), binding.EntryId, binding.Id);
    }

    /// <summary>执行行为入口；同一触发再次执行或停止入口结束当前录制。</summary>
    /// <param name="graph">独立的运行快照。</param>
    /// <param name="entryId">行为入口。</param>
    /// <param name="bindingId">启动绑定的稳定标识。</param>
    public void BeginBehavior(BehaviorGraph graph, string entryId, string bindingId)
    {
        if (_disposed || _updateReserved || !graph.IsValid(entryId) || graph.Flow(entryId) == null) return;
        BehaviorCaptureTarget(graph, entryId, bindingId).BeginBehaviorCore(graph, entryId, bindingId);
    }

    private void BeginBehaviorCore(BehaviorGraph graph, string entryId, string bindingId)
    {
        if (_chainTask is { IsCompleted: false })
        {
            bool stop = bindingId == _behaviorBinding || EntryStopsRecording(graph, entryId);
            if (stop && State is Phase.Starting or Phase.Capturing && _recordStopTask == null)
            {
                var active = _chainTask;
                var context = _chainContext;
                var stopping = StopRecordingAsync();
                if (bindingId != _behaviorBinding && EntryStopsRecording(graph, entryId) && context != null)
                    ResumeBehaviorAfterStop(active, stopping, graph, entryId, bindingId, context);
            }
            else if (stop && _recordCompletion != null && State == Phase.Idle)
            {
                if (_chainContext != null) _chainContext.Cancelled = true;
                DismissRecordPanel();
            }
            return;
        }
        _chainTask = RunBehaviorAsync(graph, entryId, bindingId);
    }

    static bool EntryStopsRecording(BehaviorGraph graph, string entryId)
    {
        var seen = new HashSet<string>();
        bool Stops(IEnumerable<string> ids)
        {
            foreach (string id in ids)
            {
                if (!seen.Add(id) || graph.Node(id) is not { } node) continue;
                if (node.Action is GestureActions.RecordStop or GestureActions.RecordToggle) return true;
                if (node.Action == GestureActions.CallBehavior && graph.Flow(node.TargetId) is { } call && Stops(call.Steps)) return true;
            }
            return false;
        }
        return graph.Flow(entryId) is { } flow && Stops(flow.Steps);
    }

    async void ResumeBehaviorAfterStop(Task<bool> active, Task<bool> stopping, BehaviorGraph graph, string entryId, string bindingId,
        RunContext context)
    {
        using var videoLease = context.HoldVideo();
        try
        {
            bool stopped = await stopping;
            await active;
            if (!_disposed) _chainTask = RunBehaviorAsync(graph, entryId, bindingId, context, stopped);
        }
        catch (Exception ex) { Trace.Log($"stop behavior: {ex.Message}"); }
    }

    async Task<bool> RunBehaviorAsync(BehaviorGraph graph, string entryId, string bindingId,
        RunContext? initial = null, bool? consumeStop = null)
    {
        var stored = StoredRegion();
        var context = initial ?? new RunContext { Region = stored?.Bounds, DeviceName = stored?.DeviceName ?? "" };
        using var videoLease = context.HoldVideo();
        _chainContext = context;
        _behaviorBinding = bindingId;
        _consumeStop = consumeStop;
        try
        {
            return await BehaviorRunner.RunAsync(graph, entryId, async (node, emit) =>
            {
                string port = await RunBehaviorNodeAsync(node, context, emit);
                context.LastEvent = port;
                return port;
            }, () => _disposed);
        }
        finally
        {
            _selectionCompletion = _recordCompletion = _scrollCompletion = _delayCompletion = null;
            _editorEvent = null;
            _chainContext = null;
            _behaviorBinding = "";
        }
    }

    async Task<string> RunBehaviorNodeAsync(BehaviorNode node, RunContext context, Func<string, Task<bool>> emit)
    {
        if (_disposed) return BehaviorPorts.Cancel;
        string action = node.Action;
        if (action != GestureActions.NotifyToast) context.FailureNotice = "";
        bool ok;
        if (_consumeStop is { } stopSucceeded && action is GestureActions.RecordStop or GestureActions.RecordToggle)
        {
            _consumeStop = null;
            return stopSucceeded ? BehaviorPorts.Done : BehaviorPorts.Failed;
        }
        if (action is GestureActions.Select or GestureActions.SelectOnly)
        {
            bool record = node.RecordMode;
            ok = await RunChainStepAsync(GestureActions.Select, context, record,
                action == GestureActions.Select && node.ShowModeSwitch);
            bool cancelled = context.Cancelled;
            context.Cancelled = false;
            if (!ok) return cancelled ? BehaviorPorts.Cancel : BehaviorPorts.Failed;
            return action == GestureActions.SelectOnly ? BehaviorPorts.Done
                : context.RecordMode ? BehaviorPorts.Record : BehaviorPorts.Still;
        }
        if (action == GestureActions.ClearRegion)
        {
            await ClearStoredRegionAsync();
            context.Region = null; context.Window = IntPtr.Zero;
            context.Frozen = context.FrozenWindow = null;
            return BehaviorPorts.Done;
        }
        if (action == GestureActions.RequireStored)
        {
            var stored = StoredRegion();
            if (stored == null) return BehaviorPorts.Failed;
            context.Region = stored.Bounds; context.DeviceName = stored.DeviceName;
            context.Window = IntPtr.Zero; context.Frozen = context.FrozenWindow = null;
            context.Selected = false;
            return BehaviorPorts.Done;
        }
        if (action == GestureActions.SaveResult)
        {
            ok = context.LatestProduct == RunValue.Video
                ? context.Video != null && await SaveService.SaveSequenceAsync(context.Video)
                : context.Still != null && await SaveService.SavePngAsync(context.Still);
            return ok ? BehaviorPorts.Done : BehaviorPorts.Failed;
        }
        if (action == GestureActions.CopyLast && context.LatestProduct == RunValue.Video && context.Text == null)
        {
            if (context.Video is not { } video) return BehaviorPorts.Failed;
            var frames = BakedFrameList.RetainRange(video.Frames, 0, video.Frames.Count);
            using var reader = frames as IDisposable;
            byte[] gif = await BackgroundWork.Run(() => Encoding.GifEncoder.Encode(frames,
                Enumerable.Range(0, frames.Count).Select(i => i + 1 < frames.Count
                    ? Math.Max(10, (int)(FrameMetadata.TimeAt(frames, i + 1) - FrameMetadata.TimeAt(frames, i)))
                    : 1000 / Math.Max(1, video.Fps)).ToArray()));
            var output = new CapturedSequence { Frames = frames, Region = video.Region,
                MonitorBounds = video.MonitorBounds, MonitorDevice = video.MonitorDevice, Fps = video.Fps };
            return await Encoding.ClipboardService.SetCaptureAsync(output, gif, 0) ? BehaviorPorts.Done : BehaviorPorts.Failed;
        }
        if (action == GestureActions.NotifyToast && (context.Still == null
            || context.LastEvent is BehaviorPorts.Failed or BehaviorPorts.Cancel))
        {
            NoticeToast.Show(context.LastEvent == BehaviorPorts.Failed && context.FailureNotice.Length > 0 ? context.FailureNotice : Loc.T(context.LastEvent switch
            {
                BehaviorPorts.Failed => K.Behaviors.ActionFailed,
                BehaviorPorts.Cancel => K.Behaviors.ActionCancelled,
                _ => context.LatestProduct == RunValue.Video ? K.Behaviors.RecordingReady : K.Behaviors.ActionDone,
            }));
            context.FailureNotice = "";
            return BehaviorPorts.Done;
        }
        if (action is GestureActions.EditResult or GestureActions.EditLastStill or GestureActions.EditLastVideo)
            return await RunBehaviorEditorAsync(action, context);
        ok = await RunChainStepAsync(action, context, false, recordingStarted: async () =>
        {
            if (!await emit(BehaviorPorts.Started)) throw new InvalidOperationException("Recording start branch failed");
        });
        bool wasCancelled = context.Cancelled;
        context.Cancelled = false;
        Trace.Log($"behavior {action} ok={ok}");
        return ok ? BehaviorPorts.Done : wasCancelled ? BehaviorPorts.Cancel : BehaviorPorts.Failed;
    }

    async Task<string> RunBehaviorEditorAsync(string action, RunContext context)
    {
        bool video = action == GestureActions.EditLastVideo
            || action == GestureActions.EditResult && context.LatestProduct == RunValue.Video;
        if (video && context.Video is { } sequence && State == Phase.Idle)
        {
            if (ReferenceEquals(_lastSequence, sequence)) _lastSequence = null;
            if (!await OpenEditorAsync(sequence)) return BehaviorPorts.Failed;
        }
        else if (!await RunChainStepAsync(action, context, false)) return BehaviorPorts.Failed;
        if (_editor is not { } editor) return BehaviorPorts.Failed;
        var done = _editorEvent = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.ManagedOutput = true;
        bool outputReady = false;
        async void Saved(CapturedSequence output)
        {
            outputReady = true;
            bool adopted = false;
            try
            {
                CapturedFrame? frame = !video && output.Frames.Count > 0
                    ? await BackgroundWork.Run(() => output.Frames[0]) : null;
                if (_disposed || done.Task.IsCompleted) return;
                if (!video && frame == null) throw new InvalidOperationException("Editor output has no still frame");
                context.LatestProduct = video ? RunValue.Video : RunValue.Still;
                context.Text = null;
                if (video) { context.Video = output; adopted = true; }
                else
                {
                    context.Still = frame!;
                    context.StillRegion = output.Region; context.StillDevice = output.MonitorDevice;
                    _lastStill.Set(frame!, context.StillRegion, context.StillDevice, context.WindowCapture);
                }
                if (ReferenceEquals(_editor, editor)) CloseEditor();
                done.TrySetResult(BehaviorPorts.Saved);
            }
            catch (Exception ex)
            {
                Trace.Log("editor output: " + ex);
                if (ReferenceEquals(_editor, editor)) CloseEditor();
                done.TrySetResult(BehaviorPorts.Failed);
            }
            finally { if (!adopted) ReleaseFrames(output.Frames); }
        }
        void Closed()
        {
            if (outputReady) return;
            if (video) context.Video = null;
            done.TrySetResult(BehaviorPorts.Closed);
        }
        void Failed() { outputReady = true; CloseEditor(); done.TrySetResult(BehaviorPorts.Failed); }
        editor.OutputReady += Saved;
        editor.OutputFailed += Failed;
        editor.ClosedByUser += Closed;
        try { return await done.Task; }
        finally
        {
            editor.OutputReady -= Saved;
            editor.OutputFailed -= Failed;
            editor.ClosedByUser -= Closed;
            _editorEvent = null;
        }
    }
}
