namespace FrameCapture.Core;

/// <summary>
/// timeline 拖范围状态机（纯逻辑，像素坐标 → 帧索引）。
/// Down→Move→Up：新拖 / 左端点拖 / 右端点拖 / 单击清除。索引语义含两端。
/// </summary>
public sealed class RangeDragState
{
    /// <summary>端点命中半径：按下点落在已有端点这个距离内就拖该端点。与 <see cref="Down"/> 的 x 同单位。</summary>
    public const double HandlePx = 8;

    /// <summary>帧数。小于 2 时整条轨道不可拖。</summary>
    public int Count { get; set; }
    /// <summary>轨道可拖宽度，与传入 <see cref="Down"/>/<see cref="Move"/> 的 x 同一坐标空间。</summary>
    public double Width { get; set; }

    public int A = -1, B = -1; // -1 = 未选范围
    int _mode;                 // 0=none 1=new 2=left 3=right

    /// <summary>轨道坐标 → 帧索引：按宽度线性映射后四舍五入，越界一律钳进合法区间。</summary>
    /// <param name="x">轨道坐标，允许落在轨道外（内部钳制，不抛）。</param>
    /// <param name="width">轨道总宽；小于 1 时按 1 计算，避免除零。</param>
    /// <param name="count">帧数；小于 1 时只能得到索引 0。</param>
    /// <returns>闭区间 [0, count-1] 内的索引。</returns>
    public static int XToIdx(double x, double width, int count)
        => Math.Clamp((int)Math.Round(x / Math.Max(1, width) * (count - 1)), 0, Math.Max(0, count - 1));

    /// <summary>帧索引 → 轨道坐标：首帧 0、末帧 <see cref="Width"/>，中间等分。</summary>
    /// <param name="i">帧索引。超出 [0, Count-1] 时按比例外推出轨道外的坐标（不钳制）。</param>
    /// <returns>轨道坐标；<see cref="Count"/> 小于 2 时恒为 0（单帧没有可分拖的轨道）。</returns>
    public double FrameX(int i)
        => Count > 1 ? i / (double)(Count - 1) * Width : 0;

    /// <summary>按下：靠近已有端点(±HandlePx)则拖该端点，否则开始新拖。</summary>
    /// <param name="x">按下点的轨道坐标。</param>
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
    /// <param name="i">目标左端索引，钳进 [0, B-1]；当前无范围时先取全序列再钳。</param>
    public void SetA(int i)
    {
        if (Count < 2) return;
        if (A < 0) { A = 0; B = Count - 1; }
        A = Math.Clamp(i, 0, B - 1);
    }

    /// <summary>直接设右端点（保持至少 2 帧）。</summary>
    /// <param name="i">目标右端索引，钳进 [A+1, Count-1]；当前无范围时先取全序列再钳。</param>
    public void SetB(int i)
    {
        if (Count < 2) return;
        if (A < 0) { A = 0; B = Count - 1; }
        B = Math.Clamp(i, A + 1, Count - 1);
    }

    /// <summary>拖动中：按按下时定下的模式更新端点。模式为 none 或帧数不足时什么都不做。</summary>
    /// <param name="x">当前指针的轨道坐标，越界由 <see cref="XToIdx"/> 钳进合法区间。</param>
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
    /// <value>已选且 <c>B &gt; A</c> 时为 <c>(A, B)</c>（B 含端），否则 <c>(0, Count-1)</c>。索引恒落在序列内。</value>
    public (int a, int b) Effective => A >= 0 && B > A ? (A, B) : (0, Math.Max(0, Count - 1));

    /// <summary>当前拖拽模式（0/1/2/3），仅测试/诊断用。</summary>
    public int Mode => _mode;
}

/// <summary>单选切换按钮组的选中语义（纯逻辑）：再点当前项=取消选中（回到 off），点其它=选中该项。</summary>
public static class ToolToggle
{
    /// <summary>点一下之后的新选中值：点的就是当前项则取消，否则换成点的那项。T 为枚举一类的值类型。</summary>
    /// <param name="current">当前选中值。</param>
    /// <param name="tapped">本次点到的值。</param>
    /// <param name="off">取消后回到的「无选中」值。</param>
    /// <returns>点的与当前项相同则回到 off，否则就是点的那项。</returns>
    public static T Select<T>(T current, T tapped, T off) where T : struct
        => EqualityComparer<T>.Default.Equals(current, tapped) ? off : tapped;
}

