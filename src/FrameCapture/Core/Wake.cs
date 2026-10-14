namespace FrameCapture.Core;

/// <summary>
/// 后台线程唤醒等待方的姿势：等待方可能已经不等了。
///
/// 带超时的等待一旦超时，等待方就释放完成事件、抛异常走人；落在后面的后台线程收尾时
/// 照样去置位，而事件已经 Dispose。置位点位于 finally 或 catch 之中，那里抛出的
/// ObjectDisposedException 没有任何接手方，进程直接终止。
/// </summary>
internal static class Wake
{
    /// <summary>置位完成事件。等待方已释放它，就说明没人再等，静默返回。</summary>
    /// <param name="done">等待方创建的完成事件，可能已被等待方释放。</param>
    public static void Set(ManualResetEventSlim done)
    {
        try { done.Set(); }
        catch (ObjectDisposedException) { }
    }
}
