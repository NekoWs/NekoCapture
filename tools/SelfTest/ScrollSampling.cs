using System.Diagnostics;
using FrameCapture.Core;
using FrameCapture.Overlays;

static partial class SelfTest
{
    static void ScrollSamplingChecks()
    {
        const int width = 160, viewHeight = 480, documentHeight = 12000;
        var document = new byte[width * documentHeight * 4];
        new Random(9421).NextBytes(document);
        for (int i = 3; i < document.Length; i += 4) document[i] = 255;
        byte[] View(int top) => document.AsSpan(top * width * 4, width * viewHeight * 4).ToArray();
        var initial = View(0);
        var image = new ScrollImageBuffer(initial, width, viewHeight, viewHeight);
        Chk("scroll-buffer-initial-is-lossless", image.Height == viewHeight && image.ToArray().SequenceEqual(initial));
        int top = 0, appended = 0; long allocations = 0;
        var watch = Stopwatch.StartNew();
        while (top < documentHeight - viewHeight)
        {
            top = Math.Min(documentHeight - viewHeight, top + 237);
            var next = View(top);
            var tail = image.Tail;
            int overlap = ScrollStitch.FindOverlapExact(tail, width, tail.Length / (width * 4), next, viewHeight);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool accepted = image.Append(next, viewHeight, overlap);
            allocations += GC.GetAllocatedBytesForCurrentThread() - before;
            if (!accepted) break;
            appended++;
        }
        double averageMs = watch.Elapsed.TotalMilliseconds / Math.Max(1, appended);
        var output = image.ToArray();
        Chk("scroll-fast-sequence-keeps-every-row", image.Height == documentHeight && output.SequenceEqual(document),
            $"height={image.Height} expected={documentHeight} segments={appended}");
        long newPixelBytes = (long)(documentHeight - viewHeight) * width * 4;
        Chk("scroll-appending-allocates-new-rows-only", allocations < newPixelBytes + appended * 256 + 4096,
            $"allocated={allocations} new_pixels={newPixelBytes}");
        var skipped = View(2016);
        Chk("scroll-large-frame-gaps-have-no-overlap", ScrollStitch.FindOverlapExact(initial, width, viewHeight,
            skipped, viewHeight) == 0);
        foreach (var size in new[] { (W: 80, H: 120), (W: 400, H: 180), (W: 100, H: 10000) })
        {
            var expected = ScrollPreviewWindow.Viewport(output, width, documentHeight, size.W, size.H);
            var preview = image.Preview(size.W, size.H, documentHeight);
            Chk($"scroll-segment-preview-{size.W}x{size.H}", preview.W == expected.W && preview.H == expected.H
                && preview.Bgra.SequenceEqual(expected.Bgra));
        }
        var snapshot = image.Preview(80, 600, viewHeight);
        var initialPreview = ScrollPreviewWindow.Viewport(initial, width, viewHeight, 80, 600);
        Chk("scroll-preview-snapshot-ignores-later-rows", snapshot.Bgra.SequenceEqual(initialPreview.Bgra));
        int height = image.Height;
        Chk("scroll-buffer-rejects-no-overlap-and-duplicates", !image.Append(initial, viewHeight, 0)
            && !image.Append(initial, viewHeight, viewHeight) && image.Height == height);
        var resumed = new ScrollImageBuffer(output, width, documentHeight, viewHeight);
        Chk("scroll-resume-matches-tail-only", resumed.Tail.SequenceEqual(View(documentHeight - viewHeight))
            && resumed.Height == documentHeight && resumed.ToArray().SequenceEqual(document));
        Console.WriteLine($"SCROLL sampling_frames={appended} average_process_ms={averageMs:F2} appended_bytes={allocations}");
    }
}
