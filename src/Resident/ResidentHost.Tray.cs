using NekoCapture.Runtime;

namespace NekoCapture.Resident;

/// <summary>托盘点击判定只投递触发标识，行为在界面服务中执行。</summary>
internal sealed partial class ResidentHost
{
    private readonly TrayClickGate _trayClicks = new();

    private void ConfigureTrayClicks(ResidentManifest manifest)
    {
        _trayClicks.Configure(manifest.TraySingle, manifest.TrayDouble, manifest.TrayEquivalent);
        if (!_trayClicks.Pending) KillTimer(_window, 2);
    }

    private void TrayEvent(uint message)
    {
        string trigger = "";
        if (message == 0x0202)
        {
            trigger = _trayClicks.Release();
            if (_trayClicks.Pending) SetTimer(_window, 2, GetDoubleClickTime(), IntPtr.Zero);
        }
        else if (message == 0x0203) { trigger = _trayClicks.DoubleClick(); KillTimer(_window, 2); }
        else if (message is 0x0205 or 0x007B)
        {
            KillTimer(_window, 2); _trayClicks.Flush();
            QueueWork(["--tray-menu", Environment.TickCount64.ToString()]);
        }
        if (trigger.Length > 0) QueueWork(["--run-trigger", trigger]);
    }

    private void FlushTrayClick()
    {
        KillTimer(_window, 2);
        string trigger = _trayClicks.Flush();
        if (trigger.Length > 0) QueueWork(["--run-trigger", trigger]);
    }
}
