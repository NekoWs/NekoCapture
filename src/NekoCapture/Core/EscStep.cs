namespace NekoCapture.Core;

/// <summary>
/// Esc 的分层语义（SelfTest 覆盖）：导出中=取消导出；有选中=先取消选中；
/// 绘制态=先退出工具（绝不因误触丢整次捕获）；浏览态=关闭窗口。
/// </summary>
public enum EscAction { CancelEncode, ClearSelection, ExitTool, Close }

/// <summary>Esc 分层语义的决策表：把编辑器当前状态映射成唯一动作，UI 只执行结果。</summary>
public static class EscStep
{
    /// <summary>决定这一次 Esc 该做什么。</summary>
    /// <param name="encoding">是否正在导出（最高优先：Esc 取消导出）。</param>
    /// <param name="inToolMode">是否处于绘制态（某个绘制工具被选中）。</param>
    /// <param name="hasSelection">是否有选中标注。</param>
    /// <returns>优先级 导出 &gt; 取消选中 &gt; 退出工具 &gt; 关窗：选中优先于工具，
    /// 一次 Esc 只吃掉一层，不会同时丢选中和工具。</returns>
    public static EscAction Select(bool encoding, bool inToolMode, bool hasSelection = false)
        => encoding ? EscAction.CancelEncode : hasSelection ? EscAction.ClearSelection : inToolMode ? EscAction.ExitTool : EscAction.Close;
}
