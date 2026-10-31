namespace FrameCapture.Core;

/// <summary>不写入用户配置、由固定界面入口启动的行为。</summary>
public static class BuiltInBehaviors
{
    /// <summary>托盘立即捕获的固定入口。</summary>
    public const string Capture = "builtin.capture";

    /// <summary>创建带模式切换的选区行为，截图后提示，录制后编辑，编辑输出后复制。</summary>
    /// <returns>独立的运行图，不与用户行为共享节点。</returns>
    public static BehaviorGraph CreateCapture()
    {
        var graph = new BehaviorGraph();
        var select = graph.Add(GestureActions.Select);
        select.ShowModeSwitch = true;
        select.RecordMode = false;
        select.Branches[BehaviorPorts.Still].Add(graph.Add(GestureActions.GrabStill).Id);
        select.Branches[BehaviorPorts.Still].Add(graph.Add(GestureActions.NotifyToast).Id);
        var record = graph.Add(GestureActions.RecordStart);
        var edit = graph.Add(GestureActions.EditLastVideo);
        edit.Branches[BehaviorPorts.Saved].Add(graph.Add(GestureActions.CopyLast).Id);
        record.Branches[BehaviorPorts.Done].Add(edit.Id);
        select.Branches[BehaviorPorts.Record].Add(record.Id);
        graph.Flows.Add(new BehaviorFlow { Id = Capture, Steps = new() { select.Id } });
        return graph;
    }
}
