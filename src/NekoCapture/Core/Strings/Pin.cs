namespace NekoCapture.Core;

/// <summary>
/// 文案 · 贴图窗。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>贴图窗的文案。</summary>
    public static partial class Pin
    {
        public static readonly Str CloseAllPins = new("pin.closeAllPins", "关闭全部贴图 ({0})", "Close All Pins ({0})");
        public static readonly Str NothingToRefresh = new("pin.nothingToRefresh", "无法更新", "Nothing to refresh");
        public static readonly Str Opacity = new("pin.opacity", "不透明度", "Opacity");
        public static readonly Str RefreshContent = new("pin.refreshContent", "更新内容", "Refresh Content");
        public static readonly Str RefreshFailed = new("pin.refreshFailed", "更新失败", "Refresh failed");
        public static readonly Str Rotate = new("pin.rotate", "旋转 90°", "Rotate 90°");
        public static readonly Str SaveAs = new("pin.saveAs", "另存为…", "Save As…");
        public static readonly Str Saved = new("pin.saved", "已保存", "Saved");
        public static readonly Str Updated = new("pin.updated", "已更新", "Updated");
    }
}
