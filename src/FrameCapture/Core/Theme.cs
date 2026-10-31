using System;
using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>颜色令牌在设置页「配色」里的分节归属。</summary>
public enum ColorGroup
{
    /// <summary>表面：窗口、面板、标题栏、卡片、行、侧栏。</summary>
    Surface,
    /// <summary>文字：正文与次要。</summary>
    Text,
    /// <summary>描边：标准与更淡一档。</summary>
    Line,
    /// <summary>交互：悬停、按下、行悬停、轨道。</summary>
    Interaction,
    /// <summary>语义：提醒、成功、注意。</summary>
    Semantic,
}

/// <summary>调色板里的一枚颜色令牌：一个稳定键名，加出厂的浅色值与深色值。
/// 键名同时是设置文件里覆盖项的名字（<c>dark.CardBg</c>），因此定下来不动。</summary>
public sealed class ColorToken
{
    /// <param name="key">稳定键名，与 <see cref="Theme"/> 的属性名一致。</param>
    /// <param name="group">设置页分节归属。</param>
    /// <param name="light">出厂浅色值。</param>
    /// <param name="dark">出厂深色值。</param>
    public ColorToken(string key, ColorGroup group, Color light, Color dark)
    {
        Key = key; Group = group; Light = light; Dark = dark;
    }

    /// <summary>稳定键名，覆盖表与设置文件都用它。</summary>
    public string Key { get; }

    /// <summary>设置页分节归属。</summary>
    public ColorGroup Group { get; }

    /// <summary>出厂浅色值。</summary>
    public Color Light { get; }

    /// <summary>出厂深色值。</summary>
    public Color Dark { get; }

    /// <summary>按主题取厂值。</summary>
    /// <param name="dark">true 取深色值。</param>
    /// <returns>该主题的出厂色。</returns>
    public Color Default(bool dark) => dark ? Dark : Light;
}

/// <summary>
/// 明暗主题调色板。所有覆盖层从这里取色；主题变化时广播 <see cref="Changed"/>，订阅的窗口即时换色，新建窗口直接取新色。
/// 每个颜色是一枚 <see cref="ColorToken"/>：出厂的浅色值与深色值成对存在，用户可逐枚、逐主题覆盖（设置页「配色」）。
/// 基调：Fluent 深色冷灰 #1F2024 / 浅色冷灰 #F6F7FA，强调色可自定义。
/// 背景类颜色自带 alpha：Chrome 0xD9 最透、Window 0xF2 近不透、Card 全不透明——覆盖层要透出桌面。
/// </summary>
public static class Theme
{
    /// <summary>出厂强调色：设置里没写强调色、或用户按下「恢复出厂配色」时的落点。</summary>
    public static readonly Color DefaultAccent = Color.FromRgb(0x4C, 0xC2, 0xFF);

    /// <summary>当前强调色。</summary>
    /// <value>由 <see cref="Apply"/> 从设置写入；解析失败时保持原值。</value>
    public static Color Accent = DefaultAccent;

    /// <summary>当前生效的是否深色主题。</summary>
    /// <value>调色板所有属性分支的依据，由 <see cref="Apply"/> 从设置算出；未 Apply 前为深色。</value>
    public static bool IsDark => EffectiveDark;
    static bool EffectiveDark = true;

    /// <summary>当前主题在覆盖表键名里的写法：<c>dark</c> 或 <c>light</c>。</summary>
    public static string ModeName => EffectiveDark ? "dark" : "light";

    /// <summary>主题或任一颜色实际变化后触发，窗口订阅后可即时换色。</summary>
    public static event Action Changed = () => { };

    /// <summary>调色板即将重算，参数是重算之后的深浅档。想在换色前拍下当前画面的订阅它：
    /// <see cref="Changed"/> 广播时画面已经是新调色板，那时候再拍就拍不到退场的那一屏了。</summary>
    public static event Action<bool> Changing = _ => { };

