namespace FrameCapture.Core;

/// <summary>请求运行时按内存压力回收空闲对象，不等待终结器、不裁剪进程工作集。</summary>
public static class Memory
{
    /// <summary>提交非阻塞的优化回收请求；运行时自行决定是否执行。</summary>
    public static void Reclaim() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
}
