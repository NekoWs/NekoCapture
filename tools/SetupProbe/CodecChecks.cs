using System.IO.Compression;
using FrameCapture.CodecSetup;
using FrameCapture.Encoding;

internal static partial class Program
{
    private static void CodecChecks(string root, ref int fail)
    {
        string prefix = Path.GetFileNameWithoutExtension(FfmpegComponentInfo.Archive) + "/";
        string archive = Path.Combine(root, "codec.zip");
        void Zip(params (string Name, string Content)[] entries)
        {
            if (File.Exists(archive)) File.Delete(archive);
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            foreach (var pair in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(pair.Name).Open());
                writer.Write(pair.Content);
            }
        }
        bool Rejected(Action action)
        {
            try { action(); return false; }
            catch (InvalidDataException) { return true; }
            catch (IOException) { return true; }
            catch (OperationCanceledException) { return true; }
        }
        Zip((prefix + "bin/ffmpeg.exe", "exe"), (prefix + "bin/avcodec.dll", "dll"),
            (prefix + "bin/ffprobe.exe", "probe"), (prefix + "LICENSE.txt", "license"));
        string extracted = Path.Combine(root, "codec-extract");
        ComponentInstaller.Extract(archive, extracted, CancellationToken.None);
        Report(ref fail, File.ReadAllText(Path.Combine(extracted, "LICENSE.txt")) == "license" &&
            File.Exists(Path.Combine(extracted, "bin", "avcodec.dll")) &&
            !File.Exists(Path.Combine(extracted, "bin", "ffprobe.exe")), "codec-runtime-and-license-only", "");

        Zip((prefix + "../outside.exe", "unsafe"));
        Report(ref fail, Rejected(() => ComponentInstaller.Extract(archive, Path.Combine(root, "traversal"), CancellationToken.None))
            && !File.Exists(Path.Combine(root, "outside.exe")), "codec-zip-traversal-rejected", "");
        Zip((prefix + "C:/outside.exe", "unsafe"));
        Report(ref fail, Rejected(() => ComponentInstaller.Extract(archive, Path.Combine(root, "drive"), CancellationToken.None)),
            "codec-zip-drive-rejected", "");
        Zip((prefix + "bin/ffmpeg.exe", "link"));
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) zip.Entries[0].ExternalAttributes = unchecked((int)0xA0000000);
        Report(ref fail, Rejected(() => ComponentInstaller.Extract(archive, Path.Combine(root, "symlink"), CancellationToken.None)),
            "codec-zip-symlink-rejected", "");
        Zip((prefix + "bin/ffmpeg.exe", "one"), (prefix + "bin/ffmpeg.exe", "two"));
        Report(ref fail, Rejected(() => ComponentInstaller.Extract(archive, Path.Combine(root, "duplicate"), CancellationToken.None)),
            "codec-duplicate-entry-rejected", "");
        Zip((prefix + "bin/ffmpeg.exe", "missing-license"));
        Report(ref fail, Rejected(() => ComponentInstaller.Extract(archive, Path.Combine(root, "license"), CancellationToken.None)),
            "codec-license-required", "");
        Report(ref fail, Rejected(() => ComponentInstaller.Extract(archive, Path.Combine(root, "cancelled"), new CancellationToken(true))),
            "codec-extraction-cancellable", "");

        string target = Path.Combine(root, "codec-installed");
        Directory.CreateDirectory(target);
        string sentinel = Path.Combine(target, "current.txt");
        File.WriteAllText(sentinel, "current");
        string tampered = Path.Combine(root, "tampered.zip");
        using (var file = File.Create(tampered)) file.SetLength(FfmpegComponentInfo.DownloadBytes);
        Report(ref fail, Rejected(() => ComponentInstaller.InstallArchive(tampered, target, root, CancellationToken.None))
            && File.ReadAllText(sentinel) == "current", "codec-bad-sha-keeps-current-install", "");
        Report(ref fail, Rejected(() => ComponentInstaller.Commit(Path.Combine(root, "missing-content"), target))
            && File.ReadAllText(sentinel) == "current", "codec-failed-commit-restores-current-install", "");
        string valid = Path.Combine(root, "valid-content");
        Directory.CreateDirectory(valid);
        File.WriteAllText(Path.Combine(valid, "current.txt"), "replacement");
        ComponentInstaller.Commit(valid, target);
        Report(ref fail, File.ReadAllText(sentinel) == "replacement" && !Directory.Exists(valid)
            && Directory.GetDirectories(root, "codec-installed.backup-*").Length == 0, "codec-commit-replaces-complete-directory", "");

        string? supplied = Environment.GetEnvironmentVariable("FC_CODEC_TEST_ARCHIVE");
        if (Environment.GetEnvironmentVariable("FC_CODEC_TEST_DOWNLOAD") == "1")
        {
            supplied = Path.Combine(root, "download.zip");
            var download = typeof(ComponentInstaller).GetMethod("Download", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static)!;
            ((Task)download.Invoke(null, new object[] { supplied, new Progress<double>(), CancellationToken.None })!)
                .GetAwaiter().GetResult();
        }
        if (string.IsNullOrWhiteSpace(supplied)) { Console.WriteLine("  SKIP codec-real-archive: FC_CODEC_TEST_ARCHIVE is unset"); return; }
        string realWork = Path.Combine(root, "real-work");
        Directory.CreateDirectory(realWork);
        ComponentInstaller.InstallArchive(supplied!, target, realWork, CancellationToken.None);
        Report(ref fail, File.Exists(Path.Combine(target, "bin", "ffmpeg.exe")) &&
            File.ReadAllText(Path.Combine(target, "BUILD.txt")).Contains(FfmpegComponentInfo.Sha256),
            "codec-real-lgpl-mp4-webm-install", "");
    }
}
