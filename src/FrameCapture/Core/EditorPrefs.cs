namespace FrameCapture.Core;

/// <summary>
/// R64：编辑器「记住上次的工具/颜色/粗细」的纯决策——把颜色/线宽落到预设槽位还是自定义槽位，
/// 以及落盘的 hex 格式。与 WPF/UI 解耦，SelfTest 可断言。
/// 约定：命中预设返回其下标；未命中返回 -1（调用方据此落到自定义槽位）。
/// </summary>
public static class EditorPrefs
{
    /// <summary>颜色按 ARGB 全等匹配预设色板：命中返回下标，否则 -1（自定义）。
    /// 用全等（含 alpha）而非仅 RGB，避免半透明自定义色被误判成不透明预设。</summary>
    public static int ColorSlot(byte a, byte r, byte g, byte b, (byte A, byte R, byte G, byte B)[] swatches)
    {
        for (int i = 0; i < swatches.Length; i++)
            if (swatches[i].A == a && swatches[i].R == r && swatches[i].G == g && swatches[i].B == b) return i;
        return -1;
    }

    /// <summary>线宽命中预设（细/中/粗）返回下标，否则 -1（自定义）。容差 0.01px。</summary>
    public static int WidthSlot(float w, float[] widths)
    {
        for (int i = 0; i < widths.Length; i++)
            if (Math.Abs(widths[i] - w) < 0.01f) return i;
        return -1;
    }

    /// <summary>颜色 → #AARRGGBB（落盘格式，保留 alpha）。</summary>
    public static string ToHex(byte a, byte r, byte g, byte b) => $"#{a:X2}{r:X2}{g:X2}{b:X2}";
}
