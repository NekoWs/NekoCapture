using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using FrameCapture.Encoding;

static partial class SelfTest
{
    static void ComponentDownloadChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "component-network-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] data = Enumerable.Range(0, 32768).Select(i => (byte)(i % 251)).ToArray();
        data[0] = 80; data[1] = 75; data[2] = 3; data[3] = 4;
        string hash = Convert.ToHexString(SHA256.HashData(data));
        string[] urls = { "https://mirror.example/component.zip", "https://origin.example/component.zip" };
        var requests = new System.Collections.Concurrent.ConcurrentBag<(string Host, long Start, long End)>();
        bool badRange = false, corrupt = false, interrupt = false, neverComplete = false;
        using var client = new HttpClient(new ComponentHandler(async (request, token) =>
        {
            string host = request.RequestUri!.Host;
            long start = request.Headers.Range!.Ranges.Single().From!.Value;
            long end = request.Headers.Range.Ranges.Single().To!.Value;
            requests.Add((host, start, end));
            if (host == "origin.example") await Task.Delay(25, token).ConfigureAwait(false);
            var bytes = data.Skip((int)start).Take((int)(end - start + 1)).ToArray();
            bool part = bytes.Length < data.Length;
            if (corrupt && host == "mirror.example" && part) bytes[^1] ^= 1;
            Stream stream = interrupt && host == "mirror.example" && part ? new ComponentInterruptedStream(bytes) : new MemoryStream(bytes);
            if (neverComplete && part) { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); }
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(badRange ? start + 1 : start, end, data.Length);
            response.Content.Headers.ContentLength = bytes.Length;
            return response;
        }));
        var progress = new InlineComponentProgress();
        string PathFor(string name) => Path.Combine(root, name);
        Task Download(string path, CancellationToken token = default) => ComponentDownloader.DownloadAsync(path, progress, token,
            client, urls, data.Length, hash);
        try
        {
            string output = PathFor("good.zip");
            Download(output).GetAwaiter().GetResult();
            Chk("component-download-four-ranges-and-full-sha", File.ReadAllBytes(output).SequenceEqual(data)
                && requests.Where(r => r.End - r.Start + 1 < data.Length).Select(r => r.Start).Distinct().Count() == 4);
            Chk("component-download-progress-reaches-complete", progress.Last.Bytes == data.Length && progress.Last.Speed > 0);
            bool rejected = false;
            try { Download(output).GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
            Chk("component-download-keeps-existing-destination", rejected && File.ReadAllBytes(output).SequenceEqual(data));

            interrupt = true;
            output = PathFor("resume.zip");
            Download(output).GetAwaiter().GetResult();
            Chk("component-download-resumes-part-on-fallback-source", File.ReadAllBytes(output).SequenceEqual(data)
                && requests.Any(r => r.Host == "origin.example" && r.Start % (data.Length / 4) == 64));
            interrupt = false;
            corrupt = true;
            output = PathFor("verified.zip");
            Download(output).GetAwaiter().GetResult();
            Chk("component-download-corrupt-mirror-retries-upstream", File.ReadAllBytes(output).SequenceEqual(data)
                && requests.Any(r => r.Host == "origin.example" && r.Start == 0 && r.End == data.Length / 4 - 1));
            corrupt = false;
            badRange = true;
            output = PathFor("range.zip");
            rejected = false;
            try { Download(output).GetAwaiter().GetResult(); } catch (HttpRequestException) { rejected = true; }
            Chk("component-download-rejects-mismatched-content-range", rejected && !File.Exists(output));
            badRange = false;
            neverComplete = true;
            output = PathFor("cancel.zip");
            using var cancel = new CancellationTokenSource(150);
            rejected = false;
            try { Download(output, cancel.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { rejected = true; }
            Chk("component-download-cancellation-cleans-part-files", rejected && !File.Exists(output)
                && Directory.GetFiles(root, "*.part-*").Length == 0);
        }
        catch (Exception ex) { Chk("component-download-fixture", false, ex.ToString()); }
        finally { Directory.Delete(root, true); }
    }

    sealed class ComponentHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }

    sealed class InlineComponentProgress : IProgress<ComponentProgress>
    {
        internal ComponentProgress Last;
        public void Report(ComponentProgress value) => Last = value;
    }

    sealed class ComponentInterruptedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (Position >= 64) throw new IOException("Simulated connection drop");
            return base.ReadAsync(buffer, offset, Math.Min(count, 64 - (int)Position), ct);
        }
    }
}
