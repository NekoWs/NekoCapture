using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FrameCapture.Drawing;

public enum AnnKind { Dot, Line, Ellipse, Arrow, Freehand, Text }

/// <summary>
/// 一条标注。坐标为帧内归一化 (0..1)，渲染时按目标帧像素缩放，分辨率无关。
/// </summary>
public sealed class Annotation
{
    public AnnKind Kind { get; init; }
    public Color Color { get; init; } = Color.FromArgb(255, 0xE7, 0x48, 0x4C);
    /// <summary>粗细（相对 1080p 的像素数）。</summary>
    public float Thickness { get; init; } = 4f;
    /// <summary>归一化点集：dot=1 点；线/箭头/圆=2 点（圆用包围盒）；手绘=多点；文字=1 点（左上锚点）。</summary>
    public List<(float X, float Y)> Points { get; } = new();
    /// <summary>文字内容（仅 Kind=Text）。</summary>
    public string Text { get; set; } = "";

    public void Render(Graphics g, int w, int h)
    {
        float scale = Math.Min(w, h) / 1080f;
        float pen = Math.Max(1.5f, Thickness * scale);
        using var brush = new SolidBrush(Color);
        using var p = new Pen(Color, pen)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        PointF[] P = Points.Select(pt => new PointF(pt.X * w, pt.Y * h)).ToArray();
        switch (Kind)
        {
            case AnnKind.Dot:
                if (P.Length > 0)
                {
                    float r = pen * 2.2f;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillEllipse(brush, P[0].X - r, P[0].Y - r, r * 2, r * 2);
                }
                break;
            case AnnKind.Line:
                if (P.Length >= 2) { g.SmoothingMode = SmoothingMode.AntiAlias; g.DrawLine(p, P[0], P[1]); }
                break;
            case AnnKind.Ellipse:
                if (P.Length >= 2)
                {
                    var r = RectangleF.FromLTRB(
                        Math.Min(P[0].X, P[1].X), Math.Min(P[0].Y, P[1].Y),
                        Math.Max(P[0].X, P[1].X), Math.Max(P[0].Y, P[1].Y));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawEllipse(p, r);
                }
                break;
            case AnnKind.Arrow:
                if (P.Length >= 2) DrawArrow(g, p, P[0], P[1], pen);
                break;
            case AnnKind.Freehand:
                if (P.Length >= 2) { g.SmoothingMode = SmoothingMode.AntiAlias; g.DrawCurve(p, P); }
                else if (P.Length == 1) g.FillEllipse(brush, P[0].X - pen, P[0].Y - pen, pen * 2, pen * 2);
                break;
            case AnnKind.Text:
                if (P.Length > 0 && !string.IsNullOrEmpty(Text))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    float fontPx = (float)FrameCapture.Core.AnnFont.BakePx(Thickness, w, h);
                    using var fnt = new Font("Microsoft YaHei UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
                    g.DrawString(Text, fnt, brush, P[0].X, P[0].Y);
                }
                break;
        }
    }

    static void DrawArrow(Graphics g, Pen p, PointF a, PointF b, float pen)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float len = Math.Max(10f, pen * 6f);
        double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
        double spread = Math.PI / 7;
        var tip = b;
        var c1 = new PointF(
            (float)(tip.X - len * Math.Cos(ang - spread)),
            (float)(tip.Y - len * Math.Sin(ang - spread)));
        var c2 = new PointF(
            (float)(tip.X - len * Math.Cos(ang + spread)),
            (float)(tip.Y - len * Math.Sin(ang + spread)));
        // 杆画到箭头根部，避免伸出
        float back = len * 0.75f;
        var basePt = new PointF(
            tip.X - back * (float)Math.Cos(ang),
            tip.Y - back * (float)Math.Sin(ang));
        g.DrawLine(p, a, basePt);
        using var br = new SolidBrush(p.Color);
        g.FillPolygon(br, new[] { tip, c1, c2 });
    }
}

/// <summary>把标注烘焙进 BGRA 帧（就地拷贝出新帧列表，不动原帧）。</summary>
public static class Bake
{
    public static List<Capture.CapturedFrame> Apply(IReadOnlyList<Capture.CapturedFrame> frames, IReadOnlyList<Annotation> anns)
    {
        if (anns.Count == 0) return frames.ToList();
        var outFrames = new List<Capture.CapturedFrame>(frames.Count);
        foreach (var f in frames)
        {
            byte[] copy = (byte[])f.Bgra.Clone();
            var gch = System.Runtime.InteropServices.GCHandle.Alloc(copy, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                using var bmp = new Bitmap(f.W, f.H, 4 * f.W, PixelFormat.Format32bppArgb, gch.AddrOfPinnedObject());
                using var g = Graphics.FromImage(bmp);
                foreach (var a in anns) a.Render(g, f.W, f.H);
            }
            finally { gch.Free(); }
            outFrames.Add(new Capture.CapturedFrame { Bgra = copy, W = f.W, H = f.H, TimeMs = f.TimeMs });
        }
        return outFrames;
    }

    /// <summary>裁剪所有帧（帧内像素坐标）。</summary>
    public static List<Capture.CapturedFrame> Crop(IReadOnlyList<Capture.CapturedFrame> frames, int x, int y, int w, int h)
    {
        var outFrames = new List<Capture.CapturedFrame>(frames.Count);
        foreach (var f in frames)
        {
            if (x < 0 || y < 0 || w < 8 || h < 8 || x + w > f.W || y + h > f.H)
                throw new ArgumentException("crop out of bounds");
            byte[] dst = new byte[w * h * 4];
            for (int row = 0; row < h; row++)
                Buffer.BlockCopy(f.Bgra, ((y + row) * f.W + x) * 4, dst, row * w * 4, w * 4);
            outFrames.Add(new Capture.CapturedFrame { Bgra = dst, W = w, H = h, TimeMs = f.TimeMs });
        }
        return outFrames;
    }
}
