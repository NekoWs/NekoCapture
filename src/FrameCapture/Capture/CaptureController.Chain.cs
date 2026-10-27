using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;
using GestureCore;

namespace FrameCapture.Services;

// 本 partial 职责：动作链的异步步骤、运行上下文与产物分派。
/// <summary>异步动作链的执行宿主。</summary>
public sealed partial class CaptureController
{
    RunContext? _chainContext;
    bool _disposed;
    Task<bool>? _chainTask;
    TaskCompletionSource<bool>? _selectionCompletion, _recordCompletion, _scrollCompletion, _delayCompletion;

    /// <summary>在 UI 线程按序运行一条动作链。录制中再次触发停止动作会结束当前录制。</summary>
    /// <param name="chain">完整动作序列。</param>
    /// <param name="onFail">失败动作序列。</param>
    /// <param name="fallback">步骤失败策略。</param>
    public void BeginChain(IReadOnlyList<string> chain, IReadOnlyList<string> onFail,
        FallbackPolicy fallback = FallbackPolicy.Stop)
    {
        if (_disposed) return;
        if (_chainTask is { IsCompleted: false })
        {
            if (State == Phase.Capturing && chain.Any(n => n is GestureActions.RecordToggle or GestureActions.RecordStop))
            {
                var active = _chainTask;
                StopRecording();
                if (!chain.Contains(GestureActions.RecordToggle)) ResumeStopChain(active, chain, onFail, fallback);
            }
            else if (State == Phase.Idle && _recordCompletion != null
                && chain.Any(n => n is GestureActions.RecordToggle or GestureActions.RecordStop))
            {
                if (_chainContext != null) _chainContext.Cancelled = true;
                RecordPanelWindow.Dismiss();
            }
            else if (State == Phase.SelectingScope) _picker?.Show();
            return;
        }
        _chainTask = RunChainAsync(chain, onFail, fallback);
    }

    async void ResumeStopChain(Task<bool> active, IReadOnlyList<string> chain, IReadOnlyList<string> onFail, FallbackPolicy fallback)
    {
        await active;
        BeginChain(chain.Where(n => GestureActions.Canonical(n) != GestureActions.Select
            && n != GestureActions.RecordStop).ToArray(), onFail, fallback);
    }

    async Task<bool> RunChainAsync(IReadOnlyList<string> chain, IReadOnlyList<string> onFail, FallbackPolicy fallback)
    {
        var context = new RunContext();
        _chainContext = context;
        bool followsSwitch = GestureActions.FollowsSwitch(chain) && _settings.Current.AutoCaptureOnSelect;
        try
        {
            bool ok = await ActionChain.RunAsync(chain, onFail, fallback, async name =>
            {
                bool result = await RunChainStepAsync(name, context, followsSwitch);
                if (result && name == GestureActions.Select && followsSwitch)
                {
                    context.FromSwitch = true;
                    result = await RunChainStepAsync(context.RecordMode ? GestureActions.RecordStart : GestureActions.GrabStill,
                        context, false);
                }
                Trace.Log($"chain step {name} ok={result}");
                return result;
            }, () => context.Cancelled);
            if (context.Still != null && !context.Cancelled)
            {
                if (!chain.Any(ConsumesStill)) ShowContextToast(context);
            }
            return ok;
        }
        finally
        {
            _selectionCompletion = _recordCompletion = _scrollCompletion = _delayCompletion = null;
            _chainContext = null;
        }
    }

    static bool ConsumesStill(string name) => name is GestureActions.CopyLast or GestureActions.EditLastStill
        or GestureActions.PinSelection or GestureActions.OcrSelection or GestureActions.NotifyToast or GestureActions.SavePng;

