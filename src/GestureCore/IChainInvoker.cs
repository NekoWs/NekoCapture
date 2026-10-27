namespace GestureCore;

/// <summary>接管整条绑定的宿主，负责异步步骤与失败策略。</summary>
public interface IChainInvoker : IActionInvoker
{
    /// <summary>受理一条完整绑定。调用发生在输入回调内，耗时工作必须投递。</summary>
    /// <param name="binding">引擎匹配到的绑定快照。</param>
    /// <returns>是否受理；false 时引擎可继续尝试 NextBinding。</returns>
    bool InvokeChain(GestureBinding binding);
}
