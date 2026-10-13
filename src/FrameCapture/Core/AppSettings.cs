using System.IO;
using System.Text.Json;

namespace FrameCapture.Core;

/// <summary>应用设置。全部字段带默认值；损坏的设置文件会被重置。</summary>
public sealed class AppSettings
{
    /// <summary>捕获帧率范围（单一事实源：设置滑条 / Normalize / 捕获引擎 / 编辑器全部引用）。
    /// 上限 240 覆盖高刷屏（144/165/240Hz）；GIF 输出受格式封顶 ~50fps，MP4/WebM 可录满。</summary>
    public const int FpsMin = 10;
    public const int FpsMax = 240;

    // R126 B2：旧 ScopeHotkey/CaptureHotkey/TapThresholdMs 退役——捕获/选区改由手势引擎的 tap/hold 触发，
    // 点按阈值即每条绑定的 LongPressMs（默认 250ms）。旧 settings.json 里残留的这些键反序列化时被忽略，无害。
    public bool UseWgc { get; set; } = true;

    /// <summary>长按捕获帧率。</summary>
    public int Fps { get; set; } = 30;

    /// <summary>跟随显示器刷新率（R100）：开启后捕获帧率自动取当前录制显示器的刷新率（如 160Hz 录满 160fps），
    /// 关闭时用上面的手动 Fps。检测失败则回退手动值。</summary>
    public bool FollowMonitorRefresh { get; set; } = false;

    /// <summary>录制产物格式（R100）：Gif 通用轻量（封顶 ~50fps）；Mp4/WebM 真视频，可录满高刷（需系统装有 ffmpeg）。</summary>
    public RecordFormat RecordFormat { get; set; } = RecordFormat.Gif;

    /// <summary>录制是否包含鼠标指针（R108）：默认开（保持既有手感）。关掉后录出的画面干净无光标，
    /// 适合做教程/演示时后期自己加高亮，或不想让光标乱入成片。</summary>
    public bool RecordIncludeCursor { get; set; } = true;

    // R61 已移除长按录制时长上限（录到用户停为止，帧落盘，护栏是磁盘）。
    // 原 MaxDurationMs 死设置（R120 清理）：运行期从无消费方，留着只会让人误以为有上限，故删除；
    // 旧 settings.json 里残留的该键反序列化时被忽略，无害。

    /// <summary>R161：胶片条改为编辑器里的功能按钮（不再自动生成，AgentStripEnabled 退役），
    /// 这两个值现在是「生成胶片条」小窗的记忆参数：抽帧数 + 每 N 帧取一次（1=逐帧）。
    /// 旧 settings.json 里残留的 AgentStripEnabled 键反序列化时被忽略，无害。</summary>
    public int AgentStripFrames { get; set; } = 12;

    /// <summary>胶片条抽样步长：每 N 帧取一次（≥1）。与抽帧数上限配合：先按步长取，再多则均匀稀释到上限。</summary>
    public int AgentStripEveryN { get; set; } = 1;

    /// <summary>是否在屏幕上常显当前捕获范围轮廓。</summary>
    public bool AlwaysShowRegionOutline { get; set; } = false;

    /// <summary>截图后范围边框的停留时间（秒，默认 1.1，R164）：截完那一下「亮边闪一下作确认」的边框
    /// 全亮停留多久再淡出。想「拍完即走」调小、想「多留一眼确认截对没」调大。仅影响截图反馈边框，
    /// 与「常显捕获范围轮廓」（一直显示）和录制边框各自独立。</summary>
    public double OutlineFadeSeconds { get; set; } = 1.1;

    /// <summary>
    /// 录制期间显示捕获范围边框。取代 Windows 11 那条由 WGC 绘制的全屏黄色系统边框
    /// （它只按"被捕获对象"画，而 WGC 只能整显示器捕获，所以永远全屏、与所选范围无关）。
    /// 本窗用 WDA_EXCLUDEFROMCAPTURE 排除，保证只提示、不入镜。
    /// </summary>
    public bool ShowRecordingBorder { get; set; } = true;

    /// <summary>完成选区后立即截取一帧（复用托盘同款按下→自动松手链路）。</summary>
    public bool AutoCaptureOnSelect { get; set; } = true;

