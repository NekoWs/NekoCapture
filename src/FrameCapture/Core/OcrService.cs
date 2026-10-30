using Windows.Globalization;
using Windows.Media.Ocr;
using FrameCapture.Capture;
using K = FrameCapture.Core.K;

namespace FrameCapture.Core;

/// <summary>一次 OCR 的结局。NoEngine=系统里没有任何可用识别语言包；Empty=引擎跑了但没提出文字。</summary>
public enum OcrStatus { Success, NoEngine, Empty }

/// <summary>OCR 结果的用户可见路由（纯决策，供单测）。编辑器与「手势/范围 OCR」共用，保证两处一致：
/// ResultWindow=开结果窗；CopyThenConfirm=直接复制并轻确认；NoEngineHint/EmptyHint=对应提示。</summary>
public enum OcrRoute { ResultWindow, CopyThenConfirm, NoEngineHint, EmptyHint }

/// <summary>
/// Windows 内置 OCR 离线识别，优先使用用户语言；纯英文行由已安装的英文引擎复核。
/// 小字增强、图像分块与识别位置统一在后台生成；无可用语言时返回 NoEngine。
/// 引擎创建、像素复制与识别均在后台执行；传入帧的像素在任务完成前保持只读。
/// </summary>
public static partial class OcrService
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
    /// <param name="text">引擎返回的文本，可为 null。</param>
    /// <returns>只会是 Success 或 Empty——NoEngine 由取引擎失败那条路径给出，不经此处。</returns>
    public static OcrStatus Classify(string text) =>
        string.IsNullOrWhiteSpace(text) ? OcrStatus.Empty : OcrStatus.Success;

    /// <summary>OCR 结果的用户可读反馈文案（纯函数，供单测）。编辑器轻提示与贴图浮层共用，保证两处措辞一致：
    /// Success=复制提示（带字数），NoEngine=装语言包指引，Empty=未识别到文字。</summary>
    /// <param name="status">识别结局。</param>
    /// <param name="textLen">识别出的字数，仅 Success 时进文案。</param>
    /// <returns>可直接显示的短句，不含状态前缀。</returns>
    public static string Feedback(OcrStatus status, int textLen) => status switch
    {
        OcrStatus.Success => Loc.F(K.Capture.RecognizedCharactersCopied, textLen),
        OcrStatus.NoEngine => Loc.T(K.Capture.NoOcrLanguagePack),
        _ => Loc.T(K.Capture.NoTextRecognized),
    };

    /// <summary>OCR 结果路由决策（纯函数，供单测）：Success 时按「是否显示结果窗」分流到结果窗或复制+轻确认；
    /// NoEngine/Empty 各自走提示。编辑器与手势/范围 OCR 共用，杜绝两处逻辑漂移。</summary>
    /// <param name="status">识别结局。</param>
    /// <param name="showResult">用户设置里是否要求弹出结果窗。</param>
    /// <returns>四种路由之一；只有 Success 会被 showResult 影响，其余状态与它无关。</returns>
    public static OcrRoute Route(OcrStatus status, bool showResult) => status switch
    {
        OcrStatus.Success => showResult ? OcrRoute.ResultWindow : OcrRoute.CopyThenConfirm,
        OcrStatus.NoEngine => OcrRoute.NoEngineHint,
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
    /// <returns>识别结果；无引擎为 NoEngine，识别失败或无文字为 Empty，成功为 Success（文本已 Trim）。</returns>
    public static async Task<OcrOutcome> RecognizeAsync(CapturedFrame frame, bool includeImage = true)
    {
        using var activity = Services.BackgroundWork.Hold();
        return await Task.Run(() => RecognizeCoreAsync(frame, includeImage)).ConfigureAwait(false);
    }

}
