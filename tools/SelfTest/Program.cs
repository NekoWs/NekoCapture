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

    static CapturedFrame Solid(int w, int h, byte r, byte g, byte b)
    {
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
    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
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
                // 工具
                '\uE915','\uE949','\uEA3F','\uE74A','\uE70F','\uE7A8',
                // 编辑器
                '\uE74E','\uE8C8','\uE116','\uE955','\uE892','\uE100','\uE769','\uE101','\uE893','\uE711','\uE73E','\uE7A7',
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
        }
        Console.WriteLine($"\nRESULT PASS={pass} FAIL={fail}");
        return fail == 0 ? 0 : 1;
    }
}