    /// <summary>选区吸附取客户区（排除窗口边框/标题栏，适合游戏画面）。</summary>
    public bool SnapContentArea { get; set; } = false;

    /// <summary>点击窗口时截取完整窗口（默认开，R165）：选区窗里点一个吸附到的窗口，不再按屏幕矩形抓像素，
    /// 而是真正截取该窗口本体（WGC 按窗口捕获，Win11 圆角透明、浏览器/GPU 窗也干净），可选带真实投影、
    /// 背景透明（macOS 式）。关掉则点窗恢复旧的「按矩形区域截图」。目标窗被遮挡或 WGC 取帧失败时自动回退区域截图。</summary>
    public bool WindowCaptureEnabled { get; set; } = true;

    /// <summary>窗口截图包含投影（默认开，R165）：在上面开关开启时，额外把窗口边缘的真实投影截进来、
    /// 外缘柔和渐隐到透明（macOS 悬浮感）。关掉则只截窗口本体（圆角透明、无投影）。</summary>
    public bool WindowCaptureShadow { get; set; } = true;

    /// <summary>截图完成后的行为（R83）：toast=弹缩略浮窗可一键进编辑器（默认，兼顾确认与标注）；
    /// editor=复制后直接打开标注编辑器（QQ 式）；copy=只复制不弹窗不开编辑器（最快，轮廓闪一下作反馈）。
    /// 三态覆盖「要确认」「要标注」「只要剪贴板」三类人群，非法值回落 toast。</summary>
    public string AfterCapture { get; set; } = "toast";

    /// <summary>按下选区快捷键后是否显示上次选区的虚线框（R55，关=干净屏，Enter 确认上次选区随之失效）。</summary>
    public bool ShowLastRegion { get; set; } = true;

    /// <summary>「按住 = 录制」教学提示是否已展示过（首次真正起录后不再打扰，R43）。</summary>
    public bool RecHintShown { get; set; } = false;

    /// <summary>捕获完成后除写剪贴板外，再存一份文件副本到本地目录（R63）：截图存 PNG、录制按 RecordFormat 存 MP4/WebM（未装 ffmpeg 回退 GIF）。
    /// 默认关（不意外落盘）；开启后写盘失败只记日志，绝不影响剪贴板主流程。</summary>
    public bool AutoSaveCopy { get; set; } = false;

    /// <summary>自动保存目录；空 = 图片\FrameCapture（R63）。</summary>
    public string AutoSavePath { get; set; } = "";

    /// <summary>编辑器记住上次的工具/颜色/粗细（默认开，R64）：下次打开直接是上次用的标注设置，
    /// 省去每次重选。关闭则每次打开回到默认（浏览 / 红 / 中）。关闭时不读写下面三项。</summary>
    public bool RememberEditorTools { get; set; } = true;

    /// <summary>上次编辑器选中的工具（Tool 枚举名）；空 = 默认浏览（R64）。</summary>
    public string LastTool { get; set; } = "";

    /// <summary>上次编辑器用的颜色（#AARRGGBB）；空 = 默认红（R64）。</summary>
    public string LastColor { get; set; } = "";

    /// <summary>上次编辑器用的线宽（px）；0 = 默认"中"（R64）。</summary>
    public double LastWidth { get; set; } = 0;

    /// <summary>编辑器窗口总在最前（图钉开关，默认关，R44）。</summary>
    public bool EditorTopmost { get; set; } = false;

    /// <summary>编辑器首次上手引导是否已看过（R45，看过一次不再打扰）。</summary>
    public bool EditorGuideShown { get; set; } = false;

    /// <summary>首次启动欢迎卡是否已弹过（R82，弹过一次永不再打扰）。</summary>
    public bool FirstRunHintShown { get; set; } = false;

    /// <summary>主题: system | light | dark。</summary>
    public string Theme { get; set; } = "system";

    /// <summary>强调色 hex (如 #4CC2FF)；空 = 跟随系统。</summary>
    public string Accent { get; set; } = "";

    /// <summary>编辑器"自定义颜色"的最近用色（#RRGGBB，新在前，≤6 条，CustomPick.RememberColor 维护）。</summary>
    public List<string> CustomColors { get; set; } = new();

