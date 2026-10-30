using System.Diagnostics;
using System.IO;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;
using Trace = FrameCapture.Core.Trace;

namespace FrameCapture.Updates;

/// <summary>管理启动检查、版本跳过、下载与安装交接。</summary>
internal sealed partial class UpdateService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly CaptureController _controller;
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _download;
    private UpdateToast? _toast;
    private IDisposable? _toastLease;

    /// <summary>创建更新服务，所有界面操作从 UI 线程进入。</summary>
    /// <param name="settings">配置服务。</param>
    /// <param name="controller">捕获任务状态。</param>
    internal UpdateService(SettingsService settings, CaptureController controller) { _settings = settings; _controller = controller; }

    /// <summary>当前产品版本。</summary>
    internal static string CurrentVersion => typeof(UpdateService).Assembly.GetName().Version!.ToString(3);

    /// <summary>按设置在启动后检查一次；同一常驻会话的界面重建不重复检查。</summary>
    internal async void Start()
    {
        if (!_settings.Current.AutoCheckUpdates || Environment.GetEnvironmentVariable("FC_TEST_NO_FG") == "1") return;
        try
        {
            bool first = await BackgroundWork.Run(() =>
            {
                var host = Runtime.ResidentProtocol.FindHost();
                uint pid = (uint)Environment.ProcessId;
                if (host != IntPtr.Zero) Native.Win32.GetWindowThreadProcessId(host, out pid);
                using var process = Process.GetProcessById((int)pid);
                string session = pid + ":" + process.StartTime.ToUniversalTime().Ticks;
                string path = Path.Combine(SettingsService.Dir, "update-session.txt");
                if (File.Exists(path) && File.ReadAllText(path) == session) return false;
                Directory.CreateDirectory(SettingsService.Dir); File.WriteAllText(path, session);
                return true;
            });
            if (first && !_shutdown.IsCancellationRequested) await CheckAsync(false);
        }
        catch (Exception ex) { Trace.Log("update startup: " + ex.Message); }
    }

    private void CloseToast()
    {
        if (_toast is { } toast) { toast.IsHitTestVisible = false; Fluent.CloseWithFade(toast); }
        _toast = null;
        _toastLease?.Dispose(); _toastLease = null;
    }

    private async void Install(UpdateRelease release)
    {
        if (_download != null || _shutdown.IsCancellationRequested) return;
        using var activity = BackgroundWork.Hold();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _download = cancellation;
        string? installer = null;
        FileStream? verifiedInstaller = null;
        bool reserved = false;
        var disabled = new List<(System.Windows.Window Window, bool Enabled)>();
        try
        {
            Downloaded = 0;
            Publish(UpdateStage.Downloading);
            _toast?.SetProgress(0, release.Length);
            var progress = new Progress<long>(done =>
            {
                if (Stage != UpdateStage.Downloading) return;
                Downloaded = done; _toast?.SetProgress(done, release.Length); Changed?.Invoke();
            });
            installer = await Task.Run(() => UpdateTransport.DownloadAsync(release, progress, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            verifiedInstaller = await BackgroundWork.Run(() =>
            {
                var file = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
                try
                {
                    if (file.Length != release.Length || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file))
                        .ToLowerInvariant() != release.Sha256) throw new IOException("Installer changed after download");
                    return file;
                }
                catch { file.Dispose(); throw; }
            });
            cancellation.Token.ThrowIfCancellationRequested();
            Publish(UpdateStage.Waiting);
            _toast?.SetWaiting(false);
            while (BackgroundWork.ActiveCount > (_toastLease != null ? 2 : 1) || !_controller.TryReserveUpdate())
                await Task.Delay(250, cancellation.Token);
            reserved = true;
            foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
            {
                if (ReferenceEquals(window, _toast)) continue;
                disabled.Add((window, window.IsEnabled)); window.IsEnabled = false;
            }
            await _controller.SaveResidentSessionAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            Publish(UpdateStage.Installing);
            _toast?.SetWaiting(true);
            var start = new ProcessStartInfo(installer) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(installer)! };
            if (!StartupService.IsElevated) start.Verb = "runas";
            start.Arguments = "--silent --restart --dir \"" + AppContext.BaseDirectory.TrimEnd('\\')
                + "\" --settings \"" + SettingsService.FilePath + "\" --language " + (Loc.IsEnglish ? "en" : "zh")
                + (StartupService.IsElevated ? " --restart-elevated" : "");
            using var process = Process.Start(start) ?? throw new IOException("Installer did not start");
            await process.WaitForExitAsync(cancellation.Token);
            if (process.ExitCode != 0) throw new IOException("Installer exit code " + process.ExitCode);
            CloseToast();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Publish(UpdateStage.Cancelled); CloseToast();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_shutdown.IsCancellationRequested) Publish(_dismissedStage);
        }
        catch (Exception ex)
        {
            Trace.Log("update install: " + ex); Publish(UpdateStage.Failed); _toast?.SetFailed();
        }
        finally
        {
            _download = null; Changed?.Invoke();
            if (reserved) _controller.ReleaseUpdate();
            foreach (var item in disabled) item.Window.IsEnabled = item.Enabled;
            bool retainInstaller = Stage == UpdateStage.Failed && verifiedInstaller != null;
            verifiedInstaller?.Dispose();
            if (installer != null && !retainInstaller)
                try { await BackgroundWork.Run(() => { File.Delete(installer); Directory.Delete(Path.GetDirectoryName(installer)!); return true; }); } catch { }
        }
    }

    /// <summary>停止检查与下载，关闭更新提示。</summary>
    public void Dispose() { _shutdown.Cancel(); _download?.Cancel(); CloseToast(); }
}
