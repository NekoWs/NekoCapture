using GC = GestureCore;
using K = FrameCapture.Core.K;

namespace FrameCapture.Core;

/// <summary>
/// 一条手势绑定的可序列化形态。JSON 友好：组合键用 "Ctrl+Shift+G" 串（复用 <see cref="HotkeySpec"/> 解析，
/// 含键名映射，不重造键表）、触发/失败策略用字符串枚举名。与 <see cref="GC.GestureBinding"/> 互转。
/// 序列化由宿主负责（GestureCore 刻意不引 System.Text.Json 以保持中立、可跨宿主复用）。
/// </summary>
public sealed class GestureBindingDto
{
    /// <summary>绑定的稳定标识。</summary>
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
    /// <summary>快捷键进入的共享行为入口；空值用于导入动作链。</summary>
    public string EntryId { get; set; } = "";
    /// <summary>入口选区的初始模式：空值取节点设置，still 为截图，record 为录制。</summary>
    public string EntryMode { get; set; } = "";
    /// <summary>导入用的有序动作；入口标识非空时执行行为图。</summary>
    public string[] Actions { get; set; } = Array.Empty<string>();
    /// <summary>导入时接入主链末尾的完成序列；规范配置保存时省略。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? OnComplete { get; set; }
    /// <summary>动作链导入的失败与取消序列。</summary>
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
            Actions = EntryId.Length > 0 ? new List<string> { GestureActions.CallBehavior }
                : (Actions ?? Array.Empty<string>()).Concat(OnComplete ?? Array.Empty<string>())
                .Where(GestureActions.IsValid).Select(GestureActions.Canonical).ToList(),
            OnFail = (OnFail ?? Array.Empty<string>()).Where(GestureActions.IsValid).ToList(),
            Fallback = fb,
            Priority = Priority,
            DoubleClickMs = Math.Clamp(DoubleClickMs, 50, 2000),
            LongPressMs = Math.Clamp(LongPressMs, 100, 5000),
        };
    }

    /// <summary>由引擎绑定回推 DTO（供导出/展示）。组合键串用修饰键名 + 主键枚举名重建。</summary>
    /// <param name="b">引擎侧绑定；<c>Chord.Key</c> 按 <c>System.Windows.Forms.Keys</c> 枚举名渲染。</param>
    /// <returns>包含引擎字段的 DTO；名称与行为入口由宿主配置保留。时长使用引擎原值。</returns>
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
            Id = b.Id, Chord = chord, Trigger = b.Trigger.ToString(), Actions = b.Actions.Concat(b.OnComplete).Select(GestureActions.Canonical).ToArray(), OnFail = b.OnFail.ToArray(),
            Fallback = b.Fallback.ToString(), Priority = b.Priority,
            DoubleClickMs = b.DoubleClickMs, LongPressMs = b.LongPressMs,
        };
    }
}

/// <summary>快捷键的绑定、迁移与出厂配置。</summary>
public sealed partial class GestureConfig
{
    /// <summary>当前动作链存储版本。</summary>
    public const int CurrentChainRevision = 3;

    /// <summary>动作链存储版本。</summary>
    public int ChainRevision { get; set; }

