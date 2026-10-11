namespace FrameCapture.Core;

/// <summary>Esc 的分层语义（SelfTest T82）：导出中=取消导出；绘制态=先退出工具（绝不因误触丢整次捕获）；浏览态=关闭窗口。</summary>
public enum EscAction { CancelEncode, ExitTool, Close }

public static class EscStep
{
    public static EscAction Select(bool encoding, bool inToolMode)
        => encoding ? EscAction.CancelEncode : inToolMode ? EscAction.ExitTool : EscAction.Close;
}
