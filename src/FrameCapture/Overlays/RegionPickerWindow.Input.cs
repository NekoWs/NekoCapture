using System.Windows;
using System.Windows.Input;
using FrameCapture.Capture;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>选区的鼠标拖框、吸附与调整输入。</summary>
public sealed partial class RegionPickerWindow
{
    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        CancelProbe();
        var dipDown = e.GetPosition(this);
        if (HitModeSwitch(dipDown)) return;   // 先判模式开关：不依赖选区在不在
        // 调整态：先看勾/叉按钮，再看是抓边角还是整块移动；点到框外就重开一个选区
        if (_sel != null)
        {
            if (OverButton(dipDown, _okButton)) { ConfirmSelection(); return; }
            if (OverButton(dipDown, _cancelButton)) { Finish(null); return; }
            var kindDown = ProbeAt(dipDown);
            if (kindDown != GrabKind.None)
            {
                var gp = WindowPosToPhys(dipDown);
                
                _grab = kindDown; _grabX = gp.X; _grabY = gp.Y; _selAtGrab = _sel.Value;
                CaptureMouse();
                return;
            }
            HideSelection();
        }
        var p = WindowPosToPhys(e.GetPosition(this));
        _dragX1 = p.X; _dragY1 = p.Y;
        _dragging = true;
        _drillLock = false;   // 起手拖拽即解除 Tab 钻取锁定
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        _pointerPending = e.GetPosition(this);
        if (_snapContent && !_dragging && _grab == GrabKind.None && _sel == null && !_drillLock)
            ScheduleProbe(_pointerPending.Value);
    }

    private void UpdatePointer(Point dipMove)
    {
        // 模式开关常显在屏幕上方：指针停在它上面给手型，但拖框与抓边角期间不打断
        if (_grab == GrabKind.None && !_dragging
            && (OverButton(dipMove, _modeCapture) || OverButton(dipMove, _modeRecord)))
        { Cursor = Cursors.Hand; return; }
        if (_grab != GrabKind.None) { DragAdjust(dipMove); return; }
        if (_sel != null) { ProbeAt(dipMove); return; }
        var dip = dipMove;
        var p = WindowPosToPhys(dip);
        if (_dragging)
        {
            var rect = RectI.FromCorners(_dragX1, _dragY1, p.X, p.Y);
            ShowSpot(rect);
            UpdateLoupe(dip, p, rect);
        }
        else
        {
            // Tab 上钻锁定：光标未明显移动就保持锁定的父容器，否则鼠标一抖就被 hover 吸附回子控件。
            if (_drillLock)
            {
                if ((dip - _drillAnchor).Length <= 6)
                {
                    if (_curElem != null) { ShowSpot(_curElem.Rect); UpdateLoupe(dip, p, _curElem.Rect); }
                    return;
                }
                _drillLock = false;   // 明显移动→解锁，恢复常规吸附
            }
            if (_snapContent)
            {
                ScheduleProbe(dip);
                var eh = _curElem;
                if (eh != null && eh.Rect.Contains(p.X, p.Y))
                {
                    _curElem = eh;
                    ShowSpot(eh.Rect);
                    UpdateLoupe(dip, p, eh.Rect);
                    return;
                }
            }
            _curElem = null;
            var hit = SnapWindowAt(p);
            if (hit != null) { ShowSpot(hit.Value); UpdateLoupe(dip, p, hit); }
            else if (_existing == null) { ShowSpot(null); UpdateLoupe(dip, p, null); }
            else UpdateLoupe(dip, p, _existing);
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_grab != GrabKind.None) { _grab = GrabKind.None;  ReleaseMouseCapture(); return; }
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var p = WindowPosToPhys(e.GetPosition(this));
        var rect = RectI.FromCorners(_dragX1, _dragY1, p.X, p.Y);
        if (rect.W < 8 || rect.H < 8)
        {
            if (_snapContent) SelectClickedElement(p);
            else CompleteClick(p, null);
            return;
        }
        CancelProbe();
        _selHwnd = IntPtr.Zero;
        if (!Editable) { Finish(rect); return; }
        ShowSelection(rect);
    }

    private void CompleteClick((int X, int Y) p, ElementHit? element)
    {
        var window = _windowCapture ? WindowHitForCapture(p) : null;
        bool wholeWindow = element != null && window is { } target && IsWholeWindowElement(element.Rect, target.Hwnd, target.Bounds);
        if (!wholeWindow && element is { Rect.W: >= 8, Rect.H: >= 8 } && element.Rect.Contains(p.X, p.Y))
        {
            _selHwnd = IntPtr.Zero;
            if (!Editable) { Finish(element.Rect); return; }
            ShowSelection(element.Rect);
            return;
        }
        if (window is { } selected)
        {
            if (!Editable) { Finish(selected.Bounds, selected.Hwnd); return; }
            ShowSelection(selected.Bounds);
            _selHwnd = selected.Hwnd;
            _selHwndBounds = selected.Bounds;
            return;
        }
        var hit = SnapWindowAt(p, fresh: true);
        _selHwnd = IntPtr.Zero;
        var baseRect = hit ?? _existing ?? _monitorPhys;
        if (!Editable) { Finish(baseRect); return; }
        ShowSelection(baseRect);
    }

    private static bool IsWholeWindowElement(RectI element, IntPtr hwnd, RectI frame)
    {
        if (element.Equals(frame)) return true;
        return Native.Win32.GetWindowRect(hwnd, out var bounds)
            && element.Equals(new RectI(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
    }

}
