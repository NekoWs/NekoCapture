// Fluent partial：配色可读性——强调色上的反白、对比度、强调色文字的落点色。
using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>Fluent 工厂的配色 partial：凡是「这个颜色压在那个底色上读不读得清」的判断都收在这里。
/// 全部成员须在 UI 线程调用。</summary>
public static partial class Fluent
{
    /// <summary>accent 底色上的反白色：WCAG 相对亮度（sRGB 线性化）低于 0.35 → 白字，否则黑字。
    /// 线性化不可省：按 0.299/0.587/0.114 直接加权会在 128 一刀切，把 #7F75FF 这类中等饱和紫判成黑字，
    /// 紫底黑字糊成一片。</summary>
    /// <param name="a">底色，即 accent 实心按钮的背景。</param>
    /// <returns>纯白或纯黑两档之一，保证任何 accent 上图标都可读。</returns>
    public static Color OnAccentColor(Color a)
    {
        static double L(double c) { c /= 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        double lum = 0.2126 * L(a.R) + 0.7152 * L(a.G) + 0.0722 * L(a.B);
        return lum < 0.35 ? Colors.White : Colors.Black;
    }

    /// <summary>两个颜色的 WCAG 相对亮度对比度（1..21）。</summary>
    /// <param name="a">前景或背景之一。</param>
    /// <param name="b">另一个。</param>
    /// <returns>对比度比值。</returns>
    public static double ContrastRatio(Color a, Color b)
    {
        static double L(double c) { c /= 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        static double Lum(Color c) => 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
        double la = Lum(a), lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>把带透明的前景压平到不透明底色上，得到眼睛真正看到的颜色：
    /// 调色板里的次要文字带 Alpha，直接拿原始 RGB 去算对比度会算出一个不存在的颜色。</summary>
    /// <param name="fg">带 Alpha 的前景。</param>
    /// <param name="bg">不透明底色。</param>
    /// <returns>压平后的不透明颜色。</returns>
    public static Color Flatten(Color fg, Color bg)
    {
        double k = fg.A / 255.0;
        return Color.FromRgb((byte)(fg.R * k + bg.R * (1 - k)), (byte)(fg.G * k + bg.G * (1 - k)),
            (byte)(fg.B * k + bg.B * (1 - k)));
    }

    /// <summary>把颜色按通道等比压暗（保留色相），用于让强调色在浅底上读得清。</summary>
    /// <param name="c">原色。</param>
    /// <param name="k">每通道乘数，0..1。</param>
    /// <returns>压暗后的颜色。</returns>
    public static Color Darken(Color c, double k)
        => Color.FromArgb(c.A, (byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));

    /// <summary>当前颜色在卡片底与窗口底里最不利那张上的对比度：文字两张底都会落到。</summary>
    /// <param name="c">候选的文字颜色。</param>
    /// <returns>两张底里较小的那个对比度。</returns>
    public static double WorstContrast(Color c)
        => Math.Min(ContrastRatio(c, Theme.CardBg), ContrastRatio(c, Theme.WindowBg));

    /// <summary>强调色文字的落点色：暗色主题下强调色当正文色会随配色失读（深紫压在 #2D2D2D 上只有
    /// 3.3:1），够读就原样用、不够退回主题正文色——深底上再压暗只会更糊，没有第三条路。
    /// 亮色主题不能照抄强调色：出厂那抹浅蓝压在浅底上只有 1.9:1，读起来像被水洗过，
    /// 所以按需要压暗到 4.5:1 为止，色相保持不变。
    /// 压在强调色实心底上的文字走 <see cref="OnAccentColor"/>（按底色亮度取黑白），两者不可混用。</summary>
    /// <returns>读得清的强调色，或暗色主题下退回的 <see cref="Theme.Text"/>。</returns>
    public static Color AccentText()
    {
        var c = Theme.Accent;
        // 出厂浅蓝压在深底上 6.5:1、压在浅底上 1.9:1：同一条规则按实际底色判，两种主题都不误伤。
        if (WorstContrast(c) >= 4.5) return c;
        if (Theme.IsDark) return Theme.Text;
        // 每轮向黑收 10%，最多 24 轮（够把任何色相压到近黑）。
        for (var i = 0; i < 24 && WorstContrast(c) < 4.5; i++) c = Darken(c, 0.90);
        return c;
    }
}
