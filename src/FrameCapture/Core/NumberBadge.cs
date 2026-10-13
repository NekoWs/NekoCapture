namespace FrameCapture.Core;

/// <summary>序号标注：编号徽章的纯决策，供编辑器放置与单测。</summary>
public static class NumberBadge
{
    /// <summary>下一个编号 = 现有编号最大值 + 1（无则 1）。删除/撤销后自动回退，绝不重号。纯函数供单测。</summary>
    public static int Next(IEnumerable<string> existingTexts)
    {
        int max = 0;
        foreach (var t in existingTexts)
            if (int.TryParse(t, out var v) && v > max) max = v;
        return max + 1;
    }

    /// <summary>徽章直径 = 字号 × 2（容纳 1–3 位数居中）。纯函数供单测。</summary>
    public static double Diameter(double fontPx) => fontPx * 2.0;

    /// <summary>编号数字压在圆圈色上的可读前景：圆圈色偏亮→true（用黑字），偏暗→false（用白字）。
    /// 与宿主 Fluent.OnAccentColor 同款亮度判定；色板含琥珀 #FFB900、白 #FFFFFF 等浅色，写死白字会不可读。
    /// 画布预览与导出烘焙两条路径共用此判定保证 WYSIWYG。纯函数供单测。</summary>
    public static bool NumberOnLightCircle(byte r, byte g, byte b)
        => (0.299 * r + 0.587 * g + 0.114 * b) > 128;
}
