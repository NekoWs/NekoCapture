namespace NekoCapture.Core;

/// <summary>序号标注：编号徽章的纯决策，供编辑器放置与单测。</summary>
public static class NumberBadge
{
    /// <summary>下一个编号 = 现有编号最大值 + 1（无则 1）。删除/撤销后自动回退，绝不重号。</summary>
    /// <param name="existingTexts">已有序号标注的文本；解析不出整数的项（普通文字标注）忽略。</param>
    /// <returns>最大编号 +1；一个数字都没有时为 1。编号只取决于现存标注，与放置顺序无关。</returns>
    public static int Next(IEnumerable<string> existingTexts)
    {
        int max = 0;
        foreach (var t in existingTexts)
            if (int.TryParse(t, out var v) && v > max) max = v;
        return max + 1;
    }

    /// <summary>徽章直径 = 字号 × 2（容纳 1–3 位数居中）。</summary>
    /// <param name="fontPx">序号字号，与 <see cref="AnnFont"/> 给出的帧像素字号同一坐标系。</param>
    /// <returns>圆标直径，单位同 <paramref name="fontPx"/>。</returns>
    public static double Diameter(double fontPx) => fontPx * 2.0;

    /// <summary>编号数字压在圆圈色上的可读前景：圆圈色偏亮→true（用黑字），偏暗→false（用白字）。
    /// 与宿主 Fluent.OnAccentColor 同款亮度判定；色板含琥珀 #FFB900、白 #FFFFFF 等浅色，写死白字会不可读。
    /// 画布预览与导出烘焙两条路径共用此判定保证 WYSIWYG。</summary>
    /// <param name="r">圆圈填充色红通道。</param>
    /// <param name="g">圆圈填充色绿通道。</param>
    /// <param name="b">圆圈填充色蓝通道。</param>
    /// <returns>true = 底色够亮，数字用黑；亮度按 BT.601 加权，阈值 128。</returns>
    public static bool NumberOnLightCircle(byte r, byte g, byte b)
        => (0.299 * r + 0.587 * g + 0.114 * b) > 128;
}
