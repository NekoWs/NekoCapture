namespace FrameCapture.Core;

/// <summary>大操作收尾的内存回收。录制/编辑会把数百 MB 位图读进内存（编辑器位图 LRU 封顶约 600MB），
/// 关闭后 .NET 只把对象标成垃圾、并不立刻把物理页交还操作系统，工作集因此虚高——用户手动跑 MemReduct
/// （内部即 SetProcessWorkingSetSize(-1,-1)）才回落。这里在「编辑器关闭 / 后台编码完成」等真正空闲的时机
/// 主动做同样的事：全代 GC（含终结器队列，位图/句柄对象多带 finalizer）→ 裁工作集，让常驻内存回到干净基线。
/// 只在 UI 空闲、无捕获进行时调用（GC.Collect 会短暂暂停 UI，故不可放在捕获/播放热路径上）。</summary>
public static class Memory
{
    /// <summary>主动回收：全代 GC → 等待终结器 → 再 GC（回收 finalizer 释放的非托管位图内存）→ 裁工作集。
    /// 幂等、吞异常；调用方负责确保此刻空闲。</summary>
    public static void Reclaim()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Native.Win32.TrimWorkingSet();
        }
        catch { }
    }
}