/// <summary>范围选择窗口的窗口吸附命中（纯逻辑）。</summary>
public static class SnapPick
{
    /// <summary>在 z 序（顶在前）候选中选第一个包含点、且不小于 4px 的窗口。</summary>
    /// <param name="zTopFirst">候选矩形序列，按 z 序自上而下；小于 4px 的候选直接跳过（多半是残影或工具条）。</param>
    /// <param name="x">命中点 X，与矩形同一坐标空间。</param>
    /// <param name="y">命中点 Y。</param>
    /// <returns>第一个包含该点的候选；无命中返回 null。</returns>
    public static Capture.RectI? Pick(IEnumerable<Capture.RectI> zTopFirst, int x, int y)
    {
        foreach (var b in zTopFirst)
        {
            if (b.W < 4 || b.H < 4) continue;
            if (b.Contains(x, y)) return b;
        }
        return null;
    }

    /// <summary>同上，但候选带 HWND 且一并返回命中的窗口句柄（窗口截图用）。
    /// bounds 用于命中判定（可能是 client），hwnd 用于按窗口捕获。</summary>
    /// <param name="zTopFirst">候选 (HWND, 矩形) 序列，按 z 序自上而下；小于 4px 的跳过。</param>
    /// <param name="x">命中点 X，与矩形同一坐标空间。</param>
    /// <param name="y">命中点 Y。</param>
    /// <returns>命中项的句柄与矩形（原样带出，不做 client→window 换算）；无命中返回 null。</returns>
    public static (IntPtr Hwnd, Capture.RectI Bounds)? PickWindow(IEnumerable<(IntPtr, Capture.RectI)> zTopFirst, int x, int y)
    {
        foreach (var (h, b) in zTopFirst)
        {
            if (b.W < 4 || b.H < 4) continue;
            if (b.Contains(x, y)) return (h, b);
        }
        return null;
    }

    /// <summary>按窗口 DPI 判断 UIA 边界是否代表完整窗口，容忍两逻辑像素的取整及描边偏差。</summary>
    /// <param name="selected">控件吸附边界，虚拟桌面物理像素。</param>
    /// <param name="window">完整窗口边界，坐标系与 selected 相同。</param>
    /// <param name="dpi">目标窗口 DPI；零按 96 处理。</param>
    /// <returns>四条边均位于容差内时为真；尺寸无效时为假。</returns>
    public static bool MatchesWindowBounds(Capture.RectI selected, Capture.RectI window, uint dpi)
    {
        if (selected.W < 8 || selected.H < 8 || window.W < 8 || window.H < 8) return false;
        long tolerance = (long)Math.Ceiling(Math.Max(96u, dpi) * 2.0 / 96);
        return Math.Abs((long)selected.X - window.X) <= tolerance
            && Math.Abs((long)selected.Y - window.Y) <= tolerance
            && Math.Abs(selected.X + (long)selected.W - window.X - window.W) <= tolerance
            && Math.Abs(selected.Y + (long)selected.H - window.Y - window.H) <= tolerance;
    }

    /// <summary>遮挡判定：z 序里索引小于 idx（即排在目标之上）的候选窗，是否有任一与 target 相交。
    /// 遮挡窗口不进入定格预抓缓存；按需 WGC 本体捕获不受此限制。</summary>
    /// <param name="zTopFirst">全部候选矩形，按 z 序自上而下。</param>
    /// <param name="idx">目标窗在序列中的下标；只看它上面的那些。</param>
    /// <param name="target">目标窗矩形，与候选同一坐标空间。</param>
    /// <returns>任一上层候选与 target 相交则为 true；idx ≤ 0 或无相交为 false。</returns>
    public static bool AnyAboveIntersects(IReadOnlyList<Capture.RectI> zTopFirst, int idx, Capture.RectI target)
    {
        for (int i = 0; i < idx && i < zTopFirst.Count; i++)
        {
            var b = zTopFirst[i];
            if (b.X < target.Right && target.X < b.Right && b.Y < target.Bottom && target.Y < b.Bottom)
                return true;
        }
        return false;
    }

    /// <summary>定格预抓排序：从「完整可见」候选（z 序顶在前）里按优先级取前 max 个 HWND——
    /// 当前聚焦窗最优先（若在候选里），其余按 z 序自上而下。纯函数供单测。</summary>
    /// <param name="focused">当前聚焦窗句柄；<see cref="IntPtr.Zero"/> 表示无聚焦窗，不参与优先。</param>
    /// <param name="zTopFirst">完整可见候选，按 z 序自上而下。</param>
    /// <param name="max">最多取几个；≤0 直接返回空表。</param>
    /// <returns>至多 max 个 HWND，聚焦窗在最前且不重复。</returns>
    public static List<IntPtr> PregrabOrder(IntPtr focused, IReadOnlyList<IntPtr> zTopFirst, int max)
    {
        var res = new List<IntPtr>();
        if (max <= 0) return res;
        if (focused != IntPtr.Zero)
            foreach (var h in zTopFirst) if (h == focused) { res.Add(focused); break; }
        foreach (var h in zTopFirst)
        {
            if (res.Count >= max) break;
            if (h != focused) res.Add(h);
        }
        return res;
    }
}
