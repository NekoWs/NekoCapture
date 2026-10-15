namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 设置窗截图分页：行标签、整段提示，以及取色格式、延时、悬浮窗、提示音四行的选项名。
/// 选项名是短词，英文一律词首大写；提示里的记法 #RRGGBB / rgb(r, g, b) / hsl(h, s%, l%) 原样保留。
/// </summary>
static partial class StringsEn
{
    /// <summary>截图分页与其四行子控件的译名。</summary>
    static readonly (string Zh, string En)[] Capture =
    {
        // 选区与窗口捕获
        ("选区自动截取", "Capture on selection"),
        ("完成选区后立即截取一帧 PNG 静态图，同时复制到剪贴板",
            "As soon as a selection is complete, one PNG frame is captured and copied to the clipboard"),
        ("吸附到控件", "Snap to controls"),
        ("悬停自动吸附窗口内的具体控件：输入框、文字、图片、分组容器等，按 Tab 可上钻到父容器。依赖应用无障碍接口，不支持时自动回退整窗吸附",
            "Hovering snaps to a specific control inside the window — input boxes, text, images, group containers — and Tab steps up to the parent container. It relies on the app's accessibility interface and falls back to whole-window snapping where that is unavailable"),
        ("点击窗口时截取完整窗口", "Click a window to capture the whole window"),
        ("选区窗里点一个吸附到的窗口，不再按屏幕矩形抓像素，而是真正截取该窗口本体——Win11 圆角处透明、浏览器/GPU 窗也干净，背景透明。关掉则点窗恢复旧的「按矩形区域截图」。拖框选区域不受影响；目标窗被别的窗遮挡或取帧失败时自动回退区域截图",
            "Clicking a window the picker snapped to no longer grabs screen pixels by rectangle; it captures the window itself — transparent at Win11's rounded corners, clean on browser and GPU-composited windows, transparent behind. Off, clicking a window goes back to capturing the rectangle. Dragging a box is unaffected, and a covered window or a failed frame falls back to a rectangle capture"),
        ("窗口截图包含投影", "Include the shadow in window captures"),
        ("在上面开关开启时，额外把窗口边缘的真实投影截进来、外缘柔和渐隐到透明。关掉则只截窗口本体，圆角透明、无投影",
            "With the switch above on, the real shadow along the window's edge is captured too, its outer edge fading softly to transparent. Off, only the window body is captured: transparent corners, no shadow"),

        // 选区过程
        ("显示上次选区", "Show the last selection"),
        ("按下选区快捷键后，用虚线框显示上次的选区位置，按 Enter 直接确认重选同区域；关闭后每次都是干净屏",
            "After the selection hotkey a dashed box marks where the last selection was, and Enter confirms that same region again. Off, every session starts on a clean screen"),
        ("选区放大镜", "Selection magnifier"),
        ("拖框或悬停选区时，光标旁显示高倍像素放大镜、坐标/尺寸读数与光标下像素颜色，按 C 复制颜色、格式见下，便于像素级精确选区/取色；关闭可省一点开销",
            "While you drag or hover a selection, a high-magnification pixel view sits beside the cursor with coordinate and size readouts and the color under the cursor. C copies that color in the format set below, for pixel-precise selection and picking. Off saves a little overhead"),
        ("选区时定格画面", "Freeze the screen while selecting"),
        ("按下选区热键的瞬间抓一张全屏定格帧，整个选区过程画面看起来就定格了——放大镜读定格帧、拖框/区域截图也裁这张定格帧，不再选完后重新抓屏，所见即所得。关掉则全程实时：屏幕实时可见、放大镜每次移动实时跟画面、截图实时重抓",
            "The instant the selection hotkey goes down a full-screen frame is taken, and the whole selection looks frozen: the magnifier reads the frozen frame, and dragging a box or capturing a region crops that same frame instead of re-capturing the screen afterwards — what you see is what you get. Off, everything is live: the screen keeps moving, the magnifier follows it on every move, and captures re-grab the screen"),
        ("定格时预抓窗口数", "Windows pre-captured on freeze"),
        ("「定格画面」与「点击窗口时截取完整窗口」都开着时，定格瞬间后台按 Z 序、当前聚焦窗最优先，预抓这么多「完整可见」窗口的干净画面；点窗口即用缓存＝定格+干净圆角+投影，没缓存好则实时抓兜底。0 = 不预抓，点窗口一律实时抓",
            "When both Freeze the screen while selecting and Click a window to capture the whole window are on, the moment of the freeze pre-captures clean images of up to this many fully visible windows in Z order, focused window first. Clicking a window then reads the cache: freeze plus clean rounded corners plus shadow, with a live capture as the fallback. 0 means no pre-capture, so every window click captures live"),
        ("不预抓", "None"),
        ("{0} 个", "{0} windows"),

        // 轮廓与确认
        ("常显捕获范围轮廓", "Always show the capture outline"),
        ("在屏幕上持续显示当前捕获范围的边框，开/关立刻生效。尚未设置范围时没有可显示的边框——先用托盘「设置捕获范围」",
            "Keeps the current capture region's border on screen, and switching takes effect at once. With no region set there is nothing to show — set one first from the tray under Set Capture Region"),
        ("截图后边框显示时间", "Border flash after a capture"),
        ("截完那一下范围边框「亮边闪一下」作确认——这里设它全亮停留多久再淡出。拍完即走调小、想多留一眼确认截对没调大。它与「常显轮廓」和录制边框各自独立",
            "The moment a capture finishes the region border lights up as confirmation; this sets how long it stays fully lit before fading. Turn it down to move on at once, up to double-check you caught the right thing. It is independent of the always-on outline and of the recording border"),
        ("{0:0.0} 秒", "{0:0.0} s"),

        // 取色格式
        ("取色格式", "Color format"),
        ("选区放大镜按 C 复制颜色时的文本形式：#RRGGBB / rgb(r, g, b) / hsl(h, s%, l%)",
            "The text form the picker's C key copies: #RRGGBB / rgb(r, g, b) / hsl(h, s%, l%)"),
        ("#RRGGBB 通用格式", "#RRGGBB, the common form"),
        ("rgb(r, g, b) 可直接贴进 CSS", "rgb(r, g, b) pastes straight into CSS"),
        ("hsl(h, s%, l%) 调色和设计常用", "hsl(h, s%, l%), common for color and design work"),

        // 延时截图
        ("托盘「延时截图」触发后倒数这么久再截当前范围，留时间打开右键菜单/悬停提示/下拉框这类一动就消失的界面",
            "After the tray's Delayed Capture is triggered, counts down this long before capturing the current region, leaving time to open a context menu, a tooltip, or a dropdown — the kind of interface that vanishes the moment you move"),
        ("关", "Off"),
        ("不延时，触发即截", "No delay; captures as soon as it triggers"),
        ("1秒", "1s"),
        ("快速：够移一下鼠标", "Quick: enough to move the mouse"),
        ("3秒", "3s"),
        ("常用：够打开一个菜单", "Usual: enough to open a menu"),
        ("5秒", "5s"),
        ("从容：够展开下拉/悬停", "Comfortable: enough to expand a dropdown or hover"),
        ("10秒", "10s"),
        ("充裕：够摆好几个界面元素", "Plenty: enough to line up several interface elements"),
        ("秒", "s"),
        ("自定义：输入 1–120 任意秒数，回车或移开焦点生效",
            "Custom: any number of seconds from 1 to 120, applied on Enter or when focus leaves"),

        // 截完的悬浮窗
        ("悬浮窗存在时间", "Toast lifetime"),
        ("截完弹出的「已复制」缩略浮窗停留多久自动消失。悬停其上会暂停计时、移开再给满时长。想拍完即走调小、想留着慢慢点进编辑器调大或常驻。",
            "How long the copied thumbnail toast stays before dismissing itself. Hovering pauses the timer, and moving away gives it the full duration again. Turn it down to move on at once, or up — or choose Resident — to keep it around and click into the editor at leisure."),
        ("常驻", "Resident"),
        ("不自动消失，点一下或 Esc 才关", "Never disappears on its own; a click or Esc closes it"),
        ("2秒", "2s"),
        ("利落：来得及看一眼", "Brisk: a glance fits"),
        ("快闪：确认一下就走", "A flash: confirms it and goes"),
        ("从容：够移动鼠标去点", "Comfortable: enough to move the mouse and click"),
        ("自定义：输入 0.5–60 任意秒数，0 为常驻，回车或移开焦点生效",
            "Custom: anything from 0.5 to 60 seconds, 0 keeps it resident, applied on Enter or when focus leaves"),

        // 截图提示音
        ("截图提示音", "Capture sound"),
        ("静态截图完成时的听觉确认。静音=不放声音；轻弹=内置柔和「嘀」；自定义…=选一个 ogg 或 wav 文件，wav 最稳，ogg 需系统解码器，播不了会自动回退轻弹。随时可「试听」",
            "The sound that confirms a still capture finished. No sound is silence; Chime is a soft built-in beep; Custom picks an ogg or wav file — wav is the safest, ogg needs a system decoder and falls back to the chime when it cannot play. Preview is always there"),
        ("静音", "No sound"),
        ("轻弹", "Chime"),
        ("自定义…", "Custom…"),
        ("试听", "Preview"),
        ("点一下试听当前提示音，选「自定义…」后也会立刻放一声让你确认",
            "Click to hear the current sound; choosing Custom also plays one right away so you can confirm it"),
        ("选择提示音", "Choose a notification sound"),
        ("提示音 (*.ogg;*.wav)|*.ogg;*.wav|所有文件 (*.*)|*.*",
            "Sounds (*.ogg;*.wav)|*.ogg;*.wav|All files (*.*)|*.*"),
        ("静音：截图完成不放声音", "No sound: a finished capture stays quiet"),
        ("内置柔和「嘀」声，作听觉确认", "A soft built-in beep as an audible confirmation"),
        ("自定义提示音文件 ogg / wav，点击可重新选择", "A custom ogg / wav file; click to pick another"),
    };
}
