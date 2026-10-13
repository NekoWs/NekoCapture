namespace FrameCapture.Core;

/// <summary>
/// 轻量撤销/重做栈：新增操作清空重做栈；撤销可逐步重做。
/// 支持三类操作——Add、Replace（编辑对象，如改文字内容）、Remove，编辑可撤销/重做且保持列表顺序。
/// 操作日志与条目列表分离——编辑中间条目后，撤销顺序仍按操作时间倒序（Add A → Add B → Edit A 的撤销次序是 Edit、B、A）。
/// </summary>
public sealed class UndoRedo<T> where T : class
{
    enum OpKind { Add, Edit, Remove }

    sealed record Op(OpKind Kind, int Index, T Item, T? Prev);

    readonly List<T> _items = new();
    readonly List<Op> _ops = new(), _undone = new();

    /// <summary>条目列表本身（同一实例的只读视图，随撤销/重做实时变化，不是快照）。</summary>
    public IReadOnlyList<T> Items => _items;
    public int Count => _items.Count;
    public int RedoCount => _undone.Count;
    public bool CanUndo => _ops.Count > 0;
    public bool CanRedo => _undone.Count > 0;

    /// <summary>追加一个对象并记为一步操作；任何在 Add 之后产生的重做栈都被清空。</summary>
    /// <param name="item">要加入的对象。栈按引用管理它，调用方后续修改须走 <see cref="Replace"/> 才可撤销。</param>
    public void Add(T item)
    {
        _items.Add(item);
        _ops.Add(new Op(OpKind.Add, _items.Count - 1, item, null));
        _undone.Clear();
    }

    /// <summary>编辑：同位置换成新对象（按引用比较定位被替换的那个）。同引用重复时取最后一个。</summary>
    /// <param name="oldItem">列表里现有的对象，按引用匹配。</param>
    /// <param name="newItem">替换后的新对象；占据原位置，列表顺序不变。</param>
    /// <returns>false = 列表里没有该引用，状态与操作栈都不变。</returns>
    public bool Replace(T oldItem, T newItem)
    {
        int i = _items.LastIndexOf(oldItem);
        if (i < 0) return false;
        _items[i] = newItem;
        _ops.Add(new Op(OpKind.Edit, i, newItem, oldItem));
        _undone.Clear();
        return true;
    }

    /// <summary>删除：从列表移除并记住原位（撤销=原位放回）。同引用重复时取最后一个。</summary>
    /// <param name="item">要移除的对象，按引用匹配。</param>
    /// <returns>false = 列表里没有该引用，状态与操作栈都不变。</returns>
    public bool Remove(T item)
    {
        int i = _items.LastIndexOf(item);
        if (i < 0) return false;
        _items.RemoveAt(i);
        _ops.Add(new Op(OpKind.Remove, i, item, null));
        _undone.Clear();
        return true;
    }

    /// <summary>撤销一步，只关心被撤对象。</summary>
    /// <param name="item">被撤销掉的那个对象；无可撤销时为 null。</param>
    /// <returns>false = 操作栈为空。</returns>
    public bool Undo(out T? item) => Undo(out item, out _);

    /// <summary>撤销一步。Add 撤销=移除；Edit 撤销=原位还原被替换掉的那个对象（restored 非 null）；Remove 撤销=原位放回。</summary>
    /// <param name="item">本步操作涉及的对象（Add/Remove 的对象，或 Edit 写入的新对象）；无可撤销时为 null。</param>
    /// <param name="restored">仅 Edit 非 null：撤销后重新占据原位的对象，调用方须把它换回渲染/订阅链。</param>
    /// <returns>false = 操作栈为空，两个 out 参数都是 null。</returns>
    public bool Undo(out T? item, out T? restored)
    {
        if (_ops.Count == 0) { item = null; restored = null; return false; }
        var op = _ops[^1];
        _ops.RemoveAt(_ops.Count - 1);
        switch (op.Kind)
        {
            case OpKind.Add: _items.Remove(op.Item); item = op.Item; restored = null; break;
            case OpKind.Edit: _items[op.Index] = op.Prev!; item = op.Item; restored = op.Prev; break;
            default: _items.Insert(Math.Min(op.Index, _items.Count), op.Item); item = op.Item; restored = null; break;
        }
        _undone.Add(op);
        return true;
    }

    /// <summary>重做一步：Add 重做=再加、Edit 重做=重新写入新对象、Remove 重做=再删原位。</summary>
    /// <param name="item">本步操作涉及的对象；无可重做时为 null。</param>
    /// <returns>false = 重做栈为空（新操作会清空它，故新操作后必然为 false）。</returns>
    public bool Redo(out T? item)
    {
        if (_undone.Count == 0) { item = null; return false; }
        var op = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        switch (op.Kind)
        {
            case OpKind.Add: _items.Add(op.Item); break;
            case OpKind.Edit: _items[op.Index] = op.Item; break;
            default: if (op.Index < _items.Count) _items.RemoveAt(op.Index); break;
        }
        _ops.Add(op);
        item = op.Item;
        return true;
    }

    /// <summary>条目与两条操作栈一并清空：撤销/重做能力随之作废，不可恢复。</summary>
    public void Clear()
    {
        _items.Clear();
        _ops.Clear();
        _undone.Clear();
    }
}
