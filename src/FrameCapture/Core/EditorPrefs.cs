namespace FrameCapture.Core;

/// <summary>
/// 编辑器「记住上次的工具/颜色/粗细」的纯决策——把颜色/线宽落到预设槽位还是自定义槽位，
/// 以及落盘的 hex 格式。与 WPF/UI 解耦，SelfTest 可断言。
/// 约定：命中预设返回其下标；未命中返回 -1（调用方据此落到自定义槽位）。
/// </summary>
public static class EditorPrefs
{
    /// <summary>颜色按 ARGB 全等匹配预设色板。
    /// 用全等（含 alpha）而非仅 RGB，避免半透明自定义色被误判成不透明预设。</summary>
    /// <param name="a">待匹配颜色的 alpha。</param>
    /// <param name="r">红通道。</param>
    /// <param name="g">绿通道。</param>
    /// <param name="b">蓝通道。</param>
    /// <param name="swatches">预设色板，按 UI 上的槽位顺序排列。</param>
    /// <returns>命中时下标（首个全等项）；未命中 -1，调用方据此选中自定义槽位。</returns>
    public static int ColorSlot(byte a, byte r, byte g, byte b, (byte A, byte R, byte G, byte B)[] swatches)
    {
        for (int i = 0; i < swatches.Length; i++)
            if (swatches[i].A == a && swatches[i].R == r && swatches[i].G == g && swatches[i].B == b) return i;
        return -1;
    }

    /// <summary>线宽命中预设（细/中/粗）返回下标，否则 -1（自定义）。</summary>
    /// <param name="w">当前线宽（逻辑像素）。</param>
    /// <param name="widths">预设线宽表，顺序即工具条挡位顺序。</param>
    /// <returns>命中下标；未命中 -1。容差 0.01px，吸收 float 往返误差。</returns>
    public static int WidthSlot(float w, float[] widths)
    {
        for (int i = 0; i < widths.Length; i++)
            if (Math.Abs(widths[i] - w) < 0.01f) return i;
        return -1;
    }

    /// <summary>颜色 → 落盘格式。</summary>
    /// <param name="a">alpha，参与落盘——半透明标注重开编辑器后要能还原。</param>
    /// <param name="r">红通道。</param>
    /// <param name="g">绿通道。</param>
    /// <param name="b">蓝通道。</param>
    /// <returns><c>#AARRGGBB</c>（大写，固定 9 字符）。</returns>
    public static string ToHex(byte a, byte r, byte g, byte b) => $"#{a:X2}{r:X2}{g:X2}{b:X2}";
}
