using System;
using System.IO;
using System.Reflection;
using FrameCapture.Setup;

/// <summary>目录占用失败必须保留源文件；释放继承的工作目录后可完整备份。</summary>
internal static partial class Program
{
    private static void BackupLockChecks(string app, ref int fail)
    {
        string original = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(app);
            bool refused = false;
            try
            {
                string unexpected = (string)Call("BackupAside", app);
                InstallEngine.ReleaseWorkingDirectory();
                Call("RestoreBackup", unexpected, app);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is IOException io && (io.HResult & 0xffff) is 32 or 33)
            {
                refused = true;
            }
            Report(ref fail, refused && File.ReadAllText(Path.Combine(app, "FrameCapture.exe")) == "exe"
                && File.ReadAllText(Path.Combine(app, "runtimes", "GestureCore.dll")) == "dll",
                "Chk locked-backup-preserves-installation", "");
            InstallEngine.ReleaseWorkingDirectory();
            string backup = (string)Call("BackupAside", app);
            Report(ref fail, !Directory.Exists(app) && File.ReadAllText(Path.Combine(backup, "FrameCapture.exe")) == "exe"
                && File.ReadAllText(Path.Combine(backup, "runtimes", "GestureCore.dll")) == "dll",
                "Chk inherited-working-directory-released-before-backup", "");
            Call("RestoreBackup", backup, app);
        }
        finally { Directory.SetCurrentDirectory(original); }
    }
}
