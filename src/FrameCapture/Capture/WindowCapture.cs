using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Capture;

/// <summary>
/// 「真·窗口截图」：用 WGC 按窗口捕获窗口本体（含 DWM 边框/Win11 圆角，圆角处 alpha=0，
/// 浏览器/GPU 窗也干净）。可选投影＝按窗口 alpha 轮廓**合成**的柔和阴影（高斯模糊轮廓→黑色渐隐），
/// 绝不抓屏幕背景——否则窗口压在别的软件上时，投影环会把背景内容（文字等）截进来，圆角处还会露出深色脏边。
/// 产出带 alpha 的 BGRA 帧 + 实际捕获矩形（物理像素，虚拟桌面坐标）。
/// 任一步失败抛异常 → 调用方回退区域截图（<see cref="LiveCapture.Grab"/>）。
/// 纯合成逻辑（相交/外扩/贴本体/合成阴影/盒式模糊）拆成静态纯函数，SelfTest 可脱离 WGC 直接验。
/// </summary>
public static class WindowCapture
{
    static WindowCapture()
    {
        // apphost 默认探测上下文无法解析 Microsoft.Windows.SDK.NET 投影程序集：
        // 使用方类型首次 JIT 前，从输出目录显式 LoadFrom 预加载一次（同 WgcLiveCapture）。
        try
        {
            var p = System.IO.Path.Combine(AppContext.BaseDirectory, "Microsoft.Windows.SDK.NET.dll");
            if (System.IO.File.Exists(p)) System.Reflection.Assembly.LoadFrom(p);
        }
        catch { }
    }

    /// <summary>投影外扩基准（@96dpi 物理像素），按窗口 DPI 缩放；也是合成阴影的最大外延。</summary>
    public const int ShadowBasePx = 24;
    /// <summary>合成阴影最大不透明度（窗口边缘处约一半，向外渐隐到 0）。</summary>
    public const double ShadowStrength = 0.5;

    /// <summary>截取窗口本体（+可选合成投影）。返回带 alpha 的帧 + 实际捕获矩形（物理像素，虚拟桌面坐标）。
    /// 失败抛异常，调用方回退区域截图。</summary>
    /// <param name="hwnd">目标窗口句柄，须非零。</param>
    /// <param name="includeShadow">是否在本体四周合成柔和投影；false 时输出紧贴窗口 frame 矩形。</param>
    /// <returns>Frame 为带 alpha 的 BGRA（圆角处 alpha=0，投影处是半透明黑）；
    /// Region 为该帧覆盖的矩形（物理像素、虚拟桌面绝对坐标），开投影时比 frame 外扩一圈。</returns>
    public static (CapturedFrame Frame, RectI Region) Grab(IntPtr hwnd, bool includeShadow)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("hwnd=0");

        // 1) 窗口本体：WGC 按窗口捕获（独立 MTA 线程，避免在 WPF STA 线程激活 WinRT 死锁）
        var (body, bw, bh) = WgcGrab(hwnd);

        // 2) 窗口 frame 矩形（DWM 扩展边界，物理像素，虚拟桌面坐标）
        var frame = GetFrameBounds(hwnd);

        // 3) 外扩矩形（给合成阴影留位），并裁到虚拟桌面（屏幕外无阴影可看，省内存）
        int margin = includeShadow ? ShadowMargin(hwnd) : 0;
        var outer = Intersect(Inflate(frame, margin), VirtualDesktop());

        // 4) 输出缓冲（全透明）
        var outBgra = new byte[(long)outer.W * outer.H * 4];
        int bx = frame.X - outer.X, by = frame.Y - outer.Y;

        // 5) 合成阴影（按本体 alpha 轮廓高斯模糊→黑色渐隐；不碰屏幕背景）
        if (includeShadow && margin > 0)
        {
            int radius = Math.Max(2, (int)Math.Round(margin / 3.0)); // 3 次盒式模糊外延≈3r≈margin，到画布边正好淡到 0
            int offsetY = Math.Max(1, margin / 8);                   // 轻微下移，更像真实投影
            SyntheticShadow(outBgra, outer.W, outer.H, body, bw, bh, bx, by, radius, ShadowStrength, offsetY);
        }

        // 6) 本体盖到 frame 位置（带 alpha，直接覆盖 → 圆角处透明、其下阴影被本体盖住）
        BlitBody(outBgra, outer.W, outer.H, body, bw, bh, bx, by);

