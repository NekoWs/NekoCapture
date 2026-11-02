using NekoCapture.Runtime;
using System.Windows.Threading;

namespace NekoCapture.Services;

/// <summary>单进程托盘与原生常驻托盘共用点击判定。</summary>
public sealed partial class TrayService
{
    private readonly TrayClickGate _trayClicks = new();
    private readonly DispatcherTimer _trayClickTimer = new();

    private void InitializeTrayClicks()
    {
        _trayClickTimer.Tick += (_, _) => { _trayClickTimer.Stop(); FireTrayTrigger(_trayClicks.Flush()); };
        RefreshTrayClicks();
    }

    private void RefreshTrayClicks()
    {
        var clicks = TriggerExecution.TrayClicks(_host.Settings.Current.Gestures);
        _trayClicks.Configure(clicks.Single, clicks.Double, clicks.Equivalent);
        _trayClickTimer.Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime);
        if (!_trayClicks.Pending) _trayClickTimer.Stop();
    }

    private void TrayRelease()
    {
        FireTrayTrigger(_trayClicks.Release());
        if (_trayClicks.Pending) { _trayClickTimer.Stop(); _trayClickTimer.Start(); }
    }

    private void TrayDoubleClick()
    { _trayClickTimer.Stop(); FireTrayTrigger(_trayClicks.DoubleClick()); }

    private void FireTrayTrigger(string id)
    { if (id.Length > 0 && !_disposed) ResidentBridge.RunTrayAction(() => TriggerExecution.Run(_host, id)); }
}
