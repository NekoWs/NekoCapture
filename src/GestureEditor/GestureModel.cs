using System.Collections.Generic;
using System.Windows.Media;
using GestureCore;

namespace GestureEditor;

/// <summary>一个可编辑动作的展示信息（宿主经 <see cref="IActionCatalog"/> 提供）。
/// <see cref="Name"/> 是持久化/引擎用的稳定标识；<see cref="Label"/>/<see cref="Description"/> 给人看。
/// <see cref="IsCapture"/>：该动作会产生一张静图截图——编辑器据此决定「截图后」下拉是否出现，
/// 库不硬编码宿主动作名。</summary>
public sealed record ActionInfo(string Name, string Label, string Description, bool IsCapture = false);

/// <summary>宿主提供动作清单：编辑器据此渲染「选动作」列表并校验动作名合法性。
/// 编辑器不硬编码任何宿主动作——这是配置器可跨软件复用的关键接缝。</summary>
public interface IActionCatalog
{
    IReadOnlyList<ActionInfo> Actions();
    bool IsValid(string name);
}

/// <summary>一条可编辑绑定（宿主无关）。组合键 = <see cref="Mods"/> + <see cref="Key"/>
/// （Key 是宿主键码，FrameCapture 用 Win32 虚拟键码；0 = 纯修饰键组合）。字段与
/// <see cref="GestureCore.GestureBinding"/> 一一对应，但用可变属性便于 UI 双向编辑。</summary>
public sealed class BindingEdit
{
    public string Id { get; set; } = "";
    /// <summary>启用/禁用（禁用=保留配置但不参与触发，不必删除）。语义由宿主解释，编辑器只呈现开关。</summary>
    public bool Enabled { get; set; } = true;
    public ModifierKeys Mods { get; set; } = ModifierKeys.None;
    public int Key { get; set; }
    public TriggerKind Trigger { get; set; } = TriggerKind.Chord;
    public List<string> Actions { get; set; } = new();
    /// <summary>完成后序列（A3）：主 <see cref="Actions"/> 成功后再跑（如"截图后→贴图"）。可空。</summary>
    public List<string> OnComplete { get; set; } = new();
    public FallbackPolicy Fallback { get; set; } = FallbackPolicy.Stop;
    /// <summary>截图后行为：本条手势触发截图完成后的动作；空=跟随默认（弹提示）。仅截图类手势生效。</summary>
    public string AfterCapture { get; set; } = "";
    public int Priority { get; set; }
    public int DoubleClickMs { get; set; } = 400;
    public int LongPressMs { get; set; } = 500;

    public KeyChord Chord => new KeyChord(Mods, Key);

    public BindingEdit Clone() => new BindingEdit
    {
        Id = Id, Enabled = Enabled, Mods = Mods, Key = Key, Trigger = Trigger,
        Actions = new List<string>(Actions), OnComplete = new List<string>(OnComplete), Fallback = Fallback, AfterCapture = AfterCapture, Priority = Priority,
        DoubleClickMs = DoubleClickMs, LongPressMs = LongPressMs,
    };
}

/// <summary>整份可编辑手势配置（宿主无关）。宿主负责与自身持久化格式互转（见 <see cref="IGestureConfigStore"/>）。</summary>
public sealed class GestureDoc
{
    public bool Enabled { get; set; }
    /// <summary>独占吞键（命中带具体键的组合键时吞掉，前台收不到）。语义由宿主解释，编辑器只呈现开关。</summary>
    public bool Exclusive { get; set; } = true;
    public List<BindingEdit> Bindings { get; set; } = new();
}

/// <summary>宿主提供配置读写：<see cref="Load"/> 取当前配置，<see cref="Save"/> 落盘（宿主自行触发热重载）。
/// 编辑器只编辑这份 <see cref="GestureDoc"/>，不关心它存成 JSON 还是别的。</summary>
public interface IGestureConfigStore
{
    GestureDoc Load();
    void Save(GestureDoc doc);
}

/// <summary>编辑器配色：宿主从自身主题注入，保证与宿主视觉一致；编辑器不引用宿主主题库（保持可复用）。</summary>
public sealed class EditorPalette
{
    public Color WindowBg = Color.FromArgb(0xF2, 0x20, 0x20, 0x20);
    public Color PanelBg = Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B);
    public Color CardBg = Color.FromRgb(0x2D, 0x2D, 0x2D);
    public Color Text = Color.FromRgb(0xFF, 0xFF, 0xFF);
    public Color TextDim = Color.FromArgb(0xB3, 0xC0, 0xC0, 0xC0);
    public Color Stroke = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);
    public Color Accent = Color.FromRgb(0x4C, 0xC2, 0xFF);
    public Color Hover = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
    public Color Press = Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF);
    public Color TrackBg = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
    public bool IsDark = true;

    public SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>压在 Accent 底上的可读前景：WCAG 相对亮度（sRGB 线性化）&lt;0.35→白字，否则黑字。
    /// 与宿主 Fluent.OnAccentColor 同款判定（旧加权亮度把 #7F75FF 误判黑字），库内自包含不引用宿主 Fluent。</summary>
    public Color OnAccent()
    {
        static double L(double c) { c /= 255.0; return c <= 0.03928 ? c / 12.92 : System.Math.Pow((c + 0.055) / 1.055, 2.4); }
        double lum = 0.2126 * L(Accent.R) + 0.7152 * L(Accent.G) + 0.0722 * L(Accent.B);
        return lum < 0.35 ? Colors.White : Colors.Black;
    }
}
