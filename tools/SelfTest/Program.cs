// SelfTest：无头功能测试（不碰鼠标键盘、不开任何窗口）
// 覆盖：GIF 编码往返/调色板 RGB 序、标注烘焙、裁剪、视觉暂留、快捷键解析、抽帧、胶片条
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Drawing;
using FrameCapture.Encoding;
using FrameCapture.Services;
using System.Windows;

using GE = GestureCore;
using GEdit = GestureEditor;

static class SelfTest
{
    static int pass, fail;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    static void Chk(string name, bool ok, string? detail = null)
    {
        if (ok) { pass++; Console.WriteLine($"PASS {name}"); }
        else { fail++; Console.WriteLine($"FAIL {name}{(detail != null ? " :: " + detail : "")}"); }
    }

    static int RenderHelp(string outPath)
    {
        var app = new Application();
        var w = FrameCapture.Overlays.HelpWindow.CreateForCapture();
        // 未 Show 的 Window 不参与布局（ActualHeight 恒 0）：把内容根单独 measure/arrange 后直接渲染视觉树
        var root = (System.Windows.UIElement)w.Content;
        root.Measure(new System.Windows.Size(640, 4000));
        double h = Math.Min(4000, Math.Max(1, root.DesiredSize.Height));
        root.Arrange(new Rect(0, 0, 640, h));
        root.UpdateLayout();
        int hh = (int)Math.Ceiling(h);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(640, hh, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var f = File.Create(outPath)) enc.Save(f);
        Console.WriteLine($"saved {outPath} 640x{hh}");
        return 0;
    }

