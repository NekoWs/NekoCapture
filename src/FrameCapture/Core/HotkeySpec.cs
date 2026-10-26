using System.Text;
using System.Text.RegularExpressions;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace FrameCapture.Core;

/// <summary>
/// 全局快捷键描述："Ctrl+Shift+G"、"F9"、"Alt+F4"…
/// F1..F24 允许无修饰键；其他键必须带至少一个修饰键，防止劫持普通输入。
/// </summary>
public sealed partial class HotkeySpec
{
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }
    public bool Win { get; init; }
    public WinFormsKeys Key { get; init; }

    /// <summary>RegisterHotKey 的修饰键位标志，取值按 Win32 原样。</summary>
    /// <value>MOD_NOREPEAT 恒被 <see cref="Modifiers"/> 叠加：按住不放不重复触发。</value>
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    /// <summary>交给 RegisterHotKey 的修饰键掩码。</summary>
    /// <value>四个分量按位或，且<b>总是</b>带上 MOD_NOREPEAT。</value>
    public uint Modifiers =>
        (Alt ? MOD_ALT : 0) | (Ctrl ? MOD_CONTROL : 0) | (Shift ? MOD_SHIFT : 0) | (Win ? MOD_WIN : 0) | MOD_NOREPEAT;

    /// <summary>主键的 Win32 虚拟键码（VK_*）。</summary>
    /// <value><c>(uint)Key</c>，直接用于 RegisterHotKey 的 vk 参数。</value>
    public uint VirtualKey => (uint)Key;

    /// <summary>主键是否为“修饰键本身”：这类键不能单独当主键绑定。</summary>
    public bool KeyIsModifier => Key is WinFormsKeys.ControlKey or WinFormsKeys.ShiftKey or WinFormsKeys.Menu
        or WinFormsKeys.LWin or WinFormsKeys.RWin or WinFormsKeys.LShiftKey or WinFormsKeys.RShiftKey
        or WinFormsKeys.LControlKey or WinFormsKeys.RControlKey;

    /// <summary>渲染成快捷键文本。</summary>
    /// <returns>形如 "Ctrl+Alt+A"；修饰键顺序固定 Ctrl → Alt → Shift → Win，与 <see cref="Parse"/> 接受的写法同源。</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Ctrl) sb.Append("Ctrl+");
        if (Alt) sb.Append("Alt+");
        if (Shift) sb.Append("Shift+");
        if (Win) sb.Append("Win+");
        sb.Append(KeyName(Key));
        return sb.ToString();
    }

    /// <summary>四个修饰键标志与主键全部相同，才算同一个快捷键。</summary>
    /// <param name="obj">待比较对象；非 <see cref="HotkeySpec"/> 直接 false。</param>
    /// <returns>逐分量全等的结果；左右修饰键（LShift 与 Shift）是不同 Key，视为不相等。</returns>
    public override bool Equals(object? obj) =>
        obj is HotkeySpec h && h.Ctrl == Ctrl && h.Alt == Alt && h.Shift == Shift && h.Win == Win && h.Key == Key;

    /// <summary>与 <see cref="Equals"/> 取同一组分量的哈希。</summary>
    /// <returns>HashCode.Combine(Ctrl, Alt, Shift, Win, Key)，保证相等对象哈希相等。</returns>
    public override int GetHashCode() => HashCode.Combine(Ctrl, Alt, Shift, Win, (int)Key);

    /// <summary>解析快捷键文本。</summary>
    /// <param name="text">以 + 分隔的串，如 "Ctrl+Shift+G"。修饰键认 ctrl/control、alt、shift、win/windows/super；
    /// 主键按 <c>System.Windows.Forms.Keys</c> 枚举名，另认 esc/enter/space/prtsc 等别名与 0–9、numpad0–9。
    /// 大小写与前后空白不敏感。</param>
    /// <returns>合法且安全的快捷键。null/空白、出现两个主键、键名认不出、裸普通键（非 F1–F24 且无修饰键）、
    /// 主键本身是修饰键时都返回 null——调用方据此拒绝注册，而不是劫持用户的普通按键。</returns>
    public static HotkeySpec? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        bool ctrl = false, alt = false, shift = false, win = false;
        WinFormsKeys? key = null;

        foreach (var rawTok in text.Split('+'))
        {
            var tok = rawTok.Trim();
            if (tok.Length == 0) continue;
            switch (tok.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "shift": shift = true; continue;
                case "win" or "windows" or "super": win = true; continue;
            }
            if (key != null) return null; // 两个主键
            var k = LookupKey(tok);
            if (k == null) return null;
            key = k;
        }

        if (key == null) return null;
        bool hasMod = ctrl || alt || shift || win;
        // 裸键只放行两类：F1–F24（本来就不参与输入）与 PrintScreen（系统里它就只为截图存在）。
        // 其余裸键一律拒收——热键在窗口外也生效，收下裸字母等于劫持别的软件的输入。
        bool safeSingle = IsFunctionKey(key.Value) || key.Value is WinFormsKeys.PrintScreen or WinFormsKeys.Snapshot;
        if (!hasMod && !safeSingle) return null;
        if (KeyIsModifierStatic(key.Value)) return null;

        return new HotkeySpec { Ctrl = ctrl, Alt = alt, Shift = shift, Win = win, Key = key.Value };
    }

    static bool IsFunctionKey(WinFormsKeys k) => k >= WinFormsKeys.F1 && k <= WinFormsKeys.F24;

    static bool KeyIsModifierStatic(WinFormsKeys k) =>
        new HotkeySpec { Key = k }.KeyIsModifier;

    static string KeyName(WinFormsKeys k) => k switch
    {
        WinFormsKeys.ControlKey => "Ctrl",
        WinFormsKeys.Menu => "Alt",
        WinFormsKeys.ShiftKey => "Shift",
        // 键帽与设置串里都写 PrtSc：Windows 的「使用 Print Screen 键打开截图工具」也叫这个名字
        WinFormsKeys.PrintScreen or WinFormsKeys.Snapshot => "PrtSc",
        _ => k.ToString(),
    };


    static WinFormsKeys? LookupKey(string tok)
    {
        if (Enum.TryParse<WinFormsKeys>(tok, ignoreCase: true, out var k) && k != WinFormsKeys.None)
        {
            // Enum.TryParse 会把 "Shift Left" 之类也接受；限制为基础键
            if ((k & WinFormsKeys.KeyCode) == k || IsFunctionKey(k)) return (k & WinFormsKeys.KeyCode);
        }
        // 数字 "0".."9" 需要转 D0..D9（Enum 名是 D0）
        if (tok.Length == 1 && char.IsBetween(tok[0], '0', '9'))
            return WinFormsKeys.D0 + (tok[0] - '0');
        switch (tok.ToLowerInvariant())
        {
            case "escape" or "esc": return WinFormsKeys.Escape;
            case "return" or "enter": return WinFormsKeys.Return;
            case "space": return WinFormsKeys.Space;
            case "up" or "pageup" or "prior": return WinFormsKeys.Up;
            case "down": return WinFormsKeys.Down;
            case "left": return WinFormsKeys.Left;
            case "right": return WinFormsKeys.Right;
            case "page down" or "pagedown" or "next": return WinFormsKeys.Next;
            case "home": return WinFormsKeys.Home;
            case "end": return WinFormsKeys.End;
            case "insert" or "ins": return WinFormsKeys.Insert;
            case "delete" or "del": return WinFormsKeys.Delete;
            case "backspace" or "back": return WinFormsKeys.Back;
            case "tab": return WinFormsKeys.Tab;
            case "capslock": return WinFormsKeys.CapsLock;
            case "printscreen" or "prtsc": return WinFormsKeys.PrintScreen;
            case "snapshot": return WinFormsKeys.Snapshot;
            case "+": return WinFormsKeys.Add;
            case "-": return WinFormsKeys.Subtract;
            case "*": return WinFormsKeys.Multiply;
            case "/": return WinFormsKeys.Divide;
        }
        if (NumPadRe().IsMatch(tok) && Enum.TryParse<WinFormsKeys>("NumPad" + tok[1..], out var np)) return np;
        return null;
    }

    [GeneratedRegex("^(numpad|num)[0-9]$")]
    private static partial Regex NumPadRe();
}