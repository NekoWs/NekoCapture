using System.Windows;
using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>
/// 明暗主题调色板。所有覆盖层从这里取色，主题切换后新建窗口即生效。
/// 基调：Fluent 深色 #202020 / 浅色 #F3F3F3，强调色可自定义。
/// </summary>
public static class Theme
{
    public static Color Accent = Color.FromRgb(0x4C, 0xC2, 0xFF);

    public static bool IsDark => EffectiveDark;
    static bool EffectiveDark = true;

    /// <summary>主题实际变化后触发，窗口订阅后可即时换色。</summary>
    public static event Action Changed = () => { };

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
            catch { /* 保持默认 */ }
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
    public static Color Text => IsDark ? C(0xFF, 0xFF, 0xFF, 0xFF) : C(0xFF, 0x1A, 0x1A, 0x1A);
    public static Color TextDim => IsDark ? C(0xB3, 0xC0, 0xC0, 0xC0) : C(0xB3, 0x60, 0x60, 0x60);
    public static Color Stroke => IsDark ? C(0x55, 0xFF, 0xFF, 0xFF) : C(0x55, 0x00, 0x00, 0x00);
    public static Color Hover => IsDark ? C(0x22, 0xFF, 0xFF, 0xFF) : C(0x33, 0x00, 0x00, 0x00);
    public static Color Press => IsDark ? C(0x15, 0xFF, 0xFF, 0xFF) : C(0x22, 0x00, 0x00, 0x00);
    public static Color CardBg => IsDark ? C(0xFF, 0x2D, 0x2D, 0x2D) : C(0xFF, 0xFF, 0xFF, 0xFF);
    public static Color TrackBg => IsDark ? C(0x33, 0xFF, 0xFF, 0xFF) : C(0x33, 0x00, 0x00, 0x00);
}

static class SystemParams
{
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
