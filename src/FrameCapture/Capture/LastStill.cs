namespace FrameCapture.Capture;

/// <summary>保留最近一次静态截图帧，供托盘「编辑上次截图」在确认浮窗自动消失后找回编辑器。
/// 纯状态持有：只留最新一帧，<see cref="Set"/> 直接替换引用，被替换掉的那一帧随之成为 GC 候选。
/// 仅在 UI 线程访问，内部无同步。</summary>
public sealed class LastStill
{
    private CapturedFrame? _frame;
    private RectI _region;
    private string _device = "";

    /// <summary>尚未保留任何帧。</summary>
    /// <value>true = <see cref="TryGet"/> 必然失败，托盘「编辑上次截图」应隐藏。</value>
    public bool Empty => _frame is null;

    /// <summary>把这一帧记为「上次截图」，直接替换当前持有的引用。</summary>
    /// <param name="frame">帧本体，按引用持有不拷贝；此后不得再改写它的像素。</param>
    /// <param name="region">帧对应的捕获矩形：物理像素、虚拟桌面绝对坐标。</param>
    /// <param name="device">捕获所在显示器设备名。</param>
    public void Set(CapturedFrame frame, RectI region, string device)
    {
        _frame = frame;
        _region = region;
        _device = device;
    }

    /// <summary>有帧则给出帧、范围与显示器名。不消费——重复调用给出同一份引用。</summary>
    /// <param name="frame">保留的帧；返回 false 时为 null，调用方不得使用。</param>
    /// <param name="region">捕获矩形（物理像素、虚拟桌面绝对坐标）；返回 false 时为默认值。</param>
    /// <param name="device">显示器设备名；返回 false 时为空串。</param>
    /// <returns>true = 取到了帧；false = 还没截过图，或已被 <see cref="Clear"/>。</returns>
    public bool TryGet(out CapturedFrame frame, out RectI region, out string device)
    {
        if (_frame is null) { frame = null!; region = default; device = ""; return false; }
        frame = _frame; region = _region; device = _device; return true;
    }

    /// <summary>丢掉保留帧及其范围，让像素可被回收（控制器释放时调用）。</summary>
    public void Clear() { _frame = null; _region = default; _device = ""; }
}
