using GC = GestureCore;

namespace FrameCapture.Core;

/// <summary>
/// 一条手势绑定的可序列化形态。JSON 友好：组合键用 "Ctrl+Shift+G" 串（复用 <see cref="HotkeySpec"/> 解析，
/// 含键名映射，不重造键表）、触发/失败策略用字符串枚举名。与 <see cref="GC.GestureBinding"/> 互转。
/// 序列化由宿主负责（GestureCore 刻意不引 System.Text.Json 以保持中立、可跨宿主复用）。
/// </summary>
public sealed class GestureBindingDto
{
    public string Id { get; set; } = "";
    /// <summary>这条快捷键的名字，列表折叠时就是它。用户自己写，存的就是他写的原文——
    /// 界面语言表不认识它，切语言也不会改它。空名在界面上按序列推导显示。</summary>
    public string Name { get; set; } = "";
    /// <summary>启用/禁用：禁用=保留配置但不进引擎、不触发，无需删除。默认启用。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>组合键串，如 "F6" / "Ctrl+Shift+G" / "Ctrl+Alt+A"（须含主键，纯修饰键组合暂不支持）。</summary>
    public string Chord { get; set; } = "";
    /// <summary>GestureCore.TriggerKind 名：KeyDown/KeyUp/Click/DoubleClick/TripleClick/LongPress/Chord。</summary>
    public string Trigger { get; set; } = "Chord";
    /// <summary>触发时执行的动作名序列（见 <see cref="GestureActions"/>）。首步是「选区」时，其余动作等选区落地后才执行。</summary>
    public string[] Actions { get; set; } = Array.Empty<string>();
    /// <summary>完成后序列：主动作全部成功后再跑（如"停止录制后→打开编辑器"）。
    /// 空=不覆盖，由宿主按默认落点收尾（截图默认弹提示浮窗）。</summary>
    public string[] OnComplete { get; set; } = Array.Empty<string>();
    /// <summary>失败时序列：主动作有步失败或选区被取消时再跑。空=不覆盖，失败静默。</summary>
    public string[] OnFail { get; set; } = Array.Empty<string>();
    /// <summary>GestureCore.FallbackPolicy 名：Stop/SkipStep/NextBinding（逐条显式，非引擎硬编码）。</summary>
    public string Fallback { get; set; } = "Stop";
    /// <summary>同一触发命中多条绑定时的先后：数值小的先试。</summary>
    public int Priority { get; set; }
    /// <summary>连击判定窗口（ms）：两次按下间隔小于它才算同一串点击。</summary>
    public int DoubleClickMs { get; set; } = 400;
    /// <summary>长按阈值（ms）。Trigger 为 Tap/Hold/HoldRelease 时本字段被复用为「按住多久算 Hold」的阈值，
    /// 同一条码上这三类触发必须取同值，否则点按与按住的判定会分叉。</summary>
    public int LongPressMs { get; set; } = 500;

    /// <summary>转引擎绑定；组合键串非法或触发名未知则返回 null（加载时跳过、不整体失败）。</summary>
    /// <returns>引擎绑定；<see cref="Chord"/> 解析失败或 <see cref="Trigger"/> 不是已知触发名时为 null。
    /// 连击/长按阈值出参已钳到 50–2000 / 100–5000，<see cref="Fallback"/> 名未知时按 Stop 处理。</returns>
    public GC.GestureBinding? ToBinding()
    {
        var spec = HotkeySpec.Parse(Chord);
        if (spec == null) return null;
        if (!Enum.TryParse<GC.TriggerKind>(Trigger, true, out var trig)) return null;
        if (!Enum.TryParse<GC.FallbackPolicy>(Fallback, true, out var fb)) fb = GC.FallbackPolicy.Stop;
        var mods = (spec.Ctrl ? GC.ModifierKeys.Control : GC.ModifierKeys.None)
                 | (spec.Alt ? GC.ModifierKeys.Alt : GC.ModifierKeys.None)
                 | (spec.Shift ? GC.ModifierKeys.Shift : GC.ModifierKeys.None)
                 | (spec.Win ? GC.ModifierKeys.Win : GC.ModifierKeys.None);
        return new GC.GestureBinding
        {
            Id = Id,
            Chord = new GC.KeyChord(mods, (int)spec.VirtualKey),
            Trigger = trig,
            Actions = Actions.Where(GestureActions.IsValid).ToList(),
            OnComplete = (OnComplete ?? Array.Empty<string>()).Where(GestureActions.IsValid).ToList(),
            OnFail = (OnFail ?? Array.Empty<string>()).Where(GestureActions.IsValid).ToList(),
            Fallback = fb,
            Priority = Priority,
            DoubleClickMs = Math.Clamp(DoubleClickMs, 50, 2000),
            LongPressMs = Math.Clamp(LongPressMs, 100, 5000),
        };
    }

