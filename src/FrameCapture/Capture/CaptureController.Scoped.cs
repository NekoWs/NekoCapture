using System;
using System.Collections.Generic;
using FrameCapture.Core;

namespace FrameCapture.Services;

// 本 partial 职责：手势「先选区，再按序执行」这一次性尾巴的挂载与结算。
/// <summary>
/// <see cref="CaptureController"/> 的前置任务分部：一条手势以「选区」开头时，范围何时落地、落在哪，只有选区交互知道
/// 用户会不会选、选在哪。这里把范围落地之后要做的事挂成一条尾巴，在各自产物的收尾点交还给宿主执行。
/// 控制器不认识动作名——它只在约定的收尾点把宿主早先交来的名字表原样回调出去。
/// </summary>
public sealed partial class CaptureController : IDisposable
{
    /// <summary>前置任务落地后，第一个动作走哪条既有的选区收尾路径。选区这一步本身不在这里，
    /// 它由 <see cref="BeginScopeSelection"/> 负责；这里只决定「选完之后先干什么」。</summary>
    public enum ScopedLead
    {
        /// <summary>没有专门的收尾路径：范围存好后直接按序执行尾巴，各动作自己读已存范围。</summary>
        None,
        /// <summary>静图——走选区即截一帧那条路径，定格帧与窗口截图裁定走同一条判定。</summary>
        Still,
        /// <summary>录制——选完即对该区域起录，尾巴等录制停止时才执行。</summary>
        Record,
        /// <summary>滚动截图——选完即进入步进拼接循环，尾巴等长图落地时执行。</summary>
        Scroll,
    }

    // 挂在本次选区上的尾巴：范围落地后按序执行的名字表、取消时的名字表，以及把名字表跑起来的宿主回调。
    IReadOnlyList<string> _tail = Array.Empty<string>();
    IReadOnlyList<string> _tailOnFail = Array.Empty<string>();
    Action<IReadOnlyList<string>>? _tailRun;
    bool _tailArmed;

    /// <summary>开始一次「先选区再执行」：空闲时先开选区，范围落地后按 <paramref name="lead"/> 走对应收尾，
    /// 收尾点再把 <paramref name="tail"/> 交回 <paramref name="run"/>。
    /// 非空闲（正在录制）时范围已经定死，不重开选区——直接把 lead 与尾巴就地跑掉，
    /// 「同一个键再按一次是停止」靠的就是这一条。</summary>
    /// <param name="lead">第一个动作走哪条选区收尾路径；<see cref="ScopedLead.None"/> 表示范围存好就直接跑尾巴。</param>
    /// <param name="tail">范围落地、且 lead 的产物收尾之后要按序执行的动作名，可为空。</param>
    /// <param name="onFail">选区被取消时按序执行的动作名，可为空。</param>
    /// <param name="run">把名字表跑起来的宿主回调；控制器只负责在收尾点原样交还。</param>
    public void BeginScopedRun(ScopedLead lead, IReadOnlyList<string> tail, IReadOnlyList<string> onFail, Action<IReadOnlyList<string>> run)
    {
        _tail = tail; _tailOnFail = onFail; _tailRun = run;
        Trace.Log($"scoped arm lead={lead} tail={tail.Count} onFail={onFail.Count} state={State}");
        if (State != Phase.Idle)
        {
            // 范围已定：不重开选区，lead 与尾巴就地执行。录制中按录制键即停，停止点再把尾巴跑掉。
            _tailArmed = false;
            var go = LeadNames(lead, tail);
            _tail = lead == ScopedLead.None ? tail : Array.Empty<string>();
            run(go);
            return;
        }
        _tailArmed = true;
        BeginScopeSelection();
        // 选区窗没开起来（相位不对）：尾巴当场作废，绝不留给下一次选区误触发
        if (State != Phase.SelectingScope) { DropScopedTail(); Trace.Log("scoped arm aborted: picker not open"); return; }
        switch (lead)
        {
            case ScopedLead.Still: _captureAfterSelect = true; break;
            case ScopedLead.Record: _recordAfterSelect = true; break;
            case ScopedLead.Scroll: _scrollAfterSelect = true; break;
        }
    }

    /// <summary>lead 与尾巴拼成一次执行的名字表：lead 自己就是第一个动作，它排在尾巴前面。</summary>
    static IReadOnlyList<string> LeadNames(ScopedLead lead, IReadOnlyList<string> tail)
    {
        if (lead == ScopedLead.None) return tail;
        var all = new List<string>(tail.Count + 1) { LeadActionName(lead) };
        all.AddRange(tail);
        return all;
    }

    /// <summary>非空闲就地执行时，lead 对应的那个动作名——由宿主给出，控制器只把它塞回名字表里跑。</summary>
    static string LeadActionName(ScopedLead lead) => lead switch
    {
        ScopedLead.Still => GestureActions.CaptureStored,
        ScopedLead.Record => GestureActions.RecordToggle,
        ScopedLead.Scroll => GestureActions.ScrollCapture,
        _ => "",
    };

    /// <summary>选区被取消：把失败尾巴交回宿主，然后清干净。没有挂尾巴时什么都不做。</summary>
    void RunScopedFail()
    {
        if (!_tailArmed) return;
        _tailArmed = false;
        var names = _tailOnFail;
        _tail = Array.Empty<string>(); _tailOnFail = Array.Empty<string>();
        var run = _tailRun; _tailRun = null;
        if (names.Count == 0) return;
        Trace.Log($"scoped cancel → onFail {names.Count}");
        run?.Invoke(names);
    }

    /// <summary>产物没落地（一帧都没抓到）：尾巴作废。留着它会把下一次截图的收尾误当成这次的完成。</summary>
    void DropScopedTail()
    {
        _tailRun = null;
        _tail = Array.Empty<string>(); _tailOnFail = Array.Empty<string>(); _tailArmed = false;
    }

    /// <summary>产物收尾点调用：把挂着的尾巴交回宿主执行。没挂尾巴时零开销。</summary>
    void RunScopedTail(string from)
    {
        var run = _tailRun;
        if (run == null) return;
        _tailRun = null;
        var names = _tail;
        _tail = Array.Empty<string>(); _tailOnFail = Array.Empty<string>(); _tailArmed = false;
        if (names.Count == 0) return;
        Trace.Log($"scoped tail from={from} n={names.Count}");
        run(names);
    }
}
