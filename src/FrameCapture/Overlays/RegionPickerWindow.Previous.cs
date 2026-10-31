using FrameCapture.Capture;

namespace FrameCapture.Overlays;

/// <summary>上次选区的整块复用与拖框边缘吸附。</summary>
public sealed partial class RegionPickerWindow
{
    private (int X, int Y) _dragRawStart;
    private RectI? PreviousRegionAt((int X, int Y) point)
        => _snapLastRegion && _existing is { } previous && previous.Contains(point.X, point.Y) ? previous : null;

    private (int X, int Y) SnapPreviousPoint((int X, int Y) point)
    {
        if (!_snapLastRegion || _existing is not { } previous) return point;
        int tolerance = (int)Math.Ceiling(8 * DpiScale);
        if (point.X < previous.X - tolerance || point.X > previous.X + previous.W + tolerance
            || point.Y < previous.Y - tolerance || point.Y > previous.Y + previous.H + tolerance) return point;
        int x = point.X, y = point.Y;
        if (Math.Abs(x - previous.X) <= tolerance) x = previous.X;
        else if (Math.Abs(x - (previous.X + previous.W)) <= tolerance) x = previous.X + previous.W;
        if (Math.Abs(y - previous.Y) <= tolerance) y = previous.Y;
        else if (Math.Abs(y - (previous.Y + previous.H)) <= tolerance) y = previous.Y + previous.H;
        return (x, y);
    }
}
