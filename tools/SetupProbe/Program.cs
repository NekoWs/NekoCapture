using System;
using System.IO;
using System.Reflection;
using FrameCapture.Setup;

/// <summary>
/// 安装器目录搬运的实机探针：在真机上把一棵目录搬到别的卷、再搬回来。
/// 装到非系统盘的那台机器上整次安装失败，本机装的是系统盘，那条路径在本机走不到，
/// 只能把搬运逻辑单独拎出来跑。全程只在仓库内与临时目录写，不碰注册表、不动已装程序。
/// </summary>
internal static class Program
{
    static readonly Type Engine = typeof(InstallEngine);

    static object Call(string name, params object[] args) =>
        Engine.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

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
        var temp = Path.Combine(Path.GetTempPath(), "fc-setup-probe-" + Guid.NewGuid().ToString("N"));
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

            // 4) 跨卷只能复制：复制到另一卷（临时目录在系统盘），源留着
            Call("CopyTree", app, temp);
            Report(ref fail,
                Directory.Exists(app) && File.Exists(Path.Combine(temp, "FrameCapture.exe"))
                && File.Exists(Path.Combine(temp, "runtimes", "GestureCore.dll")),
                "跨卷复制保留源并落到目标卷", "");

            // 5) 跨卷回滚：复制回去，不该抛
            try
            {
                Call("RestoreBackup", temp, app);
                Report(ref fail, File.Exists(Path.Combine(app, "FrameCapture.exe")),
                    "跨卷回滚把内容放回程序目录", "");
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
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
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
