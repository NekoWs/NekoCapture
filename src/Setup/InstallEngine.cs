using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace FrameCapture.Setup;

/// <summary>
/// 把载荷落到 Program Files 并登记，或反向撤销。
/// 每机器安装：一次 UAC，之后所有账户可用，程序目录不被普通权限进程改写。
/// </summary>
internal static class InstallEngine
{
    /// <summary>程序目录名，位于 64 位 Program Files 下。</summary>
    internal const string AppFolder = "FrameCapture";

    /// <summary>主程序与卸载入口的进程名，不带扩展名。</summary>
    internal const string AppName = "FrameCapture";

    /// <summary>注册表卸载项的键名，同时是 NoRepair 场景下的产品标识。</summary>
    internal const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FrameCapture";

    /// <summary>随包落地的卸载入口文件名，控制面板里的卸载命令指向它。</summary>
    internal const string UninstallEntryName = "FrameCapture-Setup.exe";

    /// <summary>主程序自启值的位置与名字，与 Services/StartupService.cs 一致。</summary>
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string RunValue = "FrameCapture";

    /// <summary>默认安装目录：64 位 Program Files 下的程序目录。</summary>
    internal static string DefaultInstallDir => Path.Combine(DotnetRuntime.ProgramFilesRoot, AppFolder);

    /// <summary>本次要装到 / 要从哪里卸掉的目录。用户在安装界面里换过位置就是换过的那个。</summary>
    internal static string InstallDir { get; set; } = DefaultInstallDir;

