namespace FrameCapture.Encoding;

/// <summary>独立组件的下载字节数、速度及校验状态。</summary>
internal readonly struct ComponentProgress
{
    /// <summary>创建组件进度快照。</summary>
    /// <param name="bytes">已接收且保留的字节数。</param>
    /// <param name="speed">每秒接收字节数。</param>
    /// <param name="verifying">是否正在校验或测试编码。</param>
    internal ComponentProgress(long bytes, double speed, bool verifying = false)
    { Bytes = bytes; Speed = speed; Verifying = verifying; }

    /// <summary>已接收字节数。</summary>
    internal long Bytes { get; }
    /// <summary>下载速度，单位字节每秒。</summary>
    internal double Speed { get; }
    /// <summary>下载完成后正在校验或测试编码。</summary>
    internal bool Verifying { get; }
    /// <summary>下载百分比。</summary>
    internal double Percent => Math.Min(100, Bytes * 100d / FfmpegComponentInfo.DownloadBytes);
}