    /// <summary>令牌在 <see cref="Registry"/> 里的下标。属性体按它取已算好的当前色。</summary>
    enum Slot
    {
        WindowBg, PanelBg, ChromeBg, CardBg, RowBg, NavBg, KeycapBg, ChipBg,
        Text, TextDim,
        Stroke, StrokeSoft, StrokeField,
        Hover, Press, RowHover, TrackBg,
        Danger, Success, Warning,
    }

    static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
    static Color O(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);

    static ColorToken Tok(Slot s, ColorGroup g, Color light, Color dark) => new(s.ToString(), g, light, dark);

    /// <summary>全部令牌，顺序即设置页的展示顺序：表面 → 文字 → 描边 → 交互 → 语义。</summary>
    static readonly ColorToken[] Registry =
    {
        // 表面：暗色往冷灰走一档并拉开层次，亮色留一点冷调而不是纯白堆叠
        Tok(Slot.WindowBg, ColorGroup.Surface, C(0xF2, 0xF6, 0xF7, 0xFA), C(0xF2, 0x1F, 0x20, 0x24)),
        Tok(Slot.PanelBg, ColorGroup.Surface, C(0xE6, 0xFF, 0xFF, 0xFF), C(0xE6, 0x2A, 0x2C, 0x31)),
        Tok(Slot.ChromeBg, ColorGroup.Surface, C(0xD9, 0xED, 0xEE, 0xF2), C(0xD9, 0x2B, 0x2D, 0x33)),
        Tok(Slot.CardBg, ColorGroup.Surface, O(0xFF, 0xFF, 0xFF), O(0x26, 0x28, 0x2D)),
        Tok(Slot.RowBg, ColorGroup.Surface, O(0xF2, 0xF3, 0xF7), O(0x2E, 0x31, 0x37)),
        Tok(Slot.NavBg, ColorGroup.Surface, O(0xEC, 0xEE, 0xF3), O(0x23, 0x25, 0x29)),
        // 键帽与芯片各自一枚底色：它们坐在卡片上，却与卡片差着一层台阶；借用行底色会把「可点」和「一行」混成一种面
        Tok(Slot.KeycapBg, ColorGroup.Surface, O(0xFB, 0xFC, 0xFD), O(0x2C, 0x2F, 0x35)),
        Tok(Slot.ChipBg, ColorGroup.Surface, O(0xF1, 0xF2, 0xF6), O(0x30, 0x33, 0x39)),
        // 文字：次要文字取不透明灰，压在四种底上都过 4.5:1；带 Alpha 的灰压在浅底上只有 3.2:1
        Tok(Slot.Text, ColorGroup.Text, O(0x1A, 0x1A, 0x1A), O(0xFF, 0xFF, 0xFF)),
        Tok(Slot.TextDim, ColorGroup.Text, O(0x5D, 0x63, 0x6E), O(0xC7, 0xCA, 0xD1)),
        // 描边：整列条目各自描满标准描边会把列表切成格子，标准描边本身也收到不抢眼的一档
        Tok(Slot.Stroke, ColorGroup.Line, C(0x2E, 0, 0, 0), C(0x2E, 0xFF, 0xFF, 0xFF)),
        Tok(Slot.StrokeSoft, ColorGroup.Line, C(0x1F, 0, 0, 0), C(0x24, 0xFF, 0xFF, 0xFF)),
        // 控件描边：键帽、芯片、输入框、开关轨道这一圈。与卡片外框分开，调细调淡不必连卡片一起动
        Tok(Slot.StrokeField, ColorGroup.Line, C(0x1F, 0, 0, 0), C(0x24, 0xFF, 0xFF, 0xFF)),
        // 交互：按下恒比悬停再走一档，两套主题同方向；行悬停是不透明色，落在行底色上不叠第二层
        Tok(Slot.Hover, ColorGroup.Interaction, C(0x14, 0, 0, 0), C(0x1E, 0xFF, 0xFF, 0xFF)),
        Tok(Slot.Press, ColorGroup.Interaction, C(0x24, 0, 0, 0), C(0x30, 0xFF, 0xFF, 0xFF)),
        Tok(Slot.RowHover, ColorGroup.Interaction, O(0xE9, 0xEB, 0xF0), O(0x36, 0x3A, 0x41)),
        Tok(Slot.TrackBg, ColorGroup.Interaction, C(0x1F, 0, 0, 0), C(0x2D, 0xFF, 0xFF, 0xFF)),
        // 语义：浅色主题把提醒色压深到能当正文读，暗色保持那抹亮红
        Tok(Slot.Danger, ColorGroup.Semantic, O(0xC4, 0x2B, 0x1C), O(0xFF, 0x6B, 0x6B)),
        Tok(Slot.Success, ColorGroup.Semantic, O(0x0E, 0x7A, 0x45), O(0x57, 0xC9, 0x8E)),
        Tok(Slot.Warning, ColorGroup.Semantic, O(0x8A, 0x5A, 0x00), O(0xE3, 0xB3, 0x41)),
    };

