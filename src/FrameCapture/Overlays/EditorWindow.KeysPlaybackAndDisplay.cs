using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FrameCapture.Core;
using FrameCapture.Encoding;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>
/// EditorWindow partial：快捷键分派、播放控制与当前帧显示（位图惰性缓存）。
/// </summary>
public sealed partial class EditorWindow : Window
{

    #region 按键分派与播放/帧显示

    /// <summary>键位 → 命令的分派表，由 <see cref="EditorCommands.BuildMap"/> 按当前设置生成；
    /// null 表示设置改过、下一次按键前重建。</summary>
    Dictionary<(ModifierKeys Mods, Key Key), List<string>>? _keyMap;

    /// <summary>设置变更：丢掉分派表，下一次按键按新键位重建。</summary>
    void Editor_OnKeysChanged() => _keyMap = null;

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        var mods = e.KeyboardDevice.Modifiers;
        // Alt 组合在 WPF 里报成 Key.System，真身在 SystemKey 里，不还原就永远命不中键位表
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        DispatchKey(mods, key, e);
    }

    /// <summary>测试通道：把一次按键直投分派表。编辑器窗未激活时 WPF 不会把 PostMessage 的键
    /// 路由进来，而激活窗口本身就是一次抢前台——键盘焦点与前台在 Win32 里是绑死的。
    /// 修饰键由调用方显式给出：真实键盘状态在测试里不可控。</summary>
    /// <param name="mods">修饰键状态。</param>
    /// <param name="key">WPF 键。</param>
    internal void FeedKeyForTest(ModifierKeys mods, Key key) => DispatchKey(mods, key, null);

    /// <summary>键位 → 命令的分派主体。真实按键与测试直投共用这一条，区别只在有没有可标记的
    /// <see cref="KeyEventArgs"/>。</summary>
    /// <param name="mods">本次按键的修饰键状态。</param>
    /// <param name="key">主键（Alt 组合已由调用方从 SystemKey 还原）。</param>
    /// <param name="e">触发分派的按键事件；测试直投为 null。</param>
    void DispatchKey(ModifierKeys mods, Key key, KeyEventArgs? e)
    {
        Trace.Log($"editor key={mods}+{key}");
        // 首次引导打开时吞掉所有键（Enter/Esc 只关引导，绝不落到"确定/放弃捕获"上）
        if (_guide != null)
        {
            if (key is Key.Escape or Key.Enter or Key.Space) CloseGuide();
            if (e != null) e.Handled = true;
            return;
        }
        // 文字输入中：编辑快捷键全部让位（Enter 上屏 / Esc 取消由输入框自理，不落到确定/放弃上）
        if (_textInput != null) return;
        _keyMap ??= EditorCommands.BuildMap(_settings.Current);
        if (!_keyMap.TryGetValue((mods, key), out var ids)) return;
        foreach (var id in ids)
            if (Run(id, e)) { if (e != null) e.Handled = true; return; }
    }

    /// <summary>执行一条编辑器命令。命令与键位解耦：键位由设置页配置，这里只认命令 Id。</summary>
    /// <param name="id">命令 Id，取自 <see cref="EditorCommands"/>。</param>
    /// <param name="e">触发本次分派的按键事件；测试直投为 null。</param>
    /// <returns>命令成立并已执行返回真；条件不成立返回假，同一个键位上的下一条命令接着试。</returns>
    bool Run(string id, KeyEventArgs? e)
    {
        switch (id)
        {
            // 选中工具下 Delete/Backspace=删对象：这条只在有选中时成立，落空就交给同键的撤销 / 清裁剪
            case "delete-object":
                if (!(_tool == Tool.Select && _sel != null)) return false;
                DeleteSelected();
                return true;
            case "undo": UndoAnn(); return true;
            case "redo": RedoAnn(); return true;
            case "color-custom": PickCustomColor(); return true;
            case "width-popup": if (_widthMoreBtn == null) return false; ShowWidthPopup(_widthMoreBtn); return true;

            case "save-frame": SaveFrame(); return true;
            case "copy-frame": ClipboardService.SetFrame(ExportFrame(_index)); return true;
            case "pin-frame": PinCurrentFrame(); return true;
            case "ocr": OcrCurrentFrame(); return true;
            case "save-recording": SaveRecording(); return true;
            case "save-afterimage": SaveAfterimage(); return true;

            case "zoom-in": ZoomStep(1.25); return true;
            case "zoom-out": ZoomStep(1 / 1.25); return true;
            case "zoom-fit": ZoomFit(); return true;
            case "zoom-one": ZoomOneToOne(); return true;

            case "play-toggle": TogglePlay(); return true;
            case "frame-prev": Step(-1); return true;
            case "frame-next": Step(+1); return true;
            case "sec-prev": Step(-_fps); return true;
            case "sec-next": Step(+_fps); return true;
            case "speed-up": SetSpeed(_speed * 2); return true;
            case "speed-down": SetSpeed(_speed / 2); return true;
            case "speed-reset": SetSpeed(1.0); return true;

            // 时间轴裁剪：端点=当前帧，钳位保证至少 2 帧
            case "trim-a": _rd.Count = _seq.Frames.Count; _rd.SetA(_index); Show(_index); return true;
            case "trim-b": _rd.Count = _seq.Frames.Count; _rd.SetB(_index); Show(_index); return true;
            case "trim-clear": _rd.A = _rd.B = -1; Show(_index); return true;

            case "commit": Commit(); return true;
            case "help": HelpWindow.ShowOrFocus(); return true;
            case "close": return CloseStep();
            default:
                if (id.StartsWith("tool.", StringComparison.Ordinal) && ToolByKey(id[5..]) is { } t)
                {
                    SetTool(t);
                    return true;
                }
                return false;
        }
    }

    /// <summary>工具键位字母 → 工具枚举：目录里的字母与这里的枚举一一对应，加新工具两处都要有。</summary>
    /// <param name="k">键位字母，<c>X</c> 是浏览态。</param>
    /// <returns>对应工具；字母不在表里返回 null。</returns>
    static Tool? ToolByKey(string k) => k switch
    {
        "X" => Tool.Browse, "V" => Tool.Select, "L" => Tool.Line, "R" => Tool.Rect,
        "O" => Tool.Ellipse, "A" => Tool.Arrow, "P" => Tool.Freehand, "H" => Tool.Highlight,
        "T" => Tool.Text, "M" => Tool.Mosaic, "C" => Tool.Crop, "N" => Tool.Number,
        _ => null,
    };

    /// <summary>Esc 的分层语义：弹层 → 导出中 → 有选中 → 绘制态 → 浏览态关闭，逐层落定，
    /// 误触 Esc 绝不丢整次捕获。</summary>
    /// <returns>恒为真：这条命令总会在某一层落定。</returns>
    bool CloseStep()
    {
        // 弹层打开时：Esc 只收弹层（分层语义的第一层）
        if (_colorPop is { IsOpen: true }) { _colorPop.IsOpen = false; return true; }
        if (_widthPop is { IsOpen: true }) { _widthPop.IsOpen = false; return true; }
        switch (EscStep.Select(_encoding, _tool != Tool.Browse, _sel != null))
        {
            case EscAction.CancelEncode: _commitCts?.Cancel(); return true;
            case EscAction.ClearSelection:
                SetSelection(null);
                Trace.Log("editor esc→deselect");
                return true;
            case EscAction.ExitTool:
                CancelPendingStroke(); SetTool(Tool.Browse);
                Trace.Log("editor esc→browse");
                return true;
            default: Fluent.CloseWithFade(this); return true;
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
        var bmp = BgraImage.ToSource(i == _index ? Cur() : _seq.Frames[i]);
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
        UpdateMosaicBrushes(); // 马赛克=区域滤镜：内容随帧走，不是静止贴图
        // 计数区时间用「帧号/帧率」= 播放基准，与时间轴刻度(DrawRange 同用 i/fps)一致；
        // 不用 _times 实际捕获时间戳（首帧非 0，与刻度是两套基准，显示会不一致）
        int fps = _fps;
        double eff = _imgRect.Width / Math.Max(1, _frameW);
        string zoomTxt = _zoom <= 1.001 ? "" : (Math.Abs(eff - 1.0) < 0.02 ? "  1:1" : $"  {eff * 100:0}%");
        string cropTxt = "";
        if (_crop.IsCropped)
        {
            var (cqW, cqH) = CropPx(_frameW, _frameH);
            cropTxt = Loc.F(K.Editor.Crop, (int)Math.Round(cqW), (int)Math.Round(cqH));
        }
        var (ta, tb2) = TrimRange;
        // 含两端闭区间 [a,b]：1-based 显示须 tb+1；全范围=未裁剪不显示（tb2<count-1 才算裁过）
        string trimTxt = _rd.A >= 0 && tb2 > ta && (ta > 0 || tb2 < _seq.Frames.Count - 1) ? Loc.F(K.Editor.Export, ta + 1, tb2 + 1) : "";
        // 计数区：帧号 + 当前帧时刻 / 总时长（末帧时刻，与刻度/播放同基准）；总时长让用户一眼看到录制长度
        _hud.Text = $"{_index + 1}/{_seq.Frames.Count}  {_index / (double)fps:0.00}s / {(_seq.Frames.Count - 1) / (double)fps:0.00}s"
            + (_speed != 1.0 ? $"  ×{_speed:0.###}" : "")
            + zoomTxt + cropTxt + trimTxt
            + (_seq.DroppedFrames > 0 ? Loc.F(K.Editor.FramesDroppedByTheMemory, _seq.DroppedFrames) : "");
        DrawRange();
    }
    #endregion

}
