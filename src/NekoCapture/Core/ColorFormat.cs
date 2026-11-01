namespace NekoCapture.Core;

/// <summary>
/// 取色复制格式：选区放大镜按 C 复制光标下像素颜色时的文本形式。
/// 只给 HEX 不够：开发者/设计师常要 rgb/hsl 直接贴进 CSS，故做成可选项。
/// 纯格式化、零依赖，供单测。屏幕像素恒不透明，故不提供带 alpha 的形式。
/// </summary>
public enum ColorCopyFormat { Hex, Rgb, Hsl }

/// <summary>
/// <see cref="ColorCopyFormat"/> 的设置串读写与颜色文本格式化。
/// 设置串恒为小写，Parse 与 ToSetting 互逆。
/// </summary>
public static class ColorFormat
{
    /// <summary>设置字符串（"hex"/"rgb"/"hsl"，大小写不敏感）→ 枚举。</summary>
    /// <param name="s">设置文件里的原始串。null、空白、未知值都按 Hex 处理，
    /// 使缺键与脏值都有确定行为，不会因为设置里多了一个没听过的值就复制失败。</param>
    /// <returns>识别到的格式；未识别时为 <see cref="ColorCopyFormat.Hex"/>。</returns>
    public static ColorCopyFormat Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "rgb" => ColorCopyFormat.Rgb,
        "hsl" => ColorCopyFormat.Hsl,
        _ => ColorCopyFormat.Hex,
    };

    /// <summary>枚举 → 设置字符串（持久化用）。</summary>
    /// <param name="f">要写回设置的格式。</param>
    /// <returns>小写串 "hex"/"rgb"/"hsl"；未列出的枚举值落 "hex"。</returns>
    public static string ToSetting(ColorCopyFormat f) => f switch
    {
        ColorCopyFormat.Rgb => "rgb",
        ColorCopyFormat.Hsl => "hsl",
        _ => "hex",
    };

    /// <summary>RGB → 指定格式的文本。</summary>
    /// <param name="r">红通道，越界值钳到 0–255。</param>
    /// <param name="g">绿通道，越界值钳到 0–255。</param>
    /// <param name="b">蓝通道，越界值钳到 0–255。</param>
    /// <param name="fmt">目标文本形式。</param>
    /// <returns>可直接粘贴的 CSS：<c>#RRGGBB</c>（大写）/ <c>rgb(r, g, b)</c> / <c>hsl(H, S%, L%)</c>。</returns>
    public static string Format(int r, int g, int b, ColorCopyFormat fmt)
    {
        r = Clamp(r); g = Clamp(g); b = Clamp(b);
        return fmt switch
        {
            ColorCopyFormat.Rgb => $"rgb({r}, {g}, {b})",
            ColorCopyFormat.Hsl => Hsl(r, g, b),
            _ => $"#{r:X2}{g:X2}{b:X2}",
        };
    }

    /// <summary>RGB → hsl(H, S%, L%)（标准转换，四舍五入到整数）。</summary>
    static string Hsl(int r, int g, int b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd));
        double l = (max + min) / 2.0, h = 0, s = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == rd) h = (gd - bd) / d + (gd < bd ? 6 : 0);
            else if (max == gd) h = (bd - rd) / d + 2;
            else h = (rd - gd) / d + 4;
            h /= 6.0;
        }
        int hh = (int)Math.Round(h * 360), ss = (int)Math.Round(s * 100), ll = (int)Math.Round(l * 100);
        return $"hsl({hh}, {ss}%, {ll}%)";
    }

    static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}
