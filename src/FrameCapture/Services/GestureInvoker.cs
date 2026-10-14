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
/// </summary>
sealed class GestureInvoker : GC.IActionInvoker
{
    readonly AppHost _host;

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
        FrameCapture.Core.Trace.Log($"gesture act {actionName} ok={result} state={c.State}");
        return result;
    }

    /// <summary>
    /// 一条绑定开跑前的前置投递：把该绑定的「截图后」上下文交给控制器，截图完成时按它分派。
    /// 失败静默——上下文没递到只会退回默认行为，不该中断已命中的手势。
    /// </summary>
    /// <param name="b">即将执行的绑定。其 <c>AfterCapture</c> 为空串 = 不指定，截图后走默认弹提示。</param>
    public void BeginBinding(GC.GestureBinding b)
    {
        try { _host.Controller.SetPendingAfterCapture(b.AfterCapture); } catch { }
    }

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
            case GestureActions.CaptureSelection: Post(() => c.BeginCaptureSelection()); return true;
            case GestureActions.RecordSelection: Post(() => c.BeginRecordSelection()); return true;
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
            case GestureActions.PinSelection: Post(() => c.PinFromClipboard()); return true;

            case GestureActions.OcrSelection:
                // 对当前范围直接 OCR（QQ 式「提取文字」）。抓取/识别在投递的动作里于 UI 线程跑，钩子回调不占重活。
                Post(() => c.OcrStoredStill());
                return true;

            case GestureActions.SettingsOpen: Post(() => Overlays.SettingsWindow.ShowOrFocus()); return true;
            case GestureActions.HelpOpen: Post(() => Overlays.HelpWindow.ShowOrFocus()); return true;
            case GestureActions.GestureEdit: Post(() => GestureEditorHost.ShowOrFocus()); return true;

            default: return false; // 未知动作=失败
        }
    }
}
