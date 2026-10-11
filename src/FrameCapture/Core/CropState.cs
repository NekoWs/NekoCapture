namespace FrameCapture.Core;

/// <summary>
/// 裁剪 = 视图状态而非破坏性编辑（SelfTest T80）。
/// 帧数据永不因裁剪被替换；裁剪框以"全帧归一化矩形"的栈记录（每级存合成后的可视区），
/// 显示时把当前矩形映射到视口，导出时一次性 Bake.Crop。撤销裁剪 = 弹栈，零内存代价。
/// 不变量：矩形恒在全帧 [0,1]² 内且 w/h &gt; 0；Push 与当前可视区求交（二次裁剪=缩窗，绝不越界）。
/// </summary>
public sealed class CropState
{
    /// <summary>全帧归一化矩形。</summary>
    public readonly record struct Norm(double X, double Y, double W, double H)
    {
        public static readonly Norm Full = new(0, 0, 1, 1);
        public double Right => X + W;
        public double Bottom => Y + H;
    }

    readonly List<Norm> _stack = new();

    /// <summary>当前可视区（无裁剪 = 全帧）。</summary>
    public Norm Current => _stack.Count == 0 ? Norm.Full : _stack[^1];
    public bool IsCropped => _stack.Count > 0;
    public int Depth => _stack.Count;

    /// <summary>
    /// 压入一次裁剪：入参为全帧归一化的拖框（任意角点序），与当前可视区求交并钳进 [0,1]²。
    /// 交集退化（宽或高 ≤ 0）返回 false 且不改栈。minFrac 为最小保留比例（编辑器按 48px 折算传入）。
    /// </summary>
    public bool Push(double ax, double ay, double bx, double by, double minFrac = 0)
    {
        var c = Current;
        double x0 = Math.Clamp(Math.Min(ax, bx), 0, 1), x1 = Math.Clamp(Math.Max(ax, bx), 0, 1);
        double y0 = Math.Clamp(Math.Min(ay, by), 0, 1), y1 = Math.Clamp(Math.Max(ay, by), 0, 1);
        double l = Math.Max(x0, c.X), r = Math.Min(x1, c.Right);
        double t = Math.Max(y0, c.Y), b = Math.Min(y1, c.Bottom);
        if (r - l <= minFrac || b - t <= minFrac) return false;
        _stack.Add(new Norm(l, t, r - l, b - t));
        return true;
    }

    /// <summary>弹出一级裁剪（撤销）。无可弹返回 false。</summary>
    public bool Pop() { if (_stack.Count == 0) return false; _stack.RemoveAt(_stack.Count - 1); return true; }

    /// <summary>当前可视区折算为帧像素矩形（四舍五入 + 钳边，至少 1px）。</summary>
    public (int X, int Y, int W, int H) ToPixel(int fw, int fh)
    {
        var c = Current;
        int x = Math.Clamp((int)Math.Round(c.X * fw), 0, Math.Max(0, fw - 1));
        int y = Math.Clamp((int)Math.Round(c.Y * fh), 0, Math.Max(0, fh - 1));
        int w = Math.Clamp((int)Math.Round(c.W * fw), 1, fw - x);
        int h = Math.Clamp((int)Math.Round(c.H * fh), 1, fh - y);
        return (x, y, w, h);
    }
}

/// <summary>
/// 标注与裁剪两条操作流共享单调序号的撤销次序（SelfTest T81）：
/// Undo 弹出"最近发生"的那条流（序号大者），Redo 对称。跨流交错不破坏 LIFO 直觉。
/// </summary>
public sealed class ActionOrder
{
    long _seq;
    long _annTop;          // 最近一次标注动作（加笔/撤销/重做）的序号
    readonly Stack<long> _crops = new();
    readonly Stack<long> _cropRedo = new();

    /// <summary>新增标注：成为最近动作，并清空一切重做栈（标准撤销语义：新动作使 redo 失效）。</summary>
    public void AnnAdded() { _annTop = ++_seq; _cropRedo.Clear(); }

    /// <summary>标注流撤销/重做完成后的打点（它成为最近动作，但不清重做栈——undo 链后仍须能 redo）。</summary>
    public void AnnStepped() { _annTop = ++_seq; }

    /// <summary>裁剪成功压栈。</summary>
    public void CropPushed() { _crops.Push(++_seq); _cropRedo.Clear(); }

    /// <summary>撤销应落在裁剪流？（裁剪栈非空且比标注流更新）</summary>
    public bool UndoPopsCrop(bool hasAnns) => _crops.Count > 0 && ( !hasAnns || _crops.Peek() > _annTop);

    /// <summary>重做应落在裁剪流？（裁剪重做栈非空且比标注流更新）</summary>
    public bool RedoPopsCrop(bool annCanRedo) => _cropRedo.Count > 0 && (!annCanRedo || _cropRedo.Peek() > _annTop);

    /// <summary>撤销消费一级裁剪。</summary>
    public void CropUndone() { if (_crops.Count > 0) _cropRedo.Push(_crops.Pop()); }

    /// <summary>重做消费一级裁剪。</summary>
    public void CropRedone() { if (_cropRedo.Count > 0) _crops.Push(_cropRedo.Pop()); }

    public bool CanUndoCrop => _crops.Count > 0;
    public bool CanRedoCrop => _cropRedo.Count > 0;
}
