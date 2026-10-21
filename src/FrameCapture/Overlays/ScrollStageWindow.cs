using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Overlays;

/// <summary>
/// 滚动截图取景层：铺满选区所在显示器，范围外压暗、范围内保持原亮度，范围上叠一圈 accent 描边。
/// 两条硬要求：整层鼠标穿透（用户要透过它滚目标窗口）、WDA_EXCLUDEFROMCAPTURE（压暗层绝不能被拼进长图）。
/// </summary>
public sealed class ScrollStageWindow : Window
{
    /// <value>范围外压暗色：约 55% 不透明黑，与深色主题底同档——压得住背景又不糊成纯黑。</value>
    static readonly Color Shade = Color.FromArgb(0x8C, 0x14, 0x14, 0x14);

    bool _closing;
    bool _syncing;

    /// <summary>
    /// 构造取景层：只建界面，调用方自行 <c>Show</c>。落点在构造期按显示器 bounds 换算成 DIP 定好，无首帧闪烁。
    /// 必须在 UI 线程构造。
    /// </summary>
    /// <param name="region">滚动选区，物理像素、虚拟桌面绝对坐标；伸出本屏的部分按本屏裁掉。</param>
    /// <param name="monitorDevice">选区所在显示器 device name；按设备取不到时按区域坐标就近回退。</param>
    /// <param name="entranceLeadMs">入场起跳前的等待（ms）。滚动流程的三层浮层同刻 Show，
    /// 由调用方给出各自的错位量，让它们按取景层 → 预览卡 → 控制条的顺序依次落位。</param>
    public ScrollStageWindow(RectI region, string monitorDevice, int entranceLeadMs = 0)
    {
        Title = "FrameCapture Scroll Stage";
        Fluent.BorderlessOverlay(this);
        // 压暗层要逐像素透明（范围内那块必须透出桌面原图）：全屏压暗层的既有做法就是分层窗，
        // 与选区窗同款；代价是这一层走软件渲染，静态四块矩形付得起。
        AllowsTransparency = true;
        SizeToContent = SizeToContent.Manual;   // 铺满整屏：尺寸由显示器 bounds 定，不按内容自适应
        Focusable = false;
        IsHitTestVisible = false;

        var mon = Monitors.ByDevice(monitorDevice) ?? Monitors.Nearest(region.X, region.Y);
        var (sx, sy) = Monitors.DpiScaleOf(mon);
        Left = mon.Bounds.X / sx; Top = mon.Bounds.Y / sy;
        Width = mon.Bounds.W / sx; Height = mon.Bounds.H / sy;

        // 选区先裁进本屏，再换算成本窗内的 DIP 坐标——虚拟桌面绝对坐标不能直接当画布坐标用
        int x0 = Math.Max(region.X, mon.Bounds.X), y0 = Math.Max(region.Y, mon.Bounds.Y);
        int x1 = Math.Min(region.Right, mon.Bounds.Right), y1 = Math.Min(region.Bottom, mon.Bounds.Bottom);
        double mw = mon.Bounds.W / sx, mh = mon.Bounds.H / sy;
        double lx = (x0 - mon.Bounds.X) / sx, ty = (y0 - mon.Bounds.Y) / sy;
        double rx = (x1 - mon.Bounds.X) / sx, by = (y1 - mon.Bounds.Y) / sy;

        var shade = new SolidColorBrush(Shade);
        shade.Freeze();
        var cv = new Canvas();
        // 范围外用上/下/左/右四块矩形围出来：比 OpacityMask 直白，且没有「遮罩坐标 vs 窗口坐标」的换算陷阱
        AddRect(cv, shade, 0, 0, mw, ty);
        AddRect(cv, shade, 0, by, mw, mh - by);
        AddRect(cv, shade, 0, ty, lx, by - ty);
        AddRect(cv, shade, rx, ty, mw - rx, by - ty);

        var frame = new Border
        {
            Width = Math.Max(0, rx - lx),
            Height = Math.Max(0, by - ty),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Theme.Accent),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(frame, lx); Canvas.SetTop(frame, ty);
        cv.Children.Add(frame);
        Content = cv;

        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            // 穿透四件套：本层永不激活、不进 Alt-Tab、不参与命中测试，滚轮与点击全部落到下面的目标窗口。
            // WS_EX_LAYERED 只在分层窗上成立（逐像素 alpha 由 AllowsTransparency 提供）；这里只补样式，
            // 绝不调 SetLayeredWindowAttributes——手工 alpha 在 DWM 合成下没人清，用了就是整层永久隐形。
            uint ex = Win32.GetWindowLongW(h, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongW(h, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE
                | Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW);
            Core.Fluent.ExcludeFromCapture(h);
            PlaceOnMonitor(mon.Bounds);
        };
        // 整屏层只做淡入：位移会让压暗矩形在入场那 190ms 里露出一条没压暗的屏幕边
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, leadMs: entranceLeadMs);
        Trace.Log($"scroll stage shown {mon.Bounds.W}x{mon.Bounds.H} region={region}");
    }

    /// <summary>带淡出收起本取景层；重复调用无副作用。</summary>
    public void CloseSelf()
    {
        if (_closing) return;
        _closing = true;
        Fluent.CloseWithFade(this);
    }

    /// <summary>按物理像素把本窗钉在显示器外接矩形上，再把同一矩形换算成 DIP 交回 WPF。
    /// 只给 DIP 尺寸会在高 DPI 下差出一两个像素的漏光边；只给物理尺寸 WPF 布局又会拿 DIP 回写覆盖它。</summary>
    /// <param name="b">目标显示器的物理像素外接矩形，虚拟桌面绝对坐标。</param>
    void PlaceOnMonitor(RectI b)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, b.X, b.Y, b.W, b.H, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        if (IsLoaded && !_syncing)
        {
            _syncing = true;
            try
            {
                var d = VisualTreeHelper.GetDpi(this);
                Left = b.X / d.DpiScaleX; Top = b.Y / d.DpiScaleY;
                Width = b.W / d.DpiScaleX; Height = b.H / d.DpiScaleY;
            }
            catch { }
            _syncing = false;
        }
    }

    /// <summary>往画布加一块压暗矩形；宽或高非正时什么都不加（选区贴到屏边时那一条本就该是空的）。</summary>
    /// <param name="cv">承载压暗矩形的画布。</param>
    /// <param name="fill">压暗画刷，四块共用同一份冻结实例。</param>
    /// <param name="x">矩形左上角 X，本窗内 DIP。</param>
    /// <param name="y">矩形左上角 Y，本窗内 DIP。</param>
    /// <param name="w">矩形宽，DIP。</param>
    /// <param name="h">矩形高，DIP。</param>
    static void AddRect(Canvas cv, Brush fill, double x, double y, double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        var r = new Rectangle { Fill = fill, Width = w, Height = h };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
        cv.Children.Add(r);
    }
}
