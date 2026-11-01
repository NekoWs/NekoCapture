using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NekoCapture.Core;

public static class Prog
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new Application();

        var stack = new StackPanel();
        var s1 = new Slider { Minimum = 0, Maximum = 54, Value = 20, Width = 300, Margin = new Thickness(20, 20, 0, 0) };
        Fluent.StyleSlider(s1, Color.FromRgb(58, 58, 64), Color.FromRgb(0x4C, 0xC2, 0xFF));
        stack.Children.Add(s1);
        stack.Measure(new Size(420, 120));
        stack.Arrange(new Rect(0, 0, 420, 120));
        stack.UpdateLayout();

        var rtb = new RenderTargetBitmap(420, 120, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(stack);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var f = File.Create(args.Length > 0 ? args[0] : "slider.png");
        enc.Save(f);
    }
}
