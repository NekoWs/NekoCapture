namespace FrameCapture.Core;

/// <summary>Esc 的分层语义（SelfTest /）：导出中=取消导出；有选中=先取消选中；绘制态=先退出工具（绝不因误触丢整次捕获）；浏览态=关闭窗口。</summary>
public enum EscAction { CancelEncode, ClearSelection, ExitTool, Close }

public static class EscStep
{
    public static EscAction Select(bool encoding, bool inToolMode, bool hasSelection = false)
        => encoding ? EscAction.CancelEncode : hasSelection ? EscAction.ClearSelection : inToolMode ? EscAction.ExitTool : EscAction.Close;
}
