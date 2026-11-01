using System;
using System.Collections.Generic;
using System.IO;

namespace NekoCapture.Setup;

/// <summary>
/// 探测本机是否已有主程序能用的 .NET Desktop 运行时。
/// 判据是列目录而不是查注册表：注册表搜索只能匹配固定版本号，而主程序 runtimeconfig 写了
/// rollForward=Major，装了 9 或 10 的机器同样能跑，按「有没有 8.0.x」查会把它们误判成缺运行时。
/// </summary>
internal static class DotnetRuntime
{
    /// <summary>主程序要求的最低运行时大版本，与 csproj 的 TargetFramework 保持同步。</summary>
    internal const int MinMajor = 8;

    /// <summary>WPF 与 WinForms 所在的共享框架目录名。</summary>
    internal const string DesktopFramework = "Microsoft.WindowsDesktop.App";

    /// <summary>扫所有可能的 dotnet 根，取 Desktop 运行时的最高版本。</summary>
    /// <returns>最高版本号；一处都没找到时 null。</returns>
    internal static Version? NewestDesktop()
    {
        Version? best = null;
        foreach (var root in CandidateRoots())
        {
            var shared = Path.Combine(root, "shared", DesktopFramework);
            if (!Directory.Exists(shared)) continue;
            foreach (var dir in Directory.GetDirectories(shared))
            {
                var v = Parse(Path.GetFileName(dir));
                if (v == null) continue;
                if (best == null || v > best) best = v;
            }
        }
        return best;
    }

    /// <summary>本机运行时是否已满足主程序要求。</summary>
    /// <param name="found">探到的最高版本；未满足时可能是低版本，也可能为 null。</param>
    /// <returns>true = 无需再装运行时。</returns>
    internal static bool Satisfied(out Version? found)
    {
        found = NewestDesktop();
        return found != null && found.Major >= MinMajor;
    }

    /// <summary>可能的 dotnet 安装根：64 位 Program Files，以及 DOTNET_ROOT 系列环境变量。</summary>
    /// <returns>去重后的存在目录。</returns>
    private static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in EnumerateRoots())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var trimmed = raw.Trim().TrimEnd('\\', '/');
            if (trimmed.Length == 0) continue;
            if (!seen.Add(trimmed)) continue;
            if (!Directory.Exists(trimmed)) continue;
            yield return trimmed;
        }
    }

    /// <summary>按优先级产出候选根路径，含自定义安装位置。</summary>
    /// <returns>未校验存在性的原始路径序列。</returns>
    private static IEnumerable<string> EnumerateRoots()
    {
        yield return Path.Combine(ProgramFilesRoot, "dotnet");
        yield return Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "";
        yield return Environment.GetEnvironmentVariable("DOTNET_ROOT_x64") ?? "";
    }

    /// <summary>64 位 Program Files。引导器是 x64 进程，ProgramFiles 即 64 位；
    /// ProgramW6432 作为兜底，避免任何重定向意外。</summary>
    internal static string ProgramFilesRoot
    {
        get
        {
            var w64 = Environment.GetEnvironmentVariable("ProgramW6432");
            if (!string.IsNullOrWhiteSpace(w64)) return w64;
            return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        }
    }

    /// <summary>把目录名解析成版本号。运行时目录名形如 8.0.32，带服务分支后缀时截掉再解析。</summary>
    /// <param name="text">目录名。</param>
    /// <returns>解析出的版本；不是版本号（例如临时目录）时 null。</returns>
    private static Version? Parse(string text)
    {
        var dash = text.IndexOf('-');
        if (dash > 0) text = text.Substring(0, dash);
        return Version.TryParse(text, out var v) ? v : null;
    }
}