    /// <summary>全部颜色令牌，按设置页的展示顺序。</summary>
    public static IReadOnlyList<ColorToken> Tokens => Registry;

    static readonly Dictionary<string, int> Ordinals = BuildOrdinals();

    static Dictionary<string, int> BuildOrdinals()
    {
        var d = new Dictionary<string, int>(Registry.Length, StringComparer.Ordinal);
        for (int i = 0; i < Registry.Length; i++) d[Registry[i].Key] = i;
        return d;
    }

    /// <summary>覆盖表：键是 <c>dark.CardBg</c> / <c>light.CardBg</c>，值是用户自定义色。</summary>
    static readonly Dictionary<string, Color> Customs = new(StringComparer.Ordinal);

    /// <summary>当前主题下每个令牌算好的实际颜色，属性体直接按下标取。</summary>
    static readonly Color[] Resolved = new Color[Registry.Length];

    static bool _accentFollowsSystem;
    static string _themeSetting = "system", _accentSetting = "";
    static int _systemRefreshQueued;

    static Theme()
    {
        Recompute();
        System.Windows.SystemParameters.StaticPropertyChanged += (_, _) => QueueSystemRefresh();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => QueueSystemRefresh();
    }

    static void QueueSystemRefresh()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || (_themeSetting != "system" && !_accentFollowsSystem)) return;
        if (System.Threading.Interlocked.Exchange(ref _systemRefreshQueued, 1) != 0) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            System.Threading.Interlocked.Exchange(ref _systemRefreshQueued, 0);
            RefreshSystemPreferences();
        }));
    }

    static void RefreshSystemPreferences()
    {
        if (_themeSetting != "system" && !_accentFollowsSystem) return;
        bool dark = IsDark; Color accent = Accent;
        Apply(_themeSetting, _accentSetting);
        if (IsDark != dark || Accent != accent) Fluent.ApplyGlobalStyles();
    }

    static Color SystemAccent()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int value)
                return Color.FromRgb((byte)value, (byte)(value >> 8), (byte)(value >> 16));
        }
        catch { }
        return DefaultAccent;
    }

    /// <summary>共享画刷：与 <see cref="Resolved"/> 同序，一枚令牌一支，全窗共用同一实例。
    /// 界面取色拿这支，而不是自己 new 一支——颜色在构造期被抄进画刷后就定死了，
    /// 改完配色要整窗重建才跟得上，取色器拖动期间界面是一片死色。</summary>
    static readonly SolidColorBrush[] Paints = BuildPaints();

    static SolidColorBrush[] BuildPaints()
    {
        var a = new SolidColorBrush[Registry.Length];
        for (int i = 0; i < a.Length; i++) a[i] = new SolidColorBrush(Colors.Transparent);
        return a;
    }

    /// <summary>窗口底与面板底的不透明版本，同样共享：弹窗与菜单压在 Mica 上要的是实底。</summary>
    static readonly SolidColorBrush WindowBgSolidPaint = new SolidColorBrush(Colors.Transparent);
    static readonly SolidColorBrush PanelBgSolidPaint = new SolidColorBrush(Colors.Transparent);

    /// <summary>强调色画刷：强调色不在令牌表里，单独跟着 <see cref="Apply"/> 与 <see cref="ResetAccent"/> 同步。</summary>
    static readonly SolidColorBrush AccentPaint = new SolidColorBrush(Colors.Transparent);

    /// <summary>这支画刷是不是调色板共享画刷。共享实例被全窗共用，就地动画会带着所有用它的元素一起动，
    /// 想做悬停这类局部动画得先克隆一份再动。</summary>
    /// <param name="b">待判画刷，可为 null。</param>
    /// <returns>是共享实例返回 true。</returns>
    public static bool IsPaletteBrush(SolidColorBrush? b)
        => b != null && (Array.IndexOf(Paints, b) >= 0
            || ReferenceEquals(b, WindowBgSolidPaint)
            || ReferenceEquals(b, PanelBgSolidPaint)
            || ReferenceEquals(b, AccentPaint));

    /// <summary>把解析好的颜色推给共享画刷。当帧落色，不给画刷挂颜色动画：
    /// 动画在飞的时候 <see cref="SolidColorBrush.Color"/> 读到的是动画值而不是终点，
    /// 渲染节拍没跟上时甚至会一直停在起始色——悬停克隆、断言、下一次比较全被带偏。
    /// 深浅互换的过渡由 <see cref="Fluent.MaskFade"/> 那层幕布负责，画刷只管把当前色答对。</summary>
    static void SyncPaints()
    {
        for (int i = 0; i < Paints.Length; i++) Paints[i].Color = Resolved[i];
        WindowBgSolidPaint.Color = Opaque(Resolved[(int)Slot.WindowBg]);
        PanelBgSolidPaint.Color = Opaque(Resolved[(int)Slot.PanelBg]);
        AccentPaint.Color = Accent;
    }

    static string ModeKey(bool dark, string key) => (dark ? "dark." : "light.") + key;

    static void Recompute()
    {
        bool dark = EffectiveDark;
        try { Changing(dark); } catch { /* 拍不到退场画面的窗口自己退回纯色遮罩 */ }
        for (int i = 0; i < Registry.Length; i++)
            Resolved[i] = Customs.TryGetValue(ModeKey(dark, Registry[i].Key), out var c)
                ? c : Registry[i].Default(dark);
        SyncPaints();
    }

    static bool Same(Color[] a)
    {
        for (int i = 0; i < a.Length; i++) if (a[i] != Resolved[i]) return false;
        return true;
    }

    /// <summary>把设置里的主题与强调色落到全局调色板，实际变了才广播 <see cref="Changed"/>。</summary>
    /// <param name="theme">"light" / "dark"；其余值（含空）跟随系统 AppsUseLightTheme。</param>
    /// <param name="accentHex">强调色 HEX 文本；system 跟随系统强调色，空白或解析失败保持当前值。</param>
    public static void Apply(string theme, string accentHex)
    {
        _themeSetting = theme is "light" or "dark" ? theme : "system";
        _accentSetting = accentHex ?? "";
        var old = (Color[])Resolved.Clone();
        var oldDark = EffectiveDark;
        var oldAcc = Accent;
        EffectiveDark = theme switch
        {
            "light" => false,
            "dark" => true,
            _ => SystemParams.UserPreferenceDark(),
        };
        _accentFollowsSystem = accentHex == "system";
        if (_accentFollowsSystem) Accent = SystemAccent();
        else if (!string.IsNullOrWhiteSpace(accentHex))
        {
            try { Accent = (Color)ColorConverter.ConvertFromString(accentHex); }
            catch { /* 保持当前强调色 */ }
        }
        Recompute();
        if (EffectiveDark != oldDark || Accent != oldAcc || !Same(old))
        {
            try { Changed(); } catch { /* 某窗口换色失败不影响其余 */ }
        }
    }

    /// <summary>当前主题下这个令牌的实际颜色。</summary>
    /// <param name="key">令牌键名；认不出返回 <see cref="Colors.Transparent"/>，让错键在截图里显形而不是抛。</param>
    /// <returns>自定义色，没自定义则当前主题的出厂色。</returns>
    public static Color Current(string key)
        => Ordinals.TryGetValue(key, out var i) ? Resolved[i] : Colors.Transparent;

    /// <summary>某个主题下这个令牌的出厂色。</summary>
    /// <param name="key">令牌键名。</param>
    /// <param name="dark">true 取深色值。</param>
    /// <returns>出厂色；认不出键返回 <see cref="Colors.Transparent"/>。</returns>
    public static Color DefaultColor(string key, bool dark)
        => Ordinals.TryGetValue(key, out var i) ? Registry[i].Default(dark) : Colors.Transparent;

    /// <summary>当前主题下这个令牌的自定义色。</summary>
    /// <param name="key">令牌键名。</param>
    /// <returns>自定义色；没自定义返回 null。</returns>
    public static Color? Custom(string key)
        => Customs.TryGetValue(ModeKey(EffectiveDark, key), out var c) ? c : null;

    /// <summary>某个主题下这个令牌是否被自定义过。</summary>
    /// <param name="key">令牌键名。</param>
    /// <param name="dark">要问的主题侧。</param>
    /// <returns>有自定义色返回 true。</returns>
    public static bool IsCustom(string key, bool dark) => Customs.ContainsKey(ModeKey(dark, key));

    /// <summary>写入一枚自定义色并广播；与现值相同则什么都不做。</summary>
    /// <param name="key">令牌键名；认不出直接返回 false。</param>
    /// <param name="dark">写到哪一侧主题。</param>
    /// <param name="color">新的颜色，alpha 原样保留。</param>
    /// <returns>写入并广播了返回 true。</returns>
    public static bool SetCustom(string key, bool dark, Color color)
    {
        if (!Ordinals.ContainsKey(key)) return false;
        var mk = ModeKey(dark, key);
        if (Customs.TryGetValue(mk, out var cur) && cur == color) return false;
        Customs[mk] = color;
        Recompute();
        try { Changed(); } catch { }
        return true;
    }

    /// <summary>去掉一枚自定义色，回到该主题的出厂色。</summary>
    /// <param name="key">令牌键名。</param>
    /// <param name="dark">去掉哪一侧主题的自定义。</param>
    /// <returns>确实去掉了一条并广播了返回 true。</returns>
    public static bool ClearCustom(string key, bool dark)
    {
        if (!Customs.Remove(ModeKey(dark, key))) return false;
        Recompute();
        try { Changed(); } catch { }
        return true;
    }

    /// <summary>去掉全部自定义色，整套回到出厂调色板。有东西可去时才广播。</summary>
    public static void ClearAllCustoms()
    {
        if (Customs.Count == 0) return;
        Customs.Clear();
        Recompute();
        try { Changed(); } catch { }
    }

    /// <summary>整套覆盖表整体载入（启动与设置热重载）。非法键、非法色直接忽略。
    /// 实际改了颜色才广播——热重载路径上主题与强调色可能一个字没动，只有配色变了。</summary>
    /// <param name="map">设置文件里的 <c>dark.CardBg</c> → HEX 映射；null 视为清空。</param>
    public static void LoadCustoms(IEnumerable<KeyValuePair<string, string>>? map)
    {
        var old = (Color[])Resolved.Clone();
        Customs.Clear();
        if (map != null)
        {
            // KeyValuePair 的解构在 net48 一侧不存在：本文件与安装器共用，逐键逐值取
            foreach (var kv in map)
            {
                var k = kv.Key;
                int dot = k.IndexOf('.');
                if (dot <= 0 || dot == k.Length - 1) continue;
                if (k.Substring(0, dot) is not ("dark" or "light")) continue;
                if (!Ordinals.ContainsKey(k.Substring(dot + 1))) continue;
                var c = HexColor.Parse(kv.Value);
                if (c != null) Customs[k] = c.Value;
            }
        }
        Recompute();
        if (!Same(old))
        {
            try { Changed(); } catch { }
        }
    }

    /// <summary>导出当前覆盖表，供设置文件保存。</summary>
    /// <returns>键为 <c>dark.CardBg</c> 形式、值为最短 HEX 的新字典。</returns>
    public static Dictionary<string, string> ExportCustoms()
    {
        var d = new Dictionary<string, string>(Customs.Count, StringComparer.Ordinal);
        foreach (var kv in Customs) d[kv.Key] = HexColor.Format(kv.Value);
        return d;
    }

    /// <summary>指定主题的令牌颜色：自定义色优先，否则取该主题的出厂色。</summary>
    /// <param name="key">令牌键名。</param>
    /// <param name="dark">要问哪一侧主题。</param>
    /// <returns>那一侧的实际颜色；认不出键返回 <see cref="Colors.Transparent"/>。</returns>
    public static Color ValueOf(string key, bool dark)
        => Ordinals.TryGetValue(key, out var i)
            ? (Customs.TryGetValue(ModeKey(dark, Registry[i].Key), out var c) ? c : Registry[i].Default(dark))
            : Colors.Transparent;

    /// <summary>把强调色放回出厂值，实际变了才广播。</summary>
    public static void ResetAccent()
    {
        _accentSetting = "";
        _accentFollowsSystem = false;
        if (Accent == DefaultAccent) return;
        Accent = DefaultAccent;
        SyncPaints();
        try { Changed(); } catch { }
    }

    /// <summary>拖动强调色取色器时的即时生效：改色、同步共享画刷、广播，不落盘。</summary>
    /// <param name="c">取色板当前的颜色。</param>
    public static void PreviewAccent(Color c)
    {
        if (Accent == c) return;
        Accent = c;
        SyncPaints();
        try { Changed(); } catch { }
    }

    /// <summary>这套调色板里有没有任何一枚被自定义过。</summary>
    public static bool HasCustoms => Customs.Count > 0;

    public static Color WindowBg => Resolved[(int)Slot.WindowBg];
    public static Color PanelBg => Resolved[(int)Slot.PanelBg];
    public static Color ChromeBg => Resolved[(int)Slot.ChromeBg];

    /// <summary>把 alpha 拉满、RGB 原样保留，供没有模糊背板的分层表面使用。</summary>
    /// <param name="c">源色，只取 RGB。</param>
    /// <returns>同 RGB、alpha 为 0xFF 的颜色。</returns>
    public static Color Opaque(Color c) => Color.FromArgb(0xFF, c.R, c.G, c.B);

    /// <summary>分层窗（<c>AllowsTransparency</c>）的窗口底色：与 <see cref="WindowBg"/> 同色但不透明。
    /// 分层窗背后没有模糊背板，任何 alpha 都是直接透出桌面内容，压在文字密集的桌面上会浮出背后文字。</summary>
    public static Color WindowBgSolid => Opaque(WindowBg);

    /// <summary>分层窗与弹出层的面板底色：与 <see cref="PanelBg"/> 同色但不透明，理由同
    /// <see cref="WindowBgSolid"/>。</summary>
    public static Color PanelBgSolid => Opaque(PanelBg);

    public static Color Text => Resolved[(int)Slot.Text];

    /// <summary>次要文字：说明、提示、键帽、次要图标都读它。两套主题都取不透明灰——
    /// 带 Alpha 的浅灰压在白卡片上只有 3.2:1、压在标题栏底上 2.9:1，正文级的说明文字读起来费劲。</summary>
    public static Color TextDim => Resolved[(int)Slot.TextDim];

    public static Color Stroke => Resolved[(int)Slot.Stroke];

    /// <summary>比 <see cref="Stroke"/> 更淡的一档描边：成排出现的框用它。
    /// 一列条目各自描满标准描边，整列就被切成一堆格子，标准描边留给卡片外框这类一处边界。</summary>
    public static Color StrokeSoft => Resolved[(int)Slot.StrokeSoft];

    /// <summary>控件描边：键帽、芯片、输入框、开关轨道这一圈。与卡片外框分开一格，
    /// 调细调淡不必连卡片边界一起动。</summary>
    public static Color StrokeField => Resolved[(int)Slot.StrokeField];

    /// <summary>键帽底色：快捷键页与编辑器键位那一圈键帽的底，比卡片差开一档而自成一档。</summary>
    public static Color KeycapBg => Resolved[(int)Slot.KeycapBg];

    /// <summary>芯片底色：格式芯片、触发方式芯片这类小方块的底。</summary>
    public static Color ChipBg => Resolved[(int)Slot.ChipBg];

    /// <summary>悬停叠加层：只作为 <see cref="Fluent.Mix"/> 的一层压在底色上，不直接当底色铺。</summary>
    public static Color Hover => Resolved[(int)Slot.Hover];

    /// <summary>按下叠加层：恒比 <see cref="Hover"/> 再走一档，两套主题同方向。</summary>
    public static Color Press => Resolved[(int)Slot.Press];

    public static Color CardBg => Resolved[(int)Slot.CardBg];

    /// <summary>卡片里一行的底色：暗色比卡片亮一档，亮色比卡片暗一档。
    /// 行与卡片同色时，层次只剩 1px 描边——整页读起来是一叠线框而不是分层的表面；
    /// 给行一个方向正确的底色，边界靠明度说话，描边退回成一根细线。</summary>
    public static Color RowBg => Resolved[(int)Slot.RowBg];

    /// <summary>行悬停底色：在 <see cref="RowBg"/> 上再走一档，方向与它一致。不透明，铺上去不叠第二层。</summary>
    public static Color RowHover => Resolved[(int)Slot.RowHover];

    /// <summary>侧栏导航的承托面：导航项的静止、悬停、选中三态都算在这块底色上。</summary>
    public static Color NavBg => Resolved[(int)Slot.NavBg];

    /// <summary>轨道底色：开关关态、滑杆未填充段、输入框底。</summary>
    public static Color TrackBg => Resolved[(int)Slot.TrackBg];

    /// <summary>提醒色：撞键提示这类「需要你处理一下」的标记。</summary>
    public static Color Danger => Resolved[(int)Slot.Danger];

    /// <summary>成色：状态点这类「一切正常」的标记。</summary>
    public static Color Success => Resolved[(int)Slot.Success];

    /// <summary>注意色：可用但有前提的状态点。</summary>
    public static Color Warning => Resolved[(int)Slot.Warning];

    #region 共享画刷

    // 上面那些 Color 属性是「此刻的颜色」，取到手就凝固了；下面这些是同一批颜色的实例形态：
    // 元素的底色与笔触指到它们上，配色一改就顺着画刷自己跟上，不必重建窗口。
    // 一支画刷全窗共用，想给单个元素做悬停动画，先按 IsPaletteBrush 判出来再克隆一份（见 Fluent.BgHover）。
    // 悬停与按下是叠加层不是表面，没有对应的画刷——它们只该被 Fluent.HoverOn/PressOn 混进底色。

    /// <summary>窗口底画刷（共享实例）。</summary>
    public static SolidColorBrush WindowBgBrush => Paints[(int)Slot.WindowBg];
    /// <summary>面板底画刷（共享实例）。</summary>
    public static SolidColorBrush PanelBgBrush => Paints[(int)Slot.PanelBg];
    /// <summary>标题栏底画刷（共享实例）。</summary>
    public static SolidColorBrush ChromeBgBrush => Paints[(int)Slot.ChromeBg];
    /// <summary>卡片底画刷（共享实例）。</summary>
    public static SolidColorBrush CardBgBrush => Paints[(int)Slot.CardBg];
    /// <summary>行底画刷（共享实例）。</summary>
    public static SolidColorBrush RowBgBrush => Paints[(int)Slot.RowBg];
    /// <summary>侧栏底画刷（共享实例）。</summary>
    public static SolidColorBrush NavBgBrush => Paints[(int)Slot.NavBg];
    /// <summary>正文色画刷（共享实例）。</summary>
    public static SolidColorBrush TextBrush => Paints[(int)Slot.Text];
    /// <summary>次要文字画刷（共享实例）。</summary>
    public static SolidColorBrush TextDimBrush => Paints[(int)Slot.TextDim];
    /// <summary>标准描边画刷（共享实例）。</summary>
    public static SolidColorBrush StrokeBrush => Paints[(int)Slot.Stroke];
    /// <summary>细描边画刷（共享实例）。</summary>
    public static SolidColorBrush StrokeSoftBrush => Paints[(int)Slot.StrokeSoft];
    /// <summary>控件描边画刷（共享实例）：键帽、芯片、输入框、开关轨道。</summary>
    public static SolidColorBrush StrokeFieldBrush => Paints[(int)Slot.StrokeField];
    /// <summary>键帽底色画刷（共享实例）。</summary>
    public static SolidColorBrush KeycapBgBrush => Paints[(int)Slot.KeycapBg];
    /// <summary>芯片底色画刷（共享实例）。</summary>
    public static SolidColorBrush ChipBgBrush => Paints[(int)Slot.ChipBg];
    /// <summary>行悬停底画刷（共享实例）。</summary>
    public static SolidColorBrush RowHoverBrush => Paints[(int)Slot.RowHover];
    /// <summary>轨道画刷（共享实例）。</summary>
    public static SolidColorBrush TrackBgBrush => Paints[(int)Slot.TrackBg];
    /// <summary>提醒色画刷（共享实例）。</summary>
    public static SolidColorBrush DangerBrush => Paints[(int)Slot.Danger];
    /// <summary>成色画刷（共享实例）。</summary>
    public static SolidColorBrush SuccessBrush => Paints[(int)Slot.Success];
    /// <summary>注意色画刷（共享实例）。</summary>
    public static SolidColorBrush WarningBrush => Paints[(int)Slot.Warning];
    /// <summary>窗口底的不透明画刷（共享实例）：弹窗、菜单这类要实底的场合。</summary>
    public static SolidColorBrush WindowBgSolidBrush => WindowBgSolidPaint;
    /// <summary>面板底的不透明画刷（共享实例）。</summary>
    public static SolidColorBrush PanelBgSolidBrush => PanelBgSolidPaint;
    /// <summary>强调色画刷（共享实例）。</summary>
    public static SolidColorBrush AccentBrush => AccentPaint;

    #endregion
}

