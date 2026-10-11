namespace FrameCapture.Capture;

/// <summary>保留最近一次静态截图帧，供托盘「编辑上次截图」在确认浮窗自动消失后找回编辑器（R78）。
/// 纯状态持有：只留最新一帧（Set 覆盖旧的，旧帧随之可被 GC），可清空。仅在 UI 线程访问。</summary>
public sealed class LastStill
{
    private CapturedFrame? _frame;
    private RectI _region;
    private string _device = "";

    /// <summary>尚未保留任何帧。</summary>
    public bool Empty => _frame is null;

    public void Set(CapturedFrame frame, RectI region, string device)
    {
        _frame = frame;
        _region = region;
        _device = device;
    }

    /// <summary>有帧则取出并返回 true；空则返回 false（out 值为默认，调用方勿用）。</summary>
    public bool TryGet(out CapturedFrame frame, out RectI region, out string device)
    {
        if (_frame is null) { frame = null!; region = default; device = ""; return false; }
        frame = _frame; region = _region; device = _device; return true;
    }

    public void Clear() { _frame = null; _region = default; _device = ""; }
}
