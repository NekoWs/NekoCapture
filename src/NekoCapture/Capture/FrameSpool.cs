using System.Buffers.Binary;
using System.IO;

namespace NekoCapture.Capture;

/// <summary>
/// 磁盘暂存帧仓：录制无时长上限，帧按到达顺序落盘、不驻留内存，
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

    /// <summary>spool 文件路径，默认落在 %TEMP%\NekoCapture\spool-yyyyMMdd-HHmmss-fff.fcf。</summary>
    /// <value>构造后固定不变；文件的删除责任随 <see cref="OpenReader"/> 或 <see cref="Abandon"/> 转移。</value>
    public string Path { get; }
    public const long MinFreeBytes = 1L << 30; // 剩余 <1GB 停录

    /// <summary>剩余空间跌破 <see cref="MinFreeBytes"/> 时触发，每条录制至多一次。
    /// 在 <see cref="Add"/> 的调用线程（捕获线程）上同步触发，回调方须自己切回 UI 线程。</summary>
    /// <value>回调参数 = 触发时查到的卷剩余字节数。</value>
    public event Action<long>? DiskLow;

    /// <summary>创建 spool 文件并准备顺序写（已存在则覆盖）。写句柄以 FileShare.ReadWrite 打开，
    /// 让同路径的惰性读列表可以立刻打开。</summary>
    /// <param name="path">目标文件路径；null = 落到 %TEMP%\NekoCapture 下按当前时间命名的唯一文件。</param>
    public FrameSpool(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NekoCapture",
            $"spool-{DateTime.Now:yyyyMMdd-HHmmss-fff}.fcf");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _fs = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 1 << 21);
    }

    /// <summary>已写入的帧数。</summary>
    /// <value>持锁读，线程安全；定格（<see cref="OpenReader"/>）或丢弃之后计数固定。</value>
    public int Count { get { lock (_gate) return _index.Count; } }
    /// <summary>已写入的文件字节数，含每帧 20 字节头。</summary>
    /// <value>持锁读，线程安全。</value>
    public long BytesWritten { get { lock (_gate) return _pos; } }

    /// <summary>顺序追加一帧：20 字节小端头（w/h/timeMs/len）+ BGRA 裸数据，并登记一条索引。
    /// 由捕获线程高频调用，全部动作在 <c>_gate</c> 内完成；定格或丢弃之后调用静默无效。</summary>
    /// <param name="f">帧。<c>Bgra</c> 同步整块写出、不被持有，调用方写回后可复用该数组。</param>
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
    /// <returns>与此刻索引一致的只读帧列表；一帧未写时返回空列表而不是抛，调用方可安全枚举。</returns>
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

    /// <summary>丢弃整条暂存：关句柄并删文件。用于起录后端启动失败——产物根本不会有人来读，文件必须当场消失。</summary>
    public void Abandon()
    {
        lock (_gate)
        {
            _fs?.Dispose();
            _fs = null;
        }
        try { File.Delete(Path); } catch { }
    }

    /// <summary>只关写句柄，不删文件：已定格的文件归 <see cref="FrameSpoolList"/> 删，
    /// 未定格的残留交给启动时的 <see cref="CleanupStale"/>。</summary>
    public void Dispose()
    {
        lock (_gate) { _fs?.Dispose(); _fs = null; }
    }

    /// <summary>卷剩余字节；查询失败返回 -1（视为不拦）。</summary>
    /// <param name="pathForVolume">该卷上的任意路径，只用它取根（盘符）来定位卷。</param>
    /// <returns>可用字节数；路径无根、卷未就绪或查询抛异常时返回 -1——磁盘护栏按「查不到就不拦」处理。</returns>
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

    /// <summary>启动时清理早于 24h 且未被最近产物引用的 spool 文件。</summary>
    /// <param name="retained">最近录制仍引用的完整路径。</param>
    public static void CleanupStale(IEnumerable<string>? retained = null)
    {
        try
        {
            var keep = new HashSet<string>(retained ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NekoCapture");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "spool-*.fcf"))
                try { if (!keep.Contains(f) && File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-24)) File.Delete(f); } catch { }
        }
        catch { }
    }
}

