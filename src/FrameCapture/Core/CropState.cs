namespace FrameCapture.Core;

/// <summary>
/// 裁剪 = 视图状态而非破坏性编辑（纯逻辑，可单测）。
/// 帧数据永不因裁剪被替换；裁剪框以"全帧归一化矩形"的栈记录（每级存合成后的可视区），
/// 显示时把当前矩形映射到视口，导出时一次性 Bake.Crop。撤销裁剪 = 弹栈，零内存代价。
/// 不变量：矩形恒在全帧 [0,1]² 内且 w/h &gt; 0；Push 与当前可视区求交（二次裁剪=缩窗，绝不越界）。
/// </summary>
public sealed class CropState
{
    /// <summary>全帧归一化矩形。</summary>
    /// <param name="X">左上角 X，全帧宽度的归一化比例。</param>
    /// <param name="Y">左上角 Y，全帧高度的归一化比例。</param>
    /// <param name="W">宽，归一化比例。</param>
    /// <param name="H">高，归一化比例。</param>
    /// <returns>原样保存四个比例的矩形；构造本身不钳制，钳制由 <see cref="CropState"/> 的入栈路径负责。</returns>
    public readonly record struct Norm(double X, double Y, double W, double H)
    {
        /// <summary>未裁剪 = 整个全帧。</summary>
        public static readonly Norm Full = new(0, 0, 1, 1);
        public double Right => X + W;
        public double Bottom => Y + H;
    }

    readonly List<Norm> _stack = new();
    readonly List<Norm> _redo = new();

    /// <summary>当前可视区（无裁剪 = 全帧）。</summary>
    public Norm Current => _stack.Count == 0 ? Norm.Full : _stack[^1];
    public bool IsCropped => _stack.Count > 0;
    public int Depth => _stack.Count;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// 压入一次裁剪：入参为全帧归一化的拖框（任意角点序），与当前可视区求交并钳进 [0,1]²。
    /// 交集退化（宽 ≤ minW 或高 ≤ minH）返回 false 且不改栈；成功则清空 redo 栈（标准撤销语义）。
    /// </summary>
    /// <param name="ax">拖框一个角的 X（全帧归一化，可越界，内部钳进 [0,1]）。</param>
    /// <param name="ay">该角的 Y。</param>
    /// <param name="bx">对角角的 X；与 ax 谁大谁小都行。</param>
    /// <param name="by">对角角的 Y。</param>
    /// <param name="minW">交集最小宽（归一化），交集宽不超过它就拒绝。</param>
    /// <param name="minH">交集最小高（归一化）。</param>
    /// <returns>true = 已入栈且 redo 栈被清空；false = 交集退化，状态未变。</returns>
    public bool Push(double ax, double ay, double bx, double by, double minW = 0, double minH = 0)
    {
        var c = Current;
        double x0 = Math.Clamp(Math.Min(ax, bx), 0, 1), x1 = Math.Clamp(Math.Max(ax, bx), 0, 1);
        double y0 = Math.Clamp(Math.Min(ay, by), 0, 1), y1 = Math.Clamp(Math.Max(ay, by), 0, 1);
        double l = Math.Max(x0, c.X), r = Math.Min(x1, c.Right);
        double t = Math.Max(y0, c.Y), b = Math.Min(y1, c.Bottom);
        if (r - l <= minW || b - t <= minH) return false;
        _stack.Add(new Norm(l, t, r - l, b - t));
        _redo.Clear();
        return true;
    }

    /// <summary>弹出一级裁剪（撤销），矩形进 redo 栈供重做。</summary>
    /// <returns>false = 栈空（当前未裁剪），此时 redo 栈不变。</returns>
    public bool Pop()
    {
        if (_stack.Count == 0) return false;
        _redo.Add(_stack[^1]);
        _stack.RemoveAt(_stack.Count - 1);
        return true;
    }

    /// <summary>重做一级裁剪：redo 栈顶重新入裁剪栈。</summary>
    /// <returns>false = redo 栈空，状态未变。</returns>
    public bool RedoCrop()
    {
        if (_redo.Count == 0) return false;
        _stack.Add(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }

    /// <summary>当前可视区折算为帧像素矩形（四舍五入 + 钳边，至少 1px）。</summary>
    /// <param name="fw">帧宽（像素）。</param>
    /// <param name="fh">帧高（像素）。</param>
    /// <returns>像素矩形 (X, Y, W, H)；W/H 至少 1，且整体不越出帧外。</returns>
    public (int X, int Y, int W, int H) ToPixel(int fw, int fh) => ToPixelOf(Current, fw, fh);

    /// <summary>任意归一化矩形 → 帧像素矩形。导出在后台线程拿快照调用：纯算术、无共享状态。</summary>
    /// <param name="c">待折算的归一化矩形，调用方保证在 [0,1]² 内。</param>
    /// <param name="fw">帧宽（像素）。</param>
    /// <param name="fh">帧高（像素）。</param>
    /// <returns>像素矩形 (X, Y, W, H)；W/H 至少 1，右下角不越出帧边界。</returns>
    public static (int X, int Y, int W, int H) ToPixelOf(Norm c, int fw, int fh)
    {
        int x = Math.Clamp((int)Math.Round(c.X * fw), 0, Math.Max(0, fw - 1));
        int y = Math.Clamp((int)Math.Round(c.Y * fh), 0, Math.Max(0, fh - 1));
        int w = Math.Clamp((int)Math.Round(c.W * fw), 1, fw - x);
        int h = Math.Clamp((int)Math.Round(c.H * fh), 1, fh - y);
        return (x, y, w, h);
    }
}

/// <summary>
/// 标注与裁剪两条操作流共享单调序号的撤销次序（纯逻辑，可单测）：
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
    /// <param name="hasAnns">标注流当前有可撤销的动作。</param>
    /// <returns>true = 本次撤销交给裁剪流；false = 交给标注流（或两边都没得撤）。</returns>
    public bool UndoPopsCrop(bool hasAnns) => _crops.Count > 0 && ( !hasAnns || _crops.Peek() > _annTop);

    /// <summary>重做应落在裁剪流？（裁剪重做栈非空且比标注流更新）</summary>
    /// <param name="annCanRedo">标注流当前有可重做的动作。</param>
    /// <returns>true = 本次重做交给裁剪流；false = 交给标注流（或两边都没得重做）。</returns>
    public bool RedoPopsCrop(bool annCanRedo) => _cropRedo.Count > 0 && (!annCanRedo || _cropRedo.Peek() > _annTop);

    /// <summary>撤销消费一级裁剪。</summary>
    public void CropUndone() { if (_crops.Count > 0) _cropRedo.Push(_crops.Pop()); }

    /// <summary>重做消费一级裁剪。</summary>
    public void CropRedone() { if (_cropRedo.Count > 0) _crops.Push(_cropRedo.Pop()); }

    public bool CanUndoCrop => _crops.Count > 0;
    public bool CanRedoCrop => _cropRedo.Count > 0;
    public int CropUndoCount => _crops.Count;
    public int CropRedoCount => _cropRedo.Count;
}