    /// <summary>由引擎绑定回推 DTO（供导出/展示）。组合键串用修饰键名 + 主键枚举名重建。</summary>
    /// <param name="b">引擎侧绑定；<c>Chord.Key</c> 按 <c>System.Windows.Forms.Keys</c> 枚举名渲染。</param>
    /// <returns>等价的 DTO。不做时长钳制：引擎侧数值原样带出。</returns>
    public static GestureBindingDto FromBinding(GC.GestureBinding b)
    {
        var mods = new List<string>();
        if ((b.Chord.Mods & GC.ModifierKeys.Control) != 0) mods.Add("Ctrl");
        if ((b.Chord.Mods & GC.ModifierKeys.Alt) != 0) mods.Add("Alt");
        if ((b.Chord.Mods & GC.ModifierKeys.Shift) != 0) mods.Add("Shift");
        if ((b.Chord.Mods & GC.ModifierKeys.Win) != 0) mods.Add("Win");
        var keyName = ((System.Windows.Forms.Keys)b.Chord.Key).ToString();
        var chord = mods.Count == 0 ? keyName : string.Join("+", mods) + "+" + keyName;
        return new GestureBindingDto
        {
            Id = b.Id, Chord = chord, Trigger = b.Trigger.ToString(), Actions = b.Actions.ToArray(),
            OnComplete = b.OnComplete.ToArray(), OnFail = b.OnFail.ToArray(),
            Fallback = b.Fallback.ToString(), Priority = b.Priority,
            DoubleClickMs = b.DoubleClickMs, LongPressMs = b.LongPressMs,
        };
    }
}

/// <summary>快捷键配置（存于 settings.json 的 gestures 段），功能触发的唯一入口。
/// 首次 Normalize 时播种默认两条：截图键 = 选区→截图，录制键 = 选区→开始/停止录制 + 完成后打开编辑器
/// （<see cref="SeedIfNeeded"/>）。</summary>
public sealed class GestureConfig
{
    /// <summary>「启用快捷键」总开关，默认 true。关掉后不向引擎喂键、不装鼠标钩子——快捷键完全不参与，
    /// 键照常放行给前台。开着时命中带具体键的组合键即吞键，前台收不到那一键；
    /// 纯修饰键组合（无具体键，如 Ctrl+Alt 本身）永不吞，防止 Ctrl+C 等被劫持。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>绑定列表，顺序即引擎匹配顺序。</summary>
    public List<GestureBindingDto> Bindings { get; set; } = new();

    /// <summary>默认集是否已播种过。置真后即使 <see cref="Bindings"/> 为空也不会再播种——
    /// 尊重「我就是不要任何绑定」，清空绑定不等于恢复出厂。</summary>
    public bool Seeded { get; set; } = false;

    /// <summary>转引擎绑定表（跳过非法项）。</summary>
    /// <returns>仅含 <see cref="GestureBindingDto.Enabled"/> 为真且动作序列非空的绑定；顺序即 <see cref="Bindings"/> 顺序。</returns>
    public List<GC.GestureBinding> ToBindings()
    {
        var list = new List<GC.GestureBinding>();
        foreach (var d in Bindings)
        {
            if (!d.Enabled) continue;   // 禁用的绑定不进引擎（保留配置、不触发）
            var b = d.ToBinding();
            if (b != null && b.Actions.Count > 0) list.Add(b);
        }
        return list;
    }

    static GestureBindingDto B(string id, string chord, string trig, string[] acts,
        string[]? onDone = null, string[]? onFail = null,
        string fb = "Stop", int prio = 0, int dbl = 400, int lp = 500)
        => new GestureBindingDto
        {
            Id = id, Chord = chord, Trigger = trig, Actions = acts,
            OnComplete = onDone ?? Array.Empty<string>(), OnFail = onFail ?? Array.Empty<string>(),
            Fallback = fb, Priority = prio, DoubleClickMs = dbl, LongPressMs = lp,
        };

    /// <summary>默认截图键：与 QQ 截图一致，按一下进选区、选完即截图。播种与全新配置的唯一事实源，
    /// 设置页与帮助窗都经它取值。</summary>
    public const string DefaultCaptureKey = "Ctrl+Alt+A";

    /// <summary>默认录制键：与 QQ 一致，按一下起录、再按一下停。</summary>
    public const string DefaultRecordKey = "Ctrl+Alt+R";

