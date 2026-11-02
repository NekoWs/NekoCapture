using System.IO;
using System.Text.Json;

namespace NekoCapture.Core;

/// <summary>应用设置。字段带默认值，读取与保存时收敛受限参数；不兼容结构由安装器迁移。</summary>
public sealed class AppSettings
{
    /// <summary>当前配置结构版本；跨结构升级由安装器执行。</summary>
    public const int CurrentConfigVersion = 2;
    /// <summary>此配置文件的结构版本。</summary>
    public int ConfigVersion { get; set; } = CurrentConfigVersion;
    /// <summary>启动时是否检查稳定版更新。</summary>
    public bool AutoCheckUpdates { get; set; } = true;
    /// <summary>用户选择跳过的具体版本；其他新版本仍可提示。</summary>
    public string SkippedUpdateVersion { get; set; } = "";

    /// <summary>捕获帧率范围（单一事实源：设置滑条 / Normalize / 捕获引擎 / 编辑器全部引用）。
    /// 上限 240 覆盖高刷屏（144/165/240Hz）；GIF 输出受格式封顶 ~50fps，MP4/WebM 可录满。</summary>
    public const int FpsMin = 10;
    public const int FpsMax = 240;

    /// <summary>延时截图允许的最长等待时间，单位为秒。</summary>
    public const int DelayCaptureSecondsMax = 120;

    public bool UseWgc { get; set; } = true;

    /// <summary>长按捕获帧率。</summary>
    public int Fps { get; set; } = 60;

    /// <summary>跟随显示器刷新率：开启后捕获帧率自动取当前录制显示器的刷新率（如 160Hz 录满 160fps），
    /// 关闭时用上面的手动 Fps。检测失败则回退手动值。</summary>
    public bool FollowMonitorRefresh { get; set; } = false;

    /// <summary>录制产物格式：GIF 使用内置编码器，MP4/WebM 使用可选的 FFmpeg 组件。</summary>
    public RecordFormat RecordFormat { get; set; } = RecordFormat.Gif;

    /// <summary>录制是否包含系统鼠标指针；WGC 与 GDI 后端均遵循此值。</summary>
    public bool RecordIncludeCursor { get; set; } = true;

    /// <summary>「生成胶片条」小窗的记忆参数：抽帧数（2–48）。胶片条是编辑器里按需生成的功能，
    /// 不属于录制产物；抽样步长由它和总帧数折算，不单独记。</summary>
    public int AgentStripFrames { get; set; } = 12;

    /// <summary>是否在屏幕上常显当前捕获范围轮廓。</summary>
    public bool AlwaysShowRegionOutline { get; set; } = false;

    /// <summary>截图后范围边框的停留时间（秒，默认 1.1）：截完那一下「亮边闪一下作确认」的边框
    /// 全亮停留多久再淡出。想「拍完即走」调小、想「多留一眼确认截对没」调大。仅影响截图反馈边框，
    /// 与「常显捕获范围轮廓」（一直显示）和录制边框各自独立。</summary>
    public double OutlineFadeSeconds { get; set; } = 1.1;

    /// <summary>
    /// 录制期间显示捕获范围边框。取代 Windows 11 那条由 WGC 绘制的全屏黄色系统边框
    /// （它只按"被捕获对象"画，而 WGC 只能整显示器捕获，所以永远全屏、与所选范围无关）。
    /// 默认通过 WDA_EXCLUDEFROMCAPTURE 排除，允许捕获自身界面时入镜。
    /// </summary>
    public bool ShowRecordingBorder { get; set; } = true;

    /// <summary>选区吸附取客户区（排除窗口边框/标题栏，适合游戏画面）。</summary>
    public bool SnapContentArea { get; set; } = false;

    /// <summary>控件吸附的停留时间，单位毫秒；0 表示不等待停留。</summary>
    public int SnapDwellMs { get; set; } = 25;

/// <summary>可调整选区：松手后先给一个能拖边角改大小、可整块移动的框，确认才出图（默认关）。</summary>
public bool EditableSelection { get; set; } = false;

