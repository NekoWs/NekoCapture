using System;
using System.Collections.Generic;
using System.Linq;
using GC = GestureCore;

namespace FrameCapture.Core;

/// <summary>
/// 一条手势绑定的可序列化形态（R120 B2）。JSON 友好：组合键用 "Ctrl+Shift+G" 串（复用 <see cref="HotkeySpec"/> 解析，
/// 含键名映射，不重造键表）、触发/失败策略用字符串枚举名。与 <see cref="GC.GestureBinding"/> 互转。
/// 序列化由宿主负责（GestureCore 刻意不引 System.Text.Json 以保持中立、可跨宿主复用）。
/// </summary>
public sealed class GestureBindingDto
{
    public string Id { get; set; } = "";
    /// <summary>组合键串，如 "F6" / "Ctrl+Shift+G" / "Ctrl+Alt+A"（须含主键，纯修饰键组合暂不支持）。</summary>
    public string Chord { get; set; } = "";
    /// <summary>GestureCore.TriggerKind 名：KeyDown/KeyUp/Click/DoubleClick/TripleClick/LongPress/Chord。</summary>
    public string Trigger { get; set; } = "Chord";
    /// <summary>动作名序列（见 <see cref="GestureActions"/>）。</summary>
    public string[] Actions { get; set; } = Array.Empty<string>();
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
            Fallback = b.Fallback.ToString(), Priority = b.Priority,
            DoubleClickMs = b.DoubleClickMs, LongPressMs = b.LongPressMs,
        };
    }
}

/// <summary>手势配置（存于 settings.json 的 gestures 段）。默认关闭——附加式，零回归；开启后手势层与旧热键并存。</summary>
public sealed class GestureConfig
{
    /// <summary>手势层总开关。默认 false：旧热键路径原样工作，手势层不介入（避免动核心捕获路径的回归风险）。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>独占吞键（R120 B2）：手势命中「带具体键的组合键」（如 Ctrl+Alt+A）时，把该键的按下与抬起一起吞掉，
    /// 前台程序收不到——QQ 式全局独占，避免快捷键漏进输入框打出字符或触发别的软件。默认 true。
    /// 纯修饰键组合（无具体键，如 Ctrl+Alt 本身）永不吞，防止 Ctrl+C 等被劫持。关掉=手势层只观察、键照常放行。</summary>
    public bool Exclusive { get; set; } = true;

    public List<GestureBindingDto> Bindings { get; set; } = new();

    /// <summary>转引擎绑定表（跳过非法项）。</summary>
    public List<GC.GestureBinding> ToBindings()
    {
        var list = new List<GC.GestureBinding>();
        foreach (var d in Bindings)
        {
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
}
