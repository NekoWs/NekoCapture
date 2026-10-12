using System;
using System.Collections.Generic;

namespace GestureCore;

/// <summary>修饰键标志（自持枚举，不依赖 WPF/WinForms 的 ModifierKeys，保证库可跨 UI 框架复用）。</summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>触发方式。KeyDown/KeyUp 指组合键自身按下/抬起；Click 系列指组合键处于激活态时的鼠标动作。</summary>
public enum TriggerKind
{
    /// <summary>组合键（含主键）按下、组合键变为激活态的瞬间触发。</summary>
    KeyDown,
    /// <summary>组合键抬起、组合键失活瞬间触发（与 KeyDown 配对可实现「按住做 A、松手做 B」）。</summary>
    KeyUp,
    /// <summary>组合键激活时单击一次。</summary>
    Click,
    /// <summary>组合键激活时双击（两次单击间隔 ≤ DoubleClickMs）。</summary>
    DoubleClick,
    /// <summary>组合键激活时三击（三次单击，相邻间隔 ≤ DoubleClickMs）。</summary>
    TripleClick,
    /// <summary>组合键激活时鼠标按下并保持 ≥ LongPressMs。</summary>
    LongPress,
    /// <summary>整个组合键（修饰键 + 主键）同时按下即触发（经典热键语义，等价于「组合键激活」）。</summary>
    Chord,
}

/// <summary>动作序列中某步失败（invoker 返回 false / 抛异常）后的处理策略。<b>逐条绑定显式指定</b>，
/// 引擎不内置任何宿主语义（用户明确要求：复用库里失败策略是每条绑定的数据，而非硬编码）。</summary>
public enum FallbackPolicy
{
    /// <summary>中止本条绑定剩余动作，不再尝试其它绑定。</summary>
    Stop,
    /// <summary>跳过失败的这一步，继续本条绑定的后续动作。</summary>
    SkipStep,
    /// <summary>中止本条绑定，顺延到「同一触发」的下一条绑定（按优先级）再试——「失败自动下一个」。</summary>
    NextBinding,
}

/// <summary>一个键组合：修饰键集合 + 可选主键。<see cref="Key"/> 是不透明整型键标识（宿主自定义，
/// FrameCapture 用 Win32 虚拟键码；别的宿主可用任意编码）。Key==0 表示纯修饰键组合（如仅 Ctrl+Alt）。</summary>
public sealed class KeyChord : IEquatable<KeyChord>
{
    public ModifierKeys Mods { get; set; }
    public int Key { get; set; }

    public KeyChord() { }
    public KeyChord(ModifierKeys mods, int key) { Mods = mods; Key = key; }

    /// <summary>组合键是否「无主键、仅修饰键」（如 Ctrl+Alt 按住待命）。</summary>
    public bool IsModifierOnly => Key == 0;

    public bool Equals(KeyChord? other) =>
        other != null && Mods == other.Mods && Key == other.Key;
    public override bool Equals(object? obj) => Equals(obj as KeyChord);
    public override int GetHashCode() => unchecked((int)Mods * 397 ^ Key);

    /// <summary>可读串（修饰键部分）。主键名由宿主经 <see cref="IKeyNamer"/> 提供，见 <see cref="Format"/>。</summary>
    public override string ToString() => Format(null);

    public string Format(IKeyNamer? namer)
    {
        string mods = ModifierKeysString.FormatMods(Mods);
        string? keyName = Key != 0 ? (namer != null ? namer.Name(Key) : "#" + Key) : null;
        if (string.IsNullOrEmpty(keyName)) return mods;
        return mods.Length == 0 ? keyName! : mods + "+" + keyName;
    }
}

/// <summary>把不透明键标识映射为可读名（宿主实现；GestureCore 不内置任何键名表以保持中立）。</summary>
public interface IKeyNamer
{
    string Name(int key);
}

/// <summary>一条手势绑定：组合键 + 触发方式 + 动作序列 + 失败策略 + 优先级。</summary>
public sealed class GestureBinding
{
    public string Id { get; set; } = "";
    public KeyChord Chord { get; set; } = new KeyChord();
    public TriggerKind Trigger { get; set; } = TriggerKind.KeyDown;

    /// <summary>双击/三击判定窗口（毫秒）——可逐条配置（用户要求 DoubleClick 间隔可自定义）。</summary>
    public int DoubleClickMs { get; set; } = 400;
    /// <summary>长按判定阈值（毫秒）。</summary>
    public int LongPressMs { get; set; } = 500;

    /// <summary>动作名序列（宿主经 <see cref="IActionInvoker"/> 按名执行）。</summary>
    public List<string> Actions { get; set; } = new List<string>();

    /// <summary>完成后序列（A3）：主 <see cref="Actions"/> 全部成功后再按序执行（如"截图后→贴图/复制"）。
    /// 尽力而为：其失败不改变本条结果。宿主若把动作投递到串行队列，则天然排在主动作之后执行。</summary>
    public List<string> OnComplete { get; set; } = new List<string>();

    /// <summary>本条绑定内某步失败时的处理策略（显式数据，非引擎硬编码）。</summary>
    public FallbackPolicy Fallback { get; set; } = FallbackPolicy.Stop;

    /// <summary>同一触发命中多条绑定时的先后（小者先；相等按加入顺序）。「同键多绑定按优先级顺延」。</summary>
    public int Priority { get; set; }

    public GestureBinding Clone() => new GestureBinding
    {
        Id = Id,
        Chord = new KeyChord(Chord.Mods, Chord.Key),
        Trigger = Trigger,
        DoubleClickMs = DoubleClickMs,
        LongPressMs = LongPressMs,
        Actions = new List<string>(Actions),
        OnComplete = new List<string>(OnComplete),
        Fallback = Fallback,
        Priority = Priority,
    };
}

/// <summary>动作执行回调（宿主实现）。返回 true=成功；false=失败（触发该绑定的 <see cref="GestureBinding.Fallback"/>）。
/// 抛异常等同失败。</summary>
public interface IActionInvoker
{
    bool Invoke(string actionName);
}

/// <summary>内部工具：把修饰键枚举格式化为 "Ctrl+Alt" 形式（供 KeyChord.Format）。</summary>
static class ModifierKeysString
{
    public static string FormatMods(ModifierKeys m)
    {
        var parts = new List<string>();
        if ((m & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((m & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((m & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((m & ModifierKeys.Win) != 0) parts.Add("Win");
        return string.Join("+", parts);
    }
}
