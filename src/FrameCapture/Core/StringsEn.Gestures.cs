namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 手势这一族：触发方式、快捷键页上的词，以及动作词汇表的短名与说明。
/// 动作名（capture.frame 之类）是持久化标识，不进本表；表里翻的是给人看的那两句。
/// 触发方式在键位串里做后缀（F2 Press/Release），在下拉里做选项，两种语言都用同一个词，靠大小写区分场合的是下拉。
/// </summary>
static partial class StringsEn
{
    /// <summary>快捷键、触发方式与动作词汇表的译名。</summary>
    static readonly (string Zh, string En)[] Gestures =
    {
        // 快捷键页外壳
        ("组合键", "Shortcut"),
        ("触发方式", "Trigger"),
        ("点击设置", "Click to set"),
        ("未设键", "No key"),
        ("未绑定", "Unbound"),
        ("，", ","),

        // 完成后与失败时
        ("截图后", "After capture"),
        ("进编辑器", "Open in the editor"),
        ("什么都不做", "Do nothing"),
        ("弹提示", "Show a toast"),
        ("复制到剪贴板", "Copy to Clipboard"),
        ("不复制", "Don't copy"),
        ("跳过该步", "Skip the step"),
        ("顺延下一条", "Try the next binding"),

        // 触发方式：下拉选项，词首大写；同一个词也拼在组合键后面当后缀
        ("按下", "Press"),
        ("松开", "Release"),
        ("单击", "Click"),
        ("双击", "Double-click"),
        ("三击", "Triple-click"),
        ("长按", "Long press"),
        ("轻点", "Tap"),
        ("按住", "Hold"),
        ("松手", "Hold release"),

        // 动作词汇表：短名 + 一句话说明
        ("全屏即时截图", "Instant Full-screen Capture"),
        ("按下立即抓光标所在屏幕的当下这一帧，不走选区、不带延时、不抢焦点，适合游戏",
            "Grabs the exact frame on the screen under the cursor the moment you press: no selection, no delay, no focus steal — usable in games"),
        ("选区", "Selection"),
        ("拖框选一块区域，作为这条序列后面动作的范围", "Drag a box to pick the region the rest of this sequence works on"),
        ("截图", "Capture"),
        ("对当前范围截一张静图；跟在选区后面就是刚圈好的那块",
            "Takes a still of the current region; after a Selection step that means the region you just drew"),
        ("开始录制", "Start Recording"),
        ("对当前范围开始录制，直到收到停止", "Records the current region until a stop arrives"),
        ("停止录制", "Stop Recording"),
        ("停止录制，产物留在内存，不自动进编辑器",
            "Stops recording and keeps the result in memory; it does not open the editor on its own"),
        ("开始/停止录制", "Start/Stop Recording"),
        ("空闲→选区起录；录制中→停止，不自动进编辑器",
            "Idle → pick a region and start recording; recording → stop. It does not open the editor on its own"),
        ("对当前范围做滚动长截图", "Takes a scrolling long capture of the current region"),
        ("倒数数秒后截当前范围，用来抓一点就消失的菜单和悬停",
            "Counts down a few seconds, then captures the current region — for menus and tooltips that vanish the moment you touch them"),
        ("打开标注编辑器编辑上一次静图", "Opens the annotation editor on the last still image"),
        ("打开编辑器", "Open Editor"),
        ("打开视频编辑器编辑上一次录制，常配在完成后，让停止录制后再进编辑器",
            "Opens the video editor on the last recording; usually set under On completion so you land in the editor once recording stops"),
        ("对当前范围做 OCR：开「识别后显示结果窗」则弹可编辑结果窗，否则直接复制文本",
            "Runs OCR on the current region: with Show the result window after recognizing text on, an editable result window appears, otherwise the text is copied directly"),
        ("贴图", "Pin"),
        ("把当前范围钉在屏幕上", "Pins the current region on screen"),
        ("提示", "Notify"),
        ("把上一次截图以浮窗弹出来，点它可以进编辑器",
            "Shows the last still again as a toast; clicking it opens the editor"),
        ("把上一次截图写进剪贴板", "Writes the last still image to the clipboard"),
        ("打开设置", "Open Settings"),
        ("打开设置窗口", "Opens the settings window"),
        ("打开帮助", "Open Help"),
        ("打开使用说明窗口", "Opens the usage guide window"),
    };
}
