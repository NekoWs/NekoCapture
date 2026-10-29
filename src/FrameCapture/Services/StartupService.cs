using System.Security.Principal;

namespace FrameCapture.Services;

/// <summary>普通自启使用用户 Run 项；提权自启使用当前用户的交互式高权限登录任务。</summary>
public static partial class StartupService
{
    /// <summary>全局任务名称，用于识别本程序登记的兼容入口。</summary>
    public const string TaskName = "FrameCapture";
    /// <summary>登录入口标记；此入口禁止自动弹出 UAC。</summary>
    internal const string StartupArg = "--startup";

    /// <summary>当前进程是否拥有管理员令牌。</summary>
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return IsTokenElevated(identity.AccessToken);
            }
            catch { return false; }
        }
    }

    /// <summary>按用户请求切换登录入口；失败时保留当前入口。</summary>
    /// <param name="enable">是否启用登录自启。</param>
    /// <param name="elevated">是否使用最高权限任务。</param>
    /// <returns>登记结果；仅在成功后保存设置。</returns>
    public static StartupResult Apply(bool enable, bool elevated) => Configure(enable, elevated, false, null).Result;

    /// <summary>登记登录入口，并按需交接到同一用户的目标权限实例。</summary>
    /// <param name="enable">是否启用登录自启。</param>
    /// <param name="elevated">是否要求管理员权限。</param>
    /// <param name="restart">是否需要切换当前实例的权限。</param>
    /// <param name="arguments">已正确引用的应用参数。</param>
    /// <param name="settings">需要与登录入口一并保存的设置；null 只登记入口。</param>
    /// <returns>登记结果及实例交接状态。</returns>
    internal static StartupChange Configure(bool enable, bool elevated, bool restart, string? arguments, Core.SettingsService? settings = null)
    {
        try
        {
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable");
            using var identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new InvalidOperationException("User SID unavailable");
            if (restart && elevated && !IsElevated) return RunConfigurationHelper(enable, elevated, true, arguments, sid);
            using var registration = new WindowsStartupRegistration();
            var plan = new StartupPlan(registration, exe, sid, enable, elevated);
            if (!IsElevated && plan.NeedsElevation) return RunConfigurationHelper(enable, elevated, false, arguments, sid);
            Action? relaunch = restart && !elevated ? () => RestartStandard(arguments) : null;
            return ApplyLocal(plan, enable, elevated, settings, relaunch);
        }
        catch (Exception ex)
        {
            Core.Trace.Log("startup configuration failed: " + ex);
            return new StartupChange(StartupResult.Failed, false);
        }
    }

    /// <summary>入口、设置及权限交接作为一个事务提交，失败时恢复设置与入口。</summary>
    /// <param name="plan">已读取入口快照的登记计划。</param>
    /// <param name="enable">目标自启状态。</param>
    /// <param name="elevated">目标管理员偏好。</param>
    /// <param name="settings">持久化目标；null 表示不保存。</param>
    /// <param name="relaunch">创建等待本进程退出的目标权限实例；null 表示无需重启。</param>
    /// <returns>已提交的入口与交接结果；失败抛出异常。</returns>
    internal static StartupChange ApplyLocal(StartupPlan plan, bool enable, bool elevated, Core.SettingsService? settings, Action? relaunch)
    {
        bool previousStartup = settings?.Current.RunAtStartup ?? false, previousAdmin = settings?.Current.RunAsAdmin ?? false;
        var result = plan.Apply();
        bool saved = false;
        try
        {
            if (settings != null)
            {
                if (!settings.TrySaveStartup(enable, elevated)) throw new System.IO.IOException("Startup preferences could not be saved");
                saved = true;
            }
            relaunch?.Invoke();
            return new StartupChange(result, relaunch != null);
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            if (saved && !settings!.TrySaveStartup(previousStartup, previousAdmin))
                failures.Add(new System.IO.IOException("Startup preferences could not be restored"));
            try { plan.Restore(); } catch (Exception rollback) { failures.Add(rollback); }
            throw new AggregateException("Startup transaction failed", failures);
        }
    }

    /// <summary>是否允许本次启动自动请求提权。</summary>
    /// <param name="arguments">应用启动参数。</param>
    /// <param name="scripted">是否为隔离测试或明确禁用自动提权。</param>
    /// <param name="elevated">当前是否已提权。</param>
    /// <param name="requested">是否保存了管理员运行偏好。</param>
    /// <returns>只有交互式普通启动可自动请求一次提权。</returns>
    internal static bool ShouldElevate(IReadOnlyList<string> arguments, bool scripted, bool elevated, bool requested)
        => !scripted && !elevated && requested && !arguments.Any(a =>
            a.Equals(StartupArg, StringComparison.OrdinalIgnoreCase) || a.Equals("--elevated-attempt", StringComparison.OrdinalIgnoreCase));
}

/// <summary>登录入口登记与目标权限实例交接的结果。</summary>
/// <param name="Result">入口登记结果。</param>
/// <param name="Restarted">已创建等待当前进程退出的目标权限实例。</param>
internal readonly record struct StartupChange(StartupResult Result, bool Restarted);