        var f = new CapturedFrame { Bgra = outBgra, W = outer.W, H = outer.H, TimeMs = 0 };
        return (f, outer);
    }

    #region 纯几何/像素函数（可单测）

    /// <summary>两个矩形的交集。</summary>
    /// <param name="a">矩形 A，物理像素、虚拟桌面绝对坐标。</param>
    /// <param name="b">矩形 B，同一坐标系。</param>
    /// <returns>交集；不相交时返回 (0,0,0,0) 这个空矩形，绝不返回负宽高。</returns>
    public static RectI Intersect(RectI a, RectI b)
    {
        int x0 = Math.Max(a.X, b.X), y0 = Math.Max(a.Y, b.Y);
        int x1 = Math.Min(a.Right, b.Right), y1 = Math.Min(a.Bottom, b.Bottom);
        if (x1 <= x0 || y1 <= y0) return new RectI(0, 0, 0, 0);
        return new RectI(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>四边等距外扩。</summary>
    /// <param name="r">原矩形。</param>
    /// <param name="m">每边外扩的像素数；小于等于 0 时原样返回。</param>
    /// <returns>宽高各 +2m 的新矩形。不做屏幕裁剪——裁剪由调用方的 <see cref="Intersect"/> 负责。</returns>
    public static RectI Inflate(RectI r, int m)
        => m <= 0 ? r : new RectI(r.X - m, r.Y - m, r.W + 2 * m, r.H + 2 * m);

    /// <summary>整个虚拟桌面的外接矩形。</summary>
    /// <returns>SM_X/YVIRTUALSCREEN 加 SM_CX/CYVIRTUALSCREEN：物理像素、虚拟桌面绝对坐标，
    /// 副屏排在左上时 X/Y 可为负。</returns>
    public static RectI VirtualDesktop()
        => new(Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN), Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
               Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN), Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));

    /// <summary>把 src（sw×sh，BGRA 带 alpha）整块覆盖到 dst 的 (bx,by) 处，越界裁剪。dst 尺寸必须够。</summary>
    /// <param name="dst">目标缓冲，行主序 BGRA，长度须等于 dw*dh*4。</param>
    /// <param name="dw">目标宽（像素）。</param>
    /// <param name="dh">目标高（像素）。</param>
    /// <param name="src">源像素，行主序 BGRA 带 alpha，长度须等于 sw*sh*4。</param>
    /// <param name="sw">源宽（像素）。</param>
    /// <param name="sh">源高（像素）。</param>
    /// <param name="bx">落点左上角 X（dst 内坐标），可为负或超出 dst，超出部分被裁掉。</param>
    /// <param name="by">落点左上角 Y（dst 内坐标），同上。</param>
    public static void BlitBody(byte[] dst, int dw, int dh, byte[] src, int sw, int sh, int bx, int by)
    {
        int x0 = Math.Max(0, bx), y0 = Math.Max(0, by);
        int x1 = Math.Min(dw, bx + sw), y1 = Math.Min(dh, by + sh);
        for (int y = y0; y < y1; y++)
        {
            int srow = ((y - by) * sw + (x0 - bx)) * 4;
            int drow = (y * dw + x0) * 4;
            Buffer.BlockCopy(src, srow, dst, drow, (x1 - x0) * 4);
        }
    }

    /// <summary>按本体 alpha 轮廓**合成**柔和投影：取本体 alpha 当覆盖率 → 3 次盒式模糊（≈高斯）→
    /// 黑色 × 覆盖率 × strength 写进 dst（dst 其余保持透明）。绝不读屏幕，故背景文字/深色桌面都不会渗进来。
    /// 轮廓含圆角（本体圆角处 alpha=0），阴影自然贴合圆角。</summary>
    /// <param name="dst">目标缓冲（全透明起步），行主序 BGRA，长度须等于 w*h*4；只有算出非零覆盖率的像素会被写。</param>
    /// <param name="w">目标宽（像素）。</param>
    /// <param name="h">目标高（像素）。</param>
    /// <param name="body">窗口本体像素，只取它的 alpha 通道当阴影覆盖率。</param>
    /// <param name="bw">本体宽（像素）。</param>
    /// <param name="bh">本体高（像素）。</param>
    /// <param name="bx">本体在 dst 中的落点左上角 X。</param>
    /// <param name="by">本体在 dst 中的落点左上角 Y（阴影另加 <paramref name="offsetY"/> 下移）。</param>
    /// <param name="radius">单次盒式模糊半径，3 次叠加后外延约 3r；小于 1 按 1 处理。</param>
    /// <param name="strength">阴影最大不透明度，0–1，乘在覆盖率上。</param>
    /// <param name="offsetY">阴影整体相对本体下移的像素数——本体仍画在 (bx,by)。</param>
    public static void SyntheticShadow(byte[] dst, int w, int h, byte[] body, int bw, int bh,
        int bx, int by, int radius, double strength, int offsetY)
    {
        if (radius < 1) radius = 1;
        var cov = new float[(long)w * h];
        int oy = by + offsetY;
        int x0 = Math.Max(0, bx), y0 = Math.Max(0, oy);
        int x1 = Math.Min(w, bx + bw), y1 = Math.Min(h, oy + bh);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                cov[(long)y * w + x] = body[((long)(y - oy) * bw + (x - bx)) * 4 + 3] / 255f;

        var scratch = new float[cov.Length];
        for (int p = 0; p < 3; p++) BoxBlur(cov, scratch, w, h, radius);

        for (long i = 0, px = 0; i < cov.Length; i++, px += 4)
        {
            float a = cov[i] * (float)strength;
            if (a <= 0.004f) continue;
            if (a > 1f) a = 1f;
            dst[px] = 0; dst[px + 1] = 0; dst[px + 2] = 0; // 纯黑阴影
            dst[px + 3] = (byte)(a * 255f);
        }
    }

    /// <summary>可分离盒式模糊（滑窗和，O(w·h)），边界延伸。tmp 为等长暂存。</summary>
    static void BoxBlur(float[] a, float[] tmp, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            float sum = 0;
            for (int i = -r; i <= r; i++) sum += a[row + Clamp(i, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[row + x] = sum * inv;
                sum += a[row + Clamp(x + r + 1, 0, w - 1)] - a[row + Clamp(x - r, 0, w - 1)];
            }
        }
        for (int x = 0; x < w; x++)
        {
            float sum = 0;
            for (int i = -r; i <= r; i++) sum += tmp[Clamp(i, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                a[(long)y * w + x] = sum * inv;
                sum += tmp[Clamp(y + r + 1, 0, h - 1) * w + x] - tmp[Clamp(y - r, 0, h - 1) * w + x];
            }
        }
    }

    static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    #endregion

    #region WGC 按窗口一次性取帧

    static (byte[] bgra, int w, int h) WgcGrab(IntPtr hwnd)
    {
        byte[]? res = null; int rw = 0, rh = 0; Exception? err = null;
        using var done = new ManualResetEventSlim();
        var th = new Thread(() =>
        {
            try { (res, rw, rh) = WgcGrabCore(hwnd); }
            catch (Exception e) { err = e; }
            finally { Wake.Set(done); }
        }) { IsBackground = true, Name = "WgcWinGrab" };
        th.Start();
        if (!done.Wait(3500)) throw new TimeoutException("wgc window grab timeout");
        if (err != null) throw err;
        if (res == null || rw < 2 || rh < 2) throw new InvalidOperationException("wgc window grab empty");
        return (res, rw, rh);
    }

    static (byte[], int, int) WgcGrabCore(IntPtr hwnd)
    {
        IntPtr deviceAbi = WgcNative.CreateDirect3DDevice();
        try
        {
            var item = WgcNative.CreateItemForWindow(hwnd);
            var insp = new WinRT.IInspectable(WinRT.ComWrappersSupport.GetObjectReferenceForInterface(deviceAbi));
            var device = WinRT.CastExtensions.As<IDirect3DDevice>(insp);
            var size = item.Size;
            if (size.Width < 2 || size.Height < 2) throw new InvalidOperationException("wgc item size 0");

            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            using var session = pool.CreateCaptureSession(item);
            try { session.IsCursorCaptureEnabled = false; } catch { }
            try { WgcNative.SetBorderRequired(session, false); } catch { } // 无黄框（未授权则忽略）

            using var are = new ManualResetEventSlim();
            byte[]? bgra = null; int fw = 0, fh = 0; string? ferr = null;
            TypedEventHandler<Direct3D11CaptureFramePool, object> onArrived = (s, _) =>
            {
                try
                {
                    using var frame = s.TryGetNextFrame();
                    if (frame is null) return; // 等下一帧
                    using var sb = Windows.Graphics.Imaging.SoftwareBitmap
                        .CreateCopyFromSurfaceAsync(frame.Surface).GetAwaiter().GetResult();
                    fw = sb.PixelWidth; fh = sb.PixelHeight;
                    if (fw < 2 || fh < 2) { ferr = "surface 0"; Wake.Set(are); return; }
                    bgra = new byte[fw * fh * 4];
                    using var bb = sb.LockBuffer(Windows.Graphics.Imaging.BitmapBufferAccessMode.Read);
                    using var reference = bb.CreateReference();
                    IntPtr data = WgcNative.ReferenceBytes(reference);
                    int stride = fw * 4; // SoftwareBitmap(Bgra8) 单平面连续（同 WgcLiveCapture）
                    for (int y = 0; y < fh; y++)
                        Marshal.Copy(IntPtr.Add(data, y * stride), bgra, y * fw * 4, fw * 4);
                    Wake.Set(are);
                }
                catch (Exception e) { ferr = e.Message; Wake.Set(are); }
            };
            pool.FrameArrived += onArrived;
            session.StartCapture();
            bool got = are.Wait(1500);
            pool.FrameArrived -= onArrived;

            if (!got) throw new TimeoutException("wgc window no frame");
            if (ferr != null) throw new InvalidOperationException("wgc window frame: " + ferr);
            if (bgra == null) throw new InvalidOperationException("wgc window null frame");
            return (bgra, fw, fh);
        }
        finally { Marshal.Release(deviceAbi); }
    }

    static RectI GetFrameBounds(IntPtr hwnd)
    {
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS,
                out Win32.RECT r, Marshal.SizeOf<Win32.RECT>()) == 0 && r.Right > r.Left && r.Bottom > r.Top)
            return new RectI(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        if (Win32.GetWindowRect(hwnd, out r) && r.Right > r.Left && r.Bottom > r.Top)
            return new RectI(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        throw new InvalidOperationException("no window bounds");
    }

    static int ShadowMargin(IntPtr hwnd)
    {
        uint dpi = Win32.GetDpiForWindow(hwnd);
        if (dpi < 96) dpi = 96;
        return (int)Math.Round(ShadowBasePx * dpi / 96.0);
    }
    #endregion

}
