using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;

static partial class SelfTest
{
    /// <summary>离屏渲染编辑器整窗，供人工核对版面（截图编辑 / 录制编辑各一张）。</summary>
    /// <param name="outPath">PNG 输出路径。</param>
    /// <param name="frameCount">帧数：1 = 静图编辑，3 = 录制编辑。</param>
    /// <returns>进程退出码。</returns>
    private static int RenderEditor(string outPath, int frameCount)
    {
        _ = new Application();
        var service = new SettingsService();
        typeof(AppHost).GetProperty("Settings")!.SetValue(AppHost.Current, service);
        Theme.Apply("dark", service.Current.Accent);
        Fluent.ApplyGlobalStyles();
        Loc.Apply("zh");
        const int width = 900, height = 520;
        var frames = new List<NekoCapture.Capture.CapturedFrame>();
        for (int i = 0; i < Math.Max(1, frameCount); i++)
            frames.Add(new NekoCapture.Capture.CapturedFrame { Bgra = MockScreen(width, height), W = width, H = height, TimeMs = i * 33.3 });
        var sequence = new NekoCapture.Capture.CapturedSequence
        {
            Frames = frames,
            Region = new NekoCapture.Capture.RectI(0, 0, width, height),
            MonitorBounds = new NekoCapture.Capture.RectI(0, 0, 1920, 1080),
            Fps = 30,
        };
        var window = new EditorWindow(service, sequence)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false,
        };
        window.Show();
        window.Left = -20000; window.Top = -20000;
        PumpMs(320);
        if (window.Content is not FrameworkElement root) { Console.WriteLine("编辑器没建起来"); return 1; }
        // 编辑器走 Mica：窗口本身不画底，离屏渲染出来是一片透明。取证时先垫一层实底
        if (root is System.Windows.Controls.Panel bare) bare.Background = Theme.WindowBgSolidBrush;
        else if (root is System.Windows.Controls.Border frame) frame.Background = Theme.WindowBgSolidBrush;
        root.UpdateLayout();
        double w = Math.Max(1, root.ActualWidth), h = Math.Max(1, root.ActualHeight);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var file = File.Create(outPath)) encoder.Save(file);
        Console.WriteLine($"saved {outPath} {w:N0}x{h:N0}");
        window.Close();
        return 0;
    }
}
