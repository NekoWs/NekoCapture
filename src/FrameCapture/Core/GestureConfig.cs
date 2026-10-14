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
    /// <summary>启用/禁用：禁用=保留配置但不进引擎、不触发，无需删除。默认启用。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>组合键串，如 "F6" / "Ctrl+Shift+G" / "Ctrl+Alt+A"（须含主键，纯修饰键组合暂不支持）。</summary>
    public string Chord { get; set; } = "";
    /// <summary>GestureCore.TriggerKind 名：KeyDown/KeyUp/Click/DoubleClick/TripleClick/LongPress/Chord。</summary>
    public string Trigger { get; set; } = "Chord";
    /// <summary>动作名序列（见 <see cref="GestureActions"/>）。</summary>
    public string[] Actions { get; set; } = Array.Empty<string>();
    /// <summary>完成后序列：主动作全部成功后再跑（如"截图后→贴图"）。可空。</summary>
    public string[] OnComplete { get; set; } = Array.Empty<string>();
    /// <summary>截图后行为：本条手势触发截图完成后的动作——toast/editor/copy/pin；空=跟随默认（弹提示）。仅截图类手势生效。</summary>
    public string AfterCapture { get; set; } = "";
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
            AfterCapture = AfterCapture ?? "",
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
            OnComplete = b.OnComplete.ToArray(),
            AfterCapture = b.AfterCapture,
            Fallback = b.Fallback.ToString(), Priority = b.Priority,
            DoubleClickMs = b.DoubleClickMs, LongPressMs = b.LongPressMs,
        };
    }
}

