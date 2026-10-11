namespace FrameCapture.Core;

/// <summary>序号标注（R73）：编号徽章的纯决策，供编辑器放置与单测。</summary>
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
}
