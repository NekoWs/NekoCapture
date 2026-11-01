using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>按显示帧合并指针更新，停留后异步查询控件；过期结果不改变选区。</summary>
public sealed partial class RegionPickerWindow
{
    private readonly int _snapDwellMs;
    private bool _immediateProbePending;
    private Point? _pointerPending;
    private Point _pointerDip;
    private DispatcherTimer? _elementTimer;
    private long _probeGeneration;
    private bool _clickPending;
    private bool _pointerSubscribed;
    private long _probeDue;
    private long _cachedGeneration = -1;

    private void StartPointerUpdates()
    {
        if (_pointerSubscribed) return;
        _pointerSubscribed = true;
        CompositionTarget.Rendering += PointerFrame;
        if (!_snapContent) return;
        _elementTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(Math.Max(1, _snapDwellMs)) };
        _elementTimer.Tick += ProbeHover;
        _pointerDip = new Point(double.NaN, double.NaN);
        _pointerPending = Mouse.GetPosition(this);
        ScheduleProbe(_pointerPending.Value);
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
        if (_pointerPending is { } point)
        {
            _pointerPending = null;
            UpdatePointer(point);
        }
        TryCachedSnap();
        if (_immediateProbePending)
        {
            _immediateProbePending = false;
            ProbeHover(null, EventArgs.Empty);
        }
    }

    private void ScheduleProbe(Point point)
    {
        if (_clickPending || _pointerDip == point) return;
        _pointerDip = point; _probeGeneration++;
        _probeDue = Environment.TickCount64 + _snapDwellMs;
        _elementTimer?.Stop();
        if (_snapDwellMs == 0) _immediateProbePending = true;
        else _elementTimer?.Start();
    }

    private async void ProbeHover(object? sender, EventArgs args)
    {
        _elementTimer?.Stop();
        if (_finished || _dragging || _grab != GrabKind.None || _sel != null || _drillLock || _clickPending) return;
        if (OverButton(_pointerDip, _modeCapture) || OverButton(_pointerDip, _modeRecord)) return;
        if (TryCachedSnap()) return;
        long generation = _probeGeneration;
        var point = WindowPosToPhys(_pointerDip);
        if (PreviousRegionAt(point) != null) return;
        var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        try
        {
            var hit = await ElementProbe.Query(() => ElementSnap.At(point.X, point.Y, self)).WaitAsync(TimeSpan.FromMilliseconds(750));
            if (_finished || generation != _probeGeneration || _cachedGeneration == generation || _dragging || _sel != null || _drillLock || _clickPending) return;
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
        _elementTimer?.Stop(); _probeGeneration++; _probeDue = 0; _clickPending = false; _pointerPending = null; _immediateProbePending = false;
    }

    private bool TryCachedSnap()
    {
        if (PreviousRegionAt(WindowPosToPhys(_pointerDip)) != null) return false;
        if (!_snapContent || _probeDue == 0 || Environment.TickCount64 < _probeDue
            || _finished || _dragging || _grab != GrabKind.None || _sel != null || _drillLock || _clickPending) return false;
        if (OverButton(_pointerDip, _modeCapture) || OverButton(_pointerDip, _modeRecord)) return false;
        if (_cachedGeneration == _probeGeneration) return true;
        var point = WindowPosToPhys(_pointerDip);
        var cursor = System.Windows.Forms.Cursor.Position;
        if (Math.Abs(cursor.X - point.X) > 3 || Math.Abs(cursor.Y - point.Y) > 3) return false;
        var hit = ElementSnap.CachedAt(_probeWindow, point.X, point.Y);
        if (hit == null) return false;
        _curElem = hit; _cachedGeneration = _probeGeneration;
        ShowSpot(hit.Rect); UpdateLoupe(_pointerDip, point, hit.Rect);
        return true;
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
