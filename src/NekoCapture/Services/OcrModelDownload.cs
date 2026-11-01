using System.Net.Http;
using NekoCapture.Encoding;

namespace NekoCapture.Services;

/// <summary>模型下载的国内直连与系统代理路由、测速选源和分段续传。</summary>
internal sealed partial class OcrModelComponent
{
    static readonly HttpClient Client = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient(new ModelDownloadRouter()) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NekoCapture-Components");
        return client;
    }

    Task Download(string package, string hash, long length, long prior, string target, CancellationToken cancellation)
    {
        string suffix = $"{package}/1.0.0/{package}.1.0.0.nupkg";
        string[] sources = ["https://repo.huaweicloud.com/artifactory/api/nuget/v3/nuget-remote/" + suffix,
            "https://api.nuget.org/v3-flatcontainer/" + suffix];
        return ComponentDownloader.DownloadAsync(target, new ModelProgress(this, prior), cancellation, Client, sources, length, hash,
            reportVerification: true);
    }

    sealed class ModelProgress(OcrModelComponent owner, long prior) : IProgress<ComponentProgress>
    {
        /// <inheritdoc/>
        public void Report(ComponentProgress value)
        {
            var phase = value.Verifying ? OcrComponentPhase.Verifying
                : value.Bytes == 0 ? OcrComponentPhase.Connecting : OcrComponentPhase.Downloading;
            owner.CurrentProgress = new(phase, prior + value.Bytes, value.Speed); owner.Changed?.Invoke();
        }
    }

    sealed class ModelDownloadRouter : HttpMessageHandler
    {
        readonly HttpMessageInvoker _domestic = Create(useProxy: false);
        readonly HttpMessageInvoker _upstream = Create(useProxy: true);

        static HttpMessageInvoker Create(bool useProxy) => new(new SocketsHttpHandler
            { UseProxy = useProxy, ConnectTimeout = TimeSpan.FromSeconds(4), MaxConnectionsPerServer = 8 });

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            (request.RequestUri!.Host == "repo.huaweicloud.com" ? _domestic : _upstream).SendAsync(request, cancellationToken);

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _domestic.Dispose(); _upstream.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

/// <summary>可选模型组件的下载、校验、安装和卸载状态。</summary>
internal enum OcrComponentPhase { Missing, Connecting, Downloading, Verifying, Installing, Available, Cancelled, Failed, Uninstalling, UninstallFailed }

/// <summary>模型组件安装的进度快照。</summary>
/// <param name="Phase">安装阶段。</param>
/// <param name="Bytes">下载并保留的总字节数。</param>
/// <param name="Speed">每秒下载字节数。</param>
internal sealed record OcrComponentProgress(OcrComponentPhase Phase, long Bytes, double Speed);
