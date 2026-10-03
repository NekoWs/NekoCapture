using System.IO;
using System.Text.Json;

namespace FrameCapture.Core;

/// <summary>应用设置。全部字段带默认值；损坏的设置文件会被重置。</summary>
public sealed class AppSettings
{
    public string ScopeHotkey { get; set; } = "Ctrl+Shift+R";
    public string CaptureHotkey { get; set; } = "Ctrl+Shift+G";

    /// <summary>点按判定阈值(ms)。按下到放开小于该值视为点按(单帧)。0 = 全部按长按处理。</summary>
    public int TapThresholdMs { get; set; } = 250;
    public bool UseWgc { get; set; } = true;

    /// <summary>长按捕获帧率。</summary>
    public int Fps { get; set; } = 30;

    /// <summary>长按捕获最长时间(ms)。到时间后即使不松手也会结束捕获并打开编辑器。</summary>
    public int MaxDurationMs { get; set; } = 6000;

    /// <summary>是否自动生成给 Agent 看的逐帧胶片条。</summary>
    public bool AgentStripEnabled { get; set; } = true;

    /// <summary>胶片条抽帧数量。</summary>
    public int AgentStripFrames { get; set; } = 12;

    /// <summary>是否在屏幕上常显当前捕获范围轮廓。</summary>
    public bool AlwaysShowRegionOutline { get; set; } = false;

    /// <summary>主题: system | light | dark。</summary>
    public string Theme { get; set; } = "system";

    /// <summary>强调色 hex (如 #4CC2FF)；空 = 跟随系统。</summary>
    public string Accent { get; set; } = "";

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
        Fps = Math.Clamp(Fps, 10, 60);
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
    }

    public void Mutate(Action<AppSettings> mutate)
    {
        lock (_gate)
        {
            mutate(_current);
            _current.Normalize();
            Save(_current);
        }
    }
}
