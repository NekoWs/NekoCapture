namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 快捷键页：分组标题、编辑器命令名、绘图工具名，以及这一页自己的说明文字。
/// 命令名与工具名同时是编辑器工具条提示与帮助窗的文案来源，本表只保证快捷键页这一窗完整；
/// 编辑器与帮助窗整窗迁移时读的是同一批键，译名直接复用。
/// 顿号、分号与书名号是中文的列举记法，英文里换成逗号、分号与弯引号——句子结构整个交给译名。
/// </summary>
static partial class StringsEn
{
    /// <summary>快捷键页与编辑器键位表的译名。</summary>
    static readonly (string Zh, string En)[] Keys =
    {
        // 分组标题
        ("绘制工具", "Drawing tools"),
        ("编辑", "Editing"),
        ("导出", "Export"),
        ("视图", "View"),
        ("播放", "Playback"),
        ("时间轴裁剪", "Timeline trim"),
        ("窗口", "Window"),

        // 绘图工具名（ToolCatalog 的短名）
        ("选中", "Select"),
        ("直线", "Line"),
        ("矩形", "Rectangle"),
        ("圆圈", "Ellipse"),
        ("箭头", "Arrow"),
        ("手绘", "Freehand"),
        ("荧光笔", "Highlighter"),
        ("文字", "Text"),
        ("马赛克", "Mosaic"),
        ("裁剪", "Crop"),
        ("序号", "Number"),
        ("浏览 / 退出绘制", "Browse / stop drawing"),

        // 编辑器命令名
        ("删除选中对象", "Delete selected object"),
        ("撤销", "Undo"),
        ("重做", "Redo"),
        ("取色与自定义颜色", "Pick and custom color"),
        ("线宽选择", "Stroke width"),
        ("保存当前帧", "Save current frame"),
        ("复制当前帧", "Copy current frame"),
        ("钉到屏幕", "Pin to screen"),
        ("导出录制", "Export recording"),
        ("保存视觉暂留合成图", "Save afterimage composite"),
        ("放大", "Zoom in"),
        ("缩小", "Zoom out"),
        ("适配窗口", "Fit to window"),
        ("逐像素 1:1", "Pixel-perfect 1:1"),
        ("播放 / 暂停", "Play / pause"),
        ("上一帧", "Previous frame"),
        ("下一帧", "Next frame"),
        ("往前一秒", "Back one second"),
        ("往后一秒", "Forward one second"),
        ("播放加速", "Speed up"),
        ("播放减速", "Slow down"),
        ("播放常速", "Normal speed"),
        ("裁剪起点设为当前帧", "Set trim start to this frame"),
        ("裁剪终点设为当前帧", "Set trim end to this frame"),
        ("清除裁剪区间", "Clear trim range"),
        ("确定并复制", "Confirm and copy"),
        ("取消并关闭", "Cancel and close"),

        // 这一页自己的文字
        ("全局键位如下，增删改请打开下方手势配置器。",
            "Global keys are listed below. To add, remove, or change one, open the gesture configurator underneath."),
        ("   打开手势配置器…", "   Open the gesture configurator…"),
        ("编辑器窗口里的键位。点一下键位框，再按下新键即改；按 Esc 取消这个键位，加号再加一个按法。改完立即生效。",
            "Keys for the editor window. Click a key box, then press a new key to rebind it; press Esc to clear that key, or the plus sign to add another way to press it. Changes apply immediately."),
        ("恢复", "Reset"),
        ("全部恢复默认键位", "Restore all default keys"),
        ("与{0}同键 {1}", "Shares a key with {0} — {1}"),
        ("、", ", "),
        ("；", "; "),
        ("「{0}」", "“{0}”"),
    };
}
