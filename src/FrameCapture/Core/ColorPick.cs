using System.Globalization;
using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>
/// 自绘调色板的纯数学（SelfTest 覆盖）：HSV 双向换算 + HEX 解析/格式化。
/// 替代系统 ColorDialog 的地基——UI 只做拖拽与绑定，换算全在这里，可离线断言。
/// H∈[0,360)，S/V/A∈[0,1]。
/// </summary>
public static class Hsv
{
    /// <summary>RGB → HSV。</summary>
    /// <param name="r">红通道 0–255。</param>
    /// <param name="g">绿通道 0–255。</param>
    /// <param name="b">蓝通道 0–255。</param>
    /// <returns>H∈[0,360)、S∈[0,1]、V=max/255。纯黑与纯灰阶时 S=0、H 无定义取 0。</returns>
    public static (double H, double S, double V) FromRgb(byte r, byte g, byte b)
    {
        double R = r / 255.0, G = g / 255.0, B = b / 255.0;
        double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B)), d = max - min;
        double h = d switch
        {
            0 => 0,
            var dd when max == R => 60 * (((G - B) / dd) % 6),
            var dd when max == G => 60 * (((B - R) / dd) + 2),
            var dd => 60 * (((R - G) / dd) + 4),
        };
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    /// <summary>HSV → RGB，与 <see cref="FromRgb"/> 互逆（差 8bit 量化误差）。</summary>
    /// <param name="h">色相（度）：任意实数，按 360 取模归一到 [0,360)，负值折正。</param>
    /// <param name="s">饱和度，越界钳到 [0,1]。</param>
    /// <param name="v">明度，越界钳到 [0,1]。</param>
    /// <returns>R/G/B 各 0–255（四舍五入）。alpha 不在此列，由 HEX 侧负责。</returns>
    public static (byte R, byte G, byte B) ToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1); v = Math.Clamp(v, 0, 1);
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        (double r, double g, double b) rgb = h switch
        {
            < 60 => (c, x, 0), < 120 => (x, c, 0), < 180 => (0, c, x),
            < 240 => (0, x, c), < 300 => (x, 0, c), _ => (c, 0, x),
        };
        return ((byte)Math.Round((rgb.r + m) * 255), (byte)Math.Round((rgb.g + m) * 255), (byte)Math.Round((rgb.b + m) * 255));
    }
}

/// <summary>#RGB / #RRGGBB / #RRGGBBAA 解析与最短形式格式化（A=255 时省略 AA）。</summary>
public static class HexColor
{
    /// <summary>解析用户输入的 HEX 颜色。</summary>
    /// <param name="text">3/6/8 位十六进制；前缀 <c>#</c> 可省，也认 <c>0x</c>；3 位按每位翻倍展开。</param>
    /// <returns>带 alpha 的颜色，6 位输入按不透明（A=255）。长度不是 3/6/8 或含非十六进制字符时 null。</returns>
    public static Color? Parse(string? text)
    {
        if (text == null) return null;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = "#" + s[2..];
        if (!s.StartsWith('#')) s = "#" + s;
        s = s[1..];
        if (s.Length == 3) s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length == 6) s += "FF";
        if (s.Length != 8) return null;
        for (int i = 0; i < 8; i++) if (!Uri.IsHexDigit(s[i])) return null;
        return Color.FromArgb(
            Convert.ToByte(s.Substring(6, 2), 16),
            Convert.ToByte(s.Substring(0, 2), 16),
            Convert.ToByte(s.Substring(2, 2), 16),
            Convert.ToByte(s.Substring(4, 2), 16));
    }

    /// <summary>颜色 → 最短 HEX 文本。</summary>
    /// <param name="c">要格式化的颜色。</param>
    /// <returns>不透明时 <c>#RRGGBB</c>，带透明时 <c>#RRGGBBAA</c>；大写，alpha=255 不写冗余尾。</returns>
    public static string Format(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
}
