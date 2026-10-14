// UICap — 设计审查截图工具：单窗截图(同名全截) / 全屏 / burst 连拍动画帧
// 用法: UICap win "<标题>" out.png | desk out.png | burst "<标题>" 秒 间隔ms 前缀
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

static class P
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int idx);
    [DllImport("gdi32.dll")] public static extern IntPtr BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint op);
    public const uint SRCCOPY = 0xCC0020;
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<IntPtr> FindAll(string title)
    {
        var outp = new List<IntPtr>();
        EnumWindows((h, l) =>
        {
            var sb = new StringBuilder(512);
            GetWindowTextW(h, sb, 512);
            if (sb.ToString() == title && IsWindowVisible(h)) outp.Add(h);
            return true;
        }, IntPtr.Zero);
        return outp;
    }
}

class Program
{
    static Bitmap GrabDesk(int x, int y, int w, int hh)
    {
        var bmp = new Bitmap(w, hh, PixelFormat.Format32bppArgb);
        IntPtr sdc = P.GetDC(IntPtr.Zero);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            P.BitBlt(hdc, 0, 0, w, hh, sdc, x, y, P.SRCCOPY);
            g.ReleaseHdc(hdc);
        }
        P.ReleaseDC(IntPtr.Zero, sdc);
        return bmp;
    }

    static (int x, int y, int w, int h) TitleRect(string title)
    {
        // 覆盖所有同名窗的最小包围盒（编辑器+两条悬浮条一锅端）
        var found = P.FindAll(title);
        if (found.Count == 0) return (0, 0, 0, 0);
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        foreach (var h in found)
        {
            P.GetWindowRect(h, out var rc);
            l = Math.Min(l, rc.Left); t = Math.Min(t, rc.Top);
            r = Math.Max(r, rc.Right); b = Math.Max(b, rc.Bottom);
        }
        return (l, t, r - l, b - t);
    }

    static int Region(string title, string outPath)
    {
        var (x, y, w, h) = TitleRect(title);
        if (w < 2) { Console.WriteLine("not-found"); return 2; }
        using var bmp = GrabDesk(x, y, w, h);
        bmp.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"saved {outPath} {w}x{h} @({x},{y})");
        return 0;
    }

    static void WriteTimes(string prefix, List<string> times)
    {
        // 每帧实际时刻侧车：frame<TAB>elapsedMs<TAB>x y w h —— 逐帧审查时判断动画时序的事实依据
        File.WriteAllText($"{prefix}.times.txt", string.Join("\n", times));
    }

    static int BurstRegion(string title, double seconds, int intervalMs, string prefix)
    {
        // 每帧重算包围盒：窗口移动/淡出也拍得到
        var sw = Stopwatch.StartNew();
        int i = 0, warm = 0;
        var times = new List<string>();
        while (sw.Elapsed.TotalSeconds < seconds && i < 300)
        {
            var (x, y, w, h) = TitleRect(title);
            if (w < 2)
            {
                if (++warm > 240) break;
                Thread.Sleep(30);
                continue;
            }
            warm = 0;
            using var bmp = GrabDesk(x, y, w, h);
            bmp.Save($"{prefix}.{i:D3}.png", ImageFormat.Png);
            times.Add($"{i}\t{sw.ElapsedMilliseconds}\t{x} {y} {w} {h}");
            i++;
            long target = (i + 1) * (long)intervalMs;
            Thread.Sleep(Math.Max(2, (int)(target - sw.ElapsedMilliseconds)));
        }
        WriteTimes(prefix, times);
        Console.WriteLine($"burst {i} frames -> {prefix}.NNN.png (+times)");
        return 0;
    }

    static Bitmap Grab(IntPtr h)
    {
        P.GetWindowRect(h, out var r);
        int w = r.Right - r.Left, hh = r.Bottom - r.Top;
        if (w < 2 || hh < 2) return new Bitmap(1, 1);
        var bmp = new Bitmap(w, hh, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        IntPtr hdc = g.GetHdc();
        P.PrintWindow(h, hdc, 2 /*PW_RENDERFULLCONTENT*/);
        g.ReleaseHdc(hdc);
        return bmp;
    }

    static int Montage(string prefix, string outPath, int cols, int cellW, int from = 0, int to = int.MaxValue)
    {
        // 拼帧总览：每格 = 一帧（等比缩进 cellW），左上角标 "#帧号 @时刻ms"，逐帧审查的高效载体
        var files = Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(prefix + ".000.png"))!,
            Path.GetFileName(prefix) + ".*.png")
            .Where(f =>
            {
                var n = Path.GetFileNameWithoutExtension(f).Split('.')[^1];
                return int.TryParse(n, out int idx) && idx >= from && idx <= to;
            })
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count == 0) { Console.WriteLine("not-found"); return 2; }
        var times = new Dictionary<int, string>();
        if (File.Exists($"{prefix}.times.txt"))
            foreach (var line in File.ReadAllLines($"{prefix}.times.txt"))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int fi)) times[fi] = parts[1];
            }
        using var first = new Bitmap(files[0]);
        int cellH = Math.Max(60, (int)((double)cellW * first.Height / Math.Max(1, first.Width)) + 16);
        int rows = (files.Count + cols - 1) / cols;
        int pad = 2;
        using var sheet = new Bitmap(cols * (cellW + pad) + pad, rows * (cellH + pad) + pad, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(24, 24, 28));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            using var font = new Font("Consolas", 8f, FontStyle.Bold);
            for (int k = 0; k < files.Count; k++)
            {
                using var bmp = new Bitmap(files[k]);
                int cx = pad + (k % cols) * (cellW + pad), cy = pad + (k / cols) * (cellH + pad);
                double sc = Math.Min((double)cellW / bmp.Width, (double)(cellH - 16) / bmp.Height);
                int w = Math.Max(1, (int)(bmp.Width * sc)), h = Math.Max(1, (int)(bmp.Height * sc));
                int fidx = int.Parse(Path.GetFileNameWithoutExtension(files[k]).Split('.')[^1]);
                string tms = times.TryGetValue(fidx, out var t) ? t + "ms" : "?";
                string label = $"#{fidx:D3} @{tms} {bmp.Width}x{bmp.Height}";
                g.DrawString(label, font, Brushes.White, cx + 1, cy + 1);
                g.DrawImage(bmp, cx, cy + 16, w, h);
                g.DrawRectangle(Pens.DimGray, cx - 1, cy + 15, w + 1, h + 1);
            }
        }
        sheet.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"montage {files.Count} frames -> {outPath} {sheet.Width}x{sheet.Height}");
        return 0;
    }

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: win <title> out.png | desk out.png | burst <title> sec intervalMs prefix"); return 1; }
        switch (args[0])
        {
            case "win": return Win(args[1], args[2]);
            case "desk": return Desk(args[1]);
            case "burst": return Burst(args[1], double.Parse(args[2]), int.Parse(args[3]), args[4]);
            case "region": return Region(args[1], args[2]);
            case "burstregion": return BurstRegion(args[1], double.Parse(args[2]), int.Parse(args[3]), args[4]);
            case "burstdesk": return BurstDesk(double.Parse(args[1]), int.Parse(args[2]), args[3]);
            case "montage": return Montage(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]),
                args.Length > 5 ? int.Parse(args[5]) : 0, args.Length > 6 ? int.Parse(args[6]) : int.MaxValue);
        }
        return 1;
    }

    static int Win(string title, string outPath)
    {
        var found = P.FindAll(title);
        if (found.Count == 0) { Console.WriteLine("not-found"); return 2; }
        for (int i = 0; i < found.Count; i++)
        {
            using var bmp = Grab(found[i]);
            string p = found.Count == 1 ? outPath : outPath.Replace(".png", $"-{i}.png");
            bmp.Save(p, ImageFormat.Png);
            Console.WriteLine($"saved {p} {bmp.Width}x{bmp.Height}");
        }
        return 0;
    }

