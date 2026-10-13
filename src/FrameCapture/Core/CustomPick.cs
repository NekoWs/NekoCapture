namespace FrameCapture.Core;

/// <summary>自定义线宽 / 最近自定义色的纯逻辑决策表（SelfTest 覆盖，UI 只做转发）。</summary>
public static class CustomPick
{
    /// <summary>自定义线宽的允许区间（逻辑像素）。</summary>
    /// <value>下限 1 保证看得见，上限 24 免得粗到看不出形状。</value>
    public const float WidthMin = 1f, WidthMax = 24f;

    /// <summary>线宽选择：idx 落在预设区间取预设值，否则视为自定义槽位。</summary>
    /// <param name="idx">工具条挡位下标；负数与越界都按自定义槽位处理。</param>
    /// <param name="presets">预设线宽表（细/中/粗），顺序即 UI 挡位顺序。</param>
    /// <param name="custom">用户自填线宽，仅自定义槽位生效，钳到 <see cref="WidthMin"/>–<see cref="WidthMax"/>。</param>
    /// <returns>该挡位的线宽（逻辑像素）。</returns>
    public static float Width(int idx, float[] presets, float custom)
        => idx >= 0 && idx < presets.Length ? presets[idx] : Math.Clamp(custom, WidthMin, WidthMax);

    /// <summary>最近自定义色：新色置顶、大小写不敏感去重、上限 cap 条；空/非法项丢弃。</summary>
    /// <param name="existing">已存的最近色（hex 串），最近的排在前面；可为 null。</param>
    /// <param name="hex">本次要记的颜色；null 或空串直接忽略。</param>
    /// <param name="cap">列表条数上限，超出上限的靠后项一律丢弃。</param>
    /// <returns>新建的列表（不修改入参）：新色在首、统一大写、重复色只留最靠前的那条。</returns>
    public static List<string> RememberColor(IReadOnlyList<string>? existing, string? hex, int cap = 6)
    {
        var list = new List<string>();
        void Add(string? c)
        {
            if (string.IsNullOrEmpty(c) || list.Count >= cap) return;
            var u = c.ToUpperInvariant();
            if (!list.Contains(u)) list.Add(u);
        }
        Add(hex);
        if (existing != null) foreach (var c in existing) Add(c);
        return list;
    }
}
