namespace FrameCapture.Capture;

/// <summary>保留最近一次静态截图帧，供托盘「编辑上次截图」在确认浮窗自动消失后找回编辑器。
/// 只保留最新帧或磁盘加载函数；<see cref="Set"/> 替换引用，恢复的像素在首次使用时读取。
/// 仅在 UI 线程访问，内部无同步。</summary>
public sealed class LastStill
{
    private CapturedFrame? _frame;
    private Func<CapturedFrame?>? _load;
    private RectI _region;
    private string _device = "";

    /// <summary>这一帧是不是窗口本体截图（带有效 alpha：圆角处透明、投影处半透明黑）。
    /// 复制时靠它决定要不要连带 PNG 形态——只给 CF_DIB 的话，透明边在只认 DIB 的应用里就是一圈黑。</summary>
    /// <value>true = 窗口截图；false = 屏幕矩形截图，帧内没有有效 alpha。</value>
    public bool WindowCapture { get; private set; }

    /// <summary>尚未保留任何帧。</summary>
    /// <value>true = <see cref="TryGet"/> 必然失败，托盘「编辑上次截图」应隐藏。</value>
    public bool Empty => _frame is null && _load is null;

    /// <summary>已物化的帧；惰性恢复尚未读取时为 null。</summary>
    internal CapturedFrame? LoadedFrame => _frame;

    /// <summary>读取截图位置，不物化像素。</summary>
    /// <param name="region">缓存截图范围。</param>
    /// <param name="device">显示器设备名。</param>
    internal void TryGetMetadata(out RectI region, out string device) { region = _region; device = _device; }

    /// <summary>把这一帧记为「上次截图」，直接替换当前持有的引用。</summary>
    /// <param name="frame">帧本体，按引用持有不拷贝；此后不得再改写它的像素。</param>
    /// <param name="region">帧对应的捕获矩形：物理像素、虚拟桌面绝对坐标。</param>
    /// <param name="device">捕获所在显示器设备名。</param>
    /// <param name="windowCapture">true = 带 alpha 的窗口本体截图，见 <see cref="WindowCapture"/>。</param>
    public void Set(CapturedFrame frame, RectI region, string device, bool windowCapture = false)
    {
        _frame = frame;
        _load = null;
        _region = region;
        _device = device;
        WindowCapture = windowCapture;
    }

    /// <summary>恢复磁盘缓存，首次编辑或复制时才读取像素。</summary>
    /// <param name="load">只读帧加载函数。</param>
    /// <param name="region">缓存截图范围。</param>
    /// <param name="device">显示器设备名。</param>
    /// <param name="windowCapture">是否保留窗口透明通道。</param>
    internal void Restore(Func<CapturedFrame?> load, RectI region, string device, bool windowCapture)
    {
        _frame = null; _load = load; _region = region; _device = device; WindowCapture = windowCapture;
    }

    /// <summary>有帧则给出帧、范围与显示器名。不消费——重复调用给出同一份引用。</summary>
    /// <param name="frame">保留的帧；返回 false 时为 null，调用方不得使用。</param>
    /// <param name="region">捕获矩形（物理像素、虚拟桌面绝对坐标）；返回 false 时为默认值。</param>
    /// <param name="device">显示器设备名；返回 false 时为空串。</param>
    /// <returns>true = 取到了帧；false = 还没截过图，或已被 <see cref="Clear"/>。</returns>
    public bool TryGet(out CapturedFrame frame, out RectI region, out string device)
    {
        if (_frame == null && _load != null) { try { _frame = _load(); } finally { _load = null; } }
        if (_frame is null) { frame = null!; region = default; device = ""; return false; }
        frame = _frame; region = _region; device = _device; return true;
    }

    /// <summary>丢掉保留帧及其范围，让像素可被回收（控制器释放时调用）。</summary>
    public void Clear() { _frame = null; _load = null; _region = default; _device = ""; WindowCapture = false; }
}
