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
    /// <summary>组合键（含主键）按下后<b>快速抬起</b>（按住时长 &lt; 阈值）触发——「轻点」。
    /// 与 <see cref="Hold"/>/<see cref="HoldRelease"/> 配合实现「同一键：轻点做 A、按住做 B」。
    /// 阈值取该组合键 Tap/Hold/HoldRelease 绑定的 <see cref="GestureBinding.LongPressMs"/>。</summary>
    Tap,
    /// <summary>组合键按下并<b>保持 ≥ 阈值</b>时触发（仍在按住的那一刻，需宿主周期调用 <c>Tick</c>）。
    /// 用于「确认按住后才开始」（如按住才起录，避免轻点误触发）；与 <see cref="Tap"/> 互斥于同一次按下。</summary>
    Hold,
    /// <summary>组合键抬起、且本次按住时长 ≥ 阈值时触发（与 <see cref="Hold"/> 配对：按住做 A、松手做 B）。
    /// 若绑了 <see cref="TriggerKind.KeyDown"/>（按下即开始，零丢帧），本触发负责「松手结束」。</summary>
    HoldRelease,
}

/// <summary>动作序列中某步失败（invoker 返回 false / 抛异常）后的处理策略。<b>逐条绑定显式指定</b>，
/// 失败策略由每条绑定提供。</summary>
public enum FallbackPolicy
{
    /// <summary>中止本条绑定剩余动作，也不顺延到其它绑定。</summary>
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

    /// <summary>无参构造：默认无修饰键、无主键（<see cref="IsModifierOnly"/> 为 true），供对象初始化器与序列化框架使用。</summary>
    public KeyChord() { }

    /// <summary>按修饰键与主键构造组合键。</summary>
    /// <param name="mods">修饰键标志的按位或；<see cref="ModifierKeys.None"/> 表示裸键。</param>
    /// <param name="key">不透明整型主键标识，0 = 纯修饰键组合。</param>
    public KeyChord(ModifierKeys mods, int key) { Mods = mods; Key = key; }

    /// <summary>组合键是否「无主键、仅修饰键」（如 Ctrl+Alt 按住待命）。</summary>
    public bool IsModifierOnly => Key == 0;

    /// <summary>按值比较：修饰键与主键都相同即同一组合键，与实例身份无关。</summary>
    /// <param name="other">待比较的组合键；null 视为不相等。</param>
    /// <returns>修饰键与主键均相同时为 true。</returns>
    public bool Equals(KeyChord? other) =>
        other != null && Mods == other.Mods && Key == other.Key;

    /// <summary>装箱形式的值比较，等价于 <c>Equals(KeyChord)</c> 重载；非 <see cref="KeyChord"/> 一律 false。</summary>
    /// <param name="obj">待比较对象，可为 null。</param>
    /// <returns>同为组合键且修饰键、主键都相同时为 true。</returns>
    public override bool Equals(object? obj) => Equals(obj as KeyChord);

    /// <summary>与 <c>Equals(KeyChord)</c> 一致的散列：值相等的组合键散列必相等。</summary>
    /// <returns>由 <see cref="Mods"/> 与 <see cref="Key"/> 混合出的散列码；溢出按 unchecked 环绕。</returns>
    public override int GetHashCode() => unchecked((int)Mods * 397 ^ Key);

    /// <summary>可读串（修饰键部分）。主键名由宿主经 <see cref="IKeyNamer"/> 提供，见 <see cref="Format"/>。</summary>
    /// <returns>如 "Ctrl+Alt"；无修饰键时为空串。带主键时以 "#键码" 占位——这里没有命名器。</returns>
    public override string ToString() => Format(null);

    /// <summary>格式化为可读串，修饰键在前、主键在后，以 "+" 连接。</summary>
    /// <param name="namer">主键标识 → 可读名的宿主实现；传 null 时主键以 "#键码" 占位。</param>
    /// <returns>如 "Ctrl+Shift+F12"；纯修饰键组合只返回修饰键部分；命名器返回空串时同样只剩修饰键部分。</returns>
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
    /// <summary>键标识 → 显示名，仅用于界面回显，不参与任何匹配判定。</summary>
    /// <param name="key">与 <see cref="KeyChord.Key"/> 同一编码体系的键标识，调用方保证非 0。</param>
    /// <returns>可读键名（如 "F12"）；返回空串时 <see cref="KeyChord.Format"/> 退化为只输出修饰键部分。</returns>
    string Name(int key);
}

/// <summary>一条手势绑定：组合键 + 触发方式 + 动作序列 + 失败策略 + 优先级。</summary>
public sealed class GestureBinding
{
    /// <summary>绑定标识，配置内唯一；引擎不解释其含义，只随整条绑定透传给 <see cref="IActionInvoker.BeginBinding"/>。</summary>
    public string Id { get; set; } = "";
    /// <summary>触发本条的组合键；引擎按键值相等匹配，替换实例即换键。</summary>
    public KeyChord Chord { get; set; } = new KeyChord();
    /// <summary>触发方式；同一组合键可同时存在多条不同触发的绑定，互不干扰。</summary>
    public TriggerKind Trigger { get; set; } = TriggerKind.KeyDown;

    /// <summary>双击/三击判定窗口（毫秒），逐条可配。同一组合键多条绑定时取优先级最高那条的值，
    /// 且优先取绑了 DoubleClick/TripleClick 的那条；都取不到时按 400。</summary>
    public int DoubleClickMs { get; set; } = 400;
    /// <summary>长按判定阈值（毫秒），兼作键盘轻点/按住（Tap/Hold/HoldRelease）的时长阈值。
    /// 同一组合键多条绑定时取优先级最高且触发匹配的那条；取不到时长按按 500、键盘按住按 250。</summary>
    public int LongPressMs { get; set; } = 500;

