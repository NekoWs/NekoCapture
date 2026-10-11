// 渲染候选图标矩阵为 PNG，人工挑选语义最贴合的
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class RenderMatrix
{
    public static void Run()
    {
        var cands = new (string Label, string Chars)[]
        {
            ("Redo-a  ", "\uE7A6\uE7A7\uE72C\uE72D\uE70B\uF0B2"),
            ("Redo-b  ", "\uE7A6\uE7A6\uE7A6\uE7A7\uE7A7\uE7A7"),
        };
int cellW = 130, cellH = 86, labelW = 140;
        int rows = cands.Length, cols = 6;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, labelW + cols * cellW, rows * cellH));
            var tf = new Typeface(new FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            for (int r = 0; r < rows; r++)
            {
                var ft = new FormattedText(cands[r].Label, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Consolas"), 15, Brushes.Black, 1.0);
                dc.DrawText(ft, new Point(6, r * cellH + 28));
                for (int c = 0; c < cols; c++)
                {
                    string ch = cands[r].Chars[c..(c + 1)];
                    var g = new FormattedText(ch, System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, tf, 34, Brushes.Black, 1.0);
                    dc.DrawText(g, new Point(labelW + c * cellW + 42, r * cellH + 18));
                    var hex = new FormattedText(((int)ch[0]).ToString("X4"), System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Consolas"), 11, Brushes.Gray, 1.0);
                    dc.DrawText(hex, new Point(labelW + c * cellW + 30, r * cellH + 62));
                }
            }
        }
        var rtb = new RenderTargetBitmap(labelW + cols * cellW, rows * cellH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(@"D:\Backup\Capture\scripts\glyph-matrix.png");
        enc.Save(fs);
        System.Console.WriteLine("matrix written");
    }
}
// 顶层不需要，用独立 Main：通过 StartupObject 指定
