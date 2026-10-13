namespace FrameCapture.Core;

/// <summary>
/// 轻量文件跟踪日志（开发期常开，写入工作区便于诊断）。
/// 路径相对程序目录向上五级定位到仓库根；写失败（目录只读、文件被占）静默忽略，绝不影响功能。
/// </summary>
public static class Trace
{
    static readonly string Path = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fc-trace.log"));

    /// <summary>追加一行带毫秒时间戳的跟踪记录。</summary>
    /// <param name="msg">日志正文；时间戳与换行由本方法负责，调用方不必自带。</param>
    public static void Log(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }
}
