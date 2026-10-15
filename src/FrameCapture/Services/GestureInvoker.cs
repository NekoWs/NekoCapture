using System.Collections.Generic;
using FrameCapture.Core;
using GC = GestureCore;

namespace FrameCapture.Services;

/// <summary>
/// 手势动作执行器：把 <see cref="GestureActions"/> 里的动作名映射到 AppHost/CaptureController 的真实操作。
/// 钩子回调占着全系统输入链，故动作一律 <b>BeginInvoke 投递</b>到 UI 线程、绝不在回调内跑重活。
///
/// 返回值语义（供引擎失败策略）：多数动作乐观返回 true 并投递，真正的状态判定（是否空闲/是否在录）
/// 放到投递的动作里于 UI 线程运行时做——钩子回调本身就在 UI 线程，若在回调里同步查 State，
/// 同一次按键的 KeyDown/KeyUp 之间投递的动作还没跑，读到的是过期状态，按住录制便停不掉。
/// 仅「即时定格」这类需要即时让位的动作保留回调内同步 State 检查。
///
/// 「选区」是整条序列里唯一的<b>前置任务</b>：它什么时候算完成只有宿主知道，
/// 所以引擎把整条序列交回 <see cref="InvokePrelude"/>，后续动作与完成/失败落点都由宿主在范围落地后结算。
/// </summary>
sealed class GestureInvoker : GC.IPreludeInvoker
{
    readonly AppHost _host;

    /// <summary>当前正在执行的绑定，供前置任务把完成/失败序列交给控制器。
    /// 引擎保证 <see cref="BeginBinding"/> 先于本条任何动作调用，两者在同一次输入回调里紧邻发生。</summary>
    GC.GestureBinding? _binding;

    /// <summary>只保存宿主引用；控制器在每次 <see cref="Invoke"/> 时才取，故构造可早于控制器装配完成。</summary>
    /// <param name="host">提供 <see cref="CaptureController"/> 的宿主，整个生命周期内持有。</param>
    public GestureInvoker(AppHost host) => _host = host;

