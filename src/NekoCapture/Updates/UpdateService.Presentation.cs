using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;

namespace NekoCapture.Updates;

/// <summary>共享检查结果与进度，最新版本提示保留四秒；手动检查在页面内反馈，自动提示使用浮窗。</summary>
internal sealed partial class UpdateService
{
    private Task<UpdateRelease>? _checkTask;
    private bool _manualRequested;
    private int _inlineViews;
    private UpdateStage _dismissedStage = UpdateStage.Cancelled;
    private System.Windows.Threading.DispatcherTimer? _currentNoticeTimer;

    /// <summary>当前检查或安装状态。</summary>
    internal UpdateStage Stage { get; private set; }
    /// <summary>当前已验证的可更新版本。</summary>
    internal UpdateRelease? Release { get; private set; }
    /// <summary>当前来源已下载的字节数。</summary>
    internal long Downloaded { get; private set; }
    /// <summary>检查或更新事务仍未完成。</summary>
    internal bool Busy => _checkTask != null || _download != null;
    /// <summary>在界面线程通知状态与进度变更。</summary>
    internal event Action? Changed;

    private void Publish(UpdateStage stage)
    {
        _currentNoticeTimer?.Stop();
        Stage = stage;
        if (stage == UpdateStage.Current)
        {
            if (_currentNoticeTimer == null)
            {
                _currentNoticeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                _currentNoticeTimer.Tick += (_, _) => Publish(UpdateStage.Idle);
            }
            _currentNoticeTimer.Start();
        }
        Changed?.Invoke();
    }

    /// <summary>接入可见的页内更新面板，并接管当前浮窗。</summary>
    internal void AttachInlineView() { _inlineViews++; CloseToast(); }

    /// <summary>离开页面；进行中的下载继续使用浮窗显示。</summary>
    internal void DetachInlineView()
    {
        _inlineViews = Math.Max(0, _inlineViews - 1);
        if (_inlineViews == 0 && _download != null && Stage is UpdateStage.Downloading or UpdateStage.Waiting or UpdateStage.Installing or UpdateStage.Failed) ShowToast();
    }

    /// <summary>检查最新稳定版；并发请求共用一次检查，手动检查不弹出角落提示。</summary>
    /// <param name="manual">是否由用户主动检查。</param>
    /// <returns>检查结果已登记的完成任务。</returns>
    internal async Task CheckAsync(bool manual = true)
    {
        if (_shutdown.IsCancellationRequested || _download != null) return;
        if (_checkTask == null) _manualRequested = manual;
        else _manualRequested |= manual;
        using var activity = BackgroundWork.Hold();
        var check = _checkTask ??= Task.Run(() => UpdateTransport.CheckAsync(_shutdown.Token));
        CloseToast();
        Publish(UpdateStage.Checking);
        try
        {
            var release = await check;
            if (!ReferenceEquals(_checkTask, check)) return;
            _checkTask = null;
            if (_shutdown.IsCancellationRequested) return;
            bool requested = _manualRequested;
            Release = Version.Parse(release.Version) > Version.Parse(CurrentVersion) ? release : null;
            if (!requested && !_settings.Current.AutoCheckUpdates) { Publish(UpdateStage.Idle); return; }
            if (Release == null) { Publish(UpdateStage.Current); return; }
            if (!requested && Version.TryParse(_settings.Current.SkippedUpdateVersion, out var skipped)
                && Version.Parse(release.Version) <= skipped) { Publish(UpdateStage.Skipped); return; }
            Publish(UpdateStage.Available);
            if (!requested && _inlineViews == 0) ShowToast();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_checkTask, check)) return;
            _checkTask = null; Release = null;
            Trace.Log("update check: " + ex.Message);
            Publish(UpdateStage.CheckFailed);
        }
        finally { if (ReferenceEquals(_checkTask, check)) _checkTask = null; }
    }

    private void ShowToast()
    {
        if (_shutdown.IsCancellationRequested || _inlineViews > 0 || Release is not { } release || _toast != null) return;
        _toastLease = BackgroundWork.Hold();
        _toast = new UpdateToast(CurrentVersion, release.Version, InstallUpdate, DismissUpdate);
        if (Stage is UpdateStage.Downloading or UpdateStage.Waiting or UpdateStage.Installing)
            _toast.SetProgress(Downloaded, release.Length);
        if (Stage is UpdateStage.Waiting or UpdateStage.Installing) _toast.SetWaiting(Stage == UpdateStage.Installing);
        if (Stage == UpdateStage.Failed) _toast.SetFailed();
        _toast.Show();
    }

    /// <summary>下载并安装当前已验证的更新。</summary>
    internal void InstallUpdate() { if (Release is { } release) Install(release); }

    /// <summary>取消当前提示或下载，可保存跳过的具体版本。</summary>
    /// <param name="skip">是否跳过当前版本。</param>
    internal async void DismissUpdate(bool skip)
    {
        if (Stage == UpdateStage.Installing || Release is not { } release) return;
        _dismissedStage = skip ? UpdateStage.Skipped : UpdateStage.Cancelled;
        _download?.Cancel();
        CloseToast();
        Publish(_download != null ? UpdateStage.Cancelling : _dismissedStage);
        if (skip)
        {
            try { await _settings.MutateAsync(s => s.SkippedUpdateVersion = release.Version); }
            catch (Exception ex) { Trace.Log("update skip: " + ex.Message); }
        }
    }
}
