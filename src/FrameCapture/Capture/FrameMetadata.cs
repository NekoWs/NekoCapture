namespace FrameCapture.Capture;

/// <summary>读取帧索引的时间与尺寸，不物化磁盘或烘焙帧的像素。</summary>
internal static class FrameMetadata
{
    /// <summary>读取指定帧的时间戳。</summary>
    /// <param name="frames">只读帧序列。</param>
    /// <param name="index">帧下标。</param>
    /// <returns>相对捕获开始的毫秒数。</returns>
    internal static double TimeAt(IReadOnlyList<CapturedFrame> frames, int index) => At(frames, index).TimeMs;

    /// <summary>读取指定帧的元数据。</summary>
    /// <param name="frames">只读帧序列。</param>
    /// <param name="index">帧下标。</param>
    /// <returns>时间戳、尺寸与字节数；非磁盘帧的偏移为零。</returns>
    internal static FrameSpoolList.Entry At(IReadOnlyList<CapturedFrame> frames, int index)
    {
        if (frames is FrameSpoolList spool) return spool.Meta(index);
        if (frames is BakedFrameList baked) return baked.Meta(index);
        var frame = frames[index];
        return new(0, frame.TimeMs, frame.W, frame.H, frame.Bgra.Length);
    }
}
