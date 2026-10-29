using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>按显示帧合并指针更新，停留后异步查询控件；过期结果不改变选区。</summary>
public sealed partial class RegionPickerWindow
{
    private Point? _pointerPending;
    private Point _pointerDip;
    private DispatcherTimer? _elementTimer;
    private long _probeGeneration;
    private bool _clickPending;
    private bool _pointerSubscribed;

    private void StartPointerUpdates()
    {
        if (_pointerSubscribed) return;
        _pointerSubscribed = true;
        CompositionTarget.Rendering += PointerFrame;
        if (!_snapContent) return;
        _elementTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(85) };
        _elementTimer.Tick += ProbeHover;
    }

    private void StopPointerUpdates()
    {
        CompositionTarget.Rendering -= PointerFrame;
        _pointerSubscribed = false;
        _elementTimer?.Stop();
        _pointerPending = null; _probeGeneration++;
    }

    private void PointerFrame(object? sender, EventArgs args)
    {
        if (_pointerPending is not { } point) return;
        _pointerPending = null;
        UpdatePointer(point);
    }

    private void ScheduleProbe(Point point)
    {
        if (_clickPending || _pointerDip == point) return;
        _pointerDip = point; _probeGeneration++;
        _elementTimer?.Stop(); _elementTimer?.Start();
    }

    private async void ProbeHover(object? sender, EventArgs args)
    {
        _elementTimer?.Stop();
        if (_finished || _dragging || _grab != GrabKind.None || _sel != null || _drillLock || _clickPending) return;
        long generation = _probeGeneration;
        var point = WindowPosToPhys(_pointerDip);
        var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        try
        {
            var hit = await ElementProbe.Query(() => ElementSnap.At(point.X, point.Y, self)).WaitAsync(TimeSpan.FromMilliseconds(750));
            if (_finished || generation != _probeGeneration || _dragging || _sel != null || _drillLock || _clickPending) return;
            var cursor = System.Windows.Forms.Cursor.Position;
            if (Math.Abs(cursor.X - point.X) > 3 || Math.Abs(cursor.Y - point.Y) > 3) return;
            if (hit is not { Rect.W: >= 8, Rect.H: >= 8 } || !hit.Rect.Contains(point.X, point.Y)) return;
            _curElem = hit;
            ShowSpot(hit.Rect); UpdateLoupe(_pointerDip, point, hit.Rect);
        }
        catch (TimeoutException) { }
    }

    private void CancelProbe()
    {
        _elementTimer?.Stop(); _probeGeneration++; _clickPending = false; _pointerPending = null;
    }

    private async void SelectClickedElement((int X, int Y) point)
    {
        var hit = _curElem;
        CancelProbe();
        if (hit != null && hit.Rect.Contains(point.X, point.Y)) { CompleteClick(point, hit); return; }
        _clickPending = true;
        long generation = _probeGeneration;
        var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        try
        {
            hit = await ElementProbe.Query(() => ElementSnap.At(point.X, point.Y, self)).WaitAsync(TimeSpan.FromMilliseconds(150));
        }
        catch (TimeoutException) { hit = null; }
        if (_finished || generation != _probeGeneration) return;
        _clickPending = false;
        CompleteClick(point, hit);
    }
}