    /// <summary>规范动作名并导入行为图；重复调用保持相同结果。</summary>
    /// <returns>是否需要写回配置。</returns>
    public bool Migrate()
    {
        Bindings ??= new();
        bool changed = ChainRevision < CurrentChainRevision;
        foreach (var b in Bindings)
        {
            b.EntryId ??= ""; b.EntryMode ??= "";
            if (Enum.TryParse<GC.TriggerKind>(b.Trigger, true, out var trigger) && b.Trigger != trigger.ToString())
            { b.Trigger = trigger.ToString(); changed = true; }
            string mode = b.EntryMode.ToLowerInvariant();
            if (mode is not (BehaviorPorts.Still or BehaviorPorts.Record)) mode = "";
            if (b.EntryMode != mode) { b.EntryMode = mode; changed = true; }
            b.OnFail = (b.OnFail ?? Array.Empty<string>()).Select(GestureActions.Canonical).ToArray();
            var actions = (b.Actions ?? Array.Empty<string>()).Concat(b.OnComplete ?? Array.Empty<string>())
                .Select(GestureActions.Canonical).ToArray();
            if (ChainRevision < 2)
            {
                var expanded = new List<string>();
                bool region = false;
                foreach (var name in actions)
                {
                    if (name == GestureActions.RecordStart && !region)
                    {
                        expanded.Add(GestureActions.StoredScope);
                        region = true;
                    }
                    expanded.Add(name);
                    region |= name is GestureActions.Select or GestureActions.StoredScope;
                }
                actions = expanded.ToArray();
            }
            if (ChainRevision < 3 && b.Id == "default.record" && actions.SequenceEqual(new[]
                { GestureActions.Select, GestureActions.RecordToggle, GestureActions.EditLastVideo }))
                actions[2] = GestureActions.EditResult;
            changed |= b.OnComplete != null || !actions.SequenceEqual(b.Actions ?? Array.Empty<string>());
            b.Actions = actions;
            b.OnComplete = null;
        }
        ChainRevision = Math.Max(ChainRevision, CurrentChainRevision);
        return EnsureGraph() || changed;
    }

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
            if (!d.Enabled || !HasRunnableEntry(d)) continue;
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
            OnComplete = onDone, OnFail = onFail ?? Array.Empty<string>(),
            Fallback = fb, Priority = prio, DoubleClickMs = dbl, LongPressMs = lp,
        };

    /// <summary>默认截图键：与 QQ 截图一致，按一下进选区、选完即截图。播种与全新配置的唯一事实源，
    /// 设置页与帮助窗都经它取值。</summary>
    public const string DefaultCaptureKey = "Ctrl+Alt+A";

    /// <summary>默认录制键：与 QQ 一致，按一下起录、再按一下停。</summary>
    public const string DefaultRecordKey = "Ctrl+Alt+R";

    /// <summary>出厂截图链为选区与静图；录制链为选区、录制切换与编辑。默认名称按显示语言推导。</summary>
    /// <param name="captureKey">截图键串；非法则跳过。</param>
    /// <param name="recordKey">录制键串；非法则跳过。</param>
    /// <returns>包含有效键位的出厂绑定。</returns>
    public static List<GestureBindingDto> DefaultQqSet(string captureKey, string recordKey)
    {
        var list = new List<GestureBindingDto>();
        if (HotkeySpec.Parse(captureKey) != null)
            list.Add(B("default.capture", captureKey, "Chord",
                new[] { GestureActions.Select, GestureActions.GrabStill }));
        if (HotkeySpec.Parse(recordKey) != null)
            list.Add(B("default.record", recordKey, "Chord",
                new[] { GestureActions.Select, GestureActions.RecordToggle, GestureActions.EditResult }));
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

    /// <summary>为仅含单个入口动作的出厂绑定补齐动作链；自定义 Id 与完整动作链保持原值。</summary>
    public void UpgradeStockDefaults()
    {
        if (ChainRevision >= 2) return;
        foreach (var b in Bindings)
        {
            if (b.Id == "default.capture" && b.Actions.Length == 1 && GestureActions.Canonical(b.Actions[0]) == GestureActions.Select)
                b.Actions = new[] { GestureActions.Select, GestureActions.GrabStill };
            else if (b.Id == "default.record" && b.Actions.Length == 1 && b.Actions[0] == GestureActions.RecordToggle)
            {
                b.Actions = new[] { GestureActions.Select, GestureActions.RecordToggle };
                if (b.OnComplete == null || b.OnComplete.Length == 0) b.OnComplete = new[] { GestureActions.EditLastVideo };
            }
        }
        Migrate();
    }

    /// <summary>哪些启用中的绑定在「组合键 + 触发方式」上撞了车：同一个键位触发上挂着多条，
    /// 引擎按优先级与顺序只走第一条，排在后面的永远不触发。非法或空组合键压根不进引擎，不参与判定。</summary>
    /// <returns>撞车绑定的 Id 集合——只收排在后面那几条，第一条照常触发、不标；没有撞车时为空集。</returns>
    public HashSet<string> ConflictIds()
    {
        var seen = new HashSet<string>();
        var clash = new HashSet<string>();
        foreach (var b in Bindings)
        {
            if (!b.Enabled || !HasRunnableEntry(b)) continue;
            var spec = HotkeySpec.Parse(b.Chord);
            if (spec == null) continue;
            // 组合键按解析结果比，不按字符串比：Ctrl+Alt+A 与 ALT+CTRL+A 是同一个键。
            string key = $"{spec.Ctrl}|{spec.Alt}|{spec.Shift}|{spec.Win}|{spec.VirtualKey}|{b.Trigger}";
            if (!seen.Add(key)) clash.Add(b.Id);
        }
        return clash;
    }

    /// <summary>「截图」键位文案（就绪卡用）：对当前范围截图与整帧即时截图都算截图。
    /// 只列组合键、不带触发后缀——这一行回答的是「按什么键」，把「轻按 / 按住松开」一起塞进去，
    /// 读者会以为还要多按几下。</summary>
    /// <returns>键位文案；一条都没绑时为「未设键」。</returns>
    public string CaptureKeyText()
        => DescribeChordsFor(GestureActions.GrabStill, GestureActions.CaptureStored, GestureActions.CaptureFrame);

    /// <summary>「录制」键位文案：按一下切换、按下起录松开停录，两种写法都算录制键；同样只列组合键。</summary>
    /// <returns>键位文案；一条都没绑时为「未设键」。</returns>
    public string RecordKeyText()
        => DescribeChordsFor(GestureActions.RecordToggle, GestureActions.RecordStart, GestureActions.RecordStop);

    /// <summary>触发方式 → 后缀文案（供帮助/展示反映真实手感）。Chord 及未知返回 <see cref="Str.Empty"/>——组合键本身即触发，无需后缀。</summary>
    /// <param name="trigger"><c>GestureCore.TriggerKind</c> 的名字；未知名字与 Chord 一样返回 <see cref="Str.Empty"/>。</param>
    /// <returns>可直接拼在组合键后面的后缀（如「按下」「松手」）；无后缀时为 <see cref="Str.Empty"/>。</returns>
    public static Str TriggerLabel(string trigger) => trigger switch
    {
        "KeyDown" => K.Triggers.TriggerKeydown,
        "KeyUp" => K.Triggers.TriggerKeyup,
        "Tap" => K.Triggers.TriggerTap,
        "Hold" => K.Triggers.TriggerHold,
        "HoldRelease" => K.Triggers.TriggerHoldrelease,
        "LongPress" => K.Triggers.TriggerLongpress,
        "DoubleClick" => K.Triggers.TriggerDoubleclick,
        "TripleClick" => K.Triggers.TriggerTripleclick,
        "Click" => K.Triggers.TriggerClick,
        _ => Str.Empty,
    };

    /// <summary>同 <see cref="DescribeKeysFor"/>，但只给组合键、不带触发后缀，按绑定顺序去重。
    /// 用在「按什么键」这种一句话场合（就绪卡）；带后缀的完整写法留给帮助窗那种键位表。</summary>
    /// <param name="actionNames">要查的动作名（见 <see cref="GestureActions"/>）；任一动作命中即计入。</param>
    /// <returns>组合键串，「，」分隔；没有任何启用绑定时为「未设键」。</returns>
    public string DescribeChordsFor(params string[] actionNames)
    {
        var chords = new List<string>();
        foreach (var b in Bindings)
        {
            if (!b.Enabled || !HasRunnableEntry(b) || !(b.EntryId.Length > 0 ? FlowActions(b.EntryId, b.EntryMode) : b.Actions).Any(a => actionNames.Contains(a))) continue;
            if (HotkeySpec.Parse(b.Chord) == null) continue;
            if (!chords.Contains(b.Chord)) chords.Add(b.Chord);
        }
        return chords.Count == 0 ? Loc.T(K.Common.NoKey) : string.Join(Loc.T(K.Hotkeys.Comma), chords);
    }

    /// <summary>把「绑定了指定动作的启用绑定」渲染成人类可读键位：按组合键分组、合并触发后缀（如 <c>F2 按下/松开</c>），
    /// 多条以顿号分隔（如 <c>Ctrl+Alt+R，F2 按下/松开</c>）。供帮助窗反映用户真实键位，
    /// 因此无匹配时返回「未设键」而不是回落某个默认键——默认键可能压根没绑动作。</summary>
    /// <param name="actionNames">要查的动作名（见 <see cref="GestureActions"/>）；任一动作命中即计入。</param>
    /// <returns>渲染好的键位串；没有任何启用绑定时为「未设键」。</returns>
    public string DescribeKeysFor(params string[] actionNames)
    {
        var byChord = new Dictionary<string, List<string>>();
        foreach (var b in Bindings)
        {
            if (!b.Enabled || !HasRunnableEntry(b) || !(b.EntryId.Length > 0 ? FlowActions(b.EntryId, b.EntryMode) : b.Actions).Any(a => actionNames.Contains(a))) continue;
            if (HotkeySpec.Parse(b.Chord) == null) continue;
            if (!byChord.TryGetValue(b.Chord, out var list)) byChord[b.Chord] = list = new List<string>();
            var suf = Loc.T(TriggerLabel(b.Trigger));
            if (suf.Length > 0 && !list.Contains(suf)) list.Add(suf);
        }
        if (byChord.Count == 0) return Loc.T(K.Common.NoKey);
        return string.Join(Loc.T(K.Hotkeys.Comma), byChord.Select(kv => kv.Key + (kv.Value.Count == 0 ? "" : " " + string.Join("/", kv.Value))));
    }
}
