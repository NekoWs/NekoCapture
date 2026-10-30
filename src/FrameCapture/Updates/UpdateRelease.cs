using System.Security.Cryptography;
using System.Text.Json;

namespace FrameCapture.Updates;

/// <summary>经过发布签名验证的安装包描述。</summary>
/// <param name="Version">产品版本。</param>
/// <param name="Installer">安装包文件名。</param>
/// <param name="Length">安装包字节数。</param>
/// <param name="Sha256">安装包 SHA256。</param>
internal sealed record UpdateRelease(string Version, string Installer, long Length, string Sha256)
{
    /// <summary>发布仓库。</summary>
    internal const string Repository = "NekoWs/FrameCapture";

    /// <summary>安装包在官方仓库的固定版本地址。</summary>
    internal string DownloadUrl => $"https://github.com/{Repository}/releases/download/v{Version}/{Installer}";

    /// <summary>校验签名并解析发布清单，拒绝不匹配的仓库、格式或安装包名。</summary>
    /// <param name="envelope">包含 Base64 清单与 RSA 签名的 JSON。</param>
    /// <returns>通过校验的发布信息。</returns>
    internal static UpdateRelease Parse(byte[] envelope)
    {
        using var outer = JsonDocument.Parse(envelope);
        byte[] manifest = Convert.FromBase64String(outer.RootElement.GetProperty("manifest").GetString()!);
        byte[] signature = Convert.FromBase64String(outer.RootElement.GetProperty("signature").GetString()!);
        using var key = RSA.Create();
        using var resource = typeof(UpdateRelease).Assembly.GetManifestResourceStream("FrameCapture.update-public.pem")!;
        using var reader = new System.IO.StreamReader(resource);
        key.ImportFromPem(reader.ReadToEnd());
        if (!key.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("Invalid update manifest signature");
        using var doc = JsonDocument.Parse(manifest);
        var root = doc.RootElement;
        string version = root.GetProperty("version").GetString()!, installer = root.GetProperty("installer").GetString()!;
        string sha = root.GetProperty("sha256").GetString()!;
        long size = root.GetProperty("length").GetInt64();
        if (root.GetProperty("schema").GetInt32() != 1 || root.GetProperty("repository").GetString() != Repository
            || !System.Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision > 0
            || parsed.ToString(3) != version || installer != $"FrameCapture-Setup-{version}-x64.exe"
            || size is < 1024 or > 536870912 || sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
            throw new System.IO.InvalidDataException("Invalid update manifest");
        return new(version, installer, size, sha.ToLowerInvariant());
    }
}