    static TaskCompletionSource<bool> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    static Task WaitPickerExitAsync()
    {
        var completion = Completion();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PickerExitMs) };
        timer.Tick += (_, _) => { timer.Stop(); completion.TrySetResult(true); };
        timer.Start();
        return completion.Task;
    }

    async Task<bool> RunChainStepAsync(string name, RunContext context, bool followsSwitch)
    {
        if (_disposed || (context.Cancelled && GestureActions.InfoOf(name).Class != VerbClass.Terminal)) return false;
        var values = (context.Region != null ? RunValue.Region : RunValue.None)
            | (context.Window != IntPtr.Zero ? RunValue.Window : RunValue.None)
            | (context.Still != null ? RunValue.Still : RunValue.None)
            | (context.Video != null ? RunValue.Video : RunValue.None)
            | (State == Phase.Capturing ? RunValue.Recording : RunValue.None);
        if ((GestureActions.InfoOf(name).Needs & ~values) != RunValue.None) return false;
        switch (name)
        {
            case GestureActions.StoredScope:
                var stored = StoredRegion() ?? FullMonitorAtCursor();
                context.Region = stored.Bounds; context.DeviceName = stored.DeviceName;
                context.Window = IntPtr.Zero; context.Frozen = null; context.FrozenWindow = null;
                context.Selected = false;
                return true;
            case GestureActions.Select:
                context.Still = null; context.Text = null;
                if (State == Phase.Editing) CloseEditor();
                if (State == Phase.Capturing)
                {
                    if (_pendingSel == null) return false;
                    context.Region = _pendingSel.Value.Bounds;
                    context.DeviceName = _pendingSel.Value.DeviceName;
                    return true;
                }
                if (State != Phase.Idle || _scrollTimer != null || _delayTimer != null) return false;
                _selectionCompletion = Completion();
                OpenChainPicker(context, followsSwitch);
                bool selected = await _selectionCompletion.Task;
                if (selected) await WaitPickerExitAsync();
                return selected;

            case GestureActions.GrabStill:
            case GestureActions.GrabWindow:
            case GestureActions.CaptureFrame:
                return GrabChainStill(context, name);

            case GestureActions.RecordToggle when State == Phase.Capturing:
            case GestureActions.RecordStop:
                return StopRecording();

            case GestureActions.RecordStart:
            case GestureActions.RecordToggle:
                if (State != Phase.Idle) return false;
                if (name == GestureActions.RecordToggle && context.Region == null)
                {
                    var region = StoredRegion() ?? FullMonitorAtCursor();
                    context.Region = region.Bounds; context.DeviceName = region.DeviceName;
                }
                if (context.Region == null) return false;
                _pendingSel = new Selection(context.DeviceName, context.Region.Value, context.Window)
                    .WithWindowHonor(_settings.Current.RecordWindowClean);
                context.Frozen = null; context.FrozenWindow = null;
                var recordingCompletion = _recordCompletion = Completion();
                if (context.Selected && _settings.Current.RecordNeedsStartClick)
                {
                    if (_settings.Current.ShowRecordingBorder) ShowCaptureScope(context.Region.Value);
                    RecordPanelWindow.ShowArmed(context.Region.Value, context.DeviceName,
                        () =>
                        {
                            RecordPanelWindow.Current?.SwitchToRecording();
                            _panelRecording = true;
                            if (!StartRecording())
                            {
                                _panelRecording = false;
                                RecordPanelWindow.Dismiss();
                                recordingCompletion.TrySetResult(false);
                            }
                            else context.Recording = true;
                        }, () =>
                        {
                            if (!StopRecording())
                            {
                                context.Cancelled = true;
                                RetireCaptureOutline();
                                recordingCompletion.TrySetResult(false);
                            }
                        });
                }
                else
                {
                    if (context.Selected)
                    {
                        _panelRecording = true;
                        RecordPanelWindow.ShowRecording(context.Region.Value, context.DeviceName, () => StopRecording());
                    }
                    if (!StartRecording()) { RecordPanelWindow.Dismiss(); _panelRecording = false; return false; }
                }
                if (RecordPanelWindow.Current is { } panel)
                    panel.Closed += (_, _) => { if (State != Phase.Capturing) recordingCompletion.TrySetResult(false); };
                context.Recording = State == Phase.Capturing;
                bool recorded = await recordingCompletion.Task;
                context.Recording = false;
                context.Video = recorded ? _lastSequence : null;
                return recorded;

            case GestureActions.ScrollCapture:
                if (context.Region == null || State != Phase.Idle || _scrollTimer != null) return false;
                _scrollCompletion = Completion();
                StartScrollLoop(context.Region.Value, context.DeviceName);
                return await _scrollCompletion.Task;

            case GestureActions.DelayedCapture:
                if (State != Phase.Idle || _delayTimer != null) return false;
                _delayCompletion = Completion();
                BeginDelayedCapture();
                return await _delayCompletion.Task;

            case GestureActions.OcrSelection:
                return await RecognizeStoredStillAsync();
            case GestureActions.CopyLast:
                if (context.Text != null) { System.Windows.Clipboard.SetText(context.Text); return true; }
                if (context.Still != null)
                {
                    Encoding.ClipboardService.SetFrame(context.Still, includePng: context.WindowCapture);
                    return true;
                }
                if (!HasLastStill) return false;
                CopyLastStill(); return true;
            case GestureActions.EditLastStill:
                if (State != Phase.Idle || !HasLastStill) return false;
                if (context.Still != null) OpenEditorForFrame(context.Still, context.StillRegion, context.StillDevice);
                else EditLastStill();
                return true;
            case GestureActions.EditLastVideo:
                return EditLastVideo();
            case GestureActions.PinSelection:
                if (State != Phase.Idle) return false;
                if (context.Still == null && !GrabChainStill(context, GestureActions.GrabStill, useStoredFallback: true)) return false;
                PinFrameAt(context.Still!, context.StillRegion); return true;
            case GestureActions.NotifyToast:
                if (context.Still != null) ShowContextToast(context);
                else if (HasLastStill) ShowLastStillToast();
                else return false;
                return true;
            case GestureActions.SavePng:
                return context.Still != null && SaveService.SavePng(context.Still);
            case GestureActions.SettingsOpen:
                SettingsWindow.ShowOrFocus(); return true;
            case GestureActions.HelpOpen:
                HelpWindow.ShowOrFocus(); return true;
            default:
                return false;
        }
    }

    bool GrabChainStill(RunContext context, string name, bool useStoredFallback = false)
    {
        if (State != Phase.Idle) return false;
        var region = name == GestureActions.CaptureFrame ? FullMonitorAtCursor()
            : context.Region is { } bounds ? new ScreenRegion(context.DeviceName, bounds)
            : useStoredFallback ? StoredRegion() ?? FullMonitorAtCursor() : FullMonitorAtCursor();
        bool window = name != GestureActions.CaptureFrame && (name == GestureActions.GrabWindow
            || (_settings.Current.WindowCaptureEnabled && context.Window != IntPtr.Zero));
        if (name == GestureActions.GrabWindow && context.Window == IntPtr.Zero) return false;
        var (frame, captured, windowCapture) = ChooseStillFrame(
            context.Frozen != null, window,
            () => context.FrozenWindow is { } cached ? (cached, context.FrozenWindowRegion) : null,
            () =>
            {
                try { var (f, r) = WindowCapture.Grab(context.Window, _settings.Current.WindowCaptureShadow); return (f, r); }
                catch { return null; }
            }, name == GestureActions.CaptureFrame ? null : context.Frozen,
            context.FrozenOrigin, region.Bounds, r => LiveCapture.Grab(r));
        if (frame == null || (name == GestureActions.GrabWindow && !windowCapture)) return false;
        DispatchStill(frame, captured, region.DeviceName, windowCapture);
        return true;
    }

    void ShowContextToast(RunContext context)
    {
        if (context.Still == null) return;
        var frame = context.Still;
        CaptureToast.Show(frame, context.StillRegion, context.StillDevice,
            () => { Encoding.ClipboardService.SetFrame(frame, context.WindowCapture); OpenEditorForFrame(frame, context.StillRegion, context.StillDevice); },
            () => Encoding.ClipboardService.SetFrame(frame, context.WindowCapture));
    }
}

