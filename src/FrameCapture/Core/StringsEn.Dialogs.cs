namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 零散小窗与轻提示：胶片条设置、首启与启动通知、延时倒计时、OCR 结果与回执、
/// 键位录入框、选区窗与滚动录制的现场提示。这些文案短、出现得快，句子要一眼读完。
/// 滚动提示是录制中途唯一的反馈，说清「现在发生了什么」和「你该做什么」。
/// </summary>
static partial class StringsEn
{
    /// <summary>小窗、轻提示与现场反馈的译名。</summary>
    static readonly (string Zh, string En)[] Dialogs =
    {
        // 胶片条设置
        ("生成", "Create"),
        ("抽帧数", "Frame count"),
        ("从当前录制抽样拼成一张逐帧图，含裁剪与标注，共 {0} 帧，确定后复制到剪贴板",
            "Samples the current recording into one image, crop and annotations included, {0} frames in all. "
            + "Choosing Done copies it to the clipboard."),
        ("帧 · 2–48，步长取完还多则均匀稀释到此数", "frames · 2–48; if the step yields more, they thin out evenly to this number"),
        ("每 N 帧取一次", "Take every Nth"),
        ("帧 · 1 逐帧取，2 隔帧取", "frames · 1 takes every frame, 2 every other"),

        // 启动与首启通知
        ("FrameCapture 已启动", "FrameCapture is running"),
        ("已在托盘待命 · 截图 {0} · 录制 {1}", "Waiting in the tray · capture {0} · record {1}"),
        ("FrameCapture 已就绪", "FrameCapture is ready"),
        ("选完区域即截图 · 录制键再按一下停止 · 双击贴图关闭 · 右键贴图 / 托盘查看更多",
            "Pick a region to capture · press the record key again to stop · double-click a pin to close it "
            + "· right-click a pin or the tray for more"),
        ("查看帮助与全部快捷键", "See the help and every shortcut"),

        // 文字识别
        ("{0} 字", "{0} characters"),
        ("复制全部", "Copy all"),
        ("已识别 {0} 字 · 已复制", "Recognized {0} characters · copied"),
        ("未识别到文字", "No text recognized"),
        ("未安装 OCR 语言包，请到 设置 › 时间和语言 › 语音 添加",
            "No OCR language pack is installed — add one in Settings › Time & Language › Speech"),

        // 延时截图与键位录入
        ("趁这几秒打开要截的菜单 / 悬停提示…", "Use these seconds to open the menu or tooltip you want to capture…"),
        ("按新键 · Esc 解绑", "Press a new key · Esc clears it"),

        // 选区与贴图
        ("拖框选择 · 悬停点击吸附窗口 · Enter 确认上次选区 · Esc 取消",
            "Drag to select · hover and click to snap to a window · Enter reuses the last region · Esc cancels"),
        ("拖框选择 · 悬停点击吸附窗口 · Esc 取消",
            "Drag to select · hover and click to snap to a window · Esc cancels"),
        ("预览", "Preview"),
        ("处理中…", "Working…"),
        ("已复制 ", "Copied "),
        ("范围 {0}×{1}", "Region {0}×{1}"),
        ("全屏", "Full screen"),

        // 滚动录制的现场提示
        ("此环境无法注入滚轮，请手动滚动", "This environment can't inject the wheel, scroll by hand"),
        ("这个窗口不接受滚轮注入，请手动滚动", "This window doesn't accept wheel injection, scroll by hand"),
        ("自动滚动中：光标已移入选区，停止或结束后移回原位",
            "Auto-scrolling: the cursor moved into the region and returns when you stop or finish"),
        ("已到内容底部，点「完成」出图", "Bottom of the content reached — choose Done to stitch"),
        ("画面一直在变化、判定不了停稳，自动滚动已停止",
            "The view keeps changing, so settling can't be detected; auto-scroll stopped"),
        ("滚动太快、找不到重叠——请少滚一点", "Scrolled too fast to find an overlap — scroll a little at a time"),
    };
}
