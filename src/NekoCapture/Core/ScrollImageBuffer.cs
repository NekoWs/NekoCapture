namespace NekoCapture.Core;

/// <summary>按新增行保存长图，匹配仅使用末次视图，完成时才合并连续像素。</summary>
internal sealed class ScrollImageBuffer
{
    readonly object _gate = new();
    readonly List<(byte[] Pixels, int Start, int Rows)> _parts = new();
    int _height;
    byte[] _tail;

    /// <summary>长图的物理像素宽度。</summary>
    internal int Width { get; }
    /// <summary>已保存的总行数。</summary>
    internal int Height => Volatile.Read(ref _height);
    /// <summary>末次拼入的完整视图，只读像素供串行匹配使用。</summary>
    internal byte[] Tail => _tail;

    /// <summary>接管只读起始像素，继续会话时仅取尾部视图参与匹配。</summary>
    /// <param name="pixels">连续的起始长图 BGRA。</param>
    /// <param name="width">长图宽度。</param>
    /// <param name="height">长图高度。</param>
    /// <param name="viewHeight">每次捕获的视图高度。</param>
    internal ScrollImageBuffer(byte[] pixels, int width, int height, int viewHeight)
    {
        if (width <= 0 || height <= 0 || viewHeight <= 0 || (long)width * height * 4 != pixels.Length)
            throw new ArgumentException("Invalid scroll image dimensions");
        Width = width; _height = height;
        _parts.Add((pixels, 0, height));
        int rows = Math.Min(height, viewHeight);
        _tail = rows == height ? pixels : pixels.AsSpan((height - rows) * width * 4, rows * width * 4).ToArray();
    }

    /// <summary>保存视图中重叠之后的行；调用方串行写入，像素在返回后保持只读。</summary>
    /// <param name="pixels">完整视图的 BGRA。</param>
    /// <param name="height">视图高度。</param>
    /// <param name="overlap">顶部重叠行数，必须大于零。</param>
    /// <returns>是否追加了新行。</returns>
    internal bool Append(byte[] pixels, int height, int overlap)
    {
        if (overlap <= 0 || overlap >= height || (long)Width * height * 4 != pixels.Length) return false;
        int rows = height - overlap;
        var added = GC.AllocateUninitializedArray<byte>(checked(rows * Width * 4));
        Buffer.BlockCopy(pixels, overlap * Width * 4, added, 0, added.Length);
        lock (_gate)
        {
            int total = checked(_height + rows);
            if ((long)total * Width > Array.MaxLength / 4) throw new InvalidOperationException("Scroll image exceeds pixel buffer limit");
            _parts.Add((added, _height, rows));
            Volatile.Write(ref _height, total);
        }
        _tail = pixels;
        return true;
    }

    /// <summary>按宽度缩放可见尾部，直接从分段读取，仅分配预览大小的像素。</summary>
    /// <param name="maxWidth">预览宽度上限。</param>
    /// <param name="maxHeight">预览高度上限。</param>
    /// <param name="height">本次预览的长图行数快照。</param>
    /// <returns>预览 BGRA 及物理像素尺寸。</returns>
    internal (byte[] Bgra, int W, int H) Preview(int maxWidth, int maxHeight, int height)
    {
        if (maxWidth <= 0 || maxHeight <= 0 || height <= 0) return (Array.Empty<byte>(), 0, 0);
        lock (_gate)
        {
            height = Math.Min(height, _height);
            int width = Math.Min(Width, maxWidth);
            double scale = (double)width / Width;
            int rows = (int)Math.Min(maxHeight, Math.Ceiling(height * scale));
            int sourceRows = Math.Min(height, (int)Math.Ceiling(rows / scale));
            int first = height - sourceRows;
            var output = GC.AllocateUninitializedArray<byte>(checked(width * rows * 4));
            int part = FindPart(first);
            for (int y = 0; y < rows; y++)
            {
                int sy = y == rows - 1 ? height - 1 : first + (int)((long)y * sourceRows / rows);
                while (sy >= _parts[part].Start + _parts[part].Rows) part++;
                var chunk = _parts[part];
                int rowOffset = (sy - chunk.Start) * Width * 4;
                if (width == Width) Buffer.BlockCopy(chunk.Pixels, rowOffset, output, y * width * 4, width * 4);
                else for (int x = 0; x < width; x++)
                {
                    int sx = x == width - 1 ? Width - 1 : (int)((long)x * Width / width);
                    Buffer.BlockCopy(chunk.Pixels, rowOffset + sx * 4, output, (y * width + x) * 4, 4);
                }
            }
            return (output, width, rows);
        }
    }

    int FindPart(int row)
    {
        int lo = 0, hi = _parts.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_parts[mid].Start <= row) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>合并并复用最终产物的连续 BGRA，释放分段像素引用。</summary>
    /// <returns>精确覆盖长图有效尺寸的只读像素。</returns>
    internal byte[] ToArray()
    {
        lock (_gate)
        {
            if (_parts.Count == 1 && _parts[0].Pixels.Length == _height * Width * 4) return _parts[0].Pixels;
            var output = GC.AllocateUninitializedArray<byte>(checked(_height * Width * 4));
            foreach (var chunk in _parts)
                Buffer.BlockCopy(chunk.Pixels, 0, output, chunk.Start * Width * 4, chunk.Rows * Width * 4);
            _parts.Clear();
            _parts.Add((output, 0, _height));
            return output;
        }
    }
}
