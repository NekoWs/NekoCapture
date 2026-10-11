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
            ("DblTapA  ", "\uE7C4\uE962\uE815\uE760\uEAC1\uEFB7"),
            ("DblTapB  ", "\uE9D9\uE7F1\uE853\uE9E0\uF22B\uE8B7"),
            ("DblTapC  ", "\uEC4A\uE7F4\uE945\uE7C3\uE9A5\uE823"),
            ("DblTapD  ", "\uE768\uF5B0\uE105\uE8A1\uE81C\uE735"),
            ("DblTapE  ", "\uE8C8\uE8C9\uE8CB\uE8CC\uE8CD\uE8CE"),
            ("DblTapF  ", "\uE7AD\uE753\uE9F5\uEB51\uE900\uE909"),
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
