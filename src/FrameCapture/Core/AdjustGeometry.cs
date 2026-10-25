using FrameCapture.Capture;

namespace FrameCapture.Core;

/// <summary>调整选区时按住的位置：整块移动、某条边、某个角。</summary>
public enum GrabKind
{
    /// <summary>没抓住任何东西。</summary>
    None,
    /// <summary>框内：整块移动。</summary>
    Move,
    /// <summary>左边。</summary>
    Left,
    /// <summary>右边。</summary>
    Right,
    /// <summary>上边。</summary>
    Top,
    /// <summary>下边。</summary>
    Bottom,
    /// <summary>左上角。</summary>
    TopLeft,
    /// <summary>右上角。</summary>
    TopRight,
    /// <summary>左下角。</summary>
    BottomLeft,
    /// <summary>右下角。</summary>
    BottomRight,
}

/// <summary>
/// 可调整选区的纯几何：按坐标判断抓住了哪条边/角，按位移算出新矩形，按目标矩形的边吸附。
/// 抽成纯函数是为了能直接断言——拾取与缩放的分支最容易左右写反，靠人手试拖几百次靠不住。
/// </summary>
public static class AdjustGeometry
{
    /// <summary>最小选区边长（物理像素）：再小就抓不住边，也没法继续缩。</summary>
    public const int MinSide = 8;

    /// <summary>判断某个点抓住了选区的哪个部位。</summary>
    /// <param name="sel">当前选区（物理像素）。</param>
    /// <param name="x">点横坐标（物理像素）。</param>
    /// <param name="y">点纵坐标（物理像素）。</param>
    /// <param name="tol">边角判定半径（物理像素）。</param>
    /// <returns>抓住的部位；既不在边上也不在框内时为 <see cref="GrabKind.None"/>。</returns>
    public static GrabKind Hit(RectI sel, int x, int y, int tol)
    {
        int t = Math.Max(1, tol);
        bool nearL = Math.Abs(x - sel.X) <= t;
        bool nearR = Math.Abs(x - (sel.X + sel.W)) <= t;
        bool nearT = Math.Abs(y - sel.Y) <= t;
        bool nearB = Math.Abs(y - (sel.Y + sel.H)) <= t;
        bool withinX = x >= sel.X - t && x <= sel.X + sel.W + t;
        bool withinY = y >= sel.Y - t && y <= sel.Y + sel.H + t;
        if (withinX && withinY)
        {
            // 角优先：角落同时满足两条边，先判角才不会永远只判到一条边
            if (nearL && nearT) return GrabKind.TopLeft;
            if (nearR && nearT) return GrabKind.TopRight;
            if (nearL && nearB) return GrabKind.BottomLeft;
            if (nearR && nearB) return GrabKind.BottomRight;
            if (nearL) return GrabKind.Left;
            if (nearR) return GrabKind.Right;
            if (nearT) return GrabKind.Top;
            if (nearB) return GrabKind.Bottom;
        }
        if (x > sel.X && x < sel.X + sel.W && y > sel.Y && y < sel.Y + sel.H) return GrabKind.Move;
        return GrabKind.None;
    }

    /// <summary>抓取部位对应的光标形状名。</summary>
    /// <param name="kind">抓取部位。</param>
    /// <returns>光标内部名；无法对应时为空串。</returns>
    public static string CursorFor(GrabKind kind) => kind switch
    {
        GrabKind.Left or GrabKind.Right => "SizeWE",
        GrabKind.Top or GrabKind.Bottom => "SizeNS",
        GrabKind.TopLeft or GrabKind.BottomRight => "SizeNWSE",
        GrabKind.TopRight or GrabKind.BottomLeft => "SizeNESW",
        GrabKind.Move => "SizeAll",
        _ => "",
    };

    /// <summary>按位移算出缩放后的选区：被抓的边跟着走、对边不动，整块不出界也不小于最小边长。</summary>
    /// <param name="sel">按下时的选区（物理像素）。</param>
    /// <param name="kind">抓住的部位。</param>
    /// <param name="dx">横向位移。</param>
    /// <param name="dy">纵向位移。</param>
    /// <param name="bounds">允许的范围（通常是显示器矩形）。</param>
    /// <returns>缩放后的矩形；<see cref="GrabKind.None"/> 时原样返回。</returns>
    public static RectI Resize(RectI sel, GrabKind kind, int dx, int dy, RectI bounds)
    {
        if (kind == GrabKind.None) return sel;
        if (kind == GrabKind.Move) return Move(sel, dx, dy, bounds);
        int l = sel.X, t = sel.Y, r = sel.X + sel.W, b = sel.Y + sel.H;
        switch (kind)
        {
            case GrabKind.Left: l += dx; break;
            case GrabKind.Right: r += dx; break;
            case GrabKind.Top: t += dy; break;
            case GrabKind.Bottom: b += dy; break;
            case GrabKind.TopLeft: l += dx; t += dy; break;
            case GrabKind.TopRight: r += dx; t += dy; break;
            case GrabKind.BottomLeft: l += dx; b += dy; break;
            case GrabKind.BottomRight: r += dx; b += dy; break;
        }
        l = Math.Clamp(l, bounds.X, bounds.X + bounds.W - MinSide);
        r = Math.Clamp(r, l + MinSide, bounds.X + bounds.W);
        t = Math.Clamp(t, bounds.Y, bounds.Y + bounds.H - MinSide);
        b = Math.Clamp(b, t + MinSide, bounds.Y + bounds.H);
        return new RectI(l, t, r - l, b - t);
    }

    /// <summary>整块平移，夹在允许范围内。</summary>
    /// <param name="sel">当前选区。</param>
    /// <param name="dx">横向位移。</param>
    /// <param name="dy">纵向位移。</param>
    /// <param name="bounds">允许的范围。</param>
    /// <returns>平移后的矩形。</returns>
    public static RectI Move(RectI sel, int dx, int dy, RectI bounds)
    {
        int w = Math.Min(sel.W, bounds.W), h = Math.Min(sel.H, bounds.H);
        int x = Math.Clamp(sel.X + dx, bounds.X, bounds.X + bounds.W - w);
        int y = Math.Clamp(sel.Y + dy, bounds.Y, bounds.Y + bounds.H - h);
        return new RectI(x, y, w, h);
    }
}
