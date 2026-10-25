using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameCapture.Capture;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>
/// 选区的可调整态：拖完一次之后不立刻出图，先给出一个可拖边角改大小、可整块移动的框，
/// 右下角摆一个勾一个叉确认/取消。拖边角时会吸到附近的控件边界——吸附到子控件之后想把
/// 周围兄弟控件一起框进来，靠这个拖着吸一下就够。
/// </summary>
public sealed partial class RegionPickerWindow
{
    /// <summary>当前调整中的选区（物理像素）；null 表示还在拖框新建阶段。</summary>
    private RectI? _sel;

    /// <summary>这次按下抓的是哪条边/角，或整块移动。</summary>
    private GrabKind _grab;

    /// <summary>按下时的物理坐标与那时的选区，位移都相对它们算。</summary>
    private int _grabX, _grabY;
    private RectI _selAtGrab;

    /// <summary>八个控制点：四角 + 四边中点。</summary>
    private readonly Rectangle[] _handles = new Rectangle[8];

    /// <summary>确认/取消两个按钮（画在遮罩上，命中自己判）。</summary>
    private Border? _okButton, _cancelButton;

    /// <summary>点选窗口时记下它的句柄与边界：选区一动就不再按窗口抓，留给普通区域截图。</summary>
    private IntPtr _selHwnd;
    private RectI _selHwndBounds;

    /// <summary>本窗句柄，交给吸附做排除。</summary>
    private IntPtr _selfHwnd;

    /// <summary>控制点边长（DIP）。</summary>
    private const double HandleSize = 11;

    /// <summary>边角判定半径（DIP）：太小抓不住，太大会把整块移动吃掉。</summary>
    private const double GrabTol = 7;

    /// <summary>上次问吸附的时间：拖边角时按 80 ms 节流。</summary>
    private DateTime _snapAt = DateTime.MinValue;

    /// <summary>吸附容差（DIP）。</summary>
    private const double SnapTol = 10;

    /// <summary>按钮直径（DIP）。</summary>
    private const double ButtonSize = 32;

