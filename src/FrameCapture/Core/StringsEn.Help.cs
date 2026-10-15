namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 使用指南窗口。左栏是分区名，右栏一行一个「名字 → 这一行讲什么」：
/// 名字保持短促的词条感，说明句按英文习惯重排语序，不跟着中文逐字断句。
/// 分区标题（快速上手 / 更多玩法）与正文里引用的界面名，都跟对应窗口那张表用同一个词。
/// </summary>
static partial class StringsEn
{
    /// <summary>使用指南窗口的译名。</summary>
    static readonly (string Zh, string En)[] Help =
    {
        // 窗口与翻页
        ("使用指南", "Usage Guide"),
        ("\u2190 上一页", "\u2190 Previous"),
        ("下一页 \u2192", "Next \u2192"),
        ("第 {0} / {1} 步", "Step {0} of {1}"),
        ("完成 \u2713", "Done \u2713"),

        // 快速上手
        ("快速上手", "Quick Start"),
        ("定范围 → 触发 → 复制。", "Set a region → trigger → copy."),
        ("设置捕获范围 · 拖框或点窗口", "Set the capture region · drag a box or click a window"),
        ("截图：截完自动进剪贴板", "Capture: the shot lands in the clipboard"),
        ("录制：按住起录，松手停", "Record: hold to start, release to stop"),
        ("编辑器里：确定复制 / 取消", "In the editor: Done copies, cancel closes"),
        ("随时回到本指南", "This guide is always one key away"),
        ("键位都是默认值，可在「手势配置器」里改成自己的习惯。",
            "Every key is a default — change them in the Gesture Configurator to fit your habits."),

        // 选捕获范围
        ("选捕获范围", "Choose the region"),
        ("指哪儿，截哪儿。", "Capture exactly what you point at."),
        ("拖框", "Drag a box"),
        ("选任意矩形；滚轮微调、方向键移动", "Any rectangle; the wheel resizes and the arrows move it"),
        ("悬停", "Hover"),
        ("自动吸附整窗；Tab 上钻到父容器", "Snaps to the whole window; Tab steps up to the parent"),
        ("确认范围 / 取消", "Confirm region / cancel"),
        ("清除范围", "Clear the region"),
        ("托盘菜单里，恢复「截光标所在屏」", "In the tray menu; goes back to capturing the screen under the cursor"),
        ("设置里可开「常显捕获范围轮廓」，范围一眼在；「吸附到控件」让悬停吸到按钮/文字级。",
            "Settings can keep the region outline always visible; Snap to controls hovers down to button and text level."),

        // 截图
        ("一次触发，进剪贴板。", "One key, straight to the clipboard."),
        ("触发即截", "Trigger captures"),
        ("有范围截范围，没范围截光标所在屏", "With a region it captures that, otherwise the screen under the cursor"),
        ("每条手势单独设：弹浮窗 / 进编辑器 / 什么都不做 / 贴图；写不写剪贴板是另一条独立开关",
            "Set per binding: floating card, open in editor, nothing, or pin it. "
            + "Writing to the clipboard is a separate switch."),
        ("悬浮窗", "Floating card"),
        ("点它进编辑器；常驻/1/2/5秒/自定义可调", "Click it to open the editor; stay, or 1/2/5 seconds, or custom"),
        ("提示音", "Sound"),
        ("关闭 / 轻弹 / 自定义 ogg·wav，可试听", "Off / flick / your own ogg·wav, with a preview"),
        ("「截图后」在「手势配置器」每条手势里设置；托盘还能「编辑上次截图」。",
            "After capture is set per binding in the Gesture Configurator; the tray also offers Edit last capture."),

        // 录制
        ("按住录，松手成片。", "Hold to record, release for the clip."),
        ("按住 / 松开", "Hold / release"),
        ("起录 / 停止 · REC 红徽标表示正在录", "Start / stop · the red REC badge means it is recording"),
        ("帧率", "Frame rate"),
        ("手动 10–240；开「跟随显示器刷新率」自动录满高刷",
            "Manual 10–240; Follow monitor refresh records the display's full rate"),
        ("格式", "Format"),
        ("GIF 通用；MP4 / WebM 需 ffmpeg", "GIF plays everywhere; MP4 / WebM need ffmpeg"),
        ("录完", "After recording"),
        ("自动进编辑器预览，Enter 复制成片", "Opens in the editor to preview; Enter copies the clip"),
        ("录屏边框只提示不入镜；「包含鼠标指针」可关。",
            "The recording border is a hint and never lands in the clip; Include cursor can be turned off."),

        // 编辑器
        ("看、剪、标、复制。", "Watch, trim, annotate, copy."),
        ("播放暂停 / 逐帧 · Shift 走一秒", "Play and pause / step a frame · Shift steps a second"),
        ("Ctrl+滚轮", "Ctrl+wheel"),
        ("缩放；1:1 看像素", "Zoom; 1:1 to inspect pixels"),
        ("掐头 / 去尾，只导出要的一段", "Trim the head or the tail and export only the part you want"),
        ("确定复制成片 / 取消", "Done copies the clip, cancel closes"),
        ("存当前帧 PNG / 导出 MP4·WebM·GIF", "Save the current frame as PNG / export MP4·WebM·GIF"),
        ("Ctrl+H 视觉暂留合成图；「生成胶片条」抽帧拼成一张逐帧图直接复制，喂 AI 最好用。",
            "Ctrl+H blends the frames into one image; Film strip samples them into a single sheet "
            + "you can copy straight into an AI prompt."),

        // 工具页脚与贴图
        ("左侧竖栏，单键切换。", "The column on the left switches sections with a single key."),
        ("把图钉在屏幕上。", "Pin an image onto the screen."),
        ("编辑器里把当前帧钉到屏幕", "Pins the current frame from the editor"),
        ("滚轮 / +/-", "Wheel / +-"),
        ("缩放贴图 / 调透明度", "Zoom the pin / change opacity"),
        ("拖动", "Drag"),
        ("「贴图窗口吸附」：贴齐屏幕边、其他贴图、任意窗口边",
            "Pin snapping lines the pin up with screen edges, other pins, and any window edge"),
        ("Esc / 右键", "Esc / right-click"),
        ("关闭 / 右键菜单存盘、识字、复制", "Close / the menu saves, reads text, copies"),
        ("吸附开关在设置「贴图」页。", "The snapping switch lives on the Pins page in Settings."),

        // 识别文字
        ("图里的字，变文本。", "Turn the text in an image into real text."),
        ("识别当前帧 → 结果窗：选哪段复制哪段", "Recognize the current frame → results window: copy the part you select"),
        ("也能对贴图识字", "Pins can be read the same way"),
        ("贴图右键", "Right-click a pin"),
        ("用 Windows 内置 OCR，离线可用；关「显示结果窗」则直接全文进剪贴板。",
            "Uses the built-in Windows OCR and works offline; turn off Show result window "
            + "and everything goes to the clipboard."),

        // 更多玩法
        ("更多玩法", "More to try"),
        ("藏在设置和托盘里。", "Hidden in Settings and the tray."),
        ("托盘触发后倒数几秒再截——摆好菜单/悬停再入镜，支持自定义秒数",
            "Counts down before shooting so menus and tooltips stay on screen; the seconds are adjustable"),
        ("长网页/长对话自动拼接成一张", "Stitches a long page or conversation into one image"),
        ("键位设置", "Key settings"),
        ("全局键在手势配置器改，编辑器里的键在设置 · 快捷键直接改",
            "Global keys in the Gesture Configurator; editor keys right on Settings · Shortcuts"),
        ("托盘菜单", "Tray menu"),
        ("暂停手势 / 清除范围 / 设置…", "Pause gestures / clear region / Settings…"),
        ("外观", "Appearance"),
        ("主题跟随系统，强调色可自定义", "The theme follows the system, the accent color is yours"),
        ("本页只是地图，细节都在各处的悬停气泡里——指到哪读到哪。",
            "This page is only the map; the details are in tooltips wherever you hover"),
        ("再按一次工具键或 {0} 退出绘制；滚轮调粗细，文字工具调字号；Alt 吸色。",
            "Press the tool key again or {0} to leave drawing; the wheel changes thickness, "
            + "the text tool changes size; Alt picks a color."),
    };
}
