using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
var app = new Application();
if (args.Length > 0 && args[0] == "matrix") { RenderMatrix.Run(); return; }
if (args.Length > 1 && args[0] == "sweep") { RenderMatrix.Sweep(args[1]); return; }
string[] gs = { "\uE768", "\uE769", "\uE76A", "\uE76B", "\uE76C" };
var ft = new Typeface(new FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
int x = 0; var vt = new DrawingVisual();
using (var dt = vt.RenderOpen())
{
    for (int i = 0; i < gs.Length; i++)
    {
        dt.DrawText(new FormattedText(gs[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, ft, 64, Brushes.White, 1.0), new Point(x, 10));
        x += 90;
    }
}
var rtb = new RenderTargetBitmap(x, 90, 96, 96, PixelFormats.Pbgra32);
rtb.Render(vt);
var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
using var f = System.IO.File.Create(args.Length > 0 ? args[0] : "probe.png");
enc.Save(f);
