using System.Diagnostics;
using System.IO;
using FrameCapture.Encoding;

static partial class SelfTest
{
    static void VideoComponentChecks(string? ffmpeg = null)
    {
        string args = VideoEncoder.BuildArgs(641, 401, 59.94, VideoFormat.Mp4, "out.mp4", mediaFoundation: true);
        Chk("video-lgpl-h264-mf-args", args.Contains("-c:v h264_mf -hw_encoding 0") && args.Contains("-pix_fmt nv12")
            && !args.Contains("libx264") && !args.Contains("-preset") && args.Contains("+faststart")
            && args.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2") && args.Contains("-framerate 59.94"));
        Chk("video-mf-bitrate-bounded", args.Contains("-b:v 4000000") &&
            VideoEncoder.BuildArgs(7680, 4320, 240, VideoFormat.Mp4, "out.mp4", mediaFoundation: true).Contains("-b:v 100000000"));
        Chk("video-webm-alpha-independent-of-mp4-encoder",
            VideoEncoder.BuildArgs(640, 400, 30, VideoFormat.WebM, "out.webm", alpha: true, mediaFoundation: true)
                .Contains("-c:v libvpx-vp9 -pix_fmt yuva420p -auto-alt-ref 0"));
        if (ffmpeg == null) return;
        string root = Path.Combine(Path.GetTempPath(), "video-component-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string mp4 = Path.Combine(root, "odd.mp4");
            VideoEncoder.Encode(MakeVidFrames(6, 65, 49), ffmpeg, mp4, VideoFormat.Mp4, 24);
            Chk("video-component-real-mp4-odd-region", new FileInfo(mp4).Length > 300);
            var frames = MakeVidFrames(6, 64, 48);
            foreach (var frame in frames)
                for (int i = 3; i < 32 * 4; i += 4) frame.Bgra[i] = 0;
            string webm = Path.Combine(root, "alpha.webm");
            VideoEncoder.Encode(frames, ffmpeg, webm, VideoFormat.WebM, 24);
            string raw = Path.Combine(root, "decoded.bgra");
            using var decode = Process.Start(new ProcessStartInfo(ffmpeg,
                $"-v error -c:v libvpx-vp9 -i \"{webm}\" -frames:v 1 -f rawvideo -pix_fmt bgra \"{raw}\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
            })!;
            if (!decode.WaitForExit(30000)) { decode.Kill(true); throw new TimeoutException("Component decode timed out"); }
            var pixels = File.ReadAllBytes(raw);
            Chk("video-component-real-webm-alpha", decode.ExitCode == 0 && pixels.Length == 64 * 48 * 4
                && pixels[3] < 50 && pixels[^1] > 240);
            string cancelled = Path.Combine(root, "cancelled.mp4");
            bool stopped = false;
            try { VideoEncoder.Encode(frames, ffmpeg, cancelled, VideoFormat.Mp4, 24, ct: new CancellationToken(true)); }
            catch (OperationCanceledException) { stopped = true; }
            Chk("video-component-cancel-removes-incomplete-output", stopped && !File.Exists(cancelled));
        }
        catch (Exception ex) { Chk("video-component-real-encoding", false, ex.ToString()); }
        finally { Directory.Delete(root, true); }
    }
}
