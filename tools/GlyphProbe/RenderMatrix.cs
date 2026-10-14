// 渲染候选图标矩阵为 PNG，人工挑选语义最贴合的
using System.Windows;
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

    /// <summary>glyphs 模式：渲染指定码点集合（十六进制串，如 E70E E70D E74D），缺字形（宽度 0）标 MISSING，用于验证图标可用性。</summary>
    public static void Glyphs(string outPath, string[] hexes)
    {
        int cell = 96, labelH = 22;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, cell * hexes.Length, cell));
            var tf = new Typeface(new FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var mono = new Typeface("Consolas");
            for (int i = 0; i < hexes.Length; i++)
            {
                int cp = System.Convert.ToInt32(hexes[i], 16);
                var ch = ((char)cp).ToString();
                var g = new FormattedText(ch, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 40, Brushes.Black, 1.0);
                bool missing = g.Width <= 0.5;
                if (!missing) dc.DrawText(g, new Point(i * cell + (cell - g.Width) / 2, 12));
                var lbl = new FormattedText(missing ? hexes[i] + " MISS" : hexes[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mono, 11, missing ? Brushes.Red : Brushes.Gray, 1.0);
                dc.DrawText(lbl, new Point(i * cell + 6, cell - labelH));
            }
        }
        var rtb = new RenderTargetBitmap(cell * hexes.Length, cell, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(outPath);
        enc.Save(fs);
        System.Console.WriteLine($"glyphs -> {outPath}");
    }

    public static void Run()
        {
            var cands = new (string Label, string Chars)[]
            {
            ("List     ", "\uE292\uE8FD\uEA37\uEB3C\uE200\uE168"),
            ("Num      ", "\uE8EF\uE9F5\uE9F9\uF20B\uE1CB\uE8C1"),
            ("Step     ", "\uE8CB\uE7C1\uE7C6\uE8A7\uE71D\uE8AB"),
            ("Mark     ", "\uE707\uE7C4\uE7B9\uE7B3\uE81F\uE73C"),
            ("Flag     ", "\uE7C1\uE8AB\uE12A\uE8A7\uE71D\uE73C"),
            ("Misc     ", "\uE9D5\uE9CE\uE912\uE1C3\uE8C2\uE8D3"),
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
