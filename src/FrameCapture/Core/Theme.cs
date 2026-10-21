using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>
/// 明暗主题调色板。所有覆盖层从这里取色；主题变化时广播 <see cref="Changed"/>，订阅的窗口即时换色，新建窗口直接取新色。
/// 基调：Fluent 深色 #202020 / 浅色 #F3F3F3，强调色可自定义。
/// 背景类颜色自带 alpha：Chrome 0xD9 最透、Window 0xF2 近不透、Card 全不透明——覆盖层要透出桌面。
/// </summary>
public static class Theme
{
    /// <summary>当前强调色。</summary>
    /// <value>由 <see cref="Apply"/> 从设置写入；解析失败时保持原值。</value>
    public static Color Accent = Color.FromRgb(0x4C, 0xC2, 0xFF);

    /// <summary>当前生效的是否深色主题。</summary>
    /// <value>调色板所有属性分支的依据，由 <see cref="Apply"/> 从设置算出；未 Apply 前为深色。</value>
    public static bool IsDark => EffectiveDark;
    static bool EffectiveDark = true;

    /// <summary>主题实际变化后触发，窗口订阅后可即时换色。</summary>
    public static event Action Changed = () => { };

    /// <summary>把设置里的主题与强调色落到全局调色板，实际变了才广播 <see cref="Changed"/>。</summary>
    /// <param name="theme">"light" / "dark"；其余值（含空）跟随系统 AppsUseLightTheme。</param>
    /// <param name="accentHex">强调色 HEX 文本；null、空白或解析失败都保持当前强调色。</param>
    public static void Apply(string theme, string accentHex)
    {
        var oldDark = EffectiveDark;
        var oldAcc = Accent;
        EffectiveDark = theme switch
        {
            "light" => false,
            "dark" => true,
            _ => SystemParams.UserPreferenceDark(),
        };
        if (!string.IsNullOrWhiteSpace(accentHex))
        {
            try { Accent = (Color)ColorConverter.ConvertFromString(accentHex); }
            catch { /* 保持当前强调色 */ }
        }
        if (EffectiveDark != oldDark || Accent != oldAcc)
        {
            try { Changed(); } catch { /* 某窗口换色失败不影响其余 */ }
        }
    }

    static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    public static Color WindowBg => IsDark ? C(0xF2, 0x20, 0x20, 0x20) : C(0xF2, 0xF9, 0xF9, 0xF9);
    public static Color PanelBg => IsDark ? C(0xE6, 0x2B, 0x2B, 0x2B) : C(0xE6, 0xFF, 0xFF, 0xFF);
    public static Color ChromeBg => IsDark ? C(0xD9, 0x1C, 0x1C, 0x1C) : C(0xD9, 0xEB, 0xEB, 0xEB);

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
    public static Color Text => IsDark ? C(0xFF, 0xFF, 0xFF, 0xFF) : C(0xFF, 0x1A, 0x1A, 0x1A);
    /// <summary>次要文字：说明、提示、键帽、次要图标都读它。亮色主题用不透明的 #616161——
    /// 带 Alpha 的浅灰压在白卡片上只有 3.2:1、压在标题栏底上 2.9:1，正文级的说明文字读起来费劲；
    /// 暗色主题把透明度提到 0.8，压在卡片上从 4.5:1 抬到 5.4:1，留出余量。</summary>
    public static Color TextDim => IsDark ? C(0xCC, 0xC0, 0xC0, 0xC0) : C(0xFF, 0x61, 0x61, 0x61);
    public static Color Stroke => IsDark ? C(0x55, 0xFF, 0xFF, 0xFF) : C(0x55, 0x00, 0x00, 0x00);
    /// <summary>比 <see cref="Stroke"/> 更淡的一档描边：成排出现的框用它。
    /// 一列条目各自描满标准描边，整列就被切成一堆格子，标准描边留给卡片外框这类一处边界。</summary>
    public static Color StrokeSoft => IsDark ? C(0x2E, 0xFF, 0xFF, 0xFF) : C(0x26, 0x00, 0x00, 0x00);
    public static Color Hover => IsDark ? C(0x22, 0xFF, 0xFF, 0xFF) : C(0x33, 0x00, 0x00, 0x00);
    public static Color Press => IsDark ? C(0x15, 0xFF, 0xFF, 0xFF) : C(0x22, 0x00, 0x00, 0x00);
    public static Color CardBg => IsDark ? C(0xFF, 0x2D, 0x2D, 0x2D) : C(0xFF, 0xFF, 0xFF, 0xFF);
    /// <summary>卡片里一行的底色：暗色比卡片亮一档，亮色比卡片暗一档。
    /// 行与卡片同色时，层次只剩 1px 描边——整页读起来是一叠线框而不是分层的表面；
    /// 给行一个方向正确的底色，边界靠明度说话，描边退回成一根细线。</summary>
    public static Color RowBg => IsDark ? C(0xFF, 0x35, 0x35, 0x38) : C(0xFF, 0xF5, 0xF5, 0xF6);
    /// <summary>行悬停底色：在 <see cref="RowBg"/> 上再走一档，方向与它一致。</summary>
    public static Color RowHover => IsDark ? C(0xFF, 0x3D, 0x3D, 0x41) : C(0xFF, 0xEC, 0xEC, 0xEE);
    public static Color TrackBg => IsDark ? C(0x33, 0xFF, 0xFF, 0xFF) : C(0x33, 0x00, 0x00, 0x00);

    /// <summary>问题色：撞键提示这类「需要你处理一下」的标记，明暗两套同值。</summary>
    public static Color Danger => C(0xFF, 0xFF, 0x6B, 0x6B);
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