    /// <summary>确认选区的键位：可调整的框出现后按它出图。</summary>
    public string PickerConfirmChord { get; set; } = "Enter";

    /// <summary>取消选区的键位：Esc 始终有效，这里配的是替代或额外的键。</summary>
    public string PickerCancelChord { get; set; } = "Esc";

    /// <summary>选区时向左逐像素移动鼠标的键位，空值表示不绑定。</summary>
    public string PickerNudgeLeftChord { get; set; } = "Left";
    /// <summary>选区时向右逐像素移动鼠标的键位，空值表示不绑定。</summary>
    public string PickerNudgeRightChord { get; set; } = "Right";
    /// <summary>选区时向上逐像素移动鼠标的键位，空值表示不绑定。</summary>
    public string PickerNudgeUpChord { get; set; } = "Up";
    /// <summary>选区时向下逐像素移动鼠标的键位，空值表示不绑定。</summary>
    public string PickerNudgeDownChord { get; set; } = "Down";

    /// <summary>点击窗口时截取完整窗口（默认开）：选区窗里点一个吸附到的窗口，截的是该窗口本体而非屏幕矩形像素
    /// （WGC 按窗口捕获，Win11 圆角透明、浏览器/GPU 窗也干净），可选带真实投影、
    /// 背景透明。关掉则按屏幕矩形抓像素。目标窗口按 WGC 独立捕获，取帧失败时自动回退区域截图。</summary>
    public bool WindowCaptureEnabled { get; set; } = false;

    /// <summary>窗口截图包含投影（默认开）：在上面开关开启时，额外把窗口边缘的真实投影截进来、
    /// 外缘柔和渐隐到透明，看上去悬浮在桌面上。关掉则只截窗口本体（圆角透明、无投影）。</summary>
    public bool WindowCaptureShadow { get; set; } = false;

    /// <summary>边框填充色：MP4 这类没有透明通道的容器，把窗口截图与录制的透明边（圆角、投影）拍平到这个颜色。
    /// 默认白——投影是半透明黑，铺在白底上才读得出阴影，铺在黑底上整圈就消失了。
    /// GIF 与 WebM 自带透明通道，这个设置对它们不起作用。</summary>
    public string VideoMatteColor { get; set; } = "#FFFFFF";

    /// <summary>点击窗口时录制该窗口（默认开）：录制选区里点一个吸附到的窗口，录的是该窗口本体而非屏幕矩形——
    /// 别的窗口盖上来、窗口挪位置都不会进画面，成片只有这一个窗口。
    /// 产物保留 alpha：GIF 走 1 bit 透明，WebM 走 vp9 透明，MP4 按「边框填充色」拍平。
    /// 关掉则按屏幕矩形录；WGC 按窗口取帧失败时也自动回退区域录制。</summary>
    public bool RecordWindowClean { get; set; } = true;

    /// <summary>按下选区快捷键后是否显示上次选区的虚线框（关=干净屏，Enter 确认上次选区随之失效）。</summary>
    public bool ShowLastRegion { get; set; } = true;

    /// <summary>显示上次选区时，允许悬停和点击吸附到该区域。</summary>
    public bool SnapLastRegion { get; set; } = true;

    /// <summary>捕获完成后除写剪贴板外，再存一份文件副本到本地目录：截图存 PNG、录制按 RecordFormat 存 MP4/WebM（未装 ffmpeg 回退 GIF）。
    /// 默认关（不意外落盘）；开启后写盘失败只记日志，绝不影响剪贴板主流程。</summary>
    public bool AutoSaveCopy { get; set; } = false;

    /// <summary>自动保存目录；空 = 图片\NekoCapture。</summary>
    public string AutoSavePath { get; set; } = "";

    /// <summary>编辑器记住上次的工具/颜色/粗细（默认开）：下次打开直接是上次用的标注设置，
    /// 省去每次重选。关闭则每次打开回到默认（浏览 / 红 / 中）。关闭时不读写下面三项。</summary>
    public bool RememberEditorTools { get; set; } = true;

