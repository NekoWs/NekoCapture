namespace FrameCapture.Core;

/// <summary>
/// 选区放大镜的纯几何决策：源矩形取法与浮窗摆放。无 WPF/无共享状态，供单测。
/// 快照以监视器左上角为原点（快照像素 (0,0) = 虚拟桌面 (monitorX,monitorY)）。
/// </summary>
public static class MagnifierMath
{
    /// <summary>
    /// 放大镜取源矩形：以 (cx,cy) 为中心取 span×span，整体夹进快照内。
    /// 快照某边小于 span 时该边退化为整幅，故边长取 span 与两边的最小值以保持正方形。
    /// </summary>
    /// <param name="cx">中心点的快照横坐标（像素，非 DIP）。</param>
    /// <param name="cy">中心点的快照纵坐标（像素，非 DIP）。</param>
    /// <param name="snapW">快照宽（像素）。</param>
    /// <param name="snapH">快照高（像素）。</param>
    /// <param name="span">期望的取源边长（像素）。</param>
    /// <returns>源矩形 (X,Y,Size)：Size = min(span, snapW, snapH) 且 ≥1，矩形不越出快照边界。</returns>
    public static (int X, int Y, int Size) Crop(int cx, int cy, int snapW, int snapH, int span)
    {
        int s = Math.Min(span, Math.Min(snapW, snapH));
        if (s < 1) s = 1;
        int x = Math.Clamp(cx - s / 2, 0, Math.Max(0, snapW - s));
        int y = Math.Clamp(cy - s / 2, 0, Math.Max(0, snapH - s));
        return (x, y, s);
    }

    /// <summary>放大镜浮窗摆放：默认贴光标右下 gap 处；越出视图右/下边则翻到左/上侧。</summary>
    /// <param name="cursorX">光标在视图内的横坐标（DIP）。</param>
    /// <param name="cursorY">光标在视图内的纵坐标（DIP）。</param>
    /// <param name="loupeW">浮窗宽（DIP）。</param>
    /// <param name="loupeH">浮窗高（DIP）。</param>
    /// <param name="viewW">可视区宽（DIP），越界判定的右边界。</param>
    /// <param name="viewH">可视区高（DIP），越界判定的下边界。</param>
    /// <param name="gap">光标与浮窗近边的间距（DIP）。</param>
    /// <returns>浮窗左上角 (X,Y)（DIP）：翻转后仍越出左上时钳到 0；浮窗比视图还大时不保证完整可见。</returns>
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

    /// <summary>从 BGRA 缓冲取 (x,y) 像素的 RGB。取不到时返回黑，不抛。</summary>
    /// <param name="bgra">行主序 BGRA 缓冲，每像素 4 字节；null 视为取不到。</param>
    /// <param name="w">行宽（像素），用于换算行偏移，须 &gt;0。</param>
    /// <param name="x">像素横坐标，左上原点、从 0 起。</param>
    /// <param name="y">像素纵坐标，左上原点、从 0 起。</param>
    /// <returns>(R,G,B) 各 0–255；坐标为负、w≤0 或偏移越过缓冲末尾时返回 (0,0,0)。</returns>
    public static (int R, int G, int B) PixelRgb(byte[] bgra, int w, int x, int y)
    {
        if (bgra == null || w <= 0 || x < 0 || y < 0) return (0, 0, 0);
        long i = ((long)y * w + x) * 4;
        if (i + 3 >= bgra.Length) return (0, 0, 0);
        return (bgra[i + 2], bgra[i + 1], bgra[i]); // BGRA → R,G,B
    }

    /// <summary>RGB → #RRGGBB（大写）。</summary>
    /// <param name="r">红通道，越界值按 0–255 端点钳制。</param>
    /// <param name="g">绿通道，越界值按 0–255 端点钳制。</param>
    /// <param name="b">蓝通道，越界值按 0–255 端点钳制。</param>
    /// <returns>7 字符文本，每通道固定两位大写十六进制。</returns>
    public static string Hex(int r, int g, int b) =>
        $"#{Clamp255(r):X2}{Clamp255(g):X2}{Clamp255(b):X2}";

    static int Clamp255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}
