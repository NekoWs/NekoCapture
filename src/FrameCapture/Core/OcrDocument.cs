using System.Windows;
using System.Windows.Media.Imaging;

namespace FrameCapture.Core;

/// <summary>一个识别词及其在未旋转原图坐标系中的位置。</summary>
/// <param name="Text">识别文字。</param>
/// <param name="Bounds">原图像素矩形。</param>
/// <param name="Start">词在所属行中的 UTF-16 起始位置。</param>
public sealed record OcrWordInfo(string Text, Rect Bounds, int Start)
{
    /// <summary>与文字的 grapheme 顺序对应的字符框；为空时由词框估算。</summary>
    public IReadOnlyList<Rect>? CharacterBounds { get; init; }
}

/// <summary>识别行及其覆盖原图时所需的旋转信息。</summary>
/// <param name="Text">按阅读顺序排列的行文字。</param>
/// <param name="Words">按阅读顺序排列的识别词。</param>
/// <param name="Angle">围绕中心的顺时针旋转角度。</param>
/// <param name="Center">原图像素中的旋转中心。</param>
public sealed record OcrLineInfo(string Text, IReadOnlyList<OcrWordInfo> Words, double Angle, Point Center);

/// <summary>识别状态、全文、原图和可选择的文字位置；位图已冻结。</summary>
/// <param name="Status">识别结局。</param>
/// <param name="Text">按行排列的全文。</param>
public readonly record struct OcrOutcome(OcrStatus Status, string Text)
{
    /// <summary>与原图像素对齐的文字行。</summary>
    public IReadOnlyList<OcrLineInfo> Lines { get; init; } = Array.Empty<OcrLineInfo>();
    /// <summary>识别成功时用于原位选字的只读原图。</summary>
    public BitmapSource? Image { get; init; }
}
