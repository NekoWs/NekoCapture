namespace NekoCapture.Core;

/// <summary>可编辑的托盘触发与初始行为。</summary>
public static class TrayDefaults
{
    /// <summary>默认立即捕获入口。</summary>
    public const string Capture = "tray.capture";
    /// <summary>默认滚动截图入口。</summary>
    public const string Scroll = "tray.scroll";
    /// <summary>默认延迟捕获入口。</summary>
    public const string Delay = "tray.delay";
    /// <summary>默认剪贴板贴图入口。</summary>
    public const string Clipboard = "tray.clipboard";

    /// <summary>读取默认行为的名称。</summary>
    /// <param name="id">行为标识。</param>
    /// <returns>默认名称；用户行为返回空文案。</returns>
    public static Str FlowLabel(string id) => id switch
    {
        Capture => K.Tray.CaptureNow, Scroll => K.Common.ScrollingCapture,
        Delay => K.Common.DelayedCapture, Clipboard => K.Tray.PinFromClipboard, _ => Str.Empty,
    };

    /// <summary>为尚未初始化托盘目录的配置加入可编辑入口。</summary>
    /// <param name="bindings">有序触发列表。</param>
    /// <param name="graph">共享行为图。</param>
    public static void Seed(List<GestureBindingDto> bindings, BehaviorGraph graph)
    {
        int serial = 0;
        BehaviorNode Node(string action)
        {
            string id;
            do { id = "tray.node." + ++serial; } while (graph.Node(id) != null);
            var node = graph.Add(action); node.Id = id; return node;
        }
        void Flow(string id, params BehaviorNode[] nodes)
        { graph.Flows.Add(new BehaviorFlow { Id = id, Steps = nodes.Select(n => n.Id).ToList() }); }
        if (graph.Flow(Capture) == null)
        {
            var select = Node(GestureActions.Select);
            select.Branches[BehaviorPorts.Still] = new() { Node(GestureActions.GrabStill).Id, Node(GestureActions.NotifyToast).Id };
            var record = Node(GestureActions.RecordStart); var edit = Node(GestureActions.EditLastVideo);
            edit.Branches[BehaviorPorts.Saved].Add(Node(GestureActions.CopyLast).Id);
            record.Branches[BehaviorPorts.Done].Add(edit.Id); select.Branches[BehaviorPorts.Record].Add(record.Id);
            Flow(Capture, select);
        }
        if (graph.Flow(Scroll) == null) Flow(Scroll, Node(GestureActions.SelectOnly), Node(GestureActions.ScrollCapture), Node(GestureActions.NotifyToast));
        if (graph.Flow(Delay) == null) Flow(Delay, Node(GestureActions.DelayedCapture), Node(GestureActions.NotifyToast));
        if (graph.Flow(Clipboard) == null)
        {
            var read = Node(GestureActions.ReadClipboardImage);
            read.Branches[BehaviorPorts.Failed].Add(Node(GestureActions.NotifyToast).Id);
            Flow(Clipboard, read, Node(GestureActions.PinSelection));
        }
        void Bind(string source, string suffix, string entry)
        {
            string id = "tray." + suffix;
            if (!bindings.Any(b => b.Id == id)) bindings.Add(new GestureBindingDto { Id = id, Source = source, EntryId = entry });
        }
        Bind(TriggerSources.TrayClick, "click", Capture); Bind(TriggerSources.TrayDoubleClick, "doubleClick", Capture);
        foreach (string id in new[] { Capture, Scroll, Delay, Clipboard }) Bind(TriggerSources.TrayMenu, "menu." + id, id);
    }
}
