using System;
using System.IO;

namespace NekoCapture.Core;

/// <summary>产品用户目录与兼容配置入口，迁移保留来源并原子提交目标目录。</summary>
internal static class ProductPaths
{
    /// <summary>来源产品标识，仅用于升级兼容。</summary>
    internal const string LegacyName = "FrameCapture";

    /// <summary>读取配置目录，支持显式隔离路径。</summary>
    internal static string SettingsDirectory => Environment.GetEnvironmentVariable("NC_SETTINGS_DIR") is { Length: > 0 } path
        ? Path.GetFullPath(path) : Environment.GetEnvironmentVariable("FC_SETTINGS_DIR") is { Length: > 0 } legacy
            ? Path.GetFullPath(legacy) : UserDirectory(Environment.SpecialFolder.ApplicationData);

    /// <summary>读取用户目录，首次使用时复制来源目录，保留其配置、组件和缓存。</summary>
    /// <param name="folder">用户目录所在的系统文件夹。</param>
    /// <returns>产品目录；迁移暂时失败时使用完整的来源目录。</returns>
    internal static string UserDirectory(Environment.SpecialFolder folder)
    {
        string root = Environment.GetEnvironmentVariable("NC_TEST_NO_FG") == "1"
            && Environment.GetEnvironmentVariable("NC_TEST_USER_DATA") is { Length: > 0 } isolated
            ? Path.Combine(Path.GetFullPath(isolated), folder.ToString()) : Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root)) return "";
        return Migrate(root);
    }

    /// <summary>在指定根目录内迁移产品目录，已有目标不覆盖。</summary>
    /// <param name="root">已知的用户目录根路径。</param>
    /// <returns>可用的产品目录。</returns>
    internal static string Migrate(string root)
    {
        string destination = Path.Combine(root, "NekoCapture"), source = Path.Combine(root, LegacyName);
        if (Directory.Exists(destination) || !Directory.Exists(source)) return destination;
        string staged = destination + ".migrate-" + Guid.NewGuid().ToString("N");
        try
        {
            Copy(source, staged);
            if (!Directory.Exists(destination)) Directory.Move(staged, destination);
            return destination;
        }
        catch (IOException) { return Directory.Exists(destination) ? destination : source; }
        catch (UnauthorizedAccessException) { return source; }
        finally
        {
            if (Directory.Exists(staged)) try { Directory.Delete(staged, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source))
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