    /// <summary>上次编辑器选中的工具（Tool 枚举名）；空 = 默认浏览。</summary>
    public string LastTool { get; set; } = "";

    /// <summary>上次编辑器用的颜色（#AARRGGBB）；空 = 默认红。</summary>
    public string LastColor { get; set; } = "";

    /// <summary>上次编辑器用的线宽（px）；0 = 默认"中"。</summary>
    public double LastWidth { get; set; } = 0;

    /// <summary>编辑器窗口总在最前（图钉开关，默认关）。</summary>
    public bool EditorTopmost { get; set; } = false;

    /// <summary>编辑器命令的自定义键位：命令 Id → 这条命令的全部按法，用 <c>|</c> 分隔，顺序即分派顺序。
    /// 空串 = 这条命令一个键都不占；表里没有的 Id 取 <see cref="EditorCommands"/> 的默认按法。
    /// 与默认按法完全一致时不写条目，字典里只留真正的偏离。</summary>
    public Dictionary<string, string> EditorKeys { get; set; } = new();

    /// <summary>首次启动欢迎卡是否已弹过：置真后欢迎卡永久静默。</summary>
    public bool FirstRunHintShown { get; set; } = false;

    /// <summary>主题: system | light | dark。</summary>
    public string Theme { get; set; } = "system";

    /// <summary>强调色 HEX；system 跟随系统强调色，空值使用出厂色。</summary>
    public string Accent { get; set; } = "";

    /// <summary>调色板覆盖表：<c>dark.CardBg</c> / <c>light.Text</c> 这类「主题.令牌」→ HEX。
    /// 只存与出厂值不同的那几枚；空表就是出厂配色。写入走设置页「配色」。</summary>
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>最近套用的配色预设标识（<see cref="ColorPresets"/>）；认不出按出厂处理。
    /// 套预设之后单改某枚颜色，这个字段留着——它记的是「从哪套起步」，与逐枚覆盖互不冲突。</summary>
    public string ColorPreset { get; set; } = "";

    /// <summary>配色预设格式版本，由安装器迁移时更新。</summary>
    public int PresetRevision { get; set; } = ColorPresets.Revision;

    /// <summary>界面语言: system | zh | en。中文是源语言，缺译的文案回退中文。</summary>
    public string UiLanguage { get; set; } = "system";

    /// <summary>编辑器"自定义颜色"的最近用色（#RRGGBB，新在前，≤6 条，CustomPick.RememberColor 维护）。</summary>
    public List<string> CustomColors { get; set; } = new();

    /// <summary>开机自启。与 <see cref="RunAsAdmin"/> 同时为真时走最高权限的计划任务：
    /// 登录时不弹 UAC，直接以管理员身份起来。</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>管理员启动偏好，默认关闭；当前权限由进程令牌决定。
    /// 开启时授权并重启为管理员，关闭时重启为普通权限；同时启用自启时使用最高权限登录任务。</summary>
    public bool RunAsAdmin { get; set; }

    /// <summary>启动时显示就绪提示，关闭后静默进入托盘。</summary>
    public bool StartupToast { get; set; } = true;

    /// <summary>就绪提示显示当前截图与录制快捷键。</summary>
    public bool WelcomeShowsHotkeys { get; set; } = true;

    /// <summary>启动提示显示秒数，悬停暂停，移开后重新计时。</summary>
    public double WelcomeToastSeconds { get; set; } = 5;

    /// <summary>选区放大镜（默认开）：拖框/悬停选区时，光标旁显示高倍像素放大镜 + 坐标/尺寸读数，
    /// 便于像素级精确选区。关闭则不抓快照、不显示放大镜。</summary>
    public bool PickerMagnifier { get; set; } = true;

