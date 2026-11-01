using System.Diagnostics;
using System.IO;
using System.Windows;
using NekoCapture.Core;

namespace NekoCapture.Setup;

/// <summary>无确认页的升级入口，权限不足时只请求一次 UAC。</summary>
internal static class SilentInstall
{
    /// <summary>执行静默升级并返回可供更新器判定的退出码。</summary>
    /// <param name="args">安装器原始参数。</param>
    /// <returns>零表示完成；其他值表示取消或失败。</returns>
    internal static int Run(string[] args)
    {
        if (!App.Elevated) return App.RelaunchElevated(string.Join(" ", args.Select(InstallEngine.QuoteArgument))) ? 0 : 1223;
        string logPath = Path.Combine(Path.GetTempPath(), "NekoCapture", "setup.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var log = new LogProgress(logPath);
        Exception? failure = Attempt(log);
        bool installed = failure == null;
        if (!installed)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Fluent.ApplyGlobalStyles();
            Theme.Apply(ThemeMode(), "#4CC2FF");
            var window = new UpdateFailureWindow(failure!, logPath, () => Attempt(log));
            app.Run(window);
            installed = window.Completed;
        }
        if (args.Contains("--restart")) LaunchApp(args.Contains("--restart-elevated"));
        return installed ? 0 : 1;
    }

    private static Exception? Attempt(LogProgress log)
    {
        log.Report("Setup " + InstallEngine.PackageVersion + " cwd=" + Environment.CurrentDirectory
            + " target=" + InstallEngine.InstallDir);
        try
        {
            if (!InstallEngine.TryTarget(InstallEngine.InstallDir, out var target, out var why)) throw new IOException(why);
            InstallEngine.InstallDir = target;
            if (!DotnetRuntime.Satisfied(out _))
            {
                string runtime = Path.Combine(Path.GetTempPath(), "NekoCapture", "runtime-" + Guid.NewGuid().ToString("N") + ".exe");
                try
                {
                    if (!RuntimeSetup.DownloadAsync(runtime, new RuntimeProgress(), CancellationToken.None).GetAwaiter().GetResult())
                        throw new IOException("Runtime download failed");
                    int result = RuntimeSetup.RunInstaller(runtime);
                    if (result != 0 && result != 3010) throw new IOException("Runtime setup failed: " + result);
                }
                finally { try { File.Delete(runtime); } catch { } }
            }
            return InstallEngine.Install(log) ? null : InstallEngine.LastFailure ?? new IOException("Installation failed");
        }
        catch (Exception ex) { log.Report(ex.ToString()); return ex; }
    }

    private static string ThemeMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0 ? "light" : "dark";
        }
        catch { return "dark"; }
    }

    private static void LaunchApp(bool elevated)
    {
        string exe = Path.Combine(InstallEngine.InstallDir, "NekoCapture.exe");
        try
        {
            if (!File.Exists(exe)) return;
            var start = elevated ? new ProcessStartInfo(exe) { UseShellExecute = false }
                : new ProcessStartInfo("explorer.exe", InstallEngine.QuoteArgument(exe)) { UseShellExecute = true };
            Process.Start(start);
        }
        catch { }
    }

    /// <summary>由安装目录中的引导器等待下载副本退出后清理它。</summary>
    internal static void ScheduleCleanup()
    {
        try
        {
            string self = typeof(SilentInstall).Assembly.Location;
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NekoCapture", "updates")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(self).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            string helper = Path.Combine(InstallEngine.InstallDir, InstallEngine.UninstallEntryName);
            if (!File.Exists(helper)) return;
            Process.Start(new ProcessStartInfo(helper, "--purge " + Process.GetCurrentProcess().Id + " " + InstallEngine.QuoteArgument(self))
                { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { }
    }

    private sealed class LogProgress(string path) : IProgress<string>
    {
        /// <summary>同步记录安装进度。</summary>
        /// <param name="value">进度或错误信息。</param>
        public void Report(string value) { try { File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + value + Environment.NewLine); } catch { } }
    }

    private sealed class RuntimeProgress : IProgress<(long Done, long Total)>
    {
        /// <summary>运行时下载使用静默界面。</summary>
        /// <param name="value">下载进度。</param>
        public void Report((long Done, long Total) value) { }
    }
}
