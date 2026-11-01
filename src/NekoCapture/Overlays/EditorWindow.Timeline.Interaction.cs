using System.Windows;
using System.Windows.Input;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>录制时间轴的定位、范围拖动与起止点操作。</summary>
public sealed partial class EditorWindow
{
    int _tlMode; // 0 空闲 1 拖动跳转/待拉选段 2 拖范围左端 3 拖范围右端 4 拉选段中
    double _tlPressX; int _tlPressIdx;   // 时间轴按下点（拖过阈值从此点拉选段）

    void Range_Down(object sender, MouseButtonEventArgs e)
    {
        if (_encoding) return;
        Focus();
        if (_playing) TogglePlay();
        _rd.Count = _seq.Frames.Count;
        _rd.Width = _rangeCanvas.ActualWidth;
        double x = e.GetPosition(_rangeCanvas).X;
        _tlPressX = x;
        _tlPressIdx = Core.RangeDragState.XToIdx(x, _rangeCanvas.ActualWidth, _seq.Frames.Count);
        _tlMode = 1;   // 暂定 scrub；Move 中若拖过阈值转为「拉选段」
        if (_rd.A >= 0 && _seq.Frames.Count > 1)
        {
            var (a, b) = TrimRange;
            double xa = _rd.FrameX(a), xb = _rd.FrameX(b);
            if (Math.Abs(x - xb) <= 10) _tlMode = 3;
            else if (Math.Abs(x - xa) <= 10) _tlMode = 2;
        }
        if (_tlMode == 1) { if (_playing) TogglePlay(); Show(_tlPressIdx); }
        _rangeCanvas.CaptureMouse();
        e.Handled = true;
    }

    void Range_Move(object sender, MouseEventArgs e)
    {
        double x = e.GetPosition(_rangeCanvas).X;
        if (_tlMode == 0)
        {
            if (_rd.Width != _rangeCanvas.ActualWidth) _rd.Width = _rangeCanvas.ActualWidth;
            var (a, b) = TrimRange;
            bool near = _rd.A >= 0 && _seq.Frames.Count > 1 &&
                (Math.Abs(x - _rd.FrameX(a)) <= 10 || Math.Abs(x - _rd.FrameX(b)) <= 10);
            _rangeCanvas.Cursor = near ? Cursors.SizeWE : Cursors.Hand;
            return;
        }
        int idx = Core.RangeDragState.XToIdx(x, _rangeCanvas.ActualWidth, _seq.Frames.Count);
        if (_tlMode == 1 && Math.Abs(x - _tlPressX) > 4)
            _tlMode = 4;   // 拖过阈值：从按下点开始拉选段（[ ] 键同样可设入/出点）
        switch (_tlMode)
        {
            case 1: Show(idx); break;   // 未过阈值=scrub 跳帧
            case 4:
                _rd.A = Math.Min(_tlPressIdx, idx); _rd.B = Math.Max(_tlPressIdx, idx);
                Show(idx); DrawRange(); break;
            case 2: _rd.SetA(idx); DrawRange(); break;
            case 3: _rd.SetB(idx); DrawRange(); break;
        }
        e.Handled = true;
    }

    void Range_Up(object sender, MouseButtonEventArgs e)
    {
        if (_tlMode == 4 && _rd.B - _rd.A < 1) { _rd.A = _rd.B = -1; }   // 退化拖（几乎没动）=清除，不留 1 帧选段
        _tlMode = 0;
        _rangeCanvas.ReleaseMouseCapture();
        DrawRange();
        e.Handled = true;
    }

    (int a, int b) TrimRange { get { _rd.Count = _seq.Frames.Count; return _rd.Effective; } }
}
