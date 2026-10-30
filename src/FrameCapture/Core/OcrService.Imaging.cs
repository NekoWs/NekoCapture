using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using FrameCapture.Capture;
using FrameCapture.Services;

namespace FrameCapture.Core;

/// <summary>OCR 的灰度增强、重叠分块和原图坐标映射。</summary>
public static partial class OcrService
{
    const int OcrScale = 2, OcrPadding = 12, OcrOverlap = 192;
    static readonly SemaphoreSlim RecognitionGate = new(1, 1);

    private static async Task<OcrOutcome> RecognizeCoreAsync(CapturedFrame frame, bool includeImage)
    {
        await RecognitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var engine = GetEngine();
            if (engine == null) return new(OcrStatus.NoEngine, "");
            var englishLanguage = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(language => language.LanguageTag.StartsWith("en-", StringComparison.OrdinalIgnoreCase));
            var english = engine.RecognizerLanguage.LanguageTag.StartsWith("en-", StringComparison.OrdinalIgnoreCase) || englishLanguage == null
                ? null : OcrEngine.TryCreateFromLanguage(englishLanguage);
            if (frame.W <= 0 || frame.H <= 0 || (long)frame.W * frame.H * 4 > frame.Bgra.LongLength)
                return new(OcrStatus.Empty, "");
            int limit = (int)OcrEngine.MaxImageDimension / OcrScale - OcrPadding * 2;
            var xs = TileStarts(frame.W, limit); var ys = TileStarts(frame.H, limit);
            var lines = new List<OcrLineInfo>();
            for (int yi = 0; yi < ys.Count; yi++)
            for (int xi = 0; xi < xs.Count; xi++)
            {
                int x = xs[xi], y = ys[yi], w = Math.Min(limit, frame.W - x), h = Math.Min(limit, frame.H - y);
                // 重叠区按词中心归属，保留完整词框并避免重复文字。
                var owned = new Rect(xi == 0 ? 0 : (xs[xi - 1] + limit + x) / 2.0,
                    yi == 0 ? 0 : (ys[yi - 1] + limit + y) / 2.0, 0, 0);
                owned.Width = (xi + 1 == xs.Count ? frame.W : (x + w + xs[xi + 1]) / 2.0) - owned.X;
                owned.Height = (yi + 1 == ys.Count ? frame.H : (y + h + ys[yi + 1]) / 2.0) - owned.Y;
                using var bitmap = PrepareTile(frame, x, y, w, h);
                var result = await engine.RecognizeAsync(bitmap);
                var center = new Point(x + w / 2.0, y + h / 2.0);
                var latin = english != null && (result.Lines.Count == 0 || result.Lines.Any(line => line.Text.Any(char.IsAsciiLetter)))
                    ? await english.RecognizeAsync(bitmap) : null;
                foreach (var (line, angle) in ChooseOcrLines(result, latin))
                {
                    var rotate = new RotateTransform(angle, center.X, center.Y);
                    var words = new List<(string Text, Rect Bounds)>();
                    foreach (var word in line.Words)
                    {
                        var box = word.BoundingRect;
                        var bounds = new Rect(x + box.X / OcrScale - OcrPadding, y + box.Y / OcrScale - OcrPadding,
                            box.Width / OcrScale, box.Height / OcrScale);
                        var middle = rotate.Transform(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
                        if (!string.IsNullOrWhiteSpace(word.Text) && bounds.Width > 0 && bounds.Height > 0
                            && middle.X >= owned.Left && middle.X < owned.Right && middle.Y >= owned.Top && middle.Y < owned.Bottom)
                            words.Add((word.Text, bounds));
                    }
                    if (words.Count > 0) lines.Add(BuildOcrLine(words, angle, center));
                }
            }
            var ordered = MergeOcrLines(lines);
            string text = Normalize(string.Join("\n", ordered.Select(line => line.Text)));
            return new(Classify(text), text) { Lines = ordered, Image = includeImage && text.Length > 0 ? BgraImage.ToSource(frame) : null };
        }
        catch (Exception error)
        {
            Trace.Log("ocr: " + error.Message);
            return new(OcrStatus.Empty, "");
        }
        finally { RecognitionGate.Release(); }
    }

    static List<int> TileStarts(int length, int limit)
    {
        if (limit <= OcrOverlap) throw new InvalidOperationException("OCR image limit is too small.");
        if (length <= limit) return new() { 0 };
        int count = (int)Math.Ceiling((length - limit) / (double)(limit - OcrOverlap)) + 1;
        return Enumerable.Range(0, count).Select(index => (int)Math.Round((length - limit) * index / (double)(count - 1))).ToList();
    }

    static SoftwareBitmap PrepareTile(CapturedFrame frame, int left, int top, int width, int height)
    {
        int w = width + OcrPadding * 2, h = height + OcrPadding * 2;
        var gray = new byte[w * h];
        var histogram = new int[256];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int source = ((top + y) * frame.W + left + x) * 4;
            int alpha = frame.Bgra[source + 3];
            int value = (frame.Bgra[source] * 29 + frame.Bgra[source + 1] * 150 + frame.Bgra[source + 2] * 77) >> 8;
            value = (value * alpha + 255 * (255 - alpha)) / 255;
            gray[(y + OcrPadding) * w + x + OcrPadding] = (byte)value; histogram[value]++;
        }
        int count = width * height;
        int Percentile(double fraction)
        {
            int total = 0, threshold = Math.Max(1, (int)(count * fraction));
            for (int i = 0; i < histogram.Length; i++) if ((total += histogram[i]) >= threshold) return i;
            return 255;
        }
        int low = Percentile(0.001), high = Percentile(0.999);
        bool invert = Percentile(0.5) < 128;
        bool stretch = high - low >= 16;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int index = y * w + x;
            if (x < OcrPadding || y < OcrPadding || x >= w - OcrPadding || y >= h - OcrPadding)
                gray[index] = 255;
            else
            {
                int value = stretch ? Math.Clamp((gray[index] - low) * 255 / (high - low), 0, 255) : gray[index];
                gray[index] = (byte)(invert ? 255 - value : value);
            }
        }
        var sourceBitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, gray, w);
        var scaled = new TransformedBitmap(sourceBitmap, new ScaleTransform(OcrScale, OcrScale));
        var pixels = new byte[scaled.PixelWidth * scaled.PixelHeight];
        scaled.CopyPixels(pixels, scaled.PixelWidth, 0);
        return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Gray8, scaled.PixelWidth, scaled.PixelHeight);
    }
}
