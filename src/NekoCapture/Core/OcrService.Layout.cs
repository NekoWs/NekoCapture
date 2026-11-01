using System.Text;
using System.Windows;
using Windows.Media.Ocr;

namespace NekoCapture.Core;

/// <summary>识别行的阅读顺序与重叠分块拼接。</summary>
public static partial class OcrService
{
    static bool IsCjk(char value) => value is >= '\u2E80' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF';

    static IEnumerable<(OcrLine Line, double Angle)> ChooseOcrLines(OcrResult primary, OcrResult? latin)
    {
        if (primary.Lines.Count == 0 && latin != null)
            return latin.Lines.Select(line => (line, latin.TextAngle ?? 0));
        var used = new HashSet<OcrLine>();
        var result = new List<(OcrLine, double)>();
        foreach (var line in primary.Lines)
        {
            OcrLine chosen = line;
            double angle = primary.TextAngle ?? 0;
            if (latin != null && !line.Text.Any(IsCjk) && line.Text.Any(char.IsAsciiLetter))
            {
                Rect Bounds(OcrLine value)
                {
                    var bounds = Rect.Empty;
                    foreach (var word in value.Words) bounds.Union(new Rect(word.BoundingRect.X, word.BoundingRect.Y,
                        word.BoundingRect.Width, word.BoundingRect.Height));
                    return bounds;
                }
                var bounds = Bounds(line);
                var candidate = latin.Lines.Select(value => (Line: value, Bounds: Bounds(value)))
                    .Where(value => value.Line.Text.Any(char.IsAsciiLetter) && !value.Bounds.IsEmpty)
                    .OrderByDescending(value =>
                    {
                        var overlap = Rect.Intersect(value.Bounds, bounds);
                        return overlap.IsEmpty ? 0 : overlap.Width * overlap.Height / Math.Max(1, bounds.Width * bounds.Height);
                    }).FirstOrDefault();
                if (candidate.Line != null)
                {
                    var overlap = Rect.Intersect(candidate.Bounds, bounds);
                    if (!overlap.IsEmpty && overlap.Width * overlap.Height >= bounds.Width * bounds.Height * 0.6)
                    { chosen = candidate.Line; angle = latin.TextAngle ?? 0; }
                }
            }
            if (used.Add(chosen)) result.Add((chosen, angle));
        }
        return result;
    }

    static OcrLineInfo BuildOcrLine(IEnumerable<(string Text, Rect Bounds)> source, double angle, Point center)
    {
        var words = new List<OcrWordInfo>();
        var text = new StringBuilder();
        foreach (var item in source)
        {
            if (text.Length > 0)
            {
                char previous = text[^1], next = item.Text[0];
                bool punctuation = ",.;:!?%)]}，。；：！？、）】》".Contains(next) || "([{（【《".Contains(previous);
                if (!punctuation && !(IsCjk(previous) && IsCjk(next))) text.Append(' ');
            }
            words.Add(new(item.Text, item.Bounds, text.Length)); text.Append(item.Text);
        }
        return new(text.ToString(), words, angle, center);
    }

    static Rect LineBounds(OcrLineInfo line)
    {
        var bounds = Rect.Empty;
        foreach (var word in line.Words) bounds.Union(word.Bounds);
        return bounds;
    }

    static IReadOnlyList<OcrLineInfo> MergeOcrLines(List<OcrLineInfo> source)
    {
        var ordered = source.OrderBy(line => LineBounds(line).Top).ThenBy(line => LineBounds(line).Left).ToList();
        var result = new List<OcrLineInfo>();
        foreach (var line in ordered)
        {
            var bounds = LineBounds(line);
            int match = Math.Abs(line.Angle) < 0.5 ? result.FindLastIndex(existing =>
            {
                if (Math.Abs(existing.Angle) >= 0.5) return false;
                var other = LineBounds(existing);
                double overlap = Math.Min(bounds.Bottom, other.Bottom) - Math.Max(bounds.Top, other.Top);
                double gap = Math.Max(bounds.Left, other.Left) - Math.Min(bounds.Right, other.Right);
                return overlap > Math.Min(bounds.Height, other.Height) * 0.65
                    && gap < Math.Max(bounds.Height, other.Height) * 2;
            }) : -1;
            if (match < 0) { result.Add(line); continue; }
            var combined = result[match].Words.Concat(line.Words).OrderBy(word => word.Bounds.Left).ToArray();
            var kept = new List<(string Text, Rect Bounds)>();
            foreach (var word in combined)
            {
                if (kept.Any(other =>
                {
                    var intersection = Rect.Intersect(word.Bounds, other.Bounds);
                    return !intersection.IsEmpty && intersection.Width * intersection.Height
                        > Math.Min(word.Bounds.Width * word.Bounds.Height, other.Bounds.Width * other.Bounds.Height) * 0.6;
                })) continue;
                kept.Add((word.Text, word.Bounds));
            }
            result[match] = BuildOcrLine(kept, 0, new Point());
        }
        return result;
    }
}
