
namespace NekoCapture.Core;

/// <summary>动作的稳定标识、界面名称与说明。</summary>
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
    /// <summary>选取运行区域。</summary>
    public const string Select = "select";
    /// <summary>读取已存区域，没有时使用光标所在屏幕。</summary>
    public const string StoredScope = "scope.stored";
    /// <summary>抓取运行区域或光标屏静图。</summary>
    public const string GrabStill = "grab.still";
    /// <summary>抓取选区命中的窗口。</summary>
    public const string GrabWindow = "grab.window";
    /// <summary>保存运行静图。</summary>
    public const string SavePng = "save.png";
    public const string ScopeSelect = "scope.select";
    public const string CaptureStored = "capture.stored";
    public const string RecordToggle = "record.toggle";
    public const string RecordStart = "record.start";
    public const string RecordStop = "record.stop";
    public const string ScrollCapture = "scroll.capture";
    public const string DelayedCapture = "capture.delayed";
    public const string EditLastStill = "edit.last";
    /// <summary>编辑本链最近产出的静图或录制。</summary>
    public const string EditResult = "edit.result";
    public const string EditLastVideo = "edit.lastvideo";
    public const string OcrSelection = "ocr.selection";
    public const string PinSelection = "pin.selection";
    public const string NotifyToast = "notify.toast";
    public const string CopyLast = "copy.clipboard";
    public const string SettingsOpen = "settings.open";
    public const string HelpOpen = "help.open";
    /// <summary>隐藏模式开关并只选择区域。</summary>
    public const string SelectOnly = "select.only";
    /// <summary>读取有效的已存选区。</summary>
    public const string RequireStored = "scope.saved";
    /// <summary>清除持续保存的选区。</summary>
    public const string ClearRegion = "scope.clear";
    /// <summary>等待本次静图或录制写盘完成。</summary>
    public const string SaveResult = "save.result";
    /// <summary>调用一个共享行为入口。</summary>
    public const string CallBehavior = "behavior.call";

    /// <summary>按稳定顺序排列的规范动作目录，供界面展示与运行时校验。</summary>
    public static readonly GestureAction[] All =
    {
        new(Select, K.Actions.ScopeSelectLabel, K.Actions.ScopeSelectDesc),
        new(StoredScope, K.Hotkeys.StoredScope, K.Hotkeys.StoredScopeDesc),
        new(GrabStill, K.Common.Capture, K.Actions.CaptureStoredDesc),
        new(GrabWindow, K.Hotkeys.GrabWindow, K.Hotkeys.GrabWindowDesc),
        new(SavePng, K.Hotkeys.SavePng, K.Hotkeys.SavePngDesc),
        new(CaptureFrame, K.Actions.CaptureFrameLabel, K.Actions.CaptureFrameDesc),
        new(RecordToggle, K.Actions.RecordToggleLabel, K.Actions.RecordToggleDesc),
        new(RecordStart, K.Common.RecordStartLabel, K.Actions.RecordStartDesc),
        new(RecordStop, K.Actions.RecordStopLabel, K.Actions.RecordStopDesc),
        new(ScrollCapture, K.Common.ScrollingCapture, K.Actions.ScrollCaptureDesc),
        new(DelayedCapture, K.Common.DelayedCapture, K.Actions.DelayedCaptureDesc),
        new(OcrSelection, K.Common.RecognizeText, K.Actions.OcrSelectionDesc),
        new(PinSelection, K.Common.Pin, K.Actions.PinSelectionDesc),
        new(EditResult, K.Hotkeys.EditResult, K.Hotkeys.EditResultDesc),
        new(EditLastStill, K.Behaviors.EditStill, K.Actions.EditLastStillDesc),
        new(EditLastVideo, K.Actions.EditLastVideoLabel, K.Actions.EditLastVideoDesc),
        new(NotifyToast, K.Actions.NotifyToastLabel, K.Actions.NotifyToastDesc),
        new(CopyLast, K.Actions.CopyLastLabel, K.Actions.CopyLastDesc),
        new(SettingsOpen, K.Actions.SettingsOpenLabel, K.Actions.SettingsOpenDesc),
        new(HelpOpen, K.Actions.HelpOpenLabel, K.Actions.HelpOpenDesc),
        new(SelectOnly, K.Behaviors.SelectOnly, K.Behaviors.SelectOnlyDesc),
        new(RequireStored, K.Behaviors.StoredRequired, K.Behaviors.StoredRequiredDesc),
        new(ClearRegion, K.Behaviors.ClearRegion, K.Behaviors.ClearRegionDesc),
        new(SaveResult, K.Behaviors.SaveResult, K.Behaviors.SaveResultDesc),
        new(CallBehavior, K.Behaviors.Call, K.Behaviors.CallDesc),
    };

    /// <summary>持久化动作名归一化。</summary>
    /// <param name="name">动作名。</param>
    /// <returns>规范动作名。</returns>
    public static string Canonical(string name) => name switch
    {
        ScopeSelect => Select,
        CaptureStored => GrabStill,
        _ => name,
    };

    /// <summary>动作的数据契约。</summary>
    /// <param name="name">动作名。</param>
    /// <returns>动作类别、所需值与产出值。</returns>
    public static VerbInfo InfoOf(string name) => Canonical(name) switch
    {
        Select or SelectOnly or RequireStored => new(VerbClass.Input, RunValue.None, RunValue.Region),
        StoredScope => new(VerbClass.Input, RunValue.None, RunValue.Region),
        GrabStill or CaptureFrame or DelayedCapture => new(VerbClass.Product, RunValue.None, RunValue.Still),
        GrabWindow => new(VerbClass.Product, RunValue.Window, RunValue.Still),
        RecordStart => new(VerbClass.Product, RunValue.Region, RunValue.Video),
        RecordToggle => new(VerbClass.Product, RunValue.None, RunValue.Video),
        RecordStop => new(VerbClass.Product, RunValue.Recording, RunValue.Video),
        ScrollCapture => new(VerbClass.Product, RunValue.Region, RunValue.Still),
        SavePng => new(VerbClass.Terminal, RunValue.Still, RunValue.None),
        EditLastStill => new(VerbClass.Terminal, RunValue.Still, RunValue.None),
        EditLastVideo => new(VerbClass.Terminal, RunValue.Video, RunValue.None),
        _ => new(VerbClass.Terminal, RunValue.None, RunValue.None),
    };

    /// <summary>动作可配置的事件出口。</summary>
    /// <param name="action">规范动作名。</param>
    /// <returns>按界面顺序排列的事件名。</returns>
    public static string[] PortsOf(string action) => Canonical(action) switch
    {
        Select => new[] { BehaviorPorts.Still, BehaviorPorts.Record, BehaviorPorts.Cancel, BehaviorPorts.Failed },
        RecordStart or RecordToggle =>
            new[] { BehaviorPorts.Started, BehaviorPorts.Cancel, BehaviorPorts.Done, BehaviorPorts.Failed },
        SelectOnly or ScrollCapture or DelayedCapture =>
            new[] { BehaviorPorts.Done, BehaviorPorts.Cancel, BehaviorPorts.Failed },
        EditLastStill or EditLastVideo or EditResult =>
            new[] { BehaviorPorts.Saved, BehaviorPorts.Closed, BehaviorPorts.Failed },
        _ => new[] { BehaviorPorts.Done, BehaviorPorts.Failed },
    };

    /// <summary>事件出口的界面名称。</summary>
    /// <param name="action">父动作。</param>
    /// <param name="port">事件名。</param>
    /// <returns>当前语言的标签。</returns>
    public static string PortLabel(string action, string port) => Loc.T(port switch
    {
        BehaviorPorts.Still => K.Common.Capture,
        BehaviorPorts.Record => K.Common.Recording,
        BehaviorPorts.Cancel when action == Select => K.Common.CancelSelection,
        BehaviorPorts.Cancel => K.Behaviors.Cancel,
        BehaviorPorts.Failed => K.Hotkeys.OnFailure,
        BehaviorPorts.Saved => K.Behaviors.Saved,
        BehaviorPorts.Closed => K.Behaviors.Closed,
        BehaviorPorts.Started => K.Behaviors.Started,
        BehaviorPorts.Done when action is RecordStart or RecordToggle => K.Behaviors.Stopped,
        _ => K.Behaviors.Done,
    });

    /// <summary>是否由选区开关选择产物。</summary>
    /// <param name="chain">完整动作链。</param>
    /// <returns>含选区时为真。</returns>
    public static bool FollowsSwitch(IEnumerable<string> chain)
        => chain.Any(n => Canonical(n) == Select);

    /// <summary>每一步静态缺少的前置值。</summary>
    /// <param name="chain">完整动作链。</param>
    /// <returns>与步骤一一对应的缺失值；录制状态由运行时提供。</returns>
    public static RunValue[] MissingInputs(IReadOnlyList<string> chain)
    {
        var values = RunValue.None;
        var missing = new RunValue[chain.Count];
        for (int i = 0; i < chain.Count; i++)
        {
            var info = InfoOf(chain[i]);
            missing[i] = info.Needs & ~values;
            values |= info.Produces;
            if (Canonical(chain[i]) == Select) values |= RunValue.Window;
        }
        return missing;
    }

    static readonly HashSet<string> Names =
        new(Enumerable.Select(All, a => a.Name));

    /// <summary>名字是否在本词汇表里。</summary>
    /// <param name="name">待校验的动作名，区分大小写。</param>
    /// <returns>null 或表外名为 false；设置文件里的未知动作名据此判为未实现，而不是崩在分发处。</returns>
    public static bool IsValid(string? name) => name != null && Names.Contains(Canonical(name));

    /// <summary>动作名 → 中文短名（供设置窗「当前生效键位」等处展示）。</summary>
    /// <param name="name">动作名；null 或空串按空名处理。</param>
    /// <returns>表内条目的 Label；未知名原样返回（不崩，便于前向兼容），空名返回空串。</returns>
    public static string LabelOf(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        name = Canonical(name);
        foreach (var a in All) if (a.Name == name) return a.Label;
        return name!;
    }
}
