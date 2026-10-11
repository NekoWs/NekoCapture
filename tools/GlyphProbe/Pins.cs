using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// GlyphProbe pins: 验证图钉字形是否真实存在（非豆腐块）
static class Pins
{
    public static void Run(string outPath)
    {
        // R50 序号标注候选：E2A6 NumberSymbol / E9F9 Calculator / E8EF CheckboxComposite / EA3C / F215 NumberField / E825 CalculatorMultiply / E9D5 / EC3C / E958 / E8A1
        string[] gs = { "\uE2A6", "\uE9F9", "\uE8EF", "\uEA3C", "\uF215", "\uE825", "\uE9D5", "\uEC3C", "\uE958", "\uE8A1" };
        var ft = new Typeface(new FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        int x = 0; var vt = new DrawingVisual();
        using (var dt = vt.RenderOpen())
        {
            dt.DrawRectangle(Brushes.Black, null, new Rect(0, 0, gs.Length * 90, 90));
            for (int i = 0; i < gs.Length; i++)
            {
                dt.DrawText(new FormattedText(gs[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, ft, 64, Brushes.White, 1.0), new Point(x, 10));
                x += 90;
            }
        }
        var rtb = new RenderTargetBitmap(x, 90, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(vt);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using var f = System.IO.File.Create(outPath);
        enc.Save(f);
    }
}
