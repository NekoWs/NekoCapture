using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;

namespace FrameCapture.Services;

/// <summary>在界面线程登记录制状态，后台串行启动后端和完成帧仓。</summary>
public sealed partial class CaptureController
{
    private RecordingSession? _recordingSession;
    private Task<bool>? _recordStartTask, _recordStopTask;
    private int _recordGeneration;

    /// <summary>启动独立录制会话；启动期间可接收停止请求。</summary>
    /// <returns>后端是否成功启动。</returns>
    public Task<bool> StartRecordingAsync()
    {
        if (_disposed || State != Phase.Idle) return Task.FromResult(false);
        State = Phase.Starting;
        _recordStopTask = null;
        return _recordStartTask = StartRecordingCoreAsync();
    }

    private async Task<bool> StartRecordingCoreAsync()
    {
        using var activity = BackgroundWork.Hold();
        int generation = ++_recordGeneration;
        RecordingSession? session = null;
        try
        {
            if (!_panelRecording) HideOutline();
            var selection = _pendingSel ?? (StoredRegion() ?? FullMonitorAtCursor()).ToSelection();
            _pendingSel = selection;
            _prevFg = Native.Win32.GetForegroundWindow();
            var monitor = Monitors.ByDevice(selection.DeviceName) ?? Monitors.Nearest(selection.Bounds.X, selection.Bounds.Y);
            var settings = _settings.Current;
            int fps = settings.Fps;
            bool followRefresh = settings.FollowMonitorRefresh, wgc = settings.UseWgc;
            bool cursor = settings.RecordIncludeCursor, shadow = settings.WindowCaptureShadow;
            session = await BackgroundWork.Run(() =>
            {
                int hz = followRefresh ? DisplayRefresh.RateOf(monitor.DeviceName) : 0;
                int actualFps = hz > 0 ? Math.Clamp(hz, AppSettings.FpsMin, AppSettings.FpsMax) : fps;
                var recording = new RecordingSession(selection, monitor, actualFps, () =>
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        if (!_disposed && generation == _recordGeneration && State is Phase.Starting or Phase.Capturing)
                            _ = StopRecordingAsync();
                    }));
                try { recording.Start(wgc, cursor, shadow); return recording; }
                catch { recording.Dispose(); throw; }
            });
            if (_disposed)
            {
                await BackgroundWork.Run(() => { session.Dispose(); return true; });
                return false;
            }
            _recordingSession = session;
            State = Phase.Capturing;
            if (settings.ShowRecordingBorder)
            {
                ShowCaptureScope(selection.Bounds);
                if (!_panelRecording)
                {
                    _recBadgeTimer!.Interval = TimeSpan.FromMilliseconds(RecBadgeDelayMs);
                    _recBadgeTimer.Start();
                }
            }
            Trace.Log("chain recording started");
            return true;
        }
        catch (Exception ex)
        {
            Trace.Log("record start: " + ex);
            if (session != null)
            {
                _recordingSession = null;
                try { await BackgroundWork.Run(() => { session.Dispose(); return true; }); }
                catch (Exception error) { Trace.Log("record start release: " + error); }
            }
            if (!_disposed)
            {
                State = Phase.Idle;
                RecordPanelWindow.Dismiss(); _panelRecording = false;
                RetireCaptureOutline();
            }
            _recordCompletion?.TrySetResult(false);
            return false;
        }
    }

    /// <summary>等待启动后停止录制并交付产物；重复请求共用同一收尾任务。</summary>
    /// <returns>是否生成了可供后续动作使用的录制产物。</returns>
    public Task<bool> StopRecordingAsync()
    {
        if (_recordStopTask is { IsCompleted: false }) return _recordStopTask;
        if (_disposed || State is not (Phase.Starting or Phase.Capturing)) return Task.FromResult(false);
        return _recordStopTask = StopRecordingCoreAsync();
    }

    private async Task<bool> StopRecordingCoreAsync()
    {
        using var activity = BackgroundWork.Hold();
        var completion = _recordCompletion;
        bool success = false;
        try
        {
            if (State == Phase.Starting && _recordStartTask != null && !await _recordStartTask) return false;
            if (_disposed || _recordingSession is not { } session) return false;
            State = Phase.Stopping;
            _recordingSession = null;
            _recBadgeTimer?.Stop();
            RecordPanelWindow.Dismiss(); _panelRecording = false;
            _outline?.SetRecProcessing();
            var sequence = await BackgroundWork.Run(() =>
            {
                try { return session.Finish(); }
                finally { session.Dispose(); }
            });
            if (_disposed)
            {
                await BackgroundWork.Run(() => { (sequence?.Frames as IDisposable)?.Dispose(); return true; });
                return false;
            }
            if (sequence == null) return false;
            var previous = _lastSequence;
            _lastSequence = sequence;
            try { await BackgroundWork.Run(() => { (previous?.Frames as IDisposable)?.Dispose(); return true; }); }
            catch (Exception error) { Trace.Log("previous recording release: " + error); }
            return success = !_disposed;
        }
        catch (Exception ex) { Trace.Log("record finish: " + ex); return false; }
        finally
        {
            if (!_disposed) { State = Phase.Idle; RetireCaptureOutline(); }
            completion?.TrySetResult(success);
        }
    }

    private async void ReleaseRecording()
    {
        using var activity = BackgroundWork.Hold();
        try
        {
            if (_recordStartTask != null) await _recordStartTask;
            if (_recordStopTask != null) await _recordStopTask;
            var session = _recordingSession; _recordingSession = null;
            await BackgroundWork.Run(() => { session?.Dispose(); return true; });
        }
        catch (Exception ex) { Trace.Log("record release: " + ex); }
    }
}
