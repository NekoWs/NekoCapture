namespace FrameCapture.Services;

/// <summary>登录入口登记的结果。</summary>
public enum StartupResult
{
    /// <summary>自启入口已移除。</summary>
    Off,
    /// <summary>普通用户自启项已登记。</summary>
    RunKey,
    /// <summary>当前用户的最高权限登录任务已登记。</summary>
    Task,
    /// <summary>用户取消了授权。</summary>
    Cancelled,
    /// <summary>登记失败，设置保持当前值。</summary>
    Failed,
}
