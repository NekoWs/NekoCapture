namespace FrameCapture.Core;

/// <summary>轻量撤销/重做栈：新增操作清空重做栈；撤销可逐步重做。</summary>
public sealed class UndoRedo<T>
{
    readonly List<T> _done = new(), _undone = new();

    public IReadOnlyList<T> Items => _done;
    public int Count => _done.Count;
    public bool CanUndo => _done.Count > 0;
    public bool CanRedo => _undone.Count > 0;

    public void Add(T item)
    {
        _done.Add(item);
        _undone.Clear();
    }

    public bool Undo(out T? item)
    {
        if (_done.Count == 0) { item = default!; return false; }
        item = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        _undone.Add(item);
        return true;
    }

    public bool Redo(out T? item)
    {
        if (_undone.Count == 0) { item = default!; return false; }
        item = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        _done.Add(item);
        return true;
    }

    public void Clear()
    {
        _done.Clear();
        _undone.Clear();
    }
}
