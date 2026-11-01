namespace NekoCapture.Core;

/// <summary>
/// 轻量文件跟踪日志（开发期常开）。落点是程序目录向上五级，主程序落在 src\，
/// SelfTest 落在 tools\，都在工作区里好找；写失败（目录只读、文件被占）静默忽略，绝不影响功能。
/// </summary>
public static class Trace
{
    /// <summary>跟踪日志落点；程序不是从仓库里的构建输出运行时为 null，那时一个字都不写。</summary>
    static readonly string? Path = ResolveFromEnv() ?? Resolve();

    /// <summary>环境变量 <c>NC_TRACE_FILE</c> 给出的日志路径（诊断用）；没设或目录为空时为 null。</summary>
    /// <returns>可写的日志路径，或 null。</returns>
    static string? ResolveFromEnv()
    {
        var p = Environment.GetEnvironmentVariable("NC_TRACE_FILE");
        if (string.IsNullOrWhiteSpace(p)) return null;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(p)) ?? "";
            return dir.Length <= 3 ? null : System.IO.Path.GetFullPath(p);
        }
        catch { return null; }
    }

    /// <summary>从程序目录向上五级取日志落点。</summary>
    /// <returns>日志路径；程序装在仓库构建输出之外时为 null。</returns>
    /// <remarks>装进 Program Files 或数据盘之后，向上五级会走到盘根：那里要么写不进去，
    /// 要么在盘根长出一个没人认领、也没人清的日志文件。两种都不如不写。</remarks>
    static string? Resolve()
    {
        var p = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "nc-trace.log"));
        var dir = System.IO.Path.GetDirectoryName(p) ?? "";
        // 目录名短到只剩盘符，说明五级已经越过仓库根走到卷上
        return dir.Length <= 3 ? null : p;
    }

    /// <summary>追加一行带毫秒时间戳的跟踪记录。</summary>
    /// <param name="msg">日志正文；时间戳与换行由本方法负责，调用方不必自带。</param>
    public static void Log(string msg)
    {
        var file = Path;
        if (file == null) return;
        try
        {
            System.IO.File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }
}
