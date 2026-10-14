using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 滚动截图的实时预览卡：贴在选区右侧（放不下翻到左侧），显示已拼接长图的缩略与实时尺寸。
/// 长图按图区尺寸降采样后再上屏——累积图动辄上万行，整幅转 BitmapSource 会把内存吃穿。
/// 图区用 <see cref="Stretch.Uniform"/>：长图再长也只缩进容器，永不溢出面板。
/// </summary>
public sealed class ScrollPreviewWindow : Window
{
    const double PanelW = 200;    // 面板宽（DIP）
    const double Gap = 12;        // 面板与选区的间距（DIP）
    const double PadX = 12, PadTop = 10, PadBottom = 12;
    const double HeadH = 20, HeadGap = 8;

    readonly Image _img;
    readonly TextBlock _size;
    readonly int _maxW, _maxH;   // 缩略上限＝图区的物理像素尺寸
    bool _closing;
    int _lastW, _lastH;          // 上次上屏的长图尺寸，同尺寸重复调用不重绘

    /// <summary>
    /// 构造预览卡：只建界面，调用方自行 <c>Show</c>。落点在构造期定好，缩略上限按图区尺寸与屏幕 DPI 算定。
    /// 必须在 UI 线程构造。
    /// </summary>
    /// <param name="region">滚动选区，物理像素、虚拟桌面绝对坐标；面板贴着它的外侧放。</param>
    /// <param name="monitorDevice">选区所在显示器 device name；按设备取不到时按区域坐标就近回退。</param>
    public ScrollPreviewWindow(RectI region, string monitorDevice)
    {
        Title = "FrameCapture Scroll Preview";
        Fluent.BorderlessOverlay(this);

        var mon = Monitors.ByDevice(monitorDevice) ?? Monitors.Nearest(region.X, region.Y);
        var (sx, sy) = Monitors.DpiScaleOf(mon);
        double monH = mon.Bounds.H / sy;
        double panelH = Math.Min(region.H / sy + 40, monH - 40);
        if (panelH < 150) panelH = Math.Min(150, Math.Max(120, monH - 8));
        double imgH = Math.Max(48, panelH - PadTop - PadBottom - HeadH - HeadGap);
        _maxW = Math.Max(1, (int)((PanelW - 2 * PadX) * sx));
        _maxH = Math.Max(1, (int)(imgH * sy));

        // 落点：优先选区右侧；越过该屏右界则翻到左侧；两侧都放不下贴选区内右缘，最后再钳进屏内
        int gapPx = (int)(Gap * sx), panelWpx = (int)(PanelW * sx), panelHpx = (int)(panelH * sy);
        int left = region.Right + gapPx;
        if (left + panelWpx > mon.Bounds.Right) left = region.X - gapPx - panelWpx;
        if (left < mon.Bounds.X) left = region.Right - panelWpx - gapPx;
        left = Math.Max(mon.Bounds.X + 4, Math.Min(left, mon.Bounds.Right - panelWpx - 4));
        int top = region.Y;
        if (top + panelHpx > mon.Bounds.Bottom) top = mon.Bounds.Bottom - panelHpx;
        if (top < mon.Bounds.Y) top = mon.Bounds.Y;
        Left = left / sx; Top = top / sy;

        var card = new Border
        {
            Width = PanelW,
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Stroke.R, Theme.Stroke.G, Theme.Stroke.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(PadX, PadTop, PadX, PadBottom),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black },
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new Grid { Height = HeadH };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = "预览",
            FontSize = 13.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Theme.Text),
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _size = new TextBlock
        {
            Text = "—",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Accent),
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_size, 1);
        head.Children.Add(_size);
        root.Children.Add(head);

        _img = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
        // 图区底色比卡片再深一档：白底文档贴在同色卡片上看不出边界
        Color well = Theme.IsDark ? Color.FromRgb(0x1B, 0x1B, 0x1E) : Color.FromRgb(0xE6, 0xE6, 0xEA);
        var holder = new Border
        {
            Height = imgH,
            Margin = new Thickness(0, HeadGap, 0, 0),
            Background = new SolidColorBrush(well),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = _img,
        };
        Grid.SetRow(holder, 1);
        root.Children.Add(holder);

        card.Child = root;
        Content = card;

        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(h);
            // 不激活、不进 Alt-Tab：预览卡只是给人看的，滚动目标窗口必须一直握着焦点
            uint ex = Win32.GetWindowLongW(h, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongW(h, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
            if (Environment.GetEnvironmentVariable("FC_TOAST_NOEXCLUDE") != "1")
                Win32.SetWindowDisplayAffinity(h, Win32.WDA_EXCLUDEFROMCAPTURE);
        };

        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
        Trace.Log($"scroll preview shown well={_maxW}x{_maxH}");
    }

    /// <summary>刷新一帧预览：长图先降采样到图区尺寸再上屏。与上次尺寸相同则直接返回，不重绘。</summary>
    /// <param name="bgra">已拼接长图本体，行主序 BGRA，行距 <c>w*4</c>。</param>
    /// <param name="w">长图宽，物理像素。</param>
    /// <param name="h">长图高，物理像素。</param>
    public void SetImage(byte[] bgra, int w, int h)
    {
        if (_closing || w <= 0 || h <= 0 || (w == _lastW && h == _lastH)) return;
        var (px, tw, th) = Thumbnail(bgra, w, h, _maxW, _maxH);
        if (tw <= 0 || th <= 0) return;
        var src = BitmapSource.Create(tw, th, 96, 96, PixelFormats.Bgra32, null, px, tw * 4);
        src.Freeze();
        _img.Source = src;
        _size.Text = $"{w}×{h} px";
        _lastW = w; _lastH = h;
    }

    /// <summary>带淡出收起本预览卡；重复调用无副作用。</summary>
    public void CloseSelf()
    {
        if (_closing) return;
        _closing = true;
        Fluent.CloseWithFade(this);
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
