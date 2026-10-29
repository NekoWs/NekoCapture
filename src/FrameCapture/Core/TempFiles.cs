using System;
using System.IO;

namespace FrameCapture.Core;

/// <summary>
/// 临时目录的管家：截图/长图/编码过程中的中间文件都落在 <c>%TEMP%\FrameCapture</c>。
/// 这些文件有的只活几秒，有的要留到「继续滚动」用完；没人收拾就会一直涨——
/// 实测一台天天截图的机器上积到两千多个文件、2.6 GB。
/// 启动时清理超过保留期且未被最近产物引用的自家中间文件。</summary>
public static class TempFiles
{
    /// <summary>中间文件的保留期：超过这个时长的自家文件在下一次启动时清掉。</summary>
    /// <value>24 小时。剪贴板与「继续滚动」的寿命都远短于此。</value>
    public static readonly TimeSpan Keep = TimeSpan.FromHours(24);

    /// <summary>中间文件目录（不存在时由调用方创建）。</summary>
    /// <value>绝对路径：<c>%TEMP%\FrameCapture</c>。</value>
    public static string Dir => Path.Combine(Path.GetTempPath(), "FrameCapture");

    /// <summary>拼一个中间文件的完整路径。</summary>
    /// <param name="name">文件名，含扩展名。</param>
    /// <returns>绝对路径；目录不在此创建。</returns>
    public static string PathOf(string name) => Path.Combine(Dir, name);

    /// <summary>删掉一个中间文件；文件不在、被占用、没权限都静默跳过——清理绝不打断任何流程。</summary>
    /// <param name="path">要删的路径，可为 null。</param>
    public static void Drop(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>清掉超过保留期的自家中间文件。只认自家的几种前缀，别人的东西一个都不碰。</summary>
    /// <param name="retained">仍被最近产物引用的完整路径。</param>
    /// <returns>删掉的文件数。</returns>
    public static int SweepStale(IEnumerable<string>? retained = null)
    {
        int removed = 0;
        var keep = new HashSet<string>(retained ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(Dir)) return 0;
            var limit = DateTime.UtcNow - Keep;
            foreach (var f in Directory.EnumerateFiles(Dir))
            {
                if (!IsOwn(Path.GetFileName(f))) continue;
                if (keep.Contains(f)) continue;
                try
                {
                    if (File.GetLastWriteTimeUtc(f) >= limit) continue;
                    File.Delete(f);
                    removed++;
                }
                catch { }
            }
        }
        catch { }
        if (removed > 0) Trace.Log($"temp sweep removed {removed}");
        return removed;
    }

    /// <summary>这个文件名是不是本程序写的中间文件。</summary>
    /// <param name="name">纯文件名。</param>
    /// <returns>认得的前缀返回 true。</returns>
    static bool IsOwn(string name) =>
        name.StartsWith("capture-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("scroll-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("afterimage-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("fc_autosave_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("spool-", StringComparison.OrdinalIgnoreCase)
        || name.Equals("scrollpreview.png", StringComparison.OrdinalIgnoreCase)
        || name.Equals("wincap.png", StringComparison.OrdinalIgnoreCase)
        || name.Equals("freezetest.png", StringComparison.OrdinalIgnoreCase);
}