    /// <summary>本窗的 DPI 缩放。</summary>
    private double DpiScale
    {
        get { try { return VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { return 1; } }
    }

    /// <summary>调整选区时是否吸附控件；按住 Alt 临时关。</summary>
    private static bool ResizeSnapOn =>
        (SettingsService.Shared?.Current.ResizeSnapToControls ?? true)
        && !FrameCapture.Native.Win32.IsKeyDown(FrameCapture.Native.Win32.VK_MENU);

    /// <summary>建控制点与两个按钮：都不吃鼠标事件，命中在 OnDown/OnUp 里自己判。</summary>
    private void BuildAdjustChrome()
    {
        for (int i = 0; i < _handles.Length; i++)
        {
            var h = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0x0B, 0x0B, 0x0B)),
                StrokeThickness = 1.5,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            _handles[i] = h;
            _canvas.Children.Add(h);
        }
        _okButton = AdjustButton("\uE73E", Theme.Accent, Loc.T(K.Capture.ConfirmSelection));
        _cancelButton = AdjustButton("\uE711", null, Loc.T(K.Capture.CancelSelection));
        _canvas.Children.Add(_okButton);
        _canvas.Children.Add(_cancelButton);
    }

    /// <summary>做一个圆形按钮：勾用强调色底，叉用深色底。</summary>
    /// <param name="glyph">Segoe Fluent Icons 字形。</param>
    /// <param name="accent">强调色；null 表示用中性底。</param>
    /// <param name="tip">悬停提示。</param>
    /// <returns>按钮边框。</returns>
    private Border AdjustButton(string glyph, Color? accent, string tip) => new()
    {
        Width = ButtonSize,
        Height = ButtonSize,
        CornerRadius = new CornerRadius(ButtonSize / 2),
        Background = new SolidColorBrush(accent ?? Color.FromArgb(0xE6, 0x1B, 0x1B, 0x1B)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
        ToolTip = tip,
        Child = Fluent.Glyph(glyph, 14, new SolidColorBrush(accent is { } c ? Fluent.OnAccentColor(c) : Colors.White)),
    };

    /// <summary>进入调整态并重画：框、控制点、按钮、尺寸徽标一起更新。</summary>
    /// <param name="sel">选区（物理像素）。</param>
    private void ShowSelection(RectI sel)
    {
        if (_handles[0] == null || _okButton == null) BuildAdjustChrome();
        // 调整态收掉放大镜：这时光标是在改框，不是取色；留着它挡在选区边上也碍事
        if (_loupe != null) _loupe.Visibility = Visibility.Collapsed;
        if (_loupeHex != null) _loupeHexText = "";
        _sel = sel;
        ShowSpot(sel);
        LayoutAdjustChrome(sel);
        if (_hintLabel != null) _hintLabel.Text = Loc.T(K.Capture.AdjustHint);
    }

    /// <summary>退出调整态：藏起控制点与按钮，提示条回到拖框文案。</summary>
    private void HideSelection()
    {
        _sel = null;
        _grab = GrabKind.None;
        foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
        if (_okButton != null) _okButton.Visibility = Visibility.Collapsed;
        if (_cancelButton != null) _cancelButton.Visibility = Visibility.Collapsed;
        if (_hintLabel != null) _hintLabel.Text = Loc.T(K.Capture.DragToSelectHover);
        ShowSpot(null);
    }

    /// <summary>摆控制点与两个按钮：按钮贴在选区右下角外侧，贴到屏底时挪到内侧。</summary>
    /// <param name="sel">选区（物理像素）。</param>
    private void LayoutAdjustChrome(RectI sel)
    {
        Rect r = ToDip(sel);
        var pts = new (double X, double Y)[]
        {
            (r.X, r.Y), (r.X + r.Width / 2, r.Y), (r.X + r.Width, r.Y),
            (r.X + r.Width, r.Y + r.Height / 2), (r.X + r.Width, r.Y + r.Height),
            (r.X + r.Width / 2, r.Y + r.Height), (r.X, r.Y + r.Height),
            (r.X, r.Y + r.Height / 2),
        };
        for (int i = 0; i < _handles.Length; i++)
        {
            Canvas.SetLeft(_handles[i], pts[i].X - HandleSize / 2);
            Canvas.SetTop(_handles[i], pts[i].Y - HandleSize / 2);
            _handles[i].Visibility = Visibility.Visible;
        }
        const double gap = 8;
        double bx = r.X + r.Width - ButtonSize * 2 - gap;
        double by = r.Y + r.Height + gap;
        if (by + ButtonSize > Height - 4) by = r.Y + r.Height - ButtonSize - gap;
        bx = Math.Max(4, bx);
        by = Math.Max(4, by);
        if (_okButton != null) { Canvas.SetLeft(_okButton, bx); Canvas.SetTop(_okButton, by); _okButton.Visibility = Visibility.Visible; }
        if (_cancelButton != null) { Canvas.SetLeft(_cancelButton, bx + ButtonSize + gap); Canvas.SetTop(_cancelButton, by); _cancelButton.Visibility = Visibility.Visible; }
    }

    /// <summary>调整态下试抓某个点：返回抓住的部位，并把光标换成对应形状。</summary>
    /// <param name="dip">相对本窗的 DIP 坐标。</param>
    /// <returns>抓住的部位；不在选区附近时为 <see cref="GrabKind.None"/>。</returns>
    private GrabKind ProbeAt(Point dip)
    {
        if (_sel == null) return GrabKind.None;
        var p = WindowPosToPhys(dip);
        var kind = AdjustGeometry.Hit(_sel.Value, p.X, p.Y, Math.Max(1, (int)Math.Round(GrabTol * DpiScale)));
        var name = AdjustGeometry.CursorFor(kind);
        if (name.Length != 0) Cursor = (Cursor)new CursorConverter().ConvertFromString(name)!;
        return kind;
    }

    /// <summary>点是否落在某个按钮上（按钮不吃鼠标事件，这里自己判）。</summary>
    /// <param name="dip">DIP 坐标。</param>
    /// <param name="b">按钮。</param>
    /// <returns>落在按钮矩形内返回 true。</returns>
    private static bool OverButton(Point dip, Border? b)
    {
        if (b == null || b.Visibility != Visibility.Visible) return false;
        double x = Canvas.GetLeft(b), y = Canvas.GetTop(b);
        return dip.X >= x && dip.X <= x + b.Width && dip.Y >= y && dip.Y <= y + b.Height;
    }

    /// <summary>拖边角时的吸附：按住 Alt 或设置里关掉就直接返回。</summary>
    /// <param name="sel">按位移算出的选区。</param>
    /// <param name="kind">抓住的部位。</param>
    /// <param name="physX">光标物理横坐标。</param>
    /// <param name="physY">光标物理纵坐标。</param>
    /// <returns>吸附后的选区。</returns>
    private RectI SnapResizing(RectI sel, GrabKind kind, int physX, int physY)
    {
        if (kind is GrabKind.None or GrabKind.Move || !ResizeSnapOn) return sel;
        var hit = ElementSnap.At(physX, physY, _selfHwnd);
        if (hit == null) return sel;
        return AdjustGeometry.Snap(sel, kind, hit.Rect, Math.Max(1, (int)Math.Round(SnapTol * DpiScale)));
    }

    /// <summary>调整态下拖动：按抓住的部位改选区。</summary>
    /// <param name="dip">当前 DIP 坐标。</param>
    private void DragAdjust(Point dip)
    {
        var p = WindowPosToPhys(dip);
        var moved = AdjustGeometry.Resize(_selAtGrab, _grab, p.X - _grabX, p.Y - _grabY, _monitorPhys);
        // 吸附要跨进程问无障碍树，几十毫秒一次；每帧都问，框就跟不上手。
        // 折中：框每帧按位移走，吸附每 80 ms 才问一次——松手前最后一次吸附照样生效。
        var now = DateTime.UtcNow;
        RectI rect = moved;
        if (now - _snapAt >= TimeSpan.FromMilliseconds(80))   // lint-motion:ignore 吸附刷新间隔，不是动效
        {
            rect = SnapResizing(moved, _grab, p.X, p.Y);
            _snapAt = now;
        }
        ShowSelection(rect);
    }
    /// <summary>确认当前选区。</summary>
    /// <returns>确实确认了返回 true。</returns>
    private bool ConfirmSelection()
    {
        if (_sel is not { } sel) return false;
        // 点选窗口之后没动过框 → 仍走干净窗口截图（从窗口自己的表面取，被遮挡也不影响）；
        // 动过框就说明用户要的是自定义范围，退回普通区域截图。
        if (_selHwnd != IntPtr.Zero && sel.X == _selHwndBounds.X && sel.Y == _selHwndBounds.Y
            && sel.W == _selHwndBounds.W && sel.H == _selHwndBounds.H)
        {
            Finish(sel, _selHwnd);
            return true;
        }
        Finish(sel);
        return true;
    }
}
