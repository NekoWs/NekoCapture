using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>选区旁的无边框长图预览，按宽度缩放并随内容增高，超出屏幕时显示最新尾部。</summary>
public sealed class ScrollPreviewWindow : Window
{
    const double PreviewWidth = 280, Gap = 12, ScreenMargin = 4;
    readonly Image _img;
    readonly int _maxW, _maxH;
    readonly double _sx, _sy, _anchorTop, _screenTop, _screenBottom;
    bool _closing;
    int _lastW, _lastH, _imageGeneration;

    /// <summary>创建不激活、不接收鼠标输入且排除捕获的预览窗口，调用方负责显示。</summary>
    /// <param name="region">选区的虚拟桌面物理像素坐标。</param>
    /// <param name="monitorDevice">选区所在显示器，未找到时使用最近的显示器。</param>
    /// <param name="entranceLeadMs">入场动画的延迟毫秒数。</param>
    public ScrollPreviewWindow(RectI region, string monitorDevice, int entranceLeadMs = 0)
    {
        Title = "FrameCapture Scroll Preview";
        Fluent.BorderlessOverlay(this);
        AllowsTransparency = true;
        SizeToContent = SizeToContent.Manual;

        var mon = Monitors.ByDevice(monitorDevice) ?? Monitors.Nearest(region.X, region.Y);
        (_sx, _sy) = Monitors.DpiScaleOf(mon);
        double gap = Gap * _sx, margin = ScreenMargin * _sx;
        double rightRoom = mon.Bounds.Right - margin - region.Right - gap;
        double leftRoom = region.X - gap - mon.Bounds.X - margin;
        double preferred = Math.Min(PreviewWidth * _sx, mon.Bounds.W - 2 * margin);
        bool onRight = rightRoom >= preferred || rightRoom >= leftRoom;
        double room = onRight ? rightRoom : leftRoom;
        double width = Math.Min(preferred, Math.Max(120 * _sx, room));
        _maxW = Math.Max(1, (int)width);
        _maxH = Math.Max(1, (int)(mon.Bounds.H - ScreenMargin * _sy * 2));
        double initialWidth = Math.Min(Math.Max(1, region.W), _maxW);
        double left = onRight ? region.Right + gap : region.X - gap - initialWidth;
        Left = Math.Clamp(left, mon.Bounds.X + margin, mon.Bounds.Right - margin - initialWidth) / _sx;
        _screenTop = mon.Bounds.Y / _sy + ScreenMargin;
        _screenBottom = mon.Bounds.Bottom / _sy - ScreenMargin;
        _anchorTop = Math.Clamp(region.Y / _sy, _screenTop, _screenBottom - 1);
        Width = initialWidth / _sx;
        Height = Math.Max(1, Math.Min(_maxH, Math.Ceiling(region.H * initialWidth / Math.Max(1, region.W)))) / _sy;
        Top = Math.Max(_screenTop, Math.Min(_anchorTop, _screenBottom - Height));

        _img = new Image
        {
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
        var viewport = new Grid { ClipToBounds = true, IsHitTestVisible = false };
        viewport.Children.Add(_img);
        Content = viewport;
        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            uint ex = Win32.GetWindowLongW(h, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongW(h, Win32.GWL_EXSTYLE,
                ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT);
            Fluent.ExcludeFromCapture(h);
        };
        Closed += (_, _) => { _closing = true; ++_imageGeneration; _img.Source = null; };
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, leadMs: entranceLeadMs);
    }

    /// <summary>后台生成有界预览像素；长图增长不改变缩放比例，过期结果不显示。</summary>
    /// <param name="bgra">已拼接长图，行主序 BGRA。</param>
    /// <param name="w">长图的物理像素宽度。</param>
    /// <param name="h">长图的物理像素高度。</param>
    public async void SetImage(byte[] bgra, int w, int h)
    {
        if (_closing || w <= 0 || h <= 0 || (w == _lastW && h == _lastH)) return;
        int generation = ++_imageGeneration;
        BitmapSource? src;
        try
        {
            src = await BackgroundWork.Run(() =>
            {
                var (pixels, width, height) = Viewport(bgra, w, h, _maxW, _maxH);
                if (width <= 0 || height <= 0) return null;
                var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                bitmap.Freeze(); return bitmap;
            });
        }
        catch (Exception ex) { Trace.Log("scroll preview: " + ex); return; }
        if (_closing || generation != _imageGeneration || src == null) return;
        double widthDip = src.PixelWidth / _sx, heightDip = src.PixelHeight / _sy;
        _img.Width = widthDip; _img.Height = heightDip; _img.Source = src;
        Width = widthDip;
        double top = Math.Max(_screenTop, Math.Min(_anchorTop, _screenBottom - heightDip));
        if (IsVisible)
        {
            Animate(HeightProperty, heightDip);
            Animate(TopProperty, top);
        }
        else { Height = heightDip; Top = top; }
        _lastW = w; _lastH = h;
    }

