using System.Threading;

namespace FrameCapture.Core;

/// <summary>串行后台无障碍查询；只保留一个正在执行与一个最新请求，调用方可独立超时。</summary>
internal static class ElementProbe
{
    private static readonly object Gate = new();
    private static readonly AutoResetEvent Ready = new(false);
    private static Request? _pending;
    private static readonly Lazy<Thread> Worker = new(Start);
    private static long _activeSince;

    /// <summary>无障碍提供程序长时间未返回，空闲时可重建界面进程恢复查询。</summary>
    internal static bool Stalled => Volatile.Read(ref _activeSince) is var since && since != 0 && Environment.TickCount64 - since > 2000;

    /// <summary>提交最新的命中或层级查询，不阻塞界面。</summary>
    /// <param name="query">仅捕获点位、窗口句柄或元素，不捕获选区窗口。</param>
    /// <returns>查询结果；尚未执行就被替换时为空。</returns>
    internal static Task<ElementHit?> Query(Func<ElementHit?> query)
    {
        var request = new Request(query);
        _ = Worker.Value;
        lock (Gate) { _pending?.Completion.TrySetResult(null); _pending = request; }
        Ready.Set();
        return request.Completion.Task;
    }

    private static Thread Start()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                Ready.WaitOne();
                Request? request;
                lock (Gate) { request = _pending; _pending = null; }
                if (request == null) continue;
                Volatile.Write(ref _activeSince, Environment.TickCount64);
                try { request.Completion.TrySetResult(request.Query()); }
                catch { request.Completion.TrySetResult(null); }
                finally { Volatile.Write(ref _activeSince, 0); }
            }
        }) { IsBackground = true, Name = "Element snap", Priority = ThreadPriority.BelowNormal };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return thread;
    }

    private sealed class Request(Func<ElementHit?> query)
    {
        /// <summary>后台串行执行的查询。</summary>
        internal readonly Func<ElementHit?> Query = query;
        /// <summary>支持异步通知的结果。</summary>
        internal readonly TaskCompletionSource<ElementHit?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