    /// <summary>卸载时定位程序目录：以登记项里写下的 InstallLocation 为准——装在哪由用户定，
    /// 只有登记知道。读不到（登记被手工删过、老包没写）才按默认目录处理。</summary>
    /// <returns>登记过的目录，取不到时为 <see cref="DefaultInstallDir"/>。</returns>
    internal static string RecordedInstallDir()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(UninstallKey);
            if (k?.GetValue("InstallLocation") is string s && !string.IsNullOrWhiteSpace(s)) return s;
        }
        catch { /* 注册表读不动就按默认目录走，最坏是少删一个空目录 */ }
        return DefaultInstallDir;
    }

    /// <summary>把用户挑的位置收敛成可安装的路径。只拦真会出事的那几条，其余交给安装本身报错。</summary>
    /// <param name="dir">候选目录，允许相对写法与尾随分隔符。</param>
    /// <param name="full">规范化后的绝对路径；不可用时为 <see cref="DefaultInstallDir"/>。</param>
    /// <param name="why">不可用的原因，一句话，可直接上屏。</param>
    /// <returns>true = 可以装到这里。</returns>
    internal static bool TryTarget(string? dir, out string full, out string why)
    {
        full = DefaultInstallDir;
        why = "";
        if (string.IsNullOrWhiteSpace(dir)) { why = "没有填写位置"; return false; }

        string p;
        try { p = Path.GetFullPath(dir); }
        catch (Exception) { why = "路径写法不合法"; return false; }

        p = p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(p);
        if (p.Length == 0 || string.Equals(p, root?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        { why = "不能直接装到磁盘根目录，请在盘下建一个子文件夹"; return false; }
        if (p.Length > 240) { why = "路径太长，程序里的文件会放不下"; return false; }

        foreach (var bad in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                     Path.GetTempPath(),
                 })
            if (Within(p, bad)) { why = "这里装着系统或临时文件，换一个位置"; return false; }
        if (string.Equals(p, DotnetRuntime.ProgramFilesRoot, StringComparison.OrdinalIgnoreCase))
        { why = "要装到 Program Files 下面的一个子文件夹里"; return false; }

        // 目录里已有东西时只认「这里就是 FrameCapture」：安装会把整个目录搬走再删掉备份，
        // 指到用户的资料文件夹上就是一场灾难。
        if (Directory.Exists(p) && !File.Exists(Path.Combine(p, AppName + ".exe")) && DirHasAnyEntry(p))
        { why = "这个文件夹里已有别的文件，请换一个空文件夹"; return false; }

        full = p;
        return true;
    }

    /// <summary>a 是否就是 b，或者在 b 里面。</summary>
    /// <param name="a">待判路径，已规范化且无尾随分隔符。</param>
    /// <param name="b">参照目录。</param>
    /// <returns>true = 两者是同一个目录或有包含关系。</returns>
    private static bool Within(string a, string b)
    {
        var t = b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return t.Length > 0 && (string.Equals(a, t, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(t + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>目录里是否有任何文件或子目录。</summary>
    /// <param name="dir">目标目录。</param>
    /// <returns>true = 非空；读不动（没权限、被占用）按非空处理，宁可拒装不可误删。</returns>
    private static bool DirHasAnyEntry(string dir)
    {
        try { return Directory.EnumerateFileSystemEntries(dir).Any(); }
        catch (Exception) { return true; }
    }

    /// <summary>开始菜单程序组目录。</summary>
    internal static string StartMenuDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", AppFolder);

    /// <summary>载荷来源：优先内嵌资源，缺省时退回 exe 同目录的 payload.zip，便于本地反复试装。</summary>
    /// <returns>可读取的 zip 流；两处都没有时抛异常。</returns>
    internal static Stream OpenPayload()
    {
        var asm = typeof(InstallEngine).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase)) continue;
            var s = asm.GetManifestResourceStream(name);
            if (s != null) return s;
        }
        var side = Path.Combine(AppDir, "payload.zip");
        if (File.Exists(side)) return File.OpenRead(side);
        throw new FileNotFoundException("安装载荷缺失：既没有内嵌资源，也没有同目录 payload.zip");
    }

    /// <summary>引导器自身所在目录。</summary>
    private static string AppDir => Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

    /// <summary>结束所有正在运行的主程序实例，否则被占用的 dll 删不掉也换不掉。</summary>
    internal static void KillApp()
    {
        foreach (var p in Process.GetProcessesByName(AppName))
        {
            try
            {
                p.Kill();
                p.WaitForExit(5000);
            }
            catch { /* 进程可能刚好自己退了 */ }
            finally { p.Dispose(); }
        }
    }

    /// <summary>执行安装。调用方负责提权。</summary>
    /// <param name="log">进度与结果文本。</param>
    /// <returns>true = 安装并登记成功。</returns>
    internal static bool Install(IProgress<string> log)
    {
        var backup = Path.Combine(Path.GetTempPath(), "FrameCapture-backup-" + Guid.NewGuid().ToString("N"));
        var moved = false;
        try
        {
            log.Report("正在结束运行中的主程序");
            KillApp();

            if (Directory.Exists(InstallDir))
            {
                log.Report("正在备份已装版本");
                Directory.Move(InstallDir, backup);
                moved = true;
            }

            log.Report("正在释放文件");
            Directory.CreateDirectory(InstallDir);
            using (var zip = OpenPayload())
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Read))
            {
                foreach (var e in archive.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    var dest = Path.GetFullPath(Path.Combine(InstallDir, e.FullName));
                    // 载荷由我们自己打包，仍拒绝越界条目，避免坏包写到目录外
                    if (!dest.StartsWith(Path.GetFullPath(InstallDir) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("载荷条目路径越界：" + e.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, true);
                }
            }

            log.Report("正在放置卸载入口");
            PlaceUninstallEntry();

            log.Report("正在登记卸载信息与快捷方式");
            Register();
            CreateShortcut();

            if (moved) { TryDeleteDir(backup); }
            log.Report("完成");
            return true;
        }
        catch (Exception ex)
        {
            log.Report("失败：" + ex.Message);
            // 新装没成功就把原样搬回去，用户至少还有上一个可用版本
            try
            {
                if (moved && !Directory.Exists(InstallDir)) Directory.Move(backup, InstallDir);
            }
            catch { /* 回滚失败已无更优解，日志里已留下失败原因 */ }
            return false;
        }
    }

    /// <summary>执行卸载。调用方负责提权，且需从临时副本运行以删掉自己那份。</summary>
    /// <param name="log">进度与结果文本。</param>
    /// <returns>true = 程序目录、快捷方式、开机自启、个人设置与登记项都已清除。</returns>
    internal static bool Uninstall(IProgress<string> log)
    {
        try
        {
            // 装在哪由用户定，只有登记知道：先按登记定位，再动手删
            InstallDir = RecordedInstallDir();

            log.Report("正在结束运行中的主程序");
            KillApp();

            log.Report("正在删除程序目录 " + InstallDir);
            TryDeleteDir(InstallDir);

            log.Report("正在删除快捷方式");
            TryDeleteDir(StartMenuDir);

            // 设置与错误日志由主程序自己建，卸载要连它一起清才留不下痕迹。
            // 取的是当前用户的环境变量：别人账户下的同名目录动不了，也不该动。
            log.Report("正在清除个人设置");
            TryDeleteDir(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder));

            // 自启值是主程序自己写的，卸载器必须清掉，否则「启动时运行」留一条指向空路径的死值
            log.Report("正在清除开机自启");
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (run?.GetValue(RunValue) != null) run.DeleteValue(RunValue, false);
            }
            catch { /* 别的用户写的自启值动不了，跳过 */ }

            log.Report("正在清除卸载登记");
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { /* 键本就不存在 */ }

            log.Report("完成");
            return true;
        }
        catch (Exception ex)
        {
            log.Report("失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>写控制面板「应用和功能」需要的登记项。</summary>
    private static void Register()
    {
        var version = typeof(InstallEngine).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "FrameCapture");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", PublisherName());
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("DisplayIcon", Path.Combine(InstallDir, AppName + ".exe"));
        // 卸载入口随包落地，用户删掉下载目录里的引导器也不影响以后卸载
        key.SetValue("UninstallString", "\"" + Path.Combine(InstallDir, UninstallEntryName) + "\" --uninstall");
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(DirSizeBytes(InstallDir) / 1024), RegistryValueKind.DWord);
    }

    /// <summary>把引导器自身复制到程序目录，作为卸载命令的落点。</summary>
    /// <remarks>复制正在运行的 exe 是允许的（读共享），落地的那份不在执行中，卸载时可删。</remarks>
    private static void PlaceUninstallEntry()
    {
        var self = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(self) || !File.Exists(self)) return;
        var dest = Path.Combine(InstallDir, UninstallEntryName);
        // 引导器就放在目标目录里跑的情况：把文件复制到自己身上只会抛一场
        if (string.Equals(Path.GetFullPath(self), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) return;
        File.Copy(self, dest, true);
    }

    /// <summary>发行者名，取自工程的 Company 特性，发行脚本可用 -p:Company 覆盖。</summary>
    /// <returns>非空的发行者名称。</returns>
    private static string PublisherName()
    {
        var c = typeof(InstallEngine).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
        return string.IsNullOrWhiteSpace(c) ? AppName : c!;
    }

    /// <summary>在开始菜单建程序组与快捷方式。走系统 Shell 对象，不引第三方互操作程序集。</summary>
    private static void CreateShortcut()
    {
        Directory.CreateDirectory(StartMenuDir);
        var lnk = Path.Combine(StartMenuDir, "FrameCapture.lnk");
        var shell = Type.GetTypeFromProgID("WScript.Shell");
        if (shell == null) return;
        dynamic sh = Activator.CreateInstance(shell)!;
        dynamic sc = sh.CreateShortcut(lnk);
        sc.TargetPath = Path.Combine(InstallDir, AppName + ".exe");
        sc.WorkingDirectory = InstallDir;
        sc.IconLocation = Path.Combine(InstallDir, AppName + ".exe,0");
        sc.Description = "FrameCapture";
        sc.Save();
    }

    /// <summary>目录内文件字节总数，供 EstimatedSize 用。</summary>
    /// <param name="dir">目标目录。</param>
    /// <returns>字节数；目录不可读时 0。</returns>
    private static long DirSizeBytes(string dir)
    {
        long sum = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                sum += new FileInfo(f).Length;
        }
        catch { /* 统计失败不影响安装 */ }
        return sum;
    }

    /// <summary>尽力递归删除目录。</summary>
    /// <param name="dir">目标目录。</param>
    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* 占用中的文件留给重启后处理 */ }
    }
}
