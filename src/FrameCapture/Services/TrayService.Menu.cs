using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using FrameCapture.Core;
using FrameCapture.Native;

namespace FrameCapture.Services;

/// <summary>托盘菜单预热并复用布局与锚点句柄，换色或换语言时重建。</summary>
public sealed partial class TrayService
{
    private bool _disposed, _menuEnglish;
    private DispatcherOperation? _menuWarmup;
    private long _menuRequestedAt;

    private void QueueMenuWarmup()
    {
        if (_disposed || _menuWarmup?.Status == DispatcherOperationStatus.Pending) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        _menuWarmup = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _menuWarmup = null;
            PrepareMenu();
        }));
    }

    /// <summary>在不显示窗口的情况下准备菜单、文本布局与锚点对象。</summary>
    internal void PrepareMenu()
    {
        if (_disposed || (_menu != null && _menuAnchor != null && _menuResources != null && _menuEnglish == Loc.IsEnglish)) return;
        BuildMenu();
    }

    private void BuildMenu()
    {
        ReleaseMenu();
        var menu = Fluent.Ui(new ContextMenu { Resources = _menuResources ??= Fluent.MenuResources(), StaysOpen = false });
        menu.Resources[SystemParameters.MenuPopupAnimationKey] = PopupAnimation.None;
        var anchor = new Window
        {
            Width = 1, Height = 1, WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent, ShowInTaskbar = false, ShowActivated = true,
            Topmost = true,
        };
        anchor.Resources[SystemParameters.MenuPopupAnimationKey] = PopupAnimation.None;
        _menu = menu; _menuAnchor = anchor; _menuEnglish = Loc.IsEnglish;

        void Item(Str label, string glyph, Action action)
        {
            var item = new MenuItem { Header = Loc.T(label), Icon = Fluent.Glyph(glyph, 15, Theme.TextDimBrush) };
            item.Click += (_, _) => { menu.IsOpen = false; ResidentBridge.RunTrayAction(action); };
            menu.Items.Add(item);
        }
        Item(K.Tray.CaptureNow, "\uE7A8", () => _host.Controller.BeginImmediateCapture());
        Item(K.Common.ScrollingCapture, "\uE8CB", () => _host.Controller.BeginScrollCapture());
        Item(K.Common.DelayedCapture, "\uE823", () => _host.Controller.BeginDelayedCapture());
        Item(K.Tray.PinFromClipboard, "\uE718", async () =>
        {
            if (!await _host.Controller.PinFromClipboardAsync()) Notify(Loc.T(K.Tray.NoImageOnTheClipboard));
        });
        menu.Items.Add(new Separator());
        Item(K.Common.Settings, "\uE713", OpenSettings);
        Item(K.Tray.Exit, "\uE8BB", () => { ResidentBridge.RetireHost(); Dispose(); Application.Current?.Shutdown(); });

        menu.PlacementTarget = anchor; menu.Placement = PlacementMode.MousePoint;
        WatchMenu(menu, anchor);
        anchor.Deactivated += (_, _) => menu.IsOpen = false;
        menu.Closed += (_, _) =>
        {
            if (!menu.IsOpen && ReferenceEquals(_menuAnchor, anchor)) anchor.Hide();
        };
        menu.Opened += (_, _) => Trace.Log($"tray menu opened latency={Math.Max(0, Environment.TickCount64 - _menuRequestedAt)}ms");
        menu.ApplyTemplate();
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
    }

    /// <summary>立即在鼠标处打开已准备的托盘菜单。</summary>
    public void ShowMenu() => ShowMenu(Environment.TickCount64);

    /// <summary>打开菜单并记录从原始右键请求开始的耗时。</summary>
    /// <param name="requestedAt">原始请求的系统单调毫秒时间。</param>
    internal void ShowMenu(long requestedAt)
    {
        if (_disposed) return;
        _menuRequestedAt = requestedAt;
        PrepareMenu();
        var menu = _menu!; var anchor = _menuAnchor!;
        if (menu.IsOpen) { menu.Focus(); return; }
        var cursor = System.Windows.Forms.Cursor.Position;
        Win32.SetWindowPos(new WindowInteropHelper(anchor).EnsureHandle(), IntPtr.Zero, cursor.X, cursor.Y, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        anchor.Show(); anchor.Activate();
        menu.IsOpen = true;
    }

    private void ReleaseMenu()
    {
        _menuWarmup?.Abort(); _menuWarmup = null;
        var menu = _menu; var anchor = _menuAnchor;
        _menu = null; _menuAnchor = null;
        if (menu != null) menu.IsOpen = false;
        anchor?.Close();
    }
}
