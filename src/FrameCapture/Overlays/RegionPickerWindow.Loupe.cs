using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Native;
using Loc = FrameCapture.Core.Loc;
using K = FrameCapture.Core.K;

namespace FrameCapture.Overlays;

/// <summary>
/// RegionPickerWindow partial：放大镜与读数——loupe 绘制与刷新、坐标/尺寸读数、取色与格式切换、
/// 全局钩子代收的选区窗按键、选区高亮与命中判定。
/// 本 partial 内所有坐标字段都成对出现：DIP（WPF 布局用）与物理像素（虚拟桌面绝对坐标，捕获/吸附用）。
/// </summary>
public sealed partial class RegionPickerWindow : Window
{
    private Core.ColorCopyFormat _colorFormat; // 按 C 复制颜色时的文本形式；按 F 可循环切换（Hex→RGB→HSL）
    private (int r, int g, int b)? _lastRgb; // 放大镜中心最近一次像素色，供 F 切换格式后就地刷新读数
    private Border? _loupe;
    private Image? _loupeImg;
    private Rectangle? _loupeMarker;
    private Border? _loupeSwatch;
    private TextBlock? _loupeHex;
    private TextBlock? _loupeInfo;
    private string _loupeHexText = ""; // 当前放大镜中心像素 #RRGGBB，供 C 键复制
    private const int LoupeSpan = 15;   // 放大镜取源边长（源像素）
    private const double LoupeSize = 150; // 放大镜显示边长（DIP）
    private Rectangle _spotStroke = null!;
    private Border _badge = null!;
    private TextBlock _badgeText = null!;

