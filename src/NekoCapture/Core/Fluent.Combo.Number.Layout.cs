using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace NekoCapture.Core;

/// <summary>按小数位对齐滚轮，最终位置取自原生输入框的字符几何。</summary>
public static partial class Fluent
{
    private sealed record NumberGlyph(char Value, double X, double Width);

    private sealed record NumberLayout(Dictionary<int, NumberGlyph> Digits, NumberGlyph Dot, bool HasDecimal, double Pitch)
    {
        /// <summary>最高整数位。</summary>
        internal int LastInteger => Digits.Keys.Max();
        /// <summary>最低小数位；无小数时为个位。</summary>
        internal int LastFraction => Digits.Keys.Min();

        /// <summary>取得实有数字或位于可见数字两侧的零位。</summary>
        /// <param name="place">十进制位次，个位为零。</param>
        /// <returns>数字、位置与宽度。</returns>
        internal NumberGlyph At(int place)
        {
            if (Digits.TryGetValue(place, out var glyph)) return glyph;
            double x = place >= 0 ? Digits[0].X - place * Pitch : Dot.X + Dot.Width + (-place - 1) * Pitch;
            return new('0', x, Pitch);
        }
    }

    private static TextBlock NumberLabel(TextBox text, string value)
    {
        var label = new TextBlock { Text = value, FontFamily = text.FontFamily, FontSize = text.FontSize,
            FontWeight = text.FontWeight, FontStyle = text.FontStyle, FontStretch = text.FontStretch,
            Foreground = text.Foreground };
        Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return label;
    }

    private static NumberLayout LayoutNumber(TextBox text, string value, bool native)
    {
        var digits = new Dictionary<int, NumberGlyph>();
        double pitch = NumberLabel(text, "0").DesiredSize.Width;
        double dotWidth = NumberLabel(text, ".").DesiredSize.Width;
        var first = text.Text.Length > 0 ? text.GetRectFromCharacterIndex(0) : Rect.Empty;
        double origin = first.IsEmpty ? text.Padding.Left : first.Left;
        int dot = value.IndexOf('.'); bool hasDecimal = dot >= 0;
        if (!hasDecimal) dot = value.Length;
        var separator = new NumberGlyph('.', origin + NumberLabel(text, value[..dot]).DesiredSize.Width, dotWidth);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            double x = origin + (i == 0 ? 0 : NumberLabel(text, value[..i]).DesiredSize.Width);
            double width = NumberLabel(text, character.ToString()).DesiredSize.Width;
            if (native && text.Text == value)
            {
                var leading = text.GetRectFromCharacterIndex(i);
                var trailing = text.GetRectFromCharacterIndex(i, true);
                if (!leading.IsEmpty) x = leading.Left;
                if (!leading.IsEmpty && !trailing.IsEmpty && trailing.Left > leading.Left) width = trailing.Left - leading.Left;
            }
            if (character == '.') separator = new(character, x, width);
            else if (character is >= '0' and <= '9') digits[i < dot ? dot - i - 1 : dot - i] = new(character, x, width);
        }
        return new(digits, separator, hasDecimal, pitch);
    }
}
