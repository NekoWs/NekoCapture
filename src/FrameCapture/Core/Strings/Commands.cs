namespace FrameCapture.Core;

/// <summary>
/// 文案 · 编辑器命令名与分组标题。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，目录与取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>编辑器命令名与分组标题。</summary>
    public static partial class Commands
    {
        public static readonly Str CloseName = new("commands.closeName", "取消并关闭", "Cancel and close");
        public static readonly Str ColorCustomName = new("commands.colorCustomName", "取色与自定义颜色", "Picker & custom color");
        public static readonly Str CommitName = new("commands.commitName", "确定并复制", "Confirm and copy");
        public static readonly Str DeleteObjectName = new("commands.deleteObjectName", "删除选中对象", "Delete selected object");
        public static readonly Str GroupDrawingtools = new("commands.groupDrawingtools", "绘制工具", "Drawing tools");
        public static readonly Str GroupEditing = new("commands.groupEditing", "编辑", "Editing");
        public static readonly Str GroupExport = new("commands.groupExport", "导出", "Export");
        public static readonly Str GroupPlayback = new("commands.groupPlayback", "播放", "Playback");
        public static readonly Str GroupTimelinetrim = new("commands.groupTimelinetrim", "时间轴裁剪", "Timeline trim");
        public static readonly Str GroupView = new("commands.groupView", "视图", "View");
        public static readonly Str GroupWindow = new("commands.groupWindow", "窗口", "Window");
        public static readonly Str PlayToggleName = new("commands.playToggleName", "播放 / 暂停", "Play / pause");
        public static readonly Str SecNextName = new("commands.secNextName", "往后一秒", "Forward one second");
        public static readonly Str SecPrevName = new("commands.secPrevName", "往前一秒", "Back one second");
        public static readonly Str SpeedDownName = new("commands.speedDownName", "播放减速", "Slow down");
        public static readonly Str SpeedResetName = new("commands.speedResetName", "播放常速", "Normal speed");
        public static readonly Str SpeedUpName = new("commands.speedUpName", "播放加速", "Speed up");
        public static readonly Str TrimAName = new("commands.trimAName", "裁剪起点设为当前帧", "Set trim start to this frame");
        public static readonly Str TrimBName = new("commands.trimBName", "裁剪终点设为当前帧", "Set trim end to this frame");
        public static readonly Str TrimClearName = new("commands.trimClearName", "清除裁剪区间", "Clear trim range");
        public static readonly Str WidthPopupName = new("commands.widthPopupName", "线宽选择", "Stroke width");
        public static readonly Str ZoomFitName = new("commands.zoomFitName", "适配窗口", "Fit to window");
        public static readonly Str ZoomInName = new("commands.zoomInName", "放大", "Zoom in");
        public static readonly Str ZoomOneName = new("commands.zoomOneName", "逐像素 1:1", "Pixel-perfect 1:1");
        public static readonly Str ZoomOutName = new("commands.zoomOutName", "缩小", "Zoom out");
    }
}
