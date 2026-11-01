using NekoCapture.Core;

namespace NekoCapture.Capture;

/// <summary>独占一次录制的后端与帧仓；启动、停录与释放在后台串行执行。</summary>
internal sealed class RecordingSession : IDisposable
{
    private readonly LiveCapture _live = new();
    private FrameSpool? _spool;
    private WgcLiveCapture? _wgc;
    private readonly Selection _selection;
    private readonly MonitorInfo _monitor;
    private readonly int _fps;
    private bool _includeCursor;
    private readonly System.Diagnostics.Stopwatch _clock = new();

    /// <summary>创建独立帧仓并连接捕获回调。</summary>
    /// <param name="selection">本次捕获范围。</param>
    /// <param name="monitor">目标显示器。</param>
    /// <param name="fps">实际录制帧率。</param>
    /// <param name="faulted">磁盘不足或写帧失败的通知，不得阻塞捕获线程。</param>
    internal RecordingSession(Selection selection, MonitorInfo monitor, int fps, Action faulted)
    {
        _selection = selection; _monitor = monitor; _fps = fps;
        var spool = _spool = new FrameSpool();
        spool.DiskLow += _ => faulted();
        _live.FrameSampled += spool.Add;
        _live.Faulted += _ => faulted();
    }

    /// <summary>启动 WGC 或 GDI；失败时调用方负责释放会话。</summary>
    /// <param name="useWgc">是否优先使用 WGC。</param>
    /// <param name="cursor">是否捕获指针。</param>
    /// <param name="shadow">窗口录制是否保留投影。</param>
    internal void Start(bool useWgc, bool cursor, bool shadow)
    {
        _includeCursor = cursor;
        _clock.Start();
        if (useWgc)
        {
            try
            {
                if (_selection.LockedToWindow && TryStartWgc(_selection, cursor, shadow)) return;
                if (TryStartWgc(_selection.WithWindowHonor(false), cursor, shadow)) return;
            }
            catch (Exception ex) { Trace.Log("wgc unavailable: " + ex.Message); }
        }
        _live.Start(_monitor, _selection.Bounds, _fps, cursor);
        Trace.Log("capture backend=gdi");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private bool TryStartWgc(Selection selection, bool cursor, bool shadow)
    {
        WgcLiveCapture? capture = null;
        try
        {
            capture = new WgcLiveCapture();
            capture.FrameSampled += _spool!.Add;
            if (selection.LockedToWindow) capture.StartWindow(selection.Window, _monitor, _fps, cursor, shadow);
            else capture.Start(_monitor, selection.Bounds, _fps, cursor);
            _wgc = capture;
            Trace.Log(selection.LockedToWindow ? "capture backend=wgc-window" : "capture backend=wgc");
            return true;
        }
        catch (Exception ex)
        {
            try { capture?.Dispose(); } catch { }
            Trace.Log("wgc init failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>停止采集并移交惰性帧列表，空录制尝试补抓一帧。</summary>
    /// <returns>录制产物；没有可用帧时为 null。</returns>
    internal CapturedSequence? Finish()
    {
        _clock.Stop();
        _live.Stop();
        if (_wgc is { } capture)
        {
            try { capture.Stop(); capture.Dispose(); }
            catch (Exception ex) { Trace.Log("wgc stop: " + ex); }
            finally { _wgc = null; }
            Trace.Log($"wgc stats delivered={capture.Delivered} kept={capture.KeptThrottle} missed={capture.FramesMissed} dur={_clock.ElapsedMilliseconds}ms");
        }
        IReadOnlyList<CapturedFrame>? frames = null;
        try
        {
            if (_spool is { } spool)
            {
                frames = spool.OpenReader();
                _spool = null;
            }
            if (frames == null || frames.Count == 0)
            {
                (frames as IDisposable)?.Dispose(); frames = null;
                if (LiveCapture.Grab(_selection.Bounds, _includeCursor) is not { } frame) return null;
                frames = new[] { frame };
            }
            return new CapturedSequence
            {
                Frames = frames, Region = _selection.Bounds, MonitorBounds = _monitor.Bounds,
                MonitorDevice = _selection.DeviceName, Fps = _fps, DroppedFrames = 0,
            };
        }
        catch { (frames as IDisposable)?.Dispose(); throw; }
    }

    /// <summary>停止后端并丢弃尚未移交的帧仓，不接触已移交的产物。</summary>
    public void Dispose()
    {
        try { _wgc?.Dispose(); } catch { }
        _wgc = null;
        _live.Dispose();
        _spool?.Abandon(); _spool = null;
    }
}
