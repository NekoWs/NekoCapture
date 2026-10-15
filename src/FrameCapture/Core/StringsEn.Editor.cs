namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 编辑器窗口：欢迎引导、底部工具条、时间轴与 HUD 片段、导出与胶片条。
/// 工具名与命令名沿用 <c>StringsEn.Keys.cs</c> 那一套词，工具条上的 Rectangle / Ellipse / Mosaic
/// 与快捷键页列的是同一个词，用户在一处学会另一处不用再猜。
/// 工具条按钮的 tooltip 是悬停时唯一的线索，动词开头、说清按下去会发生什么。
/// </summary>
static partial class StringsEn
{
    /// <summary>编辑器窗口的译名。</summary>
    static readonly (string Zh, string En)[] Editor =
    {
        // 首次打开的欢迎引导
        ("欢迎用 FrameCapture 编辑器", "Welcome to the FrameCapture editor"),
        ("标注", "Annotate"),
        ("底部工具条选 线/矩形 R/圆 O/箭头/手绘/文字 T/马赛克 M，再点同一工具退出绘制；选中 V 可点选已画标注拖动/删除",
            "Pick a tool on the bottom bar: Line, Rectangle R, Ellipse O, Arrow, Freehand, Text T, Mosaic M. "
            + "Press the same tool again to stop drawing. Select V clicks an annotation so you can move or delete it."),
        ("裁剪工具拖框，框外压暗并显示网格提示，松开即应用；Ctrl+Z 可撤销",
            "Drag with the Crop tool: everything outside the box dims and a grid appears, release to apply. Ctrl+Z undoes it."),
        ("确定/取消", "Done / cancel"),
        ("Enter 写入剪贴板 · Esc 关闭 · 图钉=总在最前",
            "Enter copies to the clipboard · Esc closes · the pin keeps the window on top"),
        ("更多", "More"),
        ("F1 完整帮助，任意按钮悬停有说明", "F1 opens the full help; every button explains itself on hover"),
        ("知道了", "Got it"),
        ("开始使用", "Start here"),

        // 画布与时间轴上的即时提示
        ("拖拽选片段 · [ / ] 设入出点", "Drag to pick a range · [ and ] set the in and out points"),
        ("拖拽选择片段", "Drag to select a range"),
        ("松开应用裁剪", "Release to apply the crop"),

        // 底部工具条
        ("上一秒", "Back one second"),
        ("播放/暂停 · 空格", "Play / pause · Space"),
        ("下一秒", "Forward one second"),
        ("生成胶片条", "Film strip"),
        ("确定", "Done"),
        ("总在最前", "Always on top"),
        ("帮助与快捷键", "Help and shortcuts"),
        ("颜色", "Color"),
        ("RGBA 调色板", "RGBA palette"),
        ("细", "Thin"),
        ("中", "Medium"),
        ("粗", "Bold"),
        ("{0}笔画", "{0} stroke"),
        ("自定义线宽…", "Custom stroke width…"),
        ("字号减小", "Smaller text"),
        ("字号增大", "Larger text"),
        ("撤销上一笔 · 可撤 {0} 步", "Undo the last edit · {0} steps to undo"),
        ("重做上一笔 · 可重做 {0} 步", "Redo the last edit · {0} steps to redo"),
        ("最近用色 {0}", "Recent color {0}"),

        // HUD 片段：拼在帧号与时长后面，前导空格是分隔符，保留在键与值里
        ("  裁剪 {0}×{1}", "  Crop {0}×{1}"),
        ("  导出 {0}–{1}", "  Export {0}–{1}"),
        ("  ⚠内存预算已丢 {0} 帧", "  ⚠{0} frames dropped by the memory budget"),

        // 导出、保存与文字识别
        ("导出 {0}…", "Exporting {0}…"),
        ("导出 {0}… {1}/{2}", "Exporting {0}… {1}/{2}"),
        ("已导出 {0}", "Exported {0}"),
        ("导出失败", "Export failed"),
        ("未找到 ffmpeg", "ffmpeg not found"),
        ("未找到 ffmpeg，无法导出视频 — 安装方法见帮助，按 F1",
            "ffmpeg not found, so video export is unavailable — the help explains how to install it, press F1"),
        ("MP4 视频|*.mp4|WebM 视频|*.webm|GIF 动图|*.gif",
            "MP4 video|*.mp4|WebM video|*.webm|GIF animation|*.gif"),
        ("生成胶片条…", "Building film strip…"),
        ("胶片条已复制 · {0} 帧", "Film strip copied · {0} frames"),
        ("生成胶片条失败", "Film strip failed"),
        ("剪贴板被占用，生成失败", "The clipboard is busy, so nothing was created"),
    };
}
