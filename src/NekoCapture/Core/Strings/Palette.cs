namespace NekoCapture.Core;

/// <summary>可视化配色编辑文案。</summary>
public static partial class K
{
    /// <summary>颜色预览与编辑操作。</summary>
    public static class Palette
    {
        public static readonly Str Hint = new("palette.hint", "悬停查看颜色，点击修改", "Hover to inspect a color; click to edit");
        public static readonly Str SampleWindow = new("palette.sampleWindow", "示例窗口", "Sample window");
        public static readonly Str SampleInput = new("palette.sampleInput", "输入内容", "Input text");
        public static readonly Str RestoreColor = new("palette.restoreColor", "恢复此颜色", "Reset this color");
        public static readonly Str RestoreAccent = new("palette.restoreAccent", "恢复默认强调色", "Reset accent color");
    }
}
