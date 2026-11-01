using System.IO;
using Microsoft.Win32;

namespace NekoCapture.Setup;

/// <summary>兼容安装身份、用户目录与登录入口，既有安装原位升级。</summary>
internal static partial class InstallEngine
{
    private const string LegacyName = Core.ProductPaths.LegacyName;
    private const string LegacyUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FrameCapture";

    private static string? ApplicationIn(string directory)
    {
        foreach (string name in new[] { AppName, LegacyName })
        {
            string path = Path.Combine(directory, name + ".exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static void MigrateProductIdentity(IProgress<string> log)
    {
        try { MigrateProductIdentityCore(log); }
        catch (Exception error)
        {
            File.Copy(Path.Combine(InstallDir, AppName + ".exe"), Path.Combine(InstallDir, LegacyName + ".exe"), true);
            File.Copy(Path.Combine(InstallDir, UninstallEntryName), Path.Combine(InstallDir, LegacyName + "-Setup.exe"), true);
            log.Report("Compatibility launchers retained: " + error);
        }
    }

    private static void MigrateProductIdentityCore(IProgress<string> log)
    {
        string oldExe = Path.Combine(InstallDir, LegacyName + ".exe");
        string newExe = Path.Combine(InstallDir, AppName + ".exe");
        using (var startup = new Services.WindowsStartupRegistration()) startup.RenameExecutable(oldExe, newExe);
        foreach (string sid in Registry.Users.GetSubKeyNames())
        {
            if (!sid.StartsWith("S-1-", StringComparison.Ordinal) || sid.EndsWith("_Classes", StringComparison.Ordinal)) continue;
            using var run = Registry.Users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (run?.GetValue(LegacyName) is not string command || !command.StartsWith("\"" + oldExe + "\"", StringComparison.OrdinalIgnoreCase)) continue;
            run.SetValue(AppName, "\"" + newExe + "\"" + command.Substring(oldExe.Length + 2));
            run.DeleteValue(LegacyName, false);
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
            Core.ProductPaths.UserDirectory(folder);
        using var profiles = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (profiles != null)
        foreach (string sid in profiles.GetSubKeyNames())
        {
            using var profile = profiles.OpenSubKey(sid);
            if (profile?.GetValue("ProfileImagePath") is not string path) continue;
            foreach (string kind in new[] { "Roaming", "Local" })
                Core.ProductPaths.Migrate(Path.Combine(Environment.ExpandEnvironmentVariables(path), "AppData", kind));
        }
        try { RemoveLegacyShortcut(); } catch (Exception error) { log.Report(error.ToString()); }
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var entry = root.OpenSubKey(LegacyUninstallKey);
                if (entry?.GetValue("InstallLocation") is string installed && string.Equals(Path.GetFullPath(installed), Path.GetFullPath(InstallDir), StringComparison.OrdinalIgnoreCase))
                    root.DeleteSubKeyTree(LegacyUninstallKey, false);
            }
            catch (Exception error) { log.Report(error.ToString()); }
        }
    }

    private static void RemoveLegacyShortcut()
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        if (string.IsNullOrWhiteSpace(programs)) return;
        string directory = Path.Combine(programs, LegacyName), link = Path.Combine(directory, LegacyName + ".lnk");
        if (!File.Exists(link)) return;
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null) return;
        dynamic shell = Activator.CreateInstance(type)!, shortcut = shell.CreateShortcut(link);
        try
        {
            if (!string.Equals((string)shortcut.TargetPath, Path.Combine(InstallDir, LegacyName + ".exe"), StringComparison.OrdinalIgnoreCase)) return;
            File.Delete(link);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