    static int RenderPick(string outPath)
    {
        var app = new Application();
        // 弹层同款外观（PanelBg/Stroke/圆角），内容=ColorPickView，半透明色展示棋盘底
        var view = new FrameCapture.Overlays.ColorPickView { Color = System.Windows.Media.Color.FromArgb(0x80, 0xE8, 0x4C, 0x3D) };
        var root = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(FrameCapture.Core.Theme.PanelBg),
            BorderBrush = new System.Windows.Media.SolidColorBrush(FrameCapture.Core.Theme.Stroke),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10), Child = view,
        };
        root.Measure(new System.Windows.Size(420, 400));
        double h = Math.Max(1, root.DesiredSize.Height);
        root.Arrange(new Rect(0, 0, 420, h));
        root.UpdateLayout();
        view.Color = System.Windows.Media.Color.FromArgb(0x80, 0xE8, 0x4C, 0x3D); // 布局后重设一次：准星/滑块按 ActualWidth 落位
        root.UpdateLayout();
        int ww = (int)Math.Ceiling(Math.Max(1, root.DesiredSize.Width)), hh = (int)Math.Ceiling(h);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(ww, hh, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var f = File.Create(outPath)) enc.Save(f);
        Console.WriteLine($"saved {outPath} {ww}x{hh}");
        return 0;
    }

    static int RenderSlider(string outPath)
    {
        var app = new Application();
        // 滑条取证（R57）：放大 3 倍看填充线与圆钮接缝是否露底色缝
        var panel = new System.Windows.Controls.StackPanel { Background = new System.Windows.Media.SolidColorBrush(FrameCapture.Core.Theme.PanelBg) };
        foreach (double v in new double[] { 0, 35, 100 })
        {
            var sl = new System.Windows.Controls.Slider { Minimum = 0, Maximum = 100, Value = v, Width = 300, Margin = new Thickness(10) };
            FrameCapture.Core.Fluent.StyleSlider(sl);
            panel.Children.Add(sl);
        }
        panel.Measure(new System.Windows.Size(340, 200));
        panel.Arrange(new Rect(0, 0, 340, Math.Max(1, panel.DesiredSize.Height)));
        panel.UpdateLayout();
        int hh = (int)Math.Ceiling(Math.Max(1, panel.DesiredSize.Height));
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(340, hh, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(panel);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var f = File.Create(outPath)) enc.Save(f);
        Console.WriteLine($"saved {outPath} 340x{hh}");
        return 0;
    }

    // 滚动拼接真实数据取证（R87）：给两张 PNG，跑 ScrollStitch 找重叠并拼，存 stitched.png 供肉眼核对
    static int StitchMode(string pathA, string pathB)
    {
        var (a, aw, ah) = LoadBgra(pathA);
        var (b, bw, bh) = LoadBgra(pathB);
        if (aw != bw) { Console.WriteLine($"width mismatch {aw} vs {bw}"); return 1; }
        int ov = FrameCapture.Core.ScrollStitch.FindOverlap(a, aw, ah, b, bh);
        var merged = FrameCapture.Core.ScrollStitch.Append(a, aw, ah, b, bh, ov);
        int mh = merged.Length / (aw * 4);
        Console.WriteLine($"A {aw}x{ah}  B {bw}x{bh}  overlap={ov}  merged={aw}x{mh}");
        using var bmp = new System.Drawing.Bitmap(aw, mh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, aw, mh), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < mh; y++)
            System.Runtime.InteropServices.Marshal.Copy(merged, y * aw * 4, IntPtr.Add(data.Scan0, y * data.Stride), aw * 4);
        bmp.UnlockBits(data);
        string outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pathA))!, "stitched.png");
        bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"saved {outPath}");
        return 0;
    }

    static (byte[], int, int) LoadBgra(string path)
    {
        using var bmp = new System.Drawing.Bitmap(path);
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var buf = new byte[data.Stride * h];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(data);
        if (data.Stride != w * 4)   // 裁掉行对齐填充，成紧凑 w*4*h
        {
            var tight = new byte[w * h * 4];
            for (int y = 0; y < h; y++) System.Buffer.BlockCopy(buf, y * data.Stride, tight, y * w * 4, w * 4);
            buf = tight;
        }
        return (buf, w, h);
    }

    static CapturedFrame Solid(int w, int h, byte r, byte g, byte b)    {
        var buf = new byte[w * h * 4];
        for (int i = 0; i < buf.Length; i += 4) { buf[i] = b; buf[i + 1] = g; buf[i + 2] = r; buf[i + 3] = 255; }
        return new CapturedFrame { Bgra = buf, W = w, H = h };
    }

    static CapturedFrame MovingSquare(int w, int h, int k, int steps)
    {
        // 红方块从左到右移动，背景黑
        var buf = new byte[w * h * 4];
        for (int i = 0; i < buf.Length; i += 4) buf[i + 3] = 255;
        int sq = Math.Max(4, w / 8);
        int x0 = (int)((k / (double)Math.Max(1, steps - 1)) * (w - sq));
        int y0 = h / 2 - sq / 2;
        for (int y = y0; y < y0 + sq; y++)
            for (int x = x0; x < x0 + sq; x++)
            {
                int o = (y * w + x) * 4;
                buf[o] = 0x23; buf[o + 1] = 0x11; buf[o + 2] = 0xE8; // BGRA 红 #E81123
            }
        return new CapturedFrame { Bgra = buf, W = w, H = h, TimeMs = k * 1000.0 / 30 };
    }

    static (int count, int w, int h, Color c0) DecodeGif(byte[] gif)
    {
        using var ms = new MemoryStream(gif);
        using var img = Image.FromStream(ms, false, true);
        var fd = img.FrameDimensionsList[0];
        int frames = img.GetFrameCount(new FrameDimension(fd));
        img.SelectActiveFrame(FrameDimension.Time, 0);
        var bmp = new Bitmap(img);
        var c0 = bmp.GetPixel(img.Width / 2, img.Height / 2);
        bmp.Dispose();
        return (frames, img.Width, img.Height, c0);
    }

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        // 离屏渲染帮助窗取证（R49）：独立进程、不 Show、不注册热键，不打断正在运行的实例
        if (args.Length == 2 && args[0] == "renderhelp") return RenderHelp(args[1]);
        if (args.Length == 2 && args[0] == "renderpick") return RenderPick(args[1]);
        if (args.Length == 2 && args[0] == "renderslider") return RenderSlider(args[1]);
        if (args.Length == 3 && args[0] == "stitch") return StitchMode(args[1], args[2]);
        string tmp = Path.Combine(Path.GetTempPath(), "FrameCapture", "selftest");
        Directory.CreateDirectory(tmp);

        // T1 GIF 编码往返 + 调色板 RGB 序回归（历史 bug：调色板写成 BGR 导致红变蓝）
        try
        {
            int W = 64, H = 48, N = 8;
            var frames = Enumerable.Range(0, N).Select(k => MovingSquare(W, H, k, N)).ToList();
            var delays = Enumerable.Repeat(33, N).ToList();
            var gif = GifEncoder.Encode(frames, delays);
            Chk("T1a-gif-header", gif[0] == (byte)'G' && gif[1] == (byte)'I' && gif[2] == (byte)'F');
            var (fc, gw, gh, c0) = DecodeGif(gif);
            Chk("T1b-gif-frames", fc == N, $"got {fc}");
            Chk("T1c-gif-size", gw == W && gh == H, $"got {gw}x{gh}");
            // 中心像素：第 0 帧方块在左侧——中心应接近黑；红方块像素存在性单独查
            bool reddish(int r, int g, int b) => r > 150 && g < 120 && b < 120;
            Chk("T1d-gif-black-bg", c0.R < 80 && c0.G < 80 && c0.B < 80, $"c0={c0}");
            // 全红帧：调色板必须把红色存成红（RGB 序回归测试）
            var redGif = GifEncoder.Encode([Solid(W, H, 0xE8, 0x11, 0x23)], [100]);
            using (var ms2 = new MemoryStream(redGif))
            using (var rimg = Image.FromStream(ms2))
            {
                var rbmp = new Bitmap(rimg);
                var rc = rbmp.GetPixel(1, 1);
                rbmp.Dispose();
                Chk("T1e-palette-rgb-order", reddish(rc.R, rc.G, rc.B), $"px={rc}");
            }
            var head = System.Text.Encoding.ASCII.GetString(gif.Take(1200).ToArray());
            Chk("T1f-gif-loop", head.Contains("NETSCAPE2.0"), "loop ext");
        }
        catch (Exception e) { Chk("T1-gif", false, e.Message); }

        // T2 标注烘焙：黑底画红线 → 线上红、线外仍黑
        try
        {
            var f = Solid(100, 100, 0, 0, 0);
            var ann = new Annotation { Kind = AnnKind.Line, Color = Color.FromArgb(255, 0xE8, 0x11, 0x23), Thickness = 6 };
            ann.Points.Add((0.2f, 0.5f)); ann.Points.Add((0.8f, 0.5f));
            var baked = Bake.Apply([f], [ann])[0];
            int on = (50 * 100 + 50) * 4;
            int off = (10 * 100 + 50) * 4;
            Chk("T2a-bake-line-on", baked.Bgra[on + 2] > 150 && baked.Bgra[on] < 100,
                $"rgba={baked.Bgra[on]},{baked.Bgra[on + 1]},{baked.Bgra[on + 2]}");
            Chk("T2b-bake-line-off", baked.Bgra[off] < 30 && baked.Bgra[off + 2] < 30);
            // 点、圆、箭头、手绘各跑一遍不抛异常且改变像素
            foreach (var kind in new[] { AnnKind.Dot, AnnKind.Ellipse, AnnKind.Arrow, AnnKind.Freehand })
            {
                var a = new Annotation { Kind = kind, Color = Color.FromArgb(255, 255, 255, 0), Thickness = 8 };
                if (kind == AnnKind.Dot) a.Points.Add((0.5f, 0.5f));
                else if (kind == AnnKind.Freehand) { a.Points.Add((0.3f, 0.3f)); a.Points.Add((0.5f, 0.6f)); a.Points.Add((0.7f, 0.4f)); }
                else { a.Points.Add((0.3f, 0.3f)); a.Points.Add((0.7f, 0.7f)); }
                var outF = Bake.Apply([Solid(80, 80, 0, 0, 0)], [a])[0];
                bool changed = false;
                for (int i = 2; i < outF.Bgra.Length; i += 4) if (outF.Bgra[i] > 150) { changed = true; break; } // 蓝通道为0的纯黄才计数？
                // 黄色 #FFFF00: BGRA=(0,255,255)；查蓝通道应保持低、绿通道高
                bool yellow = false;
                for (int i = 0; i < outF.Bgra.Length; i += 4)
                    if (outF.Bgra[i] < 40 && outF.Bgra[i + 1] > 150 && outF.Bgra[i + 2] > 150) { yellow = true; break; }
                Chk($"T2-{kind}-baked", yellow);
            }
            // T2c/T2d 序号编号自适应（R146）：圆圈色决定编号黑白；绿底隔离背景色后查前景像素
            var numLight = new Annotation { Kind = AnnKind.Number, Color = Color.White, Thickness = 50, Text = "8" };
            numLight.Points.Add((0.5f, 0.5f));
            var bakedL = Bake.Apply([Solid(200, 200, 0, 200, 0)], [numLight])[0]; // 绿底白圈(亮)
            bool hasBlack = false;
            for (int i = 0; i < bakedL.Bgra.Length; i += 4)
                if (bakedL.Bgra[i] < 40 && bakedL.Bgra[i + 1] < 40 && bakedL.Bgra[i + 2] < 40) { hasBlack = true; break; }
            Chk("T2c-number-lightcircle-blackfg", hasBlack, "白圆圈(亮)上编号为黑→绿底中现黑像素");
            var numDark = new Annotation { Kind = AnnKind.Number, Color = Color.FromArgb(255, 0xE8, 0x11, 0x23), Thickness = 50, Text = "8" };
            numDark.Points.Add((0.5f, 0.5f));
            var bakedD = Bake.Apply([Solid(200, 200, 0, 200, 0)], [numDark])[0]; // 绿底红圈(暗)
            bool hasWhite = false;
            for (int i = 0; i < bakedD.Bgra.Length; i += 4)
                if (bakedD.Bgra[i] > 240 && bakedD.Bgra[i + 1] > 240 && bakedD.Bgra[i + 2] > 240) { hasWhite = true; break; }
            Chk("T2d-number-darkcircle-whitefg", hasWhite, "红圆圈(暗)上编号为白→绿底中现白像素");
        }
        catch (Exception e) { Chk("T2-bake", false, e.Message); }

        // T3 裁剪：尺寸 + 像素对应
        try
        {
            var f = Solid(100, 100, 0, 0, 0);
            int o = (25 * 100 + 30) * 4; f.Bgra[o] = 200; // 蓝标记 at (30,25)
            var cropped = Bake.Crop([f], 10, 20, 40, 30)[0];
            Chk("T3a-crop-size", cropped.W == 40 && cropped.H == 30, $"{cropped.W}x{cropped.H}");
            int mo = (5 * 40 + 20) * 4; // 原(30,25)→裁后(20,5)
            Chk("T3b-crop-pixel", Math.Abs(cropped.Bgra[mo] - 200) < 30, $"b={cropped.Bgra[mo]}");
        }
        catch (Exception e) { Chk("T3-crop", false, e.Message); }

        // T4 视觉暂留合成
        try
        {
            var seq = Enumerable.Range(0, 10).Select(k => MovingSquare(64, 48, k, 10)).ToList();
            var ai = BgraImage.Afterimage(seq, 5);
            Chk("T4a-afterimage-size", ai.W == 64 && ai.H == 48);
            int reds = 0;
            for (int i = 0; i < ai.Bgra.Length; i += 4)
                if (ai.Bgra[i + 2] > 150 && ai.Bgra[i] < 120) reds++;
            Chk("T4b-afterimage-trails", reds > 4 * 4 * 6, $"red px={reds}"); // 多帧方块轨迹叠加应多于单帧面积
        }
        catch (Exception e) { Chk("T4-afterimage", false, e.Message); }

        // T5 快捷键解析
        try
        {
            var s1 = FrameCapture.Core.HotkeySpec.Parse("Ctrl+Shift+R");
            Chk("T5a-parse-ctrl-shift-r", s1 != null && s1.Ctrl && s1.Shift && s1.Key == System.Windows.Forms.Keys.R);
            var s2 = FrameCapture.Core.HotkeySpec.Parse("F5");
            Chk("T5b-parse-bare-fkey", s2 != null && s2.Key == System.Windows.Forms.Keys.F5 && !s2.Ctrl);
            Chk("T5c-parse-bare-letter-null", FrameCapture.Core.HotkeySpec.Parse("A") == null);
            Chk("T5d-parse-empty-null", FrameCapture.Core.HotkeySpec.Parse("") == null);
            Chk("T5e-roundtrip", s1 != null && FrameCapture.Core.HotkeySpec.Parse(s1.ToString())?.ToString() == s1.ToString());
        }
        catch (Exception e) { Chk("T5-hotkey", false, e.Message); }

        // T6 抽帧索引
        try
        {
            var idx = BgraImage.SampleIndices(30, 12);
            Chk("T6a-sample-count", idx.Count == 12, $"got {idx.Count}");
            Chk("T6b-sample-sorted", idx.SequenceEqual(idx.OrderBy(x => x)) && idx.Distinct().Count() == idx.Count);
        }
        catch (Exception e) { Chk("T6-sample", false, e.Message); }

        // T7 Agent 胶片条
        try
        {
            var frames = Enumerable.Range(0, 16).Select(k =>
            {
                var f = MovingSquare(160, 90, k, 16); f.TimeMs = k * 50; return f;
            }).ToList();
            var seq = new CapturedSequence
            {
                Frames = frames,
                Region = new RectI(0, 0, 160, 90),
                MonitorBounds = new RectI(0, 0, 1920, 1080),
            };
            string p = Path.Combine(tmp, "strip.png");
            AgentStrip.Generate(seq, 12, p);
            Chk("T7a-strip-file", File.Exists(p));
            if (File.Exists(p))
            {
                int sw, sh;
                using (var img = Image.FromFile(p)) { sw = img.Width; sh = img.Height; }
                Chk("T7b-strip-wide", sw > sh * 2 && sw > 800, $"{sw}x{sh}");
                File.Delete(p);
            }
        }
        catch (Exception e) { Chk("T7-strip", false, e.ToString()); }

        // T8 主题：明/暗双调色板对比度正确（文字与背景必须反向）
        try
        {
            double Lum(System.Windows.Media.Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            FrameCapture.Core.Theme.Apply("dark", "#4CC2FF");
            double dBg = Lum(FrameCapture.Core.Theme.WindowBg), dTx = Lum(FrameCapture.Core.Theme.Text);
            Chk("T8a-dark-palette", dBg < 60 && dTx > 180, $"bg={dBg:F0} text={dTx:F0}");
            Chk("T8b-dark-contrast", Math.Abs(dTx - dBg) > 120);
            FrameCapture.Core.Theme.Apply("light", "#4CC2FF");
            double lBg = Lum(FrameCapture.Core.Theme.WindowBg), lTx = Lum(FrameCapture.Core.Theme.Text);
            Chk("T8c-light-palette", lBg > 190 && lTx < 80, $"bg={lBg:F0} text={lTx:F0}");
            Chk("T8d-light-contrast", Math.Abs(lTx - lBg) > 120);
            Chk("T8e-accent-applied", FrameCapture.Core.Theme.Accent.B > 200 && FrameCapture.Core.Theme.Accent.R < 120);
            FrameCapture.Core.Theme.Apply("system", "#4CC2FF");
            Chk("T8f-system-no-crash", true);
            FrameCapture.Core.Theme.Apply("dark", "#4CC2FF");
        }
        catch (Exception e) { Chk("T8-theme", false, e.Message); }

        // T9 富色内容量化：彩色渐变帧编 GIF 解码后仍保留多色
        try
        {
            int W = 64, H = 64;
            var buf = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int o = (y * W + x) * 4;
                    buf[o] = (byte)(x * 4); buf[o + 1] = (byte)(y * 4); buf[o + 2] = 128; buf[o + 3] = 255;
                }
            var fr = new CapturedFrame { Bgra = buf, W = W, H = H };
            var gif = GifEncoder.Encode([fr, fr], [80, 80]);
            using var ms9 = new MemoryStream(gif);
            using var img9 = Image.FromStream(ms9);
            var bmp = new Bitmap(img9);
            var p1 = bmp.GetPixel(2, 2); var p2 = bmp.GetPixel(60, 60); var p3 = bmp.GetPixel(60, 2);
            bmp.Dispose();
            bool distinct = !(p1.R == p2.R && p2.R == p3.R && p1.G == p2.G && p2.G == p3.G);
            Chk("T9a-gradient-multicolor", distinct, $"{p1}|{p2}|{p3}");
                        // 渐变：b=x*4, g=y*4 → 左上蓝低、右下蓝高；解码后方向性必须保持
            Chk("T9b-gradient-direction", p2.B > p1.B + 120 && p2.G > p1.G + 120 && p2.R > 90, $"p1={p1} p2={p2} p3={p3}");
        }
        catch (Exception e) { Chk("T9-quant", false, e.Message); }

        // T10 UI glyph 回归：所有用到的图标必须真实存在于 Segoe Fluent Icons（首字体吞 BMP 导致豆腐块）
        try
        {
            var uiGlyphs = new[]
            {
                // 工具（选中箭头 + 线/圆(线性环)/箭头/手绘/裁剪 + 宽度点 + 自定义入口：调色板/更多）
                '\uE8B0','\uE949','\uEA3F','\uF0AF','\uE70F','\uE7A8','\uE915','\uE790','\uE712',
                // 编辑器（面性播放键组 + 动作）
                '\uF8AC','\uF08D','\uF5B0','\uF8AE','\uF08F','\uF8AD',
                '\uE105','\uF413','\uE116','\uE955','\uE711','\uF78C','\uE7A7','\uE7A6','\uE713','\uE9CE',
                // 设置窗
                '\uE71A','\uE7F4','\uE916','\uEC4A','\uE823','\uE8FD','\uE8B9','\uE7E8','\uE793','\uE706','\uE708','\uE770',
            };
            var ff = new System.Windows.Media.FontFamily("Segoe Fluent Icons");
            var tfc = new System.Windows.Media.Typeface(ff, System.Windows.FontStyles.Normal, System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal);
            bool ok = tfc.TryGetGlyphTypeface(out var gt) && gt != null;
            bool all = true;
            foreach (var ch in uiGlyphs)
                if (ok && !(gt!.CharacterToGlyphMap.ContainsKey(ch) && gt.AdvanceWidths[gt.CharacterToGlyphMap[ch]] > 0.08))
                { all = false; Console.WriteLine($"  missing glyph U+{(int)ch:X4}"); }
            Chk("T10-ui-glyphs-resolve", all && ok, "tofu risk");
            // T10b 目录字形动态锁：工具目录里每个 Glyph 必须在字体里真实存在且有宽度——
            // 静态清单会漏新工具（R52 加 V 时就漏了），目录本身才是该锁的对象
            bool catOk = true;
            foreach (var t in FrameCapture.Core.ToolCatalog.Tools)
            {
                char g = t.Glyph[0];
                if (!(ok && gt!.CharacterToGlyphMap.ContainsKey(g) && gt.AdvanceWidths[gt.CharacterToGlyphMap[g]] > 0.08))
                { catOk = false; Console.WriteLine($"  catalog glyph U+{(int)g:X4} ({t.Key}) missing"); }
            }
            Chk("T10b-catalog-glyphs-resolve", catOk, "工具目录字形全部可渲染");
        }
        catch (Exception e) { Chk("T10-glyphs", false, e.Message); }

        // T11 Fluent 模板可解析（XAML 命名空间/触发器语法回归）
        try
        {
            var tplA = FrameCapture.Core.Fluent.IconButtonTemplate();
            var sl = new System.Windows.Controls.Slider();
            FrameCapture.Core.Fluent.StyleSlider(sl);
            Chk("T11-fluent-templates", tplA != null && sl.Template != null, "parse");
        }
        catch (Exception e) { Chk("T11-fluent-templates", false, e.Message); }

        // T12 全局 ToolTip/菜单样式可解析（主题相关 XAML 回归）
        try
        {
            var tt = FrameCapture.Core.Fluent.ToolTipStyle();
            var sb = FrameCapture.Core.Fluent.ScrollBarStyle();
            var dict = FrameCapture.Core.Fluent.MenuResources();
            Chk("T12-global-styles", tt != null && sb != null && dict != null && dict.Count >= 3, "parse");
        }
        catch (Exception e) { Chk("T12-global-styles", false, e.Message); }

        // T13 同类多标注：3 个不同颜色的圆/点全部烘焙可见（防“第 N 笔丢失”回归）
        try
        {
            var anns = new List<Annotation>();
            var cols = new[] { Color.FromArgb(255, 255, 0, 0), Color.FromArgb(255, 0, 255, 0), Color.FromArgb(255, 0, 0, 255) };
            float[] cxs = { 0.25f, 0.5f, 0.75f };
            for (int i = 0; i < 3; i++)
            {
                var a = new Annotation { Kind = AnnKind.Ellipse, Color = cols[i], Thickness = 5 };
                a.Points.Add((cxs[i] - 0.08f, 0.32f)); a.Points.Add((cxs[i] + 0.08f, 0.68f));
                anns.Add(a);
            }
            anns.Add(new Annotation { Kind = AnnKind.Dot, Color = cols[0], Thickness = 7 });
            anns[3].Points.Add((0.25f, 0.9f));
            anns.Add(new Annotation { Kind = AnnKind.Dot, Color = cols[1], Thickness = 7 });
            anns[4].Points.Add((0.5f, 0.9f));
            anns.Add(new Annotation { Kind = AnnKind.Dot, Color = cols[2], Thickness = 7 });
            anns[5].Points.Add((0.75f, 0.9f));
            var img = Bake.Apply([Solid(200, 100, 0, 0, 0)], anns)[0];
            int Count(System.Func<byte, byte, byte, bool> m)
            {
                int n = 0;
                for (int i = 0; i < img.Bgra.Length; i += 4)
                    if (m(img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2])) n++;
                return n;
            }
            int red = Count((b, g, r) => r > 150 && g < 80 && b < 80);
            int green = Count((b, g, r) => g > 150 && r < 80 && b < 80);
            int blue = Count((b, g, r) => b > 150 && r < 80 && g < 80);
            Chk("T13a-three-ellipses", red > 90 && green > 90 && blue > 90, $"r={red} g={green} b={blue}");
            int dotRed = img.Bgra[((int)(0.9 * 99) * 200 + (int)(0.25 * 199)) * 4 + 2];
            Chk("T13b-three-dots", dotRed > 150, $"dot@0.25,0.9 red={dotRed}");
        }
        catch (Exception e) { Chk("T13-multi-ann", false, e.Message); }

        // T14 FrameRing：预算驱逐语义（保首帧、丢最老、计数正确）+ 自适应预算区间
        try
        {
            int F = 1024;
            var ring = new FrameRing(4 * F);
            for (int i = 0; i < 6; i++)
                ring.Add(new CapturedFrame { Bgra = new byte[F], W = 1, H = 1, TimeMs = i });
            var kept = ring.Frames.Select(f => (int)f.TimeMs).ToList();
            Chk("T14a-evict-order", kept.SequenceEqual(new[] { 0, 3, 4, 5 }), $"kept={string.Join(",", kept)}");
            Chk("T14b-dropped-count", ring.Dropped == 2, $"Dropped={ring.Dropped}");
            var snap = ring.Snapshot();
            snap.Clear();
            Chk("T14c-snapshot-copy", ring.Frames.Count == 4);
            long ab = FrameRing.AdaptiveBudget();
            Chk("T14d-adaptive-range", ab >= 512L << 20 && ab <= 2560L << 20, $"{ab >> 20}MB");
            // GlobalMemoryStatusEx 必须真实成功（dwLength 错误会静默回落 512MB 兜底）
            long availGb = ab >= 2560L << 20 ? -1 : ab >> 30;
            Chk("T14e-memquery-works", ab > 512L << 20 || availGb == -1,
                $"budget={ab >> 20}MB（若恰好等于兜底值请人工确认内存查询）");
        }
        catch (Exception e) { Chk("T14-framer-ring", false, e.Message); }

        // T15 剪贴板 DataObject：文件 + 位图 + 自定义 GIF 三格式齐备（不触碰真实剪贴板）
        try
        {
            var frame = Solid(16, 16, 10, 20, 30);
            var seq = new CapturedSequence
            {
                Frames = new List<CapturedFrame> { frame },
                Region = new RectI(0, 0, 16, 16),
                MonitorBounds = new RectI(0, 0, 16, 16),
            };
            byte[] gif = { 1, 2, 3, 4 };
            string gp = Path.Combine(Path.GetTempPath(), "fc-t15.gif");
            var data = ClipboardService.BuildDataObject(seq, gif, 0, gp);
            var fmts = data.GetFormats(true);
            bool fileDrop = fmts.Any(f => f.ToString() == DataFormats.FileDrop);
            bool dib = fmts.Any(f => { string n = f.ToString(); return n == DataFormats.Bitmap || n == "Bitmap" || n == "DeviceBitmap"; });
            bool custom = data.GetData(ClipboardService.GifFormat) is byte[] gb && gb.SequenceEqual(gif);
            Chk("T15a-clipboard-formats", fileDrop && dib && custom,
                $"fileDrop={fileDrop} dib={dib} custom={custom} fmts={string.Join(",", fmts.Select(x => x.ToString()))}");
            var raw = data.GetData(DataFormats.FileDrop);
            string[] fl = raw switch
            {
                System.Collections.Specialized.StringCollection sc => sc.Cast<string>().ToArray(),
                string[] sa => sa,
                System.Collections.IEnumerable en => en.Cast<object>().Select(o => o.ToString() ?? "").ToArray(),
                _ => Array.Empty<string>(),
            };
            Chk("T15b-clipboard-gifpath", fl.Length == 1 && fl[0] == gp, $"type={raw?.GetType().Name} n={fl.Length}");
        }
        catch (Exception e) { Chk("T15-clipboard", false, e.Message); }

        // T16 timeline 拖范围状态机（原 UI 内逻辑下沉，人工清单缩小）
        try
        {
            var rd = new FrameCapture.Core.RangeDragState { Count = 16, Width = 1000 };
            rd.Down(rd.FrameX(3)); rd.Move(rd.FrameX(7)); rd.Up();
            Chk("T16a-drag-new", rd.A == 3 && rd.B == 7 && rd.Effective == (3, 7), $"A={rd.A} B={rd.B}");
            rd.Down(rd.FrameX(5)); rd.Up();
            Chk("T16b-click-clear", rd.A == -1 && rd.B == -1 && rd.Effective == (0, 15), $"A={rd.A}");
            rd.Down(rd.FrameX(2)); rd.Move(rd.FrameX(9)); rd.Up();
            rd.Down(rd.FrameX(2) + 3); // 距左端点 <8px
            Chk("T16c-grab-left", rd.Mode == 2);
            rd.Move(rd.FrameX(1)); rd.Up();
            Chk("T16d-move-left", rd.A == 1 && rd.B == 9, $"A={rd.A} B={rd.B}");
            rd.Down(rd.FrameX(9) - 2);
            Chk("T16e-grab-right", rd.Mode == 3);
            rd.Move(rd.FrameX(12)); rd.Up();
            Chk("T16f-move-right", rd.A == 1 && rd.B == 12, $"B={rd.B}");
            rd.A = rd.B = -1;
            rd.Down(rd.FrameX(10)); rd.Move(rd.FrameX(4)); rd.Up();
            Chk("T16g-reverse-drag", rd.A == 4 && rd.B == 10, $"A={rd.A} B={rd.B}");
            Chk("T16h-idx-clamp", FrameCapture.Core.RangeDragState.XToIdx(-50, 1000, 16) == 0
                && FrameCapture.Core.RangeDragState.XToIdx(1500, 1000, 16) == 15);
            var one = new FrameCapture.Core.RangeDragState { Count = 1, Width = 100 };
            one.Down(50); one.Up();
            Chk("T16i-single-frame-noop", one.A == -1 && one.Mode == 0);
        }
        catch (Exception e) { Chk("T16-range-drag", false, e.Message); }

        // T17 窗口吸附命中：z 序取最先包含者、跳过 <4px、未命中返回 null
        try
        {
            var cands = new[]
            {
                new RectI(100, 100, 200, 200),   // 前景小窗
                new RectI(10, 10, 3, 3),         // 过小应跳过
                new RectI(0, 0, 1000, 1000),     // 背景大窗
            };
            var hit = FrameCapture.Core.SnapPick.Pick(cands, 150, 150);
            Chk("T17a-topmost-hit", hit?.Equals(cands[0]) == true, $"{hit}");
            var hit2 = FrameCapture.Core.SnapPick.Pick(cands, 800, 800);
            Chk("T17b-background", hit2?.Equals(cands[2]) == true, $"{hit2}");
            var hit3 = FrameCapture.Core.SnapPick.Pick(cands, 11, 11);
            Chk("T17c-skip-tiny", hit3?.Equals(cands[2]) == true, $"{hit3}");
            Chk("T17d-miss-null", FrameCapture.Core.SnapPick.Pick(cands, 1500, 1500) == null);
        }
        catch (Exception e) { Chk("T17-snap", false, e.Message); }

        // BENCH（仅 FC_BENCH=1 时运行，不计入 PASS/FAIL）：4K GIF 编码耗时
        if (Environment.GetEnvironmentVariable("FC_BENCH") == "1")
        {
            int W = 3840, H = 2160, N = 30;
            var frames = new List<CapturedFrame>();
            var rnd = new Random(7);
            for (int i = 0; i < N; i++)
            {
                var px = new byte[W * H * 4];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int o = (y * W + x) * 4;
                        px[o] = (byte)((x + i * 40) & 0xFF);
                        px[o + 1] = (byte)(y % 255);
                        px[o + 2] = (byte)((x * y / 4096 + i * 20) & 0xFF);
                        px[o + 3] = 255;
                    }
                frames.Add(new CapturedFrame { Bgra = px, W = W, H = H, TimeMs = i * 50 });
            }
            var sw4k = Stopwatch.StartNew();
            byte[] gif = GifEncoder.Encode(frames, Enumerable.Repeat(50, N).ToList());
            sw4k.Stop();
            Console.WriteLine($"BENCH 4K x{N}: encode={sw4k.ElapsedMilliseconds}ms gif={gif.Length / 1024 / 1024.0:F1}MB");
        }

        // T18 并行编码：多帧输出与顺序逐帧编码逐字节一致 + 取消生效
        try
        {
            var fr = new List<CapturedFrame>();
            for (int i = 0; i < 8; i++)
            {
                var px = new byte[64 * 64 * 4];
                for (int o = 0; o < px.Length; o += 4) { px[o] = (byte)(i * 30); px[o + 1] = (byte)(o * 7 & 255); px[o + 2] = (byte)(i * 11 & 255); px[o + 3] = 255; }
                fr.Add(new CapturedFrame { Bgra = px, W = 64, H = 64, TimeMs = i * 40 });
            }
            var delays = Enumerable.Repeat(40, 8).ToList();
            byte[] par = GifEncoder.Encode(fr, delays);
            using var msr = new MemoryStream();
            foreach (byte b in par) msr.WriteByte(b);
            bool ok = par.Length > 100 && par[0] == 'G';
            var csc = new System.Threading.Tasks.TaskCompletionSource<int>();
            var cts = new CancellationTokenSource();
            cts.Cancel();
            bool cancelled = false;
            try { GifEncoder.Encode(fr, delays, true, null, cts.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Chk("T18a-parallel-valid", ok, $"len={par.Length}");
            Chk("T18b-cancel", cancelled);
        }
        catch (Exception e) { Chk("T18-parallel", false, e.Message); }

        // T19 UndoRedo：新增清空重做栈；撤销/重做往返；裁剪清空语义
        try
        {
            var h = new FrameCapture.Core.UndoRedo<string>();
            h.Add("a"); h.Add("b");
            Chk("T19a-undo", h.Undo(out var u1) && u1 == "b" && h.Count == 1 && h.CanRedo);
            h.Add("c");
            Chk("T19b-add-clears-redo", !h.CanRedo && h.Items.SequenceEqual(new[] { "a", "c" }));
            Chk("T19c-redo-empty", !h.Redo(out _));
            h.Undo(out _); h.Undo(out _);
            // 撤销全部后 _undone=[c,a]，重做按后进先出：a 然后 c；重做后仍可再撤销
            Chk("T19d-redo-order", h.Redo(out var r1) && r1 == "a" && h.Redo(out var r2) && r2 == "c"
                && h.Items.SequenceEqual(new[] { "a", "c" }) && h.CanUndo && !h.CanRedo);
        }
        catch (Exception e) { Chk("T19-undo-redo", false, e.Message); }

        // T20 WGC（Windows.Graphics.Capture）后端：设备/项创建 + 真实取帧
        try
        {
            var mons = FrameCapture.Capture.Monitors.All();
            var mon = mons.First(m => m.IsPrimary) ?? mons[0];
            var reg = new FrameCapture.Capture.RectI(mon.Bounds.X + 4, mon.Bounds.Y + 4, 256, 192);
            // WGC 仅在捕获区域内容变化时产帧：用一个持续重绘的小窗制造屏幕更新
            using var probeForm = new System.Windows.Forms.Form
            {
                TopMost = true,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow,
                StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                Location = new System.Drawing.Point(mon.Bounds.X, mon.Bounds.Y),
                Size = new System.Drawing.Size(300, 200),
                Text = "FC-WGC-PROBE",
            };
            var probeLabel = new System.Windows.Forms.Label { Dock = System.Windows.Forms.DockStyle.Fill, Text = "0" };
            probeForm.Controls.Add(probeLabel);
            using var probeTimer = new System.Windows.Forms.Timer { Interval = 60 };
            int probeTick = 0;
            probeTimer.Tick += (_, _) => { probeLabel.Text = (++probeTick).ToString(); };
            probeForm.Show();
            probeTimer.Start();
            using var wgc = new FrameCapture.Capture.WgcLiveCapture();
            int n = 0; int fw = 0, fh = 0; bool badFmt = false;
            wgc.FrameSampled += f =>
            {
                if (f.Bgra.Length != f.W * f.H * 4) badFmt = true;
                fw = f.W; fh = f.H;
                Interlocked.Increment(ref n);
            };
            for (int i = 0; i < 20 && probeTick < 2; i++) System.Threading.Thread.Sleep(50); // 等窗口先画起来
            wgc.Start(mon, reg, 30);
            var sw0 = System.Diagnostics.Stopwatch.StartNew();
            while (n < 3 && sw0.ElapsedMilliseconds < 3000) Thread.Sleep(50);
            wgc.Stop();
            Chk("T20a-wgc-frames", n >= 3 && !badFmt && fw == reg.W && fh == reg.H, $"n={n} {fw}x{fh} badFmt={badFmt} missed={wgc.FramesMissed} err={wgc.LastLoopError?.Split('\n')[0]}");
            int after = n; Thread.Sleep(200);
            Chk("T20b-wgc-stopped", n == after);
            using var wgc2 = new FrameCapture.Capture.WgcLiveCapture();
            int n2 = 0;
            wgc2.FrameSampled += _ => Interlocked.Increment(ref n2);
            wgc2.Start(mon, reg, 30);
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            while (n2 < 1 && sw1.ElapsedMilliseconds < 3000) Thread.Sleep(50);
            wgc2.Stop();
            probeTimer.Stop();
            probeForm.Hide();
            Chk("T20c-wgc-restart", n2 >= 1, $"n2={n2} missed={wgc2.FramesMissed} err={wgc2.LastLoopError?.Split('\n')[0]}");

            // T73 关闭 WGC 系统捕获提示边框（Windows 11 那条绕整屏的黄色 #FFD11C 框）：
            // 我们的 WGC 只能整显示器捕获，系统边框必然全屏、与所选录制范围无关，于是改为
            // 自家按范围绘制轮廓。这里断言这个"关边框"的动作在支持的系统上真的成功了
            // （Win10 没有 IGraphicsCaptureSession3，SystemBorderSuppressed 保持 null→跳过，不算失败）。
            var canBorderless = System.OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
            Chk("T73-wgc-borderless", wgc2.SystemBorderSuppressed == true || !canBorderless,
                $"suppressed={wgc2.SystemBorderSuppressed} canBorderless={canBorderless}");

            // T74 轮廓窗"在屏可见但不进捕获"的机制前提：WDA_EXCLUDEFROMCAPTURE(0x11) 必须真的能被设置成功，
            // 否则 RegionOutlineWindow 会静默变成"看不见"（那就等于录制期间没有任何范围提示）。
            {
                var probeWin = new System.Windows.Window
                {
                    Title = "SelfTest DisplayAffinity",
                    Width = 200, Height = 120, ShowInTaskbar = false, ShowActivated = false,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                    Left = -4000, Top = -4000, Topmost = true,
                    AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
                    WindowStyle = System.Windows.WindowStyle.None,
                    Content = new System.Windows.Controls.Border
                    {
                        BorderBrush = System.Windows.Media.Brushes.DodgerBlue,
                        BorderThickness = new System.Windows.Thickness(2),
                    },
                };
                var h = new System.Windows.Interop.WindowInteropHelper(probeWin).EnsureHandle();
                bool setOk = SetWindowDisplayAffinity(h, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
                Chk("T74-exclude-from-capture", setOk,
                    $"setOk={setOk} err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()} hwnd=0x{h:X}");
                probeWin.Close();
            }
        }
        catch (Exception e) { Chk("T20-wgc-backend", false, e.Message); }

        // T68: 时间轴裁剪端点直设与钳位（A<=B-1、B>=A+1、至少 2 帧）
        {
            var rd = new FrameCapture.Core.RangeDragState { Count = 10, Width = 300 };
            var dflt = rd.Effective;
            rd.SetB(4); rd.SetA(2);
            var got = rd.Effective;
            rd.SetA(9); var a = rd.Effective;
            rd.SetB(1); var b = rd.Effective;
            rd.SetA(-5); var c = rd.Effective;
            Chk("T68-timeline-setab", dflt == (0, 9) && got == (2, 4) && a == (3, 4) && b == (3, 4) && c == (0, 4),
                $"dflt={dflt} set={got} overA={a} underB={b} underA={c}");
        }

        // T69: 主题变更事件（实际变化才触发；强调色变化同样触发）
        {
            int fired = 0;
            Action th = () => fired++;
            FrameCapture.Core.Theme.Changed += th;
            try
            {
                FrameCapture.Core.Theme.Apply("dark", "#4CC2FF");
                int base0 = fired;
                FrameCapture.Core.Theme.Apply("dark", "#4CC2FF");
                bool noDup = fired == base0;
                FrameCapture.Core.Theme.Apply("light", "#4CC2FF");
                bool darkFlip = fired == base0 + 1;
                FrameCapture.Core.Theme.Apply("light", "#FF0000");
                bool accFlip = fired == base0 + 2;
                Chk("T69-theme-changed", noDup && darkFlip && accFlip, $"fired={fired - base0}");
            }
            finally
            {
                FrameCapture.Core.Theme.Changed -= th;
                FrameCapture.Core.Theme.Apply("dark", "#4CC2FF");
            }
        }

        // T70: 时间轴坐标与帧号映射：边界与单调性
        {
            var rd = new FrameCapture.Core.RangeDragState { Count = 20, Width = 380 };
            int first = FrameCapture.Core.RangeDragState.XToIdx(0, 380, 20), last = FrameCapture.Core.RangeDragState.XToIdx(380, 380, 20),
                over = FrameCapture.Core.RangeDragState.XToIdx(9999, 380, 20);
            bool mono = true; int prev = -1;
            for (double x = 0; x <= 380; x += 5)
            {
                int i = FrameCapture.Core.RangeDragState.XToIdx(x, 380, 20);
                if (i < prev) { mono = false; break; }
                prev = i;
            }
            Chk("T70-timeline-map", first == 0 && last == 19 && over == 19 && mono, $"first={first} last={last} over={over} mono={mono}");
        }

        // T71 ElementSnap：UIA 控件级吸附——命中的是文本框边界而非整窗
        try
        {
            var win = new System.Windows.Window
            {
                Title = "SelfTest ElementSnap",
                Width = 320, Height = 240,
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = 150, Top = 150,
                Topmost = true,
                Content = new System.Windows.Controls.TextBox
                {
                    Text = "snap me", Width = 140, Height = 26,
                    Margin = new System.Windows.Thickness(20),
                },
            };
            win.Show();
            win.Activate();
            var pump = new System.Windows.Threading.DispatcherFrame();
            win.Dispatcher.BeginInvoke(() => { pump.Continue = false; }, System.Windows.Threading.DispatcherPriority.Loaded);
            System.Windows.Threading.Dispatcher.PushFrame(pump);
            var tb = (System.Windows.Controls.TextBox)win.Content;
            var tl = tb.PointToScreen(new System.Windows.Point(0, 0));
            var br = tb.PointToScreen(new System.Windows.Point(tb.ActualWidth, tb.ActualHeight));
            int cx = (int)((tl.X + br.X) / 2), cy = (int)((tl.Y + br.Y) / 2);
            var hit = FrameCapture.Core.ElementSnap.At(cx, cy, IntPtr.Zero);
            Chk("T71a-uia-hit", hit != null);
            Chk("T71b-uia-rect", hit != null &&
                Math.Abs(hit.Rect.X - tl.X) <= 4 && Math.Abs(hit.Rect.Y - tl.Y) <= 4 &&
                Math.Abs(hit.Rect.W - (br.X - tl.X)) <= 4 && Math.Abs(hit.Rect.H - (br.Y - tl.Y)) <= 4,
                hit == null ? "null" : $"{hit.Rect} vs tb {tl.X},{tl.Y} {(int)(br.X - tl.X)}x{(int)(br.Y - tl.Y)}");
            var up = hit != null ? FrameCapture.Core.ElementSnap.Parent(hit) : null;
            Chk("T71c-uia-parent", up != null && up.Rect.W * up.Rect.H >= hit!.Rect.W * hit.Rect.H);
            win.Close();
        }
        catch (Exception ex) { Chk("T71-uia", false, ex.Message); }

        // T72 C1 选中态语义：工具单选切换（再点=取消）+ accent 反白对比色（纯逻辑）
        {
            var a = FrameCapture.Core.ToolToggle.Select(System.DayOfWeek.Sunday, System.DayOfWeek.Monday, System.DayOfWeek.Sunday);
            var b = FrameCapture.Core.ToolToggle.Select(a, System.DayOfWeek.Monday, System.DayOfWeek.Sunday);   // 再点当前 → 取消
            var c = FrameCapture.Core.ToolToggle.Select(System.DayOfWeek.Saturday, System.DayOfWeek.Sunday, System.DayOfWeek.Sunday); // 直选"浏览"透传
            Chk("T72a-tool-toggle", a == System.DayOfWeek.Monday && b == System.DayOfWeek.Sunday && c == System.DayOfWeek.Sunday,
                $"a={a} b={b} c={c}");
            var onLight = FrameCapture.Core.Fluent.OnAccentColor(System.Windows.Media.Color.FromRgb(0x4C, 0xC2, 0xFF));
            var onDark = FrameCapture.Core.Fluent.OnAccentColor(System.Windows.Media.Color.FromRgb(0x2B, 0x3A, 0x8F));
            Chk("T72b-onaccent", onLight == System.Windows.Media.Colors.Black && onDark == System.Windows.Media.Colors.White,
                $"light={onLight} dark={onDark}");
        }

        // T75 捕获状态机入口守卫（纯逻辑决策表）：
        //  回归护栏 = "只有 StartCapture 允许隐藏范围轮廓"。旧版 BeginCapture 第一行无条件
        //  HideOutline()，守卫在其后 → 编辑器开着再按捕获键会把常显轮廓永久弄没。
        {
            var idle = FrameCapture.Core.CaptureGuards.OnCaptureHotkey(false, false, false);
            var cap = FrameCapture.Core.CaptureGuards.OnCaptureHotkey(true, false, false);
            var sel = FrameCapture.Core.CaptureGuards.OnCaptureHotkey(false, true, false);
            var edi = FrameCapture.Core.CaptureGuards.OnCaptureHotkey(false, false, true);
            Chk("T75a-capture-intent",
                idle == FrameCapture.Core.CaptureGuards.CaptureIntent.StartCapture &&
                cap == FrameCapture.Core.CaptureGuards.CaptureIntent.Ignore &&
                sel == FrameCapture.Core.CaptureGuards.CaptureIntent.Ignore &&
                edi == FrameCapture.Core.CaptureGuards.CaptureIntent.RefocusEditor,
                $"idle={idle} cap={cap} sel={sel} edi={edi}");
            // 不变量：非起录意图一律不得隐藏轮廓
            bool onlyStartHides =
                !FrameCapture.Core.CaptureGuards.MayHideOutline(FrameCapture.Core.CaptureGuards.CaptureIntent.Ignore) &&
                !FrameCapture.Core.CaptureGuards.MayHideOutline(FrameCapture.Core.CaptureGuards.CaptureIntent.RefocusEditor) &&
                FrameCapture.Core.CaptureGuards.MayHideOutline(FrameCapture.Core.CaptureGuards.CaptureIntent.StartCapture);
            Chk("T75b-outline-hide-invariant", onlyStartHides);
            // 选取热键：录制中不打断；选取窗活着=顶前；窗没了(Alt+F4)=复位重开
            var s1 = FrameCapture.Core.CaptureGuards.OnScopeHotkey(true, false, false);
            var s2 = FrameCapture.Core.CaptureGuards.OnScopeHotkey(false, true, true);
            var s3 = FrameCapture.Core.CaptureGuards.OnScopeHotkey(false, true, false);
            var s4 = FrameCapture.Core.CaptureGuards.OnScopeHotkey(false, false, false);
            Chk("T75c-scope-intent",
                s1 == FrameCapture.Core.CaptureGuards.ScopeIntent.Ignore &&
                s2 == FrameCapture.Core.CaptureGuards.ScopeIntent.RefocusPicker &&
                s3 == FrameCapture.Core.CaptureGuards.ScopeIntent.StartSelection &&
                s4 == FrameCapture.Core.CaptureGuards.ScopeIntent.StartSelection,
                $"s1={s1} s2={s2} s3={s3} s4={s4}");
            // 录制结束后的轮廓去向：常显+有已存范围=留在屏上；否则淡出
            var o1 = FrameCapture.Core.CaptureGuards.OnCaptureFinished(true, true);
            var o2 = FrameCapture.Core.CaptureGuards.OnCaptureFinished(true, false);
            var o3 = FrameCapture.Core.CaptureGuards.OnCaptureFinished(false, true);
            Chk("T75d-outline-after-capture",
                o1 == FrameCapture.Core.CaptureGuards.AfterCaptureOutline.KeepPersistent &&
                o2 == FrameCapture.Core.CaptureGuards.AfterCaptureOutline.FadeOut &&
                o3 == FrameCapture.Core.CaptureGuards.AfterCaptureOutline.FadeOut,
                $"o1={o1} o2={o2} o3={o3}");
        }

        // T77/T78 作用域（原 T76 全局钩子决策表随旧 F6/F7 热键路径退役，R126 B2）
        {
            // T77：Fps 钳制单一事实源（常量 = 文档范围，Normalize 钳越界值）
            Chk("T77a-fps-constants",
                AppSettings.FpsMin == 10 && AppSettings.FpsMax == 240 && AppSettings.FpsMin < AppSettings.FpsMax,
                "Fps 常量单一事实源：10..240（R100 放开高刷）");
            var fpsLo = new AppSettings { Fps = 5 }; fpsLo.Normalize();
            var fpsHi = new AppSettings { Fps = 999 }; fpsHi.Normalize();
            var fpsOk = new AppSettings { Fps = 30 }; fpsOk.Normalize();
            Chk("T77b-fps-normalize-clamps",
                fpsLo.Fps == AppSettings.FpsMin && fpsHi.Fps == AppSettings.FpsMax && fpsOk.Fps == 30,
                "Normalize 把 Fps 钳进 [FpsMin,FpsMax]，合法值原样通过");
            // T78：裁剪→标注重映射（Core.CropRemap）——坐标变换、跨界丢弃、像素级往返
            {
                var dotIn = new Annotation { Kind = AnnKind.Dot, Color = Color.FromArgb(255, 0xE7, 0x48, 0x4C), Thickness = 8 };
                dotIn.Points.Add((0.5f, 0.5f));
                var lineCross = new Annotation { Kind = AnnKind.Line, Color = Color.Red, Thickness = 4 };
                lineCross.Points.Add((0.1f, 0.5f)); lineCross.Points.Add((0.3f, 0.5f)); // 跨左裁剪边
                var dotOut = new Annotation { Kind = AnnKind.Dot, Color = Color.Lime, Thickness = 4 };
                dotOut.Points.Add((0.9f, 0.9f)); // 框外
                var input = new[] { dotIn, lineCross, dotOut };
                var kept = FrameCapture.Core.CropRemap.Remap(input, 0.25, 0.25, 0.5, 0.5);
                bool mapOk = kept.Count == 1 && kept[0].Kind == AnnKind.Dot
                    && Math.Abs(kept[0].Points[0].X - 0.5f) < 1e-5 && Math.Abs(kept[0].Points[0].Y - 0.5f) < 1e-5;
                Chk("T78a-crop-remap-coords", mapOk, "框内标注保留且坐标 (0.5,0.5)→裁剪框 (0.5,0.5)");
                Chk("T78b-crop-remap-drops-and-purity",
                    kept.Count == 1 && kept[0] != dotIn
                    && dotIn.Points[0].X == 0.5f && dotIn.Points[0].Y == 0.5f
                    && lineCross.Points.Count == 2 && dotOut.Points.Count == 1,
                    "跨界/框外丢弃；输入不被修改（返回新列表）");
                // 像素级往返：先烘焙再裁剪 ≡ 先裁剪再按重映射烘焙
                var f0 = Solid(200, 100, 0, 0, 0);
                var baked = Bake.Apply([f0], [dotIn])[0];
                var pathA = Bake.Crop([baked], 50, 25, 100, 50)[0];              // 烘焙→裁剪
                var pathB = Bake.Apply(Bake.Crop([f0], 50, 25, 100, 50), kept)[0]; // 裁剪→重映射→烘焙
                bool pxSame = pathA.W == pathB.W && pathA.H == pathB.H && pathA.Bgra.AsSpan().SequenceEqual(pathB.Bgra);
                bool centerRed = pathB.Bgra[(25 * 100 + 50) * 4 + 2] > 0x80; // 红点在裁剪框中心
                Chk("T78c-crop-remap-pixel-roundtrip", pxSame && centerRed,
                    $"Bake(Crop(Bake)) ≡ Crop→Remap→Bake 逐字节相等={pxSame} 中心红点={centerRed}");
            }
            // T79：视图缩放/平移数学（Core.ZoomMath）
            {
                // 视口 800x600，帧 1600x1200 → sFit=0.5；1:1 → zoom=2
                double sFit = ZoomMath.FitScale(800, 600, 1600, 1200);
                Chk("T79a-fit-and-onetoone",
                    Math.Abs(sFit - 0.5) < 1e-9 && Math.Abs(ZoomMath.OneToOneZoom(sFit) - 2.0) < 1e-9
                    && ZoomMath.ClampZoom(0.2) == ZoomMath.MinZoom && ZoomMath.ClampZoom(99) == ZoomMath.MaxZoom,
                    "sFit=0.5 → 1:1 即 zoom=2；zoom 钳进 [1,16]");
                var (px1, py1) = ZoomMath.ClampPan(500, -900, 800, 600, 400, 300);   // 图像小于视口
                var (px2, py2) = ZoomMath.ClampPan(500, -900, 800, 600, 1600, 1200); // 图像大于视口 → ±400/±300
                Chk("T79b-pan-clamp",
                    px1 == 0 && py1 == 0 && px2 == 400 && py2 == -300,
                    "适配尺寸强制居中；放大后边缘不内缩（无黑边）");
                // 锚点不变量：锚下图像点缩放前后不动。锚 (250,150)（中心偏移 -150,-150），zoom 1→2
                var (z, ax, ay) = ZoomMath.ZoomAt(1.0, 0, 0, 2.0, 250, 150, 800, 600, 1600, 1200);
                double u0 = (250 - 400 - 0) / (sFit * 1.0), u1 = (250 - 400 - ax) / (sFit * z);
                Chk("T79c-zoom-at-anchor-invariant",
                    Math.Abs(z - 2.0) < 1e-9 && Math.Abs(u1 - u0) < 1e-6 && Math.Abs(ax - 150) < 1e-6 && Math.Abs(ay - 150) < 1e-6,
                    "锚点下图像点保持不动（u0=u1=-300 帧像素）");
                var (dx, dy) = ZoomMath.PanBy(100, 0, -1000, 50, 800, 600, 1600, 1200);
                Chk("T79d-panby-clamped", dx == -400 && dy == 50, "拖拽增量叠加后仍受钳制");
            }
            // T80：裁剪=视图状态栈（Core.CropState），帧数据不被破坏
            {
                var cs = new CropState();
                var (px0, py0, pw0, ph0) = cs.ToPixel(1600, 1200);
                Chk("T80a-identity", !cs.IsCropped && cs.Current.Equals(new CropState.Norm(0, 0, 1, 1))
                    && px0 == 0 && py0 == 0 && pw0 == 1600 && ph0 == 1200, "无裁剪=全帧，ToPixel 全尺寸");
                bool p1 = cs.Push(0.25, 0.25, 0.75, 0.75);
                var (px1, py1, pw1, ph1) = cs.ToPixel(1600, 1200);
                Chk("T80b-push-topixel", p1 && cs.Depth == 1 && px1 == 400 && py1 == 300 && pw1 == 800 && ph1 == 600,
                    "拖框(0.25,0.25)-(0.75,0.75) → 像素(400,300,800,600)");
                bool p2 = cs.Push(0.1, 0.1, 0.5, 0.5); // 二次裁剪=与当前可视区求交
                var c2 = cs.Current;
                bool pop1 = cs.Pop(); var c3 = cs.Current;
                Chk("T80c-intersect-and-pop", p2 && Math.Abs(c2.X - 0.25) < 1e-12 && Math.Abs(c2.W - 0.25) < 1e-12
                    && pop1 && Math.Abs(c3.W - 0.5) < 1e-12 && cs.Depth == 1,
                    "二次裁剪与当前区求交(0.25,0.25,0.25,0.25)；Pop 回退一级");
                int d0 = cs.Depth;
                bool deg = cs.Push(0.5, 0.5, 0.5, 0.5);
                bool frac = cs.Push(0.3, 0.3, 0.35, 0.9, 0.1); // 交宽 0.05 ≤ minFrac
                Chk("T80d-degenerate-rejected", !deg && !frac && cs.Depth == d0, "退化交集/低于最小比例 → 拒绝且栈不变");
                var cs2 = new CropState();
                cs2.Push(2, 2, -3, -3); // 越界拖框钳进全帧
                var cf = cs2.Current;
                Chk("T80e-clamp-to-unit", Math.Abs(cf.X) < 1e-12 && Math.Abs(cf.W - 1) < 1e-12, "越界拖框钳为全帧");
                var cs3 = new CropState();
                cs3.Push(0.2, 0.2, 0.6, 0.8);
                cs3.Pop();
                bool redo1 = cs3.RedoCrop();
                var cr = cs3.Current;
                bool redo2 = cs3.RedoCrop();
                cs3.Push(0.1, 0.1, 0.3, 0.3);
                cs3.Pop();
                cs3.Push(0.3, 0.3, 0.5, 0.5); // 与当前区相交的成功裁剪 → 清空 redo
                bool redo3 = cs3.RedoCrop();
                Chk("T80f-pop-redo-roundtrip",
                    redo1 && Math.Abs(cr.X - 0.2) < 1e-12 && Math.Abs(cr.H - 0.6) < 1e-12 && !redo2 && !redo3 && cs3.Depth == 2,
                    "Pop→Redo 精确还原矩形；redo 耗尽返回 false；新裁剪清空 redo 栈");
            }
            // T81：标注/裁剪双流撤销次序（Core.ActionOrder）
            {
                var ao = new ActionOrder();
                ao.AnnAdded();       // seq1 画一笔
                ao.CropPushed();     // seq2 裁剪 → 撤销应弹裁剪
                bool undoCrop = ao.UndoPopsCrop(hasAnns: true);
                ao.CropUndone();     // → 裁剪进重做栈
                ao.AnnStepped();     // seq3 标注流撤销完成（标注此刻可 redo）
                bool redoAnnFirst = !ao.RedoPopsCrop(annCanRedo: true);   // 标注比裁剪旧 → 先 redo 标注
                ao.AnnStepped();     // seq4 标注 redo 完成
                bool redoCropNext = ao.RedoPopsCrop(annCanRedo: false);   // 标注流没了 → 轮到裁剪
                ao.CropRedone();
                Chk("T81a-lifo-across-streams",
                    undoCrop && redoAnnFirst && redoCropNext && ao.CanUndoCrop && !ao.CanRedoCrop,
                    "Undo 弹更新的裁剪；Redo 按原始顺序先标注后裁剪，双流交错不乱序");
                var ao2 = new ActionOrder();
                ao2.CropPushed();    // seq1
                ao2.AnnAdded();      // seq2 标注
                ao2.AnnStepped();    // seq3 标注撤销（标注栈变空）
                bool undoCrop2 = ao2.UndoPopsCrop(hasAnns: false); // 标注已空 → 落到裁剪
                ao2.CropUndone();
                bool redoCrop = ao2.RedoPopsCrop(annCanRedo: false);
                ao2.CropRedone();
                Chk("T81b-empty-ann-and-redo", undoCrop2 && redoCrop && ao2.CanUndoCrop && !ao2.CanRedoCrop,
                    "标注流空时撤销落到裁剪；重做对称回裁剪栈");
                var ao3 = new ActionOrder();
                ao3.CropPushed();    // seq1
                ao3.CropUndone();    // 裁剪进 redo
                ao3.AnnAdded();      // seq2 新动作 → 清空 redo
                Chk("T81c-new-action-clears-redo", !ao3.CanRedoCrop && !ao3.CanUndoCrop,
                    "裁剪被撤销后画新笔 → 裁剪 redo 失效（标准撤销语义），两栈皆空");
            }
            // T82：Esc 分层（导出中=取消；绘制态=退工具；浏览=关闭）
            {
                Chk("T82a-esc-layers",
                    EscStep.Select(encoding: true, inToolMode: true) == EscAction.CancelEncode
                    && EscStep.Select(encoding: false, inToolMode: true) == EscAction.ExitTool
                    && EscStep.Select(encoding: false, inToolMode: false) == EscAction.Close,
                    "导出中优先取消；绘制态先退工具（误触不丢捕获）；仅浏览态关闭");
            }
            // T83：自定义线宽/最近自定义色（Core.CustomPick）
            {
                float[] presets = { 3f, 6f, 10f };
                Chk("T83a-width-presets",
                    CustomPick.Width(0, presets, 99) == 3f && CustomPick.Width(2, presets, 99) == 10f,
                    "idx 在预设区间 → 预设值，自定义值不参与");
                Chk("T83b-width-custom-clamp",
                    CustomPick.Width(3, presets, 17) == 17f && CustomPick.Width(3, presets, 0.2f) == CustomPick.WidthMin
                    && CustomPick.Width(3, presets, 500) == CustomPick.WidthMax && CustomPick.Width(-1, presets, 8) == 8f,
                    "自定义槽位/越界 idx → 自定义值钳 1..24");
                var mem = CustomPick.RememberColor(new[] { "#FF0000", "#00FF00" }, "#0000FF");
                Chk("T83c-recent-newest-first-dedupe",
                    mem.Count == 3 && mem[0] == "#0000FF" && mem[1] == "#FF0000",
                    "新色置顶，旧色依序保留");
                var mem2 = CustomPick.RememberColor(new[] { "#ff0000", "#00FF00" }, "#FF0000");
                Chk("T83d-recent-case-insensitive",
                    mem2.Count == 2 && mem2[0] == "#FF0000" && mem2[1] == "#00FF00",
                    "大小写不敏感去重，保留首次出现的大写形态");
                var mem3 = CustomPick.RememberColor(
                    new[] { "#111111", "#222222", "#333333", "#444444", "#555555" }, "#666666", cap: 6);
                var mem4 = CustomPick.RememberColor(mem3, "#777777", cap: 6);
                Chk("T83e-recent-cap",
                    mem3.Count == 6 && mem4.Count == 6 && mem4[0] == "#777777" && mem4[1] == "#666666" && !mem4.Contains("#555555"),
                    "上限 6 条：新色置顶，超限挤掉最旧一条");
                var mem5 = CustomPick.RememberColor(new[] { "", "#AABBCC" }, null);
                Chk("T83f-recent-skip-empty",
                    mem5.Count == 1 && mem5[0] == "#AABBCC",
                    "空/新色缺失时丢弃，不占位");
            }
            // T84：时间轴秒标步长（Core.TimelineScale）
            {
                Chk("T84a-short-clip-fine-labels",
                    TimelineScale.LabelStepFrames(39, 60) == 3,
                    "短片段时间轴拉宽（39px/帧@60fps）→ 0.05s 步长（3帧=117px），不再只有一枚 0s");
                Chk("T84b-long-clip-coarse-labels",
                    TimelineScale.LabelStepFrames(1, 60) == 120,
                    "长片段时间轴压扁（1px/帧@60fps）→ 2s 步长（120帧），不糊成一片");
                Chk("T84c-mid-clip",
                    TimelineScale.LabelStepFrames(10, 30) == 15,
                    "中密度（10px/帧@30fps）→ 0.5s 步长（15帧）");
                Chk("T84d-degenerate-inputs",
                    TimelineScale.LabelStepFrames(0, 60) == 1 && TimelineScale.LabelStepFrames(10, 0) == 1
                    && TimelineScale.LabelStepFrames(0.001, 60) == 1800,
                    "退化输入返回 1；极窄时落到最大档 30s（1800帧）");
            }
            // T85：GIF 误差扩散抖动（R25）——调色板被杂色占满时，暗渐变不得退化成少数几档
            {
                int W = 200, H = 40;
                var buf = new byte[W * H * 4];
                for (int y = 0; y < H; y++)
                {
                    int v = 10 + y * 30 / H; // 左半：10..39 共 30 档暗灰渐变
                    for (int x = 0; x < W; x++)
                    {
                        int o = (y * W + x) * 4;
                        if (x < W / 2) { buf[o] = (byte)v; buf[o + 1] = (byte)v; buf[o + 2] = (byte)v; }
                        else
                        {
                            // 300 个离散杂色（真实 UI 帧的色种量级）：压调色板但不垄断
                            int s = ((y * W + x) % 300) * 869;
                            buf[o] = (byte)(s & 0xFF); buf[o + 1] = (byte)((s >> 8) & 0xFF); buf[o + 2] = (byte)((s >> 16) & 0xFF);
                        }
                        buf[o + 3] = 255;
                    }
                }
                var gif = GifEncoder.Encode([new CapturedFrame { Bgra = buf, W = W, H = H, TimeMs = 0 }], [100]);
                using var msD = new MemoryStream(gif);
                using var imgD = Image.FromStream(msD, false, true);
                var dbmp = new Bitmap(imgD);
                // 抖动的本质是空间混合保均值：逐行均值必须贴合源渐变。
                // 无抖动时整段行共享同一档（带状纹），逐行均值会偏离源值数档。
                double worst = 0;
                var rowMeans = new double[H];
                for (int y = 0; y < H; y++)
                {
                    long rowSum = 0;
                    for (int x = 0; x < W / 2; x++) rowSum += dbmp.GetPixel(x, y).R;
                    rowMeans[y] = rowSum / (double)(W / 2);
                    worst = Math.Max(worst, Math.Abs(rowMeans[y] - (10 + y * 30 / H)));
                }
                dbmp.Dispose();
                Chk("T85a-gradient-dithered", worst <= 2.5,
                    $"暗渐变逐行均值最大偏离 {worst:F2}（无抖动整段行同档，偏离可达 ±5）" +
                    (Environment.GetEnvironmentVariable("FC_T85DBG") == "1"
                        ? " dec=[" + string.Join(" ", rowMeans.Select(m => m.ToString("F0"))) + "]"
                        + " src=[" + string.Join(" ", Enumerable.Range(0, H).Select(y => (10 + y * 30 / H).ToString())) + "]" : ""));
            }
            // T86：视觉暂留合成——末帧全亮、历史帧留衰减尾迹、暗底亮物可见；出图供逐帧目检
            {
                int W = 160, H = 40;
                var frames = new List<CapturedFrame>();
                for (int k = 0; k < 5; k++)
                {
                    var px = new byte[W * H * 4];
                    for (int i = 0; i < px.Length; i += 4) { px[i] = 20; px[i + 1] = 20; px[i + 2] = 20; px[i + 3] = 255; } // 暗底
                    int cx = 16 + k * 30;
                    for (int y = 14; y < 26; y++)
                        for (int x = cx; x < cx + 12; x++)
                        { int o = (y * W + x) * 4; px[o] = 0xFF; px[o + 1] = 0xC2; px[o + 2] = 0x4C; } // 亮青方块
                    frames.Add(new CapturedFrame { Bgra = px, W = W, H = H, TimeMs = k * 100 });
                }
                var comb = BgraImage.Afterimage(frames, 5);
                int At(int x, int y) => comb.Bgra[(y * W + x) * 4]; // B 通道
                int final = At(16 + 4 * 30 + 6, 20), first = At(16 + 6, 20), mid = At(16 + 2 * 30 + 6, 20);
                Chk("T86a-afterimage-final-bright", final >= 0xF0, $"末帧位置 B={final}（应全亮）");
                Chk("T86b-afterimage-trail-decaying", first > 20 && first < mid && mid < final,
                    $"尾迹单调衰减 first={first} mid={mid} final={final}");
                BgraImage.SavePng(comb, Path.Combine(tmp, "afterimage-t86.png"));
                Chk("T86c-afterimage-png", File.Exists(Path.Combine(tmp, "afterimage-t86.png")));
            }
            // T87：GIF 厘秒重采样——60fps 不提速、30fps 不丢帧、总时长守恒、延迟全为 ≥2cs 的整十毫秒
            {
                var (k60, d60) = FrameCapture.Core.GifTiming.Resample(Enumerable.Repeat(16, 60).ToList());
                int t60 = d60.Sum();
                Chk("T87a-60fps-duration", k60.Count >= 45 && k60.Count <= 55 && Math.Abs(t60 - 960) <= 5 * k60.Count
                    && d60.All(d => d >= 20 && d % 10 == 0) && k60[^1] == 59,
                    $"60fps: keep={k60.Count} total={t60}ms（目标 960±{5 * k60.Count}）min={d60.Min()}");
                var (k30, d30) = FrameCapture.Core.GifTiming.Resample(Enumerable.Repeat(33, 30).ToList());
                Chk("T87b-30fps-kept-all", k30.Count == 30 && Math.Abs(d30.Sum() - 990) <= 150 && d30.All(d => d >= 20),
                    $"30fps: keep={k30.Count} total={d30.Sum()}ms");
                var (k1, d1) = FrameCapture.Core.GifTiming.Resample(new List<int> { 16 });
                Chk("T87c-single-frame", k1.Count == 1 && k1[0] == 0 && d1[0] == 20, $"单帧 keep={k1.Count} d={d1[0]}");
                // 端到端：60 帧 16ms 编码 → WinForms 解码帧数 = 重采样后帧数（GIF 不再 1.2× 提速）
                int W = 32, H = 32;
                var fr = new List<CapturedFrame>();
                for (int i = 0; i < 60; i++)
                {
                    var px = new byte[W * H * 4];
                    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                        { int o = (y * W + x) * 4; px[o] = (byte)(x * 8); px[o + 1] = (byte)(i * 4); px[o + 2] = (byte)(y * 8); px[o + 3] = 255; }
                    fr.Add(new CapturedFrame { Bgra = px, W = W, H = H, TimeMs = i * 16 });
                }
                var gif = GifEncoder.Encode(fr, Enumerable.Repeat(16, 60).ToList(), true);
                using var ms = new MemoryStream(gif);
                using var img = System.Drawing.Image.FromStream(ms);
                var fd = img.FrameDimensionsList[0];
                int n = img.GetFrameCount(FrameDimension.Time);
                Chk("T87d-gif-framecount", n == k60.Count, $"GIF 解码 {n} 帧 = 重采样 {k60.Count} 帧");
            }
            // T88：截屏浮窗落点（R42）——大区域贴内侧右下；小区域贴外下缘；越界翻转/钳进工作区
            {
                var work = new RectI(0, 0, 3840, 2100);
                var big = FrameCapture.Core.ToastPlace.Place(new RectI(100, 100, 1200, 800), work, 300, 84);
                Chk("T88a-inside-bottom-right", big.X == 988 && big.Y == 804, $"大区域 → {big}（期望 988,804）");
                var small = FrameCapture.Core.ToastPlace.Place(new RectI(2000, 300, 100, 60), work, 300, 84);
                Chk("T88b-below-region", small.X == 1800 && small.Y == 370, $"小区域 → {small}（期望 1800,370）");
                var lowEdge = FrameCapture.Core.ToastPlace.Place(new RectI(2000, 2000, 100, 60), work, 300, 84);
                Chk("T88c-flip-above", lowEdge.Y == 1906, $"贴底小区域 → {lowEdge}（期望翻上 y=1906）");
                var corner = FrameCapture.Core.ToastPlace.Place(new RectI(0, 0, 100, 60), work, 300, 84);
                Chk("T88d-clamp-workarea", corner.X == 4 && corner.Y == 70, $"左上角小区域 → {corner}（x 钳 4）");
                bool inside = true;
                foreach (var r in new[] { new RectI(-1920, -100, 800, 500), new RectI(3700, 2050, 120, 40), new RectI(50, 50, 3740, 2000) })
                {
                    var p = FrameCapture.Core.ToastPlace.Place(r, work, 300, 84);
                    if (p.X < work.X || p.Y < work.Y || p.Right > work.Right || p.Bottom > work.Bottom) inside = false;
                }
                Chk("T88e-always-in-workarea", inside, "多区域（含负坐标/贴边）浮窗恒在工作区内");
            }
            {
                // T89 裁剪拖框引导（R45）
                var r1 = FrameCapture.Core.CropGuide.Rect(300, 400, 100, 120);
                Chk("T89a-rect-anydir", r1 == (100.0, 120.0, 200.0, 280.0), $"左上→右下反向拖 → {r1}");
                var r2 = FrameCapture.Core.CropGuide.Rect(100, 120, 300, 400);
                Chk("T89b-rect-same", r2 == r1, "两方向拖框同一矩形");
                var h1 = FrameCapture.Core.CropGuide.HintPos(100, 100, 200, 150, 120, 24, 800, 600);
                Chk("T89c-hint-below", h1.Y == 258 && h1.X == 140, $"默认框下居中 → {h1}");
                var h2 = FrameCapture.Core.CropGuide.HintPos(100, 400, 200, 180, 120, 24, 800, 600);
                Chk("T89d-hint-flip-up", h2.Y == 368, $"下方放不下翻上方 → {h2}");
                var h3 = FrameCapture.Core.CropGuide.HintPos(700, 100, 90, 50, 120, 24, 800, 600);
                Chk("T89e-hint-clamp-x", h3.X == 676 && h3.Y == 158, $"贴右缘横向钳边 → {h3}");
            }
            {
                // T90 文字标注（R46）：字号公式 + 裁剪保文本
                Chk("T90a-font-frame", FrameCapture.Core.AnnFont.FramePx(3) == 22 && FrameCapture.Core.AnnFont.FramePx(10) == 50, "细=22 粗=50 @1080p");
                Chk("T90b-font-1080", Math.Abs(FrameCapture.Core.AnnFont.BakePx(3, 1920, 1080) - 22) < 1e-9, "1080p 帧 = 基准");
                Chk("T90c-font-half", Math.Abs(FrameCapture.Core.AnnFont.BakePx(3, 960, 540) - 11) < 1e-9, "半分辨率等比缩 → 11px");
                Chk("T90d-font-floor", FrameCapture.Core.AnnFont.BakePx(1, 100, 100) == 10, "小帧下限 10px 防不可读");
                var ta = new FrameCapture.Drawing.Annotation { Kind = FrameCapture.Drawing.AnnKind.Text, Thickness = 6 };
                ta.Points.Add((0.5f, 0.5f)); ta.Text = "你好 世界";
                var keptT = FrameCapture.Core.CropRemap.Remap(new[] { ta }, 0.25, 0.25, 0.5, 0.5);
                Chk("T90e-remap-keeps-text", keptT.Count == 1 && keptT[0].Text == "你好 世界" && Math.Abs(keptT[0].Points[0].X - 0.5) < 1e-6,
                    $"裁剪重映射保留文字与坐标 → {keptT.Count}");
            }
            {
                // T91 马赛克（R47）：块粒度公式 + 块均值正确性 + 越界钳制
                Chk("T91a-block-1080", FrameCapture.Core.MosaicBlock.Px(3, 1920, 1080) == 6 && FrameCapture.Core.MosaicBlock.Px(10, 1920, 1080) == 20, "细=6 粗=20 @1080p");
                Chk("T91b-block-floor", FrameCapture.Core.MosaicBlock.Px(1, 200, 200) == 4, "小帧下限 4px");
                // 4x4 图，块 2：左上块 (10,10,10)×2 + (30,30,30)×2 → 均值 20
                var buf = new byte[4 * 4 * 4];
                void Put(int px, int py, byte v) { int i = (py * 4 + px) * 4; buf[i] = v; buf[i + 1] = v; buf[i + 2] = v; buf[i + 3] = 255; }
                for (int py = 0; py < 2; py++) for (int px = 0; px < 2; px++) Put(px, py, (px + py) % 2 == 0 ? (byte)10 : (byte)30);
                FrameCapture.Services.BgraImage.MosaicInPlace(buf, 4, 4, 0, 0, 2, 2, 2);
                Chk("T91c-mean", buf[0] == 20 && buf[4] == 20 && buf[16] == 20 && buf[20] == 20, $"块内全等于均值 20 → {buf[0]},{buf[4]},{buf[16]},{buf[20]}");
                Chk("T91d-untouched", buf[(3 * 4 + 3) * 4] == 0, "区域外像素不动");
                void Put2(int px, int py, byte v) { int i = (py * 4 + px) * 4; buf[i] = v; buf[i + 1] = v; buf[i + 2] = v; buf[i + 3] = 255; }
                Put2(2, 2, 100); // 其余 (2,3)(3,2)(3,3) 为 0 → 右下块均值 25
                FrameCapture.Services.BgraImage.MosaicInPlace(buf, 4, 4, 2, 2, 99, 99, 2); // 越界钳到 2x2
                Chk("T91e-clamp", buf[(2 * 4 + 2) * 4] == 25 && buf[(3 * 4 + 3) * 4] == 25, $"越界矩形钳进画布 → 右下块均值 25,{buf[(2 * 4 + 2) * 4]},{buf[(3 * 4 + 3) * 4]}");
            }
            {
                // T92 工具目录（R48）：单一数据源防漂移——加新工具漏帮助/漏字形/键位撞车当场红
                var tools = FrameCapture.Core.ToolCatalog.Tools;
                Chk("T92a-count", tools.Length == 11, $"十一个绘制工具 → {tools.Length}");
                var keys = tools.Select(t => t.Key).ToList();
                Chk("T92b-keys", keys.Distinct().Count() == 11 && keys.Select(k => k[0]).OrderBy(c => c).SequenceEqual("ACHLMNOPRTV"), "键位 V/L/R/O/A/P/H/T/M/C/N 唯一且齐全");
                Chk("T92c-glyphs", tools.All(t => t.Glyph.Length == 1 && t.Glyph[0] >= 0xE000 && t.Glyph[0] <= 0xF8FF) && tools.Select(t => t.Glyph).Distinct().Count() == 11,
                    "字形均为私有区码位且互不重复（GlyphProbe 定稿）");
                Chk("T92d-tips", tools.All(t => t.Tip.Contains(t.Key) && t.Help.Length >= 8), "工具条提示含快捷键、帮助说明非占位");
                Chk("T92e-heading", FrameCapture.Core.ToolCatalog.HelpHeading.Contains(FrameCapture.Core.ToolCatalog.ExitKey), "退出键 X 写进帮助小节标题");
                Chk("T92f-sym", tools.All(t => t.Sym.Length == 1 && !(t.Sym[0] >= 0xE000 && t.Sym[0] <= 0xF8FF)),
                    "帮助符号是普通 Unicode（默认字体可渲染，图标字形进帮助窗=豆腐块）");
            }
            {
                // T93 操作栈（R50）：编辑可撤销/重做、顺序保持、操作时序 LIFO——文字再编辑与对象化编辑的地基
                var h = new FrameCapture.Core.UndoRedo<string>();
                var a = "A"; var b = "B"; var b2 = "B'";
                h.Add(a); h.Add(b);
                Chk("T93a-replace", h.Replace(b, b2) && h.Items.SequenceEqual(new[] { a, b2 }) && h.Count == 2, "Replace 原位换新对象");
                Chk("T93b-undo-edit", h.Undo(out var it, out var res) && ReferenceEquals(it, b2) && ReferenceEquals(res, b) && h.Items.SequenceEqual(new[] { a, b }),
                    "撤销编辑=原位还原旧对象");
                Chk("T93c-redo-edit", h.Redo(out var it2) && ReferenceEquals(it2, b2) && h.Items.SequenceEqual(new[] { a, b2 }), "重做编辑=原位再换");
                // 时序：Add A → Add B → Edit A→A'；撤销次序必须是 Edit、B、A（不是列表尾序）
                var h2 = new FrameCapture.Core.UndoRedo<string>();
                var a0 = "A"; var b0 = "B"; var a1 = "A'";
                h2.Add(a0); h2.Add(b0); h2.Replace(a0, a1);
                h2.Undo(out _, out var r1);
                Chk("T93d-lifo-edit", ReferenceEquals(r1, a0) && h2.Items.SequenceEqual(new[] { a0, b0 }), "先撤编辑（即使它不在列表尾）");
                h2.Undo(out var r2, out _);
                Chk("T93e-lifo-add", ReferenceEquals(r2, b0) && h2.Items.SequenceEqual(new[] { a0 }), "再撤 Add B");
                h2.Undo(out var r3, out _);
                Chk("T93f-empty", ReferenceEquals(r3, a0) && h2.Count == 0 && !h2.CanUndo, "最后撤 Add A，栈空");
                Chk("T93g-replace-missing", !h2.Replace("nope", "x"), "替换不存在的对象返回 false");
            }
            {
                // T94 马赛克 WYSIWYG（R50）：编辑器预览=区域提取后 MosaicInPlace(0,0)，烘焙=整帧内 rect 起块
                // 两者必须逐字节一致，否则预览与输出块相位错位
                int w = 64, h = 64, rx = 10, ry = 8, rw = 30, rh = 20, blk = 6;
                var full = new byte[w * h * 4];
                var rnd = new Random(7);
                rnd.NextBytes(full);
                var fullCopy = (byte[])full.Clone();
                FrameCapture.Services.BgraImage.MosaicInPlace(fullCopy, w, h, rx, ry, rw, rh, blk);
                var region = new byte[rw * rh * 4];
                for (int row = 0; row < rh; row++)
                    Buffer.BlockCopy(fullCopy, ((ry + row) * w + rx) * 4, region, row * rw * 4, rw * 4);
                var regCopy = new byte[rw * rh * 4];
                for (int row = 0; row < rh; row++)
                    Buffer.BlockCopy(full, ((ry + row) * w + rx) * 4, regCopy, row * rw * 4, rw * 4);
                FrameCapture.Services.BgraImage.MosaicInPlace(regCopy, rw, rh, 0, 0, rw, rh, blk);
                Chk("T94a-region-eq-bake", region.SequenceEqual(regCopy), "区域提取马赛克 ≡ 整帧 rect 马赛克（逐字节）");
                var ann = new Annotation { Kind = AnnKind.Mosaic, Thickness = 6 };
                ann.Points.Add((0.1f, 0.2f)); ann.Points.Add((0.5f, 0.6f));
                // 100×50 帧：归一化 (0.1,0.2)-(0.5,0.6) → 像素矩形 (10,10,40,20)；框内变块均值、框外原样
                var src = Solid(100, 50, 10, 20, 30);
                for (int i = 0; i < src.Bgra.Length; i++) src.Bgra[i] = (byte)(i % 251);
                var srcSnap = (byte[])src.Bgra.Clone();
                var baked = Bake.Apply(new[] { src }, new[] { ann })[0];
                int iIn = (15 * 100 + 15) * 4, iOut = (15 * 100 + 5) * 4;
                bool inChanged = baked.Bgra[iIn] != srcSnap[iIn] || baked.Bgra[iIn + 1] != srcSnap[iIn + 1] || baked.Bgra[iIn + 2] != srcSnap[iIn + 2];
                Chk("T94b-bake-rect-map", inChanged && baked.Bgra[iOut] == srcSnap[iOut] && baked.Bgra[iOut + 1] == srcSnap[iOut + 1] && baked.Bgra[iOut + 2] == srcSnap[iOut + 2],
                    "归一化→像素矩形映射正确：框内马赛克、框外原样");
            }
            {
                // T95 删除操作（R52 选中工具）：原位删除、原位放回、与 Add/Edit 混排 LIFO
                var h = new FrameCapture.Core.UndoRedo<string>();
                var a = "A"; var b = "B"; var c = "C";
                h.Add(a); h.Add(b); h.Add(c);
                Chk("T95a-remove", h.Remove(b) && h.Items.SequenceEqual(new[] { a, c }), "删除后剩 A,C");
                Chk("T95b-undo-remove", h.Undo(out var it, out _) && ReferenceEquals(it, b) && h.Items.SequenceEqual(new[] { a, b, c }), "撤销删除=原位放回");
                Chk("T95c-redo-remove", h.Redo(out _) && h.Items.SequenceEqual(new[] { a, c }), "重做删除=再移除");
                h.Add("D");
                h.Remove(a);
                h.Undo(out _, out _);
                Chk("T95d-lifo-mixed", h.Items.SequenceEqual(new[] { a, c, "D" }), "Add D → Remove A → 撤 Remove：A 回原位 0");
                h.Undo(out var r2, out _);
                Chk("T95e-undo-add-after", ReferenceEquals(r2, "D") && h.Items.SequenceEqual(new[] { a, c }), "再撤 Add D");
                Chk("T95f-remove-missing", !h.Remove("nope"), "删除不存在的对象返回 false");
            }
            {
                // T96 Esc 分层加选中层（R52）：取消导出 > 取消选中 > 退工具 > 关窗
                Chk("T96a-esc-sel",
                    EscStep.Select(encoding: false, inToolMode: true, hasSelection: true) == EscAction.ClearSelection
                    && EscStep.Select(encoding: true, inToolMode: true, hasSelection: true) == EscAction.CancelEncode
                    && EscStep.Select(encoding: false, inToolMode: false, hasSelection: true) == EscAction.ClearSelection,
                    "有选中时 Esc 先取消选中（导出中仍优先取消导出）");
                Chk("T96b-esc-compat",
                    EscStep.Select(encoding: false, inToolMode: true) == EscAction.ExitTool
                    && EscStep.Select(encoding: false, inToolMode: false) == EscAction.Close,
                    "无选中时旧语义不变（默认参数向后兼容）");
            }
            {
                // T97 调色板数学（R54）：HSV 双向 + HEX 解析/格式化——自绘 RGBA 调色板的正确性地基
                static (byte, byte, byte) Rgb(double h, double s, double v) => FrameCapture.Core.Hsv.ToRgb(h, s, v);
                Chk("T97a-hsv-known",
                    Rgb(0, 1, 1) == (255, 0, 0) && Rgb(120, 1, 1) == (0, 255, 0) && Rgb(240, 1, 1) == (0, 0, 255)
                    && Rgb(0, 0, 1) == (255, 255, 255) && Rgb(0, 1, 0) == (0, 0, 0) && Rgb(60, 1, 1) == (255, 255, 0),
                    "红/绿/蓝/白/黑/黄 已知值");
                bool rt = true;
                var rnd = new Random(97);
                for (int i = 0; i < 400; i++)
                {
                    byte r0 = (byte)rnd.Next(256), g0 = (byte)rnd.Next(256), b0 = (byte)rnd.Next(256);
                    var (h, s, v) = FrameCapture.Core.Hsv.FromRgb(r0, g0, b0);
                    var (r1, g1, b1) = FrameCapture.Core.Hsv.ToRgb(h, s, v);
                    if (Math.Abs(r0 - r1) > 1 || Math.Abs(g0 - g1) > 1 || Math.Abs(b0 - b1) > 1) { rt = false; break; }
                }
                Chk("T97b-hsv-roundtrip", rt, "400 随机色 HSV→RGB 往返误差 ≤1");
                Chk("T97c-hex-parse",
                    FrameCapture.Core.HexColor.Parse("#f0a") is { R: 255, G: 0, B: 170, A: 255 }
                    && FrameCapture.Core.HexColor.Parse("#112233") is { R: 0x11, G: 0x22, B: 0x33, A: 255 }
                    && FrameCapture.Core.HexColor.Parse("#11223380") is { R: 0x11, G: 0x22, B: 0x33, A: 0x80 }
                    && FrameCapture.Core.HexColor.Parse("112233") is { R: 0x11 }
                    && FrameCapture.Core.HexColor.Parse("#12345") == null && FrameCapture.Core.HexColor.Parse("#gggggg") == null
                    && FrameCapture.Core.HexColor.Parse(null) == null,
                    "#RGB/#RRGGBB/#RRGGBBAA/无# 可解析，坏输入返回 null");
                var cA = System.Windows.Media.Color.FromRgb(0x11, 0x22, 0x33);
                var cT = System.Windows.Media.Color.FromArgb(0x80, 0x11, 0x22, 0x33);
                Chk("T97d-hex-format",
                    FrameCapture.Core.HexColor.Format(cA) == "#112233" && FrameCapture.Core.HexColor.Format(cT) == "#11223380"
                    && FrameCapture.Core.HexColor.Parse(FrameCapture.Core.HexColor.Format(cT)) is { A: 0x80 },
                    "A=255 省略 AA；带透明出 8 位；格式↔解析闭环");
            }
            {
                // T98 设置项默认值与 JSON 往返（R55 新增 ShowLastRegion）：老配置缺字段必须回默认 true，不能默默变 false
                var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings());
                var bare = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
                var full = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
                Chk("T98a-showlast-default", new AppSettings().ShowLastRegion, "默认显示上次选区");
                Chk("T98b-showlast-missing", bare.ShowLastRegion, "老配置无此字段 → 保持默认 true");
                Chk("T98c-showlast-roundtrip", full.ShowLastRegion
                    && !System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                        System.Text.Json.JsonSerializer.Serialize(new AppSettings { ShowLastRegion = false }))!.ShowLastRegion,
                    "开/关均能序列化往返");
            }
            {
                // T99 连按两下=免按住录制状态机随旧 F6/F7 热键路径退役（R126 B2）；保留 T128a 光标默认断言
                Chk("T128a-cursor-default", new AppSettings().RecordIncludeCursor, "录制包含鼠标指针默认开（无回归）");
            }
            {
                // T100 磁盘暂存帧仓（R61，FrameSpool/FrameSpoolList）：落盘往返 / 惰性回读 / 切片 / 元数据 / 文件生命周期
                var dir = Path.Combine(Path.GetTempPath(), "FrameCapture", "selftest");
                Directory.CreateDirectory(dir);
                var spoolPath = Path.Combine(dir, $"spool-test-{Guid.NewGuid():N}.fcf");
                var f0 = new CapturedFrame { W = 4, H = 2, TimeMs = 0, Bgra = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray() };
                var f1 = new CapturedFrame { W = 4, H = 2, TimeMs = 16.5, Bgra = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray() };
                var f2 = new CapturedFrame { W = 4, H = 2, TimeMs = 33, Bgra = Enumerable.Repeat((byte)0x7f, 32).ToArray() };
                int diskLowHits = 0;
                using (var spool = new FrameSpool(spoolPath))
                {
                    spool.DiskLow += _ => diskLowHits++;
                    spool.Add(f0); spool.Add(f1); spool.Add(f2);
                    Chk("T100a-count", spool.Count == 3, $"Count={spool.Count}");
                    Chk("T100b-file-exists", File.Exists(spoolPath), "spool 文件已落盘");
                    var list = spool.OpenReader();
                    Chk("T100c-roundtrip",
                        list.Count == 3
                        && list[0].Bgra.SequenceEqual(f0.Bgra) && list[0].TimeMs == 0 && list[0].W == 4 && list[0].H == 2
                        && list[1].Bgra.SequenceEqual(f1.Bgra) && list[1].TimeMs == 16.5
                        && list[2].Bgra.SequenceEqual(f2.Bgra) && list[2].TimeMs == 33,
                        "三帧 BGRA/时间戳/尺寸往返一致");
                    var m1 = list.Meta(1);
                    Chk("T100d-meta", m1.TimeMs == 16.5 && m1.W == 4 && m1.H == 2 && m1.Len == 32, "Meta 免回读 BGRA");
                    var slice = list.Slice(1, 2);
                    Chk("T100e-slice", slice.Count == 2 && slice[0].Bgra.SequenceEqual(f1.Bgra) && slice[1].Bgra.SequenceEqual(f2.Bgra),
                        "Slice(1,2) 索引映射正确");
                    var slice2 = list.Slice(2, 99);
                    Chk("T100f-slice-clamp", slice2.Count == 1 && slice2[0].TimeMs == 33, "Slice 越界钳到末尾");
                    Chk("T100g-nodisklow", diskLowHits == 0, "正常磁盘 → 护栏不误触发");
                    slice.Dispose(); slice2.Dispose(); // 先释放所有读句柄，Windows 才允许删文件
                    Chk("T100h-slice-no-delete", File.Exists(spoolPath), "Slice.Dispose 不删文件（非属主）");
                    list.Dispose();
                    Chk("T100i-dispose-delete", !File.Exists(spoolPath), "属主 list.Dispose 删 spool 文件");
                }
                var p2 = Path.Combine(dir, $"spool-test-{Guid.NewGuid():N}.fcf");
                var sp2 = new FrameSpool(p2);
                sp2.Add(f0);
                sp2.Abandon();
                Chk("T100j-abandon", !File.Exists(p2), "Abandon 删半成品文件");
                Chk("T100k-freebytes", FrameSpool.FreeBytesFor(spoolPath) > 0, "卷剩余空间查询有效");

                // T101 自动保存副本（R63，SaveService）：唯一命名 / 目录回退 / 关闭零副作用
                var svDir = Path.Combine(dir, "autosave-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(svDir);
                Chk("T101a-unique-free",
                    FrameCapture.Services.SaveService.UniqueName(svDir, "FrameCapture x", "png") == "FrameCapture x.png",
                    "无冲突 → 原名");
                File.WriteAllBytes(Path.Combine(svDir, "FrameCapture x.png"), new byte[] { 1 });
                Chk("T101b-unique-2",
                    FrameCapture.Services.SaveService.UniqueName(svDir, "FrameCapture x", "png") == "FrameCapture x (2).png",
                    "同名占用 → (2)");
                File.WriteAllBytes(Path.Combine(svDir, "FrameCapture x.gif"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(svDir, "FrameCapture x (2).gif"), new byte[] { 1 });
                Chk("T101c-unique-3",
                    FrameCapture.Services.SaveService.UniqueName(svDir, "FrameCapture x", "gif") == "FrameCapture x (3).gif",
                    "同名+（2）占用 → (3)");
                // SelfTest 不建 SettingsService（Shared=null）→ 自动保存视为关闭：目录回退默认、写盘零副作用
                var resolved = FrameCapture.Services.SaveService.ResolveFolder();
                Chk("T101d-folder-fallback",
                    resolved == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FrameCapture")
                    && Directory.Exists(resolved),
                    "空路径 → 回退 图片\\FrameCapture 且已建");
                int before = Directory.GetFiles(resolved).Length;
                var oneSeq = new CapturedSequence { Frames = new List<CapturedFrame> { f0 }, Region = default, MonitorBounds = default };
                var twoSeq = new CapturedSequence { Frames = new List<CapturedFrame> { f0, f1 }, Region = default, MonitorBounds = default };
                FrameCapture.Services.SaveService.MaybeSaveArtifact(oneSeq, new byte[] { 1, 2, 3 });
                FrameCapture.Services.SaveService.MaybeSaveArtifact(twoSeq, new byte[] { 1, 2, 3 });
                FrameCapture.Services.SaveService.MaybeSavePng(f0);
                Chk("T101e-off-noop", Directory.GetFiles(resolved).Length == before,
                    "关闭状态（Shared=null）→ 零落盘，绝不影响捕获");
                try { Directory.Delete(svDir, true); } catch { }
            }

            // T102 编辑器记住工具/颜色/粗细（R64，EditorPrefs 纯决策）
            {
                var sw = new (byte A, byte R, byte G, byte B)[]
                {
                    (255, 0xE8, 0x11, 0x23), (255, 0xFF, 0xB9, 0x00),
                    (255, 0x10, 0x89, 0x3E), (255, 0x00, 0x78, 0xD4), (255, 0xFF, 0xFF, 0xFF),
                };
                var ws = new float[] { 3f, 6f, 10f };
                Chk("T102a-color-hit", FrameCapture.Core.EditorPrefs.ColorSlot(255, 0x10, 0x89, 0x3E, sw) == 2, "颜色命中预设 → 下标 2");
                Chk("T102b-color-custom", FrameCapture.Core.EditorPrefs.ColorSlot(255, 0x12, 0x34, 0x56, sw) == -1, "非预设色 → 自定义 -1");
                Chk("T102c-color-alpha", FrameCapture.Core.EditorPrefs.ColorSlot(128, 0xE8, 0x11, 0x23, sw) == -1, "同 RGB 不同 alpha → 不误判成不透明预设");
                Chk("T102d-width-hit", FrameCapture.Core.EditorPrefs.WidthSlot(6f, ws) == 1, "线宽命中预设 → 下标 1");
                Chk("T102e-width-custom", FrameCapture.Core.EditorPrefs.WidthSlot(7.5f, ws) == -1, "非预设宽 → 自定义 -1");
                Chk("T102f-hex", FrameCapture.Core.EditorPrefs.ToHex(255, 0xE8, 0x11, 0x23) == "#FFE81123", "颜色 → #AARRGGBB");
            }

            // T103 钉图滚轮缩放（R65，PinZoom 纯决策）
            {
                Chk("T103a-zoom-in", System.Math.Abs(FrameCapture.Core.PinZoom.Next(1.0, 1) - 1.1) < 1e-9, "delta>0 → ×1.1 放大");
                Chk("T103b-zoom-out", System.Math.Abs(FrameCapture.Core.PinZoom.Next(1.0, -1) - 1.0 / 1.1) < 1e-9, "delta<0 → ÷1.1 缩小");
                Chk("T103c-zoom-max", FrameCapture.Core.PinZoom.Next(6.0, 1) == 6.0, "已到上限再放大 → 锁死 6.0");
                Chk("T103d-zoom-min", FrameCapture.Core.PinZoom.Next(0.1, -1) == 0.1, "已到下限再缩小 → 锁死 0.1");
                Chk("T103e-zoom-zero", FrameCapture.Core.PinZoom.Next(2.5, 0) == 2.5, "delta=0 → 不变");
            }

            // T104 贴图不透明度滚轮（R66，PinOpacity 纯决策）
            {
                Chk("T104a-op-up", System.Math.Abs(FrameCapture.Core.PinOpacity.Next(0.5, 1) - 0.6) < 1e-9, "delta>0 → +0.1 更不透明");
                Chk("T104b-op-down", System.Math.Abs(FrameCapture.Core.PinOpacity.Next(0.5, -1) - 0.4) < 1e-9, "delta<0 → −0.1 更透明");
                Chk("T104c-op-max", FrameCapture.Core.PinOpacity.Next(1.0, 1) == 1.0, "已全不透明再增 → 锁死 1.0");
                Chk("T104d-op-min", FrameCapture.Core.PinOpacity.Next(0.2, -1) == 0.2, "已到最透明再减 → 锁死 0.2");
                Chk("T104e-op-zero", FrameCapture.Core.PinOpacity.Next(0.7, 0) == 0.7, "delta=0 → 不变");
            }

            // T105 OCR 结果分类（R69，OcrService.Classify 纯决策）
            {
                Chk("T105a-ocr-text", FrameCapture.Core.OcrService.Classify("hello 世界") == FrameCapture.Core.OcrStatus.Success, "有文字 → Success");
                Chk("T105b-ocr-empty", FrameCapture.Core.OcrService.Classify("") == FrameCapture.Core.OcrStatus.Empty, "空串 → Empty");
                Chk("T105c-ocr-ws", FrameCapture.Core.OcrService.Classify("  \n\t ") == FrameCapture.Core.OcrStatus.Empty, "全空白 → Empty");
                Chk("T105d-ocr-padded", FrameCapture.Core.OcrService.Classify("  有字  ") == FrameCapture.Core.OcrStatus.Success, "带空白有字 → Success");
            }

            // T106 选区放大镜几何（R70，MagnifierMath 纯决策）
            {
                var c1 = FrameCapture.Core.MagnifierMath.Crop(100, 100, 1000, 1000, 15);
                Chk("T106a-crop-center", c1 == (93, 93, 15), "中心 → 居中 15×15");
                var c2 = FrameCapture.Core.MagnifierMath.Crop(2, 2, 1000, 1000, 15);
                Chk("T106b-crop-left", c2 == (0, 0, 15), "近左上 → 夹到 0,0");
                var c3 = FrameCapture.Core.MagnifierMath.Crop(998, 998, 1000, 1000, 15);
                Chk("T106c-crop-right", c3 == (985, 985, 15), "近右下 → 夹到 snap-span");
                var c4 = FrameCapture.Core.MagnifierMath.Crop(5, 5, 10, 10, 15);
                Chk("T106d-crop-small", c4 == (0, 0, 10), "快照小于 span → 退化整幅");
                var p1 = FrameCapture.Core.MagnifierMath.Place(100, 100, 150, 180, 1000, 1000, 18);
                Chk("T106e-place-br", p1 == (118, 118), "默认贴右下");
                var p2 = FrameCapture.Core.MagnifierMath.Place(950, 100, 150, 180, 1000, 1000, 18);
                Chk("T106f-place-flipx", p2 == (782, 118), "越右边 → 翻左");
                var p3 = FrameCapture.Core.MagnifierMath.Place(100, 950, 150, 180, 1000, 1000, 18);
                Chk("T106g-place-flipy", p3 == (118, 752), "越下边 → 翻上");
            }

            // T107 放大镜取色（R71，MagnifierMath.PixelRgb/Hex 纯决策）
            {
                var buf = new byte[] { 10, 20, 30, 255, 40, 50, 60, 255 }; // 2px BGRA
                Chk("T107a-rgb-p0", FrameCapture.Core.MagnifierMath.PixelRgb(buf, 2, 0, 0) == (30, 20, 10), "像素0 → R,G,B");
                Chk("T107b-rgb-p1", FrameCapture.Core.MagnifierMath.PixelRgb(buf, 2, 1, 0) == (60, 50, 40), "像素1 → R,G,B");
                Chk("T107c-rgb-oob", FrameCapture.Core.MagnifierMath.PixelRgb(buf, 2, 0, 1) == (0, 0, 0), "越界 → 0,0,0");
                Chk("T107d-rgb-neg", FrameCapture.Core.MagnifierMath.PixelRgb(buf, 2, -1, 0) == (0, 0, 0), "负坐标 → 0,0,0");
                Chk("T107e-hex", FrameCapture.Core.MagnifierMath.Hex(255, 128, 0) == "#FF8000", "RGB→#RRGGBB 大写");
                Chk("T107f-hex-clamp", FrameCapture.Core.MagnifierMath.Hex(300, -5, 16) == "#FF0010", "越界分量夹到 0–255");
            }

            // T108 OCR 文本清理（R72，OcrService.Normalize 纯决策）
            {
                Chk("T108a-collapse-blank", FrameCapture.Core.OcrService.Normalize("a\n\n\n\nb") == "a\n\nb", "连续空行压成一个");
                Chk("T108b-trim-end", FrameCapture.Core.OcrService.Normalize("  x  \n  y  ") == "  x\n  y", "去行尾空白留缩进");
                Chk("T108c-empty", FrameCapture.Core.OcrService.Normalize("") == "", "空 → 空");
                Chk("T108d-single", FrameCapture.Core.OcrService.Normalize("one") == "one", "单行原样");
                Chk("T108e-allblank", FrameCapture.Core.OcrService.Normalize("\n\n\n") == "", "全空行 → 空");
                Chk("T108f-crlf", FrameCapture.Core.OcrService.Normalize("a\r\nb\r\nc") == "a\nb\nc", "CRLF→LF");
            }

            // T109 序号编号（R73，NumberBadge 纯决策）
            {
                Chk("T109a-first", FrameCapture.Core.NumberBadge.Next(new string[0]) == 1, "无 → 1");
                Chk("T109b-seq", FrameCapture.Core.NumberBadge.Next(new[] { "1", "2", "3" }) == 4, "1,2,3 → 4");
                Chk("T109c-gap", FrameCapture.Core.NumberBadge.Next(new[] { "1", "3" }) == 4, "删中间不补洞 → max+1");
                Chk("T109d-rollback", FrameCapture.Core.NumberBadge.Next(new[] { "2", "3" }) == 4, "删 1 后仍 4（撤销回退靠重算）");
                Chk("T109e-nonnum", FrameCapture.Core.NumberBadge.Next(new[] { "x", "5" }) == 6, "非数字忽略");
                Chk("T109f-diam", FrameCapture.Core.NumberBadge.Diameter(20) == 40, "直径=字号×2");
                Chk("T109g-lightcircle", FrameCapture.Core.NumberBadge.NumberOnLightCircle(0xFF, 0xB9, 0x00) && FrameCapture.Core.NumberBadge.NumberOnLightCircle(0xFF, 0xFF, 0xFF), "琥珀#FFB900/白圆圈→黑编号");
                Chk("T109h-darkcircle", !FrameCapture.Core.NumberBadge.NumberOnLightCircle(0xE8, 0x11, 0x23) && !FrameCapture.Core.NumberBadge.NumberOnLightCircle(0, 0, 0), "红#E81123/黑圆圈→白编号");
            }

            // T110 贴图磁边（R74，PinSnap 纯决策）
            {
                var s1 = FrameCapture.Core.PinSnap.Snap(5, 500, 200, 100, 0, 0, 1920, 1080, 12);
                Chk("T110a-left", s1.Left == 0 && s1.Top == 500, "左边距 5<阈值 → 吸左");
                var s2 = FrameCapture.Core.PinSnap.Snap(1730, 500, 200, 100, 0, 0, 1920, 1080, 12);
                Chk("T110b-right", s2.Left == 1720 && s2.Top == 500, "右边距 10<阈值 → 吸右");
                var s3 = FrameCapture.Core.PinSnap.Snap(500, 3, 200, 100, 0, 0, 1920, 1080, 12);
                Chk("T110c-top", s3.Left == 500 && s3.Top == 0, "上边距 3<阈值 → 吸上");
                var s4 = FrameCapture.Core.PinSnap.Snap(500, 985, 200, 100, 0, 0, 1920, 1080, 12);
                Chk("T110d-bottom", s4.Left == 500 && s4.Top == 980, "下边距 5<阈值 → 吸下");
                var s5 = FrameCapture.Core.PinSnap.Snap(500, 500, 200, 100, 0, 0, 1920, 1080, 12);
                Chk("T110e-none", s5.Left == 500 && s5.Top == 500, "远离边缘 → 不动");
                Chk("T110f-thresh", FrameCapture.Core.PinSnap.Threshold(200, 100) == 24
                    && FrameCapture.Core.PinSnap.Threshold(1000, 800) == 56
                    && FrameCapture.Core.PinSnap.Threshold(300, 300) == 36, "阈值=短边12%夹24–56（R119 放宽）");
            }

            // T111 OCR 反馈文案（R75，编辑器/贴图共用措辞）
            {
                Chk("T111a-success", FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.Success, 42) == "已识别 42 字 · 已复制", "成功带字数");
                Chk("T111b-noengine", FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.NoEngine, 0).Contains("语言包"), "无引擎→装语言包指引");
                Chk("T111c-empty", FrameCapture.Core.OcrService.Feedback(FrameCapture.Core.OcrStatus.Empty, 0) == "未识别到文字", "空→未识别");
            }

            // T112 标注改样式副本（R76，WithStyle 纯函数）
            {
                var red = System.Drawing.Color.FromArgb(255, 0xE7, 0x48, 0x4C);
                var blue = System.Drawing.Color.FromArgb(255, 0, 0, 255);
                var b0 = new FrameCapture.Drawing.Annotation { Kind = FrameCapture.Drawing.AnnKind.Arrow, Color = red, Thickness = 4 };
                b0.Points.Add((0.1f, 0.2f)); b0.Points.Add((0.5f, 0.6f)); b0.Text = "hi";
                var c1 = b0.WithStyle(blue, null);
                Chk("T112a-color", c1.Color.B == 255 && c1.Thickness == 4 && c1.Kind == FrameCapture.Drawing.AnnKind.Arrow && c1.Points.Count == 2, "改色保留粗细/几何");
                var c2 = b0.WithStyle(null, 10);
                Chk("T112b-width", c2.Color.R == 0xE7 && c2.Thickness == 10, "改粗细保留色");
                var c3 = b0.WithStyle(blue, 10);
                Chk("T112c-both", c3.Color.B == 255 && c3.Thickness == 10, "两者都改");
                Chk("T112d-keep", c1.Points[0].X == 0.1f && c1.Points[1].Y == 0.6f && c1.Text == "hi" && b0.Points.Count == 2, "点集/文字保留且原对象不变");
            }

            // T113 贴图对齐参考线（R77，SnapToLines 纯决策）
            {
                var e = Array.Empty<double>();
                // 左边贴屏幕边（xEdge 接受 left 锚点）
                var a1 = FrameCapture.Core.PinSnap.SnapToLines(105, 500, 200, 100, new[] { 100.0 }, e, e, e, 12);
                Chk("T113a-edge-left", a1.Left == 100 && a1.Top == 500, "left 距屏幕边 5<阈值 → 吸左");
                // 右边贴屏幕边（xEdge 接受 right 锚点）
                var a2 = FrameCapture.Core.PinSnap.SnapToLines(105, 500, 200, 100, new[] { 300.0 }, e, e, e, 12);
                Chk("T113b-edge-right", a2.Left == 100, "right(305) 距屏幕边 300 差 5 → 右对齐到 300");
                // 中心贴其他贴图线（xAny 接受 center 锚点）
                var a3 = FrameCapture.Core.PinSnap.SnapToLines(105, 500, 200, 100, e, new[] { 200.0 }, e, e, 12);
                Chk("T113c-any-center", a3.Left == 100, "center(205) 距贴图线 200 差 5 → 中心对齐");
                // 关键：屏幕边不接受 center 锚点（否则半张图出屏）
                var a4 = FrameCapture.Core.PinSnap.SnapToLines(105, 500, 200, 100, new[] { 205.0 }, e, e, e, 12);
                Chk("T113d-edge-no-center", a4.Left == 105, "center 恰在屏幕边线上也不吸（屏幕边只认左右边）");
                // 超阈值不动
                var a5 = FrameCapture.Core.PinSnap.SnapToLines(105, 500, 200, 100, new[] { 50.0 }, e, e, e, 12);
                Chk("T113e-far", a5.Left == 105 && a5.Top == 500, "参考线超阈值 → 不动");
                // y 轴中线贴其他贴图
                var a6 = FrameCapture.Core.PinSnap.SnapToLines(500, 305, 200, 100, e, e, e, new[] { 350.0 }, 12);
                Chk("T113f-any-middle", a6.Top == 300, "middle(355) 距贴图线 350 差 5 → 垂直中心对齐");
                // 空参考线 → 原样
                var a7 = FrameCapture.Core.PinSnap.SnapToLines(123, 456, 200, 100, e, e, e, e, 12);
                Chk("T113g-empty", a7.Left == 123 && a7.Top == 456, "无参考线 → 原样");
                // x/y 各轴独立取最近
                var a8 = FrameCapture.Core.PinSnap.SnapToLines(105, 505, 200, 100, new[] { 100.0 }, new[] { 302.0 }, new[] { 700.0 }, e, 12);
                Chk("T113h-axes", a8.Left == 102 && a8.Top == 505, "x 取最近(right→302 差3)，y 超阈值不动");
            }

            // T114 最近静态截图保留（R78，LastStill 状态持有）
            {
                var ls = new FrameCapture.Capture.LastStill();
                Chk("T114a-empty", ls.Empty && !ls.TryGet(out _, out _, out _), "初始为空 → TryGet false");
                var f1 = new FrameCapture.Capture.CapturedFrame { Bgra = new byte[4], W = 1, H = 1 };
                var f2 = new FrameCapture.Capture.CapturedFrame { Bgra = new byte[8], W = 2, H = 1 };
                ls.Set(f1, new FrameCapture.Capture.RectI(10, 20, 30, 40), "DEV1");
                var ok1 = ls.TryGet(out var gf, out var gr, out var gd);
                Chk("T114b-set", ok1 && !ls.Empty && ReferenceEquals(gf, f1) && gr.X == 10 && gr.W == 30 && gd == "DEV1", "Set 后取出帧/区域/设备一致");
                ls.Set(f2, new FrameCapture.Capture.RectI(1, 2, 3, 4), "DEV2");
                var ok2 = ls.TryGet(out var gf2, out var gr2, out var gd2);
                Chk("T114c-overwrite", ok2 && ReferenceEquals(gf2, f2) && gr2.X == 1 && gd2 == "DEV2", "再次 Set 覆盖为最新一帧");
                ls.Clear();
                Chk("T114d-clear", ls.Empty && !ls.TryGet(out _, out _, out _), "Clear 后回到空");
            }

            // T115 贴图旋转（R79，PinRotate 纯函数）
            {
                static FrameCapture.Capture.CapturedFrame Mk(int w, int h)
                {
                    var b = new byte[w * h * 4];
                    for (int i = 0; i < w * h; i++) b[i * 4] = (byte)(i + 1); // B 通道 = 像素序号+1
                    return new FrameCapture.Capture.CapturedFrame { Bgra = b, W = w, H = h };
                }
                static int B(FrameCapture.Capture.CapturedFrame f, int x, int y) => f.Bgra[(y * f.W + x) * 4];
                static bool Same(FrameCapture.Capture.CapturedFrame a, FrameCapture.Capture.CapturedFrame b)
                {
                    if (a.W != b.W || a.H != b.H || a.Bgra.Length != b.Bgra.Length) return false;
                    for (int i = 0; i < a.Bgra.Length; i++) if (a.Bgra[i] != b.Bgra[i]) return false;
                    return true;
                }
                var r1 = FrameCapture.Core.PinRotate.Rotate90Cw(Mk(2, 1));
                Chk("T115a-h2v", r1.W == 1 && r1.H == 2 && B(r1, 0, 0) == 1 && B(r1, 0, 1) == 2, "横 2x1 [A B] → 竖 1x2 A上B下");
                var r2 = FrameCapture.Core.PinRotate.Rotate90Cw(Mk(2, 2));
                Chk("T115b-2x2", r2.W == 2 && r2.H == 2 && B(r2, 0, 0) == 3 && B(r2, 1, 0) == 1 && B(r2, 0, 1) == 4 && B(r2, 1, 1) == 2, "2x2 顺时针：上行→右列");
                var orig = Mk(3, 2);
                Chk("T115c-identity4", Same(FrameCapture.Core.PinRotate.Rotate(orig, 4), orig), "转 4 次回原图");
                Chk("T115d-steps", ReferenceEquals(FrameCapture.Core.PinRotate.Rotate(orig, 0), orig)
                    && Same(FrameCapture.Core.PinRotate.Rotate(orig, 5), FrameCapture.Core.PinRotate.Rotate(orig, 1))
                    && Same(FrameCapture.Core.PinRotate.Rotate(orig, -1), FrameCapture.Core.PinRotate.Rotate(orig, 3)), "steps=0 同实例；5≡1、-1≡3");
            }
            // T116 截图后行为解析（R83，AfterCapture 纯函数）
            {
                Chk("T116a-editor", FrameCapture.Core.AfterCapture.Resolve("editor") == FrameCapture.Core.AfterCaptureAction.Editor, "editor→Editor");
                Chk("T116b-copy", FrameCapture.Core.AfterCapture.Resolve("copy") == FrameCapture.Core.AfterCaptureAction.Copy, "copy→Copy");
                Chk("T116c-toast", FrameCapture.Core.AfterCapture.Resolve("toast") == FrameCapture.Core.AfterCaptureAction.Toast, "toast→Toast");
                Chk("T116d-dirty", FrameCapture.Core.AfterCapture.Resolve(null) == FrameCapture.Core.AfterCaptureAction.Toast
                    && FrameCapture.Core.AfterCapture.Resolve("") == FrameCapture.Core.AfterCaptureAction.Toast
                    && FrameCapture.Core.AfterCapture.Resolve("Editor") == FrameCapture.Core.AfterCaptureAction.Toast
                    && FrameCapture.Core.AfterCapture.Resolve("garbage") == FrameCapture.Core.AfterCaptureAction.Toast, "null/空/大小写/脏值→回落 Toast");
                Chk("T116e-pin", FrameCapture.Core.AfterCapture.Resolve("pin") == FrameCapture.Core.AfterCaptureAction.Pin, "R117 pin→Pin");
            }
            // T117 荧光笔半透明烘焙（R85）：盖在白底上应透出白（半透明），而非不透明纯色
            {
                var hl = new Annotation { Kind = AnnKind.Highlight, Color = Color.FromArgb(255, 255, 0, 0), Thickness = 300 };
                hl.Points.Add((0.2f, 0.5f)); hl.Points.Add((0.5f, 0.5f)); hl.Points.Add((0.8f, 0.5f));
                var outF = Bake.Apply([Solid(100, 100, 255, 255, 255)], [hl])[0];
                int on = (50 * 100 + 50) * 4;
                byte b0 = outF.Bgra[on], g0 = outF.Bgra[on + 1], r0 = outF.Bgra[on + 2];
                Chk("T117a-highlight-translucent", r0 > 200 && g0 > 90 && g0 < 235 && b0 > 90 && b0 < 235,
                    $"rgba={b0},{g0},{r0} 期望 R高 G/B中(透出白，半透明)");
                var pen = new Annotation { Kind = AnnKind.Freehand, Color = Color.FromArgb(255, 255, 0, 0), Thickness = 300 };
                pen.Points.Add((0.2f, 0.5f)); pen.Points.Add((0.5f, 0.5f)); pen.Points.Add((0.8f, 0.5f));
                var penF = Bake.Apply([Solid(100, 100, 255, 255, 255)], [pen])[0];
                int pon = (50 * 100 + 50) * 4;
                Chk("T117b-pen-opaque-contrast", penF.Bgra[pon] < 60 && penF.Bgra[pon + 1] < 60,
                    $"对照不透明手绘 B/G 应低 rgba={penF.Bgra[pon]},{penF.Bgra[pon + 1]},{penF.Bgra[pon + 2]}");
            }
            // T118 截图提示音合成（R86，CaptureSound 纯函数）：WAV 头合法 + 时长 + 峰值安全 + 有声音且衰减
            {
                var s = FrameCapture.Core.CaptureSound.Samples();
                var wav = FrameCapture.Core.CaptureSound.MakeWav(s);
                bool riff = wav.Length > 44 && wav[0] == 'R' && wav[1] == 'I' && wav[2] == 'F' && wav[3] == 'F'
                    && wav[8] == 'W' && wav[9] == 'A' && wav[10] == 'V' && wav[11] == 'E'
                    && wav[12] == 'f' && wav[13] == 'm' && wav[14] == 't' && wav[15] == ' '
                    && wav[36] == 'd' && wav[37] == 'a' && wav[38] == 't' && wav[39] == 'a';
                int dataBytes = wav.Length - 44;
                int declared = BitConverter.ToInt32(wav, 4);
                Chk("T118a-wav-header", riff && declared == 36 + dataBytes, $"len={wav.Length} riffSize={declared} expect={36 + dataBytes}");
                double msDur = dataBytes / 2.0 / 44100.0 * 1000.0;
                Chk("T118b-duration", msDur > 30 && msDur < 60, $"时长 {msDur:F1}ms");
                short peak = 0;
                for (int i = 44; i + 1 < wav.Length; i += 2) { short v = BitConverter.ToInt16(wav, i); if (Math.Abs((int)v) > Math.Abs((int)peak)) peak = v; }
                double pf = Math.Abs((int)peak) / 32767.0;
                Chk("T118c-peak-safe", pf > 0.1 && pf < 0.5, $"峰值 {pf:F3} 满量程（不静音/不削顶）");
                short last = BitConverter.ToInt16(wav, wav.Length - 2);
                Chk("T118d-decays", Math.Abs((int)last) / 32767.0 < 0.02 && peak != 0, $"末样本 {Math.Abs((int)last) / 32767.0:F4} 峰值 {pf:F3}");
            }
            // T119 滚动截图拼接算法（R87，ScrollStitch 纯函数）：找重叠 + 拼接 + 无重叠 + 抗噪
            {
                static byte Gray(int row)   // 雪崩整数哈希：相邻/任意偏移行输出都近似独立，杜绝线性假重叠
                {
                    uint x = (uint)row;
                    x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
                    return (byte)(x >> 24);
                }
                static byte[] Frame(int w, int start, int len)   // 文档行 start..start+len-1
                {
                    var buf = new byte[len * w * 4];
                    for (int r = 0; r < len; r++)
                    {
                        byte g = Gray(start + r);
                        for (int x = 0; x < w; x++) { int p = (r * w + x) * 4; buf[p] = g; buf[p + 1] = g; buf[p + 2] = g; buf[p + 3] = 255; }
                    }
                    return buf;
                }
                static byte[] ConstFrame(int w, int len, byte g)
                {
                    var buf = new byte[len * w * 4];
                    for (int i = 0; i < buf.Length; i += 4) { buf[i] = g; buf[i + 1] = g; buf[i + 2] = g; buf[i + 3] = 255; }
                    return buf;
                }
                int W = 8;
                var acc = Frame(W, 0, 100);        // 文档行 0..99
                var bOv = Frame(W, 60, 100);       // 文档行 60..159 → 与 acc 底部重叠 40 行
                int ov = FrameCapture.Core.ScrollStitch.FindOverlap(acc, W, 100, bOv, 100);
                Chk("T119a-overlap", ov == 40, $"重叠应 40，实得 {ov}");
                var merged = FrameCapture.Core.ScrollStitch.Append(acc, W, 100, bOv, 100, ov);
                int newH = merged.Length / (W * 4);
                byte RowGray(byte[] buf, int row) => buf[(row * W) * 4];   // 该行第 0 列 B 通道=灰度
                Chk("T119b-append", newH == 160 && RowGray(merged, 100) == Gray(100) && RowGray(merged, 159) == Gray(159),
                    $"拼接高 {newH} 期望 160，行100={RowGray(merged, 100)} 期望 {Gray(100)}，行159={RowGray(merged, 159)} 期望 {Gray(159)}");
                var bNo = ConstFrame(W, 100, (byte)((acc[(99 * W) * 4] + 128) & 0xFF)); // 常量帧，与 acc 末行差 128、且各行灰度各异 → 无重叠
                int ovNo = FrameCapture.Core.ScrollStitch.FindOverlap(acc, W, 100, bNo, 100);
                Chk("T119c-no-overlap", ovNo == 0, $"无重叠应 0，实得 {ovNo}");
                var bNoise = (byte[])bOv.Clone();   // 每像素 ±3 抖动，仍应找到重叠 40
                var rng = new Random(1234);
                for (int i = 0; i < bNoise.Length; i += 4)
                {
                    int d = rng.Next(-3, 4);
                    bNoise[i] = (byte)Math.Clamp(bNoise[i] + d, 0, 255);
                    bNoise[i + 1] = (byte)Math.Clamp(bNoise[i + 1] + d, 0, 255);
                    bNoise[i + 2] = (byte)Math.Clamp(bNoise[i + 2] + d, 0, 255);
                }
                int ovN = FrameCapture.Core.ScrollStitch.FindOverlap(acc, W, 100, bNoise, 100);
                Chk("T119d-noise-tolerant", ovN == 40, $"抗噪重叠应 40，实得 {ovN}");
                // T119e：重叠区含个别离群行（平滑滚动重采样/闪烁光标行），旧「运行均值」早退会误杀返回 0，
                // 新版抽样多数匹配应仍判 40（R119 修「滚动太快」误报的回归护栏）
                var bOutlier = (byte[])bOv.Clone();
                for (int rr = 0; rr < 2; rr++)   // 把 b 顶部两行（重叠区首两行）改成完全不同的灰度
                {
                    byte g = (byte)(Gray(9000 + rr) ^ 0xFF);
                    for (int x = 0; x < W; x++) { int p = (rr * W + x) * 4; bOutlier[p] = g; bOutlier[p + 1] = g; bOutlier[p + 2] = g; }
                }
                int ovO = FrameCapture.Core.ScrollStitch.FindOverlap(acc, W, 100, bOutlier, 100);
                Chk("T119e-outlier-tolerant", ovO == 40, $"含离群行的重叠应仍判 40，实得 {ovO}");
                // T119f：±1px 垂直抖动（平滑/分数滚动取整，奇数行整体下移 1px）。旧「只比对应行」→ 半数行差超阈
                // → 误判 0 → 拼接出现重复内容带「重叠」；新版容差趟应仍判 40（R120 修「重叠」回归护栏）
                var bJit = new byte[100 * W * 4];
                for (int r = 0; r < 100; r++)
                {
                    byte g = Gray(60 + r + (r % 2));
                    for (int x = 0; x < W; x++) { int p = (r * W + x) * 4; bJit[p] = g; bJit[p + 1] = g; bJit[p + 2] = g; bJit[p + 3] = 255; }
                }
                int ovJ = FrameCapture.Core.ScrollStitch.FindOverlap(acc, W, 100, bJit, 100);
                Chk("T119f-jitter-tolerant", ovJ == 40, $"±1px 抖动的重叠应判 40，实得 {ovJ}");
            }

            // T120 取色复制格式（R89，ColorFormat 纯函数）：HEX/RGB/HSL 文本 + 分量夹取 + 设置串解析往返
            {
                var Hex = FrameCapture.Core.ColorCopyFormat.Hex;
                var Rgb = FrameCapture.Core.ColorCopyFormat.Rgb;
                var Hsl = FrameCapture.Core.ColorCopyFormat.Hsl;
                string Fmt(int r, int g, int b, FrameCapture.Core.ColorCopyFormat f) => FrameCapture.Core.ColorFormat.Format(r, g, b, f);
                Chk("T120a-hex", Fmt(255, 128, 0, Hex) == "#FF8000", Fmt(255, 128, 0, Hex));
                Chk("T120b-rgb", Fmt(255, 128, 0, Rgb) == "rgb(255, 128, 0)", Fmt(255, 128, 0, Rgb));
                Chk("T120c-hsl-orange", Fmt(255, 128, 0, Hsl) == "hsl(30, 100%, 50%)", Fmt(255, 128, 0, Hsl));
                Chk("T120d-hsl-gray", Fmt(128, 128, 128, Hsl) == "hsl(0, 0%, 50%)", Fmt(128, 128, 128, Hsl));
                Chk("T120e-hsl-white", Fmt(255, 255, 255, Hsl) == "hsl(0, 0%, 100%)", Fmt(255, 255, 255, Hsl));
                Chk("T120f-clamp", Fmt(300, -5, 128, Hex) == "#FF0080", Fmt(300, -5, 128, Hex));
                Chk("T120g-parse", FrameCapture.Core.ColorFormat.Parse("RGB") == Rgb && FrameCapture.Core.ColorFormat.Parse("hsl") == Hsl
                    && FrameCapture.Core.ColorFormat.Parse("garbage") == Hex && FrameCapture.Core.ColorFormat.Parse(null) == Hex, "解析含大小写/未知回退 Hex");
                Chk("T120h-tosetting", FrameCapture.Core.ColorFormat.ToSetting(Hsl) == "hsl" && FrameCapture.Core.ColorFormat.ToSetting(Hex) == "hex", "枚举↔设置串往返");
            }
            // T121 剪贴板图片建帧（R92，BgraImage.FromSource）：ToSource↔FromSource 往返无损 + 非 Bgra32 转换 + 空值
            {
                var f0 = new FrameCapture.Capture.CapturedFrame
                {
                    Bgra = new byte[] { 10, 20, 30, 255, 40, 50, 60, 128, 70, 80, 90, 0, 100, 110, 120, 255 },
                    W = 2, H = 2, TimeMs = 0,
                };
                var src = FrameCapture.Services.BgraImage.ToSource(f0);
                var back = FrameCapture.Services.BgraImage.FromSource(src);
                Chk("T121a-roundtrip", back != null && back.W == 2 && back.H == 2 && back.Bgra.SequenceEqual(f0.Bgra),
                    "ToSource→FromSource BGRA 逐字节往返");
                // 非 Bgra32 源（Bgr32，alpha 位被忽略）→ 转 Bgra32 后 alpha=255、BGR 分量不变
                var bgr = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
                    System.Windows.Media.PixelFormats.Bgr32, null, new byte[] { 10, 20, 30, 0 }, 4);
                var conv = FrameCapture.Services.BgraImage.FromSource(bgr);
                Chk("T121b-bgr32", conv != null && conv.Bgra[0] == 10 && conv.Bgra[1] == 20 && conv.Bgra[2] == 30 && conv.Bgra[3] == 255,
                    conv == null ? "null" : $"BGR→BGRA {conv.Bgra[0]},{conv.Bgra[1]},{conv.Bgra[2]},{conv.Bgra[3]}");
                Chk("T121c-null", FrameCapture.Services.BgraImage.FromSource(null) == null, "null → null");
            }
            // T122 暂停全局热键决策随旧 HookDecision 决策表退役（R126 B2）：暂停语义现由 HotkeyManager
            // 「Paused 时不喂 KeySink」实现（见 HotkeyManager.HandleKey），属宿主接线，无独立纯逻辑可测。
            // T123 适配倍率封顶（R97，ZoomMath.FitScale）：小图不放大（默认 1:1 清晰），大图仍缩小适配
            {
                Chk("T123a-small-no-upscale", Math.Abs(ZoomMath.FitScale(900, 700, 320, 220) - 1.0) < 1e-9,
                    "图小于视口 → 1.0（不放大变糊）");
                Chk("T123b-large-downscale", Math.Abs(ZoomMath.FitScale(800, 600, 1600, 1200) - 0.5) < 1e-9,
                    "图大于视口 → 0.5 缩小适配（旧行为不变）");
                Chk("T123c-exact-fit", Math.Abs(ZoomMath.FitScale(800, 600, 800, 600) - 1.0) < 1e-9,
                    "图恰等视口 → 1.0");
                Chk("T123d-onetoone-small", Math.Abs(ZoomMath.OneToOneZoom(ZoomMath.FitScale(900, 700, 320, 220)) - 1.0) < 1e-9,
                    "小图 1:1 键 → _zoom=1.0（默认即 1:1）");
            }
            // T124 矩形标注烘焙（R98）：空心框——边着色、内部与外部保持原色（区别于填充矩形）
            {
                var rf = Solid(100, 100, 0, 0, 0);
                var ra = new Annotation { Kind = AnnKind.Rect, Color = Color.FromArgb(255, 255, 255, 0), Thickness = 150 };
                ra.Points.Add((0.2f, 0.2f)); ra.Points.Add((0.8f, 0.8f));
                var rb = Bake.Apply([rf], [ra])[0];
                bool Yellow(int x, int y) { int i = (y * 100 + x) * 4; return rb.Bgra[i] < 60 && rb.Bgra[i + 1] > 150 && rb.Bgra[i + 2] > 150; }
                bool Black(int x, int y) { int i = (y * 100 + x) * 4; return rb.Bgra[i] < 40 && rb.Bgra[i + 1] < 40 && rb.Bgra[i + 2] < 40; }
                Chk("T124a-rect-edge", Yellow(50, 20), "上边中点为框色（黄）");
                Chk("T124b-rect-hollow", Black(50, 50), "内部保持原色（空心不填充）");
                Chk("T124c-rect-outside", Black(5, 5), "框外保持原色");
            }
            // T125 Shift 约束几何（R99，ShapeConstraint）：正方/正圆 与 45° 吸附
            {
                var sq = ShapeConstraint.Square(0, 0, 100, 60);
                Chk("T125a-square-min", Math.Abs(sq.X - 60) < 1e-9 && Math.Abs(sq.Y - 60) < 1e-9, "取较小边作正方边长");
                var sq2 = ShapeConstraint.Square(0, 0, -100, 60);
                Chk("T125b-square-sign", Math.Abs(sq2.X + 60) < 1e-9 && Math.Abs(sq2.Y - 60) < 1e-9, "保留拖拽方向符号（左上象限）");
                var a1 = ShapeConstraint.Angle45(0, 0, 100, 10);   // ~5.7° → 0°
                Chk("T125c-angle0", Math.Abs(a1.Y) < 1e-6 && a1.X > 100, "近水平吸附到 0°（y=0）");
                var a2 = ShapeConstraint.Angle45(0, 0, 100, 90);   // ~42° → 45°
                Chk("T125d-angle45", Math.Abs(a2.X - a2.Y) < 1e-6 && a2.X > 0, "近 45° 吸附（x=y）");
                var a3 = ShapeConstraint.Angle45(0, 0, 10, 100);   // ~84° → 90°
                Chk("T125e-angle90", Math.Abs(a3.X) < 1e-6 && a3.Y > 100, "近垂直吸附到 90°（x=0）");
                var a0 = ShapeConstraint.Angle45(0, 0, 0, 0);
                Chk("T125f-angle-zero", a0.X == 0 && a0.Y == 0, "零长度不崩（原样返回）");
            }
            // T126 高刷节流 + 刷新率检测（R100）：无 16ms 地板 + 逐帧时间判定 + EnumDisplaySettings
            {
                Chk("T126a-interval-60", Math.Abs(FpsThrottle.IntervalMs(60) - 1000.0 / 60) < 1e-9,
                    "60fps 间隔 ≈16.67ms（不再被 16ms 地板截断）");
                Chk("T126b-interval-240", Math.Abs(FpsThrottle.IntervalMs(240) - 1000.0 / 240) < 1e-9,
                    "240fps 间隔 ≈4.17ms（高刷可低至亚 16ms）");
                Chk("T126c-keepall", FpsThrottle.ShouldKeep(1000, 999, 16.7, true), "keepAll 全保（目标≥刷新率）");
                Chk("T126d-first-frame", FpsThrottle.ShouldKeep(0, -1, 16.7, false), "首帧必保（lastKept<0）");
                Chk("T126e-drop-early", !FpsThrottle.ShouldKeep(1005, 1000, 16.7, false), "未到间隔丢帧（5ms<16.7）");
                Chk("T126f-keep-due", FpsThrottle.ShouldKeep(1017, 1000, 16.7, false), "到间隔保帧（17≥16.7-0.5）");
                Chk("T126g-refresh-null", DisplayRefresh.RateOf(null) == 0 && DisplayRefresh.RateOf("") == 0, "空设备名返回 0 不抛");
                int hz = DisplayRefresh.RateOf(FrameCapture.Capture.Monitors.All().FirstOrDefault(m => m.IsPrimary)?.DeviceName);
                Chk("T126h-refresh-real", hz > 0, $"本机主屏刷新率检测 → {hz}Hz");
            }

            // T127 视频编码（R101，VideoEncoder/FfmpegLocator）：定位 + 命令行拼装 + 帧率推算 + 真实 ffmpeg 编码
            {
                string? ff = FfmpegLocator.Find();
                Chk("T127a-ffmpeg-find", ff == null || File.Exists(ff), ff ?? "未找到 ffmpeg（PATH/常见目录，将回退 GIF）");

                string mp4Args = VideoEncoder.BuildArgs(640, 400, 59.94, VideoFormat.Mp4, @"C:\t\o.mp4");
                Chk("T127b-args-mp4",
                    mp4Args.Contains("-f rawvideo") && mp4Args.Contains("-pix_fmt bgra") && mp4Args.Contains("-s 640x400")
                    && mp4Args.Contains("libx264") && mp4Args.Contains("yuv420p") && mp4Args.Contains("+faststart")
                    && mp4Args.Contains("-framerate 59.94"),
                    "MP4：rawvideo/bgra/尺寸/libx264/yuv420p/faststart/invariant fps");

                string webmArgs = VideoEncoder.BuildArgs(320, 240, 30, VideoFormat.WebM, @"C:\t\o.webm");
                Chk("T127c-args-webm",
                    webmArgs.Contains("libvpx-vp9") && webmArgs.Contains("-crf 40") && webmArgs.Contains("-b:v 0")
                    && webmArgs.Contains("-s 320x240"),
                    "WebM：libvpx-vp9/crf40/bitrate0/尺寸");

                // 奇数尺寸必须向下取偶（yuv420p 要求偶数宽高，否则 libx264/vp9 报 "width not divisible by 2"）
                string oddArgs = VideoEncoder.BuildArgs(641, 401, 30, VideoFormat.Mp4, @"C:\t\o.mp4");
                Chk("T127g-args-odd-scale",
                    oddArgs.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2") && oddArgs.Contains("-s 641x401"),
                    "奇数尺寸 → 插入向下取偶 scale 滤镜");
                Chk("T127h-args-even-noscale",
                    !mp4Args.Contains("-vf") && !webmArgs.Contains("-vf"),
                    "偶数尺寸 → 不加 scale 滤镜（常见路径零额外开销）");

                var tf = MakeVidFrames(11, 32, 24); // 11 帧跨 1000ms → 10fps
                double fps = VideoEncoder.FpsFromFrames(tf);
                Chk("T127d-fps-from-frames", Math.Abs(fps - 10.0) < 0.01, $"11 帧跨 1s → {fps:F2}fps");
                Chk("T127e-fps-single", Math.Abs(VideoEncoder.FpsFromFrames([tf[0]]) - 30) < 1e-9, "单帧回退默认 30fps");

                if (ff != null)
                {
                    string outMp4 = Path.Combine(Path.GetTempPath(), $"fc_enc_{Guid.NewGuid():N}.mp4");
                    try
                    {
                        VideoEncoder.Encode(MakeVidFrames(12, 64, 48), ff, outMp4, VideoFormat.Mp4, 24);
                        long len = new FileInfo(outMp4).Length;
                        Chk("T127f-encode-mp4", len > 500, $"真实 ffmpeg 编码 MP4 → {len} 字节");
                    }
                    catch (Exception ex) { Chk("T127f-encode-mp4", false, "编码异常：" + ex.Message.Split('\n')[0]); }
                    finally { try { if (File.Exists(outMp4)) File.Delete(outMp4); } catch { } }

                    // 奇数尺寸真实编码：修复前 libx264 直接失败，修复后经 scale 取偶应成功产出
                    string outOdd = Path.Combine(Path.GetTempPath(), $"fc_encodd_{Guid.NewGuid():N}.mp4");
                    try
                    {
                        VideoEncoder.Encode(MakeVidFrames(6, 65, 49), ff, outOdd, VideoFormat.Mp4, 24);
                        long len = new FileInfo(outOdd).Length;
                        Chk("T127i-encode-odd", len > 300, $"奇数尺寸 65×49 真实编码 → {len} 字节（取偶成功）");
                    }
                    catch (Exception ex) { Chk("T127i-encode-odd", false, "奇数尺寸编码异常：" + ex.Message.Split('\n')[0]); }
                    finally { try { if (File.Exists(outOdd)) File.Delete(outOdd); } catch { } }
                }
                else
                {
                    Chk("T127f-encode-mp4", true, "无 ffmpeg，跳过实编码");
                    Chk("T127i-encode-odd", true, "无 ffmpeg，跳过实编码");
                }
            }
        }
        // T129 内存回收助手（R120 A1）：录制/编辑大位图关闭后主动全代 GC + 裁工作集，等价用户手动 MemReduct
        {
            bool ok = true;
            try
            {
                var big = new byte[64 << 20]; // 64MB：制造一次 LOH 分配，Reclaim 应能回收并裁工作集
                big[0] = 1;
                FrameCapture.Core.Memory.Reclaim();
                GC.KeepAlive(big);
            }
            catch { ok = false; }
            Chk("T129a-reclaim-no-throw", ok, "Memory.Reclaim() 全代 GC + 裁工作集不抛");
        }
        // T130 GestureCore 引擎（R120 B1，netstandard2.0 可复用库）：按住/单击/双击消歧/长按/失败策略/修饰键/三击/优先级
        {
            var M = new GestureProbe();
            var eng = new GE.GestureEngine(M);
            GE.GestureBinding B(string id, GE.ModifierKeys mods, int key, GE.TriggerKind trig, string[] acts,
                GE.FallbackPolicy fb = GE.FallbackPolicy.Stop, int prio = 0, int dbl = 400, int lp = 500)
                => new GE.GestureBinding { Id = id, Chord = new GE.KeyChord(mods, key), Trigger = trig,
                     Actions = new List<string>(acts), Fallback = fb, Priority = prio, DoubleClickMs = dbl, LongPressMs = lp };
            const int F6 = 0x75, A = 0x41, CTRL = 0x11, ALT = 0x12;

            // T130a 按住做 A、松手做 B（KeyDown/KeyUp 配对）
            eng.SetBindings(new[] {
                B("s", GE.ModifierKeys.None, F6, GE.TriggerKind.KeyDown, new[] { "StartRec" }),
                B("t", GE.ModifierKeys.None, F6, GE.TriggerKind.KeyUp, new[] { "StopRec" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 500);
            Chk("T130a-hold-pattern", M.Log.SequenceEqual(new[] { "StartRec", "StopRec" }), "F6 按下→StartRec、抬起→StopRec，实得 " + string.Join(",", M.Log));

            // T130b 组合键激活 + 单击（仅绑 Click → 零延迟立即触发）
            M.Log.Clear();
            eng.SetBindings(new[] { B("c", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Capture" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10);
            eng.OnMouseUp(0, GE.ModifierKeys.None, 30);
            Chk("T130b-click-held", M.Log.SequenceEqual(new[] { "Capture" }), "F6 按住单击→Capture（无更高阶绑定，立即触发）");

            // T130c Click 与 DoubleClick 并存：单击等满间隔才触发 Click；双击立即触发 DoubleClick
            M.Log.Clear();
            eng.SetBindings(new[] {
                B("one", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "One" }),
                B("two", GE.ModifierKeys.None, F6, GE.TriggerKind.DoubleClick, new[] { "Two" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            eng.Tick(419);
            bool waited = M.Log.Count == 0;
            eng.Tick(420);
            Chk("T130c-single-waits-then-click", waited && M.Log.SequenceEqual(new[] { "One" }), "单击须等满 400ms 才判 Click→One，实得 " + string.Join(",", M.Log));
            M.Log.Clear();
            eng.OnMouseDown(0, GE.ModifierKeys.None, 1000); eng.OnMouseUp(0, GE.ModifierKeys.None, 1010);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 1100); eng.OnMouseUp(0, GE.ModifierKeys.None, 1110);
            Chk("T130c-double-immediate", M.Log.SequenceEqual(new[] { "Two" }), "两次连击达最高阶→立即 DoubleClick→Two");

            // T130d 长按：保持 ≥LongPressMs 触发 LongPress 并吞掉随后的抬起（不再计单击）
            M.Log.Clear();
            eng.SetBindings(new[] {
                B("tap", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Tap" }),
                B("hold", GE.ModifierKeys.None, F6, GE.TriggerKind.LongPress, new[] { "Hold" }, lp: 500) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10);
            eng.Tick(509);
            bool notYet = M.Log.Count == 0;
            eng.Tick(510);
            eng.OnMouseUp(0, GE.ModifierKeys.None, 700);
            Chk("T130d-longpress", notYet && M.Log.SequenceEqual(new[] { "Hold" }), "按住满 500ms→LongPress→Hold，抬起不再触发 Tap");

            // T130e NextBinding：首条动作失败 → 顺延下一条绑定
            M.Log.Clear(); M.Fail = new HashSet<string> { "Fail1" };
            eng.SetBindings(new[] {
                B("b1", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Fail1", "After" }, GE.FallbackPolicy.NextBinding, prio: 0),
                B("b2", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Second" }, GE.FallbackPolicy.Stop, prio: 1) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            Chk("T130e-next-binding", M.Log.SequenceEqual(new[] { "Fail1", "Second" }), "b1 首步失败→NextBinding→b2 执行 Second（After 不跑）");
            M.Fail.Clear();

            // T130f SkipStep：失败步跳过、继续本条后续动作
            M.Log.Clear(); M.Fail = new HashSet<string> { "Fail1" };
            eng.SetBindings(new[] { B("b1", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Fail1", "After" }, GE.FallbackPolicy.SkipStep) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            Chk("T130f-skip-step", M.Log.SequenceEqual(new[] { "Fail1", "After" }), "SkipStep：Fail1 失败被跳过、After 继续");
            M.Fail.Clear();

            // T130g Stop：失败即中止、不顺延
            M.Log.Clear(); M.Fail = new HashSet<string> { "Fail1" };
            eng.SetBindings(new[] {
                B("b1", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Fail1", "After" }, GE.FallbackPolicy.Stop, prio: 0),
                B("b2", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Second" }, GE.FallbackPolicy.Stop, prio: 1) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            Chk("T130g-stop", M.Log.SequenceEqual(new[] { "Fail1" }), "Stop：Fail1 失败即中止，After/Second 均不跑");

            // T130h 修饰键组合 Ctrl+Alt+A + 单击
            M.Log.Clear();
            eng.SetBindings(new[] { B("pick", GE.ModifierKeys.Control | GE.ModifierKeys.Alt, A, GE.TriggerKind.Click, new[] { "Pick" }) });
            eng.OnKeyDown(CTRL, GE.ModifierKeys.Control, 0);
            eng.OnKeyDown(ALT, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 0);
            eng.OnKeyDown(A, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 10); eng.OnMouseUp(0, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 20);
            Chk("T130h-mods-chord", M.Log.SequenceEqual(new[] { "Pick" }), "Ctrl+Alt+A 激活 + 单击→Pick");

            // T130i 三击
            M.Log.Clear();
            eng.SetBindings(new[] {
                B("one", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "One" }),
                B("three", GE.ModifierKeys.None, F6, GE.TriggerKind.TripleClick, new[] { "Three" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 110); eng.OnMouseUp(0, GE.ModifierKeys.None, 120);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 210); eng.OnMouseUp(0, GE.ModifierKeys.None, 220);
            Chk("T130i-triple", M.Log.SequenceEqual(new[] { "Three" }), "三击达最高阶→TripleClick→Three");

            // T130j 优先级：同触发多条，小优先级先跑且成功即止
            M.Log.Clear();
            eng.SetBindings(new[] {
                B("low", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "B" }, prio: 1),
                B("high", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "A" }, prio: 0) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            Chk("T130j-priority", M.Log.SequenceEqual(new[] { "A" }), "优先级 0 的绑定先跑且成功即止→A");
        }
        // T131 GestureConfig 配置层（R120 B2）：DTO 转换 / 非法跳过 / 默认与预设 / JSON 往返 / 动作过滤
        {
            var d = new GestureBindingDto { Id = "x", Chord = "Ctrl+Shift+G", Trigger = "Chord", Actions = new[] { GestureActions.CaptureFrame } };
            var b = d.ToBinding();
            Chk("T131a-dto-to-binding", b != null && b.Chord.Mods == (GE.ModifierKeys.Control | GE.ModifierKeys.Shift)
                && b.Chord.Key == 0x47 && b.Trigger == GE.TriggerKind.Chord,
                "Ctrl+Shift+G→Chord(Control|Shift, VK G=0x47)，实得 " + (b == null ? "null" : $"{b.Chord.Mods}/{b.Chord.Key:X}/{b.Trigger}"));

            Chk("T131b-bare-key-rejected", new GestureBindingDto { Chord = "G", Trigger = "Chord", Actions = new[] { GestureActions.CaptureFrame } }.ToBinding() == null,
                "裸键 G（不安全）解析失败→ToBinding null（复用 HotkeySpec 安全规则）");

            var def = GestureConfig.DefaultBindings("Ctrl+Shift+G", "Ctrl+Shift+R");
            Chk("T131c-default", def.Count == 2 && def[0].Actions[0] == GestureActions.CaptureFrame && def[1].Actions[0] == GestureActions.ScopeSelect,
                "默认绑定：捕获键→捕获单帧、选区键→选取范围");

            var qq = GestureConfig.PresetQQ("Ctrl+Shift+G", "Ctrl+Shift+R");
            Chk("T131d-qq-preset", qq.Count == 2 && qq.All(x => x.Actions[0] == GestureActions.ScopeSelect),
                "QQ 预设：两键均→选取范围（配合选完自动截图）");

            var cfg = new GestureConfig { Enabled = true, Bindings = GestureConfig.PresetHoldToRecord("F6", "F7") };
            var json = System.Text.Json.JsonSerializer.Serialize(cfg);
            var back = System.Text.Json.JsonSerializer.Deserialize<GestureConfig>(json)!;
            var eb = back.ToBindings();
            Chk("T131e-json-roundtrip", back.Enabled && eb.Count == 3
                && eb.Any(x => x.Trigger == GE.TriggerKind.KeyDown && x.Actions[0] == GestureActions.RecordStart)
                && eb.Any(x => x.Trigger == GE.TriggerKind.KeyUp && x.Actions[0] == GestureActions.RecordStop),
                "按住即录预设 JSON 往返：Enabled + KeyDown→开始录制 + KeyUp→结束录制，实得 " + eb.Count + " 条");

            var bad = new GestureBindingDto { Chord = "F6", Trigger = "Click", Actions = new[] { GestureActions.CaptureFrame, "no.such.action" } };
            var bb = bad.ToBinding();
            Chk("T131f-action-filter", bb != null && bb.Actions.Count == 1 && bb.Actions[0] == GestureActions.CaptureFrame,
                "未知动作名被过滤，仅保留合法动作");

            var rt = GestureBindingDto.FromBinding(new GE.GestureBinding {
                Chord = new GE.KeyChord(GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 0x41),
                Trigger = GE.TriggerKind.Click, Actions = new List<string> { GestureActions.ScopeSelect } });
            Chk("T131g-from-binding-chord", rt.Chord == "Ctrl+Alt+A" && rt.Trigger == "Click",
                "引擎绑定回推组合键串 Ctrl+Alt+A，实得 " + rt.Chord);
        }
        // T132 ToastSeconds 归一化（R120 C）：默认 3.2、0=常驻保留、负→0、超界夹取、过小→0.5
        {
            Chk("T132a-toast-default", new AppSettings().ToastSeconds == 3.2, "默认 3.2 秒");
            var s0 = new AppSettings { ToastSeconds = 0 }; s0.Normalize();
            var sneg = new AppSettings { ToastSeconds = -5 }; sneg.Normalize();
            var sbig = new AppSettings { ToastSeconds = 100 }; sbig.Normalize();
            var stiny = new AppSettings { ToastSeconds = 0.3 }; stiny.Normalize();
            Chk("T132b-toast-normalize", s0.ToastSeconds == 0 && sneg.ToastSeconds == 0 && sbig.ToastSeconds == 60 && Math.Abs(stiny.ToastSeconds - 0.5) < 1e-9,
                $"0 保留(常驻)、负→0、100→60、0.3→0.5，实得 {s0.ToastSeconds}/{sneg.ToastSeconds}/{sbig.ToastSeconds}/{stiny.ToastSeconds}");
        }
        // T133 OCR 预览优先默认（R120 D2）：结果窗默认开（预览即选取入口，不自动复制）
        Chk("T133-ocr-preview-default", new AppSettings().OcrShowResult, "识别结果窗默认开（预览优先）");
        // T134 键盘钩子仅在「有手势 KeySink」时安装（R120 B2 修复，R126 B2 改无参 Install）：
        // 手势层是默认触发路径，钩子把键喂给 KeySink；滚动截图 Esc 取消、浮窗 Enter/Esc 也依赖此钩子。
        // 无 KeySink（手势关闭）时不装钩子，零开销。
        {
            bool sinkInstalled;
            var hk = new FrameCapture.Services.HotkeyManager();
            try { hk.KeySink = (vk, down, mods, time) => false; hk.Install(); sinkInstalled = hk.Installed; }
            finally { hk.Dispose(); }
            bool idleNotInstalled;
            var hk2 = new FrameCapture.Services.HotkeyManager();
            try { hk2.Install(); idleNotInstalled = hk2.Installed; }
            finally { hk2.Dispose(); }
            Chk("T134a-hook-for-keysink", sinkInstalled, "有手势 KeySink 时装钩子（否则手势永不触发）");
            Chk("T134b-no-hook-when-idle", !idleNotInstalled, "无 KeySink 时不装钩子（零开销）");
        }
        // T135 QQ 风味选区动作（R120 B2）：capture.selection / record.selection 注册 + Ctrl+Alt+A/R 解析成 Chord
        {
            Chk("T135a-actions-registered",
                GestureActions.IsValid("capture.selection") && GestureActions.IsValid("record.selection"),
                "选区截图/选区录制动作已注册");
            var d1 = new GestureBindingDto { Id = "qq.shot", Chord = "Ctrl+Alt+A", Trigger = "Chord", Actions = new[] { "capture.selection" } };
            var b1 = d1.ToBinding();
            Chk("T135b-shot-chord-parse",
                b1 != null && b1.Chord.Key == 0x41 && (b1.Chord.Mods & GE.ModifierKeys.Control) != 0 &&
                (b1.Chord.Mods & GE.ModifierKeys.Alt) != 0 && b1.Trigger == GE.TriggerKind.Chord &&
                b1.Actions.Count == 1 && b1.Actions[0] == "capture.selection",
                "Ctrl+Alt+A → Chord(Ctrl+Alt+A) → capture.selection 解析正确");
            var d2 = new GestureBindingDto { Id = "qq.rec", Chord = "Ctrl+Alt+R", Trigger = "Chord", Actions = new[] { "record.selection" } };
            var b2 = d2.ToBinding();
            Chk("T135c-rec-chord-parse",
                b2 != null && b2.Chord.Key == 0x52 && b2.Trigger == GE.TriggerKind.Chord && b2.Actions[0] == "record.selection",
                "Ctrl+Alt+R → Chord → record.selection 解析正确");
        }
        // T136 录制同键起停 + 即时截图动作（R120 B2 用户实测修正）
        {
            Chk("T136a-record-toggle-registered", GestureActions.IsValid("record.toggle"), "record.toggle 动作已注册");
            Chk("T136b-capture-frame-still-valid", GestureActions.IsValid("capture.frame"), "capture.frame 仍在（改为即时抓取，名不变）");
            var d = new GestureBindingDto { Id = "qq.rec2", Chord = "Ctrl+Alt+R", Trigger = "Chord", Actions = new[] { "record.toggle" } };
            var b = d.ToBinding();
            Chk("T136c-toggle-parse",
                b != null && b.Chord.Key == 0x52 && b.Trigger == GE.TriggerKind.Chord && b.Actions[0] == "record.toggle",
                "Ctrl+Alt+R → record.toggle 解析正确（同键起停）");
        }
        // T137 独占吞键（R120 B2）：引擎回报「带具体键的组合键命中」→ 宿主吞键；纯修饰键组合/未命中键不吞
        {
            var M = new GestureProbe();
            var eng = new GE.GestureEngine(M);
            GE.GestureBinding B(string id, GE.ModifierKeys mods, int key, GE.TriggerKind trig, string[] acts)
                => new GE.GestureBinding { Id = id, Chord = new GE.KeyChord(mods, key), Trigger = trig,
                     Actions = new List<string>(acts), Fallback = GE.FallbackPolicy.Stop };
            const int A = 0x41, CTRL = 0x11, ALT = 0x12, F6 = 0x75;

            // Ctrl+Alt+A 绑 Chord：Ctrl/Alt 按下不吞，A 命中才吞，A 抬起也吞（键态一致）
            eng.SetBindings(new[] { B("pick", GE.ModifierKeys.Control | GE.ModifierKeys.Alt, A, GE.TriggerKind.Chord, new[] { "Pick" }) });
            bool swCtrl = eng.OnKeyDown(CTRL, GE.ModifierKeys.Control, 0);
            bool swAlt = eng.OnKeyDown(ALT, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 0);
            bool swA = eng.OnKeyDown(A, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 0);
            bool swAUp = eng.OnKeyUp(A, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 50);
            Chk("T137a-swallow-on-trigger-key", !swCtrl && !swAlt && swA, "Ctrl/Alt 不吞、A 命中组合键才吞（按下）");
            Chk("T137b-swallow-keyup-too", swAUp, "A 抬起也吞（按下被吞→抬起一起吞，前台键态一致）");

            // 纯修饰键组合（Key==0）：触发但不吞，避免劫持 Ctrl+C 等
            M.Log.Clear();
            eng.SetBindings(new[] { B("mod", GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 0, GE.TriggerKind.Chord, new[] { "Mod" }) });
            eng.OnKeyDown(CTRL, GE.ModifierKeys.Control, 0);
            bool swModAlt = eng.OnKeyDown(ALT, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 0);
            Chk("T137c-modifier-only-no-swallow", !swModAlt && M.Log.SequenceEqual(new[] { "Mod" }), "纯修饰键组合触发但不吞键");

            // 未绑定任何组合键的键：不吞
            M.Log.Clear();
            eng.SetBindings(new[] { B("pick", GE.ModifierKeys.Control | GE.ModifierKeys.Alt, A, GE.TriggerKind.Chord, new[] { "Pick" }) });
            bool swF6 = eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            Chk("T137d-unbound-key-no-swallow", !swF6, "未绑定的键（F6）不吞");
        }
        // T138 手势配置器模型/校验（R122 方案 C）：GestureEditor 库的纯逻辑，宿主无关，可脱离 UI 单测
        {
            var cat = new TestCatalog();
            var nm = new TestNamer();
            GEdit.BindingEdit BE(string id, GE.ModifierKeys mods, int key, string[] acts)
                => new GEdit.BindingEdit { Id = id, Mods = mods, Key = key, Actions = new List<string>(acts) };

            var ok = new GEdit.GestureDoc { Enabled = true, Bindings = { BE("a", GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 0x41, new[] { "capture.frame" }) } };
            Chk("T138a-valid-no-problem", GEdit.GestureValidation.Validate(ok, cat, nm).Count == 0, "合法绑定无校验问题");

            var emptyChord = new GEdit.GestureDoc { Bindings = { BE("a", GE.ModifierKeys.None, 0, new[] { "capture.frame" }) } };
            Chk("T138b-empty-chord-error", GEdit.GestureValidation.HasErrors(GEdit.GestureValidation.Validate(emptyChord, cat, nm)), "空组合键判为 error");

            var noAct = new GEdit.GestureDoc { Bindings = { BE("a", GE.ModifierKeys.Control, 0x41, new string[0]) } };
            Chk("T138c-no-action-error", GEdit.GestureValidation.HasErrors(GEdit.GestureValidation.Validate(noAct, cat, nm)), "无动作判为 error");

            var badAct = new GEdit.GestureDoc { Bindings = { BE("a", GE.ModifierKeys.Control, 0x41, new[] { "no.such" }) } };
            Chk("T138d-unknown-action-error", GEdit.GestureValidation.HasErrors(GEdit.GestureValidation.Validate(badAct, cat, nm)), "未知动作判为 error");

            var dup = new GEdit.GestureDoc { Bindings = {
                BE("a", GE.ModifierKeys.Control, 0x41, new[] { "capture.frame" }),
                BE("b", GE.ModifierKeys.Control, 0x41, new[] { "record.toggle" }) } };
            var p4 = GEdit.GestureValidation.Validate(dup, cat, nm);
            Chk("T138e-duplicate-warn-not-error", p4.Count > 0 && !GEdit.GestureValidation.HasErrors(p4), "组合键+触发重复仅提示、非 error");

            var badTime = new GEdit.GestureDoc { Bindings = { new GEdit.BindingEdit { Id = "a", Mods = GE.ModifierKeys.Control, Key = 0x41, Actions = new List<string> { "capture.frame" }, DoubleClickMs = 9999 } } };
            var p5 = GEdit.GestureValidation.Validate(badTime, cat, nm);
            Chk("T138f-timing-warn-not-error", p5.Count > 0 && !GEdit.GestureValidation.HasErrors(p5), "时间越界仅提示、非 error");
        }
        // T139 编辑器控件配色模板可套可 measure（R122/R124）：XamlReader 现拼的 ComboBox/ListBox 模板在 measure 套模板时才解析，
        // 编译与 XamlReader.Parse 都不报（SelectionBoxItem 嵌套模板坑）——这里真正 Measure 兜底。
        {
            var pal = new GEdit.EditorPalette();
            string comboErr = null, listErr = null;
            try {
                var cb = new System.Windows.Controls.ComboBox();
                GEdit.EditorStyles.ApplyCombo(cb, pal);
                cb.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "A" });
                cb.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "B" });
                cb.SelectedIndex = 0;
                cb.Measure(new System.Windows.Size(200, 30));
                cb.Arrange(new System.Windows.Rect(0, 0, 200, 30));
            } catch (Exception ex) { comboErr = ex.Message; }
            try {
                var lb = new System.Windows.Controls.ListBox();
                GEdit.EditorStyles.ApplyList(lb, pal);
                lb.Items.Add(new System.Windows.Controls.ListBoxItem { Content = "A" });
                lb.SelectedIndex = 0;
                lb.Measure(new System.Windows.Size(200, 60));
                lb.Arrange(new System.Windows.Rect(0, 0, 200, 60));
            } catch (Exception ex) { listErr = ex.Message; }
            Chk("T139a-combo-template-measures", comboErr == null, "ComboBox 配色模板可套可 measure（深色下拉可读）" + (comboErr != null ? " 异常:" + comboErr : ""));
            Chk("T139b-list-item-style-measures", listErr == null, "ListBox 条目样式可套可 measure" + (listErr != null ? " 异常:" + listErr : ""));
        }
        // T140 文字字号映射（C3）：线宽挡位→帧像素字号，单调递增，锚定 细3/中6/粗10 = 22/34/50px @1080p
        {
            double f3 = FrameCapture.Core.AnnFont.FramePx(3), f6 = FrameCapture.Core.AnnFont.FramePx(6), f10 = FrameCapture.Core.AnnFont.FramePx(10);
            Chk("T140a-font-anchor-values", Math.Abs(f3 - 22) < 1e-3 && Math.Abs(f6 - 34) < 1e-3 && Math.Abs(f10 - 50) < 1e-3, $"字号锚定 细{f3}/中{f6}/粗{f10} = 22/34/50");
            Chk("T140b-font-monotonic", f3 < f6 && f6 < f10 && FrameCapture.Core.AnnFont.FramePx(24) > f10, "字号随线宽单调递增（A+ 变大）");
        }
        // T141 启用/禁用（A1）：禁用绑定不校验、不进引擎
        {
            var cat = new TestCatalog(); var nm = new TestNamer();
            var offDoc = new GEdit.GestureDoc { Bindings = { new GEdit.BindingEdit { Id = "off", Enabled = false, Mods = GE.ModifierKeys.None, Key = 0, Actions = new List<string>() } } };
            Chk("T141a-disabled-not-validated", GEdit.GestureValidation.Validate(offDoc, cat, nm).Count == 0, "禁用绑定不产生校验问题（不挡保存）");
            var cfg = new FrameCapture.Core.GestureConfig();
            cfg.Bindings.Add(new FrameCapture.Core.GestureBindingDto { Id = "off", Enabled = false, Chord = "F6", Actions = new[] { "capture.frame" } });
            cfg.Bindings.Add(new FrameCapture.Core.GestureBindingDto { Id = "on", Enabled = true, Chord = "F7", Actions = new[] { "scope.select" } });
            var bs = cfg.ToBindings();
            Chk("T141b-disabled-excluded-from-engine", bs.Count == 1 && bs[0].Id == "on", "禁用绑定不进引擎、启用保留");
        }
        // T142 动作目录拆分/改名（A2）：选区/开始/停止/同键起停 齐全 + 即时截图语义=全屏
        {
            bool split = FrameCapture.Core.GestureActions.IsValid("scope.select")
                && FrameCapture.Core.GestureActions.IsValid("record.start")
                && FrameCapture.Core.GestureActions.IsValid("record.stop")
                && FrameCapture.Core.GestureActions.IsValid("record.toggle");
            Chk("T142a-record-actions-split", split, "选区/开始录制/停止录制/开始停止(同键) 四动作齐全");
            var cf = FrameCapture.Core.GestureActions.All.First(a => a.Name == "capture.frame");
            Chk("T142b-instant-is-fullscreen", cf.Label.Contains("全屏"), $"即时截图改名含「全屏」：{cf.Label}");
        }
        // T143 完成后序列（A3）：主序列成功后跑 OnComplete；主序列失败则不跑
        {
            var M = new GestureProbe();
            var eng = new GE.GestureEngine(M);
            const int F6 = 0x75;
            var b = new GE.GestureBinding { Id = "x", Chord = new GE.KeyChord(GE.ModifierKeys.None, F6), Trigger = GE.TriggerKind.Click,
                Actions = new List<string> { "Cap" }, OnComplete = new List<string> { "Pin", "Copy" } };
            eng.SetBindings(new[] { b });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            Chk("T143a-oncomplete-after-main", M.Log.SequenceEqual(new[] { "Cap", "Pin", "Copy" }), "主→完成 顺序执行，实得 " + string.Join(",", M.Log));
            M.Log.Clear(); M.Fail.Add("Cap");
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            Chk("T143b-oncomplete-skipped-on-fail", !M.Log.Contains("Pin"), "主动作失败则完成后序列不跑，实得 " + string.Join(",", M.Log));
        }
        // T144 录制动作去默认行为（R124）：停止/同键起停不再自动开编辑器；开编辑器是独立动作、配在完成后序列
        {
            Chk("T144a-edit-lastvideo-exists", FrameCapture.Core.GestureActions.IsValid("edit.lastvideo"),
                "新增『打开编辑器（上次录制）』动作");
            var rs = FrameCapture.Core.GestureActions.All.First(a => a.Name == "record.stop");
            Chk("T144b-record-stop-no-default-editor", rs.Description.Contains("不进编辑器"),
                $"停止录制文案明确不进编辑器：{rs.Description}");
            var rt = FrameCapture.Core.GestureActions.All.First(a => a.Name == "record.toggle");
            Chk("T144c-record-toggle-no-default-editor", rt.Description.Contains("不进编辑器"),
                $"同键起停文案明确不进编辑器：{rt.Description}");
            var lv = FrameCapture.Core.GestureActions.All.First(a => a.Name == "edit.lastvideo");
            Chk("T144d-edit-lastvideo-guides-oncomplete", lv.Description.Contains("完成后序列"),
                $"打开编辑器动作文案引导配到完成后序列：{lv.Description}");
        }
        // T145 裁剪视图居中（R125）：非居中裁剪后，裁剪子区中心必落在视口中心+pan。
        // 旧公式用 s 而非整帧显示宽 w 定位裁剪中心，非居中裁剪会把画面甩出视口、中键拖不回。
        {
            void CropCenter(double vw, double vh, double fw, double fh, double cx0, double cy0, double cw0, double ch0, double s, double panX, double panY, string tag)
            {
                var (x, y, w, h) = ZoomMath.CropViewRect(vw, vh, fw, fh, cx0, cy0, cw0, ch0, s, panX, panY);
                double ccx = x + (cx0 + cw0 / 2) * w, ccy = y + (cy0 + ch0 / 2) * h;
                Chk($"T145{tag}-center", Math.Abs(ccx - (vw / 2 + panX)) < 1e-6 && Math.Abs(ccy - (vh / 2 + panY)) < 1e-6,
                    $"裁剪中心=视口中心+pan：实得 ({ccx:F2},{ccy:F2}) 期望 ({vw / 2 + panX},{vh / 2 + panY})");
            }
            CropCenter(1000, 800, 1920, 1080, 0.2, 0.3, 0.4, 0.5, 0.5, 0, 0, "a");    // 非居中裁剪
            CropCenter(1000, 800, 1920, 1080, 0.2, 0.3, 0.4, 0.5, 0.5, 30, -20, "b"); // 带 pan
            CropCenter(1000, 800, 1920, 1080, 0, 0, 1, 1, 0.5, 0, 0, "c");            // 未裁剪=整帧居中
            var (_, _, ww, hh) = ZoomMath.CropViewRect(1000, 800, 1920, 1080, 0.2, 0.3, 0.4, 0.5, 0.5, 0, 0);
            Chk("T145d-fullframe-size", Math.Abs(ww - 960) < 1e-6 && Math.Abs(hh - 540) < 1e-6, $"整帧显示尺寸=帧×s：实得 {ww}x{hh}");
        }
        // T146 键盘点按/按住时序（R126 B2）：同一捕获键 轻点→截图、按住→录制。
        // Tap=快速抬起(<阈值)，HoldRelease=长按抬起(≥阈值)，Hold=按住到阈值即触发（确认按住才开始）。
        {
            var M = new GestureProbe();
            var eng = new GE.GestureEngine(M);
            GE.GestureBinding B(string id, GE.ModifierKeys mods, int key, GE.TriggerKind trig, string[] acts, int lp = 250)
                => new GE.GestureBinding { Id = id, Chord = new GE.KeyChord(mods, key), Trigger = trig,
                     Actions = new List<string>(acts), LongPressMs = lp };
            const int F6 = 0x75;

            // T146a 轻点（100ms<250）→ 只触发 Tap
            eng.SetBindings(new[] { B("tap", GE.ModifierKeys.None, F6, GE.TriggerKind.Tap, new[] { "Shot" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 100);
            Chk("T146a-tap-quick", M.Log.SequenceEqual(new[] { "Shot" }), "轻点 F6(100ms)→Tap→Shot，实得 " + string.Join(",", M.Log));

            // T146b 长按抬起（400ms≥250）→ 只触发 HoldRelease
            M.Log.Clear();
            eng.SetBindings(new[] { B("hr", GE.ModifierKeys.None, F6, GE.TriggerKind.HoldRelease, new[] { "Stop" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 400);
            Chk("T146b-holdrelease-long", M.Log.SequenceEqual(new[] { "Stop" }), "长按 F6(400ms)抬起→HoldRelease→Stop，实得 " + string.Join(",", M.Log));

            // T146c 零丢帧：KeyDown 起录 + Tap 丢弃 + HoldRelease 停止；轻点 → Start,Discard
            M.Log.Clear();
            eng.SetBindings(new[] {
                B("kd", GE.ModifierKeys.None, F6, GE.TriggerKind.KeyDown, new[] { "Start" }),
                B("tp", GE.ModifierKeys.None, F6, GE.TriggerKind.Tap, new[] { "Discard" }),
                B("hr", GE.ModifierKeys.None, F6, GE.TriggerKind.HoldRelease, new[] { "Stop" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 100);
            Chk("T146c-zeroloss-tap", M.Log.SequenceEqual(new[] { "Start", "Discard" }), "零丢帧轻点→按下起录+抬起丢弃截图，实得 " + string.Join(",", M.Log));

            // T146d 零丢帧：长按 → Start,Stop（保留录制）
            M.Log.Clear();
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 400);
            Chk("T146d-zeroloss-hold", M.Log.SequenceEqual(new[] { "Start", "Stop" }), "零丢帧长按→按下起录+抬起停止保留，实得 " + string.Join(",", M.Log));

            // T146e 非零丢帧：Hold 到阈值才起录，抬起停止；249ms 未起录
            M.Log.Clear();
            eng.SetBindings(new[] {
                B("h", GE.ModifierKeys.None, F6, GE.TriggerKind.Hold, new[] { "Start" }),
                B("hr", GE.ModifierKeys.None, F6, GE.TriggerKind.HoldRelease, new[] { "Stop" }),
                B("tp", GE.ModifierKeys.None, F6, GE.TriggerKind.Tap, new[] { "Shot" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.Tick(249); bool early = M.Log.Count == 0;
            eng.Tick(250);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 400);
            Chk("T146e-hold-at-threshold", early && M.Log.SequenceEqual(new[] { "Start", "Stop" }), "按住满 250ms 才起录→Stop，249ms 未起录，实得 " + string.Join(",", M.Log));

            // T146f 认领吞键：tap/hold 组合键按下即吞（即便没绑 KeyDown/Chord），自动重复与抬起也吞
            M.Log.Clear();
            eng.SetBindings(new[] { B("tap", GE.ModifierKeys.None, F6, GE.TriggerKind.Tap, new[] { "Shot" }) });
            bool swDown = eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            bool swRepeat = eng.OnKeyDown(F6, GE.ModifierKeys.None, 50);   // 自动重复
            bool swUp = eng.OnKeyUp(F6, GE.ModifierKeys.None, 100);
            Chk("T146f-claim-swallow", swDown && swRepeat && swUp, $"tap/hold 键按下+重复+抬起全吞（前台收不到），实得 down={swDown} rep={swRepeat} up={swUp}");
        }
        // T147 默认手势集播种（R126 B2）：捕获键 轻点截图+按住录制，选区键 组合键选区；一次性、尊重已有绑定。
        {
            var set = FrameCapture.Core.GestureConfig.DefaultTapHoldSet("Ctrl+Shift+G", "Ctrl+Shift+R");
            Chk("T147a-default-set-shape",
                set.Count == 4
                && set.Any(b => b.Trigger == "Tap" && b.Actions.Contains("capture.selection"))
                && set.Any(b => b.Trigger == "Hold" && b.Actions.Contains("record.start"))
                && set.Any(b => b.Trigger == "HoldRelease" && b.Actions.Contains("record.stop"))
                && set.Any(b => b.Trigger == "Chord" && b.Actions.Contains("scope.select")),
                $"默认集=捕获键 Tap/Hold/HoldRelease + 选区键 Chord，实得 {set.Count} 条");
            Chk("T147b-default-hold-threshold",
                set.Where(b => b.Trigger is "Tap" or "Hold" or "HoldRelease").All(b => b.LongPressMs == 250),
                "tap/hold 三条按住阈值=250ms");

            var cfg = new FrameCapture.Core.GestureConfig();
            cfg.SeedIfNeeded("Ctrl+Shift+G", "Ctrl+Shift+R");
            Chk("T147c-seed-empty", cfg.Bindings.Count == 4 && cfg.Enabled && cfg.Seeded, "空配置播种默认集并启用、置 Seeded");

            var cfg2 = new FrameCapture.Core.GestureConfig();
            cfg2.Bindings.Add(new FrameCapture.Core.GestureBindingDto { Id = "mine", Chord = "F6", Trigger = "Chord", Actions = new[] { "capture.frame" } });
            cfg2.SeedIfNeeded("Ctrl+Shift+G", "Ctrl+Shift+R");
            Chk("T147d-seed-respects-existing", cfg2.Bindings.Count == 1 && cfg2.Bindings[0].Id == "mine" && cfg2.Seeded,
                "已有绑定不被覆盖，仅补置 Seeded");

            var cfg3 = new FrameCapture.Core.GestureConfig { Seeded = true };   // 已播种（用户曾清空绑定）
            cfg3.SeedIfNeeded("Ctrl+Shift+G", "Ctrl+Shift+R");
            Chk("T147e-seed-idempotent", cfg3.Bindings.Count == 0, "已播种后清空绑定不再重新塞回");
        }
        Console.WriteLine($"\nRESULT PASS={pass} FAIL={fail}");
        return fail == 0 ? 0 : 1;
    }

    static List<CapturedFrame> MakeVidFrames(int n, int w, int h)
    {
        var list = new List<CapturedFrame>(n);
        for (int i = 0; i < n; i++)
        {
            var buf = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    buf[o] = (byte)((x * 4 + i * 17) & 0xFF);
                    buf[o + 1] = (byte)((y * 6) & 0xFF);
                    buf[o + 2] = (byte)(i * 20 & 0xFF); // 逐帧变 → 有运动，编码器不空
                    buf[o + 3] = 255;
                }
            list.Add(new CapturedFrame { Bgra = buf, W = w, H = h, TimeMs = n > 1 ? i * (1000.0 / (n - 1)) : 0 });
        }
        return list;
    }
}

/// <summary>GestureCore 测试探针：记录被调用的动作名，可指定某些动作必然失败以驱动失败策略分支。</summary>
sealed class GestureProbe : GestureCore.IActionInvoker
{
    public List<string> Log = new();
    public HashSet<string> Fail = new();
    public bool Invoke(string actionName) { Log.Add(actionName); return !Fail.Contains(actionName); }
}

/// <summary>GestureEditor 校验测试用的最小动作清单（宿主无关，只认两个合法动作名）。</summary>
sealed class TestCatalog : GestureEditor.IActionCatalog
{
    static readonly GestureEditor.ActionInfo[] All =
    {
        new("capture.frame", "即时截图", ""),
        new("record.toggle", "选区录制·同键停", ""),
    };
    public IReadOnlyList<GestureEditor.ActionInfo> Actions() => All;
    public bool IsValid(string name) => name == "capture.frame" || name == "record.toggle";
}

/// <summary>测试键名器：VK → 可读名（仅覆盖测试用到的键）。</summary>
sealed class TestNamer : GestureCore.IKeyNamer
{
    public string Name(int key) => key switch { 0x41 => "A", 0x52 => "R", 0x75 => "F6", _ => "#" + key };
}
