using System.Text.Json.Serialization;

namespace NekoCapture.Core;

/// <summary>识别进程的有界输入和结构化输出协议。</summary>
internal static class OcrWorkerProtocol
{
    /// <summary>原始 BGRA 请求的协议标识。</summary>
    internal const int Magic = 0x314F4346;
    /// <summary>单次请求允许的最大原图像素数。</summary>
    internal const long MaxPixels = 40_000_000;
}

/// <summary>一次识别的输出；失败信息仅供日志记录。</summary>
internal sealed class OcrWorkerResult
{
    /// <summary>识别行，保持引擎的阅读顺序。</summary>
    public OcrWorkerLine[] Lines { get; set; } = [];
    /// <summary>失败原因，成功时为空。</summary>
    public string Error { get; set; } = "";
}

/// <summary>文字行和其逐字符定位。</summary>
internal sealed class OcrWorkerLine
{
    /// <summary>整行文字。</summary>
    public string Text { get; set; } = "";
    /// <summary>识别置信度。</summary>
    public double Confidence { get; set; }
    /// <summary>识别字符及其在原图中的四角。</summary>
    public OcrWorkerCharacter[] Characters { get; set; } = [];
}

/// <summary>字符的文字和四边形坐标。</summary>
internal sealed class OcrWorkerCharacter
{
    /// <summary>一个识别 token 的文字。</summary>
    public string Text { get; set; } = "";
    /// <summary>左上、右上、右下、左下的原图坐标，依次存放 X、Y。</summary>
    public double[] Quad { get; set; } = [];
}

/// <summary>识别进程协议的静态 JSON 元数据。</summary>
[JsonSerializable(typeof(OcrWorkerResult))]
internal partial class OcrWorkerJson : JsonSerializerContext;