    /// <summary>开机自启。</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>选区放大镜（默认开，R70）：拖框/悬停选区时，光标旁显示高倍像素放大镜 + 坐标/尺寸读数，
    /// 便于像素级精确选区（QQ/Snipaste 招牌手感）。关闭则不抓快照、不显示放大镜。</summary>
    public bool PickerMagnifier { get; set; } = true;

    /// <summary>选区时定格画面（默认开，R166）：按下选区热键的瞬间抓一张全屏定格帧，整个选区过程画面看起来就
    /// 定格了——放大镜读定格帧、拖框/区域截图也裁这张定格帧（不再选完后重新抓屏），所见即所得（QQ/Snipaste 式）。
    /// 关掉则全程实时：屏幕实时可见、放大镜每次移动实时重抓跟画面、截图实时重抓。
    /// 与「点击窗口时截取完整窗口」正交：定格开着且窗口截图也开着时，定格瞬间会后台按 Z 序预抓可见窗口的干净
    /// 画面（WGC），点窗口即用缓存＝定格+干净圆角+投影；没缓存好则实时抓兜底。</summary>
    public bool FreezeOnSelect { get; set; } = true;

    /// <summary>定格时最多预抓多少个「完整可见」窗口的干净画面（默认 6，R166）：按 Z 序取前 N 个
    /// （当前聚焦窗最优先）。0=不预抓（点窗口一律实时抓）。仅在「定格 + 窗口截图」都开启时生效。</summary>
    public int FreezePregrabMaxWindows { get; set; } = 6;

    /// <summary>识别后显示结果窗（默认开，R120 D2 改为预览优先）：编辑器/贴图「识别文字」成功后弹出可编辑、
    /// 可部分选中复制的结果窗（含字数、复制全部钮）——预览即选取入口，此时<b>不再自动写剪贴板</b>（要哪段选哪段，
    /// 或点「复制全部」）。关闭则退回快速路径：直接写剪贴板 + 轻提示，不弹窗。</summary>
    public bool OcrShowResult { get; set; } = true;

    /// <summary>贴图磁边（默认开，R74）：拖动贴图松手时，若某条边贴近所在屏幕工作区边缘则自动吸附贴合，
    /// 便于把贴图整齐停靠到屏幕四边/四角（Snipaste 式手感）。关闭则松手停在原地。</summary>
    public bool PinSnapEdges { get; set; } = true;

    /// <summary>截图提示音（默认关，R86）：静态截图完成时放一声柔和短促的「嘀」作听觉确认（QQ 招牌反馈）。
    /// 默认关——后台工具每次截图都响会吵；设置里带「试听」可先听再决定。开启后 toast/进编辑器/只复制三态都响。</summary>
    public bool CaptureSound { get; set; } = false;

    /// <summary>自定义提示音文件（R161，空=内置「轻弹」嘀声）：设置里提示音下拉选「自定义…」后
    /// 经文件选择窗选定（ogg/wav）。wav 走 winmm 保证可播；其余格式走系统媒体管线，缺解码器时回退嘀声。</summary>
    public string CustomSoundPath { get; set; } = "";

    /// <summary>取色复制格式（默认 hex，R89）：选区放大镜按 C 复制光标下像素颜色时的文本形式。
    /// "hex"=#RRGGBB（QQ 同款）/ "rgb"=rgb(r, g, b) / "hsl"=hsl(h, s%, l%)——后两者便于直接贴进 CSS。
    /// 无法识别的值回退 hex（向后兼容旧配置）。</summary>
    public string ColorCopyFormat { get; set; } = "hex";

    /// <summary>延时截图秒数（默认 3，R91）：托盘「延时截图」触发后倒数这么久再截当前范围，
    /// 留时间打开右键菜单/悬停提示/下拉框这类一动就消失的界面。0=立即（无延时）。
    /// 设置里给 关/1/3/5/10 秒预设 chip，是功能可选项而非任意数值。</summary>
    public int DelayCaptureSeconds { get; set; } = 3;

    /// <summary>自定义手势配置（R120 B2）：把功能触发方式拆成「组合键 + 触发方式 → 动作序列」，可分离/组合键绑定。
    /// 默认关闭——附加式，旧热键路径原样工作、零回归；开启后手势层与旧热键并存。引擎在独立的 GestureCore 库里。</summary>
    public GestureConfig Gestures { get; set; } = new();

