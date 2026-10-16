namespace FrameCapture.Core;

/// <summary>
/// 文案 · 标注与视频编辑器。一行一句：稳定 id、中文原文、英文译名。
/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。
/// </summary>
public static partial class K
{
    /// <summary>标注与视频编辑器的文案。</summary>
    public static partial class Editor
    {
        public static readonly Str AlwaysOnTop = new("editor.alwaysOnTop", "总在最前", "Always on top");
        public static readonly Str Annotate = new("editor.annotate", "标注", "Annotate");
        public static readonly Str BackOneSecond = new("editor.backOneSecond", "上一秒", "Back one second");
        public static readonly Str Bold = new("editor.bold", "粗", "Bold");
        public static readonly Str BuildingFilmStrip = new(
            "editor.buildingFilmStrip",
            "生成胶片条…",
            "Building film strip…");
        public static readonly Str Color = new("editor.color", "颜色", "Color");
        public static readonly Str CopyCurrentFrame = new("editor.copyCurrentFrame", "复制当前帧", "Copy current frame");
        public static readonly Str Create = new("editor.create", "生成", "Create");
        public static readonly Str Crop = new("editor.crop", "  裁剪 {0}×{1}", "  Crop {0}×{1}");
        public static readonly Str Crop2 = new("editor.crop2", "裁剪", "Crop");
        public static readonly Str CustomStrokeWidth = new(
            "editor.customStrokeWidth",
            "自定义线宽…",
            "Custom stroke width…");
        public static readonly Str Done = new("editor.done", "确定", "Done");
        public static readonly Str DoneCancel = new("editor.doneCancel", "确定/取消", "Done / cancel");
        public static readonly Str DragToPickARange = new(
            "editor.dragToPickARange",
            "拖拽选片段 · [ / ] 设入出点",
            "Drag to pick a range · [ and ] set the in and out points");
        public static readonly Str DragToSelectARange = new(
            "editor.dragToSelectARange",
            "拖拽选择片段",
            "Drag to select a range");
        public static readonly Str DragWithTheCropTool = new(
            "editor.dragWithTheCropTool",
            "裁剪工具拖框，框外压暗并显示网格提示，松开即应用；Ctrl+Z 可撤销",
            "Drag with the Crop tool: everything outside the box dims and a grid appears, release to apply. Ctrl+Z undoes it.");
        public static readonly Str EnterCopiesToTheClipboard = new(
            "editor.enterCopiesToTheClipboard",
            "Enter 写入剪贴板 · Esc 关闭 · 图钉=总在最前",
            "Enter copies to the clipboard · Esc closes · the pin keeps the window on top");
        public static readonly Str EveryNFramesRowsCols = new(
            "editor.everyNFramesRowsCols",
            "每 {0} 帧取一次 · {1} 行 × {2} 格",
            "One every {0} frames · {1} rows × {2} cells");
        public static readonly Str Export = new("editor.export", "  导出 {0}–{1}", "  Export {0}–{1}");
        public static readonly Str Exported = new("editor.exported", "已导出 {0}", "Exported {0}");
        public static readonly Str ExportFailed = new("editor.exportFailed", "导出失败", "Export failed");
        public static readonly Str Exporting = new("editor.exporting", "导出 {0}…", "Exporting {0}…");
        public static readonly Str Exporting2 = new("editor.exporting2", "导出 {0}… {1}/{2}", "Exporting {0}… {1}/{2}");
        public static readonly Str ExportRecording = new("editor.exportRecording", "导出录制", "Export recording");
        public static readonly Str F1OpensTheFullHelp = new(
            "editor.f1OpensTheFullHelp",
            "F1 完整帮助，任意按钮悬停有说明",
            "F1 opens the full help; every button explains itself on hover");
        public static readonly Str FfmpegNotFound = new("editor.ffmpegNotFound", "未找到 ffmpeg", "ffmpeg not found");
        public static readonly Str FfmpegNotFoundSoVideo = new(
            "editor.ffmpegNotFoundSoVideo",
            "未找到 ffmpeg，无法导出视频 — 安装方法见帮助，按 F1",
            "ffmpeg not found, so video export is unavailable — the help explains how to install it, press F1");
        public static readonly Str FilmStrip = new("editor.filmStrip", "生成胶片条", "Film strip");
        public static readonly Str FilmStripCopiedFrames = new(
            "editor.filmStripCopiedFrames",
            "胶片条已复制 · {0} 帧",
            "Film strip copied · {0} frames");
        public static readonly Str FilmStripFailed = new("editor.filmStripFailed", "生成胶片条失败", "Film strip failed");
        public static readonly Str ForwardOneSecond = new("editor.forwardOneSecond", "下一秒", "Forward one second");
        public static readonly Str FramesDroppedByTheMemory = new(
            "editor.framesDroppedByTheMemory",
            "  ⚠内存预算已丢 {0} 帧",
            "  ⚠{0} frames dropped by the memory budget");
        public static readonly Str FramesValue = new("editor.framesValue", "{0} 帧", "{0} frames");
        public static readonly Str GotIt = new("editor.gotIt", "知道了", "Got it");
        public static readonly Str HelpAndShortcuts = new("editor.helpAndShortcuts", "帮助与快捷键", "Help and shortcuts");
        public static readonly Str HowManyFrames = new("editor.howManyFrames", "抽多少帧", "How many frames");
        public static readonly Str LargerText = new("editor.largerText", "字号增大", "Larger text");
        public static readonly Str Medium = new("editor.medium", "中", "Medium");
        public static readonly Str More = new("editor.more", "更多", "More");
        public static readonly Str Mp4VideoMp4WebmVideo = new(
            "editor.mp4VideoMp4WebmVideo",
            "MP4 视频|*.mp4|WebM 视频|*.webm|GIF 动图|*.gif",
            "MP4 video|*.mp4|WebM video|*.webm|GIF animation|*.gif");
        public static readonly Str NextFrame = new("editor.nextFrame", "下一帧", "Next frame");
        public static readonly Str PickATool = new(
            "editor.pickATool",
            "底部工具条选 线/矩形 R/圆 O/箭头/手绘/文字 T/马赛克 M，再点同一工具退出绘制；选中 V 可点选已画标注拖动/删除",
            "Pick a tool on the bottom bar: Line, Rectangle R, Ellipse O, Arrow, Freehand, Text T, Mosaic M. Press the same tool again to stop drawing. Select V clicks an annotation so you can move or delete it.");
        public static readonly Str PinToScreen = new("editor.pinToScreen", "钉到屏幕", "Pin to screen");
        public static readonly Str PlayPauseSpace = new(
            "editor.playPauseSpace",
            "播放/暂停 · 空格",
            "Play / pause · Space");
        public static readonly Str PreviousFrame = new("editor.previousFrame", "上一帧", "Previous frame");
        public static readonly Str RecentColor = new("editor.recentColor", "最近用色 {0}", "Recent color {0}");
        public static readonly Str Redo = new("editor.redo", "重做", "Redo");
        public static readonly Str RedoTheLastEditSteps = new(
            "editor.redoTheLastEditSteps",
            "重做上一笔 · 可重做 {0} 步",
            "Redo the last edit · {0} steps to redo");
        public static readonly Str ReleaseToApplyTheCrop = new(
            "editor.releaseToApplyTheCrop",
            "松开应用裁剪",
            "Release to apply the crop");
        public static readonly Str RgbaPalette = new("editor.rgbaPalette", "RGBA 调色板", "RGBA palette");
        public static readonly Str SamplesTheCurrentRecordingInto = new(
            "editor.samplesTheCurrentRecordingInto",
            "从当前录制抽样拼成一张逐帧图，含裁剪与标注，共 {0} 帧，确定后复制到剪贴板",
            "Samples the current recording into one image, crop and annotations included, {0} frames in all. Choosing Done copies it to the clipboard.");
        public static readonly Str SaveAfterimageComposite = new(
            "editor.saveAfterimageComposite",
            "保存视觉暂留合成图",
            "Save afterimage composite");
        public static readonly Str SaveCurrentFrame = new("editor.saveCurrentFrame", "保存当前帧", "Save current frame");
        public static readonly Str SmallerText = new("editor.smallerText", "字号减小", "Smaller text");
        public static readonly Str StartHere = new("editor.startHere", "开始使用", "Start here");
        public static readonly Str Stroke = new("editor.stroke", "{0}笔画", "{0} stroke");
        public static readonly Str TheClipboardIsBusySo = new(
            "editor.theClipboardIsBusySo",
            "剪贴板被占用，生成失败",
            "The clipboard is busy, so nothing was created");
        public static readonly Str Thin = new("editor.thin", "细", "Thin");
        public static readonly Str Undo = new("editor.undo", "撤销", "Undo");
        public static readonly Str UndoTheLastEditSteps = new(
            "editor.undoTheLastEditSteps",
            "撤销上一笔 · 可撤 {0} 步",
            "Undo the last edit · {0} steps to undo");
        public static readonly Str WelcomeToTheFramecaptureEditor = new(
            "editor.welcomeToTheFramecaptureEditor",
            "欢迎用 FrameCapture 编辑器",
            "Welcome to the FrameCapture editor");
    }
}
