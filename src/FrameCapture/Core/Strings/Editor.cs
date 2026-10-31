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
        public static readonly Str StillWorkspace = new("editor.stillWorkspace", "截图编辑", "Image editor");
        public static readonly Str VideoWorkspace = new("editor.videoWorkspace", "录制编辑", "Recording editor");
        public static readonly Str Browse = new("editor.browse", "浏览", "Browse");
        public static readonly Str SaveOutput = new("editor.saveOutput", "保存", "Save");
        public static readonly Str ContinueBehavior = new("editor.continueBehavior", "应用编辑并继续行为", "Apply edits and continue");
        public static readonly Str CopyAndComplete = new("editor.copyAndComplete", "复制并完成", "Copy and finish");
        public static readonly Str BrowseHint = new("editor.browseHint", "Ctrl + 滚轮缩放 · 中键拖动平移", "Ctrl + wheel to zoom · Middle-drag to pan");
        public static readonly Str SelectHint = new("editor.selectHint", "点击标注以移动、调整或删除", "Select an annotation to move, resize, or delete");
        public static readonly Str CropHint = new("editor.cropHint", "拖动裁剪 · 撤销可恢复", "Drag to crop · Undo to restore");
        public static readonly Str ResetCrop = new("editor.resetCrop", "还原裁剪", "Reset crop");
        public static readonly Str FitView = new("editor.fitView", "适配", "Fit");
        public static readonly Str TrimStart = new("editor.trimStart", "设为起点", "Set start");
        public static readonly Str TrimEnd = new("editor.trimEnd", "设为终点", "Set end");
        public static readonly Str ResetTrim = new("editor.resetTrim", "还原选段", "Reset trim");
        public static readonly Str FullRecording = new("editor.fullRecording", "完整录制", "Full recording");
        public static readonly Str SelectedDuration = new("editor.selectedDuration", "选段 {0} · {1} 帧", "Selection {0} · {1} frames");
        public static readonly Str FramePosition = new("editor.framePosition", "第 {0} / {1} 帧", "Frame {0} / {1}");
        public static readonly Str CancelExport = new("editor.cancelExport", "取消导出", "Cancel export");
        public static readonly Str CancelingExport = new("editor.cancelingExport", "正在取消…", "Canceling…");
        public static readonly Str ExportProgress = new("editor.exportProgress", "{0} · {1}%", "{0} · {1}%");
        public static readonly Str CompletedChanges = new("editor.completedChanges", "已编辑 {0} 处", "{0} edits");
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

        public static readonly Str Crop2 = new("editor.crop2", "裁剪", "Crop");
        public static readonly Str CustomStrokeWidth = new(
            "editor.customStrokeWidth",
            "自定义线宽…",
            "Custom stroke width…");

        public static readonly Str DragToPickARange = new(
            "editor.dragToPickARange",
            "拖拽选片段 · [ / ] 设入出点",
            "Drag to pick a range · [ and ] set the in and out points");

        public static readonly Str EveryNFramesRowsCols = new(
            "editor.everyNFramesRowsCols",
            "每 {0} 帧取一次 · {1} 行 × {2} 格",
            "One every {0} frames · {1} rows × {2} cells");

        public static readonly Str Exported = new("editor.exported", "已导出 {0}", "Exported {0}");
        public static readonly Str ExportFailed = new("editor.exportFailed", "导出失败", "Export failed");
        public static readonly Str Exporting = new("editor.exporting", "导出 {0}…", "Exporting {0}…");

        public static readonly Str ExportRecording = new("editor.exportRecording", "导出录制", "Export recording");

        public static readonly Str FfmpegNotFoundSoVideo = new(
            "editor.ffmpegNotFoundSoVideo",
            "请在「设置 · 录制」安装视频组件",
            "Install the video component in Settings · Recording.");
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
        public static readonly Str PinToScreen = new("editor.pinToScreen", "钉到屏幕", "Pin to screen");

        public static readonly Str PreviousFrame = new("editor.previousFrame", "上一帧", "Previous frame");
        public static readonly Str RecentColor = new("editor.recentColor", "最近用色 {0}", "Recent color {0}");
        public static readonly Str Redo = new("editor.redo", "重做", "Redo");

        public static readonly Str ReleaseToApplyTheCrop = new(
            "editor.releaseToApplyTheCrop",
            "松开应用裁剪",
            "Release to apply the crop");
        public static readonly Str RgbaPalette = new("editor.rgbaPalette", "RGBA 调色板", "RGBA palette");
        public static readonly Str SamplesTheCurrentRecordingInto = new("editor.samplesTheCurrentRecordingInto", "将 {0} 帧拼为长图，含裁剪与标注，确认后复制", "Combine {0} frames with crops and annotations; confirm to copy.");
        public static readonly Str SaveAfterimageComposite = new(
            "editor.saveAfterimageComposite",
            "保存视觉暂留合成图",
            "Save afterimage composite");
        public static readonly Str SaveCurrentFrame = new("editor.saveCurrentFrame", "保存当前帧", "Save current frame");
        public static readonly Str SmallerText = new("editor.smallerText", "字号减小", "Smaller text");
        public static readonly Str Stroke = new("editor.stroke", "{0}笔画", "{0} stroke");

        public static readonly Str Thin = new("editor.thin", "细", "Thin");
        public static readonly Str Undo = new("editor.undo", "撤销", "Undo");

    }
}
