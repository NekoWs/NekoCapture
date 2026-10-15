
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
    /// <value>编辑器、帮助与设置窗展示用，不参与持久化。</value>
    public string Label => Loc.T(_label);

    /// <summary>这个动作做什么的一句话，按当前界面语言取词。</summary>
    /// <value>设置与帮助里的详细说明。</value>
    public string Description => Loc.T(_desc);

    /// <summary>装配一个动作条目，各字段原样保存。</summary>
    /// <param name="name">稳定动作名（英文小写点分）。</param>
    /// <param name="label">短名文案。</param>
    /// <param name="desc">详细说明文案。</param>
    public GestureAction(string name, Str label, Str desc) { Name = name; _label = label; _desc = desc; }

    readonly Str _label;
    readonly Str _desc;
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
        new(ScopeSelect, K.Actions.ScopeSelectLabel, K.Actions.ScopeSelectDesc),
        new(CaptureStored, K.Common.Capture, K.Actions.CaptureStoredDesc),
        new(CaptureFrame, K.Actions.CaptureFrameLabel, K.Actions.CaptureFrameDesc),
        new(RecordToggle, K.Actions.RecordToggleLabel, K.Actions.RecordToggleDesc),
        new(RecordStart, K.Actions.RecordStartLabel, K.Actions.RecordStartDesc),
        new(RecordStop, K.Actions.RecordStopLabel, K.Actions.RecordStopDesc),
        new(ScrollCapture, K.Common.ScrollingCapture, K.Actions.ScrollCaptureDesc),
        new(DelayedCapture, K.Common.DelayedCapture, K.Actions.DelayedCaptureDesc),
        new(OcrSelection, K.Common.RecognizeText, K.Actions.OcrSelectionDesc),
        new(PinSelection, K.Common.Pin, K.Actions.PinSelectionDesc),
        new(EditLastStill, K.Tray.EditLastScreenshot, K.Actions.EditLastStillDesc),
        new(EditLastVideo, K.Actions.EditLastVideoLabel, K.Actions.EditLastVideoDesc),
        new(NotifyToast, K.Actions.NotifyToastLabel, K.Actions.NotifyToastDesc),
        new(CopyLast, K.Actions.CopyLastLabel, K.Actions.CopyLastDesc),
        new(SettingsOpen, K.Actions.SettingsOpenLabel, K.Actions.SettingsOpenDesc),
        new(HelpOpen, K.Actions.HelpOpenLabel, K.Actions.HelpOpenDesc),
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
