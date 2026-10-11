// 渲染候选图标矩阵为 PNG，人工挑选语义最贴合的
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class RenderMatrix
{
    /// <summary>sweep 模式：把 PUA 全区按网格铺出来，肉眼挑选语义合适的字形。</summary>
    public static void Sweep(string outPath)
    {
        int start = 0xE700, end = 0xF8FF, cols = 64, cell = 26, labelW = 64;
        int rows = (end - start + 1 + cols - 1) / cols;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, labelW + cols * cell, rows * cell));
            var tf = new Typeface(new FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var mono = new Typeface("Consolas");
            for (int r = 0; r < rows; r++)
            {
                int rowBase = start + r * cols;
                var lbl = new FormattedText(rowBase.ToString("X4"), System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, mono, 10, Brushes.Gray, 1.0);
                dc.DrawText(lbl, new Point(4, r * cell + 7));
                if (r % 4 == 0) dc.DrawLine(new Pen(Brushes.LightGray, 1), new Point(labelW, r * cell), new Point(labelW + cols * cell, r * cell));
                for (int c = 0; c < cols; c++)
                {
                    int cp = rowBase + c; if (cp > end) break;
                    var g = new FormattedText(((char)cp).ToString(), System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, tf, 17, Brushes.Black, 1.0);
                    if (g.Width > 0.5) // 缺字形宽度为 0，跳过
                        dc.DrawText(g, new Point(labelW + c * cell + 4, r * cell + 3));
                }
            }
        }
        var rtb = new RenderTargetBitmap(labelW + cols * cell, rows * cell, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(outPath);
        enc.Save(fs);
        System.Console.WriteLine($"sweep {start:X4}-{end:X4} -> {outPath}");
    }

    public static void Run()
    {
        var cands = new (string Label, string Chars)[]
        {
            ("Caret-a  ", "\uEA83\uEA84\uEA85\uEA86\uEA87\uEA88"),
            ("Caret-b  ", "\uEA89\uEA8A\uEA8B\uEA8C\uEA8D\uEA8E"),
            ("PauseFill", "\uE769\uF5B1\uF8AC\uEC8F\uEA8D\uEA8C"),
            ("CloseFill", "\uEA39\uE9A3\uF13D\uECC9\uF131\uEB3B"),
            ("GearFill ", "\uE713\uF85D\uE7F3\uEFCF\uF8B6\uE5C0"),
            ("GifFill  ", "\uE116\uEA5C\uE8BE\uF684\uE786\uF4A4"),
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