static class SystemParams
{
    /// <summary>读系统的「应用用深色」偏好。</summary>
    /// <returns>AppsUseLightTheme = 0 时 true；取不到值（键缺失、值不是 int）按 false 走浅色，注册表访问抛异常时按 true。</returns>
    public static bool UserPreferenceDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            return v is int i && i == 0;
        }
        catch { return true; }
    }
}

/// <summary>#RGB / #RRGGBB / #RRGGBBAA 解析与最短形式格式化（A=255 时省略 AA）。
/// 与 <see cref="Theme"/> 同文件：覆盖表在设置里就是 HEX 文本，装进调色板与写回设置都过这里。</summary>
public static class HexColor
{
    /// <summary>解析用户输入的 HEX 颜色。</summary>
    /// <param name="text">3/6/8 位十六进制；前缀 <c>#</c> 可省，也认 <c>0x</c>；3 位按每位翻倍展开。</param>
    /// <returns>带 alpha 的颜色，6 位输入按不透明（A=255）。长度不是 3/6/8 或含非十六进制字符时 null。</returns>
    public static Color? Parse(string? text)
    {
        if (text == null) return null;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        if (s.StartsWith("#")) s = s.Substring(1);
        if (s.Length == 3) s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
        if (s.Length == 6) s += "FF";
        if (s.Length != 8) return null;
        for (int i = 0; i < 8; i++) if (!Uri.IsHexDigit(s[i])) return null;
        return Color.FromArgb(
            Convert.ToByte(s.Substring(6, 2), 16),
            Convert.ToByte(s.Substring(0, 2), 16),
            Convert.ToByte(s.Substring(2, 2), 16),
            Convert.ToByte(s.Substring(4, 2), 16));
    }

    /// <summary>颜色 → 最短 HEX 文本。</summary>
    /// <param name="c">要格式化的颜色。</param>
    /// <returns>不透明时 <c>#RRGGBB</c>，带透明时 <c>#RRGGBBAA</c>；大写，alpha=255 不写冗余尾。</returns>
    public static string Format(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
}
