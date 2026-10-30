using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>原图上的按字拖选、按词点击与跨行复制。</summary>
internal sealed class OcrImageView : FrameworkElement
{
    readonly OcrOutcome _document;
    readonly List<Cell> _cells = new();
    readonly RotateTransform[] _rotations;
    readonly Cell[][] _lineCells;
    readonly record struct Cell(int Start, int Length, Rect Bounds, int Line, int Word, bool Vertical);
    int _anchor, _caret;
    Point _pressed;
    double _zoom = 1;

    /// <summary>创建使用原图像素坐标的选字表面。</summary>
    /// <param name="document">包含原图与识别坐标的结果。</param>
    internal OcrImageView(OcrOutcome document)
    {
        _document = document; Focusable = true; Cursor = Cursors.IBeam;
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
        _rotations = document.Lines.Select(line => new RotateTransform(line.Angle, line.Center.X, line.Center.Y)).ToArray();
        int offset = 0;
        for (int li = 0; li < document.Lines.Count; li++)
        {
            var line = document.Lines[li];
            for (int wi = 0; wi < line.Words.Count; wi++)
            {
                var word = line.Words[wi];
                int[] starts = StringInfo.ParseCombiningCharacters(word.Text);
                double Weight(int index)
                {
                    char value = word.Text[starts[index]];
                    return value >= '\u2E80' ? 1 : "ilI.,:;'!|".Contains(value) ? 0.3 : "MWmw@".Contains(value) ? 0.9 : 0.6;
                }
                double total = Enumerable.Range(0, starts.Length).Sum(Weight), at = 0;
                bool vertical = word.Bounds.Height > word.Bounds.Width * 2 && starts.Length > 1;
                for (int ci = 0; ci < starts.Length; ci++)
                {
                    double width = Weight(ci) / total;
                    var bounds = vertical
                        ? new Rect(word.Bounds.X, word.Bounds.Y + at * word.Bounds.Height, word.Bounds.Width, width * word.Bounds.Height)
                        : new Rect(word.Bounds.X + at * word.Bounds.Width, word.Bounds.Y, width * word.Bounds.Width, word.Bounds.Height);
                    int length = (ci + 1 < starts.Length ? starts[ci + 1] : word.Text.Length) - starts[ci];
                    _cells.Add(new(offset + word.Start + starts[ci], length, bounds, li, wi, vertical)); at += width;
                }
            }
            offset += line.Text.Length + 1;
        }
        _lineCells = Enumerable.Range(0, document.Lines.Count).Select(line => _cells.Where(cell => cell.Line == line).ToArray()).ToArray();
        Zoom = 1;
    }

    /// <summary>当前原图显示倍率。</summary>
    internal double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 0.01, 4);
            Width = _document.Image!.PixelWidth * _zoom; Height = _document.Image.PixelHeight * _zoom;
            InvalidateVisual();
        }
    }

    /// <summary>按阅读顺序排列的选中文字。</summary>
    internal string SelectedText => _document.Text[Math.Min(_anchor, _caret)..Math.Max(_anchor, _caret)];
    /// <summary>文字选区变化时通知工具栏。</summary>
    internal event Action? SelectionChanged;

    /// <summary>选择整张图片的全部识别文字。</summary>
    internal void SelectAll() => SelectRange(0, _document.Text.Length);

    /// <summary>以全文 UTF-16 位置更新选区。</summary>
    /// <param name="anchor">固定端点。</param>
    /// <param name="caret">活动端点。</param>
    internal void SelectRange(int anchor, int caret)
    {
        anchor = Math.Clamp(anchor, 0, _document.Text.Length); caret = Math.Clamp(caret, 0, _document.Text.Length);
        if (_anchor == anchor && _caret == caret) return;
        _anchor = anchor; _caret = caret; InvalidateVisual(); SelectionChanged?.Invoke();
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.PushTransform(new ScaleTransform(_zoom, _zoom));
        drawing.DrawImage(_document.Image, new Rect(0, 0, _document.Image!.PixelWidth, _document.Image.PixelHeight));
        var accent = Theme.Accent;
        var selection = new SolidColorBrush(Color.FromArgb(100, accent.R, accent.G, accent.B));
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(85, accent.R, accent.G, accent.B)), 1 / _zoom);
        int start = Math.Min(_anchor, _caret), end = Math.Max(_anchor, _caret);
        for (int i = 0; i < _document.Lines.Count; i++)
        {
            drawing.PushTransform(_rotations[i]);
            foreach (var word in _document.Lines[i].Words)
                drawing.DrawRoundedRectangle(null, outline, word.Bounds, 2 / _zoom, 2 / _zoom);
            foreach (var cell in _lineCells[i])
                if (cell.Start < end && cell.Start + cell.Length > start) drawing.DrawRectangle(selection, null, cell.Bounds);
            drawing.Pop();
        }
        drawing.Pop();
    }

    (Cell Cell, Point Local)? Hit(Point point, bool nearbyOnly)
    {
        (Cell Cell, Point Local)? hit = null;
        double closest = double.MaxValue;
        for (int line = 0; line < _document.Lines.Count; line++)
        {
            var local = _rotations[line].Inverse!.Transform(point);
            foreach (var cell in _lineCells[line])
            {
                var bounds = cell.Bounds;
                double dx = Math.Max(0, Math.Max(bounds.Left - local.X, local.X - bounds.Right));
                double dy = Math.Max(0, Math.Max(bounds.Top - local.Y, local.Y - bounds.Bottom));
                double distance = dx * dx + dy * dy * 4;
                if ((nearbyOnly && (dx > 3 || dy > 3)) || distance >= closest) continue;
                closest = distance; hit = (cell, local);
            }
        }
        return hit;
    }

    static int Caret((Cell Cell, Point Local) hit)
    {
        var cell = hit.Cell;
        bool after = cell.Vertical ? hit.Local.Y >= cell.Bounds.Y + cell.Bounds.Height / 2
            : hit.Local.X >= cell.Bounds.X + cell.Bounds.Width / 2;
        return cell.Start + (after ? cell.Length : 0);
    }

    Point ImagePoint(MouseEventArgs e) { var point = e.GetPosition(this); return new Point(point.X / _zoom, point.Y / _zoom); }

    void SelectWord(Cell cell, bool wholeLine)
    {
        var members = _lineCells[cell.Line].Where(member => wholeLine || member.Word == cell.Word).ToArray();
        SelectRange(members[0].Start, members[^1].Start + members[^1].Length);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); Focus();
        var point = ImagePoint(e);
        if (Hit(point, true) is not { } hit) { SelectRange(0, 0); return; }
        e.Handled = true; _pressed = point;
        if (e.ClickCount >= 2) { SelectWord(hit.Cell, e.ClickCount >= 3); return; }
        int caret = Caret(hit);
        SelectRange((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? _anchor : caret, caret);
        CaptureMouse();
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = ImagePoint(e);
        if (IsMouseCaptured && Hit(point, false) is { } hit)
        {
            SelectRange(_anchor, Caret(hit));
            BringIntoView(new Rect(point.X * _zoom, point.Y * _zoom, 1, 1));
        }
        else if (Hit(point, true) is { } hovered)
            ToolTip = _document.Lines[hovered.Cell.Line].Words[hovered.Cell.Word].Text;
        else ToolTip = null;
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture(); e.Handled = true;
        var point = ImagePoint(e);
        if ((_pressed - point).Length < 3 && _anchor == _caret && Hit(point, true) is { } hit) SelectWord(hit.Cell, false);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Escape && _anchor != _caret) { SelectRange(0, 0); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
