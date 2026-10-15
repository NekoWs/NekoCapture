
namespace FrameCapture.Core;

/// <summary>
/// 手势动作词汇表：把「功能」抽象成稳定的动作名，供 GestureCore 绑定序列引用。
/// 名与 UI 文案分离——名是持久化/引擎用的稳定标识（改名会破坏已存 settings.json，故用英文小写点分），
/// 文案是给人看的。新增功能 = 在此加一条 + 在 GestureInvoker 里接上实现。
/// </summary>
public readonly struct GestureAction
{
    /// <summary>稳定动作名，持久化与引擎只认这个。</summary>
    /// <value>英文小写点分串（如 <c>capture.frame</c>）；改名等于让已存绑定失效。</value>
    public string Name { get; }

    /// <summary>给人看的短名，按当前界面语言取词。</summary>
    /// <value>编辑器、帮助与设置窗展示用，不参与持久化；没有译名时就是登记的中文原文。</value>
    public string Label => Loc.T(_label);

    /// <summary>这个动作做什么的一句话，按当前界面语言取词。</summary>
    /// <value>帮助窗与手势配置器里的详细说明；没有译名时就是登记的中文原文。</value>
    public string Description => Loc.T(_desc);

    /// <summary>会产生静图截图：手势配置器据此决定「截图后」下拉是否显示（序列里没有截图类动作就不显示）。</summary>
    /// <value>true = 该动作产出一张静图。</value>
    public bool IsCapture { get; }

    /// <summary>装配一个动作条目，各字段原样保存。</summary>
    /// <param name="name">稳定动作名（英文小写点分）。</param>
    /// <param name="label">中文短名，也是取词的键。</param>
    /// <param name="desc">详细说明，也是取词的键。</param>
    /// <param name="isCapture">是否产出静图，默认 false（非截图类）。</param>
    public GestureAction(string name, string label, string desc, bool isCapture = false) { Name = name; _label = label; _desc = desc; IsCapture = isCapture; }

    readonly string _label;
    readonly string _desc;
}

/// <summary>
/// 动作名常量与全量目录：<see cref="All"/> 是唯一清单，设置窗、手势配置器、帮助窗都从这里取名字与文案。
/// </summary>
public static class GestureActions
{
    // 稳定动作名常量（持久化标识，勿随意改名）
    public const string CaptureFrame = "capture.frame";
    public const string ScopeSelect = "scope.select";
    public const string CaptureSelection = "capture.selection";
    public const string RecordSelection = "record.selection";
    public const string RecordToggle = "record.toggle";
    public const string RecordStart = "record.start";
    public const string RecordStop = "record.stop";
    public const string ScrollCapture = "scroll.capture";
    public const string DelayedCapture = "capture.delayed";
    public const string EditLastStill = "edit.last";
    public const string EditLastVideo = "edit.lastvideo";
    public const string OcrSelection = "ocr.selection";
    public const string PinSelection = "pin.selection";
    public const string SettingsOpen = "settings.open";
    public const string HelpOpen = "help.open";
    public const string GestureEdit = "gesture.edit";

    /// <summary>全部可用动作（顺序即建议展示顺序）。GestureInvoker 必须覆盖每一个，否则该动作视为未实现（执行失败）。</summary>
    public static readonly GestureAction[] All =
    {
        new(CaptureFrame, "全屏即时截图", "按下立即抓光标所在屏幕的当下这一帧，不走选区、不带延时、不抢焦点，适合游戏", true), // lockeys
        new(ScopeSelect, "选区", "拖框选一块区域", true), // lockeys
        new(CaptureSelection, "选区截图", "先选一块区域，选完立即截静态图", true), // lockeys
        new(RecordStart, "开始录制", "对当前范围开始录制，直到收到停止"), // lockeys
        new(RecordStop, "停止录制", "停止录制，产物留在内存不进编辑器；要进编辑器请在「完成后序列」加『打开编辑器』"), // lockeys
        new(RecordToggle, "开始/停止录制", "空闲→选区起录；录制中→停止。不进编辑器，要进编辑器请在完成后序列加『打开编辑器』"), // lockeys
        new(RecordSelection, "选区录制", "先选一块区域，选完立即起录该区域，停止另配一条"), // lockeys
        new(ScrollCapture, "滚动截图", "对当前范围做滚动长截图", true), // lockeys
        new(DelayedCapture, "延时截图", "倒数数秒后截当前范围，用来抓一点就消失的菜单和悬停", true), // lockeys
        new(EditLastStill, "编辑上次截图", "打开标注编辑器编辑上一次静图"), // lockeys
        new(EditLastVideo, "打开编辑器", "打开视频编辑器编辑上一次录制，常配在『完成后序列』，让停止录制后再进编辑器"), // lockeys
        new(OcrSelection, "识别文字", "对当前范围做 OCR：开「识别后显示结果窗」则弹可编辑结果窗，否则直接复制文本", true), // lockeys
        new(PinSelection, "贴图当前选区", "把当前范围钉成贴图"), // lockeys
        new(SettingsOpen, "打开设置", "打开设置窗口"), // lockeys
        new(HelpOpen, "打开帮助", "打开使用说明窗口"), // lockeys
        new(GestureEdit, "手势配置器", "打开可视化键位配置器，增删改绑定、实时校验、保存即生效"), // lockeys
    };

    static readonly HashSet<string> Names =
        new(Enumerable.Select(All, a => a.Name));

    /// <summary>名字是否在本词汇表里。</summary>
    /// <param name="name">待校验的动作名，区分大小写。</param>
    /// <returns>null 或表外名为 false；设置文件里的未知动作名据此判为未实现，而不是崩在分发处。</returns>
    public static bool IsValid(string? name) => name != null && Names.Contains(name);

    /// <summary>动作名 → 中文短名（供设置窗「当前生效键位」等处展示）。</summary>
    /// <param name="name">动作名；null 或空串按空名处理。</param>
    /// <returns>表内条目的 Label；未知名原样返回（不崩，便于前向兼容），空名返回空串。</returns>
    public static string LabelOf(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        foreach (var a in All) if (a.Name == name) return a.Label;
        return name!;
    }
}
