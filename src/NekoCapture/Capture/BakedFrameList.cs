using System.Collections;
using NekoCapture.Core;
using NekoCapture.Drawing;

namespace NekoCapture.Capture;

/// <summary>按索引烘焙并裁剪一帧；独立租约共享只读快照，最后一个租约释放帧源。</summary>
internal sealed class BakedFrameList : IReadOnlyList<CapturedFrame>, IDisposable
{
    readonly State _state;
    readonly int _start;
    readonly int _count;
    bool _disposed;

    /// <summary>所有结果租约共用的不可变处理参数及源租约。</summary>
    sealed class State
    {
        /// <summary>由本状态持有的源范围。</summary>
        internal readonly IReadOnlyList<CapturedFrame> Source;
        /// <summary>只读标注快照。</summary>
        internal readonly IReadOnlyList<Annotation> Annotations;
        /// <summary>像素裁剪框。</summary>
        internal readonly RectI? Crop;
        /// <summary>租约及正在读取的操作总数，由状态锁保护。</summary>
        internal int Readers = 1;

        /// <summary>接管源范围及处理参数。</summary>
        /// <param name="source">独立源租约。</param>
        /// <param name="annotations">只读标注快照。</param>
        /// <param name="crop">像素裁剪框。</param>
        internal State(IReadOnlyList<CapturedFrame> source, IReadOnlyList<Annotation> annotations, RectI? crop)
        { Source = source; Annotations = annotations; Crop = crop; }
    }

    BakedFrameList(State state, int start, int count)
    { _state = state; _start = start; _count = count; }

    /// <summary>取得独立的源范围，并固定当前裁剪和标注。</summary>
    /// <param name="source">只读源帧。</param>
    /// <param name="start">范围起点。</param>
    /// <param name="count">范围长度。</param>
    /// <param name="annotations">创建时深复制的标注。</param>
    /// <param name="crop">源帧内的像素裁剪框；空值保留原尺寸。</param>
    /// <returns>由调用方释放的惰性帧列表。</returns>
    internal static BakedFrameList Create(IReadOnlyList<CapturedFrame> source, int start, int count,
        IReadOnlyList<Annotation> annotations, RectI? crop)
    {
        var snapshot = annotations.Select(a => a.WithStyle(null, null)).ToList();
        var frames = RetainRange(source, start, count);
        return new BakedFrameList(new State(frames, snapshot, crop), 0, count);
    }

    /// <summary>复制范围索引或保留现有租约，不读取像素。</summary>
    /// <param name="source">内存、磁盘或烘焙帧源。</param>
    /// <param name="start">范围起点。</param>
    /// <param name="count">范围长度。</param>
    /// <returns>磁盘及烘焙结果须由调用方释放。</returns>
    internal static IReadOnlyList<CapturedFrame> RetainRange(IReadOnlyList<CapturedFrame> source, int start, int count) =>
        source switch
        {
            FrameSpoolList spool => spool.Slice(start, count),
            BakedFrameList baked => baked.Slice(start, count),
            _ => source.Skip(start).Take(count).ToArray(),
        };

    /// <summary>为接收者创建独立的烘焙结果租约，普通序列保持不变。</summary>
    /// <param name="sequence">待交接的序列。</param>
    /// <returns>接收者拥有 Frames 的可释放租约。</returns>
    internal static CapturedSequence Retain(CapturedSequence sequence) => sequence.Frames is BakedFrameList baked
        ? new CapturedSequence
        {
            Frames = baked.Slice(0, baked.Count), Region = sequence.Region,
            MonitorBounds = sequence.MonitorBounds, MonitorDevice = sequence.MonitorDevice,
            Fps = sequence.Fps, DroppedFrames = sequence.DroppedFrames, StartedAt = sequence.StartedAt,
        } : sequence;

    /// <summary>本租约的帧数。</summary>
    public int Count => _count;

    /// <summary>读取并烘焙单帧，不缓存像素。</summary>
    /// <param name="index">范围内的帧下标。</param>
    /// <value>包含裁剪与标注的只读帧。</value>
    public CapturedFrame this[int index] => ReadFrame(index, default);

    /// <summary>逐帧合成标注后裁剪，步骤之间检查取消。</summary>
    /// <param name="frames">只读源帧。</param>
    /// <param name="index">帧下标。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>单帧只读像素。</returns>
    internal static CapturedFrame Read(IReadOnlyList<CapturedFrame> frames, int index, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var frame = frames is BakedFrameList baked ? baked.ReadFrame(index, ct) : frames[index];
        ct.ThrowIfCancellationRequested();
        return frame;
    }

    CapturedFrame ReadFrame(int index, CancellationToken ct)
    {
        Acquire(index);
        try
        {
            var frame = Read(_state.Source, _start + index, ct);
            frame = Bake.ApplyFrame(frame, _state.Annotations, ct);
            ct.ThrowIfCancellationRequested();
            return _state.Crop is { } crop ? Bake.CropFrame(frame, crop.X, crop.Y, crop.W, crop.H) : frame;
        }
        finally { Release(); }
    }

    /// <summary>取得裁剪后的尺寸与原始时间戳，不烘焙像素。</summary>
    /// <param name="index">范围内的帧下标。</param>
    /// <returns>输出帧的元数据。</returns>
    internal FrameSpoolList.Entry Meta(int index)
    {
        Acquire(index);
        try
        {
            var meta = FrameMetadata.At(_state.Source, _start + index);
            return _state.Crop is { } crop
                ? new(0, meta.TimeMs, crop.W, crop.H, checked(crop.W * crop.H * 4)) : meta;
        }
        finally { Release(); }
    }

    BakedFrameList Slice(int start, int count)
    {
        if (start < 0 || count < 0 || start > Count - count) throw new ArgumentOutOfRangeException(nameof(start));
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _state.Readers++;
            return new(_state, _start + start, count);
        }
    }

    void Acquire(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _state.Readers++;
        }
    }

    void Release()
    {
        lock (_state)
            if (--_state.Readers == 0) (_state.Source as IDisposable)?.Dispose();
    }

    /// <summary>逐帧读取当前范围。</summary>
    /// <returns>每次移动才烘焙一帧的枚举器。</returns>
    public IEnumerator<CapturedFrame> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>释放本租约；其他租约及正在读取的帧继续有效。</summary>
    public void Dispose()
    {
        lock (_state)
        {
            if (_disposed) return;
            _disposed = true;
            Release();
        }
    }
}
