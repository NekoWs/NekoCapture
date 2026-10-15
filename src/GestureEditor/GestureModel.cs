using System.Windows.Media;
using GestureCore;

namespace GestureEditor;

/// <summary>一个可编辑动作的展示信息（宿主经 <see cref="IActionCatalog"/> 提供）。
/// <see cref="Name"/> 是持久化/引擎用的稳定标识；<see cref="Label"/>/<see cref="Description"/> 给人看。
/// <see cref="IsCapture"/>：该动作会产生一张静图截图——编辑器据此决定「截图后」下拉是否出现，
/// 库不硬编码宿主动作名。</summary>
/// <param name="Name">动作的稳定标识，写进配置并交给引擎；改名等于换动作，须与宿主目录一致。</param>
/// <param name="Label">界面显示名，允许随文案调整。</param>
/// <param name="Description">悬停说明，一句话讲清这个动作做什么。</param>
/// <param name="IsCapture">是否为截图类动作，决定「截图后」小节是否出现；默认 false。</param>
/// <returns>按给定值构造的不可变记录。</returns>
public sealed record ActionInfo(string Name, string Label, string Description, bool IsCapture = false);

/// <summary>宿主提供动作清单：编辑器据此渲染「选动作」列表并校验动作名合法性。
/// 编辑器不硬编码任何宿主动作——这是配置器可跨软件复用的关键接缝。</summary>
public interface IActionCatalog
{
    /// <summary>全部可选动作，顺序即下拉顺序；实现须能在 UI 线程上快速返回。</summary>
    /// <returns>动作展示信息列表，不为 null；无可用动作时返回空列表。</returns>
    IReadOnlyList<ActionInfo> Actions();

    /// <summary>动作名是否仍在目录内——校验未知动作（配置里残留的动作名）的唯一依据。</summary>
    /// <param name="name">配置中持久化的动作稳定标识，区分大小写。</param>
    /// <returns>目录中存在同名动作时为 true。</returns>
    bool IsValid(string name);
}

/// <summary>一条可编辑绑定（宿主无关）。组合键 = <see cref="Mods"/> + <see cref="Key"/>
/// （Key 是宿主键码，FrameCapture 用 Win32 虚拟键码；0 = 纯修饰键组合）。字段与
/// <see cref="GestureCore.GestureBinding"/> 一一对应，但用可变属性便于 UI 双向编辑。</summary>
public sealed class BindingEdit
{
    /// <summary>绑定标识，配置内唯一；校验问题按它归属到对应卡片。</summary>
    public string Id { get; set; } = "";
    /// <summary>启用/禁用（禁用=保留配置但不参与触发，不必删除）。语义由宿主解释，编辑器只呈现开关。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>修饰键集合，与 <see cref="Key"/> 一起构成组合键。</summary>
    public ModifierKeys Mods { get; set; } = ModifierKeys.None;
    /// <summary>主键的宿主键码（FrameCapture 为 Win32 虚拟键码）；0 = 纯修饰键组合。</summary>
    public int Key { get; set; }
    /// <summary>触发方式；同一组合键可各配不同触发共存，互不覆盖。</summary>
    public TriggerKind Trigger { get; set; } = TriggerKind.Chord;
    /// <summary>主动作序列，按序执行；空列表是错误，挡住保存。</summary>
    public List<string> Actions { get; set; } = new();
    /// <summary>完成后序列（A3）：主 <see cref="Actions"/> 成功后再跑（如"截图后→贴图"）。可空。</summary>
    public List<string> OnComplete { get; set; } = new();
    /// <summary>本条某步失败时的处理策略，逐条显式配置，引擎不内置宿主语义。</summary>
    public FallbackPolicy Fallback { get; set; } = FallbackPolicy.Stop;
    /// <summary>截图后行为：本条手势触发截图完成后的动作；空=跟随默认（弹提示）。仅截图类手势生效。</summary>
    public string AfterCapture { get; set; } = "";
    /// <summary>截图完成后是否写剪贴板。与 <see cref="AfterCapture"/> 独立：关掉后贴图、进编辑器都不覆盖剪贴板。</summary>
    public bool CopyToClipboard { get; set; } = true;
    /// <summary>同一「组合键+触发」命中多条时的先后（小者先，相等按加入顺序）。</summary>
    public int Priority { get; set; }
    /// <summary>双击/三击判定窗口（毫秒）。落在 50–2000 之外只提示，不挡保存。</summary>
    public int DoubleClickMs { get; set; } = 400;
    /// <summary>长按判定阈值（毫秒），兼作键盘轻点/按住的时长阈值。落在 100–5000 之外只提示。</summary>
    public int LongPressMs { get; set; } = 500;

