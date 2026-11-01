using System;
using System.IO;
using NekoCapture.Setup;

/// <summary>安装识别同时接受来源产品与当前产品，当前程序优先。</summary>
internal static partial class Program
{
    private static void BrandingChecks(string root, ref int fail)
    {
        string original = InstallEngine.InstallDir;
        string directory = Path.Combine(root, "identity");
        Directory.CreateDirectory(directory);
        try
        {
            string previous = Path.Combine(directory, NekoCapture.Core.ProductPaths.LegacyName + ".exe");
            File.Copy(typeof(Program).Assembly.Location, previous);
            InstallEngine.InstallDir = directory;
            Report(ref fail, InstallEngine.TryTarget(directory, out var target, out _) && target == directory
                && InstallEngine.InstalledVersion != null && (string)Call("ApplicationIn", directory) == previous,
                "Chk rebrand-existing-installation-accepted", "");
            string current = Path.Combine(directory, "NekoCapture.exe");
            File.Copy(previous, current);
            Report(ref fail, (string)Call("ApplicationIn", directory) == current,
                "Chk rebrand-current-executable-preferred", "");
        }
        finally { InstallEngine.InstallDir = original; }
    }
}
