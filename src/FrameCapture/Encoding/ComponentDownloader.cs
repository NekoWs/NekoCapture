using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FrameCapture.Encoding;

/// <summary>对固定组件竞速选源，使用四段续传；摘要不符时只从上游重新获取。</summary>
internal static class ComponentDownloader
{
    /// <summary>下载组件并核对完整摘要；失败或取消清理本次文件。</summary>
    /// <param name="path">本次独占的归档绝对路径。</param>
    /// <param name="progress">接收下载进度。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="client">可注入的 HTTP 客户端，由调用方释放。</param>
    /// <param name="sources">使用相同字节内容的 HTTPS 下载源，最后一项为可信上游。</param>
    /// <param name="expectedBytes">归档字节数；缺省使用固定组件大小。</param>
    /// <param name="expectedHash">归档 SHA256；缺省使用固定组件摘要。</param>
    /// <param name="reportVerification">是否在核对摘要前报告校验状态。</param>
    /// <returns>完整性验证成功后完成的任务。</returns>
    internal static async Task DownloadAsync(string path, IProgress<ComponentProgress> progress, CancellationToken ct,
        HttpClient? client = null, IReadOnlyList<string>? sources = null, long? expectedBytes = null, string? expectedHash = null,
        bool reportVerification = false)
    {
        if (!Path.IsPathRooted(path) || File.Exists(path)) throw new IOException("Download requires a new absolute path");
        var urls = (sources ?? FfmpegComponentInfo.DownloadUrls).ToArray();
        if (urls.Length == 0 || urls.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme != "https"))
            throw new ArgumentException("Download sources must use HTTPS");
        long size = expectedBytes ?? FfmpegComponentInfo.DownloadBytes;
        if (size < 4) throw new ArgumentOutOfRangeException(nameof(expectedBytes));
        string hash = expectedHash ?? FfmpegComponentInfo.Sha256;
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var ownedClient = client == null
            ? new HttpClient(new HttpClientHandler { MaxConnectionsPerServer = 8 }) { Timeout = Timeout.InfiniteTimeSpan }
            : null;
        var http = client ?? ownedClient!;
        if (!http.DefaultRequestHeaders.UserAgent.Any()) http.DefaultRequestHeaders.UserAgent.ParseAdd("FrameCapture-Components");
        bool created = false;
        try
        {
            progress.Report(new ComponentProgress(0, 0, totalBytes: size));
            string winner = await SelectSource(http, urls, size, ct).ConfigureAwait(false);
            var ordered = new[] { winner }.Concat(urls.Where(u => u != winner)).ToArray();
            await Transfer(http, ordered, path, size, progress, ct).ConfigureAwait(false);
            created = true;
            if (reportVerification) progress.Report(new ComponentProgress(size, 0, verifying: true, totalBytes: size));
            if (!MatchesHash(path, hash))
            {
                File.Delete(path);
                await Transfer(http, new[] { urls[urls.Length - 1] }, path, size, progress, ct).ConfigureAwait(false);
                if (reportVerification) progress.Report(new ComponentProgress(size, 0, verifying: true, totalBytes: size));
                if (!MatchesHash(path, hash)) throw new InvalidDataException("Component SHA256 mismatch");
            }
            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            if (created && File.Exists(path)) File.Delete(path);
            ct.ThrowIfCancellationRequested();
            throw;
        }
    }

    private static async Task<string> SelectSource(HttpClient http, string[] sources, long size, CancellationToken ct)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = sources.Select(async url =>
        {
            var clock = Stopwatch.StartNew();
            var header = new byte[4];
            int read = 0;
            try
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(race.Token);
                idle.CancelAfter(TimeSpan.FromSeconds(8));
                int length = (int)Math.Min(size < 8388608 ? 65536 : 2097152, size);
                using var response = await Request(http, url, 0, length - 1, size, idle.Token).ConfigureAwait(false);
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var abort = idle.Token.Register(() => stream.Dispose());
                var bytes = new byte[65536];
                while (read < length)
                {
                    int count = await stream.ReadAsync(bytes, 0, Math.Min(bytes.Length, length - read), idle.Token).ConfigureAwait(false);
                    if (count == 0) throw new EndOfStreamException();
                    if (read < 4) Array.Copy(bytes, 0, header, read, Math.Min(count, 4 - read));
                    read += count;
                    if (read >= 65536 && clock.ElapsedMilliseconds >= 4000) break;
                }
            }
            catch { }
            bool valid = read >= Math.Min(65536, size) && header[0] == 'P' && header[1] == 'K' && header[2] == 3 && header[3] == 4;
            return (Url: valid ? url : null, Speed: read / Math.Max(0.001, clock.Elapsed.TotalSeconds));
        }).ToList();
        var results = new List<(string? Url, double Speed)>();
        try
        {
            while (tasks.Count > 0)
            {
                var done = await Task.WhenAny(tasks).ConfigureAwait(false);
                tasks.Remove(done);
                var result = await done.ConfigureAwait(false);
                results.Add(result);
                if (result.Url != null && result.Speed >= 2097152) return result.Url;
            }
            ct.ThrowIfCancellationRequested();
            var best = results.Where(r => r.Url != null).OrderByDescending(r => r.Speed).FirstOrDefault();
            if (best.Url != null) return best.Url;
            throw new HttpRequestException("No video component download source is reachable");
        }
        finally
        {
            race.Cancel();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private static async Task Transfer(HttpClient http, string[] urls, string path, long size,
        IProgress<ComponentProgress> progress, CancellationToken ct)
    {
        string prefix = path + ".part-" + Guid.NewGuid().ToString("N") + "-";
        var parts = Enumerable.Range(0, 4).Select(i => prefix + i).ToArray();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var clock = Stopwatch.StartNew();
        var gate = new object();
        long total = 0, previous = 0, lastTime = 0;
        void Report(int count)
        {
            lock (gate)
            {
                total += count;
                long now = clock.ElapsedMilliseconds;
                if (now - lastTime < 200 && total < size) return;
                double speed = (total - previous) * 1000d / Math.Max(1, now - lastTime);
                progress.Report(new ComponentProgress(total, speed, totalBytes: size));
                previous = total;
                lastTime = now;
            }
        }
        async Task Part(int index)
        {
            try
            {
                long start = size * index / 4, end = size * (index + 1) / 4 - 1, received = 0;
                using var file = new FileStream(parts[index], FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                var buffer = new byte[65536];
                Exception? failure = null;
                for (int attempt = 0; attempt < urls.Length * 3; attempt++)
                {
                    string url = urls[attempt % urls.Length];
                    if (start + received > end) return;
                    stop.Token.ThrowIfCancellationRequested();
                    try
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                        idle.CancelAfter(TimeSpan.FromSeconds(20));
                        using var response = await Request(http, url, start + received, end, size, idle.Token).ConfigureAwait(false);
                        using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using var abort = idle.Token.Register(() => source.Dispose());
                        var window = Stopwatch.StartNew();
                        long windowBytes = received;
                        while (start + received <= end)
                        {
                            idle.CancelAfter(TimeSpan.FromSeconds(12));
                            int count = await source.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, end - start - received + 1), idle.Token)
                                .ConfigureAwait(false);
                            if (count == 0) throw new EndOfStreamException("Incomplete FFmpeg range");
                            await file.WriteAsync(buffer, 0, count, stop.Token).ConfigureAwait(false);
                            received += count;
                            Report(count);
                            if (window.ElapsedMilliseconds >= 5000)
                            {
                                double speed = (received - windowBytes) / window.Elapsed.TotalSeconds;
                                if (speed < 65536 && attempt < urls.Length * 3 - 1)
                                    throw new IOException("Slow video component source");
                                window.Restart();
                                windowBytes = received;
                            }
                        }
                        return;
                    }
                    catch (Exception ex) when (!stop.IsCancellationRequested) { failure = ex; }
                }
                throw new IOException("Video component range download failed", failure);
            }
            catch { stop.Cancel(); throw; }
        }
        bool merged = false;
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 4).Select(Part)).ConfigureAwait(false);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            merged = true;
            foreach (string part in parts)
            {
                using var input = File.OpenRead(part);
                await input.CopyToAsync(output, 65536, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            if (merged && File.Exists(path)) File.Delete(path);
            throw;
        }
        finally { foreach (string part in parts) if (File.Exists(part)) File.Delete(part); }
    }

    private static async Task<HttpResponseMessage> Request(HttpClient http, string url, long start, long end, long size,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(start, end);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var range = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != start || range.To != end ||
            range.Length != size || (response.Content.Headers.ContentLength is { } length && length != end - start + 1))
        {
            response.Dispose();
            throw new InvalidDataException("Video component Content-Range mismatch");
        }
        return response;
    }

    private static bool MatchesHash(string path, string expected)
    {
        using var hash = SHA256.Create();
        using var input = File.OpenRead(path);
        return string.Equals(BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""), expected,
            StringComparison.OrdinalIgnoreCase);
    }
}
