using System.Buffers.Binary;
using System.IO;

namespace FrameCapture.Capture;

/// <summary>
/// 磁盘暂存帧仓：录制时长上限移除后，帧不再驻留内存，按到达顺序落盘，
/// 编辑器经 <see cref="OpenReader"/> 拿到的惰性列表按需回读。
/// 4K@60fps BGRA≈2GB/s——纯内存必 OOM；NVMe 顺写扛得住这个带宽，且 OS 文件缓存
/// 让"刚录完立刻回读"接近内存速度（回放顺序读同样命中缓存）。
/// 磁盘护栏：每 30 帧查一次剩余空间，低于 <see cref="MinFreeBytes"/> 触发一次
/// <see cref="DiskLow"/>，控制器就地停录——已录内容照常进编辑器，绝不静默丢帧。
/// 记录格式自描述（w/h/timeMs/len + BGRA），崩溃残留可识别；启动时清理过期 spool 文件。
/// </summary>
public sealed class FrameSpool : IDisposable
{
    private readonly object _gate = new();
    private FileStream? _fs;
    private readonly List<FrameSpoolList.Entry> _index = new();
    private long _pos;
    private int _spaceCheckIn = 30;
    private bool _diskLowFired;

    public string Path { get; }
    public const long MinFreeBytes = 1L << 30; // 剩余 <1GB 停录

    /// <summary>剩余空间不足（只触发一次）。参数=当时剩余字节。</summary>
    public event Action<long>? DiskLow;

    public FrameSpool(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FrameCapture",
            $"spool-{DateTime.Now:yyyyMMdd-HHmmss-fff}.fcf");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _fs = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 1 << 21);
    }

    public int Count { get { lock (_gate) return _index.Count; } }
    public long BytesWritten { get { lock (_gate) return _pos; } }

    public void Add(CapturedFrame f)
    {
        lock (_gate)
        {
            if (_fs == null) return;
            Span<byte> hdr = stackalloc byte[20];
            BinaryPrimitives.WriteInt32LittleEndian(hdr[0..4], f.W);
            BinaryPrimitives.WriteInt32LittleEndian(hdr[4..8], f.H);
            BinaryPrimitives.WriteDoubleLittleEndian(hdr[8..16], f.TimeMs);
            BinaryPrimitives.WriteInt32LittleEndian(hdr[16..20], f.Bgra.Length);
            _fs.Write(hdr);
            _fs.Write(f.Bgra, 0, f.Bgra.Length);
            _index.Add(new FrameSpoolList.Entry(_pos + 20, f.TimeMs, f.W, f.H, f.Bgra.Length));
            _pos += 20 + f.Bgra.Length;
            if (!_diskLowFired && --_spaceCheckIn <= 0)
            {
                _spaceCheckIn = 30;
                long free = FreeBytesFor(Path);
                if (free >= 0 && free < MinFreeBytes)
                {
                    _diskLowFired = true;
                    DiskLow?.Invoke(free);
                }
            }
        }
    }

    /// <summary>定格：关写句柄，交还惰性读列表（文件所有权随之转移给列表，Dispose 时删除）。</summary>
    public FrameSpoolList OpenReader()
    {
        lock (_gate)
        {
            // Flush 而非 Flush(true)：spool 是临时文件，回读走同一 OS 文件缓存（新 FileStream 读同路径），
            // 无需 FSync 强制落盘。Flush(true) 会把整段（可达 GB 级）脏页压到物理盘，实测卡 UI 线程 ~1.8s。
            _fs?.Flush();
            _fs?.Dispose();
            _fs = null;
            return new FrameSpoolList(Path, _index.ToArray());
        }
    }

    /// <summary>丢弃（零帧兜底/点按取单帧后）：关句柄并删文件。</summary>
    public void Abandon()
    {
        lock (_gate)
        {
            _fs?.Dispose();
            _fs = null;
        }
        try { File.Delete(Path); } catch { }
    }

    public void Dispose()
    {
        lock (_gate) { _fs?.Dispose(); _fs = null; }
    }

    /// <summary>卷剩余字节；查询失败返回 -1（视为不拦）。</summary>
    public static long FreeBytesFor(string pathForVolume)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(pathForVolume);
            if (string.IsNullOrEmpty(root)) return -1;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : -1;
        }
        catch { return -1; }
    }

    /// <summary>启动清理：删掉 24h 前的 spool 残留（崩溃/强杀留下的）。</summary>
    public static void CleanupStale()
    {
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FrameCapture");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "spool-*.fcf"))
                try { if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-24)) File.Delete(f); } catch { }
        }
        catch { }
    }
}

/// <summary>惰性帧列表：Count/索引器按需从 spool 文件回读单帧（读句柄常驻 + 锁，
/// 后台导出与 UI 拖动可并发）。Slice 共享文件与句柄、只切索引段——导出裁剪零物化。</summary>
public sealed class FrameSpoolList : IReadOnlyList<CapturedFrame>, IDisposable
{
    public readonly record struct Entry(long Offset, double TimeMs, int W, int H, int Len);

    private readonly Entry[] _idx;
    private readonly FileStream _fs;
    private readonly object _gate = new();
    private readonly bool _ownsFile;

    internal FrameSpoolList(string path, Entry[] idx, bool ownsFile = true)
    {
        Path = path; _idx = idx; _ownsFile = ownsFile;
        _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
    }

    public string Path { get; }
    public int Count => _idx.Length;
    /// <summary>帧元数据（时间戳/尺寸），无需回读 BGRA——编辑器 HUD/时间轴高频取用。</summary>
    public Entry Meta(int i) => _idx[i];
    public CapturedFrame this[int i]
    {
        get
        {
            var e = _idx[i];
            var buf = new byte[e.Len];
            lock (_gate)
            {
                _fs.Seek(e.Offset, SeekOrigin.Begin);
                _fs.ReadExactly(buf);
            }
            return new CapturedFrame { W = e.W, H = e.H, TimeMs = e.TimeMs, Bgra = buf };
        }
    }

    public IEnumerator<CapturedFrame> GetEnumerator()
    {
        for (int i = 0; i < _idx.Length; i++) yield return this[i];
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>范围切片（导出用）：不复制帧数据，读时按段索引。</summary>
    public FrameSpoolList Slice(int from, int len)
    {
        from = Math.Clamp(from, 0, Math.Max(0, _idx.Length - 1));
        len = Math.Clamp(len, 0, _idx.Length - from);
        return new FrameSpoolList(Path, _idx[from..(from + len)], ownsFile: false);
    }

    public void Dispose()
    {
        lock (_gate) { _fs.Dispose(); }
        if (_ownsFile) try { File.Delete(Path); } catch { }
    }
}
