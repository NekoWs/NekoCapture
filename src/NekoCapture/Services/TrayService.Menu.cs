using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using NekoCapture.Core;
using NekoCapture.Native;

namespace NekoCapture.Services;

/// <summary>托盘菜单预热并复用布局与锚点句柄，换色或换语言时重建。</summary>
public sealed partial class TrayService
{
    private bool _disposed, _menuEnglish;
    private DispatcherOperation? _menuWarmup;
    private long _menuRequestedAt;
    private string _menuTriggerSignature = "";
    private bool _menuTriggersDirty = true;

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
        if (_disposed) return;
        bool ready = _menu != null && _menuAnchor != null && _menuResources != null && _menuEnglish == Loc.IsEnglish;
        if (ready && !_menuTriggersDirty) return;
        string signature = System.Text.Json.JsonSerializer.Serialize(new
        {
            _host.Settings.Current.Gestures.Enabled,
            Items = _host.Settings.Current.Gestures.Bindings.Where(b => b.Source == TriggerSources.TrayMenu)
                .Select(b => new { b.Id, b.Name, b.Enabled, b.EntryId, b.MenuIcon,
                    Flow = _host.Settings.Current.Gestures.Graph.Flow(b.EntryId)?.Name,
                    Runnable = _host.Settings.Current.Gestures.Graph.Flow(b.EntryId) is { Steps.Count: > 0 }
                        && _host.Settings.Current.Gestures.Graph.IsValid(b.EntryId),
                    Actions = _host.Settings.Current.Gestures.FlowActions(b.EntryId).ToArray() }).ToArray(),
        });
        _menuTriggersDirty = false;
        if (ready && _menuTriggerSignature == signature) return;
        _menuTriggerSignature = signature; BuildMenu();
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

        void Item(string label, string glyph, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled, Icon = Fluent.Glyph(glyph, 15, Theme.TextDimBrush) };
            item.Click += (_, _) => { menu.IsOpen = false; ResidentBridge.RunTrayAction(action); };
            menu.Items.Add(item);
        }
        var config = _host.Settings.Current.Gestures;
        foreach (var binding in config.Bindings.Where(b => b.Source == TriggerSources.TrayMenu))
        {
            var flow = config.Graph.Flow(binding.EntryId);
            string label = binding.Name.Length > 0 ? binding.Name : flow == null ? Loc.T(K.Triggers.TrayMenu) : config.FlowName(flow);
            Item(label, TrayMenuIcons.Glyph(config, binding), () => TriggerExecution.Run(_host, binding.Id),
                config.Enabled && binding.Enabled && flow is { Steps.Count: > 0 } && config.Graph.IsValid(binding.EntryId));
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        Item(Loc.T(K.Common.Settings), "\uE713", OpenSettings);
        Item(Loc.T(K.Tray.Exit), "\uE8BB", () => { ResidentBridge.RetireHost(); Dispose(); Application.Current?.Shutdown(); });

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
