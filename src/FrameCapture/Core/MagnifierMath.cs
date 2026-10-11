namespace FrameCapture.Core;

/// <summary>
/// 选区放大镜的纯几何决策：源矩形取法与浮窗摆放。无 WPF/无共享状态，供单测（T106）。
/// 快照以监视器左上角为原点（快照像素 (0,0) = 虚拟桌面 (monitorX,monitorY)）。
/// </summary>
public static class MagnifierMath
{
    /// <summary>放大镜取源矩形：以快照坐标 (cx,cy) 为中心取 span×span，夹进快照内。
    /// 快照某边小于 span 时该边退化为整幅（size 取两边与 span 的最小值，保持正方形）。
    /// 返回 (x,y,size)，size≥1。</summary>
    public static (int X, int Y, int Size) Crop(int cx, int cy, int snapW, int snapH, int span)
    {
        int s = Math.Min(span, Math.Min(snapW, snapH));
        if (s < 1) s = 1;
        int x = Math.Clamp(cx - s / 2, 0, Math.Max(0, snapW - s));
        int y = Math.Clamp(cy - s / 2, 0, Math.Max(0, snapH - s));
        return (x, y, s);
    }

    /// <summary>放大镜浮窗摆放：默认贴光标右下 (gap) 处；越出视图右/下边则翻到左/上侧。
    /// 返回左上角 (x,y)，DIP 坐标。</summary>
    public static (double X, double Y) Place(double cursorX, double cursorY,
        double loupeW, double loupeH, double viewW, double viewH, double gap)
    {
        double x = cursorX + gap;
        if (x + loupeW > viewW) x = cursorX - gap - loupeW;
        if (x < 0) x = 0;
        double y = cursorY + gap;
        if (y + loupeH > viewH) y = cursorY - gap - loupeH;
        if (y < 0) y = 0;
        return (x, y);
    }
}
