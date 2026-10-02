namespace FrameCapture.Core;

/// <summary>轻量文件跟踪日志（开发期常开，写入工作区便于诊断）。</summary>
public static class Trace
{
    static readonly string Path = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fc-trace.log"));

    public static void Log(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }
}
