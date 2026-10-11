using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using FrameCapture.Capture;

namespace FrameCapture.Core;

public enum OcrStatus { Success, NoEngine, Empty }

/// <summary>OCR 结果：状态 + 识别文本（Success 时有效）。</summary>
public readonly record struct OcrOutcome(OcrStatus Status, string Text);

/// <summary>
/// Windows 内置 OCR（Windows.Media.Ocr，无需联网/第三方）。引擎优先用户配置文件语言，
/// 回退到任一已装识别语言；都没有则 NoEngine（调用方提示装语言包，绝不抛）。
/// 帧像素在首个 await 之前同步拷入 SoftwareBitmap，故调用方（编辑器）关窗/释放 spool 无竞态。
/// </summary>
public static class OcrService
{
    /// <summary>是否存在可用识别引擎（任一语言）。编辑器可据此提前置灰按钮。</summary>
    public static bool EngineAvailable => GetEngine() is not null;

    static OcrEngine? GetEngine()
    {
        try
        {
            var eng = OcrEngine.TryCreateFromUserProfileLanguages();
            if (eng is not null) return eng;
            foreach (Language l in OcrEngine.AvailableRecognizerLanguages)
            {
                eng = OcrEngine.TryCreateFromLanguage(l);
                if (eng is not null) return eng;
            }
        }
        catch { /* API 不可用 / 无语言包 → 视为无引擎 */ }
        return null;
    }

    /// <summary>识别文本分类：全空白=Empty，否则 Success。纯函数，供决策与单测。</summary>
    public static OcrStatus Classify(string text) =>
        string.IsNullOrWhiteSpace(text) ? OcrStatus.Empty : OcrStatus.Success;

    /// <summary>清理 OCR 文本：统一换行为 \n、每行去行尾空白、连续空行压成一个空行、整体去首尾空白。
    /// 保留行首缩进（OCR 缩进常有意义）。纯函数，供结果窗/剪贴板与单测。</summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new System.Text.StringBuilder();
        int blanks = 0;
        foreach (var ln in lines)
        {
            var t = ln.TrimEnd();
            if (t.Length == 0) { blanks++; if (blanks == 1) sb.Append('\n'); continue; }
            if (blanks > 0) blanks = 0;
            sb.Append(t).Append('\n');
        }
        return sb.ToString().Trim('\n');
    }

    /// <summary>识别一帧的印刷文字。Bgra8 帧直接拷入 SoftwareBitmap（无 PNG 往返）。</summary>
    public static async Task<OcrOutcome> RecognizeAsync(CapturedFrame frame)
    {
        var eng = GetEngine();
        if (eng is null) return new(OcrStatus.NoEngine, "");
        try
        {
            using var bmp = SoftwareBitmap.CreateCopyFromBuffer(
                frame.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, frame.W, frame.H);
            var res = await eng.RecognizeAsync(bmp);
            string text = (res.Text ?? "").Trim();
            return new(Classify(text), text);
        }
        catch { return new(OcrStatus.Empty, ""); }
    }
}
