using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>编辑器悬浮工具条：独立置顶小窗，跟随主窗口，圆角+阴影，不抢键盘焦点，可拖空白处移动。</summary>
public sealed class FloatBar : Window
{
    readonly Border _bd;
    readonly System.Windows.Threading.DispatcherTimer _topTimer;

    /// <summary>用户拖动过之后不再被编辑器重排。</summary>
    public bool ManualPos { get; private set; }

    public FloatBar(Window owner, FrameworkElement content)
    {
        // Owner 故意不设：在 owner 构造期间设置 Owner 会导致 UI 线程死锁；关闭由编辑器 Closed 显式处理
        Title = "FrameCapture Bar";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;

        // 与游戏等置顶窗同带竞争：失活/每 500ms 重插到置顶带最前（不动位置、不抢键盘焦点）
        Deactivated += (_, _) => { if (IsLoaded) ReassertTop(); };
        _topTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _topTimer.Tick += (_, _) => { if (IsVisible) ReassertTop(); else _topTimer!.Stop(); };
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) _topTimer.Start(); };
        Closed += (_, _) => { _topTimer.Stop(); Theme.Changed -= OnTheme; };

        _bd = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Theme.PanelBg),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5),
            Child = content,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45 },
        };
        Content = _bd;

        // 按住空白处即可拖动（按钮自己吞掉点击，不会误触）
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (e.OriginalSource is Control or ScrollViewer) return;
            ManualPos = true;
            _topTimer.Stop();
            try { DragMove(); } catch { }
            _topTimer.Start();
            e.Handled = true;
        };

        Theme.Changed += OnTheme;
    }

    void OnTheme()
    {
        var bg = _bd.Background as SolidColorBrush;
        if (bg != null && bg.CanFreeze)
        {
            var anim = new System.Windows.Media.Animation.ColorAnimation(Theme.PanelBg, TimeSpan.FromMilliseconds(260));
            var holder = new SolidColorBrush(bg.Color);
            _bd.Background = holder;
            holder.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
        else _bd.Background = new SolidColorBrush(Theme.PanelBg);
        _bd.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B));
    }

    void ReassertTop()
    {
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        Native.Win32.SetWindowPos(h, Native.Win32.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public void ShowBar()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (!IsVisible) Show();
    }

    public void HideBar()
    {
        if (!IsVisible) return;
        var a = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        a.Completed += (_, _) => Dispatcher.Invoke(Hide);
        BeginAnimation(OpacityProperty, a);
    }
}
