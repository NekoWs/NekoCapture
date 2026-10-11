using System.IO;
using System.Text.Json;

namespace FrameCapture.Core;

/// <summary>应用设置。全部字段带默认值；损坏的设置文件会被重置。</summary>
public sealed class AppSettings
{
    /// <summary>捕获帧率范围（单一事实源：设置滑条 / Normalize / 捕获引擎 / 编辑器全部引用）。</summary>
    public const int FpsMin = 10;
    public const int FpsMax = 60;

    public string ScopeHotkey { get; set; } = "Ctrl+Shift+R";
    public string CaptureHotkey { get; set; } = "Ctrl+Shift+G";

    /// <summary>点按判定阈值(ms)。按下到放开小于该值视为点按(单帧)。0 = 全部按长按处理。</summary>
    public int TapThresholdMs { get; set; } = 250;
    public bool UseWgc { get; set; } = true;

    /// <summary>长按捕获帧率。</summary>
    public int Fps { get; set; } = 30;

    /// <summary>已废弃（R61）：长按录制时长上限移除——录到用户停为止，帧落盘不再受内存/时长限制。
    /// 保留字段仅为兼容旧 settings.json（读写无害），运行期不再有任何消费方。</summary>
    public int MaxDurationMs { get; set; } = 6000;

    /// <summary>是否自动生成给 Agent 看的逐帧胶片条。</summary>
    public bool AgentStripEnabled { get; set; } = true;

    /// <summary>胶片条抽帧数量。</summary>
    public int AgentStripFrames { get; set; } = 12;

    /// <summary>是否在屏幕上常显当前捕获范围轮廓。</summary>
    public bool AlwaysShowRegionOutline { get; set; } = false;

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

    /// <summary>点按捕获快捷键时总是先弹出选区，选完立即截图（QQ 式手感）。</summary>
    public bool TapAlwaysShowsRegion { get; set; } = false;

    /// <summary>按下选区快捷键后是否显示上次选区的虚线框（R55，关=干净屏，Enter 确认上次选区随之失效）。</summary>
    public bool ShowLastRegion { get; set; } = true;

    /// <summary>「按住 = 录制」教学提示是否已展示过（首次真正起录后不再打扰，R43）。</summary>
    public bool RecHintShown { get; set; } = false;

    /// <summary>连按两下捕获键 = 开始录制（免按住），再连按两下结束（R60，ToggleRecord 状态机）。</summary>
    public bool DoubleTapRecord { get; set; } = true;

    /// <summary>捕获完成后除写剪贴板外，再存一份文件副本到本地目录（R63）：截图存 PNG、录制存 GIF。
    /// 默认关（不意外落盘）；开启后写盘失败只记日志，绝不影响剪贴板主流程。</summary>
    public bool AutoSaveCopy { get; set; } = false;

    /// <summary>自动保存目录；空 = 图片\FrameCapture（R63）。</summary>
    public string AutoSavePath { get; set; } = "";

    /// <summary>编辑器窗口总在最前（图钉开关，默认关，R44）。</summary>
    public bool EditorTopmost { get; set; } = false;

    /// <summary>编辑器首次上手引导是否已看过（R45，看过一次不再打扰）。</summary>
    public bool EditorGuideShown { get; set; } = false;

    /// <summary>主题: system | light | dark。</summary>
    public string Theme { get; set; } = "system";

    /// <summary>强调色 hex (如 #4CC2FF)；空 = 跟随系统。</summary>
    public string Accent { get; set; } = "";

    /// <summary>编辑器"自定义颜色"的最近用色（#RRGGBB，新在前，≤6 条，CustomPick.RememberColor 维护）。</summary>
    public List<string> CustomColors { get; set; } = new();

    /// <summary>开机自启。</summary>
    public bool RunAtStartup { get; set; }

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
        TapThresholdMs = Math.Clamp(TapThresholdMs, 0, 1000);
        Fps = Math.Clamp(Fps, FpsMin, FpsMax);
        MaxDurationMs = Math.Clamp(MaxDurationMs, 1000, 30_000);
        AgentStripFrames = Math.Clamp(AgentStripFrames, 4, 24);
        if (Theme is not ("system" or "light" or "dark")) Theme = "system";
        if (RegionW < 16 || RegionH < 16)
        {
            RegionMonitorId = null; RegionX = RegionY = RegionW = RegionH = 0;
        }
    }
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