/// <summary>惰性帧列表：Count/索引器按需从 spool 文件回读单帧（读句柄常驻 + 锁，
/// 后台导出与 UI 拖动可并发）。Slice 共享文件并使用独立读取句柄，只复制索引段。</summary>
public sealed class FrameSpoolList : IReadOnlyList<CapturedFrame>, IDisposable
{
    /// <summary>一帧在 spool 文件里的定位与元数据：写帧时同步登记，回读时按它 Seek。</summary>
    /// <param name="Offset">帧 BGRA 数据在文件中的字节偏移（已跳过 20 字节头）。</param>
    /// <param name="TimeMs">相对捕获开始的毫秒时间戳。</param>
    /// <param name="W">帧宽，物理像素。</param>
    /// <param name="H">帧高，物理像素。</param>
    /// <param name="Len">BGRA 字节数，恒等于 W*H*4。</param>
    /// <returns>按给定值构造的不可变记录；登记进索引后取值固定。</returns>
    public readonly record struct Entry(long Offset, double TimeMs, int W, int H, int Len);

    private readonly Entry[] _idx;
    private readonly FileStream _fs;
    private readonly object _gate = new();
    private bool _ownsFile;

    /// <summary>导出帧元数据快照，不读取像素。</summary>
    /// <returns>独立的帧索引数组。</returns>
    internal Entry[] ExportIndex() => (Entry[])_idx.Clone();

    /// <summary>由持久化会话接管文件删除责任，当前列表只关闭句柄。</summary>
    internal void RetainFile() { lock (_gate) _ownsFile = false; }

    /// <summary>打开 spool 文件的只读视图；<paramref name="ownsFile"/> 决定谁负责删文件。</summary>
    /// <param name="path">spool 文件路径，必须已存在（定格后才打开）。</param>
    /// <param name="idx">按到达顺序排列的帧索引；切片传的是原索引的一个连续片段。</param>
    /// <param name="ownsFile">true = <see cref="Dispose"/> 时删除该文件。切片必须传 false，
    /// 否则释放一份切片就会删掉全部帧的数据。</param>
    internal FrameSpoolList(string path, Entry[] idx, bool ownsFile = true)
    {
        Path = path; _idx = idx; _ownsFile = ownsFile;
        _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
    }

    /// <summary>spool 文件路径；切片与母列表指向同一个文件。</summary>
    /// <value>构造时给定，固定不变；<see cref="Dispose"/> 之后该文件可能已不存在。</value>
    public string Path { get; }
    /// <summary>本视图索引里的帧数。</summary>
    /// <value>切片只数自己那一段，不等于文件里的总帧数；枚举与索引器都以它为界。</value>
    public int Count => _idx.Length;
    /// <summary>帧元数据（时间戳/尺寸），无需回读 BGRA——编辑器 HUD/时间轴高频取用。</summary>
    /// <param name="i">帧序号，0 到 <see cref="Count"/>-1；越界直接抛 <c>IndexOutOfRangeException</c>，不做钳制。</param>
    /// <returns>索引项（readonly struct 副本），含该帧在文件里的字节偏移。</returns>
    public Entry Meta(int i) => _idx[i];
    /// <summary>按需回读第 i 帧：Seek 到该帧偏移后整块读出 BGRA，不缓存、不预读。</summary>
    /// <param name="i">帧序号，0 到 <see cref="Count"/>-1。</param>
    /// <value>每次取都新分配一份像素数组；读在锁内，后台导出与 UI 拖动可并发取不同帧。</value>
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

    /// <summary>顺序逐帧回读（每帧一次 Seek+Read），全程不物化整段视频。</summary>
    /// <returns>惰性迭代器：每次 MoveNext 才读一帧；列表中途被 Dispose，后续读取会抛异常。</returns>
    public IEnumerator<CapturedFrame> GetEnumerator()
    {
        for (int i = 0; i < _idx.Length; i++) yield return this[i];
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>范围切片（导出用）：不复制帧数据，读时按段索引。</summary>
    /// <param name="from">起始帧序号，越界被钳到 [0, Count-1]；空索引时钳到 0。</param>
    /// <param name="len">帧数，与 <paramref name="from"/> 一起钳到不超过 <see cref="Count"/>。</param>
    /// <returns>持有独立读取句柄的切片，自身不删文件；母列表释放后，已打开的切片仍可读。</returns>
    public FrameSpoolList Slice(int from, int len)
    {
        from = Math.Clamp(from, 0, Math.Max(0, _idx.Length - 1));
        len = Math.Clamp(len, 0, _idx.Length - from);
        return new FrameSpoolList(Path, _idx[from..(from + len)], ownsFile: false);
    }

    /// <summary>关读句柄；只有持有文件所有权的那一份（定格出来的列表，非切片）才删 spool 文件。</summary>
    public void Dispose()
    {
        lock (_gate) { _fs.Dispose(); }
        if (_ownsFile) try { File.Delete(Path); } catch { }
    }
}
