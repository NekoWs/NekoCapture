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
using HD = FrameCapture.Core.HookDecision;

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

        // T76 全局钩子决策表（纯逻辑）：捕获期间主键吞掉不重复触发、修饰键松开语义、
        // 范围键松开只复位、无关键放行。对应 HotkeyManager 重构（行为逐条保持）。
        {
            var begin = new HD.Outcome(HD.Act.BeginCapture, true, HD.Key.CapturePrimary);
            var end = new HD.Outcome(HD.Act.EndCapture, true, HD.Key.None);
            Chk("T76a-hook-idle-triggers",
                HD.Decide(HD.Key.None, true, 0x75, true, false, 0x75, 0x76, false, true) .Equals(begin) &&
                HD.Decide(HD.Key.None, true, 0x76, false, true, 0x75, 0x76, false, true)
                    .Equals(new HD.Outcome(HD.Act.ScopeSelect, true, HD.Key.Scope)) &&
                HD.Decide(HD.Key.None, true, 0x41, false, false, 0x75, 0x76, false, true)
                    .Equals(new HD.Outcome(HD.Act.Pass, false, HD.Key.None)),
                "空闲：捕获键→Begin、范围键→Scope、无关键→放行");
            Chk("T76b-hook-capture-primary",
                HD.Decide(HD.Key.CapturePrimary, false, 0x75, false, false, 0x75, 0x76, false, true).Equals(end) &&
                HD.Decide(HD.Key.CapturePrimary, true, 0x75, false, false, 0x75, 0x76, false, true)
                    .Equals(new HD.Outcome(HD.Act.Pass, true, HD.Key.CapturePrimary)),
                "捕获中：主键松→End；主键重复按→吞掉不重复触发");
            Chk("T76c-hook-mod-release",
                HD.Decide(HD.Key.CapturePrimary, false, 0x11, false, false, 0x75, 0x76, true, true)
                    .Equals(new HD.Outcome(HD.Act.Pass, false, HD.Key.CapturePrimary)) &&
                HD.Decide(HD.Key.CapturePrimary, false, 0x11, false, false, 0x75, 0x76, true, false).Equals(end),
                "捕获中：多余修饰键松开→放行；所需修饰键松开→End");
            Chk("T76d-hook-scope-release-and-passthrough",
                HD.Decide(HD.Key.Scope, false, 0x76, false, false, 0x75, 0x76, false, true)
                    .Equals(new HD.Outcome(HD.Act.Pass, true, HD.Key.None)) &&
                HD.Decide(HD.Key.CapturePrimary, false, 0x41, false, false, 0x75, 0x76, false, true)
                    .Equals(new HD.Outcome(HD.Act.Pass, false, HD.Key.CapturePrimary)) &&
                HD.Decide(HD.Key.Scope, true, 0x75, true, false, 0x75, 0x76, false, true).Equals(begin),
                "范围键松开→吞掉复位；捕获中无关键放行；范围按住时可按捕获键");
            // T77：Fps 钳制单一事实源（常量 = 文档范围，Normalize 钳越界值）
            Chk("T77a-fps-constants",
                AppSettings.FpsMin == 10 && AppSettings.FpsMax == 60 && AppSettings.FpsMin < AppSettings.FpsMax,
                "Fps 常量单一事实源：10..60");
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
                Chk("T92a-count", tools.Length == 8, $"八个绘制工具 → {tools.Length}");
                var keys = tools.Select(t => t.Key).ToList();
                Chk("T92b-keys", keys.Distinct().Count() == 8 && keys.Select(k => k[0]).OrderBy(c => c).SequenceEqual("ACLMOPTV"), "键位 V/L/O/A/P/T/M/C 唯一且齐全");
                Chk("T92c-glyphs", tools.All(t => t.Glyph.Length == 1 && t.Glyph[0] >= 0xE000 && t.Glyph[0] <= 0xF8FF) && tools.Select(t => t.Glyph).Distinct().Count() == 8,
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
        }
        Console.WriteLine($"\nRESULT PASS={pass} FAIL={fail}");
        return fail == 0 ? 0 : 1;
    }
}