    static void Post(Action a)
    {
        var app = System.Windows.Application.Current;
        if (app == null) { try { a(); } catch { } return; }
        try { app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, a); }
        catch { }
    }

    /// <summary>
    /// 按动作名执行一次动作，并记一条 trace（只记动作名、受理结果与控制器相位，不含任何用户输入内容）。
    /// </summary>
    /// <param name="actionName"><see cref="GestureActions"/> 里的动作名，大小写按表内原文匹配。</param>
    /// <returns>是否受理。true = 已投递到 UI 线程，最终是否真做由投递的动作在 UI 线程判状态决定；
    /// false = 动作名未知，或「即时定格」在非空闲相位被拒（引擎据此走该绑定的失败策略）。</returns>
    public bool Invoke(string actionName)
    {
        var c = _host.Controller;
        bool result = InvokeCore(actionName, c);
        Trace.Log($"gesture act {actionName} ok={result} state={c.State}");
        return result;
    }

    /// <summary>
    /// 一条绑定开跑前的前置投递：把「完成后」的上下文交给控制器。
    /// 失败静默——上下文没递到只会退回默认行为，不该中断已命中的手势。
    /// </summary>
    /// <param name="b">即将执行的绑定。它的 <c>OnComplete</c> 非空 = 这条手势自己指定落点，
    /// 截图默认的弹提示让位；为空 = 不覆盖，照默认走。</param>
    public void BeginBinding(GC.GestureBinding b)
    {
        _binding = b;
        try { _host.Controller.SetPendingAfterCapture(b.OnComplete.Count > 0 ? "none" : ""); } catch { }
    }

    /// <summary>「选区」是这条序列唯一的前置任务：它要等用户拖完框，后面的动作不能立刻跑。</summary>
    /// <param name="actionName">序列第一步的动作名。</param>
    /// <returns>只有 <see cref="GestureActions.ScopeSelect"/> 是前置任务。</returns>
    public bool IsPrelude(string actionName) => actionName == GestureActions.ScopeSelect;

    /// <summary>受理一条以「选区」开头的序列：把范围落地之后要做的事整体交给控制器，
    /// 范围定了它再把名字表原样交回 <see cref="Invoke"/>。</summary>
    /// <param name="actions">完整序列，[0] 是「选区」。</param>
    /// <returns>恒为 true——开选区这件事本身不会失败，取消走这条绑定的失败序列。</returns>
    public bool InvokePrelude(IReadOnlyList<string> actions)
    {
        var b = _binding;
        var verbs = new List<string>();
        for (int i = 1; i < actions.Count; i++) verbs.Add(actions[i]);
        var lead = verbs.Count > 0 ? LeadOf(verbs[0]) : CaptureController.ScopedLead.None;
        var tail = verbs.Count > 0 ? verbs.GetRange(1, verbs.Count - 1) : verbs;
        if (b != null) tail.AddRange(b.OnComplete);
        var onFail = b != null ? new List<string>(b.OnFail) : new List<string>();
        Trace.Log($"gesture prelude lead={lead} tail={tail.Count} onFail={onFail.Count}");
        Post(() => _host.Controller.BeginScopedRun(lead, tail, onFail, RunNames));
        return true;
    }

    /// <summary>控制器在产物收尾点交回的名字表：逐个走正常执行路径，各自按自己的路径记 trace。</summary>
    /// <param name="names">要按序执行的动作名。</param>
    void RunNames(IReadOnlyList<string> names)
    {
        foreach (var n in names) Invoke(n);
    }

    /// <summary>范围落地后，第一个动作走哪条既有的选区收尾路径。
    /// 只有「对当前范围截图」需要选区刚定格的那一帧；全屏即时截图本来就无视范围，归 None。</summary>
    static CaptureController.ScopedLead LeadOf(string name) => name switch
    {
        GestureActions.CaptureStored => CaptureController.ScopedLead.Still,
        GestureActions.RecordToggle or GestureActions.RecordStart or GestureActions.RecordStop => CaptureController.ScopedLead.Record,
        GestureActions.ScrollCapture => CaptureController.ScopedLead.Scroll,
        _ => CaptureController.ScopedLead.None,
    };

    bool InvokeCore(string actionName, CaptureController c)
    {
        switch (actionName)
        {
            case GestureActions.CaptureFrame:
                if (c.State != CaptureController.Phase.Idle) return false;
                // 即时定格：直接抓当下这一帧，不走 BeginCapture+600ms 定时器（那 600ms 会让游戏丢焦点暂停）。
                Post(() => c.CaptureInstantStill());
                return true;

            case GestureActions.ScopeSelect: Post(() => c.BeginScopeSelection()); return true;
            case GestureActions.CaptureStored: Post(() => c.CaptureStoredStill()); return true;
            case GestureActions.RecordToggle: Post(() => c.ToggleRecordSelection()); return true;

            case GestureActions.RecordStart:
                // 状态判定放到投递的动作里运行时做：钩子回调就在 UI 线程，若在回调里同步查 State，
                // 则 KeyDown 投的 StartRecording 还没跑、KeyUp 时 State 仍 Idle → stop 误判失败、录制停不掉。
                // 投递后 FIFO 保证 StartRecording 先于 StopRecording 执行，按住录制/放开停止才成立。
                Post(() => c.StartRecording());
                return true;

            case GestureActions.RecordStop:
                Post(() => c.StopRecording());
                return true;

            case GestureActions.ScrollCapture: Post(() => c.BeginScrollCapture()); return true;
            case GestureActions.DelayedCapture: Post(() => c.BeginDelayedCapture()); return true;
            case GestureActions.EditLastStill: Post(() => c.EditLastStill()); return true;
            case GestureActions.EditLastVideo: Post(() => c.EditLastVideo()); return true;
            case GestureActions.PinSelection: Post(() => c.PinStoredStill()); return true;
            case GestureActions.NotifyToast: Post(() => c.ShowLastStillToast()); return true;
            case GestureActions.CopyLast: Post(() => c.CopyLastStill()); return true;

            case GestureActions.OcrSelection:
                // 对当前范围直接 OCR（提取文字）。抓取/识别在投递的动作里于 UI 线程跑，钩子回调不占重活。
                Post(() => c.OcrStoredStill());
                return true;

            case GestureActions.SettingsOpen: Post(() => Overlays.SettingsWindow.ShowOrFocus()); return true;
            case GestureActions.HelpOpen: Post(() => Overlays.HelpWindow.ShowOrFocus()); return true;

            default: return false; // 未知动作=失败
        }
    }
}