    /// <summary>选区时定格画面（默认开）：按下选区热键的瞬间抓一张全屏定格帧，整个选区过程画面看起来就
    /// 定格了——放大镜读定格帧、拖框/区域截图也裁这张定格帧（选完不重新抓屏），所见即所得。
    /// 关掉则全程实时：屏幕实时可见、放大镜每次移动实时重抓跟画面、截图实时重抓。
    /// 与「点击窗口时截取完整窗口」正交：定格开着且窗口截图也开着时，定格瞬间会后台按 Z 序预抓可见窗口的干净
    /// 画面（WGC），点窗口即用缓存＝定格+干净圆角+投影；没缓存好则实时抓兜底。</summary>
    public bool FreezeOnSelect { get; set; } = true;

    /// <summary>定格时最多预抓多少个「完整可见」窗口的干净画面（默认 6）：按 Z 序取前 N 个
    /// （当前聚焦窗最优先）。0=不预抓（点窗口一律实时抓）。仅在「定格 + 窗口截图」都开启时生效。</summary>
    public int FreezePregrabMaxWindows { get; set; } = 6;

    /// <summary>识别成功后在原图上选字；关闭时直接复制全文。</summary>
    public bool OcrShowResult { get; set; } = true;

    /// <summary>识别引擎，默认使用系统内置 OCR；small 和 medium 使用可下载的 PP-OCRv6 模型。</summary>
    public string OcrEngine { get; set; } = OcrService.SystemEngine;

    /// <summary>贴图磁边（默认开）：拖动贴图松手时，若某条边贴近所在屏幕工作区边缘则自动吸附贴合，
    /// 便于把贴图整齐停靠到屏幕四边/四角。关闭则松手停在原地。</summary>
    public bool PinSnapEdges { get; set; } = true;

    /// <summary>截图提示音（默认关）：静态截图完成时放一声柔和短促的「嘀」作听觉确认。
    /// 默认关——后台工具每次截图都响会吵；设置里带「试听」可先听再决定。开启后 toast/进编辑器/只复制三态都响。</summary>
    public bool CaptureSound { get; set; } = false;

    /// <summary>自定义提示音文件（空=内置「轻弹」嘀声）：设置里提示音下拉选「自定义…」后
    /// 经文件选择窗选定（ogg/wav）。wav 走 winmm 保证可播；其余格式走系统媒体管线，缺解码器时回退嘀声。</summary>
    public string CustomSoundPath { get; set; } = "";

    /// <summary>取色复制格式（默认 hex）：选区放大镜按 C 复制光标下像素颜色时的文本形式。
    /// "hex"=#RRGGBB / "rgb"=rgb(r, g, b) / "hsl"=hsl(h, s%, l%)——后两者便于直接贴进 CSS。
    /// 无法识别的值回退 hex：手写 settings.json 写错也不会炸。</summary>
    public string ColorCopyFormat { get; set; } = "hex";

    /// <summary>延时截图等待秒数，范围为 0 至 <see cref="DelayCaptureSecondsMax"/>；0 表示立即捕获。</summary>
    public int DelayCaptureSeconds { get; set; } = 3;

    /// <summary>快捷键与触发方式连接共享行为入口，默认使用双快捷键方案。
    /// 关闭后不向 GestureCore 手势引擎投递输入。</summary>
    public GestureConfig Gestures { get; set; } = GestureConfig.CreateCurrentDefaults();

    /// <summary>截图后悬浮窗（CaptureToast）存在时间（秒，默认 3.2）：截完弹出的「已复制」缩略浮窗停留多久自动消失。
    /// 悬停其上会暂停计时、移开再给满时长（不受本值影响地重数）。0 = 常驻（不自动消失，点一下或 Esc 才关）。
    /// 想「拍完即走」调小、想「留着慢慢点进编辑器」调大或常驻。</summary>
    public double ToastSeconds { get; set; } = 3.2;

    /// <summary>选区切到「录制」之后先给一个开始按钮（默认开）：确认选区不等于马上开录，
    /// 按钮点下去才起录——选区定完往往还要摆一摆窗口、清一下桌面。关掉则确认选区即起录。</summary>
    public bool RecordNeedsStartClick { get; set; } = true;

