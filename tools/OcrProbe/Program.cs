using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfBitmapFrame = System.Windows.Media.Imaging.BitmapFrame;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

// OCR 可用性探针：①列出已装识别语言 ②用户语言引擎 ③渲染已知文字→OCR→比对
static class Program
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine("=== OcrProbe ===");
        try
        {
            var langs = OcrEngine.AvailableRecognizerLanguages;
            Console.WriteLine($"AvailableRecognizerLanguages = {langs.Count}");
            foreach (Language l in langs) Console.WriteLine($"  - {l.LanguageTag}");

            var eng = OcrEngine.TryCreateFromUserProfileLanguages();
            Console.WriteLine($"TryCreateFromUserProfileLanguages = {(eng is null ? "NULL" : eng.RecognizerLanguage.LanguageTag)}");
            if (eng is null)
            {
                // 回退：取第一个可用语言
                foreach (Language l in langs)
                {
                    eng = OcrEngine.TryCreateFromLanguage(l);
                    if (eng is not null) { Console.WriteLine($"fallback engine = {l.LanguageTag}"); break; }
                }
            }
            if (eng is null) { Console.WriteLine("RESULT: NO ENGINE — OCR 不可用"); return; }

            // 渲染已知文字到位图
            string sample = "Hello OCR 12345 FrameCapture";
            var ft = new FormattedText(sample,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                40, Brushes.Black, 1.0);
            int w = (int)Math.Ceiling(ft.Width) + 20, h = (int)Math.Ceiling(ft.Height) + 20;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h)); dc.DrawText(ft, new Point(10, 10)); }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(WpfBitmapFrame.Create(rtb));
            using var ms = new MemoryStream(); enc.Save(ms); ms.Position = 0;

            // PNG → SoftwareBitmap → OCR
            var result = Recognize(ms.ToArray(), eng).GetAwaiter().GetResult();
            Console.WriteLine($"RESULT: recognized = \"{result}\"");
            Console.WriteLine(result.Contains("12345") && result.Contains("FrameCapture") ? "RESULT: OCR WORKS" : "RESULT: OCR ran but text mismatch");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PROBE EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        }
    }

    static async Task<string> Recognize(byte[] png, OcrEngine eng)
    {
        using var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(png.AsBuffer());
        ras.Seek(0);
        var decoder = await WinBitmapDecoder.CreateAsync(ras);
        using var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var res = await eng.RecognizeAsync(bmp);
        return res.Text;
    }
}
