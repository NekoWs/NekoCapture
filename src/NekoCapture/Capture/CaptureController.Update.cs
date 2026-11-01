namespace NekoCapture.Services;

/// <summary>安装交接占用空闲控制器，阻止启动新的捕获和编辑任务。</summary>
public sealed partial class CaptureController
{
    private bool _updateReserved;

    /// <summary>更新器已预留安装交接。</summary>
    internal bool UpdateReserved => _updateReserved;

    /// <summary>仅在没有动作和捕获会话时预留安装交接。</summary>
    /// <returns>是否获得独占交接。</returns>
    internal bool TryReserveUpdate()
    {
        if (_updateReserved || !CanSuspend) return false;
        return _updateReserved = true;
    }

    /// <summary>安装失败或取消授权后恢复捕获入口。</summary>
    internal void ReleaseUpdate() => _updateReserved = false;
}
