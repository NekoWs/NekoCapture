namespace FrameCapture.Core;

/// <summary>把 Win32 的 32 位输入时间扩展到运行时的单调时钟。</summary>
public static class GestureClock
{
    /// <summary>将输入时间映射到当前时钟附近，兼容计数器回绕与有符号输入。</summary>
    /// <param name="timestamp">Win32 时间或完整的单调毫秒数。</param>
    /// <param name="now">当前单调毫秒数。</param>
    /// <returns>与当前时钟同一时间轴的输入时间。</returns>
    public static long Expand(long timestamp, long now) => timestamp > uint.MaxValue
        ? timestamp : now - unchecked((uint)now - (uint)timestamp);
}
