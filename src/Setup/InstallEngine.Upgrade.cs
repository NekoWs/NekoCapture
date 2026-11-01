using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace NekoCapture.Setup;

/// <summary>识别安装目录、版本与各用户配置，调用独立迁移程序。</summary>
internal static partial class InstallEngine
{
    /// <summary>更新入口指定的配置文件，提权时保持同一用户路径。</summary>
    internal static string? SettingsFile { get; set; }

    /// <summary>安装目标中可识别的产品版本。</summary>
    internal static Version? InstalledVersion
    {
        get
        {
            try
            {
                string? exe = ApplicationIn(InstallDir);
                if (exe == null) return null;
                return Version.TryParse(FileVersionInfo.GetVersionInfo(exe).FileVersion, out var version) ? version : null;
            }
            catch { return null; }
        }
    }

    /// <summary>本安装包的产品版本。</summary>
    internal static Version PackageVersion => typeof(InstallEngine).Assembly.GetName().Version!;

    /// <summary>优先使用注册表中有效的安装目录，再查默认目录与当前安装器目录。</summary>
    /// <returns>有效安装目录；未找到时返回默认目录。</returns>
    internal static string DiscoverInstallDir()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (string registration in new[] { UninstallKey, LegacyUninstallKey })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(registration);
                if (key?.GetValue("InstallLocation") is string dir && ApplicationIn(dir) != null)
                    return Path.GetFullPath(dir);
            }
            catch { }
        }
        foreach (string dir in new[] { DefaultInstallDir, Path.Combine(DotnetRuntime.ProgramFilesRoot, LegacyName), Path.GetDirectoryName(typeof(InstallEngine).Assembly.Location)! })
            if (ApplicationIn(dir) != null) return dir;
        return DefaultInstallDir;
    }

    private static IEnumerable<string> ConfigurationFiles()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(SettingsFile)) paths.Add(Path.GetFullPath(SettingsFile!));
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            foreach (string name in new[] { AppFolder, LegacyName }) paths.Add(Path.Combine(appData, name, "settings.json"));
        using var profiles = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (profiles != null)
        foreach (string sid in profiles.GetSubKeyNames())
        {
            using var profile = profiles.OpenSubKey(sid);
            if (profile?.GetValue("ProfileImagePath") is not string root) continue;
            foreach (string name in new[] { AppFolder, LegacyName })
                paths.Add(Path.Combine(Environment.ExpandEnvironmentVariables(root), "AppData", "Roaming", name, "settings.json"));
        }
        return paths.Where(File.Exists).ToArray();
    }

    private static void UpgradeConfigurations(IProgress<string> log)
    {
        string[] paths = ConfigurationFiles().ToArray();
        if (paths.Length == 0) return;
        log.Report(Core.Loc.T(Core.K.Setup.Migrating));
        string tool = Path.Combine(InstallDir, "NekoCapture.ConfigUpgrade.exe");
        var start = new ProcessStartInfo(tool, string.Join(" ", paths.Select(QuoteArgument)))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = InstallDir,
        };
        using var process = Process.Start(start) ?? throw new IOException("Configuration upgrader did not start");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Configuration upgrade failed: " + error.GetAwaiter().GetResult());
        output.GetAwaiter().GetResult();
    }

    private static void PreserveEmbeddedSettings(string backup)
    {
        if (string.IsNullOrWhiteSpace(SettingsFile)) return;
        string root = Path.GetFullPath(InstallDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string settings = Path.GetFullPath(SettingsFile!);
        if (!settings.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
        string source = Path.Combine(backup, settings.Substring(root.Length));
        if (!File.Exists(source)) return;
        string sourceDir = Path.GetDirectoryName(source)!, targetDir = Path.GetDirectoryName(settings)!;
        Directory.CreateDirectory(targetDir);
        foreach (string path in Directory.EnumerateFiles(sourceDir))
        {
            string name = Path.GetFileName(path);
            if (name == Path.GetFileName(settings) || name.StartsWith(Path.GetFileName(settings) + ".", StringComparison.OrdinalIgnoreCase)
                || name == "resident-session.json" || name.StartsWith("resident-still-", StringComparison.OrdinalIgnoreCase))
                File.Copy(path, Path.Combine(targetDir, name), true);
        }
    }

    private static Dictionary<string, (object Value, RegistryValueKind Kind)>? CaptureRegistration()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
        return key?.GetValueNames().ToDictionary(name => name, name => (key.GetValue(name)!, key.GetValueKind(name)));
    }

    private static void RestoreRegistration(Dictionary<string, (object Value, RegistryValueKind Kind)>? snapshot)
    {
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
        if (snapshot == null) return;
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKey);
        foreach (var item in snapshot) key.SetValue(item.Key, item.Value.Value, item.Value.Kind);
    }

    /// <summary>按 Windows 参数规则转义一个独立参数。</summary>
    /// <param name="value">原始参数。</param>
    /// <returns>含外层引号的参数。</returns>
    internal static string QuoteArgument(string value)
    {
        var text = new System.Text.StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { text.Append('\\', slashes * 2 + 1).Append(c); slashes = 0; continue; }
            text.Append('\\', slashes).Append(c); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
}