    #region 选区交互：按键代收、放大镜、命中与收尾（非激活窗收不到键）
    /// <summary>由 HotkeyManager 全局低级键盘钩子在 PickerActive 时调用。返回 true=本键归选区窗处理（吞掉，前台收不到）。
    /// 只接管无 Ctrl/Alt/Win 的裸键：Ctrl+C 等留给前台，Ctrl+Esc 是开始菜单，含修饰键的组合放行给手势层。
    /// 动作经 Dispatcher.BeginInvoke 投递到干净的 dispatcher 轮次执行（与浮窗/滚动同款，避免在输入链里做重活）。</summary>
    private bool OnPickerKey(uint vk)
    {
        // 确认/取消用设置里配的键位（默认 Enter / Esc），先判它们：调整态也要管用
        var pickerSettings = Services.AppHost.Current?.Settings.Current;
        if (pickerSettings != null && MatchesChord(pickerSettings.PickerCancelChord, vk))
        {
            Dispatcher.BeginInvoke(() => Finish(null));
            return true;
        }
        if (pickerSettings != null && MatchesChord(pickerSettings.PickerConfirmChord, vk))
        {
            if (_sel != null) { Dispatcher.BeginInvoke(() => ConfirmSelection()); return true; }
            if (_existing == null) return false;
            Dispatcher.BeginInvoke(() => Finish(_existing.Value));
            return true;
        }
        if (Win32.IsKeyDown(Win32.VK_CONTROL) || Win32.IsKeyDown(Win32.VK_MENU)
            || Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN)) return false;
        switch (vk)
        {
            case 0x1B: // Esc 取消
                Dispatcher.BeginInvoke(() => Finish(null));
                return true;
            case 0x0D: // Enter 确认上次选区
                if (_existing == null) return false;
                Dispatcher.BeginInvoke(() => Finish(_existing.Value));
                return true;
            case 0x43: // C 复制光标下像素色
                if (_loupeHexText.Length == 0) return false;
                {
                    var t = _loupeHexText;
                    Dispatcher.BeginInvoke(() =>
                    {
                        try { Clipboard.SetText(t); } catch { }
                        if (_loupeHex != null) _loupeHex.Text = Loc.T(K.Capture.Copied) + t;
                        // 选区时按 C 取过的色并进编辑器「最近用色」：看中的那抹红，
                        // 截完图在调色板第一格就能点到，不用回编辑器手输 HEX。存的永远是 #RRGGBB，
                        // 与当前显示成 RGB/HSL 无关。
                        if (_lastRgb is { } lc)
                            Services.AppHost.Current.Settings.Mutate(s => s.CustomColors = Core.CustomPick.RememberColor(
                                s.CustomColors, Core.ColorFormat.Format(lc.r, lc.g, lc.b, Core.ColorCopyFormat.Hex)));
                    });
                }
                return true;
            case 0x46: // F 循环取色复制格式 Hex→RGB→HSL
                Dispatcher.BeginInvoke(CycleColorFormat);
                return true;
            case 0x09: // Tab 上钻控件层级
                if (_curElem == null) return false;
                Dispatcher.BeginInvoke(DrillUpElement);
                return true;
            default: return false;
        }
    }

    /// <summary>钩子给来的虚拟键是否正是这个组合键：主键与修饰键逐位一致才算。</summary>
    /// <param name="chord">组合键串，如 <c>Enter</c> / <c>Ctrl+Enter</c>；空串或解析失败返回 false。</param>
    /// <param name="vk">钩子给来的虚拟键码。</param>
    /// <returns>完全匹配返回 true。</returns>
    private static bool MatchesChord(string? chord, uint vk)
    {
        if (string.IsNullOrWhiteSpace(chord)) return false;
        if (Core.EditorChord.Parse(chord) is not { } ch) return false;
        if (System.Windows.Input.KeyInterop.VirtualKeyFromKey(ch.Key) != vk) return false;
        var mods = ch.Mods;
        bool ctrl = (mods & System.Windows.Input.ModifierKeys.Control) != 0;
        bool alt = (mods & System.Windows.Input.ModifierKeys.Alt) != 0;
        bool shift = (mods & System.Windows.Input.ModifierKeys.Shift) != 0;
        bool win = (mods & System.Windows.Input.ModifierKeys.Windows) != 0;
        return Win32.IsKeyDown(Win32.VK_CONTROL) == ctrl
            && Win32.IsKeyDown(Win32.VK_MENU) == alt
            && Win32.IsKeyDown(Win32.VK_SHIFT) == shift
            && (Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN)) == win;
    }
    // 循环取色复制格式 Hex→RGB→HSL，就地刷新放大镜读数并持久化（下次捕获/设置窗同步）
    private void CycleColorFormat()
    {
        _colorFormat = (Core.ColorCopyFormat)(((int)_colorFormat + 1) % 3);
        if (_lastRgb is { } c && _loupeHex != null)
        {
            _loupeHexText = Core.ColorFormat.Format(c.r, c.g, c.b, _colorFormat);
            _loupeHex.Text = _loupeHexText;
        }
        Services.AppHost.Current.Settings.Mutate(s => s.ColorCopyFormat = Core.ColorFormat.ToSetting(_colorFormat));
    }

    // Tab 上钻控件层级：输入框 → 表单容器 → … → 整窗。锁定结果，光标微动不会被 hover 抢回子控件。
    private void DrillUpElement()
    {
        if (_curElem == null) return;
        var up = Core.ElementSnap.Parent(_curElem);
        if (up != null)
        {
            _curElem = up;
            _drillLock = true;
            _drillAnchor = Mouse.GetPosition(this);
            ShowSpot(up.Rect);
        }
    }

    /// <summary>物理矩形 → 本窗内的 DIP 矩形：先减显示器原点，再按可视树 DPI 缩放。</summary>
    /// <param name="phys">物理像素、虚拟桌面绝对坐标；原点不在本显示器上时会得到负坐标。</param>
    /// <returns>相对本窗左上角的 DIP 矩形，可直接喂 Canvas.SetLeft/Top。</returns>
    private Rect ToDip(RectI phys)
    {
        var tr = VisualTreeHelper.GetDpi(this);
        return new Rect(
            (phys.X - _monitorPhys.X) / tr.DpiScaleX,
            (phys.Y - _monitorPhys.Y) / tr.DpiScaleY,
            phys.W / tr.DpiScaleX,
            phys.H / tr.DpiScaleY);
    }

    /// <summary>本窗内 DIP 光标位置 → 物理像素、虚拟桌面绝对坐标（加显示器原点 + DPI 缩放，四舍五入）。</summary>
    /// <param name="dipInWindow">相对本窗左上角的 DIP 位置；本窗铺满整个显示器，故等价于屏内 DIP。</param>
    /// <returns>取整后的物理坐标、虚拟桌面绝对坐标；不做越界钳制，光标贴边时可能落在显示器之外。</returns>
    private (int X, int Y) WindowPosToPhys(Point dipInWindow)
    {
        var tr = VisualTreeHelper.GetDpi(this);
        return (
            _monitorPhys.X + (int)Math.Round(dipInWindow.X * tr.DpiScaleX),
            _monitorPhys.Y + (int)Math.Round(dipInWindow.Y * tr.DpiScaleY));
    }

    /// <summary>遮罩几何：外框 + 可选洞，靠 EvenOdd 填充规则让洞内透明、洞外压暗。</summary>
    /// <param name="outer">遮罩外框（DIP，通常 = 整窗）。</param>
    /// <param name="hole">要挖亮的选区（DIP）；null = 整屏均匀压暗，不挖洞。</param>
    /// <returns>可直接赋给 <c>Path.Data</c> 的几何；洞与外框不相交时等价于不挖洞。</returns>
    private static Geometry BuildMask(Rect outer, Rect? hole)
    {
        var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
        g.Children.Add(new RectangleGeometry(outer));
        if (hole != null) g.Children.Add(new RectangleGeometry(hole.Value));
        return g;
    }

    /// <summary>更新放大镜：定格模式读定格快照；实时模式每次移动实时重抓光标周围小区域（跟画面）。
    /// 标记光标正下方像素，读数显示坐标(+选区尺寸)与像素色。</summary>
    private void UpdateLoupe(Point dip, (int X, int Y) phys, RectI? sel)
    {
        if (!_magnifier || _loupe == null || _loupeImg == null || _loupeMarker == null || _loupeInfo == null) return;
        int sx = phys.X - _monitorPhys.X, sy = phys.Y - _monitorPhys.Y;

        // 取源：定格读定格快照；实时每次移动重抓光标周围小区域，放大镜因此始终跟画面变
        byte[]? srcBgra; int srcW, cx, cy, size, px, py;
        if (_freeze)
        {
            if (_snap == null) return; // 定格帧还没到位
            (cx, cy, size) = Core.MagnifierMath.Crop(sx, sy, _snap.PixelWidth, _snap.PixelHeight, LoupeSpan);
            _loupeImg.Source = new System.Windows.Media.Imaging.CroppedBitmap(_snap, new Int32Rect(cx, cy, size, size));
            srcBgra = _snapBgra; srcW = _snapW; px = sx; py = sy; // 取色用整幅快照坐标
        }
        else
        {
            (cx, cy, size) = Core.MagnifierMath.Crop(sx, sy, _monitorPhys.W, _monitorPhys.H, LoupeSpan);
            if (size < 2) return;
            var g = LiveCapture.Grab(new RectI(_monitorPhys.X + cx, _monitorPhys.Y + cy, size, size));
            if (g == null) return;
            _loupeImg.Source = Services.BgraImage.ToSource(g);
            srcBgra = g.Bgra; srcW = g.W; px = sx - cx; py = sy - cy; // 取色用裁片内坐标
        }

        double cell = LoupeSize / size;
        _loupeMarker.Width = _loupeMarker.Height = cell;
        Canvas.SetLeft(_loupeMarker, (sx - cx) * cell);
        Canvas.SetTop(_loupeMarker, (sy - cy) * cell);
        _loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var (lx, ly) = Core.MagnifierMath.Place(dip.X, dip.Y, _loupe.DesiredSize.Width, _loupe.DesiredSize.Height, Width, Height, 18);
        Canvas.SetLeft(_loupe, lx); Canvas.SetTop(_loupe, ly);
        _loupeInfo.Text = sel != null ? $"{phys.X}, {phys.Y}   {sel.Value.W}×{sel.Value.H}" : $"{phys.X}, {phys.Y}";
        if (srcBgra != null && _loupeSwatch != null && _loupeHex != null)
        {
            var (r, g, b) = Core.MagnifierMath.PixelRgb(srcBgra, srcW, px, py);
            _lastRgb = ((int)r, (int)g, (int)b); // 记住当前色，F 切格式时就地重算读数
            _loupeHexText = Core.ColorFormat.Format(r, g, b, _colorFormat);
            _loupeSwatch.Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
            _loupeHex.Text = _loupeHexText;
        }
        _loupe.Visibility = Visibility.Visible;
    }

    private RectI? SnapWindowAt((int X, int Y) p)
    {
        var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        // snapContent: prefer client-area rects (exclude window frame / title bar, e.g. game viewport)
        return Core.SnapPick.Pick(
            WindowEnum.ListTopmostCandidates(self).Select(t => _snapContent ? t.Client : t.Bounds), p.X, p.Y);
    }

    private void ShowSpot(RectI? phys)
    {
        // 放大镜元素是懒搭的，没搭出来就没有可画的目标；取成局部量，守卫才覆盖得住后面整段
        var path = _spotPath; var hole = _holeFill;
        if (path == null) return;
        if (phys == null)
        {
            _spotStroke.Visibility = Visibility.Collapsed;
            _badge.Visibility = Visibility.Collapsed;
            path.Data = BuildMask(new Rect(0, 0, Width, Height), null);
            if (hole != null) hole.Data = null;
            return;
        }
        var r = ToDip(phys.Value);
        _spotStroke.Visibility = Visibility.Visible;
        Canvas.SetLeft(_spotStroke, r.X); Canvas.SetTop(_spotStroke, r.Y);
        _spotStroke.Width = r.Width; _spotStroke.Height = r.Height;
        path.Data = BuildMask(new Rect(0, 0, Width, Height), r);
        if (hole != null) hole.Data = new RectangleGeometry(r);
        // 尺寸徽标：贴选区左上角外侧；顶部空间不足时贴入内侧
        _badgeText.Text = $"{phys.Value.W} × {phys.Value.H}";
        _badge.Visibility = Visibility.Visible;
        _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double bw = _badge.DesiredSize.Width, bh = _badge.DesiredSize.Height;
        double bx = r.X - 2 < 0 ? r.X + r.Width - bw - 2 : Math.Min(r.X - 2, Width - bw - 4);
        double by = r.Y - bh - 6 < 0 ? Math.Min(r.Y + 4, Height - bh - 4) : r.Y - bh - 6;
        Canvas.SetLeft(_badge, bx); Canvas.SetTop(_badge, by);
    }

    private void Finish(RectI? region) => Finish(region, IntPtr.Zero);

    private void Finish(RectI? region, IntPtr windowHwnd)
    {
        if (_finished) return;
        _finished = true;
        Result?.Invoke(region, windowHwnd);
        Close();
    }

    /// <summary>点/双击吸附到的窗口时，若开关开且该窗未被更靠上的窗遮挡，返回其 (HWND, 整窗 frame)；
    /// 否则 null（回退区域截图）。frame 用 DWM 扩展边界（含标题栏与投影前的整窗边框）。</summary>
    private (IntPtr Hwnd, RectI Bounds)? WindowHitForCapture((int X, int Y) p)
    {
        var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var cands = WindowEnum.ListTopmostCandidates(self).ToList(); // Z 序顶在前
        var pick = Core.SnapPick.PickWindow(
            cands.Select(t => (t.Hwnd, _snapContent ? t.Client : t.Bounds)), p.X, p.Y);
        if (pick == null) return null;
        int idx = cands.FindIndex(t => t.Hwnd == pick.Value.Hwnd);
        if (idx < 0) return null;
        var frame = cands[idx].Bounds;
        // 更靠上的候选窗与本窗 frame 相交 → 遮挡 → 回退区域截图
        if (Core.SnapPick.AnyAboveIntersects(cands.Select(t => t.Bounds).ToList(), idx, frame)) return null;
        return (pick.Value.Hwnd, frame);
    }
    #endregion

}