static int BurstDesk(double sec, int intervalMs, string prefix)
{
    int w = P.GetSystemMetrics(0), h = P.GetSystemMetrics(1);
    int n = Math.Min(300, (int)(sec * 1000 / intervalMs));
    for (int i = 0; i < n; i++)
    {
        using var bmp = GrabDesk(0, 0, w, h);
        bmp.Save($"{prefix}.{i:000}.png", ImageFormat.Png);
        Thread.Sleep(intervalMs);
    }
    Console.WriteLine($"burstdesk {n} frames");
    return 0;
}

    static int Desk(string outPath)
    {
        int w = P.GetSystemMetrics(0), hh = P.GetSystemMetrics(1);
        using var b = new Bitmap(w, hh, PixelFormat.Format32bppArgb);
        IntPtr sdc = P.GetDC(IntPtr.Zero);
        using (var g = Graphics.FromImage(b))
        {
            IntPtr hdc = g.GetHdc();
            P.BitBlt(hdc, 0, 0, w, hh, sdc, 0, 0, P.SRCCOPY);
            g.ReleaseHdc(hdc);
        }
        P.ReleaseDC(IntPtr.Zero, sdc);
        b.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"saved {outPath} {w}x{hh}");
        return 0;
    }

    static int Burst(string title, double seconds, int intervalMs, string prefix)
    {
        List<IntPtr> found = new();
        var w0 = Stopwatch.StartNew();
        while (found.Count == 0 && w0.Elapsed.TotalSeconds < 8)
        {
            found = P.FindAll(title);
            if (found.Count == 0) Thread.Sleep(30);
        }
        if (found.Count == 0) { Console.WriteLine("not-found"); return 2; }
        IntPtr h = found[0];
        var sw = Stopwatch.StartNew();
        int i = 0;
        while (sw.Elapsed.TotalSeconds < seconds && i < 240)
        {
            using var bmp = Grab(h);
            bmp.Save($"{prefix}.{i:D3}.png", ImageFormat.Png);
            i++;
            long target = (i + 1) * intervalMs;
            int sleep = (int)(target - sw.ElapsedMilliseconds);
            Thread.Sleep(Math.Max(2, sleep));
        }
        Console.WriteLine($"burst {i} frames -> {prefix}.NNN.png");
        return 0;
    }
}
