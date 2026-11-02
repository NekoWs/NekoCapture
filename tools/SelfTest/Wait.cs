using System.Diagnostics;

static partial class SelfTest
{
    /// <summary>等到条件成立再断言：动画收尾这类判据用轮询，别拿固定睡眠赌机器负载。</summary>
    /// <param name="done">判据。</param>
    /// <param name="timeoutMs">最长等待毫秒数。</param>
    /// <returns>条件是否在超时前成立。</returns>
    private static bool Until(Func<bool> done, int timeoutMs = 3000)
    {
        if (done()) return true;
        var clock = Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < timeoutMs) PumpMs(15);
        return done();
    }
}
