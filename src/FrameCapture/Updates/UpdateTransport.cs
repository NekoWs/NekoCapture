using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using FrameCapture.Core;

namespace FrameCapture.Updates;

/// <summary>优先直连 GitHub，连接失败后顺序尝试镜像；镜像只提供传输。</summary>
internal static class UpdateTransport
{
    private static readonly string[] Prefixes = ["", "https://gh-proxy.com/", "https://ghfast.top/"];
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5), AutomaticDecompression = DecompressionMethods.All,
    }) { Timeout = Timeout.InfiniteTimeSpan };

    private static async Task<T> TrySources<T>(string url, Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        TimeSpan timeout, CancellationToken cancellation)
    {
        Exception? failure = null;
        foreach (string prefix in Prefixes)
        {
            cancellation.ThrowIfCancellationRequested();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            attempt.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, prefix + url);
                request.Headers.UserAgent.ParseAdd("FrameCapture-Updater/1.0");
                request.Headers.CacheControl = new() { NoCache = true };
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                attempt.CancelAfter(timeout);
                return await read(response, attempt.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested)
            {
                failure = ex; Trace.Log("update source failed: " + prefix + " " + ex.Message);
            }
        }
        throw new IOException("Update sources unavailable", failure);
    }

    /// <summary>获取最新稳定版的签名清单；每个来源最多等待八秒。</summary>
    /// <param name="cancellation">取消信号。</param>
    /// <returns>已验证的最新稳定版本。</returns>
    internal static Task<UpdateRelease> CheckAsync(CancellationToken cancellation) => TrySources(
        $"https://github.com/{UpdateRelease.Repository}/releases/latest/download/update.json", async (response, token) =>
        {
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new MemoryStream();
            byte[] buffer = new byte[4096];
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                if (output.Length + count > 32768) throw new InvalidDataException("Update manifest exceeds limit");
                output.Write(buffer, 0, count);
            }
            return UpdateRelease.Parse(output.ToArray());
        }, TimeSpan.FromSeconds(8), cancellation);

    /// <summary>后台下载并校验安装包，失败来源的部分文件被覆盖，完成后才交付路径。</summary>
    /// <param name="release">已签名的发布描述。</param>
    /// <param name="progress">下载字节进度。</param>
    /// <param name="cancellation">取消信号。</param>
    /// <returns>经过 SHA256 与长度校验的安装包路径。</returns>
    internal static async Task<string> DownloadAsync(UpdateRelease release, IProgress<long> progress, CancellationToken cancellation)
    {
        string folder = Path.Combine(Path.GetTempPath(), "FrameCapture", "updates", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, release.Installer);
        Directory.CreateDirectory(folder);
        try
        {
            return await TrySources(release.DownloadUrl, async (response, token) =>
            {
                if (response.Content.Headers.ContentLength is { } size && size != release.Length)
                    throw new InvalidDataException("Installer length mismatch");
                await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[65536]; long total = 0, lastReport = 0;
                long started = Environment.TickCount64;
                progress.Report(0);
                while (true)
                {
                    using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
                    stall.CancelAfter(TimeSpan.FromSeconds(8));
                    int count = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    total += count;
                    if (total > release.Length) throw new InvalidDataException("Installer exceeds signed length");
                    long elapsed = Environment.TickCount64 - started;
                    if (elapsed > 8000 && total * 1000 / elapsed < 32768) throw new IOException("Update source is too slow");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    if (Environment.TickCount64 - lastReport >= 80) { progress.Report(total); lastReport = Environment.TickCount64; }
                }
                if (total != release.Length || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != release.Sha256)
                    throw new CryptographicException("Installer SHA256 mismatch");
                await output.FlushAsync(token).ConfigureAwait(false);
                progress.Report(total);
                return path;
            }, TimeSpan.FromMinutes(10), cancellation).ConfigureAwait(false);
        }
        catch { try { File.Delete(path); Directory.Delete(folder); } catch { } throw; }
    }
}
