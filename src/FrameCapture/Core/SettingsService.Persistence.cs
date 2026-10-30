using System.IO;
using System.Text.Json;

namespace FrameCapture.Core;

/// <summary>设置快照的串行落盘；文件访问不持有内存设置锁。</summary>
public sealed partial class SettingsService
{
    private readonly object _writeGate = new();
    private long _saveRevision;

    private (string Json, long Revision) PrepareSave()
    {
        _current.Normalize();
        long revision = ++_saveRevision;
        try { return (JsonSerializer.Serialize(_current, JsonOpts), revision); }
        catch (Exception ex) { Trace.Log("settings snapshot: " + ex.Message); return ("", revision); }
    }

    private void SaveSnapshot((string Json, long Revision) snapshot)
    {
        if (snapshot.Json.Length == 0) return;
        try
        {
            lock (_writeGate)
            {
                if (snapshot.Revision != Volatile.Read(ref _saveRevision)) return;
                lock (_gate) _selfWriteUtc = DateTime.UtcNow;
                Directory.CreateDirectory(Dir);
                WriteAtomicJson(snapshot.Json);
                lock (_gate) _selfWriteUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex) { Trace.Log("settings save: " + ex.Message); }
    }

    /// <summary>在界面线程更新设置快照，后台顺序保存后通知界面；写盘失败保留内存值。</summary>
    /// <param name="mutate">同步应用的设置修改，不得执行文件操作。</param>
    /// <returns>本次保存尝试与界面通知的完成任务。</returns>
    internal async Task MutateAsync(Action<AppSettings> mutate)
    {
        (string Json, long Revision) snapshot;
        lock (_gate) { mutate(_current); snapshot = PrepareSave(); }
        await Services.BackgroundWork.Run(() => { SaveSnapshot(snapshot); return true; });
        Changed?.Invoke();
    }
}
