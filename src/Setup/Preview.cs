using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FrameCapture.Setup;

/// <summary>
/// 离屏渲染窗口内容成 PNG。发行包里留着这个开关是为了出图核对界面，
/// 不需要真的装一遍、也不需要把窗口弹到桌面上。
/// </summary>
internal static class Preview
{
    /// <summary>渲染指定界面到文件，画布取窗口自身尺寸。</summary>
    /// <param name="path">输出 PNG 路径。</param>
    /// <param name="mode">要渲染的界面形态。</param>
    internal static void Render(string path, SetupWindow.Mode mode)
    {
        var w = new SetupWindow(mode);
        var width = (int)w.Width;
        var height = (int)w.Height;
        var content = (FrameworkElement)w.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(content);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
