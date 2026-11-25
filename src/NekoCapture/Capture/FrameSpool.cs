using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using NekoCapture.Core;

namespace NekoCapture.Capture;

/// <summary>
/// 磁盘暂存帧仓：录制无时长上限，帧按到达顺序落盘、不驻留内存，
/// 编辑器经 <see cref="OpenReader"/> 拿到的惰性列表按需回读。
/// 4K@60fps BGRA≈2GB/s——纯内存必 OOM；NVMe 顺写扛得住这个带宽，且 OS 文件缓存
/// 让"刚录完立刻回读"接近内存速度（回放顺序读同样命中缓存）。
/// 帧以 Brotli 快速档压缩后落盘（屏幕内容通常 20–30 倍），压缩收益不足 3% 的帧原样存：
/// 4K 一分钟从几十 GB 降到百 MB 级，回读时解压比省下的磁盘 I/O 更便宜。
/// 压缩与写盘在独立后台线程串行执行，捕获线程只做一次拷贝并入有界队列——
/// 编码器再慢也不会把帧堆在内存里。写线程跟不上捕获节奏且收益有限时暂停压缩、原样落盘，
/// 收益够高则降到快速档继续压缩，避免原始帧把磁盘写满。
/// 磁盘护栏：每 30 帧查一次剩余空间，低于 <see cref="MinFreeBytes"/> 触发一次
/// <see cref="DiskLow"/>，控制器就地停录——已录内容照常进编辑器，绝不静默丢帧。
/// 文件带格式魔数；记录格式自描述（w/h/time/长度 + 压缩标志），崩溃残留可识别；
/// 启动时清理过期 spool 文件。
/// </summary>
public sealed class FrameSpool : IDisposable
{
    /// <summary>帧仓文件魔数：本代布局（逐帧头 + 可选 Brotli 数据）的识别标记。</summary>
    internal static ReadOnlySpan<byte> Magic => "FCF2"u8;

    /// <summary>帧仓布局版本，写进常驻会话；读会话时版本不符就整条丢弃。</summary>
    internal const int FormatVersion = 2;

    /// <summary>每帧头字节数：w、h、时间戳、原始字节数、落盘字节数、压缩标志。</summary>
    internal const int HeaderBytes = 28;

    /// <summary>压缩收益门槛的分母：落盘字节数至少比原始少 1/32 才用压缩版本。</summary>
    private const int MinSavingDivisor = 32;

    /// <summary>帧间隔未知时的压缩耗时预算（毫秒）；已知时按帧间隔的八成计算，写线程要比捕获快才不产生背压。</summary>
    private const int CompressBudgetMs = 30;

    /// <summary>压缩超预算或收益不足后，原样落盘多少帧再试一次。</summary>
    private const int CompressionSkipFrames = 30;

    /// <summary>退避上限（帧）：画面一直难压时，最多每 600 帧试一次，别把时间浪费在无望的压缩上。</summary>
    private const int CompressionSkipCap = 600;

    /// <summary>「慢但省得多」的收益门槛：压缩耗时超预算、但落盘收益至少这么多倍时，宁可降档继续压缩，
    /// 也不退回原始落盘——4K 原始帧每秒可达 1–2 GB，把磁盘写满比帧率低一点更糟。</summary>
    private const int SlowCompressionRatio = 4;

    /// <summary>写线程队列深度；满时捕获线程短暂阻塞（等价于原来的同步写盘背压）。</summary>
    private const int QueueDepth = 2;

    private readonly object _gate = new();
    private readonly object _poolGate = new();
    private readonly Stack<byte[]> _freeBuffers = new();
    private readonly List<FrameSpoolList.Entry> _index = new();
    private readonly BlockingCollection<Job> _queue = new(QueueDepth);
    private readonly Thread _writer;
    private FileStream? _fs;
    private long _pos;
    private int _spaceCheckIn = 30;
    private bool _diskLowFired;
    private bool _faulted;
    private volatile bool _abandoned;
    private int _finished;

    /// <summary>spool 文件路径，默认落在 %TEMP%\NekoCapture\spool-yyyyMMdd-HHmmss-fff.fcf。</summary>
    /// <value>构造后固定不变；文件的删除责任随 <see cref="OpenReader"/> 或 <see cref="Abandon"/> 转移。</value>
    public string Path { get; }

