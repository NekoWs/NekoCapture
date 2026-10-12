namespace FrameCapture.Core;

/// <summary>
/// 手势动作词汇表（R120 B2）：把「功能」抽象成稳定的动作名，供 GestureCore 绑定序列引用。
/// 名与 UI 文案分离——名是持久化/引擎用的稳定标识（改名会破坏已存 settings.json，故用英文小写点分），
/// 文案是给人看的。新增功能 = 在此加一条 + 在 GestureInvoker 里接上实现。
/// </summary>
public readonly struct GestureAction
{
    public string Name { get; }
    public string Label { get; }   // 中文短名（未来编辑器/帮助展示）
    public string Description { get; }
    public GestureAction(string name, string label, string desc) { Name = name; Label = label; Description = desc; }
}

public static class GestureActions
{
    // 稳定动作名常量（持久化标识，勿随意改名）
    public const string CaptureFrame = "capture.frame";
    public const string ScopeSelect = "scope.select";
    public const string RecordStart = "record.start";
    public const string RecordStop = "record.stop";
    public const string ScrollCapture = "scroll.capture";
    public const string DelayedCapture = "capture.delayed";
    public const string EditLastStill = "edit.last";
    public const string OcrSelection = "ocr.selection";
    public const string PinSelection = "pin.selection";
    public const string SettingsOpen = "settings.open";
    public const string HelpOpen = "help.open";

    /// <summary>全部可用动作（顺序即建议展示顺序）。GestureInvoker 必须覆盖每一个，否则该动作视为未实现（执行失败）。</summary>
    public static readonly GestureAction[] All =
    {
        new(CaptureFrame, "捕获单帧", "对当前范围截取一帧（走点按路径，静图）"),
        new(ScopeSelect, "选取范围", "进入交互式选区"),
        new(RecordStart, "开始录制", "对当前范围开始录制（录到停止为止）"),
        new(RecordStop, "结束录制", "停止录制并进入编辑器"),
        new(ScrollCapture, "滚动截图", "对当前范围做滚动长截图"),
        new(DelayedCapture, "延时截图", "倒数数秒后截当前范围（抓一动就没的菜单/悬停）"),
        new(EditLastStill, "编辑上次截图", "打开标注编辑器编辑上一次静图"),
        new(OcrSelection, "识别文字", "对当前范围做 OCR 并复制文本"),
        new(PinSelection, "贴图当前选区", "把当前范围钉成贴图（Snipaste 式）"),
        new(SettingsOpen, "打开设置", "打开设置窗口"),
        new(HelpOpen, "打开帮助", "打开使用说明窗口"),
    };

    static readonly System.Collections.Generic.HashSet<string> Names =
        new(System.Linq.Enumerable.Select(All, a => a.Name));

    public static bool IsValid(string? name) => name != null && Names.Contains(name);
}
