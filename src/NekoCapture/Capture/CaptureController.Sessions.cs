using NekoCapture.Capture;

using NekoCapture.Core;

namespace NekoCapture.Services;

/// <summary>并行捕获使用独立状态、后端与行为等待，共享最近产物。</summary>
public sealed partial class CaptureController
{
    private sealed class SharedProducts
    {
        /// <summary>最近录制，所有捕获会话共用一份所有权。</summary>
        internal CapturedSequence? Video;
        /// <summary>可继续拼接的长图缓存。</summary>
        internal (string Png, RectI Region, string Device)? Scroll;
    }

    private readonly SharedProducts _products = new();
    private readonly bool _ownsProducts = true;
    private CaptureController? _nextCapture;

    private CaptureController(CaptureController parent) : this(parent._settings)
    {
        _products = parent._products;
        _lastStill = parent._lastStill;
        _ownsProducts = false;
    }

    private IEnumerable<CaptureController> CaptureSessions()
    {
        for (CaptureController? session = this; session != null; session = session._nextCapture) yield return session;
    }

    private CaptureController AvailableCaptureSession()
    {
        var interactive = CaptureSessions().FirstOrDefault(session => session.State is Phase.SelectingScope or Phase.PreparingEditor
            || session._recordCompletion is { Task.IsCompleted: false } && session.State == Phase.Idle
            || session._scrollStarting || session._scrollFinishing || session._scrollBar != null
            || session._delayWindow != null || session._delayFiring);
        if (interactive != null) return interactive;
        CaptureController last = this;
        foreach (var session in CaptureSessions())
        {
            last = session;
            if (session.State == Phase.Idle && (session._chainTask == null || session._chainTask.IsCompleted)) return session;
        }
        return last._nextCapture = new CaptureController(this);
    }

    private bool WaitingForRecording => State is Phase.Starting or Phase.Capturing or Phase.Stopping
        || _recordCompletion is { Task.IsCompleted: false } && State == Phase.Idle;

    private CaptureController BehaviorCaptureTarget(BehaviorGraph graph, string entry, string binding)
    {
        var owner = CaptureSessions().LastOrDefault(session => session.WaitingForRecording && session._behaviorBinding == binding);
        if (owner != null) return owner;
        if (EntryStopsRecording(graph, entry))
            return CaptureSessions().LastOrDefault(session => session.WaitingForRecording) ?? AvailableCaptureSession();
        return AvailableCaptureSession();
    }

    private CaptureController ChainCaptureTarget(IReadOnlyList<string> chain) =>
        CaptureSessions().LastOrDefault(session => session.WaitingForRecording
            && SelectionCapture.StopsRecording(chain, session._activeChain)) ?? AvailableCaptureSession();

    private void DismissRecordPanel()
    {
        var panel = _recordPanel; _recordPanel = null;
        if (panel?.IsLoaded == true) panel.Close();
    }

    private void ReleaseLocalCaptureBuffers()
    {
        _live.ReleaseIdleBuffers();
        _nextCapture?.ReleaseLocalCaptureBuffers();
    }
}
