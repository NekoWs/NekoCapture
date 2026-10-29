using System;
using System.IO;
using System.Reflection;
using System.Linq;
using FrameCapture.Setup;

/// <summary>
/// 安装器目录搬运探针：在仓库内验证卷判定、移动与复制恢复，文件内容与源保留均需完整。
/// </summary>
internal static partial class Program
{
    static readonly Type Engine = typeof(InstallEngine);

    static object Call(string name, params object[] args) =>
        Engine.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static, null,
            args.Select(a => a.GetType()).ToArray(), null)!.Invoke(null, args);

    static int Main()
    {
        try { return Run(); }
        catch (Exception ex)
        {
            // 抛出来的一刻要能看清是什么：探针崩成一句无信息的堆栈，等于把现场弄丢了
            Console.WriteLine("  FAIL 探针自身抛出未预期异常 " + ex.GetType().FullName);
            try { Console.WriteLine("       " + ex.Message); } catch { }
            var inner = ex.InnerException;
            while (inner != null)
            {
                Console.WriteLine("       内层 " + inner.GetType().FullName);
                try { Console.WriteLine("       " + inner.Message); } catch { }
                inner = inner.InnerException;
            }
            Console.WriteLine("SETUP-PROBE FAIL 1");
            return 1;
        }
    }

    static int Run()
    {
        int fail = 0;
        // 仓库内的暂存根：与系统盘不同卷时，这里就是「装到非系统盘」的那个现场
        var root = Path.Combine(RepoRoot(), "test-out", "setup-probe", Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "FrameCapture");
        var temp = Path.Combine(root, "copy-backup");
        try
        {
            Directory.CreateDirectory(Path.Combine(app, "runtimes"));
            File.WriteAllText(Path.Combine(app, "FrameCapture.exe"), "exe");
            File.WriteAllText(Path.Combine(app, "runtimes", "GestureCore.dll"), "dll");

            // 1) 备份：整目录挪走，备份必须与程序目录同卷——跨卷没有「移动」这回事
            var backup = (string)Call("BackupAside", app);
            bool ok1 = !Directory.Exists(app) && Directory.Exists(backup)
                && File.Exists(Path.Combine(backup, "FrameCapture.exe"))
                && File.Exists(Path.Combine(backup, "runtimes", "GestureCore.dll"))
                && string.Equals(Path.GetPathRoot(backup), Path.GetPathRoot(root),
                        StringComparison.OrdinalIgnoreCase);
            Report(ref fail, ok1, "备份与程序目录同卷、内容完整", "备份落在 " + backup);

            // 2) 回滚：同卷直接移回原位
            Call("RestoreBackup", backup, app);
            Report(ref fail,
                Directory.Exists(app) && !Directory.Exists(backup)
                && File.Exists(Path.Combine(app, "FrameCapture.exe")),
                "回滚把目录放回原位", "");

            // 3) 同卷判定：盘符大小写不同也算同卷
            Report(ref fail,
                !(bool)Call("SameVolume", @"D:\a", @"C:\b") && (bool)Call("SameVolume", @"D:\a", @"d:\c"),
                "同卷与跨卷判定", "");

            // 复制分支保留源目录
            Call("CopyTree", app, temp);
            Report(ref fail,
                Directory.Exists(app) && File.Exists(Path.Combine(temp, "FrameCapture.exe"))
                && File.Exists(Path.Combine(temp, "runtimes", "GestureCore.dll")),
                "目录复制保留源与完整内容", "");

            // 复制恢复使用明确的卷判定，文件操作限定在仓库内
            try
            {
                Call("RestoreBackup", temp, app, false);
                Report(ref fail, File.ReadAllText(Path.Combine(app, "FrameCapture.exe")) == "exe"
                    && File.ReadAllText(Path.Combine(app, "runtimes", "GestureCore.dll")) == "dll"
                    && File.Exists(Path.Combine(temp, "FrameCapture.exe")),
                    "复制恢复完整内容并保留备份", "");
            }
            catch (Exception ex)
            {
                Report(ref fail, false, "跨卷回滚把内容放回程序目录", "抛了：" + ex.Message);
            }

            // 6) 装前查剩余空间：查得到给数，查不到的卷按不拦
            long free = InstallEngine.FreeBytes(app);
            long none = InstallEngine.FreeBytes(@"\\no-such-host\share");
            Report(ref fail, free > 0 && none == -1, "剩余空间可查，查不到的卷按不拦",
                "本卷 " + (free >> 20) + " MB，坏路径 " + none);
            CodecChecks(root, ref fail);
        }
        finally
        {
            var allowed = Path.GetFullPath(Path.Combine(RepoRoot(), "test-out", "setup-probe")) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Probe path outside workspace");
            try { if (Directory.Exists(resolved)) Directory.Delete(resolved, true); } catch { }
        }
        Console.WriteLine(fail == 0 ? "SETUP-PROBE PASS" : "SETUP-PROBE FAIL " + fail);
        return fail;
    }

    /// <summary>仓库根：探针在 tools/SetupProbe/bin/&lt;配置&gt;/net48/ 下，向上五级。</summary>
    /// <returns>仓库根目录。</returns>
    static string RepoRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static void Report(ref int fail, bool ok, string what, string note)
    {
        if (!ok) fail++;
        Console.WriteLine((ok ? "  PASS " : "  FAIL ") + what + (note.Length > 0 ? "  " + note : ""));
    }
}
