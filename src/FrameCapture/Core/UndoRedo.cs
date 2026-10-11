namespace FrameCapture.Core;

/// <summary>
/// 轻量撤销/重做栈：新增操作清空重做栈；撤销可逐步重做。
/// R50 升级为操作栈：除 Add 外支持 Replace（编辑对象，如改文字内容），编辑可撤销/重做且保持列表顺序。
/// 操作日志与条目列表分离——编辑中间条目后，撤销顺序仍按操作时间倒序（Add A → Add B → Edit A 的撤销次序是 Edit、B、A）。
/// </summary>
public sealed class UndoRedo<T> where T : class
{
    enum OpKind { Add, Edit }

    sealed record Op(OpKind Kind, int Index, T Item, T? Prev);

    readonly List<T> _items = new();
    readonly List<Op> _ops = new(), _undone = new();

    public IReadOnlyList<T> Items => _items;
    public int Count => _items.Count;
    public int RedoCount => _undone.Count;
    public bool CanUndo => _ops.Count > 0;
    public bool CanRedo => _undone.Count > 0;

    public void Add(T item)
    {
        _items.Add(item);
        _ops.Add(new Op(OpKind.Add, _items.Count - 1, item, null));
        _undone.Clear();
    }

    /// <summary>编辑：同位置换成新对象（引用比较找旧对象）。找不到返回 false。</summary>
    public bool Replace(T oldItem, T newItem)
    {
        int i = _items.LastIndexOf(oldItem);
        if (i < 0) return false;
        _items[i] = newItem;
        _ops.Add(new Op(OpKind.Edit, i, newItem, oldItem));
        _undone.Clear();
        return true;
    }

    public bool Undo(out T? item) => Undo(out item, out _);

    /// <summary>撤销一步。Add 撤销=移除；Edit 撤销=原位还原旧对象（restored 非 null）。</summary>
    public bool Undo(out T? item, out T? restored)
    {
        if (_ops.Count == 0) { item = null; restored = null; return false; }
        var op = _ops[^1];
        _ops.RemoveAt(_ops.Count - 1);
        if (op.Kind == OpKind.Add) { _items.Remove(op.Item); item = op.Item; restored = null; }
        else { _items[op.Index] = op.Prev!; item = op.Item; restored = op.Prev; }
        _undone.Add(op);
        return true;
    }

    public bool Redo(out T? item)
    {
        if (_undone.Count == 0) { item = null; return false; }
        var op = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        if (op.Kind == OpKind.Add) _items.Add(op.Item);
        else _items[op.Index] = op.Item;
        _ops.Add(op);
        item = op.Item;
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _ops.Clear();
        _undone.Clear();
    }
}
