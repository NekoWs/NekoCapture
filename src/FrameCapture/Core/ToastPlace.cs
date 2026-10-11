using FrameCapture.Capture;

namespace FrameCapture.Core;

/// <summary>
/// 截屏成功浮窗的落点决策（纯逻辑，物理像素）：
/// 优先贴在捕获区右下角内侧（视线刚离开的地方）；区域太小放不下时贴区域外下缘，
/// 下缘越界则翻到区域上缘；最后整体钳进工作区，保证浮窗完整可见。
/// </summary>
public static class ToastPlace
{
    public const int Margin = 12;   // 区域内边距
    public const int Gap = 10;      // 区域外间距

    public static RectI Place(RectI region, RectI work, int toastW, int toastH)
    {
        int x, y;
        bool fitsInside = region.W >= toastW + Margin * 2 && region.H >= toastH + Margin * 2;
        if (fitsInside)
        {
            x = region.Right - Margin - toastW;
            y = region.Bottom - Margin - toastH;
        }
        else
        {
            x = region.Right - toastW;               // 与区域右缘对齐
            y = region.Bottom + Gap;                  // 区域下方
            if (y + toastH > work.Bottom) y = region.Y - Gap - toastH; // 下方放不下 → 上方
        }
        x = Math.Clamp(x, work.X + 4, Math.Max(work.X + 4, work.Right - toastW - 4));
        y = Math.Clamp(y, work.Y + 4, Math.Max(work.Y + 4, work.Bottom - toastH - 4));
        return new RectI(x, y, toastW, toastH);
    }
}
