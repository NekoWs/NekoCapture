using System.IO;
using NekoCapture.Core;

static partial class SelfTest
{
    private static void RebrandChecks()
    {
        string root = Path.Combine(SettingsService.Dir, "rebrand-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, ProductPaths.LegacyName), current = Path.Combine(root, "NekoCapture");
        Directory.CreateDirectory(Path.Combine(source, "components", "ocr"));
        File.WriteAllText(Path.Combine(source, "settings.json"), "{\"ConfigVersion\":1,\"Fps\":47}");
        File.WriteAllBytes(Path.Combine(source, "components", "ocr", "det.onnx"), new byte[] { 1, 2, 3, 4 });
        Chk("rebrand-profile-moves-to-new-identity", ProductPaths.Migrate(root) == current
            && File.ReadAllText(Path.Combine(current, "settings.json")) == File.ReadAllText(Path.Combine(source, "settings.json")));
        Chk("rebrand-components-preserved-with-source-backup", File.ReadAllBytes(Path.Combine(current, "components", "ocr", "det.onnx"))
            .SequenceEqual(new byte[] { 1, 2, 3, 4 }) && Directory.Exists(source));
        File.WriteAllText(Path.Combine(current, "settings.json"), "new settings");
        Chk("rebrand-existing-profile-not-overwritten", ProductPaths.Migrate(root) == current
            && File.ReadAllText(Path.Combine(current, "settings.json")) == "new settings");
        Chk("rebrand-product-assembly-identity", typeof(AppSettings).Assembly.GetName().Name == "NekoCapture"
            && typeof(AppSettings).Namespace == "NekoCapture.Core");
        Chk("rebrand-update-source-identity", NekoCapture.Updates.UpdateRelease.Repository == "NekoWs/NekoCapture"
            && new NekoCapture.Updates.UpdateRelease("0.9.36", "NekoCapture-Setup-0.9.36-x64.exe", 2048, new string('a', 64))
                .DownloadUrl == "https://github.com/NekoWs/NekoCapture/releases/download/v0.9.36/NekoCapture-Setup-0.9.36-x64.exe");
        Chk("rebrand-video-package-identity", NekoCapture.Encoding.FfmpegComponentInfo.Mp4.DownloadUrl
            == "https://github.com/NekoWs/NekoCapture/releases/download/v0.9.36/NekoCapture-video-mp4-1-win-x64.zip");
    }
}
