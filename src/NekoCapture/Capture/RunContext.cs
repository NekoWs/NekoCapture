using NekoCapture.Core;

namespace NekoCapture.Capture;

/// <summary>一条动作链独占的输入与产物。帧像素只读共享，烘焙帧租约持续到本链及交接完成。</summary>
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
    public CapturedSequence? Video
    {
        get => _video;
        set
        {
            if (ReferenceEquals(_video, value)) return;
            (_video?.Frames as BakedFrameList)?.Dispose();
            _video = value;
        }
    }
    CapturedSequence? _video;
    int _users;

    /// <summary>在动作链或交接期间保留烘焙结果；最后一个使用者退出时释放。</summary>
    /// <returns>由调用方释放的使用凭据。</returns>
    internal IDisposable HoldVideo()
    {
        Interlocked.Increment(ref _users);
        return new VideoLease(this);
    }

    sealed class VideoLease(RunContext context) : IDisposable
    {
        int _disposed;
        /// <summary>结束本次使用，最后一次结束时归还烘焙帧租约。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (Interlocked.Decrement(ref context._users) == 0 && context.Video?.Frames is BakedFrameList)
                context.Video = null;
        }
    }
    /// <summary>本链最近产物的类型。</summary>
    public RunValue LatestProduct { get; set; }
    /// <summary>最近动作的事件出口，供结果提示使用。</summary>
    public string LastEvent { get; set; } = "";
    /// <summary>当前失败出口可显示的本地化原因，由提示动作消费。</summary>
    public string FailureNotice { get; set; } = "";
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
