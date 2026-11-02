namespace NekoCapture.Runtime;

/// <summary>托盘单击与双击互斥；同一行为无需等待双击窗口。</summary>
internal sealed class TrayClickGate
{
    private string _single = "", _double = "";
    private bool _equivalent, _ignoreRelease, _singleDispatched;
    /// <summary>单击正在等待双击判定。</summary>
    internal bool Pending { get; private set; }

    /// <summary>应用可执行触发标识并清除未完成判定。</summary>
    /// <param name="single">单击标识；空值不执行。</param>
    /// <param name="doubleClick">双击标识；空值不执行。</param>
    /// <param name="equivalent">两个入口是否执行相同的行为与参数。</param>
    internal void Configure(string single, string doubleClick, bool equivalent)
    {
        if (_single == single && _double == doubleClick && _equivalent == equivalent) return;
        _single = single; _double = doubleClick; _equivalent = equivalent; Pending = _ignoreRelease = _singleDispatched = false;
    }

    /// <summary>处理左键释放。</summary>
    /// <returns>立即执行的触发标识；空值表示忽略或等待。</returns>
    internal string Release()
    {
        if (_ignoreRelease) { _ignoreRelease = false; return ""; }
        if (_double.Length > 0 && !_equivalent) { Pending = _single.Length > 0; return ""; }
        _singleDispatched = _single.Length > 0; return _single;
    }

    /// <summary>处理系统确认的左键双击，取消待定单击。</summary>
    /// <returns>应执行的双击标识；相同行为已执行时为空。</returns>
    internal string DoubleClick()
    {
        Pending = false; _ignoreRelease = true;
        string result = _equivalent && _singleDispatched ? "" : _double;
        _singleDispatched = false; return result;
    }

    /// <summary>双击窗口结束后提交待定单击。</summary>
    /// <returns>应执行的单击标识；没有待定单击时为空。</returns>
    internal string Flush()
    { bool pending = Pending; Pending = false; return pending ? _single : ""; }
}
