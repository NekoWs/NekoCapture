using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace FrameCapture.Services;

/// <summary>一次授权完成入口登记，提权实例在源进程退出后才接管单实例锁。</summary>
public static partial class StartupService
{
    private const string ConfigureArg = "--configure-startup";
    private const string ParentArg = "--wait-parent";

    private static StartupChange RunConfigurationHelper(bool enable, bool elevated, bool restart, string? arguments, string sid)
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            string flags = QuoteArguments(new[] { ConfigureArg, enable ? "1" : "0", elevated ? "1" : "0",
                restart ? current.Id.ToString() : "0", restart ? current.StartTime.ToUniversalTime().Ticks.ToString() : "0", sid, "--" });
            using var helper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, flags + " " + (arguments ?? ""))
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (helper == null) return new StartupChange(StartupResult.Failed, false);
            helper.WaitForExit();
            var result = Enum.IsDefined(typeof(StartupResult), helper.ExitCode) ? (StartupResult)helper.ExitCode : StartupResult.Failed;
            return new StartupChange(result, restart && result is StartupResult.Task or StartupResult.RunKey or StartupResult.Off);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return new StartupChange(StartupResult.Cancelled, false); }
        catch (Exception ex)
        {
            ReportStartupFailure("startup helper", ex);
            return new StartupChange(StartupResult.Failed, false);
        }
    }

    /// <summary>在服务和单实例锁创建前处理授权配置命令。</summary>
    /// <param name="arguments">进程参数。</param>
    /// <param name="exitCode">配置结果退出码。</param>
    /// <returns>属于配置助手命令时为 true。</returns>
    internal static bool TryHandleConfiguration(string[] arguments, out int exitCode)
    {
        exitCode = (int)StartupResult.Failed;
        if (arguments.Length == 0 || arguments[0] != ConfigureArg) return false;
        if (arguments.Length < 7 || arguments[6] != "--" || !IsElevated ||
            arguments[1] is not ("0" or "1") || arguments[2] is not ("0" or "1") ||
            !int.TryParse(arguments[3], out int parent) || !long.TryParse(arguments[4], out long ticks) ||
            parent < 0 || (parent != 0 && ticks <= 0)) return true;
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != arguments[5]) return true;
        try
        {
            using var storage = new WindowsStartupRegistration();
            var plan = new StartupPlan(storage, Environment.ProcessPath!, arguments[5], arguments[1] == "1", arguments[2] == "1");
            var settings = new Core.SettingsService();
            bool previousStartup = settings.Current.RunAtStartup, previousAdmin = settings.Current.RunAsAdmin;
            var result = plan.Apply();
            try
            {
                if (!settings.TrySaveStartup(arguments[1] == "1", arguments[2] == "1"))
                    throw new IOException("Startup preferences could not be saved");
                if (parent != 0)
                {
                    var forwarded = new[] { ParentArg, parent.ToString(), ticks.ToString(), "--elevated-attempt" }
                        .Concat(arguments.Skip(7));
                    using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, QuoteArguments(forwarded))
                    { UseShellExecute = false, CreateNoWindow = true });
                    if (child == null) throw new InvalidOperationException("Elevated instance did not start");
                }
                exitCode = (int)result;
            }
            catch
            {
                settings.TrySaveStartup(previousStartup, previousAdmin);
                plan.Restore();
                throw;
            }
        }
        catch (Exception ex) { ReportStartupFailure("startup helper configuration", ex); }
        return true;
    }

    /// <summary>让交接实例等待源进程退出，避免转发给正在退出的实例。</summary>
    /// <param name="arguments">包含父进程标识和启动时间的参数。</param>
    /// <returns>可以继续启动时为 true；父进程未退出或参数无效时为 false。</returns>
    internal static bool WaitForParent(string[] arguments)
    {
        int index = Array.IndexOf(arguments, ParentArg);
        if (index < 0) return true;
        if (index + 2 >= arguments.Length || !int.TryParse(arguments[index + 1], out int pid) ||
            !long.TryParse(arguments[index + 2], out long ticks) || pid <= 0 || pid == Environment.ProcessId || ticks <= 0) return false;
        try
        {
            using var parent = Process.GetProcessById(pid);
            if (parent.StartTime.ToUniversalTime().Ticks != ticks) return true;
            return parent.WaitForExit(30000);
        }
        catch (ArgumentException) { return true; }
        catch { return false; }
    }

    /// <summary>按 Windows argv 规则引用参数，保留空格、引号和末尾反斜杠。</summary>
    /// <param name="arguments">逐个参数值。</param>
    /// <returns>可直接用于 ProcessStartInfo 的完整参数串。</returns>
    internal static string QuoteArguments(IEnumerable<string> arguments)
    {
        string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        return string.Join(" ", arguments.Select(Quote));
    }
}
