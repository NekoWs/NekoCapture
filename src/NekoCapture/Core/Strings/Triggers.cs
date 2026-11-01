namespace NekoCapture.Core;

/// <summary>
/// 文案 · 触发方式的后缀：按下、松手、轻点…。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，目录与取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>触发方式的后缀：按下、松手、轻点…。</summary>
    public static partial class Triggers
    {
        public static readonly Str TriggerClick = new("triggers.triggerClick", "单击", "Click");
        public static readonly Str TriggerDoubleclick = new("triggers.triggerDoubleclick", "双击", "Double-click");
        public static readonly Str TriggerHold = new("triggers.triggerHold", "按住", "Hold");
        public static readonly Str TriggerHoldrelease = new("triggers.triggerHoldrelease", "松手", "Hold release");
        public static readonly Str TriggerKeydown = new("triggers.triggerKeydown", "按下", "Press");
        public static readonly Str TriggerKeyup = new("triggers.triggerKeyup", "松开", "Release");
        public static readonly Str TriggerLongpress = new("triggers.triggerLongpress", "长按", "Long press");
        public static readonly Str TriggerTap = new("triggers.triggerTap", "轻点", "Tap");
        public static readonly Str TriggerTripleclick = new("triggers.triggerTripleclick", "三击", "Triple-click");
    }
}
