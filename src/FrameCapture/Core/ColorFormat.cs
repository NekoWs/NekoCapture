namespace FrameCapture.Core;

/// <summary>
/// 取色复制格式：选区放大镜按 C 复制光标下像素颜色时的文本形式。
/// QQ 只给 HEX；开发者/设计师常要 rgb/hsl 直接贴进 CSS，故做成可选项。
/// 纯格式化、零依赖，供单测。屏幕像素恒不透明，故不提供带 alpha 的形式。
/// </summary>
public enum ColorCopyFormat { Hex, Rgb, Hsl }

public static class ColorFormat
{
    /// <summary>设置字符串（"hex"/"rgb"/"hsl"，大小写不敏感）→ 枚举；无法识别回退 Hex（向后兼容旧配置）。</summary>
    public static ColorCopyFormat Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "rgb" => ColorCopyFormat.Rgb,
        "hsl" => ColorCopyFormat.Hsl,
        _ => ColorCopyFormat.Hex,
    };

    /// <summary>枚举 → 设置字符串（持久化用）。</summary>
    public static string ToSetting(ColorCopyFormat f) => f switch
    {
        ColorCopyFormat.Rgb => "rgb",
        ColorCopyFormat.Hsl => "hsl",
        _ => "hex",
    };

    /// <summary>RGB → 指定格式的文本。分量夹到 0–255。</summary>
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
