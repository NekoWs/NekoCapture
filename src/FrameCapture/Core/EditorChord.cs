using System.Windows.Input;

namespace FrameCapture.Core;

/// <summary>
/// 编辑器命令的键位：修饰键 + 一个主键，允许裸键。
/// 全局热键走 <see cref="HotkeySpec"/>，那里裸普通键一律拒收——热键在窗口外也要生效，收下裸键等于劫持别的软件的输入。
/// 编辑器键位只在这扇窗口拿到焦点时参与分派，裸键正是它的主要形态。
/// </summary>
/// <param name="Mods">修饰键，只取 Ctrl / Alt / Shift / Win 四位。</param>
/// <param name="Key">主键，已按 <see cref="Canonical"/> 归一到不分左右、不分主键盘与小键盘的形态。</param>
/// <returns>按给定值构造的不可变记录。</returns>
public readonly record struct EditorChord(ModifierKeys Mods, Key Key)
{
    /// <summary>不带修饰键的裸键。</summary>
    /// <param name="k">主键。</param>
    /// <returns>修饰键为空的键位。</returns>
    public static EditorChord Bare(Key k) => new(ModifierKeys.None, Canonical(k));

    /// <summary>只带 Ctrl 的组合键。</summary>
    /// <param name="k">主键。</param>
    /// <returns>Ctrl 加主键。</returns>
    public static EditorChord Ctrl(Key k) => new(ModifierKeys.Control, Canonical(k));

    /// <summary>只带 Shift 的组合键。</summary>
    /// <param name="k">主键。</param>
    /// <returns>Shift 加主键。</returns>
    public static EditorChord Shift(Key k) => new(ModifierKeys.Shift, Canonical(k));

    /// <summary>把键位串读成键位：修饰键前缀 <c>Ctrl</c> / <c>Alt</c> / <c>Shift</c> / <c>Win</c>，
    /// 主键认 WPF <see cref="Key"/> 枚举名、<c>+</c> <c>-</c> <c>[</c> <c>]</c> 这类符号，以及 esc / enter / space 等别名。</summary>
    /// <param name="text">形如 <c>Ctrl+Z</c>、<c>Space</c>、<c>[</c>、<c>Shift+Left</c> 的串；空白视为未设键。</param>
    /// <returns>解析出的键位；串里没有主键、出现两个主键、或键名认不出时返回 null。</returns>
    public static EditorChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = new List<string>(text.Split('+'));
        // 加号本身当主键时，按 '+' 拆分会留下尾随空段："+" → ["", ""]，"Ctrl++" → ["Ctrl", "", ""]
        if (parts.Count >= 2 && parts[^1].Length == 0 && parts[^2].Length == 0)
        {
            parts.RemoveAt(parts.Count - 1);
            parts[^1] = "+";
        }
        ModifierKeys mods = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in parts)
        {
            var tok = raw.Trim();
            if (tok.Length == 0) continue;
            switch (tok.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; continue;
                case "alt": mods |= ModifierKeys.Alt; continue;
                case "shift": mods |= ModifierKeys.Shift; continue;
                case "win" or "windows": mods |= ModifierKeys.Windows; continue;
            }
            if (key != null) return null;   // 两个主键
            var k = Lookup(tok);
            if (k == null) return null;
            key = k;
        }
        return key == null ? null : new EditorChord(mods, Canonical(key.Value));
    }

    /// <summary>渲染成键位串，与 <see cref="Parse"/> 接受的写法同源：存进设置的串读回来必须一模一样。</summary>
    /// <returns>形如 <c>Ctrl+Z</c>、<c>Shift+Left</c>、<c>+</c>。</returns>
    public override string ToString()
    {
        var list = new List<string>();
        if (Mods.HasFlag(ModifierKeys.Control)) list.Add("Ctrl");
        if (Mods.HasFlag(ModifierKeys.Alt)) list.Add("Alt");
        if (Mods.HasFlag(ModifierKeys.Shift)) list.Add("Shift");
        if (Mods.HasFlag(ModifierKeys.Windows)) list.Add("Win");
        list.Add(Name(Key));
        return string.Join("+", list);
    }

    /// <summary>拆成键帽序列：修饰键按 Ctrl → Alt → Shift → Win 排，最后是主键显示名。
    /// 键帽渲染要拿这个而不是切 <see cref="ToString"/>：主键是加号时，串里的加号分不清哪个是分隔符。</summary>
    /// <returns>按显示顺序排好的键名。</returns>
    public IReadOnlyList<string> Tokens()
    {
        var list = new List<string>();
        if (Mods.HasFlag(ModifierKeys.Control)) list.Add("Ctrl");
        if (Mods.HasFlag(ModifierKeys.Alt)) list.Add("Alt");
        if (Mods.HasFlag(ModifierKeys.Shift)) list.Add("Shift");
        if (Mods.HasFlag(ModifierKeys.Windows)) list.Add("Win");
        list.Add(Display(Key));
        return list;
    }

    /// <summary>把一次按键读成键位：录入框用得上，调用方不必自己处理修饰键掩码与归一。</summary>
    /// <param name="mods">事件里的修饰键状态，多余的位被忽略。</param>
    /// <param name="key">事件里的主键；<c>Key.System</c> 由调用方先还原成 <c>SystemKey</c>。</param>
    /// <returns>归一后的键位。</returns>
    public static EditorChord FromKey(ModifierKeys mods, Key key) =>
        new(mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows), Canonical(key));

    /// <summary>这次按键是否命中本键位。</summary>
    /// <param name="mods">事件里的修饰键状态，多余的位会被忽略。</param>
    /// <param name="key">事件里的主键；<c>Key.System</c> 由调用方先还原成 <c>SystemKey</c>。</param>
    /// <returns>修饰键逐位相同且主键归一后相同则为真。</returns>
    public bool Matches(ModifierKeys mods, Key key) =>
        Mods == (mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows))
        && Key == Canonical(key);

    /// <summary>把同一个物理键的多种报法归一：小键盘数字并入主键盘数字，<c>OemPlus</c> 与 <c>Add</c> 同键。
    /// 键盘布局给出哪个枚举名取决于布局与是否开小键盘，不归一就会出现「同一个键两种键位串」。</summary>
    /// <param name="k">事件报出的主键。</param>
    /// <returns>归一后的主键；不在归一表里的原样返回。</returns>
    static Key Canonical(Key k) => k switch
    {
        Key.OemPlus => Key.Add,
        Key.OemMinus => Key.Subtract,
        >= Key.NumPad0 and <= Key.NumPad9 => Key.D0 + (k - Key.NumPad0),
        Key.Multiply => Key.D0,     // 小键盘 * 与 0 同为「复位」语义：播放倍速归位
        _ => k,
    };

    /// <summary>键位的显示字形：方向键用箭头，加减用符号，长键名收成键帽上放得下的缩写。</summary>
    /// <param name="k">归一后的主键。</param>
    /// <returns>键帽里显示的一段文本。</returns>
    public static string Display(Key k) => k switch
    {
        Key.Add => "+",
        Key.Subtract => "-",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.Escape => "Esc",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.PageUp => "PgUp",
        Key.Next => "PgDn",
        Key.Insert => "Ins",
        Key.Delete => "Del",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Space => "Space",
        >= Key.D0 and <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
        _ => k.ToString(),
    };

    /// <summary>键位的存储名，用于键位串与设置里的值。与显示字形只差方向键：
    /// 箭头画在键帽上好看，存进设置再读回来要认的是键名。</summary>
    /// <param name="k">归一后的主键。</param>
    /// <returns><see cref="Parse"/> 认得出的键名。</returns>
    static string Name(Key k) => k switch
    {
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Up => "Up",
        Key.Down => "Down",
        _ => Display(k),
    };

    /// <summary>键名 → 主键枚举。</summary>
    /// <param name="tok">已去空白的单个键名。</param>
    /// <returns>认得出的主键；认不出返回 null。</returns>
    static Key? Lookup(string tok)
    {
        switch (tok.ToLowerInvariant())
        {
            case "+": return Key.Add;
            case "-": return Key.Subtract;
            case "*": return Key.D0;
            case "[": return Key.OemOpenBrackets;
            case "]": return Key.OemCloseBrackets;
            case "esc" or "escape": return Key.Escape;
            case "enter" or "return": return Key.Return;
            case "space": return Key.Space;
            case "del" or "delete": return Key.Delete;
            case "back" or "backspace": return Key.Back;
            case "pgup" or "pageup" or "prior": return Key.PageUp;
            case "pgdn" or "pagedown" or "next": return Key.Next;
            case "ins" or "insert": return Key.Insert;
            case "up": return Key.Up;
            case "down": return Key.Down;
            case "left": return Key.Left;
            case "right": return Key.Right;
            case "home": return Key.Home;
            case "end": return Key.End;
            case "tab": return Key.Tab;
        }
        if (tok.Length == 1 && char.IsBetween(tok[0], '0', '9'))
            return Key.D0 + (tok[0] - '0');
        if (Enum.TryParse<Key>(tok, ignoreCase: true, out var k) && k != Key.None)
            return Canonical(k);
        return null;
    }
}
