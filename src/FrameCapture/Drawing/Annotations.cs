using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FrameCapture.Drawing;

public enum AnnKind { Dot, Line, Rect, Ellipse, Arrow, Freehand, Highlight, Text, Mosaic, Number }

/// <summary>
/// 一条标注。坐标为帧内归一化 (0..1)，渲染时按目标帧像素缩放，分辨率无关。
/// </summary>
public sealed class Annotation
{
    public AnnKind Kind { get; init; }
    public Color Color { get; init; } = Color.FromArgb(255, 0xE7, 0x48, 0x4C);
    /// <summary>粗细（相对 1080p 的像素数）。</summary>
    public float Thickness { get; init; } = 4f;
    /// <summary>归一化点集：dot=1 点；线/箭头/圆=2 点（圆用包围盒）；手绘=多点；文字=1 点（左上锚点）；马赛克=2 点（对角）。</summary>
    public List<(float X, float Y)> Points { get; } = new();
    /// <summary>文字内容（仅 Kind=Text）。</summary>
    public string Text { get; set; } = "";

    /// <summary>R85：荧光笔的不透明度（0-255）。alpha 是 Highlight 这一「类型」的属性而非颜色的一部分——
    /// 标注颜色仍存不透明色（改色/调色板/最近用色都正常），只在预览与烘焙时统一套此 alpha，两条路径同值保证 WYSIWYG。</summary>
    public const byte HighlightAlpha = 90;

    /// <summary>R76：返回改了颜色/粗细的副本（Kind/几何/文字不变；传 null 表示该项保持原值）。
    /// 供「选中已有标注→改样式」原位替换（可撤销）用。纯函数，供单测。</summary>
    public Annotation WithStyle(Color? color, float? thickness)
    {
        var e = new Annotation { Kind = Kind, Color = color ?? Color, Thickness = thickness ?? Thickness, Text = Text };
        foreach (var p in Points) e.Points.Add(p);
        return e;
    }

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
            case AnnKind.Rect:
                if (P.Length >= 2)
                {
                    var r = RectangleF.FromLTRB(
                        Math.Min(P[0].X, P[1].X), Math.Min(P[0].Y, P[1].Y),
                        Math.Max(P[0].X, P[1].X), Math.Max(P[0].Y, P[1].Y));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                }
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
            case AnnKind.Highlight:
                // 马克笔：半透明 + 平头帽（斜头笔触感），曲线同手绘；alpha 由类型统一施加
                if (P.Length >= 2)
                {
                    using var hp = new Pen(Color.FromArgb(HighlightAlpha, Color.R, Color.G, Color.B), pen)
                    { StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Round };
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawCurve(hp, P);
                }
                else if (P.Length == 1)
                    using (var hb = new SolidBrush(Color.FromArgb(HighlightAlpha, Color.R, Color.G, Color.B)))
                        g.FillEllipse(hb, P[0].X - pen, P[0].Y - pen, pen * 2, pen * 2);
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
            case AnnKind.Number:
                if (P.Length > 0 && !string.IsNullOrEmpty(Text))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    float fontPx = (float)FrameCapture.Core.AnnFont.BakePx(Thickness, w, h);
                    float d = (float)FrameCapture.Core.NumberBadge.Diameter(fontPx);
                    float cx = P[0].X, cy = P[0].Y;
                    g.FillEllipse(brush, cx - d / 2, cy - d / 2, d, d);
                    using var fnt = new Font("Microsoft YaHei UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    using var white = new SolidBrush(Color.White);
                    g.DrawString(Text, fnt, white, new RectangleF(cx - d / 2, cy - d / 2, d, d), sf);
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
            // 马赛克是字节级运算（与编辑器预览同一 MosaicInPlace），先铺底；矢量标注叠在其上
            foreach (var a in anns)
                if (a.Kind == AnnKind.Mosaic && a.Points.Count >= 2)
                {
                    int mx = (int)Math.Round(Math.Min(a.Points[0].X, a.Points[1].X) * f.W);
                    int my = (int)Math.Round(Math.Min(a.Points[0].Y, a.Points[1].Y) * f.H);
                    int mw = (int)Math.Round(Math.Abs(a.Points[1].X - a.Points[0].X) * f.W);
                    int mh = (int)Math.Round(Math.Abs(a.Points[1].Y - a.Points[0].Y) * f.H);
                    Services.BgraImage.MosaicInPlace(copy, f.W, f.H, mx, my, mw, mh,
                        FrameCapture.Core.MosaicBlock.Px(a.Thickness, f.W, f.H));
                }
            var gch = System.Runtime.InteropServices.GCHandle.Alloc(copy, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                using var bmp = new Bitmap(f.W, f.H, 4 * f.W, PixelFormat.Format32bppArgb, gch.AddrOfPinnedObject());
                using var g = Graphics.FromImage(bmp);
                foreach (var a in anns)
                    if (a.Kind != AnnKind.Mosaic) a.Render(g, f.W, f.H);
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
            outFrames.Add(CropFrame(f, x, y, w, h));
        return outFrames;
    }

    /// <summary>裁剪单帧（帧内像素坐标）。残影等稀疏采样场景用它做惰性投影，避免整段物化。</summary>
    public static Capture.CapturedFrame CropFrame(Capture.CapturedFrame f, int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || w < 8 || h < 8 || x + w > f.W || y + h > f.H)
            throw new ArgumentException("crop out of bounds");
        byte[] dst = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(f.Bgra, ((y + row) * f.W + x) * 4, dst, row * w * 4, w * 4);
        return new Capture.CapturedFrame { Bgra = dst, W = w, H = h, TimeMs = f.TimeMs };
    }
}