/// <summary>手势配置（存于 settings.json 的 gestures 段），功能触发的唯一入口。
/// 空配置在首次 Normalize 时播种「捕获键 轻点截图 + 按住录制」默认集（<see cref="SeedIfNeeded"/>）。</summary>
public sealed class GestureConfig
{
    /// <summary>手势层总开关，默认 true。关掉后不向引擎喂键、不装鼠标钩子——手势层完全不参与，
    /// 键照常放行给前台。只想「看得到手势但不抢键」应关 <see cref="Exclusive"/> 而非本开关。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>独占吞键：手势命中「带具体键的组合键」（如 Ctrl+Alt+A）时，把该键的按下与抬起一起吞掉，
    /// 前台程序收不到——全局独占，避免快捷键漏进输入框打出字符或触发别的软件。默认 true。
    /// 纯修饰键组合（无具体键，如 Ctrl+Alt 本身）永不吞，防止 Ctrl+C 等被劫持。关掉=手势层只观察、键照常放行。</summary>
    public bool Exclusive { get; set; } = true;

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
        string fb = "Stop", int prio = 0, int dbl = 400, int lp = 500)
        => new GestureBindingDto { Id = id, Chord = chord, Trigger = trig, Actions = acts, Fallback = fb, Priority = prio, DoubleClickMs = dbl, LongPressMs = lp };

    /// <summary>默认绑定集：捕获键 → 捕获单帧、选区键 → 选取范围，两条都是 Chord 触发。</summary>
    /// <param name="captureHotkey">捕获键组合键串；非法（<see cref="HotkeySpec.Parse"/> 返回 null）则不生成该条。</param>
    /// <param name="scopeHotkey">选区键组合键串；同样非法即跳过。</param>
    /// <returns>0～2 条绑定，Id 为 <c>default.capture</c> / <c>default.scope</c>。</returns>
    public static List<GestureBindingDto> DefaultBindings(string captureHotkey, string scopeHotkey)
    {
        var list = new List<GestureBindingDto>();
        if (HotkeySpec.Parse(captureHotkey) != null)
            list.Add(B("default.capture", captureHotkey, "Chord", new[] { GestureActions.CaptureFrame }));
        if (HotkeySpec.Parse(scopeHotkey) != null)
            list.Add(B("default.scope", scopeHotkey, "Chord", new[] { GestureActions.ScopeSelect }));
        return list;
    }

    /// <summary>「选区优先」预设：两个键都绑成选区，配合 <c>AutoCaptureOnSelect</c> 即选完自动截图。
    /// 一键切换，用户可在 JSON/编辑器里再改。</summary>
    /// <param name="captureHotkey">捕获键串：这里也改绑选区，非法则跳过该条。</param>
    /// <param name="scopeHotkey">选区键串：与捕获键同义，非法则跳过该条。</param>
    /// <returns>0～2 条绑定，Id 为 <c>qq.capture</c> / <c>qq.scope</c>。</returns>
    public static List<GestureBindingDto> PresetScopeFirst(string captureHotkey, string scopeHotkey)
    {
        var list = new List<GestureBindingDto>();
        if (HotkeySpec.Parse(captureHotkey) != null)
            list.Add(B("qq.capture", captureHotkey, "Chord", new[] { GestureActions.ScopeSelect }));
        if (HotkeySpec.Parse(scopeHotkey) != null)
            list.Add(B("qq.scope", scopeHotkey, "Chord", new[] { GestureActions.ScopeSelect }));
        return list;
    }

    /// <summary>「按住即录」预设：捕获键按下起录、抬起停止（KeyDown/KeyUp 配对）；选区键=选取范围。</summary>
    /// <param name="captureHotkey">捕获键串：生成 <c>rec.start</c> 与 <c>rec.stop</c> 两条配对绑定，非法则两条都不生成。</param>
    /// <param name="scopeHotkey">选区键串：非法则跳过。</param>
    /// <returns>0～3 条绑定。起录与停止必须成对出现，否则录制无法自动收尾。</returns>
    public static List<GestureBindingDto> PresetHoldToRecord(string captureHotkey, string scopeHotkey)
    {
        var list = new List<GestureBindingDto>();
        if (HotkeySpec.Parse(captureHotkey) != null)
        {
            list.Add(B("rec.start", captureHotkey, "KeyDown", new[] { GestureActions.RecordStart }));
            list.Add(B("rec.stop", captureHotkey, "KeyUp", new[] { GestureActions.RecordStop }));
        }
        if (HotkeySpec.Parse(scopeHotkey) != null)
            list.Add(B("rec.scope", scopeHotkey, "Chord", new[] { GestureActions.ScopeSelect }));
        return list;
    }

    /// <summary>默认捕获键：播种与全新配置的唯一事实源，设置页与帮助窗都经它取值。</summary>
    public const string DefaultCaptureKey = "Ctrl+Shift+G";
    /// <summary>默认选区键：与 <see cref="DefaultCaptureKey"/> 同为唯一事实源。</summary>
    public const string DefaultScopeKey = "Ctrl+Shift+R";

    /// <summary>默认「轻点截图 + 按住录制」集：捕获键轻点→选区截图、按住→起录、松手→停止；
    /// 选区键组合键→选区。按住阈值 250ms 写在三条 tap/hold 绑定的 <c>LongPressMs</c> 上，三条必须同值。</summary>
    /// <param name="captureKey">捕获键串；非法则只生成选区那一条。</param>
    /// <param name="scopeKey">选区键串；非法则只生成捕获那三条。</param>
    /// <returns>0～4 条绑定，Id 为 <c>cap.tap</c> / <c>cap.hold</c> / <c>cap.holdrelease</c>
    /// 与 <c>scope.chord</c>。</returns>
    public static List<GestureBindingDto> DefaultTapHoldSet(string captureKey, string scopeKey)
    {
        var list = new List<GestureBindingDto>();
        if (HotkeySpec.Parse(captureKey) != null)
        {
            list.Add(B("cap.tap", captureKey, "Tap", new[] { GestureActions.CaptureSelection }, lp: 250));
            list.Add(B("cap.hold", captureKey, "Hold", new[] { GestureActions.RecordStart }, lp: 250));
            list.Add(B("cap.holdrelease", captureKey, "HoldRelease", new[] { GestureActions.RecordStop }, lp: 250));
        }
        if (HotkeySpec.Parse(scopeKey) != null)
            list.Add(B("scope.chord", scopeKey, "Chord", new[] { GestureActions.ScopeSelect }));
        return list;
    }

    /// <summary>一次性播种：未播种且当前无任何绑定 → 灌入默认 tap/hold 集并启用；随后置 <see cref="Seeded"/>。
    /// 已有绑定的用户（含自定义）不受影响，只补置 Seeded。</summary>
    /// <param name="captureKey">播种用的捕获键串，通常传 <see cref="DefaultCaptureKey"/>。</param>
    /// <param name="scopeKey">播种用的选区键串，通常传 <see cref="DefaultScopeKey"/>。</param>
    public void SeedIfNeeded(string captureKey, string scopeKey)
    {
        if (Seeded) return;
        if (Bindings.Count == 0)
        {
            Bindings = DefaultTapHoldSet(captureKey, scopeKey);
            Enabled = true;
        }
        Seeded = true;
    }

    /// <summary>主捕获键文本（供欢迎卡/帮助展示）：取首个启用的 tap/hold 绑定的组合键；
    /// 退化取首个含截图动作的启用绑定；都没有则默认键。</summary>
    /// <returns>组合键串，形如 <c>Ctrl+Shift+G</c>。永不返回空串，最差回落到 <see cref="DefaultCaptureKey"/>。</returns>
    public string CaptureKeyText()
    {
        foreach (var b in Bindings)
            if (b.Enabled && (b.Trigger is "Tap" or "Hold" or "HoldRelease") && HotkeySpec.Parse(b.Chord) != null)
                return b.Chord;
        foreach (var b in Bindings)
            if (b.Enabled && b.Actions.Contains(GestureActions.CaptureSelection) && HotkeySpec.Parse(b.Chord) != null)
                return b.Chord;
        return DefaultCaptureKey;
    }

    /// <summary>主选区键文本：取首个含选区动作的启用绑定；无则默认键。</summary>
    /// <returns>组合键串；无匹配时回落 <see cref="DefaultScopeKey"/>，该键此时可能并未绑定任何动作。</returns>
    public string ScopeKeyText()
    {
        foreach (var b in Bindings)
            if (b.Enabled && b.Actions.Contains(GestureActions.ScopeSelect) && HotkeySpec.Parse(b.Chord) != null)
                return b.Chord;
        return DefaultScopeKey;
    }

    /// <summary>触发方式 → 中文后缀（供帮助/展示反映真实手感）。Chord 及未知返回空串——组合键本身即触发，无需后缀。</summary>
    /// <param name="trigger"><c>GestureCore.TriggerKind</c> 的名字；未知名字与 Chord 一样返回空串。</param>
    /// <returns>可直接拼在组合键后面的后缀（如「按下」「松手」），或空串。</returns>
    public static string TriggerLabel(string trigger) => trigger switch
    {
        "KeyDown" => "按下",
        "KeyUp" => "松开",
        "Tap" => "轻点",
        "Hold" => "按住",
        "HoldRelease" => "松手",
        "LongPress" => "长按",
        "DoubleClick" => "双击",
        "TripleClick" => "三击",
        "Click" => "单击",
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
            var suf = TriggerLabel(b.Trigger);
            if (suf.Length > 0 && !list.Contains(suf)) list.Add(suf);
        }
        if (byChord.Count == 0) return "未绑定";
        return string.Join("，", byChord.Select(kv => kv.Key + (kv.Value.Count == 0 ? "" : " " + string.Join("/", kv.Value))));
    }
}