    /// <summary>鼠标右键关闭选区（默认开）：Esc 恒可用，右键是第二只手。关掉后右键在选区层上不做事，
    /// 免得在网页里想右键复制时把选区弄丢。</summary>
    public bool PickerRightClickCancel { get; set; } = true;
    /// <summary>截图浮窗上「复制」用的键位串（默认 Ctrl+C）。浮窗活着的那几秒内由全局钩子代收，
    /// 只认修饰键完全一致的那一条；空串表示不设，浮窗上只剩标注与关闭。</summary>
    public string ToastCopyChord { get; set; } = "Ctrl+C";

    /// <summary>是否允许捕获本程序界面、范围轮廓与 REC 徽标，默认关闭。</summary>
    public bool CaptureSelf { get; set; } = false;

    #region 上次捕获范围（物理像素，虚拟桌面绝对坐标；null 表示全屏主屏）
    public string? RegionMonitorId { get; set; }
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionW { get; set; }
    public int RegionH { get; set; }
    #endregion

    /// <summary>把这份设置里的整套配色落到全局调色板：先载入覆盖表，再落主题与强调色。
    /// 覆盖表先载入，主题切换才算在用户那一套颜色上，而不是先算一遍出厂再算一遍自定义。</summary>
    public void ApplyPalette()
    {
        // 本类的 Theme 属性是主题名字符串，把同名类型遮住了，这里必须写全名
        NekoCapture.Core.Theme.LoadCustoms(Colors);
        NekoCapture.Core.Theme.Apply(Theme, Accent);
        if (string.IsNullOrWhiteSpace(Accent)) NekoCapture.Core.Theme.ResetAccent();
    }

    /// <summary>深拷贝：经 JSON 序列化往返得到一个与本机设置完全解耦的副本，改副本不影响 <c>settings.json</c>。</summary>
    /// <returns>字段值等同的副本；Gestures 等引用类型也是新的实例。</returns>
    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<AppSettings>(json)!;
    }

    /// <summary>把非法/越界的值收敛到可运行区间：帧率、胶片条参数、主题、
    /// toast 时长、预抓窗口数、配色预设标识与配色覆盖表，以及小于 16px 的「上次捕获范围」
    /// （连同显示器名一起作废）。
    /// Load / Save / Mutate 都会过它，因此读到的设置恒满足这些区间。</summary>
    public void Normalize()
    {
        Fps = Math.Clamp(Fps, FpsMin, FpsMax);
        AgentStripFrames = Math.Clamp(AgentStripFrames, 2, 48);
        if (Theme is not ("system" or "light" or "dark")) Theme = "system";
        if (UiLanguage is not ("system" or "zh" or "en")) UiLanguage = "system";
        if (OcrEngine is not (OcrService.SystemEngine or OcrService.SmallEngine or OcrService.MediumEngine)) OcrEngine = OcrService.SystemEngine;
        if (ColorPreset != "custom" && ColorPresets.All.All(p => p.Id != ColorPreset)) ColorPreset = "";
        Colors = CleanColors(Colors);
        Gestures ??= GestureConfig.CreateCurrentDefaults();
        Gestures.NormalizePreset();
        ToastSeconds = ToastSeconds <= 0 ? 0 : Math.Clamp(ToastSeconds, 0.5, 60);
        WelcomeToastSeconds = double.IsFinite(WelcomeToastSeconds) ? Math.Clamp(WelcomeToastSeconds, 1, 60) : 5;
        DelayCaptureSeconds = Math.Clamp(DelayCaptureSeconds, 0, DelayCaptureSecondsMax);
        FreezePregrabMaxWindows = Math.Clamp(FreezePregrabMaxWindows, 0, 16);
        SnapDwellMs = Math.Clamp(SnapDwellMs, 0, 5000);
        if (RegionW < 16 || RegionH < 16)
        {
            RegionMonitorId = null; RegionX = RegionY = RegionW = RegionH = 0;
        }
    }

    /// <summary>收敛配色覆盖表：认不出的令牌、认不出的主题前缀、解析不了的 HEX、与出厂色相同的条目一律丢掉。
    /// 手写 settings.json 写错一枚，代价是那枚回到出厂色，不是整套配色读不出来。</summary>
    /// <param name="raw">读到的原始表；null 当空表。</param>
    /// <returns>只含有效覆盖的新表，键序按令牌表原序重排，同一份设置两次保存出来的文件一致。</returns>
    static Dictionary<string, string> CleanColors(Dictionary<string, string>? raw)
    {
        var keep = new Dictionary<string, string>(StringComparer.Ordinal);
        if (raw != null)
        {
            foreach (var (key, hex) in raw)
            {
                var split = SplitColorKey(key);
                if (split == null) continue;
                var (dark, token) = split.Value;
                var got = HexColor.Parse(hex);
                if (got == null || got.Value == NekoCapture.Core.Theme.DefaultColor(token, dark)) continue;
                keep[key] = HexColor.Format(got.Value);
            }
        }
        // 按令牌表顺序重排：字典的枚举顺序跟着插入顺序走，逐枚改色会把文件里的行序搅乱
        var sorted = new Dictionary<string, string>(keep.Count, StringComparer.Ordinal);
        foreach (var t in NekoCapture.Core.Theme.Tokens)
            foreach (var dark in new[] { false, true })
            {
                var k = (dark ? "dark." : "light.") + t.Key;
                if (keep.TryGetValue(k, out var v)) sorted[k] = v;
            }
        return sorted;
    }

    /// <summary>拆配色覆盖表的键。</summary>
    /// <param name="key">形如 <c>dark.CardBg</c> 的键。</param>
    /// <returns>主题侧与令牌名；前缀不是 dark/light、或令牌认不出时 null。</returns>
    static (bool Dark, string Token)? SplitColorKey(string key)
    {
        int dot = key.IndexOf('.');
        if (dot <= 0 || dot == key.Length - 1) return null;
        bool dark = key.Substring(0, dot) switch
        {
            "dark" => true,
            "light" => false,
            _ => false,
        };
        if (key.Substring(0, dot) is not ("dark" or "light")) return null;
        var token = key.Substring(dot + 1);
        return NekoCapture.Core.Theme.Tokens.Any(t => t.Key == token) ? (dark, token) : null;
    }
}

