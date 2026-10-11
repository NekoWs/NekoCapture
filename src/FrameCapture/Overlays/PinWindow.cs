using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Capture;
using FrameCapture.Encoding;
using FrameCapture.Native;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>
/// R65：钉到屏幕——把当前帧（含标注/裁剪，WYSIWYG）作为一张无边框、置顶、可拖动/滚轮缩放的小图，
/// 钉在桌面原捕获位置。Snipaste/QQ 的招牌手感，此前缺位。
/// 交互：按住拖动移动；滚轮以中心为锚缩放（10%~600%）；双击或 Esc 关闭；右键菜单复制/关闭。
/// 不进任务栏（ShowInTaskbar=false），关掉只靠上述手势——与 Snipaste 贴图一致。
/// </summary>
public sealed class PinWindow : Window
{
    readonly CapturedFrame _frame;
    readonly Image _img;
    double _scale = 1.0, _baseW, _baseH;

    public PinWindow(CapturedFrame frame, RectI physPos)
    {
        _frame = frame;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Title = "FrameCapture Pin";
        UseLayoutRounding = true;
        Focusable = true;

        var src = BgraImage.ToSource(frame);
        src.Freeze();
        _img = new Image { Source = src, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);

        // 1px 深色描边：任何桌面背景上都看得见边界；圆角与截图工具贴图观感一致
        Content = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Child = _img,
            ClipToBounds = true,
        };

        Loaded += (_, _) =>
        {
            // 物理像素 → DIP：1:1 显示（与 RegionOutlineWindow 同款换算），钉在原捕获位置
            var d = VisualTreeHelper.GetDpi(this);
            _baseW = frame.W / d.DpiScaleX;
            _baseH = frame.H / d.DpiScaleY;
            Left = physPos.X / d.DpiScaleX;
            Top = physPos.Y / d.DpiScaleY;
            ApplyScale();
            Activate();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { Close(); return; } // 双击关闭（先于拖动判定，避免 DragMove 吞掉第二击）
            if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
        };
        MouseWheel += (_, e) =>
        {
            double old = _scale;
            _scale = FrameCapture.Core.PinZoom.Next(_scale, e.Delta);
            if (System.Math.Abs(_scale - old) < 1e-9) return;
            double cx = Left + Width / 2, cy = Top + Height / 2; // 以中心为锚，缩放不漂移
            ApplyScale();
            Left = cx - Width / 2; Top = cy - Height / 2;
        };
        MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        FrameCapture.Core.Fluent.FadeIn(this, 120, 10);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetWindowTextW(hwnd, "FrameCapture Pin"); // 自动化/焦点安全需要稳定标题
        };
    }

    void ApplyScale()
    {
        _img.Width = _baseW * _scale;
        _img.Height = _baseH * _scale;
        Width = _img.Width + 2;
        Height = _img.Height + 2;
    }

    void ShowMenu()
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "复制" };
        copy.Click += (_, _) => ClipboardService.SetFrame(_frame);
        var close = new MenuItem { Header = "关闭 (Esc)" };
        close.Click += (_, _) => Close();
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }
}
