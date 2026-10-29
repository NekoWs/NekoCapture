namespace FrameCapture.Services;

/// <summary>跟踪窗口关闭后仍需完成的后台产物任务。</summary>
internal static class BackgroundWork
{
    private static int _active;

    /// <summary>仍有任务持有尚未完成的产物。</summary>
    internal static bool Busy => Volatile.Read(ref _active) != 0;

    /// <summary>在异步操作完成前保持界面进程存活。</summary>
    /// <returns>可重复释放的存活凭据。</returns>
    internal static IDisposable Hold() { Interlocked.Increment(ref _active); return new Lease(); }

    private sealed class Lease : IDisposable
    {
        private int _disposed;
        /// <inheritdoc/>
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref _active); }
    }

    /// <summary>在后台执行任务，取消或失败时也释放存活计数。</summary>
    /// <typeparam name="T">结果类型。</typeparam>
    /// <param name="action">后台操作。</param>
    /// <param name="cancellation">开始前与执行中的取消信号。</param>
    /// <returns>操作结果任务。</returns>
    internal static async Task<T> Run<T>(Func<T> action, CancellationToken cancellation = default)
    {
        using var activity = Hold();
        return await Task.Run(action, cancellation).ConfigureAwait(false);
    }
}
