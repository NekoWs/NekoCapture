namespace FrameCapture.Core;

/// <summary>按序等待每个步骤。失败序列最多执行一次，失败序列中的错误不传播。</summary>
public static class ActionChain
{
    /// <summary>执行动作链与失败策略。</summary>
    /// <param name="actions">主链。</param>
    /// <param name="onFail">失败链。</param>
    /// <param name="fallback">单步失败策略。</param>
    /// <param name="step">完成时返回结果的步骤宿主。</param>
    /// <param name="cancelled">交互取消判定；为空时由失败策略决定是否继续。</param>
    /// <returns>主链是否全部成功。</returns>
    public static async Task<bool> RunAsync(IReadOnlyList<string> actions, IReadOnlyList<string> onFail,
        GestureCore.FallbackPolicy fallback, Func<string, Task<bool>> step, Func<bool>? cancelled = null)
    {
        bool success = true, failed = false;
        foreach (var name in actions)
        {
            bool ok;
            try { ok = await step(GestureActions.Canonical(name)); }
            catch { ok = false; }
            if (ok) continue;
            success = false;
            if (!failed)
            {
                failed = true;
                foreach (var failure in onFail)
                    try { await step(GestureActions.Canonical(failure)); } catch { }
            }
            if (cancelled?.Invoke() == true || fallback != GestureCore.FallbackPolicy.SkipStep) break;
        }
        return success;
    }
}