    /// <summary>动作名序列（宿主经 <see cref="IActionInvoker"/> 按名执行）。</summary>
    public List<string> Actions { get; set; } = new List<string>();

    /// <summary>完成后序列：主 <see cref="Actions"/> 全部成功后再按序执行（如"截图后→贴图/复制"）。
    /// 尽力而为：其失败不改变本条结果。宿主若把动作投递到串行队列，则天然排在主动作之后执行。
    /// 空列表＝不覆盖，由宿主按自己的默认落点收尾（截图默认弹提示浮窗）。</summary>
    public List<string> OnComplete { get; set; } = new List<string>();

    /// <summary>失败时序列：主 <see cref="Actions"/> 有步失败且策略不是 <see cref="FallbackPolicy.SkipStep"/>
    /// 时按序执行（如"提示一下为什么没成"）。空列表＝不覆盖，失败静默。
    /// 前置任务在宿主手里失败或取消时，由宿主负责跑这份列表。</summary>
    public List<string> OnFail { get; set; } = new List<string>();

    /// <summary>本条绑定内某步失败时的处理策略（显式数据，非引擎硬编码）。</summary>
    public FallbackPolicy Fallback { get; set; } = FallbackPolicy.Stop;

    /// <summary>同一触发命中多条绑定时的先后（小者先；相等按加入顺序）。「同键多绑定按优先级顺延」。</summary>
    public int Priority { get; set; }

    /// <summary>深拷贝：新建 <see cref="Chord"/> 与三份动作列表，改副本不影响原绑定。</summary>
    /// <returns>字段等值的新 <see cref="GestureBinding"/>；<see cref="GestureEngine.SetBindings"/> 入库时逐条走它。</returns>
    public GestureBinding Clone() => new GestureBinding
    {
        Id = Id,
        Chord = new KeyChord(Chord.Mods, Chord.Key),
        Trigger = Trigger,
        DoubleClickMs = DoubleClickMs,
        LongPressMs = LongPressMs,
        Actions = new List<string>(Actions),
        OnComplete = new List<string>(OnComplete),
        OnFail = new List<string>(OnFail),
        Fallback = Fallback,
        Priority = Priority,
    };
}

/// <summary>动作执行回调（宿主实现）。返回 true=成功；false=失败（触发该绑定的 <see cref="GestureBinding.Fallback"/>）。
/// 抛异常等同失败。</summary>
public interface IActionInvoker
{
    /// <summary>按名执行一个动作。宿主负责动作的真实实现与耗时，引擎只在触发线程上同步调用。</summary>
    /// <param name="actionName">动作稳定标识，取自 <see cref="GestureBinding.Actions"/>
    /// 或 <see cref="GestureBinding.OnComplete"/>。</param>
    /// <returns>true=成功；false=失败，按该条绑定的 <see cref="GestureBinding.Fallback"/> 处理。抛异常等同失败。</returns>
    bool Invoke(string actionName);

    /// <summary>绑定开始执行前通知宿主：宿主可读取 b 的元数据（完成/失败序列、优先级、时长阈值）
    /// 作为本次动作序列的上下文。先于本条的任何动作调用一次；实现应轻量、不抛。</summary>
    /// <param name="b">即将执行动作序列的绑定。引擎已克隆过入库副本，宿主不应在此改写它。</param>
    void BeginBinding(GestureBinding b);
}

/// <summary>
/// 支持<b>前置任务</b>的宿主实现 <see cref="IActionInvoker"/> 之外的这一段；不实现它的宿主保持逐步同步执行。
/// 前置任务＝它自己决定何时算完成（等用户拖完一个框、等一次选择落地），所以它之后的动作不能立刻执行。
/// 引擎只在「序列第一步」这一处认前置任务，序列中间出现同名动作按普通动作处理。
/// </summary>
public interface IPreludeInvoker : IActionInvoker
{
    /// <summary>该动作名是否是前置任务。引擎用它决定这条序列走同步执行还是整体交给宿主。</summary>
    /// <param name="actionName">序列第一步的动作名。</param>
    /// <returns>该动作是前置任务时为 true。</returns>
    bool IsPrelude(string actionName);

    /// <summary>执行带前置任务的整条序列：先跑 <paramref name="actions"/>[0] 这个前置任务，
    /// 它落地后再按序跑其余动作，跑完按宿主自己的默认落点或绑定携带的完成/失败序列收尾。
    /// 引擎调用前已调过 <see cref="IActionInvoker.BeginBinding"/>，绑定元数据由宿主自己留存。</summary>
    /// <param name="actions">完整序列，[0] 是 <see cref="IsPrelude"/> 认下的前置任务。</param>
    /// <returns>是否已受理。true＝后续成败归宿主，引擎按成功处理本条；false＝受理失败，走本条失败路径。</returns>
    bool InvokePrelude(IReadOnlyList<string> actions);
}

/// <summary>内部工具：把修饰键枚举格式化为 "Ctrl+Alt" 形式（供 KeyChord.Format）。</summary>
static class ModifierKeysString
{
    /// <summary>修饰键 → 可读串，顺序固定为 Ctrl、Alt、Shift、Win，与入参置位顺序无关。</summary>
    /// <param name="m">修饰键标志的按位或组合。</param>
    /// <returns>以 "+" 连接的修饰键名；<see cref="ModifierKeys.None"/> 返回空串。</returns>
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
