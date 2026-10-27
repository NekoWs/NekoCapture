namespace FrameCapture.Capture;

/// <summary>一条动作链独占的输入与产物。帧像素只读共享，录制帧的释放归控制器或编辑器。</summary>
public sealed class RunContext
{
    /// <summary>捕获区域，物理像素、虚拟桌面绝对坐标。</summary>
    public RectI? Region { get; set; }
    /// <summary>捕获所在显示器。</summary>
    public string DeviceName { get; set; } = "";
    /// <summary>区域命中的窗口句柄。</summary>
    public IntPtr Window { get; set; }
    /// <summary>本链最近产出的静图。</summary>
    public CapturedFrame? Still { get; set; }
    /// <summary>静图对应的矩形。</summary>
    public RectI StillRegion { get; set; }
    /// <summary>静图所在显示器。</summary>
    public string StillDevice { get; set; } = "";
    /// <summary>静图是否包含窗口透明通道。</summary>
    public bool WindowCapture { get; set; }
    /// <summary>本链的录制产物。</summary>
    public CapturedSequence? Video { get; set; }
    /// <summary>录制是否进行中。</summary>
    public bool Recording { get; set; }
    /// <summary>产物是否由选区开关决定。</summary>
    public bool FromSwitch { get; set; }
    /// <summary>选区开关的录制状态。</summary>
    public bool RecordMode { get; set; }
    /// <summary>区域是否来自本链的交互选取。</summary>
    public bool Selected { get; set; }
    /// <summary>交互是否取消或宿主是否释放。</summary>
    public bool Cancelled { get; set; }
    /// <summary>本链识别出的文字，空串表示识别结果为空。</summary>
    public string? Text { get; set; }
    /// <summary>选区时的定格画面。</summary>
    public CapturedFrame? Frozen { get; set; }
    /// <summary>定格画面的屏幕矩形。</summary>
    public RectI FrozenOrigin { get; set; }
    /// <summary>定格时缓存的窗口画面。</summary>
    public CapturedFrame? FrozenWindow { get; set; }
    /// <summary>定格窗口画面的矩形。</summary>
    public RectI FrozenWindowRegion { get; set; }
}
