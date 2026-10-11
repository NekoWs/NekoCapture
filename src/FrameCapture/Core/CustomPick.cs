namespace FrameCapture.Core;

/// <summary>自定义线宽 / 最近自定义色的纯逻辑决策表（SelfTest 覆盖，UI 只做转发）。</summary>
public static class CustomPick
{
    public const float WidthMin = 1f, WidthMax = 24f;

    /// <summary>线宽选择：idx 落在预设区间 → 预设值；否则（自定义槽位）→ 自定义值钳到 1..24。</summary>
    public static float Width(int idx, float[] presets, float custom)
        => idx >= 0 && idx < presets.Length ? presets[idx] : Math.Clamp(custom, WidthMin, WidthMax);

    /// <summary>最近自定义色：新色置顶、大小写不敏感去重、上限 cap 条；空/非法项丢弃。</summary>
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
