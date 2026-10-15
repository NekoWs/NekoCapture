
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
    /// <value>设置与帮助里的详细说明；没有译名时就是登记的中文原文。</value>
    public string Description => Loc.T(_desc);

    /// <summary>装配一个动作条目，各字段原样保存。</summary>
    /// <param name="name">稳定动作名（英文小写点分）。</param>
    /// <param name="label">中文短名，也是取词的键。</param>
    /// <param name="desc">详细说明，也是取词的键。</param>
    public GestureAction(string name, string label, string desc) { Name = name; _label = label; _desc = desc; }

    readonly string _label;
    readonly string _desc;
}

/// <summary>
/// 动作名常量与全量目录：<see cref="All"/> 是唯一清单，设置窗与帮助窗都从这里取名字与文案。
/// </summary>
public static class GestureActions
{
    // 稳定动作名常量（持久化标识，勿随意改名）
    public const string CaptureFrame = "capture.frame";
    public const string ScopeSelect = "scope.select";
    public const string CaptureStored = "capture.stored";
    public const string RecordToggle = "record.toggle";
    public const string RecordStart = "record.start";
    public const string RecordStop = "record.stop";
    public const string ScrollCapture = "scroll.capture";
    public const string DelayedCapture = "capture.delayed";
    public const string EditLastStill = "edit.last";
    public const string EditLastVideo = "edit.lastvideo";
    public const string OcrSelection = "ocr.selection";
    public const string PinSelection = "pin.selection";
    public const string NotifyToast = "notify.toast";
    public const string CopyLast = "copy.clipboard";
    public const string SettingsOpen = "settings.open";
    public const string HelpOpen = "help.open";

    /// <summary>全部可用动作（顺序即建议展示顺序）。GestureInvoker 必须覆盖每一个，否则该动作视为未实现（执行失败）。</summary>
    public static readonly GestureAction[] All =
    {
        new(ScopeSelect, "选区", "拖框选一块区域，作为这条序列后面动作的范围"), // lockeys
        new(CaptureStored, "截图", "对当前范围截一张静图；跟在选区后面就是刚圈好的那块"), // lockeys
        new(CaptureFrame, "全屏即时截图", "按下立即抓光标所在屏幕的当下这一帧，不走选区、不带延时、不抢焦点，适合游戏"), // lockeys
        new(RecordToggle, "开始/停止录制", "空闲→选区起录；录制中→停止，不自动进编辑器"), // lockeys
        new(RecordStart, "开始录制", "对当前范围开始录制，直到收到停止"), // lockeys
        new(RecordStop, "停止录制", "停止录制，产物留在内存，不自动进编辑器"), // lockeys
        new(ScrollCapture, "滚动截图", "对当前范围做滚动长截图"), // lockeys
        new(DelayedCapture, "延时截图", "倒数数秒后截当前范围，用来抓一点就消失的菜单和悬停"), // lockeys
        new(OcrSelection, "识别文字", "对当前范围做 OCR：开「识别后显示结果窗」则弹可编辑结果窗，否则直接复制文本"), // lockeys
        new(PinSelection, "贴图", "把当前范围钉在屏幕上"), // lockeys
        new(EditLastStill, "编辑上次截图", "打开标注编辑器编辑上一次静图"), // lockeys
        new(EditLastVideo, "打开编辑器", "打开视频编辑器编辑上一次录制，常配在完成后，让停止录制后再进编辑器"), // lockeys
        new(NotifyToast, "提示", "把上一次截图以浮窗弹出来，点它可以进编辑器"), // lockeys
        new(CopyLast, "复制到剪贴板", "把上一次截图写进剪贴板"), // lockeys
        new(SettingsOpen, "打开设置", "打开设置窗口"), // lockeys
        new(HelpOpen, "打开帮助", "打开使用说明窗口"), // lockeys
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
