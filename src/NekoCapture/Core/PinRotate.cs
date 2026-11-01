using NekoCapture.Capture;

namespace NekoCapture.Core;

/// <summary>贴图旋转：把已捕获帧按 90° 整数倍旋转，无损像素重排（非重采样）。
/// 纯函数，返回新帧；原帧不变。旋转 4 次回到原图。</summary>
public static class PinRotate
{
    /// <summary>顺时针旋转 90°：old(x,y) → new(H−1−y, x)，新帧宽=源帧高、高=源帧宽。</summary>
    /// <param name="f">源帧，BGRA 行主序；本方法只读，不修改其缓冲。</param>
    /// <returns>新帧：新分配的 BGRA 缓冲，W/H 互换，TimeMs 沿用源帧。</returns>
    public static CapturedFrame Rotate90Cw(CapturedFrame f)
    {
        int w = f.W, h = f.H;
        var src = f.Bgra;
        var dst = new byte[src.Length];
        int nw = h; // 新宽
        for (int y = 0; y < h; y++)
        {
            int srow = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int si = srow + x * 4;
                int nx = h - 1 - y, ny = x;
                int di = (ny * nw + nx) * 4;
                dst[di] = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }
        return new CapturedFrame { Bgra = dst, W = h, H = w, TimeMs = f.TimeMs };
    }

    /// <summary>连续旋转 steps 次（取模 4）。</summary>
    /// <param name="f">源帧。</param>
    /// <param name="steps">顺时针 90° 的次数；对 4 取模，负数按同方向回卷（−1 等价 3）。</param>
    /// <returns>steps ≡ 0 时原样返回同一实例（不复制像素）；否则返回逐步旋转后的新帧。</returns>
    public static CapturedFrame Rotate(CapturedFrame f, int steps)
    {
        steps = ((steps % 4) + 4) % 4;
        var r = f;
        for (int i = 0; i < steps; i++) r = Rotate90Cw(r);
        return r;
    }
}
