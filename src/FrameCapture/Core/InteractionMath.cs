namespace FrameCapture.Core;

/// <summary>
/// timeline 拖范围状态机（纯逻辑，像素坐标 → 帧索引）。
/// Down→Move→Up：新拖 / 左端点拖 / 右端点拖 / 单击清除。索引语义含两端。
/// </summary>
public sealed class RangeDragState
{
    public const double HandlePx = 8;

    public int Count { get; set; }
    public double Width { get; set; }

    public int A = -1, B = -1; // -1 = 未选范围
    int _mode;                 // 0=none 1=new 2=left 3=right

    public static int XToIdx(double x, double width, int count)
        => Math.Clamp((int)Math.Round(x / Math.Max(1, width) * (count - 1)), 0, Math.Max(0, count - 1));

    public double FrameX(int i)
        => Count > 1 ? i / (double)(Count - 1) * Width : 0;

    /// <summary>按下：靠近已有端点(±HandlePx)则拖该端点，否则开始新拖。</summary>
    public void Down(double x)
    {
        if (Count < 2) { _mode = 0; return; }
        if (A >= 0 && B > A)
        {
            double dxA = Math.Abs(x - FrameX(A)), dxB = Math.Abs(x - FrameX(B));
            if (dxA < HandlePx && dxA <= dxB) { _mode = 2; return; }
            if (dxB < HandlePx) { _mode = 3; return; }
        }
        _mode = 1;
        A = B = XToIdx(x, Width, Count);
    }

    /// <summary>直接设左端点（保持至少 2 帧）。</summary>
    public void SetA(int i)
    {
        if (Count < 2) return;
        if (A < 0) { A = 0; B = Count - 1; }
        A = Math.Clamp(i, 0, B - 1);
    }

    /// <summary>直接设右端点（保持至少 2 帧）。</summary>
    public void SetB(int i)
    {
        if (Count < 2) return;
        if (A < 0) { A = 0; B = Count - 1; }
        B = Math.Clamp(i, A + 1, Count - 1);
    }

    public void Move(double x)
    {
        if (_mode == 0 || Count < 2) return;
        int idx = XToIdx(x, Width, Count);
        switch (_mode)
        {
            case 1: B = idx; if (B < A) (A, B) = (B, A); break; // 支持反向拖
            case 2: A = Math.Min(idx, B); break;
            case 3: B = Math.Max(idx, A); break;
        }
    }

    /// <summary>放开：模式 1 且原地（A==B）视为“单击清除”。</summary>
    public void Up()
    {
        if (_mode == 1 && A == B) { A = B = -1; }
        _mode = 0;
    }

    /// <summary>生效范围：未选或退化时返回全序列。</summary>
    public (int a, int b) Effective => A >= 0 && B > A ? (A, B) : (0, Math.Max(0, Count - 1));

    /// <summary>当前拖拽模式（0/1/2/3），仅测试/诊断用。</summary>
    public int Mode => _mode;
}

/// <summary>范围选择窗口的窗口吸附命中（纯逻辑）。</summary>
public static class SnapPick
{
    /// <summary>在 z 序（顶在前）候选中选第一个包含点、且不小于 4px 的窗口。</summary>
    public static Capture.RectI? Pick(IEnumerable<Capture.RectI> zTopFirst, int x, int y)
    {
        foreach (var b in zTopFirst)
        {
            if (b.W < 4 || b.H < 4) continue;
            if (b.Contains(x, y)) return b;
        }
        return null;
    }
}