    /// <summary>剩余空间跌破 <see cref="MinFreeBytes"/> 时触发，每条录制至多一次。
    /// 写线程自己发现写盘失败时也用它通知调用方收尾（参数为 -1）。</summary>
    /// <value>回调参数 = 触发时查到的卷剩余字节数。</value>
    public event Action<long>? DiskLow;

    /// <summary>创建 spool 文件并启动后台写线程（已存在则覆盖）。文件头先写格式魔数；
    /// 写句柄以 FileShare.ReadWrite 打开，让同路径的惰性读列表可以立刻打开。</summary>
    /// <param name="path">目标文件路径；null = 落到 %TEMP%\NekoCapture 下按当前时间命名的唯一文件。</param>
    public FrameSpool(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NekoCapture",
            $"spool-{DateTime.Now:yyyyMMdd-HHmmss-fff}.fcf");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _fs = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 1 << 21);
        _fs.Write(Magic);
        _pos = Magic.Length;
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Frame spool", Priority = ThreadPriority.BelowNormal };
        _writer.Start();
    }

    /// <summary>已写入文件的帧数。</summary>
    /// <value>持锁读，线程安全；后台写线程可能比捕获慢几帧，定格之后计数固定。</value>
    public int Count { get { lock (_gate) return _index.Count; } }

    /// <summary>已写入文件的字节数（压缩后，含每帧头与文件魔数）。</summary>
    /// <value>持锁读，线程安全。</value>
    public long BytesWritten { get { lock (_gate) return _pos; } }

    /// <summary>把一帧交给后台写线程：拷贝像素进池化缓冲后入有界队列，压缩与写盘都不占捕获线程。
    /// 队列满时短暂阻塞，形成与磁盘写速匹配的背压；定格或丢弃之后调用静默无效。</summary>
    /// <param name="f">帧。<c>Bgra</c> 在本方法返回后即可复用，后台线程持有的是拷贝。</param>
    public void Add(CapturedFrame f)
    {
        if (_faulted || _abandoned || Volatile.Read(ref _finished) != 0) return;
        int length = f.Bgra.Length;
        if (length <= 0) return;
        byte[] buffer = RentBuffer(length);
        Buffer.BlockCopy(f.Bgra, 0, buffer, 0, length);
        try { _queue.Add(new Job(buffer, length, f.W, f.H, f.TimeMs)); }
        catch (InvalidOperationException) { ReturnBuffer(buffer); }
    }

    /// <summary>定格：等后台写线程把队列写完并关写句柄，交还惰性读列表（文件所有权随之转移给列表，Dispose 时删除）。</summary>
    /// <returns>与此刻索引一致的只读帧列表；一帧未写时返回空列表而不是抛，调用方可安全枚举。</returns>
    public FrameSpoolList OpenReader()
    {
        FinishWriter();
        lock (_gate) return new FrameSpoolList(Path, _index.ToArray());
    }

    /// <summary>丢弃整条暂存：停写线程、关句柄并删文件。用于起录后端启动失败——产物根本不会有人来读，文件必须当场消失。</summary>
    public void Abandon()
    {
        _abandoned = true;
        FinishWriter();
        try { File.Delete(Path); } catch { }
    }

    /// <summary>只关写句柄，不删文件：已定格的文件归 <see cref="FrameSpoolList"/> 删，
    /// 未定格的残留交给启动时的 <see cref="CleanupStale"/>。</summary>
    public void Dispose() => FinishWriter();

    /// <summary>停写线程并关句柄；可重复调用。</summary>
    private void FinishWriter()
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { }
        try { _writer.Join(); } catch (Exception error) { Trace.Log("frame spool join: " + error); }
        lock (_gate)
        {
            // 磁盘写满时最后几帧可能没真正落盘：记一条 trace，别把失败吞得无迹可查。
            try { _fs?.Flush(); } catch (Exception error) { Trace.Log("frame spool flush: " + error); }
            try { _fs?.Dispose(); } catch (Exception error) { Trace.Log("frame spool close: " + error); }
            _fs = null;
        }
        lock (_poolGate) _freeBuffers.Clear();
    }

    /// <summary>租一块不小于给定长度的精确缓冲：优先复用写线程归还的同类块，否则按需分配。
    /// 不用共享 ArrayPool——它按 2 的幂分桶，33MB 的 4K 帧会被租成 64MB，录制时白占一倍内存。</summary>
    /// <param name="length">需要的字节数。</param>
    /// <returns>长度不小于 <paramref name="length"/> 的缓冲。</returns>
    private byte[] RentBuffer(int length)
    {
        lock (_poolGate)
        {
            while (_freeBuffers.Count > 0)
            {
                var buffer = _freeBuffers.Pop();
                if (buffer.Length >= length) return buffer;
            }
        }
        return GC.AllocateUninitializedArray<byte>(length);
    }

    /// <summary>归还写线程用完的帧缓冲；只留少量同类块，避免换分辨率后旧尺寸长期占内存。</summary>
    /// <param name="buffer">要归还的缓冲。</param>
    private void ReturnBuffer(byte[] buffer)
    {
        lock (_poolGate)
        {
            if (_freeBuffers.Count < QueueDepth + 1) _freeBuffers.Push(buffer);
        }
    }

    /// <summary>后台写线程：逐帧压缩、写头、写数据并登记索引；任何失败只触发一次 <see cref="DiskLow"/> 并停止。</summary>
    private void WriteLoop()
    {
        byte[]? scratch = null;
        int skipCompression = 0;
        int backoff = CompressionSkipFrames;
        int written = 0;
        double intervalEma = 0, lastTimeMs = 0;
        bool fastTier = false, compressionPaused = false;
        Span<byte> header = stackalloc byte[HeaderBytes];
        try
        {
            foreach (var job in _queue.GetConsumingEnumerable())
            {
                if (_abandoned)
                {
                    ReturnBuffer(job.Buffer);
                    continue;
                }
                int stored = job.Length, flags = 0;
                if (job.TimeMs > lastTimeMs && lastTimeMs > 0)
                {
                    double interval = job.TimeMs - lastTimeMs;
                    if (interval < 10000)
                        intervalEma = intervalEma <= 0 ? interval : intervalEma * 0.7 + interval * 0.3;
                }
                lastTimeMs = job.TimeMs;
                if (skipCompression > 0) skipCompression--;
                else
                {
                    var compressClock = System.Diagnostics.Stopwatch.StartNew();
                    bool saved = false;
                    // 快速档期间每 300 帧用标准档试一次，画面变简单时自动回到收益更高的档位。
                    int quality = fastTier && written % 300 != 0 ? 0 : 1;
                    try
                    {
                        int capacity = BrotliEncoder.GetMaxCompressedLength(job.Length);
                        if (scratch == null || scratch.Length < capacity)
                            scratch = GC.AllocateUninitializedArray<byte>(capacity);
                        saved = BrotliEncoder.TryCompress(job.Buffer.AsSpan(0, job.Length), scratch, out int packed,
                                quality, 22)
                            && packed < job.Length - job.Length / MinSavingDivisor;
                        if (saved) { stored = packed; flags = 1; }
                    }
                    catch (Exception error) { Trace.Log("frame spool compress: " + error); stored = job.Length; flags = 0; }
                    compressClock.Stop();
                    double cost = compressClock.Elapsed.TotalMilliseconds;
                    // 写线程得比捕获快才不会背压：帧间隔未知时用固定预算，已知时留两成余量。
                    double budget = intervalEma > 0 ? intervalEma * 0.8 : CompressBudgetMs;
                    int ratio = saved ? (int)Math.Min(int.MaxValue, job.Length / (long)Math.Max(1, stored)) : 1;
                    // 头几帧的耗时含 Brotli 首次 JIT，不参与判定。
                    if (!saved)
                    {
                        skipCompression = backoff;
                        backoff = Math.Min(backoff * 2, CompressionSkipCap);
                        fastTier = false;
                        Paused("compress-failed");
                    }
                    else if (written < 3 || cost <= budget)
                    {
                        backoff = CompressionSkipFrames;
                        if (quality == 1) fastTier = false;
                        Resumed();
                    }
                    else if (ratio >= SlowCompressionRatio)
                    {
                        // 慢但省得多：降到快速档继续压，宁可帧率略低也不退回原始落盘把磁盘写满。
                        if (!fastTier) fastTier = true;
                        backoff = CompressionSkipFrames;
                        Resumed();
                    }
                    else
                    {
                        skipCompression = backoff;
                        backoff = Math.Min(backoff * 2, CompressionSkipCap);
                        fastTier = false;
                        Paused("slow-incompressible");
                    }

                    void Paused(string reason)
                    {
                        if (compressionPaused) return;
                        compressionPaused = true;
                        Trace.Log($"frame spool compression paused: reason={reason} saved={saved} cost={cost:0}ms ratio={ratio}x interval={intervalEma:0}ms");
                    }

                    void Resumed()
                    {
                        if (!compressionPaused) return;
                        compressionPaused = false;
                        Trace.Log($"frame spool compression resumed: cost={cost:0}ms ratio={ratio}x");
                    }
                }

                var fs = _fs ?? throw new ObjectDisposedException(nameof(FrameSpool));
                BinaryPrimitives.WriteInt32LittleEndian(header[0..4], job.W);
                BinaryPrimitives.WriteInt32LittleEndian(header[4..8], job.H);
                BinaryPrimitives.WriteDoubleLittleEndian(header[8..16], job.TimeMs);
                BinaryPrimitives.WriteInt32LittleEndian(header[16..20], job.Length);
                BinaryPrimitives.WriteInt32LittleEndian(header[20..24], stored);
                BinaryPrimitives.WriteInt32LittleEndian(header[24..28], flags);
                long dataOffset;
                lock (_gate)
                {
                    dataOffset = _pos + HeaderBytes;
                    fs.Write(header);
                    if (flags == 1) fs.Write(scratch!, 0, stored);
                    else fs.Write(job.Buffer, 0, job.Length);
                    _index.Add(new FrameSpoolList.Entry(dataOffset, job.TimeMs, job.W, job.H, job.Length));
                    _pos = dataOffset + stored;
                }
                ReturnBuffer(job.Buffer);
                written++;

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
        catch (Exception error)
        {
            _faulted = true;
            Trace.Log("frame spool write: " + error);
            if (!_diskLowFired)
            {
                _diskLowFired = true;
                try { DiskLow?.Invoke(-1); } catch { }
            }
        }
        finally { try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { } }
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

    /// <summary>写线程的一帧任务：池化缓冲、有效字节数与帧元数据。</summary>
    /// <param name="Buffer">从 ArrayPool 租到的像素拷贝，写线程负责归还。</param>
    /// <param name="Length">像素字节数（W*H*4）。</param>
    /// <param name="W">帧宽，物理像素。</param>
    /// <param name="H">帧高，物理像素。</param>
    /// <param name="TimeMs">相对捕获开始的毫秒时间戳。</param>
    private readonly record struct Job(byte[] Buffer, int Length, int W, int H, double TimeMs);

    /// <summary>剩余 <1GB 停录。</summary>
    public const long MinFreeBytes = 1L << 30;
}

/// <summary>惰性帧列表：Count/索引器按需从 spool 文件回读单帧（读句柄常驻 + 锁，
/// 后台导出与 UI 拖动可并发）。每帧按头里的压缩标志解压，像素仍是新分配的只读副本。</summary>
public sealed class FrameSpoolList : IReadOnlyList<CapturedFrame>, IDisposable
{
    /// <summary>一帧在 spool 文件里的定位与元数据：写帧时同步登记，回读时按它 Seek。</summary>
    /// <param name="Offset">帧 BGRA 数据在文件中的字节偏移（已跳过 28 字节帧头）。</param>
    /// <param name="TimeMs">相对捕获开始的毫秒时间戳。</param>
    /// <param name="W">帧宽，物理像素。</param>
    /// <param name="H">帧高，物理像素。</param>
    /// <param name="Len">解压后的 BGRA 字节数，恒等于 W*H*4。</param>
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

    /// <summary>打开 spool 文件的只读视图；<paramref name="ownsFile"/> 决定谁负责删文件。
    /// 先校验文件魔数：旧代布局或别的文件一律拒绝，绝不把偏移读成像素。</summary>
    /// <param name="path">spool 文件路径，必须已存在（定格后才打开）。</param>
    /// <param name="idx">按到达顺序排列的帧索引；切片传的是原索引的一个连续片段。</param>
    /// <param name="ownsFile">true = <see cref="Dispose"/> 时删除该文件。切片必须传 false，
    /// 否则释放一份切片就会删掉全部帧的数据。</param>
    internal FrameSpoolList(string path, Entry[] idx, bool ownsFile = true)
    {
        Path = path; _idx = idx; _ownsFile = ownsFile;
        _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
        try
        {
            Span<byte> magic = stackalloc byte[4];
            _fs.ReadExactly(magic);
            if (!magic.SequenceEqual(FrameSpool.Magic)) throw new InvalidDataException("not a frame spool file");
        }
        catch { _fs.Dispose(); throw; }
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

    /// <summary>按需回读第 i 帧：Seek 到该帧头，按压缩标志解压或直读 BGRA，不缓存、不预读。
    /// 磁盘读在锁内、解压在锁外：多个消费线程可以并行解压不同帧（导出预取靠它）。</summary>
    /// <param name="i">帧序号，0 到 <see cref="Count"/>-1。</param>
    /// <value>每次取都新分配一份像素数组；后台导出与 UI 拖动可并发取不同帧。</value>
    public CapturedFrame this[int i]
    {
        get
        {
            var entry = _idx[i];
            var pixels = GC.AllocateUninitializedArray<byte>(entry.Len);
            byte[]? packed;
            int stored;
            lock (_gate) packed = ReadRaw(entry, pixels, out stored);
            if (packed != null) DecompressFrame(packed, stored, pixels);
            return new CapturedFrame { W = entry.W, H = entry.H, TimeMs = entry.TimeMs, Bgra = pixels };
        }
    }

    /// <summary>把第 i 帧读进给定缓冲，不新分配像素：导出预取复用几块缓冲，长录制不再为每帧分配一次 4K 数组。</summary>
    /// <param name="i">帧序号，0 到 <see cref="Count"/>-1。</param>
    /// <param name="destination">目标缓冲，长度须正好等于 <paramref name="length"/>（压缩帧按缓冲长度校验解压结果）。</param>
    /// <param name="length">期望的帧字节数；与索引记录不符时返回 false，由调用方按原路径读取。</param>
    /// <returns>true = 已写入；false = 缓冲长度不符或帧长与期望不符。</returns>
    internal bool TryReadInto(int i, byte[] destination, int length)
    {
        var entry = _idx[i];
        if (entry.Len != length || destination.Length != length) return false;
        byte[]? packed;
        int stored;
        lock (_gate) packed = ReadRaw(entry, destination, out stored);
        if (packed != null) DecompressFrame(packed, stored, destination);
        return true;
    }

    /// <summary>读一帧的落盘数据：先读 28 字节头校验尺寸；压缩帧只取出压缩字节，解压交给锁外做。</summary>
    /// <param name="entry">索引项。</param>
    /// <param name="destination">未压缩帧的目标缓冲，长度至少为解压后字节数。</param>
    /// <param name="storedLength">输出压缩字节数（未压缩帧等于原始字节数）。</param>
    /// <returns>压缩帧返回租来的压缩字节（调用方解压后归还池）；未压缩帧已直接读进目标缓冲并返回 null。</returns>
    private byte[]? ReadRaw(Entry entry, byte[] destination, out int storedLength)
    {
        long headerPos = entry.Offset - FrameSpool.HeaderBytes;
        if (headerPos < 4) throw new InvalidDataException("frame header out of range");
        _fs.Seek(headerPos, SeekOrigin.Begin);
        Span<byte> header = stackalloc byte[FrameSpool.HeaderBytes];
        _fs.ReadExactly(header);
        int rawLength = BinaryPrimitives.ReadInt32LittleEndian(header[16..20]);
        int stored = BinaryPrimitives.ReadInt32LittleEndian(header[20..24]);
        int flags = BinaryPrimitives.ReadInt32LittleEndian(header[24..28]);
        if (flags is not (0 or 1)) throw new InvalidDataException("unknown frame compression flag");
        if (rawLength != entry.Len || stored <= 0 || stored > rawLength + (1 << 20))
            throw new InvalidDataException("frame header mismatch");
        storedLength = stored;
        if (flags == 0)
        {
            if (stored != rawLength) throw new InvalidDataException("raw frame length mismatch");
            _fs.ReadExactly(destination, 0, rawLength);
            return null;
        }
        var packed = GC.AllocateUninitializedArray<byte>(stored);
        _fs.ReadExactly(packed, 0, stored);
        return packed;
    }

    /// <summary>把压缩帧解到目标缓冲，并在任何路径归还压缩字节的池租约。</summary>
    /// <param name="packed">压缩字节。</param>
    /// <param name="stored">压缩字节数。</param>
    /// <param name="destination">解压目标，长度须等于原始字节数。</param>
    private static void DecompressFrame(byte[] packed, int stored, byte[] destination)
    {
        if (!BrotliDecoder.TryDecompress(packed.AsSpan(0, stored), destination, out int written)
            || written != destination.Length)
            throw new InvalidDataException("frame decompression failed");
    }

    /// <summary>顺序逐帧回读（每帧一次 Seek+解压），全程不物化整段视频。</summary>
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
