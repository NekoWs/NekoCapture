namespace NekoCapture.Updates;

/// <summary>检查与安装事务的可见状态。</summary>
internal enum UpdateStage
{
    Idle, Checking, Current, Available, CheckFailed, Downloading, Waiting, Installing, Cancelling, Cancelled, Skipped, Failed,
}