/// <summary>录制产物格式。显式赋值，避免枚举顺序变动破坏已存 settings.json。</summary>
public enum RecordFormat
{
    Gif = 0,
    Mp4 = 1,
    WebM = 2,
}

/// <summary>接收当前配置结构，文件操作与内存设置锁分离。</summary>
public sealed partial class SettingsService
{
    /// <summary>进程内唯一实例（帮助窗等无 DI 场景读取当前快捷键用）。</summary>
    public static SettingsService? Shared { get; private set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>设置目录。默认 <c>%APPDATA%\NekoCapture</c>；<c>NC_SETTINGS_DIR</c> 指到别处，
    /// 让自动化测试与脚本整份搬进临时目录，不碰用户机器上的配置。错误日志与文件监听跟着这里走。</summary>
    public static string Dir
    {
        get
        {
            return ProductPaths.SettingsDirectory;
        }
    }

    public static string FilePath => Path.Combine(Dir, "settings.json");

    private readonly object _gate = new();
    private AppSettings _current;

    public AppSettings Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>新建设置存取：把自身登记为 <see cref="Shared"/>，读盘并 Normalize，文件不存在时立刻落一份默认配置。</summary>
    public SettingsService()
    {
        Shared = this;
        _current = Load();
        // 首启落盘默认配置：让用户可以直接编辑文件，也便于脚本切换主题
        if (!File.Exists(FilePath)) Save(_current);
    }

    private static AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = new AppSettings(); defaults.Normalize(); return defaults;
        }
        string json = File.ReadAllText(FilePath);
        using var document = JsonDocument.Parse(json);
        int version = document.RootElement.TryGetProperty(nameof(AppSettings.ConfigVersion), out var value) ? value.GetInt32() : 0;
        if (version != AppSettings.CurrentConfigVersion)
            throw new InvalidDataException(Loc.T(version > AppSettings.CurrentConfigVersion ? K.Updates.ConfigNewer : K.Updates.ConfigUpgrade));
        var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? throw new InvalidDataException("Settings document is empty");
        loaded.Normalize();
        return loaded;
    }

    /// <summary>落盘：先 Normalize，再写同目录 <c>.tmp</c> 后 <c>File.Move</c> 覆盖——读者永远只会看到完整文件。
    /// 写盘失败静默（内存里的 Current 已更新，下次保存再试），绝不让设置写入打断功能。</summary>
    /// <param name="settings">要保存的设置；就地被 Normalize，保存后它即成为 <see cref="Current"/>。</param>
    public void Save(AppSettings settings)
    {
        (string Json, long Revision) snapshot;
        lock (_gate) { _current = settings; snapshot = PrepareSave(); }
        SaveSnapshot(snapshot);
    }

    static void WriteAtomic(AppSettings settings) => WriteAtomicJson(JsonSerializer.Serialize(settings, JsonOpts));

    static void WriteAtomicJson(string json)
    {
        string tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, json);
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(tmp, FilePath, overwrite: true); return; }
                catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
                { Thread.Sleep(10 << attempt); }
            }
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    DateTime _selfWriteUtc = DateTime.MinValue;
    public bool RecentlySelfWritten => (DateTime.UtcNow - _selfWriteUtc).TotalSeconds < 1.5;

    /// <summary>重载当前结构的磁盘配置；读取失败保留内存并向调用方抛出。</summary>
    public void Reload()
    {
        lock (_writeGate)
        {
            var loaded = Load();
            lock (_gate) { _current = loaded; _saveRevision++; }
        }
        Changed?.Invoke();
    }

    /// <summary>设置变化（Mutate/Reload 后触发；锁外调用，处理器可安全回读 Current）。托盘提示等消费。</summary>
    /// <value>无参无返回的 <c>Action</c>。处理器内不得阻塞——它在 UI 线程锁外同步执行。</value>
    public event Action? Changed;

    /// <summary>严格保存自启与权限偏好；写盘失败保持内存值不变，不触发界面通知。</summary>
    /// <param name="enable">是否启用登录自启。</param>
    /// <param name="elevated">是否要求管理员权限。</param>
    /// <returns>设置已原子写入时为 true。</returns>
    internal bool TrySaveStartup(bool enable, bool elevated)
    {
        lock (_writeGate)
        {
            try
            {
                while (true)
                {
                    AppSettings copy; long revision;
                    lock (_gate)
                    {
                        copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_current, JsonOpts), JsonOpts)!;
                        revision = _saveRevision;
                        _selfWriteUtc = DateTime.UtcNow;
                    }
                    copy.RunAtStartup = enable; copy.RunAsAdmin = elevated;
                    Directory.CreateDirectory(Dir);
                    WriteAtomic(copy);
                    lock (_gate)
                    {
                        if (revision != _saveRevision) continue;
                        _current.RunAtStartup = enable; _current.RunAsAdmin = elevated;
                        _saveRevision++;
                        _selfWriteUtc = DateTime.UtcNow;
                        return true;
                    }
                }
            }
            catch { return false; }
        }
    }

    /// <summary>在 UI 线程接收已完成登记和持久化的自启偏好，并通知界面。</summary>
    /// <param name="enable">已保存的登录自启值。</param>
    /// <param name="elevated">已保存的权限偏好。</param>
    internal void AcceptStartup(bool enable, bool elevated)
    {
        lock (_gate) { _current.RunAtStartup = enable; _current.RunAsAdmin = elevated; }
        Changed?.Invoke();
    }

    /// <summary>同步更新设置与保存快照，文件操作不占用内存设置锁；完成后通知界面。</summary>
    /// <param name="mutate">对当前设置的同步修改，不得执行文件操作或再次调用 Mutate。</param>
    public void Mutate(Action<AppSettings> mutate)
    {
        (string Json, long Revision) snapshot;
        lock (_gate) { mutate(_current); snapshot = PrepareSave(); }
        SaveSnapshot(snapshot);
        Changed?.Invoke();
    }
}
