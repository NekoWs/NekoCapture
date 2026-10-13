using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>完成后序列（A3）：主动作全部成功后再跑（如"截图后→贴图"）。可空。</summary>
    public string[] OnComplete { get; set; } = Array.Empty<string>();
    /// <summary>截图后行为：本条手势触发截图完成后的动作——toast/editor/copy/pin；空=跟随默认（弹提示）。仅截图类手势生效。</summary>
    public string AfterCapture { get; set; } = "";
    /// <summary>GestureCore.FallbackPolicy 名：Stop/SkipStep/NextBinding（逐条显式，非引擎硬编码）。</summary>
    public string Fallback { get; set; } = "Stop";
    public int Priority { get; set; }
    public int DoubleClickMs { get; set; } = 400;
    public int LongPressMs { get; set; } = 500;

    /// <summary>转引擎绑定；组合键串非法或触发名未知则返回 null（加载时跳过、不整体失败）。</summary>
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

/// <summary>手势配置（存于 settings.json 的 gestures 段）。B2 起为默认触发路径：
/// 首次加载自动播种「捕获键 轻点截图 + 按住录制」默认集（<see cref="SeedIfNeeded"/>），旧热键退役。</summary>
public sealed class GestureConfig
{
    /// <summary>手势层总开关。起默认 true（手势替代旧热键）。关掉则退回纯观察（不吞键、不触发）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>独占吞键：手势命中「带具体键的组合键」（如 Ctrl+Alt+A）时，把该键的按下与抬起一起吞掉，
    /// 前台程序收不到——QQ 式全局独占，避免快捷键漏进输入框打出字符或触发别的软件。默认 true。
    /// 纯修饰键组合（无具体键，如 Ctrl+Alt 本身）永不吞，防止 Ctrl+C 等被劫持。关掉=手势层只观察、键照常放行。</summary>
    public bool Exclusive { get; set; } = true;

    public List<GestureBindingDto> Bindings { get; set; } = new();

    /// <summary>默认集是否已播种过（一次性迁移）。首启/升级时若为空则播种并置真；
    /// 之后用户清空绑定不会被重新塞回（尊重「我就是不要任何绑定」）。</summary>
    public bool Seeded { get; set; } = false;

    /// <summary>转引擎绑定表（跳过非法项）。</summary>
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

    /// <summary>默认绑定：把旧捕获/选区热键平移成 Chord 触发（捕获键=捕获单帧、选区键=选取范围）。
    /// 用于用户开启手势层时的起点；非法热键串自动跳过。</summary>
    public static List<GestureBindingDto> DefaultBindings(string captureHotkey, string scopeHotkey)
    {
        var list = new List<GestureBindingDto>();
        if (HotkeySpec.Parse(captureHotkey) != null)
            list.Add(B("default.capture", captureHotkey, "Chord", new[] { GestureActions.CaptureFrame }));
        if (HotkeySpec.Parse(scopeHotkey) != null)
            list.Add(B("default.scope", scopeHotkey, "Chord", new[] { GestureActions.ScopeSelect }));
        return list;
    }

    /// <summary>「QQ 手感」预设：热键即选区、选完自动截图（配合 AutoCaptureOnSelect）；选区键同义。
    /// 一键切换，用户可在 JSON/编辑器里再改。</summary>
    public static List<GestureBindingDto> PresetQQ(string captureHotkey, string scopeHotkey)
    {
        var list = new List<GestureBindingDto>();
        if (HotkeySpec.Parse(captureHotkey) != null)
            list.Add(B("qq.capture", captureHotkey, "Chord", new[] { GestureActions.ScopeSelect }));
        if (HotkeySpec.Parse(scopeHotkey) != null)
            list.Add(B("qq.scope", scopeHotkey, "Chord", new[] { GestureActions.ScopeSelect }));
        return list;
    }

    /// <summary>「按住即录」预设：捕获键按下开始录、抬起结束（KeyDown/KeyUp 配对）；选区键=选取范围。</summary>
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

    /// <summary>默认捕获键/选区键（旧热键退役后的单一事实源；迁移播种与全新配置都用它）。</summary>
    public const string DefaultCaptureKey = "Ctrl+Shift+G";
    public const string DefaultScopeKey = "Ctrl+Shift+R";

    /// <summary>默认「轻点截图 + 按住录制」集（B2，完整旧手感）：
    /// 捕获键 轻点→选区截图(QQ 式)、按住→起录、松手→停止；选区键 组合键→选区。
    /// 按住阈值 250ms（复用 LongPressMs 字段）。</summary>
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

    /// <summary>一次性播种：未播种且当前无任何绑定 → 灌入默认 tap/hold 集并启用；随后置 Seeded。
    /// 已有绑定的用户（含自定义）不受影响，只补置 Seeded。</summary>
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
    public string ScopeKeyText()
    {
        foreach (var b in Bindings)
            if (b.Enabled && b.Actions.Contains(GestureActions.ScopeSelect) && HotkeySpec.Parse(b.Chord) != null)
                return b.Chord;
        return DefaultScopeKey;
    }

    /// <summary>触发方式 → 中文后缀（供帮助/展示反映真实手感）。Chord 及未知返回空串——组合键本身即触发，无需后缀。</summary>
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
    /// 多条以顿号分隔（如 <c>Ctrl+Alt+R，F2 按下/松开</c>）；无匹配返回「未绑定」。
    /// 供帮助窗反映用户真实键位——自定义手势下不再显示写死的默认键（旧 <c>ScopeKeyText</c> 回落默认键会显示一个没绑定的死键）。</summary>
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
