using Windows.Globalization;
using Windows.Media.Ocr;
using FrameCapture.Capture;
using K = FrameCapture.Core.K;

namespace FrameCapture.Core;

/// <summary>识别结局，区分成功、空结果、缺少语言、缺少可选组件与执行失败。</summary>
public enum OcrStatus { Success, NoEngine, Empty, ComponentMissing, Failed }

/// <summary>识别结果的预览、复制、错误或空结果提示路由。</summary>
public enum OcrRoute { ResultWindow, CopyThenConfirm, NoEngineHint, EmptyHint }

/// <summary>
/// 系统 OCR 或可选 small 模型的离线识别。系统引擎按用户语言识别并复核英文行。
/// 小字增强、图像分块与识别位置统一在后台生成；small 在单独的 CPU 进程执行。
/// 引擎创建、像素复制与识别均在后台执行；传入帧的像素在任务完成前保持只读。
/// </summary>
public static partial class OcrService
{
    /// <summary>系统内置识别引擎的设置值。</summary>
    public const string SystemEngine = "system";
    /// <summary>可选 PP-OCRv6 small 引擎的设置值。</summary>
    public const string SmallEngine = "small";
    /// <summary>当前选择的识别引擎是否可用。</summary>
    public static bool EngineAvailable => SettingsService.Shared?.Current.OcrEngine == SmallEngine
        ? Services.OcrSmallComponent.Available : GetEngine() is not null;

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

    /// <summary>按文本是否为空白区分成功与空结果。</summary>
    /// <param name="text">引擎返回的文本，可为 null。</param>
    /// <returns>只会是 Success 或 Empty——NoEngine 由取引擎失败那条路径给出，不经此处。</returns>
    public static OcrStatus Classify(string text) =>
        string.IsNullOrWhiteSpace(text) ? OcrStatus.Empty : OcrStatus.Success;

    /// <summary>把识别状态转换为短句反馈。</summary>
    /// <param name="status">识别结局。</param>
    /// <param name="textLen">识别出的字数，仅 Success 时进文案。</param>
    /// <returns>可直接显示的短句，不含状态前缀。</returns>
    public static string Feedback(OcrStatus status, int textLen) => status switch
    {
        OcrStatus.Success => Loc.F(K.Capture.RecognizedCharactersCopied, textLen),
        OcrStatus.NoEngine => Loc.T(K.Capture.NoOcrLanguagePack),
        OcrStatus.ComponentMissing => Loc.T(K.Ocr.Missing),
        OcrStatus.Failed => Loc.T(K.Common.RecognitionFailed),
        _ => Loc.T(K.Capture.NoTextRecognized),
    };

    /// <summary>成功时按预览偏好分流，其余状态显示对应提示。</summary>
    /// <param name="status">识别结局。</param>
    /// <param name="showResult">用户设置里是否要求弹出结果窗。</param>
    /// <returns>四种路由之一；只有 Success 会被 showResult 影响，其余状态与它无关。</returns>
    public static OcrRoute Route(OcrStatus status, bool showResult) => status switch
    {
        OcrStatus.Success => showResult ? OcrRoute.ResultWindow : OcrRoute.CopyThenConfirm,
        OcrStatus.NoEngine or OcrStatus.ComponentMissing or OcrStatus.Failed => OcrRoute.NoEngineHint,
        _ => OcrRoute.EmptyHint,
    };

    /// <summary>清理 OCR 文本：统一换行为 \n、每行去行尾空白、连续空行压成一个空行、整体去首尾空白。
    /// 保留行首缩进（OCR 缩进常有意义）。纯函数，供结果窗/剪贴板与单测。</summary>
    /// <param name="raw">引擎原始文本，可为 null。</param>
    /// <returns>归一化文本；输入为空或全空白时返回空串。</returns>
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

    /// <summary>后台识别印刷文字，保留原图和文字位置；小字放大，超限图分块串行处理。</summary>
    /// <param name="frame">待识别帧，须为 Bgra8 且 W/H 有效；任务完成前不得改写像素。</param>
    /// <param name="includeImage">是否为选字预览保留原图；直接复制时可关闭。</param>
    /// <param name="engine">引擎设置值；为空时读取当前设置。</param>
    /// <returns>文字、位置和原图；缺少引擎、缺少组件或执行失败时返回对应状态。</returns>
    public static async Task<OcrOutcome> RecognizeAsync(CapturedFrame frame, bool includeImage = true, string? engine = null)
    {
        using var activity = Services.BackgroundWork.Hold();
        engine ??= SettingsService.Shared?.Current.OcrEngine ?? SystemEngine;
        return await Task.Run(() => engine == SmallEngine ? RecognizeSmallAsync(frame, includeImage)
            : RecognizeCoreAsync(frame, includeImage)).ConfigureAwait(false);
    }

}