    /// <summary>默认键位集，也是出厂形态的唯一事实源：
    /// 截图键 = 选区 → 截图，完成后不覆盖（默认弹提示浮窗）；
    /// 录制键 = 选区 → 开始/停止录制，完成后覆盖成打开编辑器。
    /// 两条都以「选区」开头，后续动作等选区落地才执行；录制中再按一次时范围已定，选区这一步直接通过。
    /// 两条都是 Chord——组合键本身即触发，没有轻点与按住的歧义。
    /// 名字留空：列表折叠时按序列推导显示，界面语言切换时跟着换，用户改名即冻结成他自己的原文。</summary>
    /// <param name="captureKey">截图键串；非法则跳过该条。</param>
    /// <param name="recordKey">录制键串；非法则跳过该条。</param>
    /// <returns>0～2 条绑定，Id 为 <c>default.capture</c> / <c>default.record</c>。</returns>
    public static List<GestureBindingDto> DefaultQqSet(string captureKey, string recordKey)
    {
        var list = new List<GestureBindingDto>();
        if (HotkeySpec.Parse(captureKey) != null)
            list.Add(B("default.capture", captureKey, "Chord",
                new[] { GestureActions.ScopeSelect, GestureActions.CaptureStored }));
        if (HotkeySpec.Parse(recordKey) != null)
            list.Add(B("default.record", recordKey, "Chord",
                new[] { GestureActions.ScopeSelect, GestureActions.RecordToggle },
                onDone: new[] { GestureActions.EditLastVideo }));
        return list;
    }

    /// <summary>一次性播种：未播种且当前无任何绑定 → 灌入 <see cref="DefaultQqSet"/> 并启用；
    /// 随后置 <see cref="Seeded"/>。已有绑定的用户（含自定义）不受影响，只补置 Seeded。</summary>
    /// <param name="captureKey">播种用的截图键串，通常传 <see cref="DefaultCaptureKey"/>。</param>
    /// <param name="recordKey">播种用的录制键串，通常传 <see cref="DefaultRecordKey"/>。</param>
    public void SeedIfNeeded(string captureKey, string recordKey)
    {
        if (Seeded) return;
        if (Bindings.Count == 0)
        {
            Bindings = DefaultQqSet(captureKey, recordKey);
            Enabled = true;
        }
        Seeded = true;
    }

    /// <summary>「截图」键位文案（欢迎卡/启动气泡用）：对当前范围截图与整帧即时截图都算截图，
    /// 按绑定顺序取第一个启用的。</summary>
    /// <returns>键位文案；一条都没绑时为「未绑定」。</returns>
    public string CaptureKeyText()
        => DescribeKeysFor(GestureActions.CaptureStored, GestureActions.CaptureFrame);

    /// <summary>「录制」键位文案：按一下切换、按下起录松开停录，两种写法都算录制键。</summary>
    /// <returns>键位文案；一条都没绑时为「未绑定」。</returns>
    public string RecordKeyText()
        => DescribeKeysFor(GestureActions.RecordToggle, GestureActions.RecordStart, GestureActions.RecordStop);

    /// <summary>触发方式 → 中文后缀（供帮助/展示反映真实手感）。Chord 及未知返回空串——组合键本身即触发，无需后缀。</summary>
    /// <param name="trigger"><c>GestureCore.TriggerKind</c> 的名字；未知名字与 Chord 一样返回空串。</param>
    /// <returns>可直接拼在组合键后面的后缀（如「按下」「松手」），或空串。</returns>
    public static string TriggerLabel(string trigger) => trigger switch
    {
        "KeyDown" => "按下", // lockeys
        "KeyUp" => "松开", // lockeys
        "Tap" => "轻点", // lockeys
        "Hold" => "按住", // lockeys
        "HoldRelease" => "松手", // lockeys
        "LongPress" => "长按", // lockeys
        "DoubleClick" => "双击", // lockeys
        "TripleClick" => "三击", // lockeys
        "Click" => "单击", // lockeys
        _ => "",
    };

    /// <summary>把「绑定了指定动作的启用绑定」渲染成人类可读键位：按组合键分组、合并触发后缀（如 <c>F2 按下/松开</c>），
    /// 多条以顿号分隔（如 <c>Ctrl+Alt+R，F2 按下/松开</c>）。供帮助窗反映用户真实键位，
    /// 因此无匹配时返回「未绑定」而不是回落某个默认键——默认键可能压根没绑动作。</summary>
    /// <param name="actionNames">要查的动作名（见 <see cref="GestureActions"/>）；任一动作命中即计入。</param>
    /// <returns>渲染好的键位串；没有任何启用绑定时为「未绑定」。</returns>
    public string DescribeKeysFor(params string[] actionNames)
    {
        var byChord = new Dictionary<string, List<string>>();
        foreach (var b in Bindings)
        {
            if (!b.Enabled || !b.Actions.Any(a => actionNames.Contains(a))) continue;
            if (HotkeySpec.Parse(b.Chord) == null) continue;
            if (!byChord.TryGetValue(b.Chord, out var list)) byChord[b.Chord] = list = new List<string>();
            var suf = Loc.T(TriggerLabel(b.Trigger));
            if (suf.Length > 0 && !list.Contains(suf)) list.Add(suf);
        }
        if (byChord.Count == 0) return Loc.T("未绑定");
        return string.Join(Loc.T("，"), byChord.Select(kv => kv.Key + (kv.Value.Count == 0 ? "" : " " + string.Join("/", kv.Value))));
    }
}
