namespace FrameCapture.Capture;

/// <summary>读取帧时间信息；磁盘帧使用索引，不物化像素。</summary>
internal static class FrameMetadata
{
    /// <summary>读取指定帧的时间戳。</summary>
    /// <param name="frames">内存或磁盘帧序列。</param>
    /// <param name="index">帧下标。</param>
    /// <returns>相对捕获开始的毫秒数。</returns>
    internal static double TimeAt(IReadOnlyList<CapturedFrame> frames, int index) =>
        frames is FrameSpoolList spool ? spool.Meta(index).TimeMs : frames[index].TimeMs;
}
