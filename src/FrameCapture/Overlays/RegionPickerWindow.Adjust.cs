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
    static System.Windows.Media.Animation.DoubleAnimation MaskAnim(double to, int ms, int delay)
        => new(to, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };

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

    /// <summary>这次选区落下去要做什么：true = 起录，false = 截图。初值由调用方的意图给，用户在开关上可以改。</summary>
    public bool RecordMode { get; private set; }

    /// <summary>模式开关的两段（截图 / 录制）；点它改的是上面那个属性，不落盘。</summary>
    private Border? _modeSwitch;
    private Border? _modeCapture, _modeRecord;

    /// <summary>右键关掉整层：Esc 之外的第二只手，设置里可关。</summary>
    private readonly bool _rightClickCancel;

    /// <summary>点选窗口时记下它的句柄与边界：选区变化后使用区域截图。</summary>
    private IntPtr _selHwnd;
    private RectI _selHwndBounds;

    /// <summary>本窗句柄，交给吸附做排除。</summary>
    private IntPtr _selfHwnd;

    /// <summary>控制点边长（DIP）。</summary>
    private const double HandleSize = 11;

    /// <summary>边角判定半径（DIP）：太小抓不住，太大会把整块移动吃掉。</summary>
    private const double GrabTol = 7;

    /// <summary>两个按钮当前是否处于悬停态：只在状态变化时改画刷，避免每帧重建。</summary>
    private bool _hoverOk, _hoverCancel;

    /// <summary>刷两个按钮的悬停底色（只有状态变化才动）。</summary>
    /// <param name="ok">确认按钮是否悬停。</param>
    /// <param name="cancel">取消按钮是否悬停。</param>
    private void SetButtonHover(bool ok, bool cancel)
    {
        if (ok != _hoverOk)
        {
            _hoverOk = ok;
            if (_okButton != null)
                _okButton.Background = new SolidColorBrush(ok ? HoverOf(Theme.Accent, true) : Theme.Accent);
        }
        if (cancel != _hoverCancel)
        {
            _hoverCancel = cancel;
            if (_cancelButton != null)
                _cancelButton.Background = new SolidColorBrush(cancel ? HoverOf(CancelFill, true) : CancelFill);
        }
    }


    /// <summary>按钮直径（DIP）。</summary>
    private const double ButtonWidth = 66;
    private const double ButtonHeight = 30;

    /// <summary>可调整选区开关：关着时松手立刻出图，与没有这个功能时一致。</summary>
    private static bool Editable => SettingsService.Shared?.Current.EditableSelection ?? false;

    /// <summary>本窗的 DPI 缩放。</summary>
    private double DpiScale
    {
        get { try { return VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { return 1; } }
    }

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
        _okButton = AdjustButton(Loc.T(K.Common.Confirm), Theme.Accent, true, Loc.T(K.Capture.ConfirmSelection));
        _cancelButton = AdjustButton(Loc.T(K.Common.Cancel), null, false, Loc.T(K.Capture.CancelSelection));
        _canvas.Children.Add(_okButton);
        _canvas.Children.Add(_cancelButton);
    }

    /// <summary>做一个圆形按钮：勾用强调色底，叉用深色底。</summary>
    /// <param name="glyph">Segoe Fluent Icons 字形。</param>
    /// <param name="accent">强调色；null 表示用中性底。</param>
    /// <param name="tip">悬停提示。</param>
    /// <returns>按钮边框。</returns>
    private Border AdjustButton(string text, Color? accent, bool primary, string tip)
    {
        var b = new Border
        {
            Width = ButtonWidth,
            Height = ButtonHeight,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(accent ?? CancelFill),
            BorderBrush = new SolidColorBrush(accent is { } c
                ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            ToolTip = tip,
            // 投影是这几枚按钮唯一的"分层"手段：不画它就直接贴在截图内容上，边界糊在一起
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.55,
                Color = Colors.Black,
            },
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(accent.HasValue ? Fluent.OnAccentColor(accent.Value) : Colors.White),
            },
        };
        return b;
    }

    /// <summary>取消键的底色：比纯黑亮一点，压在深色截图上不至于糊成一团。</summary>
    static readonly Color CancelFill = Color.FromArgb(0xF2, 0x22, 0x22, 0x24);

    /// <summary>悬停时的底色。</summary>
    static Color HoverOf(Color c, bool lighten) => lighten
        ? Color.FromArgb(c.A, (byte)Math.Min(255, c.R + 26), (byte)Math.Min(255, c.G + 26), (byte)Math.Min(255, c.B + 26))
        : c;

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
        const double gap = 12;         // 按钮与选区之间的边距
        const double between = 8;       // 两个按钮之间的间距
        double bx = r.X + r.Width - (ButtonWidth * 2 + between);   // 两个按钮并排，右缘对齐选区右缘
        double by = r.Y + r.Height + gap;
        if (by + ButtonHeight > Height - 4) by = r.Y + r.Height - ButtonHeight - gap;
        bx = Math.Max(4, bx);
        by = Math.Max(4, by);
        if (_okButton != null) { Canvas.SetLeft(_okButton, bx); Canvas.SetTop(_okButton, by); _okButton.Visibility = Visibility.Visible; }
        if (_cancelButton != null) { Canvas.SetLeft(_cancelButton, bx + ButtonWidth + between); Canvas.SetTop(_cancelButton, by); _cancelButton.Visibility = Visibility.Visible; }
    }

    /// <summary>调整态下试抓某个点：返回抓住的部位，并把光标换成对应形状。</summary>
    /// <param name="dip">相对本窗的 DIP 坐标。</param>
    /// <returns>抓住的部位；不在选区附近时为 <see cref="GrabKind.None"/>。</returns>
    private GrabKind ProbeAt(Point dip)
    {
        if (_sel == null) return GrabKind.None;
        var p = WindowPosToPhys(dip);
        var kind = AdjustGeometry.Hit(_sel.Value, p.X, p.Y, Math.Max(1, (int)Math.Round(GrabTol * DpiScale)));
        // 悬在按钮上：手型 + 变色；其余按区域给形状。每条分支都给光标，不能只在抓到边角时设
        if (OverButton(dip, _okButton)) { SetButtonHover(true, false); Cursor = Cursors.Hand; return GrabKind.None; }
        if (OverButton(dip, _cancelButton)) { SetButtonHover(false, true); Cursor = Cursors.Hand; return GrabKind.None; }
        if (OverButton(dip, _modeCapture) || OverButton(dip, _modeRecord)) { SetButtonHover(false, false); Cursor = Cursors.Hand; return GrabKind.None; }
        SetButtonHover(false, false);
        var name = AdjustGeometry.CursorFor(kind);
        Cursor = name.Length == 0
            ? Cursors.Cross
            : (Cursor)new CursorConverter().ConvertFromString(name)!;
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

    /// <summary>模式开关整块的宽度：两段各 56，够放「截图」「录制」两个词。</summary>
    private const double ModeSwitchWidth = 112;

    /// <summary>模式开关里的一段：不自带命中，命中由 OnDown 按 Canvas 位置判。</summary>
    /// <param name="text">段名。</param>
    /// <param name="record">true = 这一段是「录制」。</param>
    /// <returns>段落边框，交给调用方摆位置。</returns>
    private Border ModeSegment(string text, bool record) => new()
    {
        Width = ModeSwitchWidth / 2,
        Height = ButtonHeight - 2,
        CornerRadius = new CornerRadius(6),
        Background = Brushes.Transparent,
        Visibility = Visibility.Collapsed,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
        ToolTip = record ? Loc.T(K.Settings.StartRecordingAfterSelecting) : Loc.T(K.Capture.ConfirmSelection),
    };

    /// <summary>建一枚常显的模式开关，贴在屏幕上方居中：不依赖可调整选区，还没拖框就能决定这一次要截图还是起录。</summary>
    /// <param name="screenWidth">本窗的 DIP 宽度，用来居中。</param>
    private void BuildModeSwitch(double screenWidth)
    {
        _modeSwitch = new Border
        {
            Width = ModeSwitchWidth,
            Height = ButtonHeight,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(CancelFill),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };
        _modeCapture = ModeSegment(Loc.T(K.Common.Capture), false);
        _modeRecord = ModeSegment(Loc.T(K.Common.Recording), true);
        double x = Math.Max(8, (screenWidth - ModeSwitchWidth) / 2);
        const double y = 56;   // 上方居中：离屏幕上缘一条标题栏的距离，显眼又不挡窗口自己的标题
        Canvas.SetLeft(_modeSwitch, x);
        Canvas.SetTop(_modeSwitch, y);
        // 压在遮罩之上：遮罩是画布里第一个孩子，靠插入顺序排已经不够稳，显式给一层
        Panel.SetZIndex(_modeSwitch, 10);
        Panel.SetZIndex(_modeCapture, 11);
        Panel.SetZIndex(_modeRecord, 12);
        _canvas.Children.Add(_modeSwitch);
        _canvas.Children.Add(_modeCapture);
        _canvas.Children.Add(_modeRecord);
        LayoutModeSegments(x, y);
        PaintModeSwitch();
    }

    /// <summary>把两段摆进开关里：左截图、右录制。</summary>
    /// <param name="x">开关左缘（DIP）。</param>
    /// <param name="y">开关上缘（DIP）。</param>
    private void LayoutModeSegments(double x, double y)
    {
        if (_modeCapture == null || _modeRecord == null) return;
        Canvas.SetLeft(_modeCapture, x + 1);
        Canvas.SetTop(_modeCapture, y + 1);
        Canvas.SetLeft(_modeRecord, x + ModeSwitchWidth / 2);
        Canvas.SetTop(_modeRecord, y + 1);
        _modeCapture.Visibility = Visibility.Visible;
        _modeRecord.Visibility = Visibility.Visible;
    }

    /// <summary>按当前模式刷两段的底色与字色，并把勾按钮的提示改成这一次真正会发生的事。</summary>
    private void PaintModeSwitch()
    {
        if (_modeCapture != null)
        {
            _modeCapture.Background = RecordMode ? Brushes.Transparent : Theme.AccentBrush;
            ((TextBlock)_modeCapture.Child!).Foreground = RecordMode
                ? new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent));
        }
        if (_modeRecord != null)
        {
            _modeRecord.Background = RecordMode ? Theme.AccentBrush : Brushes.Transparent;
            ((TextBlock)_modeRecord.Child!).Foreground = RecordMode
                ? new SolidColorBrush(Fluent.OnAccentColor(Theme.Accent))
                : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        }
        if (_okButton != null)
            _okButton.ToolTip = Loc.T(RecordMode ? K.Actions.RecordStartLabel : K.Capture.ConfirmSelection);
    }

    /// <summary>点模式开关的某一段，或由调用方在开窗之后摆正初值：改的是这一次选区的去向，不落盘。</summary>
    /// <param name="record">true = 切到录制。</param>
    public void SetRecordMode(bool record)
    {
        if (RecordMode == record) return;
        RecordMode = record;
        PaintModeSwitch();
    }

    /// <summary>这一击是不是落在模式开关上；落上就切段并吃掉。</summary>
    /// <param name="dip">相对本窗的 DIP 坐标。</param>
    /// <returns>吃掉了这一击返回 true。</returns>
    private bool HitModeSwitch(Point dip)
    {
        if (OverButton(dip, _modeCapture)) { SetRecordMode(false); return true; }
        if (OverButton(dip, _modeRecord)) { SetRecordMode(true); return true; }
        return false;
    }

    /// <summary>拖边角时的吸附：按住 Alt 或设置里关掉就直接返回。</summary>
    /// <param name="sel">按位移算出的选区。</param>
    /// <param name="kind">抓住的部位。</param>
    /// <param name="physX">光标物理横坐标。</param>
    /// <param name="physY">光标物理纵坐标。</param>
    /// <summary>调整态下拖动：按抓住的部位改选区。</summary>
    /// <param name="dip">当前 DIP 坐标。</param>
    private void DragAdjust(Point dip)
    {
        var p = WindowPosToPhys(dip);
        var moved = AdjustGeometry.Resize(_selAtGrab, _grab, p.X - _grabX, p.Y - _grabY, _monitorPhys);
        ShowSelection(moved);
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
