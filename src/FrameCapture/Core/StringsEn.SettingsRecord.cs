namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 设置窗录制分页：帧率、格式、只录窗口、边框与光标。
/// 帧率读数 fps 是单位，两种语言同形，不进本表；ffmpeg 状态行以空格起头，因为它接在格式说明之后。
/// </summary>
static partial class StringsEn
{
    /// <summary>录制分页的译名。</summary>
    static readonly (string Zh, string En)[] Record =
    {
        ("录制帧率", "Recording frame rate"),
        ("录制时每秒捕获的帧数；开启下方「跟随显示器刷新率」时本项隐藏，由刷新率取代",
            "Frames captured per second while recording. With Record at full monitor refresh below on, this row hides and the refresh rate takes over"),
        ("跟随显示器刷新率录满", "Record at full monitor refresh"),
        ("开启后录制帧率自动取当前录制显示器的刷新率，本机主屏检测到 {0}Hz → 录满 {1}fps，上面的手动帧率随之隐藏。",
            "The recording frame rate then follows the refresh rate of the display being recorded; the primary screen here reads {0}Hz, so recording runs at {1}fps and the manual frame rate above hides."),
        ("开启后录制帧率自动取当前录制显示器的刷新率，本机暂未检测到主屏刷新率，录制时按实际显示器实时检测，上面的手动帧率随之隐藏。",
            "The recording frame rate then follows the refresh rate of the display being recorded; no primary refresh rate was detected here, so it is read live from the actual display when recording starts and the manual frame rate above hides."),
        ("144 / 165 / 240Hz 这类高刷屏录满才丝滑；MP4/WebM 可录满，GIF 受格式封顶约 50fps",
            "High-refresh panels at 144 / 165 / 240Hz only look smooth when recorded at the full rate; MP4 and WebM can take it, GIF is capped near 50fps by the format"),

        // 录制格式
        ("录制格式", "Recording format"),
        ("录制完成后编辑器「导出录制」默认用的格式。MP4/WebM 需本机装有 ffmpeg，自动探测 PATH 与常见安装目录；未装则视频项会提示安装，GIF 始终可用",
            "The format the editor's Export recording uses by default. MP4 and WebM need ffmpeg on this machine, looked for on PATH and in the usual install folders; without it the video options offer to install it, and GIF is always available"),
        (" · 已检测到 ffmpeg", " · ffmpeg detected"),
        (" · 未检测到 ffmpeg，装 FFmpeg 后可选", " · ffmpeg not found; install FFmpeg to unlock"),
        ("通用动图，任何地方都能放；受格式封顶约 50fps、体积大",
            "An animated image that plays anywhere; capped near 50fps by the format and large on disk"),
        ("兼容性最好的视频格式，用 libx264 编码，浏览器/剪辑软件/社交平台通吃 ",
            "The most compatible video format, encoded with libx264, at home in browsers, editors, and social platforms "),
        ("开源视频格式，用 VP9 编码，同画质体积更小，网页/上传首选 ",
            "An open video format encoded with VP9: the same quality at a smaller size, the first choice for the web and uploads "),
        ("已检测到 ffmpeg · 可导出 MP4 / WebM", "ffmpeg detected · MP4 and WebM export available"),
        ("未检测到 ffmpeg · 导出 MP4/WebM 需先安装，见帮助 F1，GIF 不受影响",
            "ffmpeg not found · MP4 and WebM export needs it installed first, see the F1 help; GIF is unaffected"),

        // 只录窗口、边框与光标
        ("只录窗口", "Record window only"),
        ("录制选区里点一个窗口起录时，录的是这个窗口本体而不是那块屏幕矩形：别的窗口盖上来、窗口挪位置都不会进画面，成片里只有它。圆角处按白底拍平，GIF 与 MP4 没有透明通道。关掉则一律按屏幕矩形录",
            "When you click a window in the recording selection, it records that window itself rather than the screen rectangle: another window covering it, or moving it around, never enters the frame — the output holds only that window. Rounded corners are flattened onto white, because GIF and MP4 carry no transparency. Off, everything records as a screen rectangle"),
        ("录制时显示范围边框", "Show the region border while recording"),
        ("录制期间按真实捕获范围高亮描边，只提示、不录进产物。取代 Windows 11 那条绕整屏的黄色系统捕获边框——WGC 只能整显示器捕获，系统边框必然全屏、与所选范围无关",
            "During a recording the real capture region is outlined in a highlight, as a hint only — it never lands in the output. It stands in for the yellow system capture border Windows 11 draws around the whole screen: WGC captures a whole monitor, so that border is always full-screen and says nothing about your selection"),
        ("录制包含鼠标指针", "Include the mouse pointer"),
        ("录出的画面里带上鼠标光标，默认开。关掉后成片干净无光标，适合做教程/演示时后期自己加高亮，或不想让光标乱入",
            "The cursor appears in the recording, on by default. Off, the output stays clean, which suits tutorials where you add your own highlight afterwards, or times you would rather the cursor kept out"),
    };
}
