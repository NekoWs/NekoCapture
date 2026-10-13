using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Encoding;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

// 快捷键与播放：按键分派 + 播放控制 + 当前帧显示（位图惰性缓存）
public sealed partial class EditorWindow : Window
{

    // ---------------- 行为 ----------------

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        Trace.Log($"editor key={e.KeyboardDevice.Modifiers}+{e.Key}");
        // 首次引导打开时吞掉所有键（Enter/Esc 只关引导，绝不落到"确定/放弃捕获"上）
        if (_guide != null)
        {
            if (e.Key is Key.Escape or Key.Enter or Key.Space) CloseGuide();
            e.Handled = true;
            return;
        }
        // 文字输入中：编辑快捷键全部让位（Enter 上屏 / Esc 取消由输入框自理，不落到确定/放弃上）
        if (_textInput != null) return;
        int fps = _fps;
        var mods = e.KeyboardDevice.Modifiers;
        if (mods == ModifierKeys.Control && (e.Key == Key.Z || e.Key == Key.U)) { UndoAnn(); return; }
        if (mods == ModifierKeys.Control && e.Key == Key.Y) { RedoAnn(); return; }
        // 底部工具快捷键
        if (mods == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.S: SaveFrame(); return;
                case Key.C: ClipboardService.SetFrame(ExportFrame(_index)); return;
                case Key.P: PinCurrentFrame(); return; // R65 钉到屏幕
                case Key.O: OcrCurrentFrame(); return; // R69 识别文字
                case Key.E: SaveRecording(); return;
                case Key.H: SaveAfterimage(); return;
                // 视图缩放（浏览器惯例）：Ctrl +/− 以中心放大缩小，Ctrl+0 适配，Ctrl+1 逐像素 1:1
                case Key.OemPlus or Key.Add: ZoomStep(1.25); return;
                case Key.OemMinus or Key.Subtract: ZoomStep(1 / 1.25); return;
                case Key.D0 or Key.NumPad0: ZoomFit(); return;
                case Key.D1 or Key.NumPad1: ZoomOneToOne(); return;
            }
        }
        if (mods == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.X: SetTool(Tool.Browse); return;
                case Key.V: SetTool(Tool.Select); return;
                case Key.L: SetTool(Tool.Line); return;
                case Key.R: SetTool(Tool.Rect); return;
                case Key.O: SetTool(Tool.Ellipse); return;
                case Key.A: SetTool(Tool.Arrow); return;
                case Key.P: SetTool(Tool.Freehand); return;
                case Key.H: SetTool(Tool.Highlight); return;
                case Key.T: SetTool(Tool.Text); return;
                case Key.M: SetTool(Tool.Mosaic); return;
                case Key.C: SetTool(Tool.Crop); return;
                case Key.N: SetTool(Tool.Number); return;
                // 选中工具下 Delete/Backspace=删对象（优先于全局语义：Delete 撤销 / Backspace 清范围）
                case Key.Delete when _tool == Tool.Select && _sel != null: DeleteSelected(); return;
                case Key.Back when _tool == Tool.Select && _sel != null: DeleteSelected(); return;
                case Key.Delete: UndoAnn(); return;
                case Key.K: PickCustomColor(); return;
                case Key.W: if (_widthMoreBtn != null) ShowWidthPopup(_widthMoreBtn); return;
                // 时间轴裁剪键盘化（R31）：端点=当前帧，钳位保证至少 2 帧；Backspace 清除
                case Key.OemOpenBrackets: _rd.Count = _seq.Frames.Count; _rd.SetA(_index); Show(_index); e.Handled = true; return;
                case Key.OemCloseBrackets: _rd.Count = _seq.Frames.Count; _rd.SetB(_index); Show(_index); e.Handled = true; return;
                case Key.Back: _rd.A = _rd.B = -1; Show(_index); e.Handled = true; return;
            }
        }
        switch (e.Key)
        {
            case Key.Escape:
                // 弹层打开时：Esc 只收弹层（分层语义的第一层）
                if (_colorPop is { IsOpen: true }) { _colorPop.IsOpen = false; e.Handled = true; return; }
                if (_widthPop is { IsOpen: true }) { _widthPop.IsOpen = false; e.Handled = true; return; }
                // 分层语义（T82/T96）：导出中=取消；有选中=先取消选中；绘制态=先退出工具，误触 Esc 绝不丢整次捕获；浏览态=关闭
                switch (FrameCapture.Core.EscStep.Select(_encoding, _tool != Tool.Browse, _sel != null))
                {
                    case FrameCapture.Core.EscAction.CancelEncode: _commitCts?.Cancel(); break;
                    case FrameCapture.Core.EscAction.ClearSelection:
                        SetSelection(null);
                        FrameCapture.Core.Trace.Log("editor esc→deselect");
                        e.Handled = true;
                        break;
                    case FrameCapture.Core.EscAction.ExitTool:
                        CancelPendingStroke(); SetTool(Tool.Browse);
                        FrameCapture.Core.Trace.Log("editor esc→browse");
                        e.Handled = true;
                        break;
                    default: FrameCapture.Core.Fluent.CloseWithFade(this); break;
                }
                break;
            case Key.Enter: Commit(); break;
            case Key.F1: HelpWindow.ShowOrFocus(); e.Handled = true; break;
            case Key.Space: TogglePlay(); e.Handled = true; break;
            case Key.Left when e.KeyboardDevice.Modifiers == ModifierKeys.Shift: Step(-fps); break;
            case Key.Right when e.KeyboardDevice.Modifiers == ModifierKeys.Shift: Step(+fps); break;
            case Key.Left: Step(-1); break;
            case Key.Right: Step(+1); break;
            case Key.Y: RedoAnn(); e.Handled = true; break;
            // 缩放裸键（与 Ctrl 惯例并存）：PageUp/Down 放大缩小，Home 适配，End 逐像素 1:1
            case Key.PageUp: ZoomStep(1.25); e.Handled = true; break;
            case Key.PageDown: ZoomStep(1 / 1.25); e.Handled = true; break;
            case Key.Home: ZoomFit(); e.Handled = true; break;
            case Key.End: ZoomOneToOne(); e.Handled = true; break;
            case Key.OemPlus or Key.Add: SetSpeed(_speed * 2); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract: SetSpeed(_speed / 2); e.Handled = true; break;
            case Key.D0 or Key.Multiply: SetSpeed(1.0); e.Handled = true; break;
        }
    }

    void Step(int delta)
    {
        if (_playing) TogglePlay();
        Show(_index + delta);
    }

    void TogglePlay()
    {
        _playing = !_playing;
        if (_bPlay != null) _bPlay.Content = _playing ? "" : "";
        Trace.Log($"play={_playing}");
        if (_playing) _playTimer.Start();
        else _playTimer.Stop();
    }

    double _speed = 1.0;

    /// <summary>播放倍速 ×0.25–×8（＋/－ 调整，0 或 × 复位）。</summary>
    void SetSpeed(double s)
    {
        _speed = Math.Clamp(s, 0.25, 8.0);
        _playTimer.Interval = TimeSpan.FromMilliseconds(
            Math.Max(1000.0 / _fps, 20) / _speed);
        Show(_index);
    }

    /// <summary>惰性取帧位图：命中缓存直接给，否则从帧源（内存表或 spool 磁盘）转一张，
    /// 超上限按 FIFO 逐出最久未看的。避免长录制全量驻留 OOM。</summary>
    BitmapSource Bmp(int i)
    {
        if (_bmpCache.TryGetValue(i, out var hit)) return hit;
        var bmp = BgraImage.ToSource(_seq.Frames[i]);
        _bmpCache[i] = bmp; _bmpFifo.Enqueue(i);
        while (_bmpFifo.Count > _bmpCacheMax)
        {
            int k = _bmpFifo.Dequeue();
            if (k != i) _bmpCache.Remove(k);
        }
        return bmp;
    }

    void Show(int index)
    {
        _index = Math.Clamp(index, 0, _seq.Frames.Count - 1);
        _view.Source = Bmp(_index);
        UpdateMosaicBrushes(); // 马赛克=区域滤镜：内容随帧走，不是静止贴图（R50）
        // 计数区时间用「帧号/帧率」= 播放基准，与时间轴刻度(DrawRange 同用 i/fps)一致；
        // 不用 _times 实际捕获时间戳（首帧非 0、且与刻度基准两套，R111 看截图抓到不一致）
        int fps = _fps;
        double eff = _imgRect.Width / Math.Max(1, _frameW);
        string zoomTxt = _zoom <= 1.001 ? "" : (Math.Abs(eff - 1.0) < 0.02 ? "  1:1" : $"  {eff * 100:0}%");
        string cropTxt = "";
        if (_crop.IsCropped)
        {
            var (cqW, cqH) = CropPx(_frameW, _frameH);
            cropTxt = $"  裁剪 {cqW:0}×{cqH:0}";
        }
        var (ta, tb2) = TrimRange;
        // 含两端闭区间 [a,b]：1-based 显示须 tb+1（R31 抓到差一）；全范围=未裁剪不显示（R39 逐帧复查抓到 tb2<count-1 差一）
        string trimTxt = _rd.A >= 0 && tb2 > ta && (ta > 0 || tb2 < _seq.Frames.Count - 1) ? $"  导出 {ta + 1}–{tb2 + 1}" : "";
        // 计数区：帧号 + 当前帧时刻 / 总时长（末帧时刻，与刻度/播放同基准）；总时长让用户一眼看到录制长度（R114）
        _hud.Text = $"{_index + 1}/{_seq.Frames.Count}  {_index / (double)fps:0.00}s / {(_seq.Frames.Count - 1) / (double)fps:0.00}s"
            + (_speed != 1.0 ? $"  ×{_speed:0.###}" : "")
            + zoomTxt + cropTxt + trimTxt
            + (_seq.DroppedFrames > 0 ? $"  ⚠内存预算已丢 {_seq.DroppedFrames} 帧" : "");
        DrawRange();
    }
}