    public KeyChord Chord => new KeyChord(Mods, Key);

    /// <summary>深拷贝：两个动作序列各自新建列表，改副本不影响原条目。</summary>
    /// <returns>字段等值的新 <see cref="BindingEdit"/>，含独立的 <see cref="Actions"/>/<see cref="OnComplete"/> 实例。</returns>
    public BindingEdit Clone() => new BindingEdit
    {
        Id = Id, Enabled = Enabled, Mods = Mods, Key = Key, Trigger = Trigger,
        Actions = new List<string>(Actions), OnComplete = new List<string>(OnComplete), Fallback = Fallback, AfterCapture = AfterCapture, Priority = Priority,
        CopyToClipboard = CopyToClipboard,
        DoubleClickMs = DoubleClickMs, LongPressMs = LongPressMs,
    };
}

/// <summary>整份可编辑手势配置（宿主无关）。宿主负责与自身持久化格式互转（见 <see cref="IGestureConfigStore"/>）。</summary>
public sealed class GestureDoc
{
    /// <summary>手势层总开关：关闭时宿主不响应任何绑定，配置本身保留。语义由宿主解释。</summary>
    public bool Enabled { get; set; }
    /// <summary>独占吞键（命中带具体键的组合键时吞掉，前台收不到）。语义由宿主解释，编辑器只呈现开关。</summary>
    public bool Exclusive { get; set; } = true;
    /// <summary>全部绑定，顺序即左侧列表顺序；引擎侧另按 <see cref="BindingEdit.Priority"/> 排序。</summary>
    public List<BindingEdit> Bindings { get; set; } = new();
}

/// <summary>宿主提供配置读写：<see cref="Load"/> 取当前配置，<see cref="Save"/> 落盘（宿主自行触发热重载）。
/// 编辑器只编辑这份 <see cref="GestureDoc"/>，不关心它存成 JSON 还是别的。</summary>
public interface IGestureConfigStore
{
    /// <summary>读取当前配置供编辑。构造窗口时调用一次，此后编辑器只改内存副本。</summary>
    /// <returns>可编辑的配置对象；无配置时返回空 <see cref="GestureDoc"/>，不为 null。</returns>
    GestureDoc Load();

    /// <summary>落盘整份配置。仅在 <see cref="GestureValidation.HasErrors"/> 为 false 时被调用。</summary>
    /// <param name="doc">已通过校验的整份配置；落盘格式与热重载时机由宿主决定。</param>
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
    /// <summary>明/暗标记，由宿主随主题注入。库内配色一律取上面的颜色字段，<b>不</b>据此推导——
    /// 只改这个布尔不会改变任何像素。</summary>
    public bool IsDark = true;

    /// <summary>把调色板颜色做成<b>冻结</b>画刷，供直接赋给 Background/Foreground 等属性。</summary>
    /// <param name="c">源颜色，含 alpha；半透明色照原样冻结。</param>
    /// <returns>已 Freeze 的共享画刷：不可再改色，也不能挂颜色动画；要过渡必须新建未冻结副本当动画宿主。</returns>
    public SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>压在 Accent 底上的可读前景：WCAG 相对亮度（sRGB 线性化）&lt;0.35→白字，否则黑字。
    /// 选中项、强调色按钮的字色都取自这里，<b>不得</b>写死黑字或白字。
    /// 与宿主 Fluent.OnAccentColor 同款判定，库内自包含不引用宿主 Fluent。</summary>
    /// <returns>Accent 的相对亮度低于 0.35 时返回纯白，否则纯黑；只依赖 <see cref="Accent"/>。</returns>
    public Color OnAccent()
    {
        static double L(double c) { c /= 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        double lum = 0.2126 * L(Accent.R) + 0.7152 * L(Accent.G) + 0.0722 * L(Accent.B);
        return lum < 0.35 ? Colors.White : Colors.Black;
    }
}
