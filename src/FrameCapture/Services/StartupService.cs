using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace FrameCapture.Services;

/// <summary>开机自启：普通自启走 HKCU 的 Run 项，提权自启走计划任务。
/// 两者互斥——同一个程序只留一条入口，否则登录时会起来两个实例。
/// 提权那条要建"最高权限"的计划任务，建的那一刻需要管理员（UAC 一次），
/// 之后每次登录由任务计划程序直接以管理员身份拉起，不再弹 UAC。</summary>
public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "FrameCapture";

    /// <summary>提权自启用的计划任务名。删它同样需要管理员。</summary>
    public const string TaskName = "FrameCapture";

    /// <summary>当前进程是否以管理员身份运行。提权自启与全局钩子抓住系统键都靠它。</summary>
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>按「要不要自启」与「要不要以管理员身份运行」摆好登录入口。
    /// 两个都开 → 建最高权限的计划任务并清掉 Run 项；否则 → 写 Run 项并删掉计划任务。
    /// 建/删计划任务需要管理员：当前没提权时用 <c>runas</c> 起一个 <c>schtasks</c>（UAC 一次），
    /// 用户在 UAC 上点取消就当作失败，不去偷偷改成 Run 项。</summary>
    /// <param name="enable">是否随登录自启。</param>
    /// <param name="elevated">是否要求以管理员身份运行。</param>
    /// <returns>这一步的结果，设置页据此回话。</returns>
    public static StartupResult Apply(bool enable, bool elevated)
    {
        if (!enable)
        {
            DeleteTask();
            ApplyRun(false);
            return StartupResult.Off;
        }
        if (!elevated)
        {
            DeleteTask();
            ApplyRun(true);
            return StartupResult.RunKey;
        }
        // 提权自启：先建任务，建成了才清 Run 项（建失败时至少还有个普通自启兜底）
        var ok = EnsureTask(out var cancelled);
        if (!ok) return cancelled ? StartupResult.Cancelled : StartupResult.Failed;
        ApplyRun(false);
        return StartupResult.Task;
    }

    /// <summary>HKCU Run 项：普通自启的入口。</summary>
    /// <param name="enable">true 写、false 删。</param>
    static void ApplyRun(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\"");
            }
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* 注册表被策略禁用时后果只是自启没生效 */ }
    }

    /// <summary>建/更新最高权限的登录任务。任务已存在时覆盖（<c>/F</c>），
    /// 路径变了也会指向新的 exe。</summary>
    /// <param name="cancelled">UAC 被用户取消时为 true。</param>
    /// <returns>任务建成返回 true。</returns>
    static bool EnsureTask(out bool cancelled)
    {
        cancelled = false;
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;
        var args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F";
        var r = RunSchtasks(args, out cancelled);
        return r == 0;
    }

    /// <summary>删掉提权自启任务；本来就没有也算成功。</summary>
    static void DeleteTask() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out _);

    /// <summary>跑一次 schtasks。已提权就直接跑；没提权走 <c>runas</c>（UAC 一次）。
    /// 不弹窗、不阻塞等待：调 schtasks 是为了让它把任务落盘，返回值用 <c>/F</c> 的退出码判。</summary>
    /// <param name="args">schtasks 的参数。</param>
    /// <param name="cancelled">用户取消 UAC 时为 true。</param>
    /// <returns>schtasks 退出码；起不来进程时 -1。</returns>
    static int RunSchtasks(string args, out bool cancelled)
    {
        cancelled = false;
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            if (!IsElevated) psi.Verb = "runas";
            using var p = Process.Start(psi);
            if (p == null) return -1;
            p.WaitForExit(15000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 1223 = ERROR_CANCELLED：用户在 UAC 上点了「否」
            cancelled = true;
            return -1;
        }
        catch { return -1; }
    }

    /// <summary>以管理员身份重新启动本程序（当前实例随后退出）。用户取消 UAC 时返回 false。</summary>
    /// <param name="extraArgs">附带给新实例的参数，如 <c>--settings</c>。</param>
    /// <returns>新实例起来了返回 true。</returns>
    public static bool RelaunchElevated(string? extraArgs = null)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;
        try
        {
            var psi = new ProcessStartInfo(exe, extraArgs ?? "")
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            using var p = Process.Start(psi);
            return p != null;
        }
        catch { return false; }
    }
}

/// <summary><see cref="StartupService.Apply"/> 的实际落点。</summary>
public enum StartupResult
{
    /// <summary>已关闭自启：Run 项与计划任务都清掉。</summary>
    Off,
    /// <summary>普通自启：只写 HKCU Run 项。</summary>
    RunKey,
    /// <summary>提权自启：最高权限的登录任务已建好，Run 项已清掉。</summary>
    Task,
    /// <summary>需要管理员但用户在 UAC 上取消了。</summary>
    Cancelled,
    /// <summary>计划任务没建成（策略禁用、路径取不到、schtasks 报错）。</summary>
    Failed,
}
