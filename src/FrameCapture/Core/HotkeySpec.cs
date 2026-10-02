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

    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    public uint Modifiers =>
        (Alt ? MOD_ALT : 0) | (Ctrl ? MOD_CONTROL : 0) | (Shift ? MOD_SHIFT : 0) | (Win ? MOD_WIN : 0) | MOD_NOREPEAT;

    public uint VirtualKey => (uint)Key;

    /// <summary>主键是否为“修饰键本身”（如用户按 F12 之外的裸键）。</summary>
    public bool KeyIsModifier => Key is WinFormsKeys.ControlKey or WinFormsKeys.ShiftKey or WinFormsKeys.Menu
        or WinFormsKeys.LWin or WinFormsKeys.RWin or WinFormsKeys.LShiftKey or WinFormsKeys.RShiftKey
        or WinFormsKeys.LControlKey or WinFormsKeys.RControlKey;

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

    public override bool Equals(object? obj) =>
        obj is HotkeySpec h && h.Ctrl == Ctrl && h.Alt == Alt && h.Shift == Shift && h.Win == Win && h.Key == Key;

    public override int GetHashCode() => HashCode.Combine(Ctrl, Alt, Shift, Win, (int)Key);

    /// <summary>解析快捷键文本。非法或不安全（裸普通键）时返回 null。</summary>
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
        bool safeSingle = IsFunctionKey(key.Value);
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
