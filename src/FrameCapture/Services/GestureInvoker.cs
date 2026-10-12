using System;
using FrameCapture.Core;
using GC = GestureCore;

namespace FrameCapture.Services;

/// <summary>
/// 手势动作执行器（R120 B2）：把 <see cref="GestureActions"/> 里的动作名映射到 AppHost/CaptureController 的真实操作。
/// 钩子回调占着全系统输入链，故动作一律 <b>BeginInvoke 投递</b>到 UI 线程、绝不在回调内跑重活。
///
/// 返回值语义（供引擎失败策略）：对「有前置条件」的动作（开始/结束录制）先做<b>同步状态检查</b>——
/// 不满足即返回 false（不投递），从而让「失败自动下一个」(NextBinding) 在运行期真正生效；
/// 其余动作乐观返回 true 并投递。检查读 Controller.State 在 UI 线程（钩子回调线程），无并发问题。
/// </summary>
sealed class GestureInvoker : GC.IActionInvoker
{
    readonly AppHost _host;
    public GestureInvoker(AppHost host) => _host = host;

    static void Post(Action a)
    {
        var app = System.Windows.Application.Current;
        if (app == null) { try { a(); } catch { } return; }
        try { app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, a); }
        catch { }
    }

    public bool Invoke(string actionName)
    {
        var c = _host.Controller;
        switch (actionName)
        {
            case GestureActions.CaptureFrame:
                if (c.State != CaptureController.Phase.Idle) return false;
                Post(() =>
                {
                    c.BeginCapture();
                    // 无“松手”来源：600ms 后自动结束（与托盘「立即捕获」同款已知可靠单帧路径）
                    var t = new System.Timers.Timer(600);
                    t.Elapsed += (_, _) => { t.Stop(); t.Dispose(); Post(() => c.EndCapture()); };
                    t.Start();
                });
                return true;

            case GestureActions.ScopeSelect: Post(() => c.BeginScopeSelection()); return true;
            case GestureActions.CaptureSelection: Post(() => c.BeginCaptureSelection()); return true;
            case GestureActions.RecordSelection: Post(() => c.BeginRecordSelection()); return true;

            case GestureActions.RecordStart:
                if (c.State != CaptureController.Phase.Idle) return false;   // 非空闲→失败（可顺延下一条）
                Post(() => c.StartRecording());
                return true;

            case GestureActions.RecordStop:
                if (c.State != CaptureController.Phase.Capturing) return false; // 未在录→失败（可顺延下一条）
                Post(() => c.StopRecording());
                return true;

            case GestureActions.ScrollCapture: Post(() => c.BeginScrollCapture()); return true;
            case GestureActions.DelayedCapture: Post(() => c.BeginDelayedCapture()); return true;
            case GestureActions.EditLastStill: Post(() => c.EditLastStill()); return true;
            case GestureActions.PinSelection: Post(() => c.PinFromClipboard()); return true;

            case GestureActions.OcrSelection:
                // 尚无「对当前范围直接 OCR」的控制器入口（OCR 现长在编辑器里）；未实现→返回 false，交由失败策略处理。
                return false;

            case GestureActions.SettingsOpen: Post(() => Overlays.SettingsWindow.ShowOrFocus()); return true;
            case GestureActions.HelpOpen: Post(() => Overlays.HelpWindow.ShowOrFocus()); return true;

            default: return false; // 未知动作=失败
        }
    }
}