    /// <summary>截图后悬浮窗（CaptureToast）存在时间（秒，默认 3.2，R120 C）：截完弹出的「已复制」缩略浮窗停留多久自动消失。
    /// 悬停其上会暂停计时、移开再给满时长（不受本值影响地重数）。0 = 常驻（不自动消失，点一下或 Esc 才关）。
    /// 想「拍完即走」调小、想「留着慢慢点进编辑器」调大或常驻。</summary>
    public double ToastSeconds { get; set; } = 3.2;

    // ---- 上次捕获范围（物理像素，虚拟桌面绝对坐标；null 表示全屏主屏） ----
    public string? RegionMonitorId { get; set; }
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionW { get; set; }
    public int RegionH { get; set; }

    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<AppSettings>(json)!;
    }

    public void Normalize()
    {
        Fps = Math.Clamp(Fps, FpsMin, FpsMax);
        AgentStripFrames = Math.Clamp(AgentStripFrames, 2, 48);
        AgentStripEveryN = Math.Clamp(AgentStripEveryN, 1, 60);
        if (Theme is not ("system" or "light" or "dark")) Theme = "system";
        if (AfterCapture is not ("toast" or "editor" or "copy" or "pin")) AfterCapture = "toast";
        Gestures ??= new GestureConfig();
        Gestures.SeedIfNeeded(GestureConfig.DefaultCaptureKey, GestureConfig.DefaultScopeKey);   // R126 一次性：空配置播种「轻点截图+按住录制」默认集
        ToastSeconds = ToastSeconds <= 0 ? 0 : Math.Clamp(ToastSeconds, 0.5, 60);
        FreezePregrabMaxWindows = Math.Clamp(FreezePregrabMaxWindows, 0, 16);
        if (RegionW < 16 || RegionH < 16)
        {
            RegionMonitorId = null; RegionX = RegionY = RegionW = RegionH = 0;
        }
    }
}

/// <summary>录制产物格式（R100）。显式赋值，避免枚举顺序变动破坏已存 settings.json。</summary>
public enum RecordFormat
{
    Gif = 0,
    Mp4 = 1,
    WebM = 2,
}

/// <summary>JSON 设置存取，带并发保护与损坏自愈。</summary>
public sealed class SettingsService
{
    /// <summary>进程内唯一实例（帮助窗等无 DI 场景读取当前快捷键用）。</summary>
    public static SettingsService? Shared { get; private set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrameCapture");

    public static string FilePath => Path.Combine(Dir, "settings.json");

    private readonly object _gate = new();
    private AppSettings _current;

    public AppSettings Current
    {
        get { lock (_gate) return _current; }
    }

    public SettingsService()
    {
        Shared = this;
        _current = Load();
        // 首启落盘默认配置：让用户可以直接编辑文件，也便于脚本切换主题
        if (!File.Exists(FilePath)) Save(_current);
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null) { s.Normalize(); return s; }
            }
        }
        catch
        {
            // 损坏的设置直接重置
            try { Directory.CreateDirectory(Dir); File.Delete(FilePath); } catch { }
        }
        var def = new AppSettings();
        def.Normalize();
        return def;
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        lock (_gate)
        {
            _current = settings;
            _selfWriteUtc = DateTime.UtcNow;
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch { /* 保存失败不影响运行 */ }
        }
    }

    DateTime _selfWriteUtc = DateTime.MinValue;
    public bool RecentlySelfWritten => (DateTime.UtcNow - _selfWriteUtc).TotalSeconds < 1.5;

    /// <summary>从磁盘重载（外部编辑 settings.json 后热重载）；损坏自愈。</summary>
    public void Reload()
    {
        lock (_gate) _current = Load();
        Changed?.Invoke();
    }

    /// <summary>设置变化（Mutate/Reload 后触发；锁外调用，处理器可安全回读 Current）。托盘提示等消费。</summary>
    public event Action? Changed;

    public void Mutate(Action<AppSettings> mutate)
    {
        lock (_gate)
        {
            mutate(_current);
            _current.Normalize();
            Save(_current);
        }
        Changed?.Invoke();
    }
}
