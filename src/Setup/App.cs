using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using FrameCapture.Core;

namespace FrameCapture.Setup;

/// <summary>
/// 引导器入口。命令行形态：
/// 无参数 = 界面；<c>--install</c> = 提权后执行安装，可跟 <c>--dir "&lt;目录&gt;"</c> 指定安装位置；
/// <c>--uninstall</c> = 卸载（内部再派生临时副本以删掉安装目录里那份）；
/// <c>--purge</c> = 等某进程退出后删掉一个文件；<c>--silent</c> = 无确认页升级，<c>--restart</c> = 完成后启动软件。
/// </summary>
internal static class App
{
    /// <summary>安装开关，提权实例带着它跑完整安装。</summary>
    internal const string ArgInstall = "--install";

    /// <summary>安装位置开关，形如 <c>--dir "D:\Apps\FrameCapture"</c>；缺省装到 Program Files。</summary>
    internal const string ArgDir = "--dir";

    /// <summary>卸载开关，控制面板里的 UninstallString 指向它。</summary>
    internal const string ArgUninstall = "--uninstall";

    /// <summary>自删除开关，形如 <c>--purge &lt;等待退出的进程 id&gt; "&lt;要删除的文件&gt;"</c>。</summary>
    internal const string ArgPurge = "--purge";

    /// <summary>进程入口。</summary>
    /// <param name="args">命令行参数。</param>
    [STAThread]
    private static void Main(string[] args)
    {
        // 自删除排在最前：这个形态只负责等另一个进程退出、删一个文件，不该拉起样式与窗口
        if (args.Length >= 3 && args[0] == ArgPurge) { Purge(args[1], args[2]); return; }

        InstallEngine.InstallDir = InstallEngine.DiscoverInstallDir();
        InstallEngine.SettingsFile = ValueAfter(args, "--settings");
        if (ValueAfter(args, "--language") is string language) Loc.IsEnglish = language == "en";
        string? requestedDir = ValueAfter(args, ArgDir);
        if (requestedDir != null)
        {
            if (!InstallEngine.TryTarget(requestedDir, out var requested, out var why))
            {
                Environment.ExitCode = 1;
                if (Array.IndexOf(args, "--silent") < 0) MessageBox.Show(why, "FrameCapture");
                return;
            }
            InstallEngine.InstallDir = requested;
        }
        if (Array.IndexOf(args, "--silent") >= 0)
        {
            Environment.ExitCode = SilentInstall.Run(args);
            if (Elevated) SilentInstall.ScheduleCleanup();
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Fluent.ApplyGlobalStyles();
        Theme.Apply(ThemeModeFromRegistry(), "#4CC2FF");

        // 安装位置对每种形态都生效：预览要出的就是同一张界面
        if (InstallEngine.TryTarget(ValueAfter(args, ArgDir), out var target, out _)) InstallEngine.InstallDir = target;

        if (args.Length >= 2 && args[0] == "--preview")
        {
            // 出图核对界面：建窗但不显示，取内容离屏渲染。
            // 不显示就不会走 ContentRendered 里的自动动作，核对卸载界面不必真的去要管理员权限。
            var mode = args.Length >= 3 && args[2] == "uninstall"
                ? SetupWindow.Mode.Uninstall
                : SetupWindow.Mode.Idle;
            Preview.Render(args[1], mode);
            return;
        }
        if (Array.IndexOf(args, ArgInstall) >= 0)
        {
            if (!Elevated)
            {
                Environment.ExitCode = RelaunchElevated(string.Join(" ", args.Select(InstallEngine.QuoteArgument))) ? 0 : 1223;
                return;
            }
            app.Run(new SetupWindow(SetupWindow.Mode.Install)); return;
        }
        if (Array.IndexOf(args, ArgUninstall) >= 0)
        {
            // 卸载要找的是登记过的那个目录：装在哪由用户定，只有注册表知道
            InstallEngine.InstallDir = InstallEngine.RecordedInstallDir();
            app.Run(new SetupWindow(SetupWindow.Mode.Uninstall));
            return;
        }
        app.Run(new SetupWindow(SetupWindow.Mode.Idle));
    }

    /// <summary>取某个开关后面紧跟的那个值。</summary>
    /// <param name="args">命令行参数。</param>
    /// <param name="flag">开关名。</param>
    /// <returns>紧跟其后的那个参数；开关缺席或排在末尾时为 null。</returns>
    private static string? ValueAfter(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>等一个进程退出后删掉一个文件。正在运行的 exe 删不掉自己，只能由另一个进程代删。</summary>
    /// <param name="pid">要等其退出的进程 id；进程已不在时立刻动手。</param>
    /// <param name="file">要删除的文件全路径。</param>
    private static void Purge(string pid, string file)
    {
        try
        {
            if (int.TryParse(pid, out var id))
            {
                try { using var p = Process.GetProcessById(id); p.WaitForExit(60000); }
                catch (ArgumentException) { /* 已经退了，直接删 */ }
            }
            // 进程退出到文件句柄真正释放之间有一小段窗口，删不动就短促重试
            for (var i = 0; i < 40 && File.Exists(file); i++)
            {
                try { File.Delete(file); return; }
                catch (IOException) { System.Threading.Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { return; }
            }
        }
        catch { /* 删不掉就留在临时目录，交给系统清理临时文件 */ }
    }

    /// <summary>跟随系统深浅偏好，与主程序读同一个注册表值。</summary>
    /// <returns>"dark" 或 "light"。</returns>
    private static string ThemeModeFromRegistry()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int i && i == 0 ? "dark" : "light";
        }
        catch { return "dark"; }
    }

    /// <summary>当前是否已提权。</summary>
    internal static bool Elevated
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

    /// <summary>用 runas 动词重新拉起自己，触发一次 UAC。</summary>
    /// <param name="arguments">传给新实例的参数。</param>
    /// <returns>true = 新实例已启动，当前实例应退出。</returns>
    internal static bool RelaunchElevated(string arguments)
    {
        try
        {
            // 取自身模块在受限环境下会抛，落回程序集位置；提权失败不该以异常收场
            var exe = Process.GetCurrentProcess().MainModule?.FileName
                      ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            var psi = new ProcessStartInfo(exe, arguments) { UseShellExecute = true, Verb = "runas" };
            using var p = Process.Start(psi);
            return p != null;
        }
        catch
        {
            return false; // 用户在 UAC 里点了「否」
        }
    }

    /// <summary>把自身复制到临时目录，用于卸载时删掉安装目录里那份自己。</summary>
    /// <returns>临时副本路径；复制失败时 null。</returns>
    internal static string? CopySelfToTemp()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
            var dest = Path.Combine(Path.GetTempPath(), "FrameCapture-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(exe, dest, true);
            return dest;
        }
        catch { return null; }
    }
}
