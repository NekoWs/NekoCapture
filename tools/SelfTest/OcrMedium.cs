using System.Diagnostics;
using System.Reflection;
using NekoCapture.Core;

static partial class SelfTest
{
    static void OcrMediumChecks()
    {
        try
        {
            var settings = new AppSettings { OcrEngine = OcrService.MediumEngine }; settings.Normalize();
            Chk("ocr-medium-preference-is-valid", settings.OcrEngine == OcrService.MediumEngine);
            Chk("ocr-medium-keeps-system-default", new AppSettings().OcrEngine == OcrService.SystemEngine);
            var component = typeof(OcrService).Assembly.GetType("NekoCapture.Services.OcrModelComponent")!;
            const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var medium = component.GetProperty("Medium", statics)!.GetValue(null)!;
            var small = component.GetProperty("Small", statics)!.GetValue(null)!;
            ((Task)component.GetMethod("InstallAsync", instance)!.Invoke(medium, null)!).GetAwaiter().GetResult();
            bool available = (bool)component.GetProperty("Available", instance)!.GetValue(medium)!;
            Chk("ocr-medium-installs-verified-model-package", available);
            Chk("ocr-medium-install-does-not-install-small", !(bool)component.GetProperty("Available", instance)!.GetValue(small)!);
            if (!available) return;
            const string expected = "截图与录制 识别文字 清晰准确 NekoCapture 12345";
            var frame = OcrSample(expected, 14, false);
            var watch = Stopwatch.StartNew();
            var result = OcrService.RecognizeAsync(frame, false, OcrService.MediumEngine).GetAwaiter().GetResult();
            Console.WriteLine($"MEDIUM errors={OcrDistance(expected, result.Text)}, ms={watch.ElapsedMilliseconds}; {result.Text}");
            Chk("ocr-medium-recognizes-text-with-character-positions", result.Status == OcrStatus.Success && result.Lines.Count > 0
                && result.Lines.SelectMany(line => line.Words).All(word => word.CharacterBounds?.Count > 0));
            Chk("ocr-medium-copy-mode-does-not-retain-image", result.Image == null);
            Chk("ocr-medium-worker-exits-after-request", Process.GetProcessesByName("NekoCapture.OcrWorker").Length == 0);
        }
        catch (Exception error) { Chk("ocr-medium-integration", false, error.ToString()); }
    }
}
