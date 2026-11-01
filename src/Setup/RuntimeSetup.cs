using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NekoCapture.Setup;

/// <summary>
/// 缺运行时时的取包与安装。走微软官方短链，不在安装包里塞一份运行时：
/// 绝大多数目标机已有可用运行时，内置一份等于让所有人多下一趟 58 MB。
/// </summary>
internal static class RuntimeSetup
{
    /// <summary>
    /// .NET Desktop Runtime 8 x64 官方直链。短链按 8.0 通道解析到当前最新补丁版，
    /// 因此地址长期有效，不需要跟着补丁号改。
    /// </summary>
    internal const string DownloadUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";

    /// <summary>下载失败或用户放弃时可自行打开的页面。</summary>
    internal const string DownloadPage = "https://dotnet.microsoft.com/download/dotnet/8.0";

    /// <summary>把运行时安装器下到临时目录。</summary>
    /// <param name="dest">落地文件路径，已存在会被覆盖。</param>
    /// <param name="progress">已下载字节数与总字节数；服务器未给长度时总数为 -1。</param>
    /// <param name="ct">取消令牌，取消时删除半截文件。</param>
    /// <returns>true = 下载完成且文件非空。</returns>
    internal static async Task<bool> DownloadAsync(string dest, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        // 4.8 默认协议偏保守，交给系统按注册表决定，避免老系统上握手失败
        ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        http.Timeout = TimeSpan.FromMinutes(10);
        try
        {
            using var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1L;

            // 流用同步 using：IAsyncDisposable 是 .NET Standard 2.1 起才有的东西
            using var src = await resp.Content.ReadAsStreamAsync();
            using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);

            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await dst.WriteAsync(buffer, 0, read, ct);
                done += read;
                progress.Report((done, total));
            }
            return new FileInfo(dest).Length > 0;
        }
        catch (OperationCanceledException)
        {
            TryDelete(dest);
            return false;
        }
        catch (HttpRequestException)
        {
            TryDelete(dest);
            return false;
        }
    }

    /// <summary>静默运行运行时安装器。调用方需已提权，子进程继承令牌。</summary>
    /// <param name="path">已下载的安装器路径。</param>
    /// <returns>安装器退出码；0 = 成功，3010 = 成功但需重启，其余为失败。</returns>
    internal static int RunInstaller(string path)
    {
        var psi = new ProcessStartInfo(path, "/install /quiet /norestart")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动运行时安装器");
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>尽力删除文件，删不掉不影响主流程。</summary>
    /// <param name="path">目标路径。</param>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 临时文件残留由系统清理 */ }
    }
}