    void Animate(DependencyProperty property, double target)
    {
        double current = (double)GetValue(property);
        if (Math.Abs(current - target) < 0.1) return;
        SetValue(property, target);
        BeginAnimation(property, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>淡出并关闭预览，重复调用无副作用。</summary>
    public void CloseSelf()
    {
        if (_closing) return;
        _closing = true;
        Fluent.CloseWithFade(this);
    }

    /// <summary>仅按宽度缩小长图，屏幕高度只限制可见尾部，不参与缩放；输出分配不随长图增长。</summary>
    /// <param name="bgra">源图的连续 BGRA 像素。</param>
    /// <param name="w">源图宽度。</param>
    /// <param name="h">源图高度。</param>
    /// <param name="maxW">预览宽度上限。</param>
    /// <param name="maxH">可见高度上限。</param>
    /// <returns>预览像素及尺寸；非法尺寸或缓冲不足时返回空图。</returns>
    public static (byte[] Bgra, int W, int H) Viewport(byte[] bgra, int w, int h, int maxW, int maxH)
    {
        if (bgra == null || w <= 0 || h <= 0 || maxW <= 0 || maxH <= 0
            || (long)w * h > bgra.Length / 4) return (Array.Empty<byte>(), 0, 0);
        int ow = Math.Min(w, maxW);
        double scale = (double)ow / w;
        int oh = (int)Math.Min(maxH, Math.Ceiling(h * scale));
        int sourceHeight = Math.Min(h, (int)Math.Ceiling(oh / scale));
        int firstRow = h - sourceHeight;
        var pixels = new byte[checked(ow * oh * 4)];
        for (int y = 0; y < oh; y++)
        {
            int sy = y == oh - 1 ? h - 1 : firstRow + (int)((long)y * sourceHeight / oh);
            if (ow == w) Buffer.BlockCopy(bgra, sy * w * 4, pixels, y * ow * 4, ow * 4);
            else for (int x = 0; x < ow; x++)
            {
                int sx = x == ow - 1 ? w - 1 : (int)((long)x * w / ow);
                Buffer.BlockCopy(bgra, (sy * w + sx) * 4, pixels, (y * ow + x) * 4, 4);
            }
        }
        return (pixels, ow, oh);
    }

    /// <summary>
    /// 长图 → 缩略图：整行整列抽样（不插值、不模糊），输出尺寸恒 ≤ <c>maxW×maxH</c>，首行末行必被取到。
    /// 两个方向共用同一步长 <c>max(ceil(w/maxW), ceil(h/maxH))</c>：各轴独立取步长会让输出宽高比偏离源图，
    /// 竖长的文档缩略图会被横向拉宽；共用步长保证缩略图与源图同比例。
    /// 末位下标改写为 <c>count-1</c>——长图最新拼上的那一行永远在预览里。
    /// </summary>
    /// <param name="bgra">源图本体，行主序 BGRA，行距 <c>w*4</c>；长度不足 <c>w*h*4</c> 视为无效入参。</param>
    /// <param name="w">源图宽，像素。</param>
    /// <param name="h">源图高，像素。</param>
    /// <param name="maxW">输出宽上限，像素。</param>
    /// <param name="maxH">输出高上限，像素。</param>
    /// <returns>缩略后的 BGRA 与宽高，宽高比与源图一致；两个方向都不需要抽样时逐字节原样输出，退化入参返回空数组与 0×0。</returns>
    public static (byte[] Bgra, int W, int H) Thumbnail(byte[] bgra, int w, int h, int maxW, int maxH)
    {
        if (bgra == null || w <= 0 || h <= 0 || maxW <= 0 || maxH <= 0) return (Array.Empty<byte>(), 0, 0);
        if ((long)bgra.Length < (long)w * h * 4) return (Array.Empty<byte>(), 0, 0);
        int stride = Math.Max((w + maxW - 1) / maxW, (h + maxH - 1) / maxH);
        int[] ys = SampleIndices(h, stride), xs = SampleIndices(w, stride);
        int ow = xs.Length, oh = ys.Length;
        var dst = new byte[ow * oh * 4];
        if (ow == w)
            for (int r = 0; r < oh; r++)   // 列不抽样 → 整行连续拷贝，两向都不抽时结果与源逐字节相同
                Buffer.BlockCopy(bgra, (int)((long)ys[r] * w * 4), dst, r * ow * 4, ow * 4);
        else
            for (int r = 0; r < oh; r++)
                for (int c = 0; c < ow; c++)
                    Buffer.BlockCopy(bgra, (int)(((long)ys[r] * w + xs[c]) * 4), dst, (r * ow + c) * 4, 4);
        return (dst, ow, oh);
    }

    /// <summary>在 <c>0..count-1</c> 上按固定步长抽样，末位固定取 <c>count-1</c>。</summary>
    /// <param name="count">源方向上的像素数，须 ≥ 1。</param>
    /// <param name="stride">抽样步长，须 ≥ 1。两个方向共用同一步长，缩略图才与源图同宽高比。</param>
    /// <returns>严格递增的下标表；首元素恒为 0，末元素恒为 <c>count-1</c>。</returns>
    static int[] SampleIndices(int count, int stride)
    {
        int n = (count + stride - 1) / stride;
        var idx = new int[n];
        for (int i = 0; i < n; i++) idx[i] = i * stride;
        idx[n - 1] = count - 1;
        return idx;
    }
}
