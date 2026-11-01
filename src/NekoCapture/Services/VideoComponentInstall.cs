using NekoCapture.Encoding;

namespace NekoCapture.Services;

/// <summary>进程内共享的组件安装会话；设置页关闭和重建不影响下载。</summary>
internal sealed class VideoComponentInstall
{
    /// <summary>组件安装的可见状态。</summary>
    internal enum InstallState { Missing, Available, Downloading, Verifying, Failed, Cancelled }

    /// <summary>设置界面共用的实际安装会话。</summary>
    internal static VideoComponentInstall Shared => Mp4;
    private static readonly VideoComponentInstall Mp4 = Create(VideoFormat.Mp4, FfmpegComponentInfo.Mp4);
    private static readonly VideoComponentInstall WebM = Create(VideoFormat.WebM, FfmpegComponentInfo.WebM);
    /// <summary>是否有任意视频格式的组件正在安装。</summary>
    internal static bool AnyBusy => Mp4.Busy || WebM.Busy;
    /// <summary>取得指定导出格式的共享安装会话。</summary>
    /// <param name="format">视频导出格式。</param>
    /// <returns>对应格式的独立安装会话。</returns>
    internal static VideoComponentInstall For(VideoFormat format) => format == VideoFormat.Mp4 ? Mp4 : WebM;
    private static VideoComponentInstall Create(VideoFormat format, FfmpegPackage package) => new(
        () => FfmpegLocator.Find(format), (progress, ct) => ComponentInstaller.InstallAsync(package, progress, ct), package);
    /// <summary>会话安装的组件描述。</summary>
    internal FfmpegPackage Package { get; }
    private readonly Func<string?> _find;
    private readonly Func<IProgress<ComponentProgress>, CancellationToken, Task> _install;
    private CancellationTokenSource? _cancellation;
    private Task? _running;

    /// <summary>创建可观察的组件安装会话。</summary>
    /// <param name="find">查找可用编码器。</param>
    /// <param name="install">下载并安装组件的异步入口。</param>
    /// <param name="package">对应格式的固定组件，缺省使用 MP4。</param>
    internal VideoComponentInstall(Func<string?> find, Func<IProgress<ComponentProgress>, CancellationToken, Task> install, FfmpegPackage? package = null)
    { _find = find; _install = install; Package = package ?? FfmpegComponentInfo.Mp4; Refresh(); }

    /// <summary>状态变化通知，在启动会话的同步上下文中触发。</summary>
    internal event Action? Changed;
    /// <summary>当前安装状态。</summary>
    internal InstallState State { get; private set; }
    /// <summary>最近一次下载和安装进度。</summary>
    internal ComponentProgress Progress { get; private set; }
    /// <summary>是否存在尚未结束的安装任务。</summary>
    internal bool Busy => _cancellation != null;

    /// <summary>空闲时更新编码器可用状态。</summary>
    internal void Refresh()
    {
        if (Busy) return;
        if (_find() != null) State = InstallState.Available;
        else if (State == InstallState.Available) State = InstallState.Missing;
        Changed?.Invoke();
    }

    /// <summary>开始安装；重复点击复用同一次任务。</summary>
    /// <returns>安装成功、失败或取消后结束的任务。</returns>
    internal Task StartAsync()
    {
        if (Busy) return _running ?? Task.CompletedTask;
        if (_find() != null) { State = InstallState.Available; Changed?.Invoke(); return Task.CompletedTask; }
        _cancellation = new CancellationTokenSource();
        State = InstallState.Downloading;
        Progress = new ComponentProgress(0, 0, totalBytes: Package.DownloadBytes);
        Changed?.Invoke();
        return _running = Run(_cancellation);
    }

    /// <summary>请求取消下载或校验，保留已安装组件。</summary>
    internal void Cancel() => _cancellation?.Cancel();

    private async Task Run(CancellationTokenSource cancellation)
    {
        var progress = new Progress<ComponentProgress>(value =>
        {
            if (_cancellation != cancellation || cancellation.IsCancellationRequested) return;
            Progress = value;
            State = value.Verifying ? InstallState.Verifying : InstallState.Downloading;
            Changed?.Invoke();
        });
        try
        {
            await _install(progress, cancellation.Token);
            State = _find() != null ? InstallState.Available : InstallState.Failed;
        }
        catch (OperationCanceledException) { State = InstallState.Cancelled; }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("Video component installation failed: " + ex);
            State = InstallState.Failed;
        }
        finally
        {
            _cancellation = null;
            cancellation.Dispose();
            Changed?.Invoke();
        }
    }
}
