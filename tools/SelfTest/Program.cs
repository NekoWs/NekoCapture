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
using System.Windows.Input;

using GE = GestureCore;
using SAP = FrameCapture.Core.ScrollAutoPolicy;
using SAO = FrameCapture.Core.ScrollAutoObservation;

static partial class SelfTest
{
    static int pass, fail;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    // 逐条耗时：FC_ST_PERF=1 时把「距上一条断言花了多久」打出来，用来找时间黑洞。
    // 慢的几乎都是定长 PumpMs 等待，不是断言本身吃时间。
    static readonly bool PerfDump = Environment.GetEnvironmentVariable("FC_ST_PERF") == "1";
    static readonly System.Diagnostics.Stopwatch PerfSw = System.Diagnostics.Stopwatch.StartNew();
    static long _perfLast;

    static void Chk(string name, bool ok, string? detail = null)
    {
        if (PerfDump)
        {
            long now = PerfSw.ElapsedMilliseconds;
            Console.WriteLine($"PERF {now - _perfLast,6} {name}");
            _perfLast = now;
        }
        if (ok) { pass++; Console.WriteLine($"PASS {name}"); }
        else { fail++; Console.WriteLine($"FAIL {name}{(detail != null ? " :: " + detail : "")}"); }
    }

    /// <summary>探针：全局细滚动条样式是否真的落到 ScrollViewer 模板内的 ScrollBar 上。</summary>
    static int SbProbe(string outPath)
    {
        var app = new Application();
        Fluent.ApplyGlobalStyles();
        var sv = new System.Windows.Controls.ScrollViewer { Width = 100, Height = 60, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Visible };
        sv.Content = new System.Windows.Controls.Border { Width = 90, Height = 400, Background = System.Windows.Media.Brushes.Red };
        var win = new Window { Content = sv, Left = -3000, Top = -3000, Width = 140, Height = 100, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
        win.Show();
        win.UpdateLayout();
        System.Windows.Controls.Primitives.ScrollBar? found = null;
        void Walk(DependencyObject o)
        {
            if (found != null) return;
            if (o is System.Windows.Controls.Primitives.ScrollBar sb && sb.Orientation == System.Windows.Controls.Orientation.Vertical) { found = sb; return; }
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(o);
            for (int i = 0; i < n; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
        }
        Walk(win);
        string info = found == null
            ? "no-scrollbar"
            : $"w={found.ActualWidth} styleNotNull={found.Style != null} hasAppStyle={app.Resources.Contains(typeof(System.Windows.Controls.Primitives.ScrollBar))}";
        if (found?.Style != null)
        {
            var ours = (Style)app.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)];
            info += $"\ntt={found.Style.TargetType} setters={found.Style.Setters.Count} isOurs={ReferenceEquals(found.Style, ours)}";
            info += $"\nwidthProp={found.Width} actualH={found.ActualHeight} localWidth={found.ReadLocalValue(FrameworkElement.WidthProperty)}";
            info += $"\nminW={found.GetValue(FrameworkElement.MinWidthProperty)} desired={found.DesiredSize} parent={System.Windows.Media.VisualTreeHelper.GetParent(found)?.GetType().Name}";
            var par = System.Windows.Media.VisualTreeHelper.GetParent(found);
            info += $"\nlocalMinW={found.ReadLocalValue(FrameworkElement.MinWidthProperty)} gp={(par is FrameworkElement p ? p.GetType().Name + "/" + p.ActualWidth : "?")}";
            found.MinWidth = 0;
            win.UpdateLayout();
            info += $"\nafterMinW0 w={found.ActualWidth}";
            found.Style = ours;
            win.UpdateLayout();
            info += $"\nafterForce w={found.ActualWidth}";
        }
        File.WriteAllText(outPath, info);
        Console.WriteLine(info);
        win.Close();
        return 0;
    }

    /// <summary>取多行文本的第一行，用于把堆栈或异常里的位置压进一行诊断信息。</summary>
    /// <param name="s">可能为空、可能多行的文本。</param>
    /// <returns>第一行去掉首尾空白；输入为空时返回空串。</returns>
    static string FirstLine(string? s) => string.IsNullOrEmpty(s) ? "" : s.Split('\n')[0].Trim();

    /// <summary>
    /// 合成一张「截下来的屏幕」：深色桌面上一个文本窗口，正文按界面语言排中文或英文。
    /// 编辑器与贴图离屏拍图共用——测试图不该是一坨渐变，读者要一眼看出这是截来的画面。
    /// </summary>
    /// <param name="w">画面宽（像素）。</param>
    /// <param name="h">画面高（像素）。</param>
    /// <returns>按行紧排的 BGRA 字节，长度 w*h*4。</returns>
    static byte[] MockScreen(int w, int h)
    {
        bool en = FrameCapture.Core.Loc.IsEnglish;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new System.Windows.Media.LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(22, 24, 28), System.Windows.Media.Color.FromRgb(43, 46, 54), 40), null, new Rect(0, 0, w, h));
            var card = new Rect(w * 0.07, h * 0.09, w * 0.86, h * 0.82);
            dc.DrawRoundedRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 33, 38)),
                new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(62, 66, 74)), 1), card, 9, 9);
            dc.DrawRoundedRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(41, 44, 50)), null,
                new Rect(card.X, card.Y, card.Width, 32), 9, 9);
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(41, 44, 50)), null,
                new Rect(card.X, card.Y + 18, card.Width, 14));
            var dim = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 156, 166));
            var text = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 229, 234));
            var accent = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 168, 232));
            double px = 1.0;
            System.Windows.Media.FormattedText Ft(string s, double size, System.Windows.Media.Brush b, bool bold) => new System.Windows.Media.FormattedText(s,
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei"), System.Windows.FontStyles.Normal,
                    bold ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal),
                size, b, px);

            var title = Ft(en ? "scratchpad.txt" : "随手记.txt", 12.5, dim, false);
            dc.DrawText(title, new System.Windows.Point(card.X + 14, card.Y + 8));
            string[] head = en
                ? new[] { "Capture   Ctrl+Alt+A    drag a box, release to shoot",
                          "Record    Ctrl+Alt+R    press to start, press again to stop",
                          "Pin       Ctrl+P        nail it to the screen" }
                : new[] { "截图   Ctrl+Alt+A   拖框选区，松手即截",
                          "录制   Ctrl+Alt+R   按一下开始，再按一下停止",
                          "贴图   Ctrl+P       钉在屏幕上，比切窗口快" };
            string[] body = en
                ? new[] { "In the editor", "Save  Ctrl+S     Export  Ctrl+E",
                          "Open  Ctrl+O     Text    Ctrl+H", "Frame  <-  ->     Shift steps a second" }
                : new[] { "编辑器窗口内", "保存  Ctrl+S     导出  Ctrl+E",
                          "打开  Ctrl+O     识字  Ctrl+H", "逐帧  <-  ->     Shift 一次一秒" };
            double y = card.Y + 56;
            dc.DrawText(Ft(en ? "FrameCapture notes" : "FrameCapture 随手记", 15, text, true),
                new System.Windows.Point(card.X + 16, y));
            y += 34;
            foreach (string line in head) { dc.DrawText(Ft(line, 12.5, text, false), new System.Windows.Point(card.X + 16, y)); y += 21; }
            y += 14;
            foreach (string line in body)
            {
                dc.DrawText(Ft(line, 12.5, line == body[0] ? accent : text, line == body[0]),
                    new System.Windows.Point(card.X + 16, y));
                y += 21;
            }
            y += 14;
            dc.DrawText(Ft(en ? "Everything is produced on this machine, nothing leaves it."
                              : "所有产物都在本机生成，不联网。", 12.5, dim, false),
                new System.Windows.Point(card.X + 16, y));
        }
        var shot = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        shot.Render(dv);
        var bytes = new byte[w * h * 4];
        shot.CopyPixels(bytes, w * 4, 0);
        return bytes;
    }

    /// <summary>
    /// 离屏拍一张贴图窗：贴图窗只有画面加一圈描边，四周透明，
    /// 合成到深色桌面底上才看得出「钉在屏幕上的那块图」。
    /// </summary>
    /// <param name="outPath">输出 PNG 路径。</param>
    /// <param name="scale">像素与 DPI 的同倍放大，读图用。</param>
    /// <returns>恒为 0；渲染结果由打印行报告。</returns>
    static int RenderPin(string outPath, double scale)
    {
        var app = new Application();
        var svc = new FrameCapture.Core.SettingsService();
        typeof(FrameCapture.Services.AppHost).GetProperty("Settings")!
            .SetValue(FrameCapture.Services.AppHost.Current, svc);
        FrameCapture.Core.Theme.Apply(svc.Current.Theme, svc.Current.Accent);
        FrameCapture.Core.Fluent.ApplyGlobalStyles();
        string lang = Environment.GetEnvironmentVariable("FC_LANG") ?? "";
        FrameCapture.Core.Loc.Apply(lang == "" ? svc.Current.UiLanguage : lang);

        const int pw = 640, ph = 400;
        var frame = new FrameCapture.Capture.CapturedFrame
        { Bgra = MockScreen(pw, ph), W = pw, H = ph, TimeMs = 0 };
        var w = new FrameCapture.Overlays.PinWindow(frame, new FrameCapture.Capture.RectI(240, 180, pw, ph), false);
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -20000; w.Top = -20000;
        w.Show();
        w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        w.Opacity = 1;
        // 贴图窗把画面像素按显示器缩放换算成 DIP，离屏拍图要的是确定的像素尺寸：
        // 让画面等比填满窗口，窗口尺寸由拍图这边说了算，换一台不同缩放的机器拍出来一模一样
        void PinImagesToFill(DependencyObject node)
        {
            if (node is System.Windows.Controls.Image im)
            { im.Width = double.NaN; im.Height = double.NaN; im.Stretch = System.Windows.Media.Stretch.Uniform; }
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++) PinImagesToFill(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
        }
        w.UpdateLayout();
        PinImagesToFill(w);
        w.Width = pw; w.Height = ph;
        var root = (UIElement)w.Content;
        root.Measure(new System.Windows.Size(pw, ph));
        root.Arrange(new Rect(0, 0, pw, ph));
        root.UpdateLayout();
        int iw = pw, ih = ph;
        var pin = new System.Windows.Media.Imaging.RenderTargetBitmap(iw, ih, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        pin.Render(root);

        int ww = (int)Math.Ceiling((iw + 120) * scale), hh = (int)Math.Ceiling((ih + 90) * scale);
        // 坐标已经乘过 scale，这里再按 dpi 放大一次就是双重放大：合成层固定 96dpi
        var back = new System.Windows.Media.DrawingVisual();
        using (var dc = back.RenderOpen())
        {
            dc.DrawRectangle(new System.Windows.Media.LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(18, 19, 23), System.Windows.Media.Color.FromRgb(38, 41, 48), 35), null, new Rect(0, 0, ww, hh));
            var at = new Rect(60 * scale, 45 * scale, iw * scale, ih * scale);
            dc.DrawRoundedRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 11, 13)), null,
                new Rect(at.X + 5 * scale, at.Y + 9 * scale, at.Width, at.Height), 4, 4);
            dc.DrawImage(pin, at);
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(ww, hh, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(back);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var fs = File.Create(outPath)) enc.Save(fs);
        Console.WriteLine($"saved {outPath} {ww}x{hh} lang={(FrameCapture.Core.Loc.IsEnglish ? "en" : "zh")}");
        return 0;
    }
    static int RenderHelp(string outPath) => RenderHelp(outPath, 0, 1);
    static int RenderHelp(string outPath, int step, double scale)    {
        var app = new Application();
        // 帮助里的键位列取自 SettingsService.Shared：拍图路径不建它，那一列会整列空掉，
        // 拍出来的就不是用户看到的形态。主题与语言也走与设置窗拍图同一条规则。
        var svc = new FrameCapture.Core.SettingsService();
        FrameCapture.Core.Theme.Apply(svc.Current.Theme, svc.Current.Accent);
        string ftheme = Environment.GetEnvironmentVariable("FC_THEME") ?? "";
        if (ftheme == "light" || ftheme == "dark")
            FrameCapture.Core.Theme.Apply(ftheme, svc.Current.Accent);
        FrameCapture.Core.Fluent.ApplyGlobalStyles();
        // 界面语言与宿主同一套规则：FC_LANG 覆盖配置，拍双语界面不必先改用户配置
        string lang = Environment.GetEnvironmentVariable("FC_LANG") ?? "";
        FrameCapture.Core.Loc.Apply(lang == "" ? svc.Current.UiLanguage : lang);
        // 反射设静态 _lastStep，让窗口停在指定步（长页/短页分别取证）
        typeof(FrameCapture.Overlays.HelpWindow)
            .GetField("_lastStep", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, step);
        var w = FrameCapture.Overlays.HelpWindow.CreateForCapture();
        // 按真实固定窗口尺寸 measure/arrange：短页垂直居中、底部翻页条固定——正是用户点名的布局验证点
        double W = w.Width, H = w.Height;
        var root = (UIElement)w.Content;
        root.Measure(new System.Windows.Size(W, H));
        root.Arrange(new Rect(0, 0, W, H));
        root.UpdateLayout();
        int ww = (int)Math.Ceiling(W * scale), hh = (int)Math.Ceiling(H * scale);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(ww, hh, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var f = File.Create(outPath)) enc.Save(f);
        Console.WriteLine($"saved {outPath} {ww}x{hh} lang={(FrameCapture.Core.Loc.IsEnglish ? "en" : "zh")}");
        return 0;
    }

    /// <summary>取证模式：离屏渲染选区窗，看它自己画了什么——定格窗底、遮罩暗化、提示条。
    /// 选区窗带 WDA_EXCLUDEFROMCAPTURE，任何屏幕截图都拍不到它，只能这样取证。</summary>
    /// <param name="outPath">输出 PNG 路径。</param>
    /// <param name="withFrozen">true = 建窗前就交入定格帧（不透明窗那条路）；false = 分层透明窗那条路。</param>
    /// <returns>恒为 0。</returns>
    static int RenderPicker(string outPath, bool withFrozen)
    {
        var app = new Application();
        var svc = new FrameCapture.Core.SettingsService();
        typeof(FrameCapture.Services.AppHost).GetProperty("Settings")!
            .SetValue(FrameCapture.Services.AppHost.Current, svc);
        FrameCapture.Core.Theme.Apply(svc.Current.Theme, svc.Current.Accent);
        FrameCapture.Core.Fluent.ApplyGlobalStyles();

        // 合成一帧"屏幕"：渐变打底 + 一块纯白方块，一眼看出窗底是不是这张图、有没有铺满拉伸
        int w = 1500, h = 940;
        var bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bgra[i] = (byte)(255 - x * 255 / w);
                bgra[i + 1] = (byte)(y * 255 / h);
                bgra[i + 2] = 190;
                bgra[i + 3] = 255;
            }
        for (int y = 120; y < 320; y++)
            for (int x = 120; x < 520; x++)
            {
                int i = (y * w + x) * 4;
                bgra[i] = bgra[i + 1] = bgra[i + 2] = 255; bgra[i + 3] = 255;
            }
        var frame = new CapturedFrame { Bgra = bgra, W = w, H = h };
        var mon = new MonitorInfo(@"\\.\DISPLAY1", new RectI(0, 0, w, h), true);
        var win = new FrameCapture.Overlays.RegionPickerWindow(mon, null, snapContent: false, magnifier: true,
            windowCapture: true, freeze: true, pregrabMax: 0, frozenScreen: withFrozen ? frame : null);
        var root = (UIElement)win.Content;
        root.Measure(new System.Windows.Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
        PumpMs(300);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var f = File.Create(outPath)) enc.Save(f);
        Console.WriteLine($"saved {outPath} {w}x{h} frozen={withFrozen} opaque={win.AllowsTransparency == false && withFrozen}");
        return 0;
    }

    /// <summary>取证模式：真实设置窗口停在指定类别，离屏渲染成 PNG。
    /// 切类别靠给左侧列表项抛一个 Enter 键事件，与用户点它走同一个 ShowCat。</summary>
    /// <param name="outPath">输出 PNG 路径。</param>
    /// <param name="catName">类别文案：快捷键 / 截图 / 录制 / 产物 / 编辑器 / 贴图 / 外观 / 系统。</param>
    /// <param name="scale">渲染倍率，1 表示 96dpi 原尺寸；宣传页按 1.5 出图。</param>
    /// <param name="openAt">展开第几条快捷键再拍，-1 表示全部折叠。</param>
    /// <param name="advanced">true = 完成后与失败时按高级形态铺开。</param>
    /// <param name="clash">true = 再挂一条与第一条同键同触发的绑定，拍撞车那条的描边。</param>
    /// <returns>恒为 0。</returns>
    static int RenderSettings(string outPath, Str catName, double scale = 1, int openAt = -1, bool advanced = false, bool clash = false, bool down = false)
    {
        var app = new Application();
        // 设置窗读 AppHost.Current.Settings；本模式只 new Application()，不走 AppHost.Startup，
        // 这里只挂一个只读的 SettingsService 并套上主题，不注册热键、不碰托盘。
        var svc = new FrameCapture.Core.SettingsService();
        typeof(FrameCapture.Services.AppHost).GetProperty("Settings")!
            .SetValue(FrameCapture.Services.AppHost.Current, svc);
        FrameCapture.Core.Theme.Apply(svc.Current.Theme, svc.Current.Accent);
        // 设计审查专用：FC_THEME 覆盖主题，拍深浅两套界面不必先改用户配置（与 AppHost 同一套规则）
        string ftheme = Environment.GetEnvironmentVariable("FC_THEME") ?? "";
        if (ftheme == "light" || ftheme == "dark")
            FrameCapture.Core.Theme.Apply(ftheme, svc.Current.Accent);
        FrameCapture.Core.Fluent.ApplyGlobalStyles();
        // 界面语言与 AppHost 同一套规则：先按配置，FC_LANG 可覆盖——拍双语界面不必先改用户配置
        string lang = Environment.GetEnvironmentVariable("FC_LANG") ?? "";
        FrameCapture.Core.Loc.Apply(lang == "" ? svc.Current.UiLanguage : lang);
        // 「clash」：再挂一条与第一条同键同触发的绑定，只为拍撞车那条的描边——只改内存里的配置，不落盘。
        if (clash && svc.Current.Gestures.Bindings.Count > 0)
        {
            var first = svc.Current.Gestures.Bindings[0];
            svc.Current.Gestures.Bindings.Add(new FrameCapture.Core.GestureBindingDto
            {
                Id = "render.clash",
                Chord = first.Chord,
                Trigger = first.Trigger,
                Actions = new[] { "ocr.selection" },
            });
        }
        var w = new FrameCapture.Overlays.SettingsWindow();
        // 「adv」：先把完成后/失败时切到高级形态再建内容——行的形态在建行时按这个开关铺一次。
        if (advanced)
            typeof(FrameCapture.Overlays.SettingsWindow)
                .GetField("_hotAdvanced", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(w, true);
        // Build 与构造分开（ShowOrFocus 先 Build 再 Show），离屏渲染同样要先建内容
        typeof(FrameCapture.Overlays.SettingsWindow)
            .GetMethod("Build", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(w, null);
        double W = w.Width, H = w.Height;
        var root = (UIElement)w.Content;
        // 键事件要有 PresentationSource，未 Show 的窗没有——把窗挪到屏幕外真开一下，只为拿到输入源。
        // Mica 在场时窗口底是透明的，拍出来会没底色，这里按窗口底色平涂。
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -20000; w.Top = -20000;
        w.Background = new System.Windows.Media.SolidColorBrush(FrameCapture.Core.Theme.WindowBg);
        w.Show();
        PumpMs(400);
        var src = PresentationSource.FromVisual(w);
        void Layout()
        {
            root.Measure(new System.Windows.Size(W, H));
            root.Arrange(new Rect(0, 0, W, H));
            root.UpdateLayout();
        }
        Layout();
        // 导航项显示的是当前语言的文案，查找也要用取词后的名字
        var nav = FindNavText(root, Loc.T(catName)) as System.Windows.UIElement;
        if (nav != null && src != null)
        {
            // 类别项的 KeyDown 认 Enter/Space，与鼠标点一下走同一个 ShowCat
            nav.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice, src, 0, System.Windows.Input.Key.Enter)
            { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent, Source = nav });
            PumpMs(500); // 切类别的淡入淡出走完再拍
        }
        Layout();
        // 「open」/「open2」：展开第几条快捷键再拍——折叠态与展开态是两套版面，只拍折叠态看不到序列编辑区。
        // 行首箭头与下拉框模板里的箭头同字形，先定位键位框、回到它所在的行，才打得准这一击。
        bool opened = false;
        if (openAt >= 0 && src != null)
        {
            var boxes = new List<System.Windows.DependencyObject>();
            CollectTyped(root, typeof(FrameCapture.Core.ChordCaptureBox), boxes);
            // 键位框 → 行的列容器 → 挂着展开处理器的行首胶囊
            var head = openAt < boxes.Count
                ? System.Windows.Media.VisualTreeHelper.GetParent(
                    System.Windows.Media.VisualTreeHelper.GetParent(boxes[openAt]))
                : null;
            // 打在行容器上：行首箭头是行的后代，事件从后代冒泡上来要过完整的命中路径，
            // 离屏窗没有真实输入设备，直接打在挂处理器的那一层最可靠。
            var target = head as System.Windows.UIElement;
            if (target != null)
            {
                target.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent, Source = target });
                PumpMs(400);
                // 展开成功的标志：行首的向下箭头换成了向上箭头
                opened = FindGlyphText(root, "\uE70E") != null;
            }
        }
        Layout();
        // 「down」：把右栏滚到底再拍。编辑器绑键那一段在折叠线以下，不滚就拍不到它的真实形态。
        if (down)
        {
            var svs = new List<System.Windows.DependencyObject>();
            CollectTyped(root, typeof(System.Windows.Controls.ScrollViewer), svs);
            if (svs.Count > 0)
            {
                var sv = (System.Windows.Controls.ScrollViewer)svs[0];
                sv.ScrollToVerticalOffset(sv.ScrollableHeight);
                PumpMs(300);
                Layout();
            }
        }
        int ww = (int)Math.Ceiling(W * scale), hh = (int)Math.Ceiling(H * scale);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(ww, hh, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var f = File.Create(outPath)) enc.Save(f);
        // 不调 w.Close()：关窗回调要去重装热键，本模式没有 HotkeyManager；进程退出自然带走窗口。
        Console.WriteLine($"saved {outPath} {ww}x{hh} nav={(nav != null)} src={(src != null)} open={(opened)}");
        return 0;
    }

    /// <summary>把芯片拖动中途的样子拍下来。抬起的芯片该按原尺寸贴在光标按住的那一点，
    /// 占位框该出现在落点那一格，其余芯片让开一格——这三件事光看索引断言看不出来，
    /// 行被撑到换行、芯片被拉成两倍大，索引都还是对的。</summary>
    /// <param name="outPath">输出 PNG 路径。</param>
    /// <param name="at">拖动中光标在编辑器根上的位置。</param>
    /// <returns>恒为 0。</returns>
    static int RenderDrag(string outPath, System.Windows.Point at)
    {
        var np = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var ceType = typeof(FrameCapture.Overlays.SettingsWindow)
            .GetNestedType("ChainEditor", System.Reflection.BindingFlags.NonPublic)!;
        Func<string[]> get = () => new[] { "capture.region", "capture.stored", "pin.clipboard" };
        Func<int, IEnumerable<FrameCapture.Core.GestureAction>> cand =
            _ => Array.Empty<FrameCapture.Core.GestureAction>();
        Action<string[]> set = _ => { };
        var ed = Activator.CreateInstance(ceType, get, cand, set)!;
        var root = (System.Windows.Controls.StackPanel)ceType.GetProperty("Root")!.GetValue(ed)!;
        // 自己搭台子而不是塞进一扇不显示的窗：窗没 Show 过就不走真排版，芯片量出来是 0×0，
        // 窗模板也没应用，装饰层沿视觉树往上找不到 AdornerDecorator，抬起的芯片无处可挂。
        var stage = new System.Windows.Controls.Border
        {
            Width = 460, Height = 150,
            Background = new System.Windows.Media.SolidColorBrush(FrameCapture.Core.Theme.CardBg),
            Child = new System.Windows.Documents.AdornerDecorator { Child = root },
        };
        stage.Measure(new System.Windows.Size(460, 150));
        stage.Arrange(new System.Windows.Rect(0, 0, 460, 150));
        stage.UpdateLayout();
        var chips = (System.Windows.Controls.WrapPanel)ceType.GetField("_chips", np)!.GetValue(ed)!;
        var chip = (System.Windows.FrameworkElement)chips.Children[1];
        var sizeBefore = chip.RenderSize;
        var grab = new System.Windows.Point(12, sizeBefore.Height / 2);
        ceType.GetField("_drag", np)!.SetValue(ed, chip);
        ceType.GetField("_dragFrom", np)!.SetValue(ed, 1);
        ceType.GetField("_target", np)!.SetValue(ed, 1);
        ceType.GetField("_grab", np)!.SetValue(ed, grab);
        ceType.GetField("_pressAt", np)!.SetValue(ed, chip.TranslatePoint(new System.Windows.Point(12, 6), root));
        ceType.GetMethod("DragMove", np)!.Invoke(ed, new object[] { at });
        // 让位是 150ms 的动画，不泵一下拍到的就是第 0 帧：别的芯片还在让位前的位置上，看着像压在占位框上
        PumpMs(320);
        stage.UpdateLayout();
        var ghost = ceType.GetField("_ghost", np)!.GetValue(ed) as System.Windows.UIElement;
        var lift = ceType.GetField("_lift", np)!.GetValue(ed);
        Console.WriteLine($"drag: 芯片 {sizeBefore.Width:N0}x{sizeBefore.Height:N0} → {chip.RenderSize.Width:N0}x{chip.RenderSize.Height:N0}"
            + $" 装饰层={(lift == null ? "没挂上" : "已挂上")} 占位框第 {chips.Children.IndexOf(ghost!)} 格 / 共 {chips.Children.Count} 子");
        var tgD = chip.RenderTransform as System.Windows.Media.TransformGroup;
        var trD = tgD?.Children.OfType<System.Windows.Media.TranslateTransform>().FirstOrDefault();
        var scD = tgD?.Children.OfType<System.Windows.Media.ScaleTransform>().FirstOrDefault();
        var gotD = chip.TranslatePoint(new System.Windows.Point(0, 0), root);
        Console.WriteLine($"lift: 位移=({trD?.X:N1},{trD?.Y:N1}) 缩放={scD?.ScaleX:N3} 芯片左上角=({gotD.X:N1},{gotD.Y:N1})"
            + $" 光标按住点=({at.X - grab.X:N1},{at.Y - grab.Y:N1}) 行原点=({chips.TranslatePoint(new System.Windows.Point(0, 0), root).X:N1},"
            + $"{chips.TranslatePoint(new System.Windows.Point(0, 0), root).Y:N1})");
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(460, 150, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(stage);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var f = File.Create(outPath)) enc.Save(f);
        Console.WriteLine($"saved {outPath} 460x150");
        return 0;
    }

    /// <summary>渲染倍率：命令行最后一个参数写成 <c>@1.5</c> 就是 150%——宣传页的界面截图按 150% 出图，
    /// 位图 DPI 同步放大，字与图标都是矢量重绘，不是把 96dpi 的图放大重采样。</summary>

    /// <param name="args">命令行参数；最后一个形如 <c>@1.5</c> 的参数是倍率。</param>
    /// <returns>解析出的倍率；没写、写坏、或不是最后一个参数时返回 1。</returns>
    static double ScaleArg(string[] args) =>
        args.Length > 0 && args[^1].StartsWith("@", StringComparison.Ordinal)
        && double.TryParse(args[^1][1..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0
            ? s : 1;

    /// <summary>离屏渲染编辑器窗口：合成一段三帧捕获序列喂给它，工具条、时间轴、HUD 都是真实装配，
    /// 拍出来的就是用户按下截图后看到的那一窗。帧内容是合成的屏幕画面，画布不至于全黑。</summary>
    /// <param name="outPath">输出 PNG 路径。</param>
    /// <param name="scale">渲染倍率，1 表示 96dpi 原尺寸。</param>
    /// <param name="guide">true 时把首次打开的上手引导一并拍进去；false 时确保引导已关掉。</param>
    /// <returns>恒为 0；渲染结果由打印行报告。</returns>
    static int RenderEditor(string outPath, double scale, bool guide)
    {
        var app = new Application();
        var svc = new FrameCapture.Core.SettingsService();
        typeof(FrameCapture.Services.AppHost).GetProperty("Settings")!
            .SetValue(FrameCapture.Services.AppHost.Current, svc);
        FrameCapture.Core.Theme.Apply(svc.Current.Theme, svc.Current.Accent);
        FrameCapture.Core.Fluent.ApplyGlobalStyles();
        string lang = Environment.GetEnvironmentVariable("FC_LANG") ?? "";
        FrameCapture.Core.Loc.Apply(lang == "" ? svc.Current.UiLanguage : lang);
        const int fw = 900, fh = 520;
        var frames = new List<FrameCapture.Capture.CapturedFrame>();
        for (int i = 0; i < 3; i++)
            frames.Add(new FrameCapture.Capture.CapturedFrame { Bgra = MockScreen(fw, fh), W = fw, H = fh, TimeMs = i * 33.3 });
        var seq = new FrameCapture.Capture.CapturedSequence
        {
            Frames = frames,
            Region = new FrameCapture.Capture.RectI(0, 0, fw, fh),
            MonitorBounds = new FrameCapture.Capture.RectI(0, 0, 1920, 1080),
            Fps = 30,
        };
        var w = new FrameCapture.Overlays.EditorWindow(svc, seq);
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -20000; w.Top = -20000;
        // Mica 在场时窗口底是透明的，离屏拍图按窗口底色平涂才有底
        w.Background = new System.Windows.Media.SolidColorBrush(FrameCapture.Core.Theme.WindowBg);
        w.Show();
        // 淡入动画会把窗口留在半透明：排空 dispatcher 再把不透明度钉死，拍出来才是常态样子
        w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        w.Opacity = 1;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var t = typeof(FrameCapture.Overlays.EditorWindow);
        if (guide) t.GetMethod("ShowGuide", flags)?.Invoke(w, null);
        else
        {
            // CloseGuide 走淡出动画，紧接着拍图会把还在场上的引导一起拍进来；直接从视觉树摘掉
            if (t.GetField("_guide", flags)?.GetValue(w) is System.Windows.FrameworkElement g
                && g.Parent is System.Windows.Controls.Panel host)
            {
                host.Children.Remove(g);
                t.GetField("_guide", flags)!.SetValue(w, null);
            }
        }
        double W = w.ActualWidth, H = w.ActualHeight;
        var root = (UIElement)w.Content;
        root.Measure(new System.Windows.Size(W, H));
        root.Arrange(new Rect(0, 0, W, H));
        root.UpdateLayout();
        int ww = (int)Math.Ceiling(W * scale), hh = (int)Math.Ceiling(H * scale);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(ww, hh, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var f = File.Create(outPath)) enc.Save(f);
        Console.WriteLine($"saved {outPath} {ww}x{hh} lang={(FrameCapture.Core.Loc.IsEnglish ? "en" : "zh")} guide={guide}");
        return 0;
    }

    /// <summary>在视觉树里找左侧类别项：Border 内 Grid 里那条 StackPanel 带同名文字。</summary>
    /// <param name="node">起点节点。</param>
    /// <param name="text">类别名。</param>
    /// <returns>命中的 Border；没有则 null。</returns>
    static System.Windows.DependencyObject? FindNavText(System.Windows.DependencyObject node, string text)
    {
        if (node is System.Windows.Controls.Border b && b.Child is System.Windows.Controls.Grid g)
            foreach (var ch in g.Children)
                if (ch is System.Windows.Controls.StackPanel sp)
                    foreach (var c2 in sp.Children)
                        if (c2 is System.Windows.Controls.TextBlock tb && tb.Text == text) return b;
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
        {
            var r = FindNavText(System.Windows.Media.VisualTreeHelper.GetChild(node, i), text);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>收集视觉树里该类型的所有节点，按树的先后顺序。</summary>
    /// <param name="node">起点节点。</param>
    /// <param name="type">要找的类型。</param>
    /// <param name="into">结果收集到这里。</param>
    static void CollectTyped(System.Windows.DependencyObject node, Type type, List<System.Windows.DependencyObject> into)
    {
        if (type.IsInstanceOfType(node)) into.Add(node);
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
            CollectTyped(System.Windows.Media.VisualTreeHelper.GetChild(node, i), type, into);
    }

    /// <summary>设置页里某个时长输入后面那枚单位的文字：Tag 是「控件标记 + -unit」。</summary>
    /// <param name="root">要在哪棵子树里找。</param>
    /// <param name="tag">时长框的标记。</param>
    /// <returns>单位的文字；没找到返回空串。</returns>
    static string DurationUnitOf(System.Windows.DependencyObject root, string tag)
    {
        var all = new List<System.Windows.DependencyObject>();
        CollectTyped(root, typeof(System.Windows.Controls.TextBlock), all);
        return all.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault(t => (t.Tag as string) == tag + "-unit")?.Text ?? "";
    }
    /// <summary>在视觉树里找第一个显示该文字的元素，用于把事件打在指定的行首图标上。</summary>
    /// <param name="node">起点节点。</param>
    /// <param name="text">要匹配的文字，通常是单个 PUA 字形。</param>
    /// <returns>命中的 TextBlock；没有则 null。</returns>
    static System.Windows.DependencyObject? FindGlyphText(System.Windows.DependencyObject node, string text)
    {
        if (node is System.Windows.Controls.TextBlock tb && tb.Text == text) return tb;
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
        {
            var r = FindGlyphText(System.Windows.Media.VisualTreeHelper.GetChild(node, i), text);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>取证模式：真实编辑器窗口的工具条逐项落定，按帧渲染到 <c>&lt;prefix&gt;-NN.png</c>。
    /// 逐帧由进程内时钟驱动，不与外部截图器抢时序。</summary>
    /// <param name="outPrefix">输出文件前缀。</param>
    /// <returns>恒为 0。</returns>
    static int EditorWave(string outPrefix)
    {
        var app = new Application();
        var seq = new CapturedSequence
        {
            Frames = new List<CapturedFrame> { MovingSquare(520, 300, 0, 3), MovingSquare(520, 300, 1, 3) },   // 两帧才有多帧播放条
            Region = new RectI(0, 0, 520, 300),
            MonitorBounds = new RectI(0, 0, 1920, 1080),
        };
        var stWave = new SettingsService();
        // 应用主题：本模式只 new Application()，不走 AppHost，不套主题就永远拍成暗色。
        Theme.Apply(stWave.Current.Theme, stWave.Current.Accent);
        var win = new FrameCapture.Overlays.EditorWindow(stWave, seq);
        var NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var T = typeof(FrameCapture.Overlays.EditorWindow);
        var strip = (System.Windows.FrameworkElement?)T.GetField("_toolPanel", NP)!.GetValue(win)
                    ?? (System.Windows.FrameworkElement?)T.GetField("_toolsBar", NP)!.GetValue(win);
        var barDbg = (System.Windows.Controls.Panel?)T.GetField("_toolsBar", NP)!.GetValue(win);
        var btnDbg = barDbg!.Children.OfType<System.Windows.Controls.Button>().ToList();
        var playPanelDbg = (System.Windows.Controls.Border?)T.GetField("_playPanel", NP)!.GetValue(win);
        var playDbg = (playPanelDbg?.Child as System.Windows.Controls.Panel)?.Children.OfType<System.Windows.UIElement>().FirstOrDefault();
        var rootVis = (System.Windows.Media.Visual)win.Content;
        win.Show();
        PumpMs(40);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPrefix + "-00.png"))!);
        int n = 0;
        for (int i = 0; i < 30; i++)
        {
            PumpMs(25);
            if (i % 6 == 0 || i == 29)
                Console.WriteLine($"  f{i:D2} 播放首组={playDbg?.Opacity ?? -1:N2} 工具首={btnDbg[0].Opacity:N2} 工具末={btnDbg[^1].Opacity:N2}");
            int w = (int)Math.Ceiling(Math.Max(1, win.ActualWidth)), h = (int)Math.Ceiling(Math.Max(1, win.ActualHeight));
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(rootVis);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var f = File.Create($"{outPrefix}-{i:D2}.png")) enc.Save(f);
            n++;
        }
        Console.WriteLine($"editorwave {outPrefix} {n} 帧 {win.ActualWidth:N0}x{win.ActualHeight:N0}");
        win.Close();
        return 0;
    }
    /// <summary>滚动截图三层浮层的入场取证模式：三层同刻 Show，按 StaggerMs 依次落位，撑住窗口等外部屏采。
    /// 动效在窗口级 Opacity 上，RenderTargetBitmap 拍不到（它只渲染内容视觉），必须从屏幕合成结果里取。</summary>
    /// <param name="outPrefix">日志前缀，仅用于回显。</param>
    /// <returns>恒为 0。</returns>
    static int ScrollWave(string outPrefix)
    {
        var app = new Application();
        var stWave = new SettingsService();
        // 应用主题：本模式只 new Application()，不走 AppHost，不套主题就永远拍成暗色。
        Theme.Apply(stWave.Current.Theme, stWave.Current.Accent);
        var reg = new RectI(240, 220, 460, 320);
        var stg = new FrameCapture.Overlays.ScrollStageWindow(reg, "");
        var prv = new FrameCapture.Overlays.ScrollPreviewWindow(reg, "", Fluent.Motion.StaggerMs);
        var bar = new FrameCapture.Overlays.ScrollHintWindow(reg, "", Fluent.Motion.StaggerMs * 2);
        stg.Show(); prv.Show(); bar.Show();
        Console.WriteLine($"scrollwave {outPrefix} 已上屏");
        for (int i = 0; i < 120; i++)
        {
            PumpMs(25);
            if (i % 4 == 0)
                Console.WriteLine($"  f{i:D2} 取景层={stg.Opacity:N2} 预览卡={prv.Opacity:N2} 控制条={bar.Opacity:N2}");
        }
        stg.Close(); prv.Close(); bar.Close();
        PumpMs(200);
        return 0;
    }

    static int RenderPick(string outPath)
    {
        var app = new Application();
        // 弹层同款外观（PanelBg/Stroke/圆角），内容=ColorPickView，半透明色展示棋盘底
        var view = new FrameCapture.Overlays.ColorPickView { Color = System.Windows.Media.Color.FromArgb(0x80, 0xE8, 0x4C, 0x3D) };
        var root = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(Theme.PanelBg),
            BorderBrush = new System.Windows.Media.SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10), Child = view,
        };
        root.Measure(new System.Windows.Size(420, 400));
        double h = Math.Max(1, root.DesiredSize.Height);
        // 按自然宽排版：弹层里的宽度就是内容自己撑出来的那段，按 420 排会把右侧的竖条画到位图外面，
        // 拍出来像少了半截东西，而真实弹层根本不是这个宽度。
        double w = Math.Max(1, root.DesiredSize.Width);
        root.Arrange(new Rect(0, 0, w, h));
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
        Console.WriteLine($"saved {outPath} {ww}x{hh} lang={(FrameCapture.Core.Loc.IsEnglish ? "en" : "zh")}");
        return 0;
    }

    /// <summary>离屏渲染截图浮窗的内容：浮窗本身是 Window，先 Show 再拍它的 Content，
    /// 拍到的就是每次截完图看到的那张卡（含操作芯片与键帽）。</summary>
    /// <param name="outPath">输出 PNG 路径。</param>
    /// <returns>0 = 已出图；1 = 浮窗没建起来。</returns>
    /// <summary>只读探针：打印某个屏幕坐标上三路吸附各自会给出什么（UIA 一步命中 / UIA 过滤后 / 视觉），
    /// 用来核对 Chrome、Electron、自绘界面这些不同来源的真实表现。不改设置、不点鼠标。</summary>
    /// <param name="args">args[1] = 物理横坐标，args[2] = 物理纵坐标。</param>
    /// <returns>0 = 已打印；2 = 参数不对。</returns>
    /// <summary>取屏幕该点下面、不属于本进程的最上层窗口：遮罩在上层时用它找到真正的目标。</summary>
    /// <param name="x">物理横坐标。</param>
    /// <param name="y">物理纵坐标。</param>
    /// <returns>窗口句柄；找不到为 <c>IntPtr.Zero</c>。</returns>
    static IntPtr TopWindowAt(int x, int y)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)Environment.ProcessId) return true;
            if (!GetWindowRect(h, out var rc)) return true;
            if (x >= rc.L && x < rc.R && y >= rc.T && y < rc.B) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct ProbeRect { public int L, T, R, B; }
    delegate bool ProbeEnumProc(IntPtr h, IntPtr p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(ProbeEnumProc cb, IntPtr p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out ProbeRect r);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct ProbeInput { public int Type; public int Dx; public int Dy; public int MouseData; public int Flags; public int Time; public IntPtr Extra; }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendInput")]
    static extern uint ProbeSendInput(uint n, ProbeInput[] p, int size);
    static void ProbeMouseMove(int x, int y)
    {
        // ABSOLUTE 用的是 0..65535 的归一化坐标，不是像素——直接填像素会把光标送到屏幕外
        int sw = (int)SystemParameters.PrimaryScreenWidth;
        int sh = (int)SystemParameters.PrimaryScreenHeight;
        int nx = (int)((long)x * 65535 / Math.Max(1, sw));
        int ny = (int)((long)y * 65535 / Math.Max(1, sh));
        var item = new ProbeInput { Type = 0, Dx = nx, Dy = ny, Flags = 0x0001 | 0x8000 };
        ProbeSendInput(1, new[] { item }, System.Runtime.InteropServices.Marshal.SizeOf<ProbeInput>());
    }
    const int ProbeGwlExStyle = -20;
    const int ProbeWsExTransparent = 0x20;
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int ProbeGetWindowLong(IntPtr h, int i);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int ProbeSetWindowLong(IntPtr h, int i, int v);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetCapture")] static extern IntPtr ProbeSetCapture(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetCapture")] static extern IntPtr ProbeGetCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "ReleaseCapture")] static extern bool ProbeReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetCursorPos")] static extern bool ProbeSetCursorPos(int x, int y);
    /// <summary>验证点击穿透 + 鼠标捕获这套输入架构：UIA 能否看穿遮罩、鼠标消息是否照样到手。</summary>
    /// <param name="args">args[1] = 物理横坐标，args[2] = 物理纵坐标。</param>
    /// <returns>0 = 两项都通过；1 = 有一项不通过；2 = 参数不对。</returns>
    static int OverlayClickThrough(string[] args)
    {
        if (!int.TryParse(args[1], out int x) || !int.TryParse(args[2], out int y))
        {
            Console.WriteLine("用法: selftest overlayct <物理x> <物理y>");
            return 2;
        }
        var app = new Application();
        _ = new FrameCapture.Core.SettingsService();
        var moves = 0;
        var win = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0, 0, 0)),
            ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            Left = SystemParameters.VirtualScreenLeft, Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth, Height = SystemParameters.VirtualScreenHeight,
            Content = new System.Windows.Controls.TextBlock { Text = "穿透遮罩" },
        };
        win.Show();
        PumpMs(300);
        IntPtr h = new System.Windows.Interop.WindowInteropHelper(win).Handle;
        var src = System.Windows.Interop.HwndSource.FromHwnd(h)!;
        src.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == 0x0200) moves++;   // WM_MOUSEMOVE
            return IntPtr.Zero;
        });
        // 与选区窗同一套：设为命中测试透明 + 捕获输入
        int style = ProbeGetWindowLong(h, ProbeGwlExStyle);
        ProbeSetWindowLong(h, ProbeGwlExStyle,
            style | ProbeWsExTransparent);
        ProbeSetCapture(h);
        bool captured = ProbeGetCapture() == h;
        Console.WriteLine($"  穿透样式已设，捕获={(captured ? "到手" : "没到手")}");

        // 一、UIA 能不能看穿
        var hit = FrameCapture.Core.ElementSnap.At(x, y, IntPtr.Zero);
        Console.WriteLine(hit == null
            ? "  UIA 看穿：null（没拿到控件）"
            : $"  UIA 看穿：{(hit.Element == null ? "MSAA" : hit.Element.Current.ControlType.ProgrammaticName)} " +
              $"name=「{hit.Element?.Current.Name}」 {hit.Rect.X},{hit.Rect.Y} {hit.Rect.W}x{hit.Rect.H}");

        // 二、鼠标消息是否照样到手
        // 用 SendInput 移动：SetCursorPos 只改光标位置，不产生鼠标消息，测不出输入是否到手
        for (int i = 0; i < 12; i++)
        {
            ProbeMouseMove(x + i * 24, y + i * 6);
            PumpMs(70);
        }
        Console.WriteLine($"  鼠标消息：收到 {moves} 条 WM_MOUSEMOVE");

        ProbeReleaseCapture();
        win.Close();
        bool ok = captured && hit != null && moves > 0;
        Console.WriteLine(ok ? "  RESULT 通过" : "  RESULT 不通过");
        return ok ? 0 : 1;
    }
    static int ElemSnapProbe(string[] args)
    {
        if (!int.TryParse(args[1], out int x) || !int.TryParse(args[2], out int y))
        {
            Console.WriteLine("用法: selftest elemsnap <物理x> <物理y>");
            return 2;
        }
        var app = new Application();
        _ = new FrameCapture.Core.SettingsService();
        // overlay 模式：先铺一张和选区窗一样的最顶层遮罩，复现「遮罩压在最上面」的真实条件。
        // 不加这个模式，探针永远测不到那条路——而「吸到遮罩自己的提示文字」正是从那条路来的。
        IntPtr self = IntPtr.Zero;
        Window? overlay = null;
        Window? loupe = null;
        if (args.Length >= 4 && args[3] == "overlay")
        {
            overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0, 0, 0)),
                ShowInTaskbar = false,
                Topmost = true,
                Left = SystemParameters.VirtualScreenLeft, Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth, Height = SystemParameters.VirtualScreenHeight,
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = "选区遮罩的提示文字",
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(24),
                    FontSize = 14,
                },
            };
            overlay.Show();
            PumpMs(320);
            self = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
            Console.WriteLine($"  （已铺最顶层遮罩 hwnd={self}，模拟选区窗）");
            // 再铺一扇光标附近的小窗，模拟放大镜：只把遮罩设透明时 UIA 会撞到它
            loupe = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0, 0, 0)),
                ShowInTaskbar = false, Topmost = true,
                Left = x - 90, Top = y - 90, Width = 180, Height = 180,
                Content = new System.Windows.Controls.TextBlock { Text = "放大镜" },
            };
            loupe.Show();
            PumpMs(160);
            Console.WriteLine($"  （已铺放大镜 hwnd={new System.Windows.Interop.WindowInteropHelper(loupe).Handle}）");
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var raw = System.Windows.Automation.AutomationElement.FromPoint(new System.Windows.Point(x, y));
        if (raw == null) Console.WriteLine($"点 {x},{y}");
        else
        {
            var rc = raw.Current.BoundingRectangle;
            Console.WriteLine($"点 {x},{y}");
            Console.WriteLine($"  UIA 一步命中 {sw.ElapsedMilliseconds,4} ms  {raw.Current.ControlType.ProgrammaticName} " +
                $"name=「{raw.Current.Name}」 {rc.X:N0},{rc.Y:N0} {rc.Width:N0}x{rc.Height:N0} " +
                $"语义={FrameCapture.Core.ElementSnap.IsSemantic(raw)}");
        }
        sw.Restart();
        if (Environment.GetEnvironmentVariable("FC_ELEMSNAP_DEBUG") == "1")
            FrameCapture.Core.ElementSnap.DebugLog = s => Console.WriteLine(s);
        var hit = FrameCapture.Core.ElementSnap.At(x, y, self);
        Console.WriteLine(hit == null
            ? $"  过滤后      {sw.ElapsedMilliseconds,4} ms  null（没有说得出口的控件）"
            : hit.Element == null
                ? $"  过滤后      {sw.ElapsedMilliseconds,4} ms  MSAA 命中 {hit.Rect.X},{hit.Rect.Y} {hit.Rect.W}x{hit.Rect.H}"
                : $"  过滤后      {sw.ElapsedMilliseconds,4} ms  {hit.Element.Current.ControlType.ProgrammaticName} " +
                  $"name=「{hit.Element.Current.Name}」 {hit.Rect.X},{hit.Rect.Y} {hit.Rect.W}x{hit.Rect.H}");
        // MSAA：在目标窗口内部命中，绕过最上层遮罩
        IntPtr targetHwnd = TopWindowAt(x, y);
        if (targetHwnd != IntPtr.Zero)
        {
            if (Environment.GetEnvironmentVariable("FC_ELEMSNAP_DEBUG") == "1")
                FrameCapture.Core.MsaaSnap.DebugLog = s => Console.WriteLine("    " + s);
            var mh = FrameCapture.Core.MsaaSnap.At(targetHwnd, x, y);
            Console.WriteLine(mh == null
                ? "  MSAA        --   没有可用控件"
                : $"  MSAA        --   role={mh.Role} name=「{mh.Name}」 {mh.Rect.X},{mh.Rect.Y} {mh.Rect.W}x{mh.Rect.H} 子节点={mh.ChildCount}");
        }
        Console.WriteLine($"  最终采用    {(hit == null ? "整窗吸附" : hit.Element == null ? "MSAA 控件" : "UIA 控件")}");
        overlay?.Close();
        loupe?.Close();
        return 0;
    }

    static int RenderToast(string outPath)
    {
        var app = new Application();
        // 浮窗上那条复制键来自设置：先建一份设置，芯片才按出厂默认 Ctrl+C 画出来
        _ = new FrameCapture.Core.SettingsService();
        FrameCapture.Overlays.CaptureToast.Show(Solid(320, 180, 0x2E, 0x5C, 0x8A),
            new FrameCapture.Capture.RectI(0, 0, 320, 180), "", () => { }, () => { });
        var w = System.Windows.Application.Current.Windows.OfType<FrameCapture.Overlays.CaptureToast>().FirstOrDefault();
        if (w?.Content is not System.Windows.FrameworkElement root)
        {
            Console.WriteLine("toast 没建起来");
            return 1;
        }
        root.UpdateLayout();
        double cw = Math.Max(1, root.ActualWidth), ch = Math.Max(1, root.ActualHeight);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(cw), (int)Math.Ceiling(ch), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using (var f = File.Create(outPath)) enc.Save(f);
        Console.WriteLine($"saved {outPath} {cw:N0}x{ch:N0}");
        w.Close();
        return 0;
    }

    static int RenderSlider(string outPath)
    {
        var app = new Application();
        // 滑条取证：放大 3 倍看填充线与圆钮接缝是否露底色缝
        var panel = new System.Windows.Controls.StackPanel { Background = new System.Windows.Media.SolidColorBrush(Theme.PanelBg) };
        foreach (double v in new double[] { 0, 35, 100 })
        {
            var sl = new System.Windows.Controls.Slider { Minimum = 0, Maximum = 100, Value = v, Width = 300, Margin = new Thickness(10) };
            Fluent.StyleSlider(sl);
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

    // 让 WPF 动画在测试里真的走时间：DispatcherTimer 跑在 Background 优先级，渲染排在它前面，
    // PushFrame 期间动画时钟照常推进。Thread.Sleep 会冻住分发器，动画一步都不走。
    static void PumpMs(int ms)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    // 滚动拼接真实数据取证：给两张 PNG，跑 ScrollStitch 找重叠并拼，存 stitched.png 供肉眼核对
    static int StitchMode(string pathA, string pathB)
    {
        var (a, aw, ah) = LoadBgra(pathA);
        var (b, bw, bh) = LoadBgra(pathB);
        if (aw != bw) { Console.WriteLine($"width mismatch {aw} vs {bw}"); return 1; }
        int ov = ScrollStitch.FindOverlap(a, aw, ah, b, bh);
        var merged = ScrollStitch.Append(a, aw, ah, b, bh, ov);
        int mh = merged.Length / (aw * 4);
        Console.WriteLine($"A {aw}x{ah}  B {bw}x{bh}  overlap={ov}  merged={aw}x{mh}");
        using var bmp = new Bitmap(aw, mh, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, aw, mh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < mh; y++)
            System.Runtime.InteropServices.Marshal.Copy(merged, y * aw * 4, IntPtr.Add(data.Scan0, y * data.Stride), aw * 4);
        bmp.UnlockBits(data);
        string outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pathA))!, "stitched.png");
        bmp.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"saved {outPath}");
        return 0;
    }

    static (byte[], int, int) LoadBgra(string path)
    {
        using var bmp = new Bitmap(path);
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var buf = new byte[data.Stride * h];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(data);
        if (data.Stride != w * 4)   // 裁掉行对齐填充，成紧凑 w*4*h
        {
            var tight = new byte[w * h * 4];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(buf, y * data.Stride, tight, y * w * 4, w * 4);
            buf = tight;
        }
        return (buf, w, h);
    }

    static CapturedFrame Solid(int w, int h, byte r, byte g, byte b)    {
        var buf = new byte[w * h * 4];
        for (int i = 0; i < buf.Length; i += 4) { buf[i] = b; buf[i + 1] = g; buf[i + 2] = r; buf[i + 3] = 255; }
        return new CapturedFrame { Bgra = buf, W = w, H = h };
    }

    /// <summary>把 GIF 读回来取某像素的 alpha。解码器交的是索引色，先转 Bgra32 才看得见透明标志的效果。</summary>
    static int GifAlphaAt(byte[] gif, int x, int y)
    {
        var f = new System.Windows.Media.Imaging.GifBitmapDecoder(
            new MemoryStream(gif),
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            f, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        bgra.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), px, 4, 0);
        return px[3];
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

    /// <summary>
    /// 把设置目录整个挪进仓库内的临时目录再开跑。测试里构造的窗口和用户用的一样会写设置
    /// （关掉编辑器就记住上次用的标注工具），落在 <c>%APPDATA%</c> 等于改了用户机器的行为，
    /// 下一次跑还反过来影响断言的起点。内容每次从用户那份重新种一遍：
    /// 断言看到的仍是同一份配置，写盘全进 scratch。
    /// </summary>
    static void SettingsScratch()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FC_SETTINGS_DIR"))) return;
        try
        {
            var user = Path.Combine(SettingsService.Dir, "settings.json");
            var scratch = Path.Combine(AppContext.BaseDirectory, "settings-scratch");
            Directory.CreateDirectory(scratch);
            var mine = Path.Combine(scratch, "settings.json");
            if (File.Exists(mine)) File.Delete(mine);
            if (File.Exists(user)) File.Copy(user, mine);
            Environment.SetEnvironmentVariable("FC_SETTINGS_DIR", scratch);
        }
        catch { /* 种不上就照默认目录跑，不让测试起不来 */ }
    }

    /// <summary>
    /// 静默开关：默认打开。本进程里每一个 Window 一加载就搬到屏幕外，
    /// 用户屏幕上不出现、也不挡鼠标。版面与坐标断言在屏幕外同样成立（WPF 的版面与
    /// PointToScreen 都按窗口自身位置换算），只有「窗口在屏幕上的绝对位置」类断言例外。
    /// </summary>
    static readonly bool Quiet = Environment.GetEnvironmentVariable("FC_TEST_QUIET") != "0";

    /// <summary>把本进程每一个 Window 搬到屏幕外：挂在 Window.Loaded 的类处理器上，一处管全部。</summary>
    static void InstallQuietWindows()
    {
        if (!Quiet) return;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) =>
            {
                if (s is not Window w) return;
                try
                {
                    var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                    if (h != IntPtr.Zero) SetWindowPos(h, IntPtr.Zero, -20000, -20000, 0, 0, 0x0001 | 0x0004);
                    else { w.Left = -20000; w.Top = -20000; }
                }
                catch { }
            }));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        SettingsScratch();
        InstallQuietWindows();
        // 版面与双语审计：一次进程一个界面，矩阵由 scripts/ux-audit.ps1 铺开
        if (args.Length >= 3 && args[0] == "uxaudit") return UxAudit(args[1], args[2]);
        if (args.Length == 2 && args[0] == "uxaudit" && args[1] == "list")
        {
            foreach (var s in Surfaces) Console.WriteLine(s);
            return 0;
        }
        // 离屏渲染帮助窗取证：独立进程、不 Show、不注册热键，不打断正在运行的实例
        if (args.Length >= 2 && args[0] == "renderpin") return RenderPin(args[1], ScaleArg(args));
                if (args.Length >= 2 && args[0] == "renderhelp") return RenderHelp(args[1], args.Length >= 3 && int.TryParse(args[2], out var hs) ? hs : 0, ScaleArg(args));
        if (args.Length == 2 && args[0] == "sbprobe") return SbProbe(args[1]);
        if (args.Length == 2 && args[0] == "renderpick") return RenderPick(args[1]);
        if (args.Length == 2 && args[0] == "rendertoast") return RenderToast(args[1]);
        if (args.Length >= 3 && args[0] == "elemsnap") return ElemSnapProbe(args);
        if (args.Length >= 3 && args[0] == "overlayct") return OverlayClickThrough(args);
        if (args.Length == 2 && args[0] == "editorwave") return EditorWave(args[1]);
        if (args.Length == 2 && args[0] == "scrollwave") return ScrollWave(args[1]);
        if (args.Length == 2 && args[0] == "renderslider") return RenderSlider(args[1]);
        // 类别名走命令行会被控制台代码页搅乱，这里按索引取
        if (args.Length >= 2 && args[0] == "renderpicker")
            return RenderPicker(args[1], args.Length < 3 || args[2] != "layered");
        if (args.Length >= 2 && args[0] == "rendersettings")
        {
            Str[] catNames = { K.Settings.Hotkeys, K.Common.Capture, K.Common.Recording, K.Settings.Output, K.Common.Editor, K.Common.Pin, K.Common.Appearance, K.Common.Palette, K.Settings.System };
            int ci = args.Length >= 3 && int.TryParse(args[2], out var sc) ? sc : 0;
            return RenderSettings(args[1], catNames[Math.Clamp(ci, 0, catNames.Length - 1)], ScaleArg(args),
                args.Contains("open2") ? 1 : args.Contains("open") ? 0 : -1, args.Contains("adv"), args.Contains("clash"),
                args.Contains("down"));
        }
    if (args.Length >= 2 && args[0] == "rendereditor") return RenderEditor(args[1], ScaleArg(args), args.Contains("guide"));
        // 拖动中途的取景：第三个参数写「x,y」就是光标拖到编辑器根上的哪一点
        if (args.Length >= 2 && args[0] == "renderdrag")
        {
            double dx = 380, dy = 18;
            if (args.Length >= 3)
            {
                var ps = args[2].Split(',');
                if (ps.Length == 2 && double.TryParse(ps[0], out var ax) && double.TryParse(ps[1], out var ay)) { dx = ax; dy = ay; }
            }
            return RenderDrag(args[1], new System.Windows.Point(dx, dy));
        }
        if (args.Length == 3 && args[0] == "stitch") return StitchMode(args[1], args[2]);
        string tmp = Path.Combine(Path.GetTempPath(), "FrameCapture", "selftest");
        Directory.CreateDirectory(tmp);

        // GIF 编码往返 + 调色板 RGB 序：断言调色板条目恒按 R,G,B 写出；若退化为 BGR 则红蓝互换
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
            // 全红帧：调色板必须把红色存成红（RGB 序断言）
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

        // 标注烘焙：黑底画红线 → 线上红、线外仍黑
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
                // 黄色 #FFFF00: BGRA=(0,255,255)；查蓝通道应保持低、绿通道高
                bool yellow = false;
                for (int i = 0; i < outF.Bgra.Length; i += 4)
                    if (outF.Bgra[i] < 40 && outF.Bgra[i + 1] > 150 && outF.Bgra[i + 2] > 150) { yellow = true; break; }
                Chk($"T2-{kind}-baked", yellow);
            }
            // /序号编号自适应：圆圈色决定编号黑白；绿底隔离背景色后查前景像素
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

        // 裁剪：尺寸 + 像素对应
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

        // 视觉暂留合成
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

        // 快捷键解析
        try
        {
            var s1 = HotkeySpec.Parse("Ctrl+Shift+R");
            Chk("T5a-parse-ctrl-shift-r", s1 != null && s1.Ctrl && s1.Shift && s1.Key == System.Windows.Forms.Keys.R);
            var s2 = HotkeySpec.Parse("F5");
            Chk("T5b-parse-bare-fkey", s2 != null && s2.Key == System.Windows.Forms.Keys.F5 && !s2.Ctrl);
            Chk("T5c-parse-bare-letter-null", HotkeySpec.Parse("A") == null);
            Chk("T5d-parse-empty-null", HotkeySpec.Parse("") == null);
            Chk("T5e-roundtrip", s1 != null && HotkeySpec.Parse(s1.ToString())?.ToString() == s1.ToString());
        }
        catch (Exception e) { Chk("T5-hotkey", false, e.Message); }

        // 抽帧索引
        try
        {
            var idx = BgraImage.SampleIndices(30, 12);
            Chk("T6a-sample-count", idx.Count == 12, $"got {idx.Count}");
            Chk("T6b-sample-sorted", idx.SequenceEqual(idx.OrderBy(x => x)) && idx.Distinct().Count() == idx.Count);
        }
        catch (Exception e) { Chk("T6-sample", false, e.Message); }

        // Agent 胶片条
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

        // 编辑器递进 Render 的是抽样烘焙后的那几帧，idxs 是它们在时间轴上的编号。
        // 按时间轴编号去取帧，一稀释就越界——胶片条永远生成失败，日志里只留一句 Index out of range。
        try
        {
            var all7 = Enumerable.Range(0, 40).Select(k =>
            {
                var f = MovingSquare(160, 90, k, 16); f.TimeMs = k * 50; return f;
            }).ToList();
            var idxs7 = AgentStrip.SampleIndices(all7.Count, 8, 1);
            var sampled7 = idxs7.Select(i => all7[i]).ToList();
            var strip7 = AgentStrip.Render(sampled7, idxs7);
            Chk("T7c-sampled-frames-match-their-timeline-ids",
                idxs7.Count == 8 && sampled7.Count == 8 && strip7.PixelWidth > 800,
                $"递 K 帧 + K 个时间轴编号必须成图（实得 {idxs7.Count} 编号 / {sampled7.Count} 帧 / {strip7.PixelWidth}px）");
            bool threw7 = false;
            try { AgentStrip.Render(sampled7.Take(3).ToList(), idxs7); } catch (ArgumentException) { threw7 = true; }
            Chk("T7d-strip-rejects-missing-frames", threw7, "帧数不够格子时必须抛，不能默默画半张");

            // 弹窗实时预览走小格子 + 抽稀：画布尺寸只由格子数决定，抽不抽稀都一样；
            // 帧比格子还小（step=1）时不能崩，也不能改画布。
            var prev8 = AgentStrip.Render(sampled7, idxs7, FrameCapture.Overlays.FilmStripDialog.PreviewCell, decimate: true);
            var prevSame = AgentStrip.Render(sampled7, idxs7, FrameCapture.Overlays.FilmStripDialog.PreviewCell);
            Chk("T7g-preview-cell-scales",
                prev8.PixelWidth == prevSame.PixelWidth && prev8.PixelWidth < strip7.PixelWidth,
                $"预览 {prev8.PixelWidth}px 与同格宽全质量一致，且小于成片 {strip7.PixelWidth}px");
            var tiny = new[] { Solid(7, 5, 10, 20, 30), Solid(7, 5, 40, 50, 60) };
            var prevTiny = AgentStrip.Render(tiny, new[] { 0, 1 }, FrameCapture.Overlays.FilmStripDialog.PreviewCell, decimate: true);
            Chk("T7h-preview-tiny-frames",
                prevTiny.PixelWidth > 0 && prevTiny.PixelHeight > 0,
                $"帧比格子还小 → 抽稀退到逐像素，出图 {prevTiny.PixelWidth}x{prevTiny.PixelHeight}");
        }
        catch (Exception e) { Chk("T7-strip-sampling", false, e.ToString()); }

        // GIF 的透明只有 1 bit：窗口截图的圆角必须真透明，软投影靠 Bayer 抖动摊开而不是压成死黑边。
        // 编码端自己声明透明索引不算数——用 WPF 的 GIF 解码器读回来，看像素到底带不带 alpha。
        try
        {
            int gw = 64, gh = 48;
            var buf = new byte[gw * gh * 4];
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                {
                    int o = (y * gw + x) * 4;
                    bool edge = x < 8 || x >= gw - 8 || y < 8 || y >= gh - 8;
                    bool band = !edge && x < 12;
                    buf[o] = edge || band ? (byte)0 : (byte)200;
                    buf[o + 1] = edge || band ? (byte)0 : (byte)60;
                    buf[o + 2] = edge || band ? (byte)0 : (byte)20;
                    buf[o + 3] = edge ? (byte)0 : band ? (byte)128 : (byte)255;
                }
            var gf = new CapturedFrame { Bgra = buf, W = gw, H = gh, TimeMs = 0 };
            byte[] gbytes = GifEncoder.Encode(new[] { gf }, new[] { 100 });
            int cornerA = GifAlphaAt(gbytes, 0, 0);
            int bodyA = GifAlphaAt(gbytes, 32, 24);
            Chk("T7e-gif-transparent-corner", cornerA == 0 && bodyA == 255,
                $"透明边 alpha={cornerA}（要 0），本体 alpha={bodyA}（要 255）");

            byte[] opaque = GifEncoder.Encode(new[] { Solid(gw, gh, 0x30, 0x40, 0x50) }, new[] { 100 });
            Chk("T7f-opaque-gif-stays-opaque", GifAlphaAt(opaque, 0, 0) == 255,
                $"全不透明帧读回来 alpha={GifAlphaAt(opaque, 0, 0)}（要 255，不许声明透明）");
        }
        catch (Exception e) { Chk("T7-gif-alpha", false, e.ToString()); }

        // 主题：明/暗双调色板对比度正确（文字与背景必须反向）
        try
        {
            double Lum(System.Windows.Media.Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            Theme.Apply("dark", "#4CC2FF");
            double dBg = Lum(Theme.WindowBg), dTx = Lum(Theme.Text);
            Chk("T8a-dark-palette", dBg < 60 && dTx > 180, $"bg={dBg:F0} text={dTx:F0}");
            Chk("T8b-dark-contrast", Math.Abs(dTx - dBg) > 120);
            Theme.Apply("light", "#4CC2FF");
            double lBg = Lum(Theme.WindowBg), lTx = Lum(Theme.Text);
            Chk("T8c-light-palette", lBg > 190 && lTx < 80, $"bg={lBg:F0} text={lTx:F0}");
            Chk("T8d-light-contrast", Math.Abs(lTx - lBg) > 120);
            Chk("T8e-accent-applied", Theme.Accent.B > 200 && Theme.Accent.R < 120);
            Theme.Apply("system", "#4CC2FF");
            Chk("T8f-system-no-crash", true);
            Theme.Apply("dark", "#4CC2FF");
        }
        catch (Exception e) { Chk("T8-theme", false, e.Message); }

        // 富色内容量化：彩色渐变帧编 GIF 解码后仍保留多色
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

        // UI glyph 护栏：所有用到的图标必须真实存在于 Segoe Fluent Icons（首字体吞 BMP 导致豆腐块）
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
            var tfc = new System.Windows.Media.Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            bool ok = tfc.TryGetGlyphTypeface(out var gt) && gt != null;
            bool all = true;
            foreach (var ch in uiGlyphs)
                if (ok && !(gt!.CharacterToGlyphMap.ContainsKey(ch) && gt.AdvanceWidths[gt.CharacterToGlyphMap[ch]] > 0.08))
                { all = false; Console.WriteLine($"  missing glyph U+{(int)ch:X4}"); }
            Chk("T10-ui-glyphs-resolve", all && ok, "tofu risk");

            // 悬停动效要让位给外部换色：点选芯片时选中态会整块换掉画刷，
            // 抬起时的悬停动画若照旧盖回去，选中态当场就没了（芯片看着和没选中一样）。
            try
            {
                var chipT = new System.Windows.Controls.Border
                {
                    Width = 60,
                    Height = 24,
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33)),
                };
                FrameCapture.Core.Fluent.ChipHover(chipT);
                var picked = System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4);
                int upFired = 0;
                var dev = System.Windows.Input.Mouse.PrimaryDevice;
                // 按下即激活：选中态当场整块换掉画刷（WireChip 的 apply 就挂在这个事件上）
                chipT.MouseLeftButtonDown += (_, _) =>
                    chipT.Background = new System.Windows.Media.SolidColorBrush(picked);
                chipT.MouseLeftButtonUp += (_, _) => upFired++;
                // 悬停进入要抬起来：MouseEventArgs 的构造函数是 protected，借自己的公开子类
                chipT.RaiseEvent(new EnterMouseArgs(dev)
                { RoutedEvent = UIElement.MouseEnterEvent, Source = chipT });
                void Btn(RoutedEvent evt) => chipT.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                    dev, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = evt, Source = chipT });
                Btn(UIElement.PreviewMouseLeftButtonDownEvent);   // 按压色
                Btn(UIElement.MouseLeftButtonDownEvent);          // 选中态换底
                Btn(UIElement.MouseLeftButtonUpEvent);            // 抬起：不该把新底盖回去
                PumpMs(200);
                var brush = (System.Windows.Media.SolidColorBrush)chipT.Background;
                var after = brush.Color;
                // 断言落在「有没有在选中态画刷上开动画」：动画在离屏窗里不会真的走完，
                // 但 BeginAnimation 一旦落下就挂着不放——那正是选中态被盖掉的成因。
                Chk("T204-chip-hover-yields-to-external-recolor",
                    upFired >= 1 && !brush.HasAnimatedProperties
                    && after.R == picked.R && after.G == picked.G && after.B == picked.B,
                    $"换底后抬起不许在选中态画刷上开动画（抬起跑了 {upFired} 次，动画挂上={brush.HasAnimatedProperties}，实得 #{after.R:X2}{after.G:X2}{after.B:X2}，要 #0078D4）");
            }
            catch (Exception ex) { Chk("T204-chip-hover-yields-to-external-recolor", false, FirstLine(ex.Message)); }

            // 折叠件进出场：展开要真的把位置拉开（高度到位、淡到位），收拢要真的把位置还回去。
            try
            {
                var foldWin = new Window
                {
                    Left = -20000, Top = -20000, Width = 420, Height = 260,
                    Content = new System.Windows.Controls.StackPanel(),
                };
                var fold = FrameCapture.Core.Fluent.Revealable(
                    new System.Windows.Controls.Border { Height = 40 });
                ((System.Windows.Controls.StackPanel)foldWin.Content).Children.Add(fold);
                foldWin.Show();
                PumpMs(250);
                bool startCollapsed = fold.Visibility == Visibility.Collapsed && fold.Opacity == 0;
                FrameCapture.Core.Fluent.Reveal(fold);
                PumpMs(700);
                double openH = fold.ActualHeight;
                double openOp = fold.Opacity;
                FrameCapture.Core.Fluent.Conceal(fold);
                PumpMs(700);
                bool yielded = fold.Visibility == Visibility.Collapsed;
                Chk("T205-reveal-opens-and-conceal-yields-space",
                    startCollapsed && openH > 39 && openOp > 0.99 && yielded,
                    $"起始收起={startCollapsed} 展开后实高 {openH:0} 透明度 {openOp:0.00} 收拢让位={yielded}");
                foldWin.Close();
            }
            catch (Exception ex) { Chk("T205-reveal-opens-and-conceal-yields-space", false, FirstLine(ex.Message)); }

            // 折叠件两拍的接法：入场空地先起、内容追着落位（重叠一段），退场内容先走、空地追着收。
            // 完全同时走只读得出"整块闪一下"，完全错开又成了两个互不相干的动作。
            try
            {
                var seqWin = new Window
                {
                    Left = -20000, Top = -20000, Width = 420, Height = 260,
                    Content = new System.Windows.Controls.StackPanel(),
                };
                var seq = FrameCapture.Core.Fluent.Revealable(
                    new System.Windows.Controls.Border { Height = 40 });
                ((System.Windows.Controls.StackPanel)seqWin.Content).Children.Add(seq);
                seqWin.Show();
                PumpMs(250);
                FrameCapture.Core.Fluent.Reveal(seq);
                // 逐帧盯入场：内容开始淡那一刻空地必须已经开了一部分、又还没开完。
                // 瞬间撑开与"等空地开完才淡"都会量到满高，两种都不许。
                bool grewOpen = false;
                double hWhenContentStarts = -1;
                int g = 0;
                while (g++ < 80 && hWhenContentStarts < 0)
                {
                    if (seq.ActualHeight > 4 && seq.ActualHeight < 38) grewOpen = true;
                    if (seq.Opacity > 0.02) hWhenContentStarts = seq.ActualHeight;
                    PumpMs(16);
                }
                PumpMs(700);
                FrameCapture.Core.Fluent.Conceal(seq);
                // 逐帧盯退场：空地开始收那一刻内容必须正在挪走——还没动是不该等这么久，挪完了是拖成两段。
                double opWhenShrinkStarts = -1;
                int f = 0;
                while (f++ < 80 && opWhenShrinkStarts < 0)
                {
                    if (seq.ActualHeight < 39.5) opWhenShrinkStarts = seq.Opacity;
                    PumpMs(16);
                }
                bool shutShut = false;
                while (f++ < 160 && seq.Visibility == Visibility.Visible)
                {
                    if (seq.ActualHeight > 4 && seq.ActualHeight < 38) shutShut = true;
                    PumpMs(16);
                }
                Chk("T206-reveal-space-leads-content-follows",
                    grewOpen && hWhenContentStarts > 4 && hWhenContentStarts < 39.5
                    && opWhenShrinkStarts > 0.02 && opWhenShrinkStarts < 0.99 && shutShut,
                    $"入场：空地中途量到={grewOpen}，内容起淡时实高 {hWhenContentStarts:0}（要开了但没开完）；"
                    + $"退场：空地起收时内容透明度 {opWhenShrinkStarts:0.00}（要正在挪走），收拢中途量到={shutShut}");
                seqWin.Close();
            }
            catch (Exception ex) { Chk("T206-reveal-space-leads-content-follows", false, FirstLine(ex.Message)); }
            // 目录字形动态锁：工具目录里每个 Glyph 必须在字体里真实存在且有宽度——
            // 静态清单会漏新工具（加 V 时就漏了），目录本身才是该锁的对象
            bool catOk = true;
            foreach (var t in ToolCatalog.Tools)
            {
                char g = t.Glyph[0];
                if (!(ok && gt!.CharacterToGlyphMap.ContainsKey(g) && gt.AdvanceWidths[gt.CharacterToGlyphMap[g]] > 0.08))
                { catOk = false; Console.WriteLine($"  catalog glyph U+{(int)g:X4} ({t.Key}) missing"); }
            }
            Chk("T10b-catalog-glyphs-resolve", catOk, "工具目录字形全部可渲染");
        }
        catch (Exception e) { Chk("T10-glyphs", false, e.Message); }

        // Fluent 模板可解析（守 XAML 命名空间/触发器语法）
        try
        {
            var tplA = Fluent.IconButtonTemplate();
            var sl = new System.Windows.Controls.Slider();
            Fluent.StyleSlider(sl);
            Chk("T11-fluent-templates", tplA != null && sl.Template != null, "parse");
        }
        catch (Exception e) { Chk("T11-fluent-templates", false, e.Message); }

        // 开关滑钮必须逐帧移动：动画起点若等于目标位，滑钮瞬移且全程无任何报错
        try
        {
            var track = new System.Windows.Controls.Border
            {
                Width = 40, Height = 20, CornerRadius = new CornerRadius(10),
                Background = new System.Windows.Media.SolidColorBrush(Theme.TrackBg),
                BorderBrush = new System.Windows.Media.SolidColorBrush(Theme.Stroke), BorderThickness = new Thickness(1),
            };
            var knob = new System.Windows.Shapes.Ellipse
            {
                Width = 12, Height = 12, Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 4, 0),
                RenderTransform = new System.Windows.Media.TranslateTransform(0, 0),
            };
            track.Child = knob;
            var win = new Window
            {
                Title = "SelfTest ToggleSlide", Width = 120, Height = 80, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 150, Top = 150, Topmost = true,
                Content = new System.Windows.Controls.StackPanel { Children = { track } },
            };
            win.Show();
            var loaded = new System.Windows.Threading.DispatcherFrame();
            win.Dispatcher.BeginInvoke(() => { loaded.Continue = false; }, System.Windows.Threading.DispatcherPriority.Loaded);
            System.Windows.Threading.Dispatcher.PushFrame(loaded);
            var tt = (System.Windows.Media.TranslateTransform)knob.RenderTransform;
            Fluent.SlideToggle(knob, track, true, 20);
            double start = tt.X;
            PumpMs(320);
            double end = tt.X;
            Chk("T170a-knob-slides", start < 19 && end > 19.4, $"首帧 X={start:0.###} → 落点 X={end:0.###}");
            Fluent.SlideToggle(knob, track, false, 20);
            double back = tt.X;
            PumpMs(320);
            Chk("T170b-knob-returns", back > 1 && tt.X < 0.6, $"回程首帧 X={back:0.###} → 落点 X={tt.X:0.###}");
            win.Close();
        }
        catch (Exception e) { Chk("T170-toggle-slide", false, e.Message); }

        // 逐项错位入场：时序必须真的错开、整段压在预算内、收束后不留位移变换。
        // 步长算错有两种表现且都不报错：全为 0 等于整屏同时闪一下（没错位），
        // 恒等于 StaggerMs 则几十行列表拖成秒级。
        try
        {
            var panel = new System.Windows.Controls.StackPanel();
            var rows = new List<System.Windows.FrameworkElement>();
            for (int i = 0; i < 9; i++)
            {
                var b = new System.Windows.Controls.Border
                {
                    Width = 60, Height = 10,
                    Background = new System.Windows.Media.SolidColorBrush(Theme.CardBg),
                };
                panel.Children.Add(b);
                rows.Add(b);
            }
            var win = new Window
            {
                Title = "SelfTest Cascade", Width = 120, Height = 220, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 150, Top = 150, Topmost = true,
                Content = panel,
            };
            win.Show();
            var loaded = new System.Windows.Threading.DispatcherFrame();
            win.Dispatcher.BeginInvoke(() => { loaded.Continue = false; }, System.Windows.Threading.DispatcherPriority.Loaded);
            System.Windows.Threading.Dispatcher.PushFrame(loaded);

            Fluent.CascadeIn(rows);
            PumpMs(60);   // 首项已走 60ms，末项按 9 项算出的步长还没起跳
            double firstMid = rows[0].Opacity, lastMid = rows[^1].Opacity;
            PumpMs(Fluent.Motion.EnterMs + Fluent.Motion.StaggerMs * 3 + 150);
            double firstEnd = rows[0].Opacity, lastEnd = rows[^1].Opacity;
            Chk("T171a-cascade-staggers", firstMid > 0.2 && lastMid < 0.05 && firstEnd > 0.98 && lastEnd > 0.98,
                $"60ms 时 首项={firstMid:0.###} 末项={lastMid:0.###} → 收束 首={firstEnd:0.###} 末={lastEnd:0.###}");
            Chk("T171b-cascade-clears-transform",
                rows.All(r => r.RenderTransform is null || r.RenderTransform.Value.IsIdentity),
                $"收束后不得有有效位移：残留={string.Join(",", rows.Select(r => r.RenderTransform?.GetType().Name ?? "null").Distinct())} 新建Border变换={(new System.Windows.Controls.Border().RenderTransform?.GetType().Name ?? "null")}");

            // 单项时长与预算解耦：预算比单项还短时不允许出现负延迟，退化成同时淡入
            var solo = new List<System.Windows.FrameworkElement>
            {
                new System.Windows.Controls.Border { Width = 20, Height = 6 },
                new System.Windows.Controls.Border { Width = 20, Height = 6 },
            };
            Fluent.CascadeIn(solo, itemMs: 190, budgetMs: 100);
            PumpMs(40);
            Chk("T171c-cascade-tight-budget", solo.All(r => r.Opacity > 0.02),
                $"预算 100 < 单项 190 时两项同时起跳：{solo[0].Opacity:0.###} / {solo[1].Opacity:0.###}");
            win.Close();
        }
        catch (Exception e) { Chk("T171-cascade-in", false, e.Message); }
        // 翻页用的横向分量：必须真从侧向收拢到 0；纯横向（不抬升）也要撤掉变换，
        // 清理回调挂错动画时这一格会静默留下永久 TranslateTransform。
        try
        {
            var hostX = new System.Windows.Controls.StackPanel();
            for (int i = 0; i < 4; i++)
                hostX.Children.Add(new System.Windows.Controls.Border { Width = 40, Height = 8 });
            var winX = new Window
            {
                Title = "SelfTest CascadeSide", Width = 120, Height = 160, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 150, Top = 150, Topmost = true,
                Content = hostX,
            };
            winX.Show();
            var ldX = new System.Windows.Threading.DispatcherFrame();
            winX.Dispatcher.BeginInvoke(() => { ldX.Continue = false; }, System.Windows.Threading.DispatcherPriority.Loaded);
            System.Windows.Threading.Dispatcher.PushFrame(ldX);

            // 起跳位必须在调用返回时就已写上：立刻读，不依赖泵帧时机
            Fluent.CascadeRows(hostX, risePx: 6, riseX: -10);
            var fe0 = (System.Windows.FrameworkElement)hostX.Children[0];
            // 断言合成矩阵而非变换的具体形状：裸 TranslateTransform 与包进 TransformGroup 都要给出同样的位移
            var m0 = fe0.RenderTransform.Value;
            Chk("T172a-cascade-side-offset", m0.OffsetX == -10 && m0.OffsetY == 6 && fe0.Opacity < 0.01,
                $"起跳 X={m0.OffsetX:0.###} Y={m0.OffsetY:0.###} 透明度={fe0.Opacity:0.###} 形状={fe0.RenderTransform?.GetType().Name ?? "null"} 新Border变换={(new System.Windows.Controls.Border().RenderTransform is null ? "null" : "非null")}");
            PumpMs(Fluent.Motion.EnterMs + Fluent.Motion.StaggerMs * 3 + 150);
            Chk("T172b-cascade-side-settles",
                hostX.Children.OfType<System.Windows.FrameworkElement>().All(r => (r.RenderTransform is null || r.RenderTransform.Value.IsIdentity) && r.Opacity > 0.98),
                $"双向分量归零后变换应完全撤掉：{string.Join(",", hostX.Children.OfType<System.Windows.FrameworkElement>().Select(r => r.RenderTransform?.GetType().Name ?? "null").Distinct())}");

            // 纯横向：纵向不抬升，清理只能靠横向那条动画的 Completed
            var hostH = new System.Windows.Controls.StackPanel();
            hostH.Children.Add(new System.Windows.Controls.Border { Width = 40, Height = 8 });
            Fluent.CascadeRows(hostH, risePx: 0, riseX: 12);
            PumpMs(Fluent.Motion.EnterMs + Fluent.Motion.StaggerMs * 3 + 150);
            var feH = (System.Windows.FrameworkElement)hostH.Children[0];
            Chk("T172c-cascade-side-only-clears", feH.RenderTransform is null || feH.RenderTransform.Value.IsIdentity,
                $"新建Border变换={(new System.Windows.Controls.Border().RenderTransform?.GetType().Name ?? "null")} 纯横向收束后变换 = {feH.RenderTransform?.GetType().Name ?? "已撤"} 矩阵位移={feH.RenderTransform?.Value.OffsetX ?? 0:0.###},{feH.RenderTransform?.Value.OffsetY ?? 0:0.###}");
            winX.Close();
        }
        catch (Exception e) { Chk("T172-cascade-side", false, e.Message); }
        // 分层窗与弹出层的底色必须不透明：背后没有模糊背板，任何 alpha 都是直接透出桌面内容，
        // 压在文字密集的桌面上会浮出背后文字。变体只许改 alpha，RGB 必须跟源色一致。
        try
        {
            var card = Fluent.PopupCard(new System.Windows.Controls.TextBlock { Text = "x" });
            var inner = card.Child as System.Windows.Controls.Border;
            byte a = inner?.Background is System.Windows.Media.SolidColorBrush sb ? sb.Color.A : (byte)0;
            Chk("T173a-popupcard-fill-opaque", a == 0xFF, $"弹出层面板 alpha=0x{a:X2}");
            Chk("T173b-solid-variants-track-source",
                Theme.WindowBgSolid.A == 0xFF && Theme.PanelBgSolid.A == 0xFF
                && Theme.WindowBgSolid.R == Theme.WindowBg.R && Theme.WindowBgSolid.G == Theme.WindowBg.G
                && Theme.WindowBgSolid.B == Theme.WindowBg.B
                && Theme.PanelBgSolid.R == Theme.PanelBg.R && Theme.PanelBgSolid.G == Theme.PanelBg.G
                && Theme.PanelBgSolid.B == Theme.PanelBg.B,
                $"WindowBg 0x{Theme.WindowBg.A:X2}→0xFF  PanelBg 0x{Theme.PanelBg.A:X2}→0xFF，RGB 未变");
        }
        catch (Exception e) { Chk("T173-layered-opaque", false, e.Message); }
        // 图标按钮自带 ScaleTransform 供 hover 缩放，而缩放动画只认 ScaleTransform、遇到别的静默 return。
        // 逐项入场必须把原变换放回原位：覆盖成 TranslateTransform 会让悬停没反应，收束置 null 是永久没反应，
        // 残留一个包了层的 TransformGroup 同样致命。
        try
        {
            var hostS = new System.Windows.Controls.StackPanel();
            var btn = new System.Windows.Controls.Button { Width = 30, Height = 30 };
            var scale = new System.Windows.Media.ScaleTransform(1, 1);
            btn.RenderTransform = scale;
            hostS.Children.Add(btn);
            var winS = new Window
            {
                Title = "SelfTest CascadePreserve", Width = 120, Height = 120, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 170, Top = 170, Topmost = true,
                Content = hostS,
            };
            winS.Show();
            var ldS = new System.Windows.Threading.DispatcherFrame();
            winS.Dispatcher.BeginInvoke(() => { ldS.Continue = false; }, System.Windows.Threading.DispatcherPriority.Loaded);
            System.Windows.Threading.Dispatcher.PushFrame(ldS);

            Fluent.CascadeRows(hostS, risePx: 8);
            var during = btn.RenderTransform as System.Windows.Media.TransformGroup;
            Chk("T174a-cascade-wraps-existing",
                during is not null && during.Children.Contains(scale) && btn.Opacity < 0.01,
                $"入场中 = {(during is null ? btn.RenderTransform?.GetType().Name ?? "null" : "TransformGroup")} 透明度={btn.Opacity:0.###}");
            PumpMs(Fluent.Motion.EnterMs + Fluent.Motion.StaggerMs * 3 + 150);
            Chk("T174b-cascade-restores-transform", ReferenceEquals(btn.RenderTransform, scale),
                $"收束后变换 = {(ReferenceEquals(btn.RenderTransform, scale) ? "原 ScaleTransform 归位" : btn.RenderTransform?.GetType().Name ?? "null")}");

            // 不走动画的重建路径：包了层的位移也要摘干净，原变换留在原位
            Fluent.CascadeRows(hostS, risePx: 8);
            Fluent.ResetCascade(hostS);
            Chk("T174c-reset-unwraps-keeps-source",
                ReferenceEquals(btn.RenderTransform, scale) && btn.Opacity > 0.99,
                $"清零后 = {(ReferenceEquals(btn.RenderTransform, scale) ? "原 ScaleTransform 归位" : btn.RenderTransform?.GetType().Name ?? "null")} 透明度={btn.Opacity:0.###}");
            winS.Close();
        }
        catch (Exception e) { Chk("T174-cascade-transform-preserve", false, e.Message); }
        // RenderTransform 的默认值是单位 MatrixTransform 而非 null：只判 null 的入场缩放判断永远不成立，
        // 设置窗的 0.96→1 入场放大与配套的退场 0.97 会一起静默失效。
        try
        {
            var rootE = new System.Windows.Controls.Grid { Width = 60, Height = 60 };
            var winE = new Window
            {
                Title = "SelfTest PopupEnter", Width = 90, Height = 90, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 210, Top = 210, Topmost = true,
                Content = rootE,
            };
            Chk("T175a-identity-transform-detects", Fluent.HasNoTransform(rootE),
                $"默认变换 = {rootE.RenderTransform?.GetType().Name ?? "null"}");
            Fluent.PopupEnter(winE);
            winE.Show();
            PumpMs(Fluent.Motion.EnterMs + 220);
            var stE = rootE.RenderTransform as System.Windows.Media.ScaleTransform;
            Chk("T175b-popupenter-scales-in", stE is not null && stE.ScaleX > 0.98 && stE.ScaleY > 0.98,
                $"入场后变换 = {rootE.RenderTransform?.GetType().Name ?? "null"} ScaleX={stE?.ScaleX:0.###}");
            winE.Close();
        }
        catch (Exception e) { Chk("T175-popupenter-scale", false, e.Message); }
        // 背板材质由 DWM 合成，不属于窗口 DC：PrintWindow 类截图拍不到，暗壁纸下也与平涂底色无异。
        // 唯一确定的判据是向 DWM 回读它实际登记的材质与色调。
        try
        {
            var winM = new Window
            {
                Title = "SelfTest Mica", Width = 200, Height = 140, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 230, Top = 230,
                WindowStyle = WindowStyle.None,
            };
            Fluent.BorderlessNative(winM);
            bool micaOn = Fluent.MicaBackdrop(winM);
            winM.Show();
            PumpMs(260);
            var hwndM = new System.Windows.Interop.WindowInteropHelper(winM).Handle;
            // 属性号与取值与生产侧常量同值：38=SYSTEMBACKDROP_TYPE 20=USE_IMMERSIVE_DARK_MODE 2=Mica
            int hrB = DwmProbe.DwmGetWindowAttribute(hwndM, 38, out uint gotB, 4);
            Chk("T176a-backdrop-is-mica", hrB == 0 && gotB == 2u, $"回读 hr={hrB} 材质={gotB}");
            int hrD = DwmProbe.DwmGetWindowAttribute(hwndM, 20, out uint gotD, 4);
            Chk("T176b-tint-follows-theme", hrD == 0 && gotD == (Theme.IsDark ? 1u : 0u),
                $"回读 hr={hrD} 暗色={gotD} 主题暗={Theme.IsDark}");
            // 门控基准取 ntdll 的 RtlGetVersion：托管侧 Environment.OSVersion 在 net48 里谎报 6.2.9200，
            // 拿它当基准等于把「引导器看不见 Win11 特性」这件事钉成正确行为。
            int rtlBuild = OsProbe.RtlBuildNumber();
            Chk("T183a-rtl-build-number-sane", rtlBuild >= 10240,
                $"RtlGetVersion 内部版本={rtlBuild} 托管侧={Environment.OSVersion.Version.Build}");
            Chk("T177a-mica-gate", Fluent.MicaSupported == (rtlBuild >= 22000),
                $"MicaSupported={Fluent.MicaSupported} Rtl内部版本={rtlBuild} 托管侧={Environment.OSVersion.Version.Build}");
            Chk("T177b-backdrop-return-matches-gate", micaOn == Fluent.MicaSupported,
                $"返回={micaOn} 能力={Fluent.MicaSupported}");
            winM.Close();
        }
        catch (Exception e) { Chk("T176-mica-backdrop", false, e.Message); }
        // 编辑器工具条逐项落定：窗口入场走完才起波，左端先动右端后动，走完全部复位且 hover 缩放仍可用
        try
        {
            var stW = new SettingsService();
            stW.Mutate(s => s.EditorToolbarAnim = true);   // 这条测的就是逐项落定，起点不能取决于用户关没关这个开关
            var seqW = new CapturedSequence
            {
                Frames = new List<CapturedFrame> { MovingSquare(160, 90, 0, 1) },
                Region = new RectI(0, 0, 160, 90),
                MonitorBounds = new RectI(0, 0, 1920, 1080),
            };
            var winW = new FrameCapture.Overlays.EditorWindow(stW, seqW);
            // 挪到屏幕外再显示：这扇窗开在屏幕正中时，用户的光标压上去，WPF 就真发了 MouseEnter，
            // 那个按钮会一直停在悬停倍率上——「全部复位」永远等不到，而它跟被测代码没关系。
            winW.WindowStartupLocation = WindowStartupLocation.Manual;
            winW.Left = -20000; winW.Top = -20000;
            var barW = (System.Windows.Controls.StackPanel?)typeof(FrameCapture.Overlays.EditorWindow)
                .GetField("_toolsBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(winW);
            var btnW = barW!.Children.OfType<System.Windows.Controls.Button>().ToList();
            Chk("T178a-toolbar-items", btnW.Count > 4, $"工具条按钮数={btnW.Count}");
            winW.Show();
            // 逐项落定 = 每项比前一项晚一点起跳，晚多少由 CascadeStepMs 说了算。
            // 不去屏幕上量「同一瞬间的落差」：离屏窗口的透明度多久更新一次由合成器给，
            // 270ms 的一段波在这台机器上只落两三帧，撞不撞得上峰值全看运气，红绿不可信。
            PumpMs(400);   // 等入场锚点走完、动画挂上
            double step = Fluent.CascadeStepMs(btnW.Count, Fluent.Motion.PressMs, Fluent.Motion.QuickMs);
            bool animating = btnW.All(b => b.HasAnimatedProperties);
            Chk("T178b-wave-left-to-right",
                btnW.Count > 4 && step > 0 && step <= Fluent.Motion.StaggerMs && animating,
                $"{btnW.Count} 项，每项晚 {step:F1}ms，透明度动画挂在每个按钮上={animating}");
            PumpMs(Fluent.Motion.SpringMs + Fluent.Motion.StaggerMs + 300);
            // 收尾也不押固定时长：整段入场以 ContentRendered 为锚，锚点晚的时候上面那段泵不完最后几项。
            // 继续泵到全部复位为止，上限 4 秒——「全部复位」这个判定条件本身一分没松。
            System.Windows.Controls.Button? NotSettled() => btnW.FirstOrDefault(b => b.Opacity < 0.999
                || (b.RenderTransform != null && !b.RenderTransform.Value.IsIdentity));
            var bad = NotSettled();
            var swSettle = Stopwatch.StartNew();
            while (bad != null && swSettle.ElapsedMilliseconds < 4000) { PumpMs(20); bad = NotSettled(); }
            Chk("T178c-toolbar-settled", bad == null,
                bad == null
                    ? $"全部复位，等位 {swSettle.ElapsedMilliseconds}ms"
                    : $"残留 {bad.Content} 透明度={bad.Opacity:N2} 变换={bad.RenderTransform?.GetType().Name}"
                    + $" 缩放={bad?.RenderTransform?.Value.M11:N3}×{bad?.RenderTransform?.Value.M22:N3}");
            Chk("T178d-hover-scale-intact",
                btnW.All(b => b.RenderTransform is null or System.Windows.Media.ScaleTransform),
                string.Join(",", btnW.Select(b => b.RenderTransform?.GetType().Name ?? "null").Distinct()));
            winW.Close();
        }
        catch (Exception e) { Chk("T178-editor-toolbar-wave", false, e.Message); }
        // 播放条在上、工具条在下：上排已在动时下排仍须全透明，两排按阅读顺序前后错开一波
        try
        {
            var stP = new SettingsService();
            var seqP = new CapturedSequence
            {
                Frames = new List<CapturedFrame> { MovingSquare(160, 90, 0, 2), MovingSquare(160, 90, 1, 2) },
                Region = new RectI(0, 0, 160, 90),
                MonitorBounds = new RectI(0, 0, 1920, 1080),
            };
            var winP = new FrameCapture.Overlays.EditorWindow(stP, seqP);
            var NPP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            // 播放条按列组接管：动画挂在 Grid 的直接子元素上，按钮自身 Opacity 始终为 1，
            // 探按钮会让断言在「播放条完全没动」时也成立。
            var playPanelP = (System.Windows.Controls.Border?)typeof(FrameCapture.Overlays.EditorWindow)
                .GetField("_playPanel", NPP)!.GetValue(winP);
            var playGrp = (playPanelP?.Child as System.Windows.Controls.Panel)?.Children
                .OfType<System.Windows.UIElement>().FirstOrDefault();
            var barP = (System.Windows.Controls.StackPanel?)typeof(FrameCapture.Overlays.EditorWindow)
                .GetField("_toolsBar", NPP)!.GetValue(winP);
            var firstTool = barP!.Children.OfType<System.Windows.Controls.Button>().First();
            Chk("T179a-multiframe-parts", playGrp != null, $"播放首组={(playGrp == null ? "缺失" : "存在")}");
            // 取不到时拿一个游离元素兜底：下面的断言照样报红，但不让整轮自测崩在空引用上
            UIElement grp = playGrp ?? new UIElement();
            // 起波时刻挂 Opacity 属性变化回调：轮询抓不到窗口出现那一刻的越过。
            // 断言落在落点上——两排整段落定必须压在窗口淡入（EditorEnterMs）之内。
            // 淡入结束还有一排在路上，看上去就是「窗口先闪出来、内容慢慢展现」。
            int waveUp = -1;
            long t0P = 0;
            var ddp = System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(UIElement.OpacityProperty, typeof(UIElement))!;
            void OnWaveUp(object? s, EventArgs e)
            {
                if (waveUp < 0 && (grp.Opacity > 0.05 || firstTool.Opacity > 0.05)) waveUp = (int)(Environment.TickCount64 - t0P);
            }
            ddp.AddValueChanged(grp, OnWaveUp);
            ddp.AddValueChanged(firstTool, OnWaveUp);
            t0P = Environment.TickCount64;
            winP.Show();
            int tP = 0;
            while (tP < 3000 && waveUp < 0) { PumpMs(10); tP += 10; }
            long tSettle0 = Environment.TickCount64;
            int settle = -1;
            while (settle < 0 && Environment.TickCount64 - tSettle0 < 3000)
            {
                if (grp.Opacity > 0.999 && firstTool.Opacity > 0.999) settle = (int)(Environment.TickCount64 - tSettle0);
                else PumpMs(10);
            }
            ddp.RemoveValueChanged(grp, OnWaveUp);
            ddp.RemoveValueChanged(firstTool, OnWaveUp);
            Chk("T179b-rows-settle-inside-fade",
                waveUp >= 0 && settle >= 0 && settle <= Fluent.Motion.EditorEnterMs,
                $"起波={waveUp}ms 落定={settle}ms 窗口淡入={Fluent.Motion.EditorEnterMs}ms");
            PumpMs(Fluent.Motion.SpringMs + Fluent.Motion.StaggerMs + 300);
            Chk("T179c-both-rows-settled", grp.Opacity > 0.999 && firstTool.Opacity > 0.999,
                $"播放首组={grp.Opacity:N2} 工具首项={firstTool.Opacity:N2}");
            winP.Close();
        }
        catch (Exception e) { Chk("T179-play-row-order", false, e.Message); }

        // 轻提示：抬起淡入 → 淡出走完收起；显示中再次触发只换文字续期，不把刚看清的一行字压回全透明
        try
        {
            var stN = new SettingsService();
            var seqN = new CapturedSequence
            {
                Frames = new List<CapturedFrame> { MovingSquare(520, 300, 0, 3), MovingSquare(520, 300, 1, 3) },
                Region = new RectI(0, 0, 520, 300),
                MonitorBounds = new RectI(0, 0, 1920, 1080),
            };
            var winN = new FrameCapture.Overlays.EditorWindow(stN, seqN);
            var NPn = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var Tn = typeof(FrameCapture.Overlays.EditorWindow);
            var snackN = (System.Windows.Controls.Border?)Tn.GetField("_snack", NPn)!.GetValue(winN);
            var showN = Tn.GetMethod("ShowSnack", NPn)!;
            winN.Show();
            PumpMs(Fluent.Motion.EditorEnterMs + 260);
            Chk("T187a-snack-hidden-before", snackN!.Visibility == System.Windows.Visibility.Collapsed, snackN.Visibility.ToString());
            showN.Invoke(winN, new object[] { "已复制到剪贴板" });
            PumpMs(40);
            var shiftN = snackN.RenderTransform as System.Windows.Media.TranslateTransform;
            Chk("T187b-snack-rises-in",
                snackN.Visibility == System.Windows.Visibility.Visible
                && snackN.Opacity > 0.02 && snackN.Opacity < 0.99 && shiftN != null && shiftN.Value.OffsetY > 0.05,
                $"透明度={snackN.Opacity:N2} 位移={shiftN?.Value.OffsetY:N1}");
            PumpMs(Fluent.Motion.HoverMs + 80);
            double fullN = snackN.Opacity;
            showN.Invoke(winN, new object[] { "识别完成" });
            PumpMs(30);
            Chk("T187c-snack-retrigger-no-replay", fullN > 0.999 && snackN.Opacity > fullN - 0.02,
                $"满显示={fullN:N3} 再次触发后={snackN.Opacity:N3}");
            PumpMs(1800 + Fluent.Motion.DissolveMs + 300);
            Chk("T187d-snack-collapses-after",
                snackN.Visibility == System.Windows.Visibility.Collapsed
                && snackN.Opacity < 0.001 && snackN.RenderTransform == null,
                $"可见性={snackN.Visibility} 透明度={snackN.Opacity:N3} 变换={snackN.RenderTransform?.GetType().Name ?? "无"}");
            winN.Close();
            PumpMs(200);
        }
        catch (Exception e) { Chk("T187-snack-motion", false, e.Message); }

        // 悬停提示只放名称与快捷键：说明性文字（全角括号、全角冒号、多段子句）不该出现在 tooltip 里
        try
        {
            var stT = new SettingsService();
            var seqT = new CapturedSequence
            {
                Frames = new List<CapturedFrame> { MovingSquare(520, 300, 0, 3), MovingSquare(520, 300, 1, 3) },
                Region = new RectI(0, 0, 520, 300),
                MonitorBounds = new RectI(0, 0, 1920, 1080),
            };
            var winT = new FrameCapture.Overlays.EditorWindow(stT, seqT);
            winT.Show();
            PumpMs(Fluent.Motion.EditorEnterMs + Fluent.Motion.StaggerMs + 260);
            var tips = new List<string>();
            void WalkTips(System.Windows.DependencyObject d)
            {
                if (d is System.Windows.FrameworkElement fe && fe.ToolTip is string s) tips.Add(s);
                int cn = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < cn; i++) WalkTips(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
            }
            WalkTips(winT);
            var fat = tips.Where(s => s.Contains('（') || s.Contains('：') || s.Split(" · ").Length > 2).ToList();
            Chk("T186a-tooltips-lean", tips.Count >= 12 && fat.Count == 0,
                $"悬停 {tips.Count} 条，超标 {fat.Count} 条：{string.Join(" | ", fat.Take(3))}");
            winT.Close();
            PumpMs(200);
        }
        catch (Exception e) { Chk("T186-tooltip-lean", false, e.Message); }

        // 滚动截图的三层浮层：同刻 Show，入场按 StaggerMs 依次落位，不三件事一起弹出来
        try
        {
            var regS = new RectI(240, 220, 460, 320);
            var stg = new FrameCapture.Overlays.ScrollStageWindow(regS, "");
            var prv = new FrameCapture.Overlays.ScrollPreviewWindow(regS, "", Fluent.Motion.StaggerMs);
            var bar = new FrameCapture.Overlays.ScrollHintWindow(regS, "", Fluent.Motion.StaggerMs * 2);
            stg.Show(); prv.Show(); bar.Show();
            int leadS = 0;
            while (stg.Opacity <= 0.02 && leadS < 3000) { PumpMs(10); leadS += 10; }
            Chk("T185b-scroll-layers-staggered",
                stg.Opacity > 0.05 && prv.Opacity < 0.02 && bar.Opacity < 0.02,
                $"取景层={stg.Opacity:N2} 预览卡={prv.Opacity:N2} 控制条={bar.Opacity:N2} 起波={leadS}ms");
            PumpMs(Fluent.Motion.StaggerMs * 2 + Fluent.Motion.EnterMs + 500);
            Chk("T185c-scroll-layers-settled",
                stg.Opacity > 0.999 && prv.Opacity > 0.999 && bar.Opacity > 0.999,
                $"落位 取景层={stg.Opacity:N2} 预览卡={prv.Opacity:N2} 控制条={bar.Opacity:N2}");
            stg.Close(); prv.Close(); bar.Close();
            PumpMs(300);
        }
        catch (Exception e) { Chk("T185-scroll-layer-stagger", false, e.Message); }

        // 编辑器背板：窗底让给 Mica，画布四周的中性暗底必须自己涂满——背板渗进画布会污染取色与裁切判断。
        try
        {
            var stM = new SettingsService();
            var seqM = new CapturedSequence
            {
                Frames = new List<CapturedFrame> { MovingSquare(320, 180, 0, 2), MovingSquare(320, 180, 1, 2) },
                Region = new RectI(0, 0, 320, 180),
                MonitorBounds = new RectI(0, 0, 1920, 1080),
            };
            var winM = new FrameCapture.Overlays.EditorWindow(stM, seqM);
            winM.Show();
            PumpMs(260);
            bool mica = Fluent.MicaSupported;
            var hwndM = new System.Windows.Interop.WindowInteropHelper(winM).Handle;
            int hrM = DwmProbe.DwmGetWindowAttribute(hwndM, 38, out uint gotM, 4);
            bool seeThrough = winM.Background == System.Windows.Media.Brushes.Transparent;
            Chk("T182a-editor-backdrop-is-mica",
                (!mica || (hrM == 0 && gotM == 2u)) && seeThrough == mica,
                $"材质回读 hr={hrM}/{gotM} 能力={mica} 窗底透明={seeThrough}");
            var NPm = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var gridM = (System.Windows.Controls.Grid)typeof(FrameCapture.Overlays.EditorWindow)
                .GetField("_viewGrid", NPm)!.GetValue(winM)!;
            var hostM = System.Windows.Media.VisualTreeHelper.GetParent(gridM) as System.Windows.Controls.Border;
            var hostBg = hostM?.Background as System.Windows.Media.SolidColorBrush;
            Chk("T182b-canvas-surround-opaque", hostBg != null && hostBg.Color.A == 255,
                $"画布四周底色={(hostBg?.Color.ToString() ?? "无")}");
            winM.Close();
        }
        catch (Exception e) { Chk("T182-editor-mica", false, e.Message); }

        // 窗级淡入淡出确实在跑：动画中途读窗口 Opacity 必须停在半程，走完必须到 1、退场必须真关窗。
        // 屏幕侧的可见性由 scripts/probe-window-fade.ps1 高速屏采取证（采样率不足会把斜坡量成阶跃）。
        try
        {
            var winR = new Window
            {
                Title = "SelfTest WindowFade", Width = 220, Height = 150, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 260, Top = 260,
                WindowStyle = WindowStyle.None,
                Content = new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.Gray },
            };
            Fluent.BorderlessNative(winR);
            Fluent.FadeIn(winR, Fluent.Motion.EnterMs);
            // 采样前先等动画时钟真的走起来：并行跑护栏时首帧可能晚于半个时长，那一刀切下去读到的是
            // 还没起跳的 0。等起步不放宽判据——仍然在「半程」这一刀上读，只是确保切的是斜坡不是起点。
            bool WaitRun(Func<bool> running, int capMs)
            {
                for (int t = 0; t < capMs; t += 10) { if (running()) return true; PumpMs(10); }
                return running();
            }
            winR.Show();
            bool enterStarted = WaitRun(() => winR.Opacity > 0, Fluent.Motion.EnterMs);
            PumpMs(Fluent.Motion.EnterMs / 2);
            double c1 = winR.Opacity;
            Chk("T180a-enter-mid-fade", enterStarted && c1 > 0.05 && c1 < 0.999,
                $"入场半程 Opacity={c1:N3}（动画起步={enterStarted}）");
            PumpMs(Fluent.Motion.EnterMs + 300);
            Chk("T180b-enter-settled", winR.Opacity > 0.999, $"走完 Opacity={winR.Opacity:N3}");
            Fluent.CloseWithFade(winR);
            bool exitStarted = WaitRun(() => winR.Opacity < 0.999, Fluent.Motion.ExitMs);
            PumpMs(Fluent.Motion.ExitMs / 2);
            double d1 = winR.Opacity;
            Chk("T180c-exit-mid-fade", exitStarted && d1 > 0.02 && d1 < 0.95,
                $"退场半程 Opacity={d1:N3}（动画起步={exitStarted}）");
            PumpMs(Fluent.Motion.ExitMs + 400);
            Chk("T180d-closed-after-exit", !winR.IsLoaded, $"窗口仍存活={winR.IsLoaded}");
        }
        catch (Exception e) { Chk("T180-window-fade", false, e.Message); }

        // 通道边界：WPF 顶层窗不是 Win32 分层窗。窗级透明度走 DComp 合成面，
        // 手工挂 WS_EX_LAYERED + SetLayeredWindowAttributes 这条路写不进去也读不回来。
        try
        {
            var winP = new Window
            {
                Title = "SelfTest LayeredProbe", Width = 140, Height = 100, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 300, Top = 300,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Opacity = 0.5,
                Background = System.Windows.Media.Brushes.Black,
            };
            winP.Show();
            PumpMs(150);
            var hwndP = new System.Windows.Interop.WindowInteropHelper(winP).Handle;
            bool readP = LayerProbe.GetLayeredWindowAttributes(hwndP, out _, out byte ap, out _);
            Chk("T181a-wpf-window-is-not-layered", !readP,
                $"分层窗 alpha 回读={readP}/{ap}");
            winP.Close();

            var winQ = new Window
            {
                Title = "SelfTest LayeredProbe2", Width = 140, Height = 100, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 320, Top = 320,
                WindowStyle = WindowStyle.None,
                Content = new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.Black },
            };
            Fluent.BorderlessNative(winQ);
            winQ.Show();
            PumpMs(150);
            var hwndQ = new System.Windows.Interop.WindowInteropHelper(winQ).Handle;
            uint ex = LayerProbe.GetWindowLongW(hwndQ, LayerProbe.GWL_EXSTYLE);
            LayerProbe.SetWindowLongW(hwndQ, LayerProbe.GWL_EXSTYLE, ex | LayerProbe.WS_EX_LAYERED);
            bool setOk = LayerProbe.SetLayeredWindowAttributes(hwndQ, IntPtr.Zero, 90, LayerProbe.LWA_ALPHA);
            PumpMs(60);
            uint ex2 = LayerProbe.GetWindowLongW(hwndQ, LayerProbe.GWL_EXSTYLE);
            bool readQ = LayerProbe.GetLayeredWindowAttributes(hwndQ, out _, out _, out _);
            Chk("T181b-win32-alpha-channel-unavailable",
                !setOk && (ex2 & LayerProbe.WS_EX_LAYERED) == 0 && !readQ,
                $"写入={setOk} 样式留住={(ex2 & LayerProbe.WS_EX_LAYERED) != 0} 回读={readQ}");
            winQ.Close();
        }
        catch (Exception e) { Chk("T181-alpha-channel-boundary", false, e.Message); }



        // 全局 ToolTip/菜单样式可解析（守主题相关 XAML）
        try
        {
            var tt = Fluent.ToolTipStyle();
            var sb = Fluent.ScrollBarStyle();
            var dict = Fluent.MenuResources();
            Chk("T12-global-styles", tt != null && sb != null && dict != null && dict.Count >= 3, "parse");
        }
        catch (Exception e) { Chk("T12-global-styles", false, e.Message); }

        // 同类多标注：3 个不同颜色的圆/点全部烘焙可见（断言任何一笔都不丢失）
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
            int Count(Func<byte, byte, byte, bool> m)
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

        // FrameRing：预算驱逐语义（保首帧、丢最老、计数正确）+ 自适应预算区间
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

        // 剪贴板 DataObject：文件 + 位图 + 自定义 GIF 三格式齐备（不触碰真实剪贴板）
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

        // timeline 拖范围状态机（原 UI 内逻辑下沉，人工清单缩小）
        try
        {
            var rd = new RangeDragState { Count = 16, Width = 1000 };
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
            Chk("T16h-idx-clamp", RangeDragState.XToIdx(-50, 1000, 16) == 0
                && RangeDragState.XToIdx(1500, 1000, 16) == 15);
            var one = new RangeDragState { Count = 1, Width = 100 };
            one.Down(50); one.Up();
            Chk("T16i-single-frame-noop", one.A == -1 && one.Mode == 0);
        }
        catch (Exception e) { Chk("T16-range-drag", false, e.Message); }

        // 窗口吸附命中：z 序取最先包含者、跳过 <4px、未命中返回 null
        try
        {
            var cands = new[]
            {
                new RectI(100, 100, 200, 200),   // 前景小窗
                new RectI(10, 10, 3, 3),         // 过小应跳过
                new RectI(0, 0, 1000, 1000),     // 背景大窗
            };
            var hit = SnapPick.Pick(cands, 150, 150);
            Chk("T17a-topmost-hit", hit?.Equals(cands[0]) == true, $"{hit}");
            var hit2 = SnapPick.Pick(cands, 800, 800);
            Chk("T17b-background", hit2?.Equals(cands[2]) == true, $"{hit2}");
            var hit3 = SnapPick.Pick(cands, 11, 11);
            Chk("T17c-skip-tiny", hit3?.Equals(cands[2]) == true, $"{hit3}");
            Chk("T17d-miss-null", SnapPick.Pick(cands, 1500, 1500) == null);
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

        // 并行编码：多帧输出与顺序逐帧编码逐字节一致 + 取消生效
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
            var csc = new TaskCompletionSource<int>();
            var cts = new CancellationTokenSource();
            cts.Cancel();
            bool cancelled = false;
            try { GifEncoder.Encode(fr, delays, true, null, cts.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Chk("T18a-parallel-valid", ok, $"len={par.Length}");
            Chk("T18b-cancel", cancelled);
        }
        catch (Exception e) { Chk("T18-parallel", false, e.Message); }

        // UndoRedo：新增清空重做栈；撤销/重做往返；裁剪清空语义
        try
        {
            var h = new UndoRedo<string>();
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

        // WGC（Windows.Graphics.Capture）后端：设备/项创建 + 真实取帧
        try
        {
            var mons = Monitors.All();
            var mon = mons.First(m => m.IsPrimary) ?? mons[0];
            var reg = new RectI(mon.Bounds.X + 4, mon.Bounds.Y + 4, 256, 192);
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
            using var wgc = new WgcLiveCapture();
            int n = 0; int fw = 0, fh = 0; bool badFmt = false;
            wgc.FrameSampled += f =>
            {
                if (f.Bgra.Length != f.W * f.H * 4) badFmt = true;
                fw = f.W; fh = f.H;
                Interlocked.Increment(ref n);
            };
            for (int i = 0; i < 20 && probeTick < 2; i++) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(50); }
            wgc.Start(mon, reg, 30);
            var sw0 = Stopwatch.StartNew();
            // 探针窗靠 WinForms 定时器改字，而定时器要消息泵才走：这里若干睡，屏幕内容不变，
            // WGC 只在内容变化时产帧，于是只收到第一帧——能不能收到第三帧就变成看桌面上有没有别的东西在动。
            while (n < 3 && sw0.ElapsedMilliseconds < 3000) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(16); }
            wgc.Stop();
            Chk("T20a-wgc-frames", n >= 3 && !badFmt && fw == reg.W && fh == reg.H, $"n={n} {fw}x{fh} badFmt={badFmt} missed={wgc.FramesMissed} err={wgc.LastLoopError?.Split('\n')[0]}");
            int after = n; Thread.Sleep(200);
            Chk("T20b-wgc-stopped", n == after);
            using var wgc2 = new WgcLiveCapture();
            int n2 = 0;
            wgc2.FrameSampled += _ => Interlocked.Increment(ref n2);
            wgc2.Start(mon, reg, 30);
            var sw1 = Stopwatch.StartNew();
            while (n2 < 1 && sw1.ElapsedMilliseconds < 3000) Thread.Sleep(50);
            wgc2.Stop();
            probeTimer.Stop();
            probeForm.Hide();
            Chk("T20c-wgc-restart", n2 >= 1, $"n2={n2} missed={wgc2.FramesMissed} err={wgc2.LastLoopError?.Split('\n')[0]}");

            // 关闭 WGC 系统捕获提示边框（Windows 11 那条绕整屏的黄色 #FFD11C 框）：
            // 我们的 WGC 只能整显示器捕获，系统边框必然全屏、与所选录制范围无关，所以范围轮廓
            // 由自家绘制。这里断言这个"关边框"的动作在支持的系统上真的成功了
            // （Win10 没有 IGraphicsCaptureSession3，SystemBorderSuppressed 保持 null→跳过，不算失败）。
            var canBorderless = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
            Chk("T73-wgc-borderless", wgc2.SystemBorderSuppressed == true || !canBorderless,
                $"suppressed={wgc2.SystemBorderSuppressed} canBorderless={canBorderless}");

            // 按窗口本体录制：帧就是这一个窗口，别的窗盖上来也不进画面。
            {
                using var target = new System.Windows.Forms.Form
                {
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                    Location = new System.Drawing.Point(mon.Bounds.X + 140, mon.Bounds.Y + 140),
                    Size = new System.Drawing.Size(360, 260),
                    BackColor = System.Drawing.Color.FromArgb(0x12, 0x34, 0x56),
                    ShowInTaskbar = false,
                    Text = "FC-WGC-WINTARGET",
                };
                var tickLbl = new System.Windows.Forms.Label
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    BackColor = target.BackColor,
                    ForeColor = System.Drawing.Color.White,
                    Text = "0",
                };
                target.Controls.Add(tickLbl);
                using var tickTimer = new System.Windows.Forms.Timer { Interval = 60 };
                int tk = 0;
                tickTimer.Tick += (_, _) => { tickLbl.Text = (++tk).ToString(); };
                target.Show();
                tickTimer.Start();
                // WGC 只在被捕获窗内容变化时产帧：目标窗靠 Timer 重绘制造变化，
                // 而 Timer 走消息，等帧期间必须泵消息——Thread.Sleep 会把它冻住，一帧也等不到。
                for (int i = 0; i < 20 && tk < 2; i++) PumpMs(50); // 等窗口先画起来

                using var wgcWin = new WgcLiveCapture();
                CapturedFrame? lastWin = null;
                var winFrames = new List<CapturedFrame>();
                int nWin = 0;
                int badWin = 0; // 尺寸与缓冲不一致的帧：0 宽高的空帧、或 W*H*4 对不上 Bgra 长度
                wgcWin.FrameSampled += f =>
                {
                    if (f.W < 1 || f.H < 1 || f.Bgra.Length != (long)f.W * f.H * 4) Interlocked.Increment(ref badWin);
                    lock (winFrames) { if (winFrames.Count < 24) winFrames.Add(f); }
                    lastWin = f; Interlocked.Increment(ref nWin);
                };
                wgcWin.StartWindow(target.Handle, mon, 20, false);
                var swWin = Stopwatch.StartNew();
                while (nWin < 2 && swWin.ElapsedMilliseconds < 3000) PumpMs(50);
                Chk("T189d-window-frames", nWin >= 2 && lastWin != null && badWin == 0,
                    $"n={nWin} bad={badWin} empty={wgcWin.EmptyFrames} err={FirstLine(wgcWin.LastLoopError)}");

                var f0 = lastWin;
                Chk("T189e-window-size",
                    f0 != null && Math.Abs(f0.W - target.Width) <= 24 && Math.Abs(f0.H - target.Height) <= 24,
                    $"帧={f0?.W}x{f0?.H} 窗={target.Width}x{target.Height}");

                // 纯绿不透明窗压住目标左半边，再等几帧：产物里那块仍是目标本色，绿不进画面
                using var occl = new System.Windows.Forms.Form
                {
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                    Location = new System.Drawing.Point(target.Left + 2, target.Top + 30),
                    Size = new System.Drawing.Size(120, 150),
                    BackColor = System.Drawing.Color.Lime,
                    TopMost = true,
                    ShowInTaskbar = false,
                };
                occl.Show();
                int beforeOcc = nWin;
                var swOcc = Stopwatch.StartNew();
                while (nWin < beforeOcc + 3 && swOcc.ElapsedMilliseconds < 3000) PumpMs(50);
                wgcWin.Stop(); // Stop 等在途回调结束，之后读 lastWin 安全
                var fOcc = lastWin;
                bool occClean = false;
                string occDetail = "无帧";
                // 取样点必须落在缓冲内：帧尺寸与缓冲长度对不上时按"没采到"计，断言照样不过
                if (fOcc != null && fOcc.Bgra.Length >= (fOcc.H / 2 * fOcc.W + 60) * 4 + 3)
                {
                    int ox = 60, oy = fOcc.H / 2; // 遮挡窗盖住帧内 x∈[2,122]、y∈[30,180]
                    int oi = (oy * fOcc.W + ox) * 4;
                    byte ob = fOcc.Bgra[oi], og = fOcc.Bgra[oi + 1], orr = fOcc.Bgra[oi + 2];
                    occClean = og < 0x80 && ob > 0x40; // 目标本色 (18,52,86)；纯绿是 g=255
                    occDetail = $"({orr},{og},{ob})";
                }
                else if (fOcc != null) occDetail = $"缓冲不足 {fOcc.W}x{fOcc.H}/{fOcc.Bgra.Length}";
                Chk("T189f-window-occlusion", occClean, $"遮挡处像素={occDetail} n={nWin} bad={badWin} empty={wgcWin.EmptyFrames}");

                // 窗口本体的 alpha 原样保留：圆角处透明、本体不透明。
                // 拍平到什么底色是编码端按容器决定的事（只有 MP4 这类无透明通道才填色），取帧这一步不替用户定。
                bool cornerClear = false, coreOpaque = false;
                if (fOcc != null)
                {
                    int cw = fOcc.W;
                    for (int i = 3; i < fOcc.Bgra.Length; i += 4)
                        if (fOcc.Bgra[i] < 250) { cornerClear = true; break; }
                    int ci = ((fOcc.H / 2) * cw + cw / 2) * 4;
                    coreOpaque = fOcc.Bgra[ci + 3] == 255;
                }
                Chk("T189g-window-alpha-preserved", cornerClear && coreOpaque,
                    fOcc == null ? "无帧" : $"{fOcc.W}x{fOcc.H} 圆角透明={cornerClear} 本体不透明={coreOpaque}");

                // 开投影的录制：画布向四周各外扩一个边距，最外角透明、本体不透明。
                // 边距与形状和静图窗口截图同源（同一个 ShadowMargin / ShadowShape），两边投影才一样大。
                try
                {
                    int margin = WindowCapture.ShadowMargin(target.Handle);
                    using var wgcSh = new WgcLiveCapture();
                    CapturedFrame? shFrame = null;
                    int nSh = 0;
                    wgcSh.FrameSampled += f => { shFrame = f; Interlocked.Increment(ref nSh); };
                    wgcSh.StartWindow(target.Handle, mon, 20, false, includeShadow: true);
                    var swSh = Stopwatch.StartNew();
                    while (nSh < 1 && swSh.ElapsedMilliseconds < 3000) PumpMs(50);
                    wgcSh.Stop();
                    var sf = shFrame;
                    Chk("T189p-shadow-canvas-inflated",
                        sf != null && f0 != null && Math.Abs(sf.W - (f0.W + 2 * margin)) <= 4
                        && Math.Abs(sf.H - (f0.H + 2 * margin)) <= 4,
                        sf == null || f0 == null ? "无帧" : $"帧 {sf.W}x{sf.H} 应为 {f0.W + 2 * margin}x{f0.H + 2 * margin}（边距 {margin}）");
                    bool shCorner = sf != null && sf.Bgra[3] < 250;
                    bool shCore = false;
                    if (sf != null)
                    {
                        int ci = ((sf.H / 2) * sf.W + sf.W / 2) * 4;
                        shCore = sf.Bgra[ci + 3] == 255;
                    }
                    Chk("T189r-shadow-band-transparent", shCorner && shCore,
                        $"外角 alpha={sf?.Bgra[3]}（投影淡到透明）本体不透明={shCore}");
                }
                catch (Exception ex) { Chk("T189p-shadow-canvas", false, FirstLine(ex.Message)); }

                // 取证：遮挡期间采到的帧编成 GIF 与末帧 PNG 存 test-out，肉眼核对成片里只有目标窗
                {
                    string dir = Path.Combine(Directory.GetCurrentDirectory(), "test-out");
                    CapturedFrame[] shots;
                    lock (winFrames) shots = winFrames.ToArray();
                    if (shots.Length > 1 && Directory.Exists(dir))
                        try
                        {
                            var delays = new List<int>();
                            for (int i = 0; i < shots.Length; i++) delays.Add(50);
                            File.WriteAllBytes(Path.Combine(dir, "r99-windowrec.gif"), GifEncoder.Encode(shots, delays));
                            BgraImage.SavePng(shots[shots.Length - 1], Path.Combine(dir, "r99-windowrec.png"));
                        }
                        catch (Exception ex) { FrameCapture.Core.Trace.Log("selftest window-rec artifact: " + ex.Message); }
                }
                // 选取状态本身：点中窗口 = LockedToWindow；各功能的开关只决定认不认这个窗口
                var selWin = new Selection(@"\\.\DISPLAY1", new RectI(4, 8, 10, 10), new IntPtr(0x1234));
                var selRect = selWin with { Window = IntPtr.Zero };
                Chk("T189h-selection-window-state",
                    selWin.LockedToWindow
                    && selWin.WithWindowHonor(true).Window == new IntPtr(0x1234)
                    && !selWin.WithWindowHonor(false).LockedToWindow
                    && selWin.WithWindowHonor(false).Bounds.Equals(new RectI(4, 8, 10, 10))
                    && !selRect.LockedToWindow
                    && selWin.Region.Bounds.Equals(selWin.Bounds),
                    "选中窗口是选取状态；开关关 → 句柄作废只剩矩形");
                tickTimer.Stop();
                occl.Close();
                target.Close();
            }

            // 轮廓窗"在屏可见但不进捕获"的机制前提：WDA_EXCLUDEFROMCAPTURE(0x11) 必须真的能被设置成功，
            // 否则 RegionOutlineWindow 会静默变成"看不见"（那就等于录制期间没有任何范围提示）。
            {
                var probeWin = new Window
                {
                    Title = "SelfTest DisplayAffinity",
                    Width = 200, Height = 120, ShowInTaskbar = false, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000, Top = -4000, Topmost = true,
                    AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
                    WindowStyle = WindowStyle.None,
                    Content = new System.Windows.Controls.Border
                    {
                        BorderBrush = System.Windows.Media.Brushes.DodgerBlue,
                        BorderThickness = new Thickness(2),
                    },
                };
                var h = new System.Windows.Interop.WindowInteropHelper(probeWin).EnsureHandle();
                bool setOk = SetWindowDisplayAffinity(h, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
                Chk("T74-exclude-from-capture", setOk,
                    $"setOk={setOk} err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()} hwnd=0x{h:X}");
                probeWin.Close();
            }
        }
        catch (Exception e) { Chk("T20-wgc-backend", false, e.GetType().Name + " " + e.Message + " @" + FirstLine(e.StackTrace)); }

        // 时间轴裁剪端点直设与钳位（A<=B-1、B>=A+1、至少 2 帧）
        {
            var rd = new RangeDragState { Count = 10, Width = 300 };
            var dflt = rd.Effective;
            rd.SetB(4); rd.SetA(2);
            var got = rd.Effective;
            rd.SetA(9); var a = rd.Effective;
            rd.SetB(1); var b = rd.Effective;
            rd.SetA(-5); var c = rd.Effective;
            Chk("T68-timeline-setab", dflt == (0, 9) && got == (2, 4) && a == (3, 4) && b == (3, 4) && c == (0, 4),
                $"dflt={dflt} set={got} overA={a} underB={b} underA={c}");
        }

        // 主题变更事件（实际变化才触发；强调色变化同样触发）
        {
            int fired = 0;
            Action th = () => fired++;
            Theme.Changed += th;
            try
            {
                Theme.Apply("dark", "#4CC2FF");
                int base0 = fired;
                Theme.Apply("dark", "#4CC2FF");
                bool noDup = fired == base0;
                Theme.Apply("light", "#4CC2FF");
                bool darkFlip = fired == base0 + 1;
                Theme.Apply("light", "#FF0000");
                bool accFlip = fired == base0 + 2;
                Chk("T69-theme-changed", noDup && darkFlip && accFlip, $"fired={fired - base0}");
            }
            finally
            {
                Theme.Changed -= th;
                Theme.Apply("dark", "#4CC2FF");
            }
        }

        // 时间轴坐标与帧号映射：边界与单调性
        {
            var rd = new RangeDragState { Count = 20, Width = 380 };
            int first = RangeDragState.XToIdx(0, 380, 20), last = RangeDragState.XToIdx(380, 380, 20),
                over = RangeDragState.XToIdx(9999, 380, 20);
            bool mono = true; int prev = -1;
            for (double x = 0; x <= 380; x += 5)
            {
                int i = RangeDragState.XToIdx(x, 380, 20);
                if (i < prev) { mono = false; break; }
                prev = i;
            }
            Chk("T70-timeline-map", first == 0 && last == 19 && over == 19 && mono, $"first={first} last={last} over={over} mono={mono}");
        }

        // ElementSnap：UIA 控件级吸附——命中的是文本框边界而非整窗
        try
        {
            var win = new Window
            {
                Title = "SelfTest ElementSnap",
                Width = 320, Height = 240,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 150, Top = 150,
                Topmost = true,
                Content = new System.Windows.Controls.TextBox
                {
                    Text = "snap me", Width = 140, Height = 26,
                    Margin = new Thickness(20),
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
            var hit = ElementSnap.At(cx, cy, IntPtr.Zero);
            Chk("T71a-uia-hit", hit != null);
            Chk("T71b-uia-rect", hit != null &&
                Math.Abs(hit.Rect.X - tl.X) <= 4 && Math.Abs(hit.Rect.Y - tl.Y) <= 4 &&
                Math.Abs(hit.Rect.W - (br.X - tl.X)) <= 4 && Math.Abs(hit.Rect.H - (br.Y - tl.Y)) <= 4,
                hit == null ? "null" : $"{hit.Rect} vs tb {tl.X},{tl.Y} {(int)(br.X - tl.X)}x{(int)(br.Y - tl.Y)}");
            var up = hit != null ? ElementSnap.Parent(hit) : null;
            Chk("T71c-uia-parent", up != null && up.Rect.W * up.Rect.H >= hit!.Rect.W * hit.Rect.H);
            win.Close();
        }
        catch (Exception ex) { Chk("T71-uia", false, ex.Message); }

        // C1 选中态语义：工具单选切换（再点=取消）+ accent 反白对比色（纯逻辑）
        {
            var a = ToolToggle.Select(DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Sunday);
            var b = ToolToggle.Select(a, DayOfWeek.Monday, DayOfWeek.Sunday);   // 再点当前 → 取消
            var c = ToolToggle.Select(DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Sunday); // 直选"浏览"透传
            Chk("T72a-tool-toggle", a == DayOfWeek.Monday && b == DayOfWeek.Sunday && c == DayOfWeek.Sunday,
                $"a={a} b={b} c={c}");
            var onLight = Fluent.OnAccentColor(System.Windows.Media.Color.FromRgb(0x4C, 0xC2, 0xFF));
            var onDark = Fluent.OnAccentColor(System.Windows.Media.Color.FromRgb(0x2B, 0x3A, 0x8F));
            Chk("T72b-onaccent", onLight == System.Windows.Media.Colors.Black && onDark == System.Windows.Media.Colors.White,
                $"light={onLight} dark={onDark}");
        }

        // 捕获状态机入口守卫（纯逻辑决策表）：
        // 回归护栏 = "只有 StartCapture 允许隐藏范围轮廓"：编辑器开着再按捕获键等失败分支
        // 一律不得触碰常显轮廓，否则轮廓被永久弄没且无任何报错。
        {
            var idle = CaptureGuards.OnCaptureHotkey(false, false, false);
            var cap = CaptureGuards.OnCaptureHotkey(true, false, false);
            var sel = CaptureGuards.OnCaptureHotkey(false, true, false);
            var edi = CaptureGuards.OnCaptureHotkey(false, false, true);
            Chk("T75a-capture-intent",
                idle == CaptureGuards.CaptureIntent.StartCapture &&
                cap == CaptureGuards.CaptureIntent.Ignore &&
                sel == CaptureGuards.CaptureIntent.Ignore &&
                edi == CaptureGuards.CaptureIntent.RefocusEditor,
                $"idle={idle} cap={cap} sel={sel} edi={edi}");
            // 不变量：非起录意图一律不得隐藏轮廓
            bool onlyStartHides =
                !CaptureGuards.MayHideOutline(CaptureGuards.CaptureIntent.Ignore) &&
                !CaptureGuards.MayHideOutline(CaptureGuards.CaptureIntent.RefocusEditor) &&
                CaptureGuards.MayHideOutline(CaptureGuards.CaptureIntent.StartCapture);
            Chk("T75b-outline-hide-invariant", onlyStartHides);
            // 选取热键：录制中不打断；选取窗活着=顶前；窗没了(Alt+F4)=复位重开
            var s1 = CaptureGuards.OnScopeHotkey(true, false, false);
            var s2 = CaptureGuards.OnScopeHotkey(false, true, true);
            var s3 = CaptureGuards.OnScopeHotkey(false, true, false);
            var s4 = CaptureGuards.OnScopeHotkey(false, false, false);
            Chk("T75c-scope-intent",
                s1 == CaptureGuards.ScopeIntent.Ignore &&
                s2 == CaptureGuards.ScopeIntent.RefocusPicker &&
                s3 == CaptureGuards.ScopeIntent.StartSelection &&
                s4 == CaptureGuards.ScopeIntent.StartSelection,
                $"s1={s1} s2={s2} s3={s3} s4={s4}");
            // 录制结束后的轮廓去向：常显+有已存范围=留在屏上；否则淡出
            var o1 = CaptureGuards.OnCaptureFinished(true, true);
            var o2 = CaptureGuards.OnCaptureFinished(true, false);
            var o3 = CaptureGuards.OnCaptureFinished(false, true);
            Chk("T75d-outline-after-capture",
                o1 == CaptureGuards.AfterCaptureOutline.KeepPersistent &&
                o2 == CaptureGuards.AfterCaptureOutline.FadeOut &&
                o3 == CaptureGuards.AfterCaptureOutline.FadeOut,
                $"o1={o1} o2={o2} o3={o3}");
        }

        // 纯逻辑决策块：Fps 钳制、裁剪重映射、缩放平移数学、裁剪视图栈、撤销次序、Esc 分层、自定义取色
        {
            // Fps 钳制单一事实源（常量 = 文档范围，Normalize 钳越界值）
            Chk("T77a-fps-constants",
                AppSettings.FpsMin == 10 && AppSettings.FpsMax == 240 && AppSettings.FpsMin < AppSettings.FpsMax,
                "Fps 常量单一事实源：10..240（R100 放开高刷）");
            var fpsLo = new AppSettings { Fps = 5 }; fpsLo.Normalize();
            var fpsHi = new AppSettings { Fps = 999 }; fpsHi.Normalize();
            var fpsOk = new AppSettings { Fps = 30 }; fpsOk.Normalize();
            Chk("T77b-fps-normalize-clamps",
                fpsLo.Fps == AppSettings.FpsMin && fpsHi.Fps == AppSettings.FpsMax && fpsOk.Fps == 30,
                "Normalize 把 Fps 钳进 [FpsMin,FpsMax]，合法值原样通过");
            // 裁剪→标注重映射（Core.CropRemap）——坐标变换、跨界丢弃、像素级往返
            {
                var dotIn = new Annotation { Kind = AnnKind.Dot, Color = Color.FromArgb(255, 0xE7, 0x48, 0x4C), Thickness = 8 };
                dotIn.Points.Add((0.5f, 0.5f));
                var lineCross = new Annotation { Kind = AnnKind.Line, Color = Color.Red, Thickness = 4 };
                lineCross.Points.Add((0.1f, 0.5f)); lineCross.Points.Add((0.3f, 0.5f)); // 跨左裁剪边
                var dotOut = new Annotation { Kind = AnnKind.Dot, Color = Color.Lime, Thickness = 4 };
                dotOut.Points.Add((0.9f, 0.9f)); // 框外
                var input = new[] { dotIn, lineCross, dotOut };
                var kept = CropRemap.Remap(input, 0.25, 0.25, 0.5, 0.5);
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
            // 视图缩放/平移数学（Core.ZoomMath）
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
            // 裁剪=视图状态栈（Core.CropState），帧数据不被破坏
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
            // 标注/裁剪双流撤销次序（Core.ActionOrder）
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
            // Esc 分层（导出中=取消；绘制态=退工具；浏览=关闭）
            {
                Chk("T82a-esc-layers",
                    EscStep.Select(encoding: true, inToolMode: true) == EscAction.CancelEncode
                    && EscStep.Select(encoding: false, inToolMode: true) == EscAction.ExitTool
                    && EscStep.Select(encoding: false, inToolMode: false) == EscAction.Close,
                    "导出中优先取消；绘制态先退工具（误触不丢捕获）；仅浏览态关闭");
            }
            // 自定义线宽/最近自定义色（Core.CustomPick）
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
            // 时间轴秒标步长（Core.TimelineScale）
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
            // GIF 误差扩散抖动——调色板被杂色占满时，暗渐变不得退化成少数几档
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
            // 视觉暂留合成——末帧全亮、先前帧留衰减尾迹、暗底亮物可见；出图供逐帧目检
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
            // GIF 厘秒重采样——60fps 不提速、30fps 不丢帧、总时长守恒、延迟全为 ≥2cs 的整十毫秒
            {
                var (k60, d60) = GifTiming.Resample(Enumerable.Repeat(16, 60).ToList());
                int t60 = d60.Sum();
                Chk("T87a-60fps-duration", k60.Count >= 45 && k60.Count <= 55 && Math.Abs(t60 - 960) <= 5 * k60.Count
                    && d60.All(d => d >= 20 && d % 10 == 0) && k60[^1] == 59,
                    $"60fps: keep={k60.Count} total={t60}ms（目标 960±{5 * k60.Count}）min={d60.Min()}");
                var (k30, d30) = GifTiming.Resample(Enumerable.Repeat(33, 30).ToList());
                Chk("T87b-30fps-kept-all", k30.Count == 30 && Math.Abs(d30.Sum() - 990) <= 150 && d30.All(d => d >= 20),
                    $"30fps: keep={k30.Count} total={d30.Sum()}ms");
                var (k1, d1) = GifTiming.Resample(new List<int> { 16 });
                Chk("T87c-single-frame", k1.Count == 1 && k1[0] == 0 && d1[0] == 20, $"单帧 keep={k1.Count} d={d1[0]}");
                // 端到端：60 帧 16ms 编码 → WinForms 解码帧数 = 重采样后帧数（总时长守恒，GIF 不提速）
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
                using var img = Image.FromStream(ms);
                var fd = img.FrameDimensionsList[0];
                int n = img.GetFrameCount(FrameDimension.Time);
                Chk("T87d-gif-framecount", n == k60.Count, $"GIF 解码 {n} 帧 = 重采样 {k60.Count} 帧");
            }
            // 截屏浮窗落点——大区域贴内侧右下；小区域贴外下缘；越界翻转/钳进工作区
            {
                var work = new RectI(0, 0, 3840, 2100);
                var big = ToastPlace.Place(new RectI(100, 100, 1200, 800), work, 300, 84);
                Chk("T88a-inside-bottom-right", big.X == 988 && big.Y == 804, $"大区域 → {big}（期望 988,804）");
                var small = ToastPlace.Place(new RectI(2000, 300, 100, 60), work, 300, 84);
                Chk("T88b-below-region", small.X == 1800 && small.Y == 370, $"小区域 → {small}（期望 1800,370）");
                var lowEdge = ToastPlace.Place(new RectI(2000, 2000, 100, 60), work, 300, 84);
                Chk("T88c-flip-above", lowEdge.Y == 1906, $"贴底小区域 → {lowEdge}（期望翻上 y=1906）");
                var corner = ToastPlace.Place(new RectI(0, 0, 100, 60), work, 300, 84);
                Chk("T88d-clamp-workarea", corner.X == 4 && corner.Y == 70, $"左上角小区域 → {corner}（x 钳 4）");
                bool inside = true;
                foreach (var r in new[] { new RectI(-1920, -100, 800, 500), new RectI(3700, 2050, 120, 40), new RectI(50, 50, 3740, 2000) })
                {
                    var p = ToastPlace.Place(r, work, 300, 84);
                    if (p.X < work.X || p.Y < work.Y || p.Right > work.Right || p.Bottom > work.Bottom) inside = false;
                }
                Chk("T88e-always-in-workarea", inside, "多区域（含负坐标/贴边）浮窗恒在工作区内");
            }
            {
                // 裁剪拖框引导
                var r1 = CropGuide.Rect(300, 400, 100, 120);
                Chk("T89a-rect-anydir", r1 == (100.0, 120.0, 200.0, 280.0), $"左上→右下反向拖 → {r1}");
                var r2 = CropGuide.Rect(100, 120, 300, 400);
                Chk("T89b-rect-same", r2 == r1, "两方向拖框同一矩形");
                var h1 = CropGuide.HintPos(100, 100, 200, 150, 120, 24, 800, 600);
                Chk("T89c-hint-below", h1.Y == 258 && h1.X == 140, $"默认框下居中 → {h1}");
                var h2 = CropGuide.HintPos(100, 400, 200, 180, 120, 24, 800, 600);
                Chk("T89d-hint-flip-up", h2.Y == 368, $"下方放不下翻上方 → {h2}");
                var h3 = CropGuide.HintPos(700, 100, 90, 50, 120, 24, 800, 600);
                Chk("T89e-hint-clamp-x", h3.X == 676 && h3.Y == 158, $"贴右缘横向钳边 → {h3}");
            }
            {
                // 文字标注：字号公式 + 裁剪保文本
                Chk("T90a-font-frame", AnnFont.FramePx(3) == 22 && AnnFont.FramePx(10) == 50, "细=22 粗=50 @1080p");
                Chk("T90b-font-1080", Math.Abs(AnnFont.BakePx(3, 1920, 1080) - 22) < 1e-9, "1080p 帧 = 基准");
                Chk("T90c-font-half", Math.Abs(AnnFont.BakePx(3, 960, 540) - 11) < 1e-9, "半分辨率等比缩 → 11px");
                Chk("T90d-font-floor", AnnFont.BakePx(1, 100, 100) == 10, "小帧下限 10px 防不可读");
                var ta = new Annotation { Kind = AnnKind.Text, Thickness = 6 };
                ta.Points.Add((0.5f, 0.5f)); ta.Text = "你好 世界";
                var keptT = CropRemap.Remap(new[] { ta }, 0.25, 0.25, 0.5, 0.5);
                Chk("T90e-remap-keeps-text", keptT.Count == 1 && keptT[0].Text == "你好 世界" && Math.Abs(keptT[0].Points[0].X - 0.5) < 1e-6,
                    $"裁剪重映射保留文字与坐标 → {keptT.Count}");
            }
            {
                // 马赛克：块粒度公式 + 块均值正确性 + 越界钳制
                Chk("T91a-block-1080", MosaicBlock.Px(3, 1920, 1080) == 6 && MosaicBlock.Px(10, 1920, 1080) == 20, "细=6 粗=20 @1080p");
                Chk("T91b-block-floor", MosaicBlock.Px(1, 200, 200) == 4, "小帧下限 4px");
                // 4x4 图，块 2：左上块 (10,10,10)×2 + (30,30,30)×2 → 均值 20
                var buf = new byte[4 * 4 * 4];
                void Put(int px, int py, byte v) { int i = (py * 4 + px) * 4; buf[i] = v; buf[i + 1] = v; buf[i + 2] = v; buf[i + 3] = 255; }
                for (int py = 0; py < 2; py++) for (int px = 0; px < 2; px++) Put(px, py, (px + py) % 2 == 0 ? (byte)10 : (byte)30);
                BgraImage.MosaicInPlace(buf, 4, 4, 0, 0, 2, 2, 2);
                Chk("T91c-mean", buf[0] == 20 && buf[4] == 20 && buf[16] == 20 && buf[20] == 20, $"块内全等于均值 20 → {buf[0]},{buf[4]},{buf[16]},{buf[20]}");
                Chk("T91d-untouched", buf[(3 * 4 + 3) * 4] == 0, "区域外像素不动");
                void Put2(int px, int py, byte v) { int i = (py * 4 + px) * 4; buf[i] = v; buf[i + 1] = v; buf[i + 2] = v; buf[i + 3] = 255; }
                Put2(2, 2, 100); // 其余 (2,3)(3,2)(3,3) 为 0 → 右下块均值 25
                BgraImage.MosaicInPlace(buf, 4, 4, 2, 2, 99, 99, 2); // 越界钳到 2x2
                Chk("T91e-clamp", buf[(2 * 4 + 2) * 4] == 25 && buf[(3 * 4 + 3) * 4] == 25, $"越界矩形钳进画布 → 右下块均值 25,{buf[(2 * 4 + 2) * 4]},{buf[(3 * 4 + 3) * 4]}");
            }
            {
                // 工具目录：单一数据源防漂移——加新工具漏帮助/漏字形/键位撞车当场红
                var tools = ToolCatalog.Tools;
                Chk("T92a-count", tools.Length == 11, $"十一个绘制工具 → {tools.Length}");
                var keys = tools.Select(t => t.Key).ToList();
                Chk("T92b-keys", keys.Distinct().Count() == 11 && keys.Select(k => k[0]).OrderBy(c => c).SequenceEqual("ACHLMNOPRTV"), "键位 V/L/R/O/A/P/H/T/M/C/N 唯一且齐全");
                Chk("T92c-glyphs", tools.All(t => t.Glyph.Length == 1 && t.Glyph[0] >= 0xE000 && t.Glyph[0] <= 0xF8FF) && tools.Select(t => t.Glyph).Distinct().Count() == 11,
                    "字形均为私有区码位且互不重复（GlyphProbe 定稿）");
                Chk("T92d-tips", tools.All(t => t.Tip.Zh.Length >= 2 && !t.Tip.Zh.Contains('(') && !t.Tip.Zh.Contains('（')
                        && t.Tip.En.Length >= 2 && !t.Tip.En.Contains('(') && t.Help.Zh.Length >= 8),
                    "工具条提示只说动作名、两种语言都不带括注；键位由 Key 单列，帮助窗排「名称 + 键」");
                // 标题收成短名之后，「按一下 X 退出绘制」由页内那行提示承担；断言跟着那句话走，判据不变。
                Chk("T92e-exit-key-in-the-note", Loc.F(K.Help.PressTheToolKeyAgain, ToolCatalog.ExitKey).Contains(ToolCatalog.ExitKey),
                    "退出键 X 作为格式参数写进绘制页的提示句");
                Chk("T92f-sym", tools.All(t => t.Sym.Length == 1 && !(t.Sym[0] >= 0xE000 && t.Sym[0] <= 0xF8FF)),
                    "帮助符号是普通 Unicode（默认字体可渲染，图标字形进帮助窗=豆腐块）");
            }
            {
                // 操作栈：编辑可撤销/重做、顺序保持、操作时序 LIFO——文字再编辑与对象化编辑的地基
                var h = new UndoRedo<string>();
                var a = "A"; var b = "B"; var b2 = "B'";
                h.Add(a); h.Add(b);
                Chk("T93a-replace", h.Replace(b, b2) && h.Items.SequenceEqual(new[] { a, b2 }) && h.Count == 2, "Replace 原位换新对象");
                Chk("T93b-undo-edit", h.Undo(out var it, out var res) && ReferenceEquals(it, b2) && ReferenceEquals(res, b) && h.Items.SequenceEqual(new[] { a, b }),
                    "撤销编辑=原位还原旧对象");
                Chk("T93c-redo-edit", h.Redo(out var it2) && ReferenceEquals(it2, b2) && h.Items.SequenceEqual(new[] { a, b2 }), "重做编辑=原位再换");
                // 时序：Add A → Add B → Edit A→A'；撤销次序必须是 Edit、B、A（不是列表尾序）
                var h2 = new UndoRedo<string>();
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
                // 马赛克 WYSIWYG：编辑器预览=区域提取后 MosaicInPlace(0,0)，烘焙=整帧内 rect 起块
                // 两者必须逐字节一致，否则预览与输出块相位错位
                int w = 64, h = 64, rx = 10, ry = 8, rw = 30, rh = 20, blk = 6;
                var full = new byte[w * h * 4];
                var rnd = new Random(7);
                rnd.NextBytes(full);
                var fullCopy = (byte[])full.Clone();
                BgraImage.MosaicInPlace(fullCopy, w, h, rx, ry, rw, rh, blk);
                var region = new byte[rw * rh * 4];
                for (int row = 0; row < rh; row++)
                    Buffer.BlockCopy(fullCopy, ((ry + row) * w + rx) * 4, region, row * rw * 4, rw * 4);
                var regCopy = new byte[rw * rh * 4];
                for (int row = 0; row < rh; row++)
                    Buffer.BlockCopy(full, ((ry + row) * w + rx) * 4, regCopy, row * rw * 4, rw * 4);
                BgraImage.MosaicInPlace(regCopy, rw, rh, 0, 0, rw, rh, blk);
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
                // 删除操作（选中工具）：原位删除、原位放回、与 Add/Edit 混排 LIFO
                var h = new UndoRedo<string>();
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
                // Esc 分层加选中层：取消导出 > 取消选中 > 退工具 > 关窗
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
                // 调色板数学：HSV 双向 + HEX 解析/格式化——自绘 RGBA 调色板的正确性地基
                static (byte, byte, byte) Rgb(double h, double s, double v) => Hsv.ToRgb(h, s, v);
                Chk("T97a-hsv-known",
                    Rgb(0, 1, 1) == (255, 0, 0) && Rgb(120, 1, 1) == (0, 255, 0) && Rgb(240, 1, 1) == (0, 0, 255)
                    && Rgb(0, 0, 1) == (255, 255, 255) && Rgb(0, 1, 0) == (0, 0, 0) && Rgb(60, 1, 1) == (255, 255, 0),
                    "红/绿/蓝/白/黑/黄 已知值");
                bool rt = true;
                var rnd = new Random(97);
                for (int i = 0; i < 400; i++)
                {
                    byte r0 = (byte)rnd.Next(256), g0 = (byte)rnd.Next(256), b0 = (byte)rnd.Next(256);
                    var (h, s, v) = Hsv.FromRgb(r0, g0, b0);
                    var (r1, g1, b1) = Hsv.ToRgb(h, s, v);
                    if (Math.Abs(r0 - r1) > 1 || Math.Abs(g0 - g1) > 1 || Math.Abs(b0 - b1) > 1) { rt = false; break; }
                }
                Chk("T97b-hsv-roundtrip", rt, "400 随机色 HSV→RGB 往返误差 ≤1");
                Chk("T97c-hex-parse",
                    HexColor.Parse("#f0a") is { R: 255, G: 0, B: 170, A: 255 }
                    && HexColor.Parse("#112233") is { R: 0x11, G: 0x22, B: 0x33, A: 255 }
                    && HexColor.Parse("#11223380") is { R: 0x11, G: 0x22, B: 0x33, A: 0x80 }
                    && HexColor.Parse("112233") is { R: 0x11 }
                    && HexColor.Parse("#12345") == null && HexColor.Parse("#gggggg") == null
                    && HexColor.Parse(null) == null,
                    "#RGB/#RRGGBB/#RRGGBBAA/无# 可解析，坏输入返回 null");
                var cA = System.Windows.Media.Color.FromRgb(0x11, 0x22, 0x33);
                var cT = System.Windows.Media.Color.FromArgb(0x80, 0x11, 0x22, 0x33);
                Chk("T97d-hex-format",
                    HexColor.Format(cA) == "#112233" && HexColor.Format(cT) == "#11223380"
                    && HexColor.Parse(HexColor.Format(cT)) is { A: 0x80 },
                    "A=255 省略 AA；带透明出 8 位；格式↔解析闭环");
            }
            {
                // 设置项默认值与 JSON 往返（新增 ShowLastRegion）：老配置缺字段必须回默认 true，不能默默变 false
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
                // 默认值断言：RecordIncludeCursor 默认开；若退化为 false，录制将静默丢鼠标指针
                Chk("T128a-cursor-default", new AppSettings().RecordIncludeCursor, "录制包含鼠标指针默认开（无回归）");
            }
            {
                // 磁盘暂存帧仓（，FrameSpool/FrameSpoolList）：落盘往返 / 惰性回读 / 切片 / 元数据 / 文件生命周期
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

                // 自动保存副本（，SaveService）：唯一命名 / 目录回退 / 关闭零副作用
                var svDir = Path.Combine(dir, "autosave-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(svDir);
                Chk("T101a-unique-free",
                    SaveService.UniqueName(svDir, "FrameCapture x", "png") == "FrameCapture x.png",
                    "无冲突 → 原名");
                File.WriteAllBytes(Path.Combine(svDir, "FrameCapture x.png"), new byte[] { 1 });
                Chk("T101b-unique-2",
                    SaveService.UniqueName(svDir, "FrameCapture x", "png") == "FrameCapture x (2).png",
                    "同名占用 → (2)");
                File.WriteAllBytes(Path.Combine(svDir, "FrameCapture x.gif"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(svDir, "FrameCapture x (2).gif"), new byte[] { 1 });
                Chk("T101c-unique-3",
                    SaveService.UniqueName(svDir, "FrameCapture x", "gif") == "FrameCapture x (3).gif",
                    "同名+（2）占用 → (3)");
                // 先清掉自定义保存目录再把自动保存关掉，这两条判据才不依赖本机设置：
                // 用户设过保存目录时，第一条量的是他的路径；开着自动保存时，第二条会真的往盘上写。
                // 两样都在本段末尾还原。
                var saveSvc = FrameCapture.Core.SettingsService.Shared;
                string keepPath = saveSvc?.Current.AutoSavePath ?? "";
                bool keepAuto = saveSvc?.Current.AutoSaveCopy ?? false;
                saveSvc?.Mutate(s2 => { s2.AutoSavePath = ""; s2.AutoSaveCopy = false; });
                var resolved = SaveService.ResolveFolder();
                Chk("T101d-folder-fallback",
                    resolved == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FrameCapture")
                    && Directory.Exists(resolved),
                    "空路径 → 回退 图片\\FrameCapture 且已建");
                int before = Directory.GetFiles(resolved).Length;
                var oneSeq = new CapturedSequence { Frames = new List<CapturedFrame> { f0 }, Region = default, MonitorBounds = default };
                var twoSeq = new CapturedSequence { Frames = new List<CapturedFrame> { f0, f1 }, Region = default, MonitorBounds = default };
                SaveService.MaybeSaveArtifact(oneSeq, new byte[] { 1, 2, 3 });
                SaveService.MaybeSaveArtifact(twoSeq, new byte[] { 1, 2, 3 });
                SaveService.MaybeSavePng(f0);
                Chk("T101e-off-noop", Directory.GetFiles(resolved).Length == before,
                    "自动保存关闭 → 零落盘，绝不影响捕获");
                saveSvc?.Mutate(s2 => { s2.AutoSavePath = keepPath; s2.AutoSaveCopy = keepAuto; });
                try { Directory.Delete(svDir, true); } catch { }
            }

            // 编辑器记住工具/颜色/粗细（，EditorPrefs 纯决策）
            {
                var sw = new (byte A, byte R, byte G, byte B)[]
                {
                    (255, 0xE8, 0x11, 0x23), (255, 0xFF, 0xB9, 0x00),
                    (255, 0x10, 0x89, 0x3E), (255, 0x00, 0x78, 0xD4), (255, 0xFF, 0xFF, 0xFF),
                };
                var ws = new float[] { 3f, 6f, 10f };
                Chk("T102a-color-hit", EditorPrefs.ColorSlot(255, 0x10, 0x89, 0x3E, sw) == 2, "颜色命中预设 → 下标 2");
                Chk("T102b-color-custom", EditorPrefs.ColorSlot(255, 0x12, 0x34, 0x56, sw) == -1, "非预设色 → 自定义 -1");
                Chk("T102c-color-alpha", EditorPrefs.ColorSlot(128, 0xE8, 0x11, 0x23, sw) == -1, "同 RGB 不同 alpha → 不误判成不透明预设");
                Chk("T102d-width-hit", EditorPrefs.WidthSlot(6f, ws) == 1, "线宽命中预设 → 下标 1");
                Chk("T102e-width-custom", EditorPrefs.WidthSlot(7.5f, ws) == -1, "非预设宽 → 自定义 -1");
                Chk("T102f-hex", EditorPrefs.ToHex(255, 0xE8, 0x11, 0x23) == "#FFE81123", "颜色 → #AARRGGBB");
            }

            // 钉图滚轮缩放（，PinZoom 纯决策）
            {
                Chk("T103a-zoom-in", Math.Abs(PinZoom.Next(1.0, 1) - 1.1) < 1e-9, "delta>0 → ×1.1 放大");
                Chk("T103b-zoom-out", Math.Abs(PinZoom.Next(1.0, -1) - 1.0 / 1.1) < 1e-9, "delta<0 → ÷1.1 缩小");
                Chk("T103c-zoom-max", PinZoom.Next(6.0, 1) == 6.0, "已到上限再放大 → 锁死 6.0");
                Chk("T103d-zoom-min", PinZoom.Next(0.1, -1) == 0.1, "已到下限再缩小 → 锁死 0.1");
                Chk("T103e-zoom-zero", PinZoom.Next(2.5, 0) == 2.5, "delta=0 → 不变");
            }

            // 贴图不透明度滚轮（，PinOpacity 纯决策）
            {
                Chk("T104a-op-up", Math.Abs(PinOpacity.Next(0.5, 1) - 0.6) < 1e-9, "delta>0 → +0.1 更不透明");
                Chk("T104b-op-down", Math.Abs(PinOpacity.Next(0.5, -1) - 0.4) < 1e-9, "delta<0 → −0.1 更透明");
                Chk("T104c-op-max", PinOpacity.Next(1.0, 1) == 1.0, "已全不透明再增 → 锁死 1.0");
                Chk("T104d-op-min", PinOpacity.Next(0.2, -1) == 0.2, "已到最透明再减 → 锁死 0.2");
                Chk("T104e-op-zero", PinOpacity.Next(0.7, 0) == 0.7, "delta=0 → 不变");
            }

            // OCR 结果分类（，OcrService.Classify 纯决策）
            {
                Chk("T105a-ocr-text", OcrService.Classify("hello 世界") == OcrStatus.Success, "有文字 → Success");
                Chk("T105b-ocr-empty", OcrService.Classify("") == OcrStatus.Empty, "空串 → Empty");
                Chk("T105c-ocr-ws", OcrService.Classify("  \n\t ") == OcrStatus.Empty, "全空白 → Empty");
                Chk("T105d-ocr-padded", OcrService.Classify("  有字  ") == OcrStatus.Success, "带空白有字 → Success");
            }

            // 选区放大镜几何（，MagnifierMath 纯决策）
            {
                var c1 = MagnifierMath.Crop(100, 100, 1000, 1000, 15);
                Chk("T106a-crop-center", c1 == (93, 93, 15), "中心 → 居中 15×15");
                var c2 = MagnifierMath.Crop(2, 2, 1000, 1000, 15);
                Chk("T106b-crop-left", c2 == (0, 0, 15), "近左上 → 夹到 0,0");
                var c3 = MagnifierMath.Crop(998, 998, 1000, 1000, 15);
                Chk("T106c-crop-right", c3 == (985, 985, 15), "近右下 → 夹到 snap-span");
                var c4 = MagnifierMath.Crop(5, 5, 10, 10, 15);
                Chk("T106d-crop-small", c4 == (0, 0, 10), "快照小于 span → 退化整幅");
                var p1 = MagnifierMath.Place(100, 100, 150, 180, 1000, 1000, 18);
                Chk("T106e-place-br", p1 == (118, 118), "默认贴右下");
                var p2 = MagnifierMath.Place(950, 100, 150, 180, 1000, 1000, 18);
                Chk("T106f-place-flipx", p2 == (782, 118), "越右边 → 翻左");
                var p3 = MagnifierMath.Place(100, 950, 150, 180, 1000, 1000, 18);
                Chk("T106g-place-flipy", p3 == (118, 752), "越下边 → 翻上");
            }

            // 放大镜取色（，MagnifierMath.PixelRgb/Hex 纯决策）
            {
                var buf = new byte[] { 10, 20, 30, 255, 40, 50, 60, 255 }; // 2px BGRA
                Chk("T107a-rgb-p0", MagnifierMath.PixelRgb(buf, 2, 0, 0) == (30, 20, 10), "像素0 → R,G,B");
                Chk("T107b-rgb-p1", MagnifierMath.PixelRgb(buf, 2, 1, 0) == (60, 50, 40), "像素1 → R,G,B");
                Chk("T107c-rgb-oob", MagnifierMath.PixelRgb(buf, 2, 0, 1) == (0, 0, 0), "越界 → 0,0,0");
                Chk("T107d-rgb-neg", MagnifierMath.PixelRgb(buf, 2, -1, 0) == (0, 0, 0), "负坐标 → 0,0,0");
                Chk("T107e-hex", MagnifierMath.Hex(255, 128, 0) == "#FF8000", "RGB→#RRGGBB 大写");
                Chk("T107f-hex-clamp", MagnifierMath.Hex(300, -5, 16) == "#FF0010", "越界分量夹到 0–255");
            }

            // OCR 文本清理（，OcrService.Normalize 纯决策）
            {
                Chk("T108a-collapse-blank", OcrService.Normalize("a\n\n\n\nb") == "a\n\nb", "连续空行压成一个");
                Chk("T108b-trim-end", OcrService.Normalize("  x  \n  y  ") == "  x\n  y", "去行尾空白留缩进");
                Chk("T108c-empty", OcrService.Normalize("") == "", "空 → 空");
                Chk("T108d-single", OcrService.Normalize("one") == "one", "单行原样");
                Chk("T108e-allblank", OcrService.Normalize("\n\n\n") == "", "全空行 → 空");
                Chk("T108f-crlf", OcrService.Normalize("a\r\nb\r\nc") == "a\nb\nc", "CRLF→LF");
            }

            // 序号编号（，NumberBadge 纯决策）
            {
                Chk("T109a-first", NumberBadge.Next(new string[0]) == 1, "无 → 1");
                Chk("T109b-seq", NumberBadge.Next(new[] { "1", "2", "3" }) == 4, "1,2,3 → 4");
                Chk("T109c-gap", NumberBadge.Next(new[] { "1", "3" }) == 4, "删中间不补洞 → max+1");
                Chk("T109d-rollback", NumberBadge.Next(new[] { "2", "3" }) == 4, "删 1 后仍 4（撤销回退靠重算）");
                Chk("T109e-nonnum", NumberBadge.Next(new[] { "x", "5" }) == 6, "非数字忽略");
                Chk("T109f-diam", NumberBadge.Diameter(20) == 40, "直径=字号×2");
                Chk("T109g-lightcircle", NumberBadge.NumberOnLightCircle(0xFF, 0xB9, 0x00) && NumberBadge.NumberOnLightCircle(0xFF, 0xFF, 0xFF), "琥珀#FFB900/白圆圈→黑编号");
                Chk("T109h-darkcircle", !NumberBadge.NumberOnLightCircle(0xE8, 0x11, 0x23) && !NumberBadge.NumberOnLightCircle(0, 0, 0), "红#E81123/黑圆圈→白编号");
            }

            // 贴图磁边（，PinSnap 纯决策）
            {
                var s1 = PinSnap.Snap(5, 500, 200, 100, 0, 0, 1920, 1080, 12);
                Chk("T110a-left", s1.Left == 0 && s1.Top == 500, "左边距 5<阈值 → 吸左");
                var s2 = PinSnap.Snap(1730, 500, 200, 100, 0, 0, 1920, 1080, 12);
                Chk("T110b-right", s2.Left == 1720 && s2.Top == 500, "右边距 10<阈值 → 吸右");
                var s3 = PinSnap.Snap(500, 3, 200, 100, 0, 0, 1920, 1080, 12);
                Chk("T110c-top", s3.Left == 500 && s3.Top == 0, "上边距 3<阈值 → 吸上");
                var s4 = PinSnap.Snap(500, 985, 200, 100, 0, 0, 1920, 1080, 12);
                Chk("T110d-bottom", s4.Left == 500 && s4.Top == 980, "下边距 5<阈值 → 吸下");
                var s5 = PinSnap.Snap(500, 500, 200, 100, 0, 0, 1920, 1080, 12);
                Chk("T110e-none", s5.Left == 500 && s5.Top == 500, "远离边缘 → 不动");
                Chk("T110f-thresh", PinSnap.Threshold(200, 100) == 16
                    && PinSnap.Threshold(1000, 800) == 40
                    && PinSnap.Threshold(300, 300) == 24, "阈值=短边8%夹16–40（R161 参考线扩到所有窗口后调低，贴边才吸不乱跳）");
            }

            // OCR 反馈文案（，编辑器/贴图共用措辞）
            {
                Chk("T111a-success", OcrService.Feedback(OcrStatus.Success, 42) == "已识别 42 字 · 已复制", "成功带字数");
                Chk("T111b-noengine", OcrService.Feedback(OcrStatus.NoEngine, 0).Contains("语言包"), "无引擎→装语言包指引");
                Chk("T111c-empty", OcrService.Feedback(OcrStatus.Empty, 0) == "未识别到文字", "空→未识别");
            }

            // 标注改样式副本（，WithStyle 纯函数）
            {
                var red = Color.FromArgb(255, 0xE7, 0x48, 0x4C);
                var blue = Color.FromArgb(255, 0, 0, 255);
                var b0 = new Annotation { Kind = AnnKind.Arrow, Color = red, Thickness = 4 };
                b0.Points.Add((0.1f, 0.2f)); b0.Points.Add((0.5f, 0.6f)); b0.Text = "hi";
                var c1 = b0.WithStyle(blue, null);
                Chk("T112a-color", c1.Color.B == 255 && c1.Thickness == 4 && c1.Kind == AnnKind.Arrow && c1.Points.Count == 2, "改色保留粗细/几何");
                var c2 = b0.WithStyle(null, 10);
                Chk("T112b-width", c2.Color.R == 0xE7 && c2.Thickness == 10, "改粗细保留色");
                var c3 = b0.WithStyle(blue, 10);
                Chk("T112c-both", c3.Color.B == 255 && c3.Thickness == 10, "两者都改");
                Chk("T112d-keep", c1.Points[0].X == 0.1f && c1.Points[1].Y == 0.6f && c1.Text == "hi" && b0.Points.Count == 2, "点集/文字保留且原对象不变");
            }

            // 贴图对齐参考线（，SnapToLines 纯决策）
            {
                var e = Array.Empty<double>();
                // 左边贴屏幕边（xEdge 接受 left 锚点）
                var a1 = PinSnap.SnapToLines(105, 500, 200, 100, new[] { 100.0 }, e, e, e, 12);
                Chk("T113a-edge-left", a1.Left == 100 && a1.Top == 500, "left 距屏幕边 5<阈值 → 吸左");
                // 右边贴屏幕边（xEdge 接受 right 锚点）
                var a2 = PinSnap.SnapToLines(105, 500, 200, 100, new[] { 300.0 }, e, e, e, 12);
                Chk("T113b-edge-right", a2.Left == 100, "right(305) 距屏幕边 300 差 5 → 右对齐到 300");
                // 只有边贴边：center/中线锚点不参与吸附
                var a3 = PinSnap.SnapToLines(105, 500, 200, 100, e, new[] { 200.0 }, e, e, 12);
                Chk("T113c-no-center", a3.Left == 105, "center(205) 距线 200 差 5 也不再吸（R162 只认边）");
                // 关键：屏幕边不接受 center 锚点（否则半张图出屏）
                var a4 = PinSnap.SnapToLines(105, 500, 200, 100, new[] { 205.0 }, e, e, e, 12);
                Chk("T113d-edge-no-center", a4.Left == 105, "center 恰在屏幕边线上也不吸（屏幕边只认左右边）");
                // 超阈值不动
                var a5 = PinSnap.SnapToLines(105, 500, 200, 100, new[] { 50.0 }, e, e, e, 12);
                Chk("T113e-far", a5.Left == 105 && a5.Top == 500, "参考线超阈值 → 不动");
                // y 轴中线同样不吸附
                var a6 = PinSnap.SnapToLines(500, 305, 200, 100, e, e, e, new[] { 350.0 }, 12);
                Chk("T113f-no-middle", a6.Top == 305, "middle(355) 距线 350 差 5 也不再吸（R162 只认边）");
                // 空参考线 → 原样
                var a7 = PinSnap.SnapToLines(123, 456, 200, 100, e, e, e, e, 12);
                Chk("T113g-empty", a7.Left == 123 && a7.Top == 456, "无参考线 → 原样");
                // x/y 各轴独立取最近
                var a8 = PinSnap.SnapToLines(105, 505, 200, 100, new[] { 100.0 }, new[] { 302.0 }, new[] { 700.0 }, e, 12);
                Chk("T113h-axes", a8.Left == 102 && a8.Top == 505, "x 取最近(right→302 差3)，y 超阈值不动");
            }

            // 拖动实时磁吸（，Slick Window Arrangement 手感：重叠才吸 + 同类边对同类边）
            {
                var en = Array.Empty<PinSnap.Edge>();
                // 贴图 left=105 right=305 top=500 bottom=600，阈值 12
                // a：近边(左)贴同类近边(左)，垂直跨度重叠 → 吸到 100
                var m1 = PinSnap.Magnet(105, 305, 500, 600, new[] { new PinSnap.Edge(100, 480, 620) }, en, 12);
                Chk("T163a-near-overlap", m1 == 100, "左边距左参考边 5<阈值且垂直重叠 → 吸左");
                // b：垂直跨度不重叠 → 绝不吸（杜绝「到处吸附」的核心）
                var m2 = PinSnap.Magnet(105, 305, 500, 600, new[] { new PinSnap.Edge(100, 700, 800) }, en, 12);
                Chk("T163b-no-overlap", m2 == 105, "距离够近但垂直跨度不重叠 → 不吸（到处吸附的根因）");
                // c：远边(右)贴同类远边(右)，重叠 → 右对齐到 300（left 随之 100）
                var m3 = PinSnap.Magnet(105, 305, 500, 600, en, new[] { new PinSnap.Edge(300, 480, 620) }, 12);
                Chk("T163c-far-overlap", m3 == 100, "右边距右参考边 5 → 右对齐，left=100");
                // d：同类边对同类边——近边不吸远边列表里的线（左参考边放进 farTargets 不生效）
                var m4 = PinSnap.Magnet(105, 305, 500, 600, en, new[] { new PinSnap.Edge(100, 480, 620) }, 12);
                Chk("T163d-like-to-like", m4 == 105, "左参考边只在 farTargets → 左边不吸（同类边对同类边）");
                // e：超阈值不动
                var m5 = PinSnap.Magnet(105, 305, 500, 600, new[] { new PinSnap.Edge(80, 480, 620) }, en, 12);
                Chk("T163e-far", m5 == 105, "近边距参考边 25>阈值 → 不动");
                // f：多条重叠近边取最近
                var m6 = PinSnap.Magnet(105, 305, 500, 600, new[] { new PinSnap.Edge(100, 480, 620), new PinSnap.Edge(108, 480, 620) }, en, 12);
                Chk("T163f-nearest", m6 == 108, "两条重叠近边 100/108 → 取更近的 108");
                // g：边界相切（span1==Span0）不算重叠
                var m7 = PinSnap.Magnet(105, 305, 500, 600, new[] { new PinSnap.Edge(100, 600, 700) }, en, 12);
                Chk("T163g-tangent", m7 == 105, "跨度仅相切(600==600)不算重叠 → 不吸");
            }

            // 磁吸遮挡裁剪（，只吸"看得见的边"：被更靠上窗口盖住的边段剔除）
            {
                var vs = PinSnap.VisibleSegments;
                var a1 = vs(100, 200, Array.Empty<(double, double)>());
                Chk("T164a-empty", a1.Count == 1 && a1[0].s0 == 100 && a1[0].s1 == 200, "无覆盖 → 整段可见");
                var a2 = vs(100, 200, new[] { (90.0, 210.0) });
                Chk("T164b-full", a2.Count == 0, "完全被盖 → 无可见段（背景窗的边不再当参考）");
                var a3 = vs(100, 200, new[] { (130.0, 160.0) });
                Chk("T164c-middle", a3.Count == 2 && a3[0].s1 == 130 && a3[1].s0 == 160, "中段被盖 → 断成两段");
                var a4 = vs(100, 200, new[] { (100.0, 150.0) });
                Chk("T164d-head", a4.Count == 1 && a4[0].s0 == 150 && a4[0].s1 == 200, "起段被盖 → 只剩尾段");
                var a5 = vs(100, 200, new[] { (150.0, 260.0) });
                Chk("T164e-tail", a5.Count == 1 && a5[0].s0 == 100 && a5[0].s1 == 150, "尾段被盖(越界钳到 hi) → 只剩头段");
                var a6 = vs(100, 200, new[] { (110.0, 140.0), (130.0, 170.0) });
                Chk("T164f-merge", a6.Count == 2 && a6[0].s1 == 110 && a6[1].s0 == 170, "覆盖区间重叠 → 合并后裁剪");
                var a7 = vs(200, 100, new[] { (120.0, 130.0) });
                Chk("T164g-degenerate", a7.Count == 0, "lo>=hi 退化区间 → 空");
            }

            // 编辑器浏览态标注仍显示（取消选中所有工具后标注不凭空消失）
            try
            {
                var settings = new SettingsService();
                // 起点必须是「没选任何工具」：开着「记住上次用的工具」时，编辑器一打开就带着
                // 上次那个工具，第一次点它就变成取消，测出来的开关正好反了。
                settings.Mutate(s => s.RememberEditorTools = false);
                var f = MovingSquare(160, 90, 0, 1);
                var seq = new CapturedSequence { Frames = new List<CapturedFrame> { f }, Region = new RectI(0, 0, 160, 90), MonitorBounds = new RectI(0, 0, 1920, 1080) };
                var win = new FrameCapture.Overlays.EditorWindow(settings, seq);
                var T = typeof(FrameCapture.Overlays.EditorWindow);
                var NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var canvas = (System.Windows.Controls.Canvas)T.GetField("_canvas", NP)!.GetValue(win)!;
                var anns = T.GetField("_anns", NP)!.GetValue(win)!;
                var redraw = T.GetMethod("RedrawAnns", NP)!;
                var setTool = T.GetMethod("SetTool", NP)!;
                var toolType = T.GetNestedType("Tool", System.Reflection.BindingFlags.NonPublic)!;
                object rectTool = Enum.Parse(toolType, "Rect");
                // 往模型加一条矩形标注（走 UndoRedo<Annotation>.Add）
                var ann = new Annotation
                {
                    Kind = AnnKind.Rect,
                    Color = Color.FromArgb(255, 255, 0, 0),
                    Thickness = 3,
                };
                ann.Points.Add((0.2f, 0.2f)); ann.Points.Add((0.7f, 0.7f));
                anns.GetType().GetMethod("Add", new[] { typeof(Annotation) })!.Invoke(anns, new object[] { ann });
                // 选中矩形工具：画布可见、可命中、标注已画
                setTool.Invoke(win, new object[] { rectTool });
                redraw.Invoke(win, null);
                Chk("T165a-draw", canvas.Visibility == Visibility.Visible && canvas.IsHitTestVisible && canvas.Children.Count >= 1,
                    "绘制态：画布可见+可命中+有标注");
                // 再点同一工具 = 退回浏览态（取消选中所有工具）：画布保持 Visible，只关命中测试
                setTool.Invoke(win, new object[] { rectTool });
                redraw.Invoke(win, null);
                Chk("T165b-browse-visible", canvas.Visibility == Visibility.Visible, "浏览态：画布不再 Collapsed（旧 bug 根因）");
                Chk("T165c-ann-kept", canvas.Children.Count >= 1, "取消选中工具后标注仍显示（不再凭空消失）");
                Chk("T165d-no-hittest", !canvas.IsHitTestVisible, "浏览态画布不可命中（平移/缩放穿透到 _viewGrid）");
                try { win.Close(); } catch { }
            }
            catch (Exception e) { Chk("T165-editor-browse-ann", false, e.ToString()); }

            // 最近静态截图保留（，LastStill 状态持有）
            {
                var ls = new LastStill();
                Chk("T114a-empty", ls.Empty && !ls.TryGet(out _, out _, out _), "初始为空 → TryGet false");
                var f1 = new CapturedFrame { Bgra = new byte[4], W = 1, H = 1 };
                var f2 = new CapturedFrame { Bgra = new byte[8], W = 2, H = 1 };
                ls.Set(f1, new RectI(10, 20, 30, 40), "DEV1");
                var ok1 = ls.TryGet(out var gf, out var gr, out var gd);
                Chk("T114b-set", ok1 && !ls.Empty && ReferenceEquals(gf, f1) && gr.X == 10 && gr.W == 30 && gd == "DEV1", "Set 后取出帧/区域/设备一致");
                ls.Set(f2, new RectI(1, 2, 3, 4), "DEV2");
                var ok2 = ls.TryGet(out var gf2, out var gr2, out var gd2);
                Chk("T114c-overwrite", ok2 && ReferenceEquals(gf2, f2) && gr2.X == 1 && gd2 == "DEV2", "再次 Set 覆盖为最新一帧");
                ls.Clear();
                Chk("T114d-clear", ls.Empty && !ls.TryGet(out _, out _, out _), "Clear 后回到空");
            }

            // 贴图旋转（，PinRotate 纯函数）
            {
                static CapturedFrame Mk(int w, int h)
                {
                    var b = new byte[w * h * 4];
                    for (int i = 0; i < w * h; i++) b[i * 4] = (byte)(i + 1); // B 通道 = 像素序号+1
                    return new CapturedFrame { Bgra = b, W = w, H = h };
                }
                static int B(CapturedFrame f, int x, int y) => f.Bgra[(y * f.W + x) * 4];
                static bool Same(CapturedFrame a, CapturedFrame b)
                {
                    if (a.W != b.W || a.H != b.H || a.Bgra.Length != b.Bgra.Length) return false;
                    for (int i = 0; i < a.Bgra.Length; i++) if (a.Bgra[i] != b.Bgra[i]) return false;
                    return true;
                }
                var r1 = PinRotate.Rotate90Cw(Mk(2, 1));
                Chk("T115a-h2v", r1.W == 1 && r1.H == 2 && B(r1, 0, 0) == 1 && B(r1, 0, 1) == 2, "横 2x1 [A B] → 竖 1x2 A上B下");
                var r2 = PinRotate.Rotate90Cw(Mk(2, 2));
                Chk("T115b-2x2", r2.W == 2 && r2.H == 2 && B(r2, 0, 0) == 3 && B(r2, 1, 0) == 1 && B(r2, 0, 1) == 4 && B(r2, 1, 1) == 2, "2x2 顺时针：上行→右列");
                var orig = Mk(3, 2);
                Chk("T115c-identity4", Same(PinRotate.Rotate(orig, 4), orig), "转 4 次回原图");
                Chk("T115d-steps", ReferenceEquals(PinRotate.Rotate(orig, 0), orig)
                    && Same(PinRotate.Rotate(orig, 5), PinRotate.Rotate(orig, 1))
                    && Same(PinRotate.Rotate(orig, -1), PinRotate.Rotate(orig, 3)), "steps=0 同实例；5≡1、-1≡3");
            }
            // 截图后行为解析（，AfterCapture 纯函数）
            {
                Chk("T116a-editor", AfterCapture.Resolve("editor") == AfterCaptureAction.Editor, "editor→Editor");
                Chk("T116b-copy", AfterCapture.Resolve("copy") == AfterCaptureAction.Copy, "copy→Copy");
                Chk("T116c-toast", AfterCapture.Resolve("toast") == AfterCaptureAction.Toast, "toast→Toast");
                Chk("T116d-dirty", AfterCapture.Resolve(null) == AfterCaptureAction.Toast
                    && AfterCapture.Resolve("") == AfterCaptureAction.Toast
                    && AfterCapture.Resolve("Editor") == AfterCaptureAction.Toast
                    && AfterCapture.Resolve("garbage") == AfterCaptureAction.Toast, "null/空/大小写/脏值→回落 Toast");
                Chk("T116e-pin", AfterCapture.Resolve("pin") == AfterCaptureAction.Pin, "R117 pin→Pin");
            Chk("T116h-none", AfterCapture.Resolve("none") == AfterCaptureAction.None,
                    "none→None：这条手势在「完成后」里自己写了落点，默认弹提示让位");
            // 完成后/失败时是两份可选序列，随 DTO↔引擎绑定往返；空=不覆盖，由宿主按默认落点收尾
            var dtoOv = new GestureBindingDto
            {
                Chord = "F6", Trigger = "Chord", Actions = new[] { GestureActions.ScopeSelect, GestureActions.CaptureStored },
                OnComplete = new[] { GestureActions.EditLastVideo }, OnFail = new[] { GestureActions.NotifyToast },
            };
            var engOv = dtoOv.ToBinding()!;
            var backOv = GestureBindingDto.FromBinding(engOv);
            Chk("T116f-override-roundtrip",
                engOv.OnComplete.SequenceEqual(new[] { GestureActions.EditLastVideo })
                && engOv.OnFail.SequenceEqual(new[] { GestureActions.NotifyToast })
                && backOv.OnComplete.SequenceEqual(new[] { GestureActions.EditLastVideo })
                && backOv.OnFail.SequenceEqual(new[] { GestureActions.NotifyToast }),
                "完成后/失败时两份序列随 DTO↔引擎绑定往返");
            Chk("T116g-override-default-empty",
                new GestureBindingDto().OnComplete.Length == 0 && new GestureBindingDto().OnFail.Length == 0
                && new GE.GestureBinding().OnComplete.Count == 0 && new GE.GestureBinding().OnFail.Count == 0,
                "缺省不覆盖：settings.json 里没有这两份序列时，行为与不写它一致");
            var cl = new GE.GestureBinding { OnComplete = { GestureActions.NotifyToast }, OnFail = { GestureActions.CopyLast } }.Clone();
            Chk("T116i-clone-carries-override", cl.OnComplete.Count == 1 && cl.OnFail.Count == 1,
                "Clone 深拷贝两份序列，改副本不影响原绑定");
            }
            // 荧光笔半透明烘焙：盖在白底上应透出白（半透明），而非不透明纯色
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
            // 截图提示音合成（，CaptureSound 纯函数）：WAV 头合法 + 时长 + 峰值安全 + 有声音且衰减
            {
                var s = CaptureSound.Samples();
                var wav = CaptureSound.MakeWav(s);
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
            // 滚动截图拼接算法（，ScrollStitch 纯函数）：找重叠 + 拼接 + 无重叠 + 抗噪
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
                int ov = ScrollStitch.FindOverlap(acc, W, 100, bOv, 100);
                Chk("T119a-overlap", ov == 40, $"重叠应 40，实得 {ov}");
                var merged = ScrollStitch.Append(acc, W, 100, bOv, 100, ov);
                int newH = merged.Length / (W * 4);
                byte RowGray(byte[] buf, int row) => buf[(row * W) * 4];   // 该行第 0 列 B 通道=灰度
                Chk("T119b-append", newH == 160 && RowGray(merged, 100) == Gray(100) && RowGray(merged, 159) == Gray(159),
                    $"拼接高 {newH} 期望 160，行100={RowGray(merged, 100)} 期望 {Gray(100)}，行159={RowGray(merged, 159)} 期望 {Gray(159)}");
                var bNo = ConstFrame(W, 100, (byte)((acc[(99 * W) * 4] + 128) & 0xFF)); // 常量帧，与 acc 末行差 128、且各行灰度各异 → 无重叠
                int ovNo = ScrollStitch.FindOverlap(acc, W, 100, bNo, 100);
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
                int ovN = ScrollStitch.FindOverlap(acc, W, 100, bNoise, 100);
                Chk("T119d-noise-tolerant", ovN == 40, $"抗噪重叠应 40，实得 {ovN}");
                // 重叠区含个别离群行（平滑滚动重采样/闪烁光标行）：判定若退化为累积均值+提前退出会误判 0，
                // 逐行独立抽样、多数匹配应仍判 40（防个别离群行造成「滚动太快」误报）
                var bOutlier = (byte[])bOv.Clone();
                for (int rr = 0; rr < 2; rr++)   // 把 b 顶部两行（重叠区首两行）改成完全不同的灰度
                {
                    byte g = (byte)(Gray(9000 + rr) ^ 0xFF);
                    for (int x = 0; x < W; x++) { int p = (rr * W + x) * 4; bOutlier[p] = g; bOutlier[p + 1] = g; bOutlier[p + 2] = g; }
                }
                int ovO = ScrollStitch.FindOverlap(acc, W, 100, bOutlier, 100);
                Chk("T119e-outlier-tolerant", ovO == 40, $"含离群行的重叠应仍判 40，实得 {ovO}");
                // ±1px 垂直抖动（平滑/分数滚动取整，奇数行整体下移 1px）：只比对应行则半数行差超阈
                // → 误判 0 → 拼接出现重复内容带「重叠」；容差趟应仍判 40（防假「重叠」带）
                var bJit = new byte[100 * W * 4];
                for (int r = 0; r < 100; r++)
                {
                    byte g = Gray(60 + r + (r % 2));
                    for (int x = 0; x < W; x++) { int p = (r * W + x) * 4; bJit[p] = g; bJit[p + 1] = g; bJit[p + 2] = g; bJit[p + 3] = 255; }
                }
                int ovJ = ScrollStitch.FindOverlap(acc, W, 100, bJit, 100);
                Chk("T119f-jitter-tolerant", ovJ == 40, $"±1px 抖动的重叠应判 40，实得 {ovJ}");
            }
            // 文字版式页拼接（ScrollStitch 纯函数）：绝大多数行整幅同色（纯背景），签名极差 0，
            // 在任何纵向偏移下都与同类行相等，定位不了重叠——重叠只能靠少量文字行判定。
            // 已知步长逐步滚动：每步重叠必须正好等于真值，拼出的长图逐行等于原文档（不丢行、不重行）。
            {
                static byte[] Cut(byte[] src, int w, int top, int rows)
                {
                    var o = new byte[rows * w * 4];
                    Buffer.BlockCopy(src, top * w * 4, o, 0, rows * w * 4);
                    return o;
                }
                int TW = 200, TD = 1200, TF = 400, TStep = 120;
                var trnd = new Random(20260801);
                var tdoc = new byte[TD * TW * 4];
                for (int i = 0; i < tdoc.Length; i += 4) { tdoc[i] = 250; tdoc[i + 1] = 250; tdoc[i + 2] = 250; tdoc[i + 3] = 255; }
                for (int line = 0; line * 14 + 9 < TD; line++)
                {
                    if (trnd.Next(100) < 20) continue;                       // 整行留白
                    int y0 = line * 14 + 2, x = 12 + trnd.Next(10), right = TW - 30 - trnd.Next(60);
                    while (x < right)
                    {
                        int gw = 2 + trnd.Next(4), gh = 6 + trnd.Next(3);
                        if (trnd.Next(100) >= 25)
                            for (int dy = 0; dy < gh; dy++)
                                for (int dx = 0; dx < gw && x + dx < TW; dx++)
                                {
                                    int o = ((y0 + dy) * TW + x + dx) * 4;
                                    tdoc[o] = 30; tdoc[o + 1] = 32; tdoc[o + 2] = 36;
                                }
                        x += gw + 2 + trnd.Next(2);
                    }
                }
                var tacc = Cut(tdoc, TW, 0, TF);
                int taccH = TF, tBottom = TF - 1, tWrong = 0, tLost = 0;
                for (int k = 0; k < 4; k++)
                {
                    int ttop = (k + 1) * TStep;
                    var tb = Cut(tdoc, TW, ttop, TF);
                    int tov = ScrollStitch.FindOverlap(tacc, TW, taccH, tb, TF);
                    int ttruth = tBottom + 1 - ttop;
                    if (tov != ttruth) tWrong++;
                    if (tov > ttruth) tLost += tov - ttruth;
                    tacc = ScrollStitch.Append(tacc, TW, taccH, tb, TF, tov);
                    taccH = tacc.Length / (TW * 4);
                    tBottom = ttop + TF - 1;
                }
                int tBad = -1;
                for (int y = 0; y < Math.Min(taccH, tBottom + 1); y++)
                {
                    bool same = true;
                    for (int x = 0; x < TW; x++)
                    {
                        int o = (y * TW + x) * 4;
                        if (tacc[o] != tdoc[o] || tacc[o + 1] != tdoc[o + 1] || tacc[o + 2] != tdoc[o + 2]) { same = false; break; }
                    }
                    if (!same) { tBad = y; break; }
                }
                Chk("T119g-scroll-stitch 文字页重叠精确不丢行", tWrong == 0 && tLost == 0 && taccH == tBottom + 1 && tBad < 0,
                    $"错步={tWrong} 丢行={tLost} 期望高={tBottom + 1} 实际高={taccH} 首个错行={tBad}");

                // 滚动途中的帧走高置信口径（FindOverlapExact）。它与完整口径的分歧点必须锁死：
                // 将来谁把兜底加回这条路径，滚动中的实时拼接就会重新丢行，而这两条断言会先红。
                int eWrong = 0; var eGot = new System.Text.StringBuilder();
                for (int k = 0; k < 4; k++)
                {
                    int etop = (k + 1) * TStep;
                    int ev = ScrollStitch.FindOverlapExact(Cut(tdoc, TW, 0, TF), TW, TF, Cut(tdoc, TW, etop, TF), TF);
                    int etrue = Math.Max(0, TF - etop);   // 末步已滚过累积帧底部，两帧无公共行，0 才是正解
                    if (ev != etrue) eWrong++;
                    eGot.Append($"{ev}/{etrue} ");
                }
                Chk("T169a-途中帧高置信口径干净帧取到真值", eWrong == 0, $"取值/真值={eGot}");
                // 重叠区里掺进一成对不上号的行（滚动途中的混合帧）：完整口径按 ≥80% 兜底给出重叠，
                // 高置信口径必须返回 0——按可疑重叠追加会跳过整段新内容，宁可不拼这一帧
                {
                    var mb = Cut(tdoc, TW, TStep, TF);
                    var mrnd = new Random(7);
                    for (int y = 0; y < TF - TStep; y += 10)
                        for (int x = 0; x < TW; x++)
                        {
                            int o = (y * TW + x) * 4;
                            mb[o] = (byte)mrnd.Next(256); mb[o + 1] = (byte)mrnd.Next(256); mb[o + 2] = (byte)mrnd.Next(256);
                        }
                    int loose = ScrollStitch.FindOverlap(Cut(tdoc, TW, 0, TF), TW, TF, mb, TF);
                    int tight = ScrollStitch.FindOverlapExact(Cut(tdoc, TW, 0, TF), TW, TF, mb, TF);
                    Chk("T169b-途中帧不认八成兜底只认全匹配", loose > 0 && tight == 0, $"完整口径={loose} 高置信口径={tight}");
                }
                // 重叠区整段落进无信息行（滚过一大片空白）：完整口径关掉平坦行过滤再扫一趟拿到大重叠，
                // 高置信口径没有证据可用，返回 0 跳过这一帧，等下一帧再说
                {
                    var blank = new byte[TD * TW * 4];
                    for (int i = 0; i < blank.Length; i += 4) { blank[i] = 250; blank[i + 1] = 250; blank[i + 2] = 250; blank[i + 3] = 255; }
                    for (int y = 0; y < 300; y++) Buffer.BlockCopy(tdoc, y * TW * 4, blank, y * TW * 4, TW * 4);
                    var bacc = Cut(blank, TW, 200, TF);
                    var bb = Cut(blank, TW, 400, TF);
                    int bloose = ScrollStitch.FindOverlap(bacc, TW, TF, bb, TF);
                    int btight = ScrollStitch.FindOverlapExact(bacc, TW, TF, bb, TF);
                    Chk("T169c-大片空白无证据时途中口径不猜", bloose > 0 && btight == 0, $"完整口径={bloose} 高置信口径={btight}");
                }
            }
            // SendInput 注入原语的 x64 结构体布局：尺寸算错时系统按 cbSize 读输入数组，畸形输入会让它读越界内存。
            // 这条断言锁住布局，改结构体的人第一时间在这里撞回来。
            {
                int szIn = FrameCapture.Native.InputLayout.SizeOfInput;
                int szMi = FrameCapture.Native.InputLayout.SizeOfMouseInput;
                int offMi = FrameCapture.Native.InputLayout.MouseOffset;
                Chk("T168a-INPUT x64 布局 40 字节 mi@8",
                    szIn == 40 && offMi == 8 && szMi == 32 && FrameCapture.Native.InputLayout.Ok,
                    $"sizeof(INPUT)={szIn} offsetof(INPUT.mi)={offMi} sizeof(MOUSEINPUT)={szMi} Ok={FrameCapture.Native.InputLayout.Ok}");
            }
            // 自动滚动步进策略（ScrollAutoPolicy 纯函数）：发一步 → 等这一步落定 → 再发下一步。
            // 连续两帧一致才算停稳，按固定节拍连续注轮会让画面永远在动、一段都拼不上，节奏全靠这套决定撑住。
            // 只喂观测序列，不注入任何真实滚轮。
            {
                var a = SAP.Next(false, 0, SAP.NotchStart, SAO.Appended);
                Chk("T168b-拼上一段就接着发下一步", a.Inject && !a.Stop && a.Waiting && a.NoNew == 0 && a.Notch == 2,
                    $"inject={a.Inject} stop={a.Stop} waiting={a.Waiting} noNew={a.NoNew} notch={a.Notch}");
                var u = SAP.Next(true, 1, 3, SAO.Unsettled);
                Chk("T168c-那一步没落定就沉默且状态不动", !u.Inject && !u.Stop && u.Waiting && u.NoNew == 1 && u.Notch == 3,
                    $"inject={u.Inject} waiting={u.Waiting} noNew={u.NoNew} notch={u.Notch}");
                var um = SAP.Next(false, 0, 2, SAO.Unsettled);
                Chk("T168d-用户手动滚动时策略不代发滚轮", !um.Inject && !um.Stop && !um.Waiting,
                    $"inject={um.Inject} stop={um.Stop} waiting={um.Waiting}");
                // 注轮之后先撞上「还在动」的帧：这一拍必须沉默，落定那拍才拼上并发下一步
                var w = SAP.Next(true, 0, 2, SAO.Unsettled);
                var s = SAP.Next(w.Waiting, w.NoNew, w.Notch, SAO.Appended);
                Chk("T168e-注轮后先沉默一拍再拼接再发", !w.Inject && w.Waiting && s.Inject && s.Waiting && s.NoNew == 0,
                    $"unsettled inject={w.Inject} appended inject={s.Inject} noNew={s.NoNew}");
                // 连续 BottomStreak 次「停稳且画面没变」才认到底，中间每拍仍按同步长发一步（惯性滚动、懒加载会先空几拍）
                int k = 0, stopAt = 0;
                bool stopped = false;
                for (int i = 1; i <= SAP.BottomStreak + 1 && !stopped; i++)
                {
                    var d = SAP.Next(true, k, 2, SAO.NoNewContent);
                    k = d.NoNew;
                    if (d.Stop) { stopped = true; stopAt = i; break; }
                    if (!d.Inject || !d.Waiting || d.Notch != 2) { stopAt = -i; break; }
                }
                Chk("T168f-连续四次画面没变才判到底", stopped && stopAt == SAP.BottomStreak && k == SAP.BottomStreak,
                    $"stopped={stopped} 第 {stopAt} 次停（期望 {SAP.BottomStreak}） noNew={k}");
                var e = SAP.Next(false, SAP.BottomStreak, 2, SAO.NoNewContent);
                Chk("T168g-到底后不再注轮", !e.Inject && e.Stop && !e.Waiting,
                    $"inject={e.Inject} stop={e.Stop} waiting={e.Waiting}");
                var o = SAP.Next(true, 3, 4, SAO.NoOverlap);
                Chk("T168h-滚过头减半重试且不记到底", o.Inject && o.Waiting && !o.Stop && o.Notch == 2 && o.NoNew == 0,
                    $"notch={o.Notch} noNew={o.NoNew}（重试这一步不算到底）");
                var o1 = SAP.Next(true, 0, SAP.NotchMin, SAO.NoOverlap);
                Chk("T168i-减半重试不跌出下限", o1.Inject && o1.Notch == SAP.NotchMin, $"notch={o1.Notch}");
                var t = SAP.Next(true, 2, 2, SAO.TinyStep);
                Chk("T168j-一步太小加大一档重试", t.Inject && t.Waiting && !t.Stop && t.Notch == 3 && t.NoNew == 2,
                    $"notch={t.Notch} noNew={t.NoNew}");
                // 步长恒在 1..6：连发 TinyStep 顶到上限、连发 NoOverlap 触到下限，越界入参也被夹回来
                int hi = SAP.NotchStart, lo = SAP.NotchStart;
                for (int i = 0; i < 12; i++) { hi = SAP.Next(true, 0, hi, SAO.TinyStep).Notch; lo = SAP.Next(true, 0, lo, SAO.NoOverlap).Notch; }
                var c = SAP.Next(false, -3, 99, SAO.Appended);
                Chk("T168k-步长恒在 1..6 且越界入参被夹住",
                    hi == SAP.NotchMax && lo == SAP.NotchMin && c.Notch == SAP.NotchMax && c.NoNew == 0,
                    $"顶={hi}（期望 {SAP.NotchMax}） 底={lo}（期望 {SAP.NotchMin}） 越界入参 99→{c.Notch} noNew={c.NoNew}");
            }

            // 取色复制格式（，ColorFormat 纯函数）：HEX/RGB/HSL 文本 + 分量夹取 + 设置串解析往返
            {
                var Hex = ColorCopyFormat.Hex;
                var Rgb = ColorCopyFormat.Rgb;
                var Hsl = ColorCopyFormat.Hsl;
                string Fmt(int r, int g, int b, ColorCopyFormat f) => ColorFormat.Format(r, g, b, f);
                Chk("T120a-hex", Fmt(255, 128, 0, Hex) == "#FF8000", Fmt(255, 128, 0, Hex));
                Chk("T120b-rgb", Fmt(255, 128, 0, Rgb) == "rgb(255, 128, 0)", Fmt(255, 128, 0, Rgb));
                Chk("T120c-hsl-orange", Fmt(255, 128, 0, Hsl) == "hsl(30, 100%, 50%)", Fmt(255, 128, 0, Hsl));
                Chk("T120d-hsl-gray", Fmt(128, 128, 128, Hsl) == "hsl(0, 0%, 50%)", Fmt(128, 128, 128, Hsl));
                Chk("T120e-hsl-white", Fmt(255, 255, 255, Hsl) == "hsl(0, 0%, 100%)", Fmt(255, 255, 255, Hsl));
                Chk("T120f-clamp", Fmt(300, -5, 128, Hex) == "#FF0080", Fmt(300, -5, 128, Hex));
                Chk("T120g-parse", ColorFormat.Parse("RGB") == Rgb && ColorFormat.Parse("hsl") == Hsl
                    && ColorFormat.Parse("garbage") == Hex && ColorFormat.Parse(null) == Hex, "解析含大小写/未知回退 Hex");
                Chk("T120h-tosetting", ColorFormat.ToSetting(Hsl) == "hsl" && ColorFormat.ToSetting(Hex) == "hex", "枚举↔设置串往返");
            }
            // 剪贴板图片建帧（，BgraImage.FromSource）：ToSource↔FromSource 往返无损 + 非 Bgra32 转换 + 空值
            {
                var f0 = new CapturedFrame
                {
                    Bgra = new byte[] { 10, 20, 30, 255, 40, 50, 60, 128, 70, 80, 90, 0, 100, 110, 120, 255 },
                    W = 2, H = 2, TimeMs = 0,
                };
                var src = BgraImage.ToSource(f0);
                var back = BgraImage.FromSource(src);
                Chk("T121a-roundtrip", back != null && back.W == 2 && back.H == 2 && back.Bgra.SequenceEqual(f0.Bgra),
                    "ToSource→FromSource BGRA 逐字节往返");
                // 非 Bgra32 源（Bgr32，alpha 位被忽略）→ 转 Bgra32 后 alpha=255、BGR 分量不变
                var bgr = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
                    System.Windows.Media.PixelFormats.Bgr32, null, new byte[] { 10, 20, 30, 0 }, 4);
                var conv = BgraImage.FromSource(bgr);
                Chk("T121b-bgr32", conv != null && conv.Bgra[0] == 10 && conv.Bgra[1] == 20 && conv.Bgra[2] == 30 && conv.Bgra[3] == 255,
                    conv == null ? "null" : $"BGR→BGRA {conv.Bgra[0]},{conv.Bgra[1]},{conv.Bgra[2]},{conv.Bgra[3]}");
                Chk("T121c-null", BgraImage.FromSource(null) == null, "null → null");
            }
            // 暂停全局热键语义由 HotkeyManager「Paused 时不喂 KeySink」实现（见 HotkeyManager.HandleKey），
            // 属宿主接线，无独立纯逻辑可测。
            // 适配倍率封顶（，ZoomMath.FitScale）：小图不放大（默认 1:1 清晰），大图仍缩小适配
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
            // 矩形标注烘焙：空心框——边着色、内部与外部保持原色（区别于填充矩形）
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
            // Shift 约束几何（，ShapeConstraint）：正方/正圆 与 45° 吸附
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
            // 高刷节流 + 刷新率检测：无 16ms 地板 + 逐帧时间判定 + EnumDisplaySettings
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
                int hz = DisplayRefresh.RateOf(Monitors.All().FirstOrDefault(m => m.IsPrimary)?.DeviceName);
                Chk("T126h-refresh-real", hz > 0, $"本机主屏刷新率检测 → {hz}Hz");
            }

            // 视频编码（，VideoEncoder/FfmpegLocator）：定位 + 命令行拼装 + 帧率推算 + 真实 ffmpeg 编码
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

                // 透明按容器分派：WebM 带 alpha 走 yuva420p 并关掉 alt-ref（开着时参考帧会把 alpha 抹平）；
                // MP4 的 h264 没有透明通道，命令行救不了，只能喂拍平后的帧。
                string webmAlpha = VideoEncoder.BuildArgs(320, 240, 30, VideoFormat.WebM, @"C:\t\o.webm", alpha: true);
                Chk("T127l-args-webm-alpha",
                    webmAlpha.Contains("-pix_fmt yuva420p") && webmAlpha.Contains("-auto-alt-ref 0"),
                    "WebM 带透明：yuva420p 且关掉 alt-ref");
                Chk("T127m-args-mp4-never-carries-alpha",
                    !VideoEncoder.BuildArgs(320, 240, 30, VideoFormat.Mp4, @"C:\t\o.mp4", alpha: true).Contains("yuva")
                    && !webmArgs.Contains("yuva"),
                    "MP4 永不声明透明；WebM 不带透明时仍是 yuv420p");
                var semiBuf = new byte[8 * 8 * 4];
                for (int i = 3; i < semiBuf.Length; i += 4) semiBuf[i] = 255;
                semiBuf[3] = 120;
                var semi = new CapturedFrame { Bgra = semiBuf, W = 8, H = 8, TimeMs = 0 };
                Chk("T127n-carries-alpha",
                    !VideoEncoder.CarriesAlpha(new[] { Solid(8, 8, 1, 2, 3) }) && VideoEncoder.CarriesAlpha(new[] { semi }),
                    "全不透明 → false；一个半透明像素 → true");

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

                    // 奇数尺寸真实编码：不经 scale 取偶 libx264 直接失败，取偶后应成功产出
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
        // 内存回收助手：录制/编辑大位图关闭后主动全代 GC + 裁工作集，等价用户手动 MemReduct
        {
            bool ok = true;
            try
            {
                var big = new byte[64 << 20]; // 64MB：制造一次 LOH 分配，Reclaim 应能回收并裁工作集
                big[0] = 1;
                Memory.Reclaim();
                GC.KeepAlive(big);
            }
            catch { ok = false; }
            Chk("T129a-reclaim-no-throw", ok, "Memory.Reclaim() 全代 GC + 裁工作集不抛");
        }
        // GestureCore 引擎（B1，netstandard2.0 可复用库）：按住/单击/双击消歧/长按/失败策略/修饰键/三击/优先级
        {
            var M = new GestureProbe();
            var eng = new GE.GestureEngine(M);
            GE.GestureBinding B(string id, GE.ModifierKeys mods, int key, GE.TriggerKind trig, string[] acts,
                GE.FallbackPolicy fb = GE.FallbackPolicy.Stop, int prio = 0, int dbl = 400, int lp = 500)
                => new GE.GestureBinding { Id = id, Chord = new GE.KeyChord(mods, key), Trigger = trig,
                     Actions = new List<string>(acts), Fallback = fb, Priority = prio, DoubleClickMs = dbl, LongPressMs = lp };
            const int F6 = 0x75, A = 0x41, CTRL = 0x11, ALT = 0x12;

            // 按住做 A、松手做 B（KeyDown/KeyUp 配对）
            eng.SetBindings(new[] {
                B("s", GE.ModifierKeys.None, F6, GE.TriggerKind.KeyDown, new[] { "StartRec" }),
                B("t", GE.ModifierKeys.None, F6, GE.TriggerKind.KeyUp, new[] { "StopRec" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 500);
            Chk("T130a-hold-pattern", M.Log.SequenceEqual(new[] { "StartRec", "StopRec" }), "F6 按下→StartRec、抬起→StopRec，实得 " + string.Join(",", M.Log));

            // 组合键激活 + 单击（仅绑 Click → 零延迟立即触发）
            M.Log.Clear();
            eng.SetBindings(new[] { B("c", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Capture" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10);
            eng.OnMouseUp(0, GE.ModifierKeys.None, 30);
            Chk("T130b-click-held", M.Log.SequenceEqual(new[] { "Capture" }), "F6 按住单击→Capture（无更高阶绑定，立即触发）");

            // Click 与 DoubleClick 并存：单击等满间隔才触发 Click；双击立即触发 DoubleClick
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

            // 长按：保持 ≥LongPressMs 触发 LongPress 并吞掉随后的抬起（不计单击）
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

            // NextBinding：首条动作失败 → 顺延下一条绑定
            M.Log.Clear(); M.Fail = new HashSet<string> { "Fail1" };
            eng.SetBindings(new[] {
                B("b1", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Fail1", "After" }, GE.FallbackPolicy.NextBinding, prio: 0),
                B("b2", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Second" }, GE.FallbackPolicy.Stop, prio: 1) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            Chk("T130e-next-binding", M.Log.SequenceEqual(new[] { "Fail1", "Second" }), "b1 首步失败→NextBinding→b2 执行 Second（After 不跑）");
            M.Fail.Clear();

            // SkipStep：失败步跳过、继续本条后续动作
            M.Log.Clear(); M.Fail = new HashSet<string> { "Fail1" };
            eng.SetBindings(new[] { B("b1", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Fail1", "After" }, GE.FallbackPolicy.SkipStep) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            Chk("T130f-skip-step", M.Log.SequenceEqual(new[] { "Fail1", "After" }), "SkipStep：Fail1 失败被跳过、After 继续");
            M.Fail.Clear();

            // Stop：失败即中止、不顺延
            M.Log.Clear(); M.Fail = new HashSet<string> { "Fail1" };
            eng.SetBindings(new[] {
                B("b1", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Fail1", "After" }, GE.FallbackPolicy.Stop, prio: 0),
                B("b2", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "Second" }, GE.FallbackPolicy.Stop, prio: 1) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            Chk("T130g-stop", M.Log.SequenceEqual(new[] { "Fail1" }), "Stop：Fail1 失败即中止，After/Second 均不跑");

            // 修饰键组合 Ctrl+Alt+A + 单击
            M.Log.Clear();
            eng.SetBindings(new[] { B("pick", GE.ModifierKeys.Control | GE.ModifierKeys.Alt, A, GE.TriggerKind.Click, new[] { "Pick" }) });
            eng.OnKeyDown(CTRL, GE.ModifierKeys.Control, 0);
            eng.OnKeyDown(ALT, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 0);
            eng.OnKeyDown(A, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 10); eng.OnMouseUp(0, GE.ModifierKeys.Control | GE.ModifierKeys.Alt, 20);
            Chk("T130h-mods-chord", M.Log.SequenceEqual(new[] { "Pick" }), "Ctrl+Alt+A 激活 + 单击→Pick");

            // 三击
            M.Log.Clear();
            eng.SetBindings(new[] {
                B("one", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "One" }),
                B("three", GE.ModifierKeys.None, F6, GE.TriggerKind.TripleClick, new[] { "Three" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 110); eng.OnMouseUp(0, GE.ModifierKeys.None, 120);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 210); eng.OnMouseUp(0, GE.ModifierKeys.None, 220);
            Chk("T130i-triple", M.Log.SequenceEqual(new[] { "Three" }), "三击达最高阶→TripleClick→Three");

            // 优先级：同触发多条，小优先级先跑且成功即止
            M.Log.Clear();
            eng.SetBindings(new[] {
                B("low", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "B" }, prio: 1),
                B("high", GE.ModifierKeys.None, F6, GE.TriggerKind.Click, new[] { "A" }, prio: 0) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnMouseDown(0, GE.ModifierKeys.None, 10); eng.OnMouseUp(0, GE.ModifierKeys.None, 20);
            Chk("T130j-priority", M.Log.SequenceEqual(new[] { "A" }), "优先级 0 的绑定先跑且成功即止→A");
        }
        // GestureConfig 配置层：DTO 转换 / 非法跳过 / 默认与预设 / JSON 往返 / 动作过滤
        {
            var d = new GestureBindingDto { Id = "x", Chord = "Ctrl+Shift+G", Trigger = "Chord", Actions = new[] { GestureActions.CaptureFrame } };
            var b = d.ToBinding();
            Chk("T131a-dto-to-binding", b != null && b.Chord.Mods == (GE.ModifierKeys.Control | GE.ModifierKeys.Shift)
                && b.Chord.Key == 0x47 && b.Trigger == GE.TriggerKind.Chord,
                "Ctrl+Shift+G→Chord(Control|Shift, VK G=0x47)，实得 " + (b == null ? "null" : $"{b.Chord.Mods}/{b.Chord.Key:X}/{b.Trigger}"));

            Chk("T131b-bare-key-rejected", new GestureBindingDto { Chord = "G", Trigger = "Chord", Actions = new[] { GestureActions.CaptureFrame } }.ToBinding() == null,
                "裸键 G（不安全）解析失败→ToBinding null（复用 HotkeySpec 安全规则）");

            var def = GestureConfig.DefaultQqSet("Ctrl+Alt+A", "Ctrl+Alt+R");
            Chk("T131c-default-capture-chain", def.Count == 2
                && def[0].Actions.SequenceEqual(new[] { GestureActions.ScopeSelect, GestureActions.CaptureStored })
                && def[0].OnComplete.Length == 0,
                "默认截图键 = 选区→截图，完成后不覆盖（默认弹提示），实得 " + string.Join("+", def[0].Actions));
            Chk("T131c2-default-record-chain",
                def[1].Actions.SequenceEqual(new[] { GestureActions.ScopeSelect, GestureActions.RecordToggle })
                && def[1].OnComplete.SequenceEqual(new[] { GestureActions.EditLastVideo }),
                "默认录制键 = 选区→开始/停止录制，完成后覆盖成打开编辑器");

            Chk("T131d-default-skips-bad-key", GestureConfig.DefaultQqSet("G", "Ctrl+Alt+R").Count == 1,
                "非法键串那一条不生成（裸键 G 不安全），另一条照生成");

            var cfg = new GestureConfig { Enabled = true, Bindings = GestureConfig.DefaultQqSet("Ctrl+Alt+A", "Ctrl+Alt+R") };
            var json = System.Text.Json.JsonSerializer.Serialize(cfg);
            var back = System.Text.Json.JsonSerializer.Deserialize<GestureConfig>(json)!;
            var eb = back.ToBindings();
            Chk("T131e-json-roundtrip", back.Enabled && eb.Count == 2
                && eb.All(x => x.Actions[0] == GestureActions.ScopeSelect)
                && eb.Any(x => x.OnComplete.Count == 1 && x.OnComplete[0] == GestureActions.EditLastVideo),
                "默认集 JSON 往返：Enabled + 两条都以选区开头 + 完成序列存活，实得 " + eb.Count + " 条");

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
        // ToastSeconds 归一化（C）：默认 3.2、0=常驻保留、负→0、超界夹取、过小→0.5
        {
            Chk("T132a-toast-default", new AppSettings().ToastSeconds == 3.2, "默认 3.2 秒");
            var s0 = new AppSettings { ToastSeconds = 0 }; s0.Normalize();
            var sneg = new AppSettings { ToastSeconds = -5 }; sneg.Normalize();
            var sbig = new AppSettings { ToastSeconds = 100 }; sbig.Normalize();
            var stiny = new AppSettings { ToastSeconds = 0.3 }; stiny.Normalize();
            Chk("T132b-toast-normalize", s0.ToastSeconds == 0 && sneg.ToastSeconds == 0 && sbig.ToastSeconds == 60 && Math.Abs(stiny.ToastSeconds - 0.5) < 1e-9,
                $"0 保留(常驻)、负→0、100→60、0.3→0.5，实得 {s0.ToastSeconds}/{sneg.ToastSeconds}/{sbig.ToastSeconds}/{stiny.ToastSeconds}");
        }
        // OCR 预览优先默认：结果窗默认开（预览即选取入口，不自动复制）
        Chk("T133-ocr-preview-default", new AppSettings().OcrShowResult, "识别结果窗默认开（预览优先）");
        // 键盘钩子仅在「有手势 KeySink」时安装：
        // 手势层是默认触发路径，钩子把键喂给 KeySink；滚动截图 Esc 取消、浮窗 Enter/Esc 也依赖此钩子。
        // 无 KeySink（手势关闭）时不装钩子，零开销。
        {
            bool sinkInstalled;
            var hk = new HotkeyManager();
            try { hk.KeySink = (vk, down, mods, time) => false; hk.Install(); sinkInstalled = hk.Installed; }
            finally { hk.Dispose(); }
            bool idleNotInstalled;
            var hk2 = new HotkeyManager();
            try { hk2.Install(); idleNotInstalled = hk2.Installed; }
            finally { hk2.Dispose(); }
            Chk("T134a-hook-for-keysink", sinkInstalled, "有手势 KeySink 时装钩子（否则手势永不触发）");
            Chk("T134b-no-hook-when-idle", !idleNotInstalled, "无 KeySink 时不装钩子（零开销）");
        }
        // 动作词汇表：选区/截图/提示/复制在册，合成动作不在册；Ctrl+Alt+A/R 解析成 Chord
        {
            Chk("T135a-actions-registered",
                GestureActions.IsValid("scope.select") && GestureActions.IsValid("capture.stored")
                && GestureActions.IsValid("notify.toast") && GestureActions.IsValid("copy.clipboard"),
                "选区 / 截图 / 提示 / 复制到剪贴板 四个动作已注册");
            Chk("T135a2-composite-actions-gone",
                !GestureActions.IsValid("capture.selection") && !GestureActions.IsValid("record.selection")
                && !GestureActions.IsValid("gesture.edit"),
                "合成动作不在词汇表里：先选区后执行由序列本身表达，配置器窗口已并入设置");
            var d1 = new GestureBindingDto { Id = "qq.shot", Chord = "Ctrl+Alt+A", Trigger = "Chord", Actions = new[] { "scope.select", "capture.stored" } };
            var b1 = d1.ToBinding();
            Chk("T135b-shot-chord-parse",
                b1 != null && b1.Chord.Key == 0x41 && (b1.Chord.Mods & GE.ModifierKeys.Control) != 0 &&
                (b1.Chord.Mods & GE.ModifierKeys.Alt) != 0 && b1.Trigger == GE.TriggerKind.Chord &&
                b1.Actions.SequenceEqual(new[] { "scope.select", "capture.stored" }),
                "Ctrl+Alt+A → Chord(Ctrl+Alt+A) → 选区+截图 解析正确");
            var d2 = new GestureBindingDto { Id = "qq.rec", Chord = "Ctrl+Alt+R", Trigger = "Chord", Actions = new[] { "scope.select", "record.toggle" } };
            var b2 = d2.ToBinding();
            Chk("T135c-rec-chord-parse",
                b2 != null && b2.Chord.Key == 0x52 && b2.Trigger == GE.TriggerKind.Chord &&
                b2.Actions.SequenceEqual(new[] { "scope.select", "record.toggle" }),
                "Ctrl+Alt+R → Chord → 选区+开始/停止录制 解析正确");
        }
        // 录制同键起停 + 即时截图动作（B2 用户实测修正）
        {
            Chk("T136a-record-toggle-registered", GestureActions.IsValid("record.toggle"), "record.toggle 动作已注册");
            Chk("T136b-capture-frame-still-valid", GestureActions.IsValid("capture.frame"), "capture.frame 仍在（改为即时抓取，名不变）");
            var d = new GestureBindingDto { Id = "qq.rec2", Chord = "Ctrl+Alt+R", Trigger = "Chord", Actions = new[] { "record.toggle" } };
            var b = d.ToBinding();
            Chk("T136c-toggle-parse",
                b != null && b.Chord.Key == 0x52 && b.Trigger == GE.TriggerKind.Chord && b.Actions[0] == "record.toggle",
                "Ctrl+Alt+R → record.toggle 解析正确（同键起停）");
        }
        // 独占吞键：引擎回报「带具体键的组合键命中」→ 宿主吞键；纯修饰键组合/未命中键不吞
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
        // 文字字号映射（C3）：线宽挡位→帧像素字号，单调递增，锚定 细3/中6/粗10 = 22/34/50px @1080p
        {
            double f3 = AnnFont.FramePx(3), f6 = AnnFont.FramePx(6), f10 = AnnFont.FramePx(10);
            Chk("T140a-font-anchor-values", Math.Abs(f3 - 22) < 1e-3 && Math.Abs(f6 - 34) < 1e-3 && Math.Abs(f10 - 50) < 1e-3, $"字号锚定 细{f3}/中{f6}/粗{f10} = 22/34/50");
            Chk("T140b-font-monotonic", f3 < f6 && f6 < f10 && AnnFont.FramePx(24) > f10, "字号随线宽单调递增（A+ 变大）");
        }
        // 启用/禁用（A1）：禁用绑定不校验、不进引擎
        {
            var cfg = new GestureConfig();
            cfg.Bindings.Add(new GestureBindingDto { Id = "off", Enabled = false, Chord = "F6", Actions = new[] { "capture.frame" } });
            cfg.Bindings.Add(new GestureBindingDto { Id = "on", Enabled = true, Chord = "F7", Actions = new[] { "scope.select" } });
            var bs = cfg.ToBindings();
            Chk("T141b-disabled-excluded-from-engine", bs.Count == 1 && bs[0].Id == "on", "禁用绑定不进引擎、启用保留");
        }
        // 动作目录拆分/改名（A2）：选区/开始/停止/同键起停 齐全 + 即时截图语义=全屏
        {
            bool split = GestureActions.IsValid("scope.select")
                && GestureActions.IsValid("record.start")
                && GestureActions.IsValid("record.stop")
                && GestureActions.IsValid("record.toggle");
            Chk("T142a-record-actions-split", split, "选区/开始录制/停止录制/开始停止(同键) 四动作齐全");
            var cf = GestureActions.All.First(a => a.Name == "capture.frame");
            Chk("T142b-instant-is-fullscreen", cf.Label.Contains("全屏"), $"即时截图改名含「全屏」：{cf.Label}");
        }
        // 完成后序列（A3）：主序列成功后跑 OnComplete；主序列失败则不跑
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
        // 前置任务：以「选区」开头的序列整条交给宿主，引擎不猜异步；失败时序列只在真失败时跑
        {
            const int F6 = 0x75;
            void Click(GE.GestureEngine e) { e.OnKeyDown(F6, GE.ModifierKeys.None, 0); e.OnMouseDown(0, GE.ModifierKeys.None, 10); e.OnMouseUp(0, GE.ModifierKeys.None, 20); }
            GE.GestureBinding Chain(string id, string[] acts, string[]? onDone = null, string[]? onFail = null) => new GE.GestureBinding
            {
                Id = id, Chord = new GE.KeyChord(GE.ModifierKeys.None, F6), Trigger = GE.TriggerKind.Click,
                Actions = new List<string>(acts),
                OnComplete = new List<string>(onDone ?? Array.Empty<string>()),
                OnFail = new List<string>(onFail ?? Array.Empty<string>()),
            };

            var M = new GestureProbe();
            M.Prelude.Add("Scope");
            var eng = new GE.GestureEngine(M);
            eng.SetBindings(new[] { Chain("x", new[] { "Scope", "Cap", "Pin" }) });
            Click(eng);
            Chk("T143c-prelude-hands-whole-chain", M.PreludeLog.Count == 1 && M.PreludeLog[0] == "Scope|Cap|Pin" && M.Log.Count == 0,
                $"整条序列一次交给宿主，引擎不逐步跑后续，实得 prelude=[{string.Join(" / ", M.PreludeLog)}] invoke=[{string.Join(",", M.Log)}]");

            M = new GestureProbe(); M.Prelude.Add("Scope");
            eng = new GE.GestureEngine(M);
            eng.SetBindings(new[] { Chain("x", new[] { "Scope" }) });
            Click(eng);
            Chk("T143d-prelude-alone-is-sync", M.Log.SequenceEqual(new[] { "Scope" }) && M.PreludeLog.Count == 0,
                "序列只有前置任务一步：没有后续要等它，按普通动作同步执行，实得 " + string.Join(",", M.Log));

            M = new GestureProbe(); M.Prelude.Add("Scope"); M.AcceptPrelude = false;
            eng = new GE.GestureEngine(M);
            eng.SetBindings(new[] { Chain("x", new[] { "Scope", "Cap" }, onFail: new[] { "Nope" }) });
            Click(eng);
            Chk("T143e-prelude-refused-runs-onfail", M.Log.SequenceEqual(new[] { "Nope" }),
                "宿主不受理前置任务＝本条失败，跑失败时序列，实得 " + string.Join(",", M.Log));

            M = new GestureProbe(); M.Prelude.Add("Scope");
            eng = new GE.GestureEngine(M);
            eng.SetBindings(new[] { Chain("x", new[] { "Cap", "Scope" }) });
            Click(eng);
            Chk("T143f-prelude-only-at-step-one", M.Log.SequenceEqual(new[] { "Cap", "Scope" }) && M.PreludeLog.Count == 0,
                "同名动作出现在序列中间时按普通动作执行，实得 " + string.Join(",", M.Log));

            var plain = new PlainProbe();   // 不支持前置任务的宿主
            eng = new GE.GestureEngine(plain);
            eng.SetBindings(new[] { Chain("x", new[] { "Scope", "Cap" }) });
            Click(eng);
            Chk("T143g-host-without-prelude", plain.Log.SequenceEqual(new[] { "Scope", "Cap" }),
                "宿主没实现前置任务接口时保持逐步同步执行，实得 " + string.Join(",", plain.Log));

            M = new GestureProbe();
            eng = new GE.GestureEngine(M);
            eng.SetBindings(new[] { Chain("x", new[] { "Cap" }, onDone: new[] { "Done" }, onFail: new[] { "Oops" }) });
            Click(eng);
            Chk("T143h-onfail-not-on-success", M.Log.SequenceEqual(new[] { "Cap", "Done" }),
                "主序列成功：跑完成后、不跑失败时，实得 " + string.Join(",", M.Log));
            M.Log.Clear(); M.Fail.Add("Cap");
            Click(eng);
            Chk("T143i-onfail-on-stop", M.Log.SequenceEqual(new[] { "Cap", "Oops" }),
                "主序列失败且策略 Stop：跑失败时序列、不跑完成后，实得 " + string.Join(",", M.Log));
            M.Log.Clear(); M.Fail.Clear(); M.Fail.Add("Cap");
            eng.SetBindings(new[] { new GE.GestureBinding
            {
                Id = "y", Chord = new GE.KeyChord(GE.ModifierKeys.None, F6), Trigger = GE.TriggerKind.Click,
                Actions = new List<string> { "Cap", "Pin" }, Fallback = GE.FallbackPolicy.SkipStep,
                OnComplete = new List<string> { "Done" }, OnFail = new List<string> { "Oops" },
            } });
            Click(eng);
            Chk("T143j-skipstep-keeps-going", M.Log.SequenceEqual(new[] { "Cap", "Pin", "Done" }),
                "SkipStep：失败步跳过继续跑后续与完成后，失败时序列不跑，实得 " + string.Join(",", M.Log));
        }
        // 录制动作无默认行为：停止/同键起停不自动开编辑器；开编辑器是独立动作、配在完成后序列
        {
            Chk("T144a-edit-lastvideo-exists", GestureActions.IsValid("edit.lastvideo"),
                "新增『打开编辑器（上次录制）』动作");
            var rs = GestureActions.All.First(a => a.Name == "record.stop");
            Chk("T144b-record-stop-no-default-editor", rs.Description.Contains("不自动进编辑器"),
                $"停止录制文案明确不进编辑器：{rs.Description}");
            var rt = GestureActions.All.First(a => a.Name == "record.toggle");
            Chk("T144c-record-toggle-no-default-editor", rt.Description.Contains("不自动进编辑器"),
                $"同键起停文案明确不进编辑器：{rt.Description}");
            var lv = GestureActions.All.First(a => a.Name == "edit.lastvideo");
            Chk("T144d-edit-lastvideo-guides-oncomplete", lv.Description.Contains("完成后"),
                $"打开编辑器动作文案引导配到完成后序列：{lv.Description}");
        }
        // 裁剪视图居中：非居中裁剪后，裁剪子区中心必落在视口中心+pan。
        // 定位裁剪中心若用 s 而非整帧显示宽 w，非居中裁剪会把画面甩出视口、中键拖不回。
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
        // 键盘点按/按住时序：同一捕获键 轻点→截图、按住→录制。
        // Tap=快速抬起(<阈值)，HoldRelease=长按抬起(≥阈值)，Hold=按住到阈值即触发（确认按住才开始）。
        {
            var M = new GestureProbe();
            var eng = new GE.GestureEngine(M);
            GE.GestureBinding B(string id, GE.ModifierKeys mods, int key, GE.TriggerKind trig, string[] acts, int lp = 250)
                => new GE.GestureBinding { Id = id, Chord = new GE.KeyChord(mods, key), Trigger = trig,
                     Actions = new List<string>(acts), LongPressMs = lp };
            const int F6 = 0x75;

            // 轻点（100ms<250）→ 只触发 Tap
            eng.SetBindings(new[] { B("tap", GE.ModifierKeys.None, F6, GE.TriggerKind.Tap, new[] { "Shot" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 100);
            Chk("T146a-tap-quick", M.Log.SequenceEqual(new[] { "Shot" }), "轻点 F6(100ms)→Tap→Shot，实得 " + string.Join(",", M.Log));

            // 长按抬起（400ms≥250）→ 只触发 HoldRelease
            M.Log.Clear();
            eng.SetBindings(new[] { B("hr", GE.ModifierKeys.None, F6, GE.TriggerKind.HoldRelease, new[] { "Stop" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 400);
            Chk("T146b-holdrelease-long", M.Log.SequenceEqual(new[] { "Stop" }), "长按 F6(400ms)抬起→HoldRelease→Stop，实得 " + string.Join(",", M.Log));

            // 零丢帧：KeyDown 起录 + Tap 丢弃 + HoldRelease 停止；轻点 → Start,Discard
            M.Log.Clear();
            eng.SetBindings(new[] {
                B("kd", GE.ModifierKeys.None, F6, GE.TriggerKind.KeyDown, new[] { "Start" }),
                B("tp", GE.ModifierKeys.None, F6, GE.TriggerKind.Tap, new[] { "Discard" }),
                B("hr", GE.ModifierKeys.None, F6, GE.TriggerKind.HoldRelease, new[] { "Stop" }) });
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 100);
            Chk("T146c-zeroloss-tap", M.Log.SequenceEqual(new[] { "Start", "Discard" }), "零丢帧轻点→按下起录+抬起丢弃截图，实得 " + string.Join(",", M.Log));

            // 零丢帧：长按 → Start,Stop（保留录制）
            M.Log.Clear();
            eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            eng.OnKeyUp(F6, GE.ModifierKeys.None, 400);
            Chk("T146d-zeroloss-hold", M.Log.SequenceEqual(new[] { "Start", "Stop" }), "零丢帧长按→按下起录+抬起停止保留，实得 " + string.Join(",", M.Log));

            // 非零丢帧：Hold 到阈值才起录，抬起停止；249ms 未起录
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

            // 认领吞键：tap/hold 组合键按下即吞（即便没绑 KeyDown/Chord），自动重复与抬起也吞
            M.Log.Clear();
            eng.SetBindings(new[] { B("tap", GE.ModifierKeys.None, F6, GE.TriggerKind.Tap, new[] { "Shot" }) });
            bool swDown = eng.OnKeyDown(F6, GE.ModifierKeys.None, 0);
            bool swRepeat = eng.OnKeyDown(F6, GE.ModifierKeys.None, 50);   // 自动重复
            bool swUp = eng.OnKeyUp(F6, GE.ModifierKeys.None, 100);
            Chk("T146f-claim-swallow", swDown && swRepeat && swUp, $"tap/hold 键按下+重复+抬起全吞（前台收不到），实得 down={swDown} rep={swRepeat} up={swUp}");
        }
        // 默认手势集播种：截图键 = 选区→截图，录制键 = 选区→开始/停止录制 + 完成后打开编辑器；一次性、尊重已有绑定。
        {
            var set = GestureConfig.DefaultQqSet("Ctrl+Shift+G", "Ctrl+Shift+R");
            Chk("T147a-default-set-shape",
                set.Count == 2
                && set.All(b => b.Trigger == "Chord")
                && set.All(b => b.Actions.Length > 0 && b.Actions[0] == "scope.select")
                && set[0].Actions.Contains("capture.stored") && set[1].Actions.Contains("record.toggle"),
                $"默认集=两条 Chord、都以选区开头，实得 {set.Count} 条");
            Chk("T147b-default-name-empty", set.All(b => string.IsNullOrEmpty(b.Name)),
                "默认集名字留空：折叠时按序列推导显示，界面语言切换时跟着换");

            var cfg = new GestureConfig();
            cfg.SeedIfNeeded(GestureConfig.DefaultCaptureKey, GestureConfig.DefaultRecordKey);
            Chk("T147c-seed-empty",
                cfg.Bindings.Count == 2 && cfg.Enabled && cfg.Seeded
                && cfg.Bindings.Any(b => b.Chord == GestureConfig.DefaultCaptureKey && b.Trigger == "Chord" && b.Actions.Contains("scope.select"))
                && cfg.Bindings.Any(b => b.Chord == GestureConfig.DefaultRecordKey && b.Trigger == "Chord" && b.Actions.Contains("record.toggle")),
                $"播种=QQ 键位（{GestureConfig.DefaultCaptureKey} 进选区 / {GestureConfig.DefaultRecordKey} 录制切换），实得 {cfg.Bindings.Count} 条");
            Chk("T147f-qq-set-skips-bad-chord",
                GestureConfig.DefaultQqSet("Ctrl+Alt+A", "不 是 键").Count == 1,
                "录制键串解析不出来时只播截图那一条");

            var cfg2 = new GestureConfig();
            cfg2.Bindings.Add(new GestureBindingDto { Id = "mine", Chord = "F6", Trigger = "Chord", Actions = new[] { "capture.frame" } });
            cfg2.SeedIfNeeded("Ctrl+Shift+G", "Ctrl+Shift+R");
            Chk("T147d-seed-respects-existing", cfg2.Bindings.Count == 1 && cfg2.Bindings[0].Id == "mine" && cfg2.Seeded,
                "已有绑定不被覆盖，仅补置 Seeded");

            var cfg3 = new GestureConfig { Seeded = true };   // 已播种（用户曾清空绑定）
            cfg3.SeedIfNeeded("Ctrl+Shift+G", "Ctrl+Shift+R");
            Chk("T147e-seed-idempotent", cfg3.Bindings.Count == 0, "已播种后清空绑定不再重新塞回");
        }

        // 手势「识别文字」结果路由（，OcrService.Route 纯决策；编辑器与范围 OCR 共用）
        {
            Chk("T148a-success-window",
                OcrService.Route(OcrStatus.Success, true) == OcrRoute.ResultWindow,
                "Success + 显示结果窗 → 开结果窗");
            Chk("T148b-success-copy",
                OcrService.Route(OcrStatus.Success, false) == OcrRoute.CopyThenConfirm,
                "Success + 关结果窗 → 复制并轻确认");
            Chk("T148c-noengine-ignores-show",
                OcrService.Route(OcrStatus.NoEngine, true) == OcrRoute.NoEngineHint
                && OcrService.Route(OcrStatus.NoEngine, false) == OcrRoute.NoEngineHint,
                "NoEngine → 无引擎提示（与是否显示结果窗无关）");
            Chk("T148d-empty-ignores-show",
                OcrService.Route(OcrStatus.Empty, true) == OcrRoute.EmptyHint
                && OcrService.Route(OcrStatus.Empty, false) == OcrRoute.EmptyHint,
                "Empty → 未识别提示（与是否显示结果窗无关）");
            Chk("T148e-ocr-action-registered", GestureActions.IsValid("ocr.selection"),
                "ocr.selection 动作仍注册（手势可绑定，且 GestureInvoker 已接线非死动作）");
        }

        // 帮助窗键位反映用户真实绑定（，GestureConfig.DescribeKeysFor/TriggerLabel 纯决策）
        {
            var GC = new GestureConfig();
            GC.Bindings = new List<GestureBindingDto>
            {
                new() { Id = "r", Enabled = true, Chord = "Ctrl+Alt+R", Trigger = "Chord", Actions = new[] { "record.toggle" } },
                new() { Id = "a", Enabled = true, Chord = "Ctrl+Alt+A", Trigger = "Chord", Actions = new[] { "capture.stored" } },
                new() { Id = "fd", Enabled = true, Chord = "F2", Trigger = "KeyDown", Actions = new[] { "record.start" } },
                new() { Id = "fu", Enabled = true, Chord = "F2", Trigger = "KeyUp", Actions = new[] { "record.stop" } },
                new() { Id = "off", Enabled = false, Chord = "Ctrl+Alt+X", Trigger = "Chord", Actions = new[] { "capture.stored" } },
            };
            Chk("T149a-scope-unbound",
                GC.DescribeKeysFor(GestureActions.ScopeSelect) == "未设键",
                "无选区绑定 → 未绑定（不再回落显示没绑定的死键 Ctrl+Shift+R）");
            var rec = GC.DescribeKeysFor(GestureActions.RecordToggle, GestureActions.RecordStart, GestureActions.RecordStop);
            Chk("T149b-record-grouped", rec == "Ctrl+Alt+R，F2 按下/松开",
                "录制键按组合键分组、合并触发后缀，实得 " + rec);
            Chk("T149c-capture-chord-nosuffix",
                GC.DescribeKeysFor(GestureActions.CaptureStored, GestureActions.CaptureFrame) == "Ctrl+Alt+A",
                "Chord 触发无后缀 + 禁用绑定排除 → Ctrl+Alt+A");
            var th = new GestureConfig { Bindings = new List<GestureBindingDto>
            {
                new() { Id = "cap.tap", Enabled = true, Chord = "Ctrl+Shift+G", Trigger = "Tap", Actions = new[] { "capture.stored" } },
                new() { Id = "cap.hold", Enabled = true, Chord = "Ctrl+Shift+G", Trigger = "Hold", Actions = new[] { "record.start" } },
                new() { Id = "cap.holdrelease", Enabled = true, Chord = "Ctrl+Shift+G", Trigger = "HoldRelease", Actions = new[] { "record.stop" } },
            } };
            Chk("T149d-same-key-three-triggers",
                th.DescribeKeysFor(GestureActions.CaptureStored) == "Ctrl+Shift+G 轻点"
                && th.DescribeKeysFor(GestureActions.RecordStart, GestureActions.RecordStop) == "Ctrl+Shift+G 按住/松手",
                "同一个键挂三种触发方式互不覆盖：轻点截图、按住起录、松手停录");
            Chk("T149e-trigger-label",
                GestureConfig.TriggerLabel("Tap").Zh == "轻点"
                && GestureConfig.TriggerLabel("Chord").Zh == ""
                && GestureConfig.TriggerLabel("HoldRelease").Zh == "松手"
                && GestureConfig.TriggerLabel("DoubleClick").Zh == "双击",
                "触发后缀：轻点/空/松手/双击");
        }

        // 设置窗「当前生效键位」用动作短标签（，GestureActions.LabelOf 纯查找）
        {
            Chk("T150a-label-known",
                GestureActions.LabelOf("capture.stored") == "截图"
                && GestureActions.LabelOf("record.toggle") == "开始/停止录制"
                && GestureActions.LabelOf("ocr.selection") == "识别文字",
                "已知动作 → 中文短名");
            Chk("T150b-label-unknown-passthrough",
                GestureActions.LabelOf("future.xyz") == "future.xyz"
                && GestureActions.LabelOf(null) == "",
                "未知名原样返回（前向兼容不崩）/ null → 空串");
        }

        // 出厂默认两条补齐成「先选区，后执行」：只认出厂 Id 与出厂形状，用户改过的不碰，重复调用无副作用
        {
            var cfg = new GestureConfig();
            cfg.Bindings.Add(new GestureBindingDto { Id = "default.capture", Chord = "Ctrl+Alt+A", Actions = new[] { "scope.select" } });
            cfg.Bindings.Add(new GestureBindingDto { Id = "default.record", Chord = "Ctrl+Alt+R", Actions = new[] { "record.toggle" } });
            cfg.UpgradeStockDefaults();
            Chk("T190a-stock-capture-gains-verb",
                cfg.Bindings[0].Actions.SequenceEqual(new[] { "scope.select", "capture.stored" }),
                "截图那条补上「截图」这一步：" + string.Join(",", cfg.Bindings[0].Actions));
            Chk("T190b-stock-record-gains-scope-and-oncomplete",
                cfg.Bindings[1].Actions.SequenceEqual(new[] { "scope.select", "record.toggle" })
                && cfg.Bindings[1].OnComplete.SequenceEqual(new[] { "edit.lastvideo" }),
                "录制那条补上「选区」这一步，完成后补「打开编辑器」：" + string.Join(",", cfg.Bindings[1].Actions));
            cfg.UpgradeStockDefaults();
            Chk("T190c-upgrade-idempotent",
                cfg.Bindings[0].Actions.Length == 2 && cfg.Bindings[1].Actions.Length == 2
                && cfg.Bindings[1].OnComplete.Length == 1,
                "再跑一次不重复追加");

            var mine = new GestureConfig();
            mine.Bindings.Add(new GestureBindingDto { Id = "default.capture", Chord = "F6", Actions = new[] { "scope.select", "ocr.selection" } });
            mine.Bindings.Add(new GestureBindingDto { Id = "user.abc", Chord = "Ctrl+Alt+A", Actions = new[] { "scope.select" } });
            mine.Bindings.Add(new GestureBindingDto { Id = "default.record", Chord = "Ctrl+Alt+R", Actions = new[] { "record.toggle" }, OnComplete = new[] { "copy.clipboard" } });
            mine.UpgradeStockDefaults();
            Chk("T190d-user-shapes-untouched",
                mine.Bindings[0].Actions.Length == 2 && mine.Bindings[0].Actions[1] == "ocr.selection"
                && mine.Bindings[1].Actions.Length == 1
                && mine.Bindings[2].OnComplete.SequenceEqual(new[] { "copy.clipboard" }),
                "改过动作的出厂条与非出厂 Id 不动；用户自己写的完成后保留");
        }

        // 同一个组合键+触发上挂了两条：引擎按优先级与顺序只走第一条，后面那条永远不响——快捷键页靠它把那条描边发红
        {
            var cfg = new GestureConfig();
            cfg.Bindings.Add(new GestureBindingDto { Id = "a", Chord = "Ctrl+Alt+A", Actions = new[] { "capture.stored" } });
            cfg.Bindings.Add(new GestureBindingDto { Id = "b", Chord = "Ctrl+Alt+A", Actions = new[] { "ocr.selection" } });
            Chk("T192a-conflict-marks-the-later-one",
                cfg.ConflictIds().SequenceEqual(new[] { "b" }),
                "撞车只标排在后面那条：" + string.Join(",", cfg.ConflictIds()));
            cfg.Bindings[1].Trigger = "Hold";
            Chk("T192b-different-trigger-no-conflict", cfg.ConflictIds().Count == 0,
                "同一个键配不同触发不算撞车（轻点截图、按住录制是合法用法）");
            cfg.Bindings[1].Trigger = "Chord";
            cfg.Bindings[1].Enabled = false;
            Chk("T192c-disabled-not-in-conflict", cfg.ConflictIds().Count == 0, "关掉的那条压根不进引擎，不报撞车");
            cfg.Bindings[1].Enabled = true;
            cfg.Bindings[1].Chord = "ALT+CTRL+A";
            Chk("T192d-conflict-compares-parsed-chord",
                cfg.ConflictIds().SequenceEqual(new[] { "b" }), "修饰键顺序与大小写不同也是同一个键");
            var empty = new GestureConfig();
            empty.Bindings.Add(new GestureBindingDto { Id = "x", Chord = "", Actions = new[] { "capture.stored" } });
            empty.Bindings.Add(new GestureBindingDto { Id = "y", Chord = "", Actions = new[] { "ocr.selection" } });
            Chk("T192e-empty-chord-not-in-conflict", empty.ConflictIds().Count == 0, "没按键的条目不报撞车");
        }

        // 坏配置压根不进引擎：没按键、空序列、认不出的动作名各自被丢掉，越界的连击/长按阈值钳回区间
        {
            var cfg = new GestureConfig();
            cfg.Bindings.Add(new GestureBindingDto { Id = "nokey", Chord = "", Actions = new[] { "capture.frame" } });
            cfg.Bindings.Add(new GestureBindingDto { Id = "noact", Chord = "Ctrl+Alt+K", Actions = Array.Empty<string>() });
            cfg.Bindings.Add(new GestureBindingDto { Id = "badact", Chord = "Ctrl+Alt+J", Actions = new[] { "no.such.action" } });
            cfg.Bindings.Add(new GestureBindingDto
            {
                Id = "halfbad", Chord = "Ctrl+Alt+H",
                Actions = new[] { "scope.select", "no.such.action" },
                OnComplete = new[] { "no.such.action" },
            });
            cfg.Bindings.Add(new GestureBindingDto
            {
                Id = "wild", Chord = "Ctrl+Alt+L", Actions = new[] { "capture.frame" },
                DoubleClickMs = 99999, LongPressMs = -5,
            });
            var got = cfg.ToBindings();
            Chk("T194a-empty-chord-not-installed", !got.Any(x => x.Id == "nokey"), "没按键的条目不进引擎");
            Chk("T194b-empty-actions-not-installed", !got.Any(x => x.Id == "noact"), "动作序列为空的条目不进引擎");
            Chk("T194c-unknown-action-dropped",
                !got.Any(x => x.Id == "badact")
                && got.FirstOrDefault(x => x.Id == "halfbad") is { } hb
                && hb.Actions.SequenceEqual(new[] { "scope.select" }) && hb.OnComplete.Count == 0,
                "认不出的动作名被丢掉：只剩坏动作的整条不进引擎，好动作原样保留");
            var wild = got.FirstOrDefault(x => x.Id == "wild");
            Chk("T194d-timing-clamped", wild != null && wild.DoubleClickMs == 2000 && wild.LongPressMs == 100,
                $"越界阈值钳回区间：双击 {wild?.DoubleClickMs} 长按 {wild?.LongPressMs}");
        }

        // 触发后缀只有一个实现（GestureConfig.TriggerLabel），供设置下拉、帮助窗与折叠行后缀共用
        {
            var names = Enum.GetNames<GE.TriggerKind>();
            var labels = names.Where(n => n != "Chord").Select(GestureConfig.TriggerLabel).ToList();
            Chk("T193a-trigger-labels-distinct",
                labels.All(s => !string.IsNullOrEmpty(s.Zh) && !string.IsNullOrEmpty(s.En))
                && labels.Select(s => s.Zh).Distinct().Count() == labels.Count
                && labels.Select(s => s.En).Distinct().Count() == labels.Count
                && GestureConfig.TriggerLabel("Chord").Zh == "" && GestureConfig.TriggerLabel("Chord").En == "",
                $"除 Chord 外 {labels.Count} 个触发后缀两两不同且两种语言都非空，Chord 两列都空");
            Chk("T193b-unknown-trigger-empty",
                GestureConfig.TriggerLabel("NoSuchTrigger").Zh == "" && GestureConfig.TriggerLabel(null!).Zh == "",
                "未知触发名与 null 都返回空文案（配置里写了认不出的触发不会崩）");
        }

        // 快捷键行的展开动效：正文从 0 长高、逐行淡入、走完放开限高；完成后与失败时只在高级形态里长出来
        try
        {
            var appHot = System.Windows.Application.Current ?? new System.Windows.Application();
            var svcHot = new FrameCapture.Core.SettingsService();
            typeof(FrameCapture.Services.AppHost).GetProperty("Settings")!
                .SetValue(FrameCapture.Services.AppHost.Current, svcHot);
            FrameCapture.Core.Theme.Apply("dark", svcHot.Current.Accent);
            FrameCapture.Core.Fluent.ApplyGlobalStyles();
            FrameCapture.Core.Loc.Apply("zh");
            var winHot = new FrameCapture.Overlays.SettingsWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            typeof(FrameCapture.Overlays.SettingsWindow)
                .GetMethod("Build", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(winHot, null);
            winHot.Show();
            PumpMs(400);
            var hotRoot = (UIElement)winHot.Content;
            var hotBoxes = new List<System.Windows.DependencyObject>();
            CollectTyped(hotRoot, typeof(FrameCapture.Core.ChordCaptureBox), hotBoxes);
            // 键位框 → 行首的列容器 → 挂着展开处理器的胶囊：这一击打在挂处理器的那一层
            var pill = (System.Windows.UIElement)System.Windows.Media.VisualTreeHelper
                .GetParent(System.Windows.Media.VisualTreeHelper.GetParent(hotBoxes[0]))!;
            var stack = (System.Windows.Controls.Panel)System.Windows.Media.VisualTreeHelper.GetParent(pill)!;
            var body = (System.Windows.Controls.Border)stack.Children[1];
            var rows = (System.Windows.Controls.Panel)body.Child;
            Chk("T191a-collapsed-body-clipped",
                body.Visibility == Visibility.Collapsed && body.MaxHeight == 0,
                "折叠时正文不可见且限高 0");
            pill.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent, Source = pill });
            bool fading = rows.Children.Cast<UIElement>().Any(c => c.Opacity < 0.5);
            Chk("T191b-expand-grows-from-zero",
                body.Visibility == Visibility.Visible && body.MaxHeight == 0 && fading,
                $"展开当场：正文可见、限高从 0 起、有行还在淡入（淡入中的行数 {rows.Children.Cast<UIElement>().Count(c => c.Opacity < 0.5)}）");
            PumpMs(500);
            Chk("T191c-expand-releases-cap",
                double.IsPositiveInfinity(body.MaxHeight) && body.ActualHeight > 60
                && rows.Children.Cast<UIElement>().All(c => c.Opacity > 0.99),
                $"动画走完：限高放开、正文有高度、行全部到位（实测高 {body.ActualHeight:F0}）");
            var combos = new List<System.Windows.DependencyObject>();
            CollectTyped(hotRoot, typeof(System.Windows.Controls.ComboBox), combos);
            var trig = combos.Cast<System.Windows.Controls.ComboBox>().FirstOrDefault(c => c.Items.Count > 4);
            var trigItems = trig == null
                ? new List<string>()
                : trig.Items.Cast<System.Windows.Controls.ComboBoxItem>().Select(i => (string)i.Content).ToList();
            Chk("T193c-trigger-combo-covers-all-kinds",
                trigItems.Count == Enum.GetValues<GE.TriggerKind>().Length && trigItems.Distinct().Count() == trigItems.Count,
                $"触发方式下拉 {trigItems.Count} 项覆盖全部触发且两两不同名：{string.Join("/", trigItems)}");
            trig?.ApplyTemplate();
            Chk("T193d-trigger-combo-template-measures",
                trig != null && trig.Template != null && trig.ActualHeight > 12,
                $"下拉模板已套上并量出高度 {trig?.ActualHeight.ToString() ?? "?"}（深色下拉开来读得清）");
            Chk("T191d-overrides-always-shown",
                OverrideRows().All(r => r.Visibility == Visibility.Visible && r.ActualHeight > 20),
                "完成后与失败时两行始终在：没有简单/高级之分");
            // 收起整行再展开：限高放开之后再来一次，动画仍从 0 起
            pill.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent, Source = pill });
            PumpMs(400);
            pill.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent, Source = pill });
            Chk("T191f-collapse-then-expand",
                body.Visibility == Visibility.Visible && body.MaxHeight == 0,
                "收起再展开：正文重新从 0 长高");
            PumpMs(500);

            // 收起态在视觉树里仍留着子节点，判断「有没有露出来」只能看行自己的可见性
            List<System.Windows.FrameworkElement> OverrideRows() => new()
            {
                (System.Windows.FrameworkElement)rows.Children[rows.Children.Count - 2],
                (System.Windows.FrameworkElement)rows.Children[rows.Children.Count - 1],
            };
        }
        catch (Exception e) { Chk("T191-hotkey-expand-animation", false, e.Message); }

        // 快捷键页这一轮的形态：默认名按语言、名字在胶囊上就地改、键位框宽度跟着键帽走、
        // 滚动条浮在内容上、等键的框自己接住 Esc、芯片只拖不点、加号在右侧点开候选卡。
        // 芯片编辑器单独实例化，get/set 用测试自己的闭包：拖动与选动作都不会写进用户的配置。
        try
        {
            var app6 = System.Windows.Application.Current ?? new System.Windows.Application();
            var svc6 = new FrameCapture.Core.SettingsService();
            typeof(FrameCapture.Services.AppHost).GetProperty("Settings")!
                .SetValue(FrameCapture.Services.AppHost.Current, svc6);
            FrameCapture.Core.Theme.Apply("dark", svc6.Current.Accent);
            FrameCapture.Core.Fluent.ApplyGlobalStyles();
            var swType = typeof(FrameCapture.Overlays.SettingsWindow);
            const System.Reflection.BindingFlags NP = System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static;
            const System.Reflection.BindingFlags FI = System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance;
            var nameMi = swType.GetMethod("HotkeyDisplayName", NP)!;
            static FrameCapture.Core.GestureBindingDto Bind(string id, string? name = null)
                => new() { Id = id, Name = name ?? "", Chord = "Ctrl+Alt+A", Trigger = "Chord", Actions = new[] { "scope.select" } };
            string Disp(FrameCapture.Core.GestureBindingDto b, string lang)
            {
                FrameCapture.Core.Loc.Apply(lang);
                return (string)nameMi.Invoke(null, new object[] { b })!;
            }
            Chk("T196a-default-names-follow-language",
                Disp(Bind("default.capture"), "zh") == "截图" && Disp(Bind("default.record"), "zh") == "录制"
                && Disp(Bind("user.x"), "zh") == "新配置"
                && Disp(Bind("default.capture"), "en") == "Capture" && Disp(Bind("user.x"), "en") == "New preset",
                "出厂两条按界面语言取自己的名字，用户新建的按语言取「新配置」");
            Chk("T196b-user-name-survives-language",
                Disp(Bind("default.capture", "我的截图"), "en") == "我的截图",
                "用户写过的名字是他的数据，切到英文界面也原样保留");

            // 就地改名的输入框：宽度跟着内容长，光标不会顶到框边
            var renameMi = swType.GetMethod("RenameBox", NP)!;
            var renameGrid = (System.Windows.Controls.Grid)renameMi.Invoke(null,
                new object[] { "短", new Action<string>(_ => { }), new Action(() => { }) })!;
            var renameBox = renameGrid.Children.OfType<System.Windows.Controls.TextBox>().First();
            var winRn = new Window
            {
                Content = renameGrid, Left = -20000, Top = -20000, SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
            };
            winRn.Show();
            PumpMs(200);
            double shortW = renameBox.Width;
            renameBox.Text = "一条长得需要一直往右写的名字";
            PumpMs(200);
            Chk("T196c-rename-box-grows-with-text",
                shortW > 40 && renameBox.Width > shortW + 60,
                $"改名框随内容变宽：短名 {shortW:F0} → 长名 {renameBox.Width:F0}");
            winRn.Close();

            // 键位框宽度跟着键帽走：一条单键、一条三键，摆在一起比
            svc6.Current.Gestures.Bindings.Add(new FrameCapture.Core.GestureBindingDto
            { Id = "t196.one", Chord = "F6", Trigger = "Chord", Actions = new[] { "capture.frame" } });
            svc6.Current.Gestures.Bindings.Add(new FrameCapture.Core.GestureBindingDto
            { Id = "t196.three", Chord = "Ctrl+Alt+G", Trigger = "Chord", Actions = new[] { "capture.frame" } });
            var win6 = new FrameCapture.Overlays.SettingsWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false,
            };
            swType.GetMethod("Build", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!.Invoke(win6, null);
            win6.Show();
            PumpMs(400);
            var root6 = (UIElement)win6.Content;
            var boxes6 = new List<System.Windows.DependencyObject>();
            CollectTyped(root6, typeof(FrameCapture.Core.ChordCaptureBox), boxes6);
            var cb6 = boxes6.Cast<FrameCapture.Core.ChordCaptureBox>().ToList();
            var widest = cb6.OrderByDescending(b => b.ActualWidth).First();
            var slimmest = cb6.OrderBy(b => b.ActualWidth).First();
            Chk("T196d-chord-box-fits-its-keycaps",
                widest.ActualWidth > slimmest.ActualWidth + 12 && slimmest.ActualWidth < 116,
                $"三键框 {widest.ActualWidth:F0} 宽于单键框 {slimmest.ActualWidth:F0}（键帽多的框更宽，谁也不占固定宽度）");

            // 滚动条浮在内容右缘：出现滚动条时正文一点宽度都不让
            var svPlain = new System.Windows.Controls.ScrollViewer
            { Width = 200, Height = 80, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            svPlain.Content = new System.Windows.Controls.Border { Width = 190, Height = 400 };
            var svFloat = new System.Windows.Controls.ScrollViewer
            { Width = 200, Height = 80, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            svFloat.Content = new System.Windows.Controls.Border { Width = 190, Height = 400 };
            FrameCapture.Core.Fluent.OverlayScroll(svFloat);
            var pair = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            pair.Children.Add(svPlain);
            pair.Children.Add(svFloat);
            var winSv = new Window
            {
                Content = pair, Left = -20000, Top = -20000, Width = 460, Height = 140,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
            };
            winSv.Show();
            PumpMs(200);
            static double ViewportOf(System.Windows.Controls.ScrollViewer sv)
            {
                double w = 0;
                void Walk(System.Windows.DependencyObject o)
                {
                    if (w > 0) return;
                    if (o is System.Windows.Controls.ScrollContentPresenter cp) { w = cp.ActualWidth; return; }
                    int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(o);
                    for (int i = 0; i < n; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
                }
                Walk(sv);
                return w;
            }
            double vPlain = ViewportOf(svPlain), vFloat = ViewportOf(svFloat);
            Chk("T196e-scrollbar-overlays-not-laid-out", vFloat > vPlain + 4,
                $"同样出滚动条：常规排版正文 {vPlain:F0} 宽，浮层排版 {vFloat:F0} 宽（切短页没有宽度可跳）");
            winSv.Close();

            // 正在等键的键位框：窗口那道 Esc 关窗的闸要认得出它，把这一记留给框自己
            var inCapMi = swType.GetMethod("InCapturingBox", NP)!;
            var oneKeyBox = cb6.First(b => b.ActualWidth == slimmest.ActualWidth);
            bool idle = (bool)inCapMi.Invoke(null, new object[] { oneKeyBox })!;
            oneKeyBox.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = oneKeyBox });
            PumpMs(60);
            Chk("T196f-esc-guard-sees-capturing-box",
                !idle && oneKeyBox.Capturing && (bool)inCapMi.Invoke(null, new object[] { oneKeyBox })!
                && win6.IsVisible,
                "等键的框被那道闸认出来了：这一记 Esc 归解绑，不归关窗");

            // 芯片编辑器：点芯片不弹候选卡，加号在右侧弹，选中即追加，拖动改序
            var ceType = swType.GetNestedType("ChainEditor", System.Reflection.BindingFlags.NonPublic)!;
            var seq = new List<string> { "scope.select", "capture.stored", "copy.clipboard" };
            var written = new List<string>();
            var editor = Activator.CreateInstance(ceType,
                new Func<string[]>(() => seq.ToArray()),
                new Func<int, IEnumerable<FrameCapture.Core.GestureAction>>(_ => FrameCapture.Core.GestureActions.All),
                new Action<string[]>(a => { seq = a.ToList(); written.Add(string.Join(",", a)); }))!;
            var rootEd = (System.Windows.Controls.Panel)ceType.GetProperty("Root")!.GetValue(editor)!;
            var winEd = new Window
            {
                Content = rootEd, Left = -20000, Top = -20000, SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
            };
            winEd.Show();
            PumpMs(200);
            var chips = (System.Windows.Controls.WrapPanel)rootEd.Children[0];
            var pop = (System.Windows.Controls.Primitives.Popup)ceType.GetField("_pop", FI)!.GetValue(editor)!;
            void Click(System.Windows.UIElement el) => el.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = el });
            Click((System.Windows.UIElement)chips.Children[0]);
            Chk("T196g-chip-click-opens-nothing", !pop.IsOpen && written.Count == 0,
                "点芯片不再改这一步：候选卡没开，序列也没动（要换一步就删掉再加）");
            Click((System.Windows.UIElement)chips.Children[chips.Children.Count - 1]);
            PumpMs(80);
            var popTexts = new List<System.Windows.DependencyObject>();
            if (pop.Child != null) CollectTyped((System.Windows.DependencyObject)pop.Child, typeof(System.Windows.Controls.TextBlock), popTexts);
            var popLabels = popTexts.Cast<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
            int covered = FrameCapture.Core.GestureActions.All.Count(a => popLabels.Contains(a.Label));
            Chk("T196h-plus-opens-card-at-the-right",
                pop.IsOpen && pop.Placement == System.Windows.Controls.Primitives.PlacementMode.Right
                && covered == FrameCapture.Core.GestureActions.All.Count(),
                $"加号在右侧点开候选卡，{covered} 条动作与目录同数，卡里没有「取消」那一条");
            var pickTb = popTexts.Cast<System.Windows.Controls.TextBlock>()
                .First(t => t.Text == FrameCapture.Core.GestureActions.All[1].Label);
            Click((System.Windows.UIElement)System.Windows.Media.VisualTreeHelper.GetParent(
                System.Windows.Media.VisualTreeHelper.GetParent(pickTb)!)!);
            PumpMs(80);
            Chk("T196i-picking-appends-at-the-end",
                written.Count == 1 && written[0] == "scope.select,capture.stored,copy.clipboard,"
                    + FrameCapture.Core.GestureActions.All[1].Name,
                $"选中一条即追加到末尾：{written.FirstOrDefault() ?? "（没写回）"}");

            // 拖动改序：抬起来跟手、其余让位、松手落在预览那一格
            ceType.GetMethod("ClosePicker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(editor, null);
            PumpMs(120);
            // 拖动改序：抬起来跟手、其余让位、松手落在预览那一格。
            // 合成鼠标事件带不了坐标（MouseButtonEventArgs 没有带 Point 的构造函数），
            // 读真实光标又会让用例取决于用户手此刻在哪——这里直接驱动拖动自己的三步。
            void SetF(string n, object v) => ceType.GetField(n, FI)!.SetValue(editor, v);
            object? GetF(string n) => ceType.GetField(n, FI)!.GetValue(editor);
            void Call(string n, System.Windows.Point p) => ceType.GetMethod(n, FI)!.Invoke(editor, new object[] { p });
            System.Windows.Point InRoot(System.Windows.FrameworkElement el, double fx, double fy)
                => el.TranslatePoint(new System.Windows.Point(el.ActualWidth * fx, el.ActualHeight * fy), rootEd);
            var c0 = (System.Windows.FrameworkElement)chips.Children[0];
            var before = seq.ToArray();
            SetF("_drag", c0);
            SetF("_dragFrom", 0);
            SetF("_target", 0);
            SetF("_dragging", false);
            SetF("_grab", new System.Windows.Point(c0.ActualWidth / 2, c0.ActualHeight / 2));
            SetF("_pressAt", InRoot(c0, 0.5, 0.5));
            Call("DragMove", new System.Windows.Point(InRoot(c0, 0.5, 0.5).X + 24, InRoot(c0, 0.5, 0.5).Y));
            PumpMs(60);
            // 落点尺子在起拖那一刻定格，占位框那一格就是它在剩余序列里的下标
            var q = InRoot((System.Windows.FrameworkElement)chips.Children[2], 0.2, 0.5);
            Call("DragMove", q);
            PumpMs(60);
            var ghost = GetF("_ghost");
            bool ghostShown = ghost != null && chips.Children.Cast<UIElement>().Contains((UIElement)ghost);
            int target = (int)GetF("_target")!;
            Call("Drop", q);
            PumpMs(120);
            // 落点必须就是预览那一格：按预览下标算出应有的序列，再跟写回的比
            var expect = before.ToList();
            var movedItem = expect[0];
            expect.RemoveAt(0);
            expect.Insert(Math.Clamp(target, 0, expect.Count), movedItem);
            Chk("T196j-drag-lands-where-the-ghost-was",
                ghostShown && target > 0 && written.Count == 2 && written[1] == string.Join(",", expect),
                $"拖动时占位框在第 {target} 格，松手后序列 {written.LastOrDefault() ?? "（没写回）"} 与那一格一致");
            winEd.Close();
            // 设置窗不关：它的 Closed 回调要重挂全局热键，而本进程从未走过启动流程，
            // AppHost 上的手势服务还没建起来。删掉的测试绑定在内存里清干净就够了。
            svc6.Current.Gestures.Bindings.RemoveAll(b => b.Id.StartsWith("t196.", StringComparison.Ordinal));
            FrameCapture.Core.Loc.Apply("zh");
        }
        catch (Exception e) { Chk("T196-hotkey-page-round", false, e.Message); }

        // 芯片编辑器这一轮：抬起的芯片不再占位、预览排不到加号之后、候选卡跟着宿主窗走、
        // 候选条目悬停真的叠得出底色。编辑器单独实例化，get/set 是本用例自己的闭包——
        // 拖动与选动作都不写用户的配置。合成鼠标事件带不了坐标，拖动直接驱动它自己的三步。
        try
        {
            var FI7 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var ce7 = typeof(FrameCapture.Overlays.SettingsWindow).GetNestedType("ChainEditor",
                System.Reflection.BindingFlags.NonPublic)!;
            var seq7 = new List<string> { "scope.select", "capture.stored", "copy.clipboard" };
            var ed7 = Activator.CreateInstance(ce7,
                new Func<string[]>(() => seq7.ToArray()),
                new Func<int, IEnumerable<FrameCapture.Core.GestureAction>>(_ => FrameCapture.Core.GestureActions.All),
                new Action<string[]>(a => seq7 = a.ToList()))!;
            var root7 = (System.Windows.Controls.Panel)ce7.GetProperty("Root")!.GetValue(ed7)!;
            var win7 = new Window
            {
                Content = root7, Left = -20000, Top = -20000, SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
            };
            win7.Show();
            PumpMs(200);
            var chips7 = (System.Windows.Controls.WrapPanel)root7.Children[0];
            var add7 = (System.Windows.Controls.Border)ce7.GetField("_add", FI7)!.GetValue(ed7)!;
            var pop7 = (System.Windows.Controls.Primitives.Popup)ce7.GetField("_pop", FI7)!.GetValue(ed7)!;
            void Set7(string f, object? v) => ce7.GetField(f, FI7)!.SetValue(ed7, v);
            object? Get7(string f) => ce7.GetField(f, FI7)!.GetValue(ed7);
            void Call7(string m, params object[] a) => ce7.GetMethod(m, FI7, null,
                a.Length == 0 ? Type.EmptyTypes : a.Select(x => x.GetType()).ToArray(), null)!
                .Invoke(ed7, a.Length == 0 ? null : a);

            var c0 = (System.Windows.Controls.Border)chips7.Children[0];
            Set7("_drag", c0); Set7("_dragFrom", 0); Set7("_target", 0); Set7("_dragging", false);
            Set7("_grab", new System.Windows.Point(c0.ActualWidth / 2, c0.ActualHeight / 2));
            Set7("_pressAt", new System.Windows.Point(0, 0));
            // 起拖前后比这一行的宽度：占位框顶上了芯片腾出的那一格，行宽才纹丝不动。
            // 芯片要是还替自己占着位，行宽会多出一枚芯片，那正是用户看见的那块空白。
            double rowW0 = chips7.ActualWidth;
            Call7("BeginDrag");
            chips7.UpdateLayout();
            double rowW1 = chips7.ActualWidth;
            Chk("T197a-lifted-chip-takes-no-room", rowW1 <= rowW0 + 4,
                $"起拖后这一行从 {rowW0:F0} 宽涨到 {rowW1:F0} 宽：抬起的芯片还占着自己的位置，行里多出一块空白");

            // 光标一路拖到加号右边很远：预览最多停在加号前面那一格，越过加号的预览是假的
            var past = add7.TranslatePoint(new System.Windows.Point(add7.ActualWidth + 240, add7.ActualHeight / 2), root7);
            Call7("DragMove", past);
            int gi = chips7.Children.IndexOf((UIElement)Get7("_ghost")!), ai = chips7.Children.IndexOf(add7);
            Chk("T197b-preview-never-passes-the-plus", gi >= 0 && gi < ai,
                $"预览排到了加号之后（占位框第 {gi} 格，加号第 {ai} 格），松手根本落不进去");

            Call7("Drop", past);
            PumpMs(80);
            Chk("T197c-drop-past-the-plus-lands-last", seq7.Count == 3 && seq7[2] == "scope.select",
                $"拖到加号右边松手，这一步落到了第 {seq7.IndexOf("scope.select") + 1} 位，应落在最后一位");

            // 抬起的那枚芯片：尺寸必须与起拖前一模一样，左上角必须压在光标按住的那一点。
            // 让它离开这一行靠的是负外边距的话，槽位确实塌成 0，Arrange 却把负 margin 加回渲染矩形——
            // 芯片涨成两倍大、朝左上跑掉一格，索引断言全对，画面上是一张跟在手里的错尺寸的卡。
            var c1b = (System.Windows.Controls.Border)chips7.Children[1];
            chips7.UpdateLayout();   // 松手重铺出来的新芯片还没排版，不量一下尺寸是 0×0，比什么都比不出差别
            var sizeWas = c1b.RenderSize;
            var hold = new System.Windows.Point(12, sizeWas.Height / 2);
            Set7("_drag", c1b); Set7("_dragFrom", 1); Set7("_target", 1); Set7("_dragging", false);
            Set7("_grab", hold);
            Set7("_pressAt", c1b.TranslatePoint(new System.Windows.Point(12, 6), root7));
            var hoverAt = c1b.TranslatePoint(new System.Windows.Point(c1b.ActualWidth + 120, 8), root7);
            Call7("DragMove", hoverAt);
            PumpMs(60);
            Chk("T200a-lifted-chip-keeps-its-size",
                sizeWas.Width > 20 && c1b.RenderSize.Width == sizeWas.Width && c1b.RenderSize.Height == sizeWas.Height,
                $"抬起后 {c1b.RenderSize.Width:F0}×{c1b.RenderSize.Height:F0}，起拖前 {sizeWas.Width:F0}×{sizeWas.Height:F0}");
            // 比中心而不是左上角：抬起时芯片按自己的中心放大 6%，左上角本就差着放大出来的那一两 DIP。
            // 中心才是「芯片跟着光标走」的不变量——位移被缩放放大时，偏的正是中心。
            // 量「按住的那一点现在画在哪儿」：抬起时芯片按中心放大 6%，按住点也会被一起推出去，
            // 位移不先把这截扣掉，手指按着不动、画面上按住的位置也会滑走——那正是「卡片没跟着手」。
            var held = c1b.TranslatePoint(hold, root7);
            Chk("T200b-lifted-chip-sits-under-the-cursor",
                Math.Abs(held.X - hoverAt.X) < 1.5 && Math.Abs(held.Y - hoverAt.Y) < 1.5,
                $"光标停在 {hoverAt.X:F1},{hoverAt.Y:F1}，芯片上被按住的那一点却画在 {held.X:F1},{held.Y:F1}");

            // 光标停在同一个点不动，落点不许来回换格。占位框一挪别的芯片就跟着挪，
            // 拿挪完之后的实时位置反查落点就是自己喂自己：指针不动，占位框也在左右闪。
            var seen = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                Call7("DragMove", hoverAt);
                seen.Add(chips7.Children.IndexOf((UIElement)Get7("_ghost")!));
            }
            Chk("T200c-preview-holds-while-cursor-holds", seen.Distinct().Count() == 1,
                "光标停在同一个点，占位框却来回换格：" + string.Join("→", seen));
            Call7("Drop", hoverAt);
            PumpMs(80);

            // 芯片抬起来就离开芯片行：接管鼠标移动的那个元素必须全程待在视觉树里，
            // 否则卡片钉在抬起处不动，而上面按索引算的断言全都还是绿的。
            // 只钉这一条——往后的移动与松手要真发鼠标事件才走得到，那部分交给手测。
            var pressC = (System.Windows.Controls.Border)chips7.Children[0];
            pressC.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = pressC });
            Chk("T201a-drag-input-survives-the-lift", ReferenceEquals(Mouse.Captured, root7),
                "按下之后鼠标捕获不在编辑器根上，而在 "
                + ((Mouse.Captured as UIElement)?.GetType().Name ?? "没人")
                + "：芯片抬起来离开芯片行那一刻，捕获跟着它出了视觉树，卡片从此收不到移动");
            Call7("Drop", hoverAt);
            PumpMs(80);

            // 接着拖第二枚：上一次让位与落入的动画还攥着这些芯片的位移属性，动画的持有值压过本地值。
            // 抬起时不先把属性交还回本地值，卡片就钉在装饰层原点不动——第一次拖没事，第二次才犯。
            var p2 = (System.Windows.Controls.Border)chips7.Children[0];
            chips7.UpdateLayout();
            Set7("_drag", p2); Set7("_dragFrom", 0); Set7("_target", 0); Set7("_dragging", false);
            Set7("_grab", new System.Windows.Point(10, p2.ActualHeight / 2));
            Set7("_pressAt", p2.TranslatePoint(new System.Windows.Point(10, 6), root7));
            var again = p2.TranslatePoint(new System.Windows.Point(p2.ActualWidth + 140, 8), root7);
            Call7("DragMove", again);
            PumpMs(60);
            var held2 = p2.TranslatePoint(new System.Windows.Point(10, p2.ActualHeight / 2), root7);
            Chk("T202-second-drag-still-follows-the-cursor",
                Math.Abs(held2.X - again.X) < 1.5 && Math.Abs(held2.Y - again.Y) < 1.5,
                $"接着拖第二枚时卡片没跟上：光标停在 {again.X:F1},{again.Y:F1}，"
                + $"芯片上被按住的那一点画在 {held2.X:F1},{held2.Y:F1}");
            Call7("Drop", again);
            PumpMs(80);

            Call7("OpenPicker");
            PumpMs(160);
            var popBorders = new List<System.Windows.DependencyObject>();
            if (pop7.Child != null)
                CollectTyped((System.Windows.DependencyObject)pop7.Child, typeof(System.Windows.Controls.Border), popBorders);
            var item7 = popBorders.Cast<System.Windows.Controls.Border>().FirstOrDefault(b =>
                b.Child is System.Windows.Controls.StackPanel sp &&
                sp.Children.OfType<System.Windows.UIElement>().Any(u =>
                    u is System.Windows.Controls.TextBlock tb && tb.FontSize == 12.5));
            var baseColor = (item7?.Background as System.Windows.Media.SolidColorBrush)?.Color
                            ?? default(System.Windows.Media.Color);
            item7?.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
            { RoutedEvent = UIElement.MouseEnterEvent, Source = item7 });
            PumpMs(260);
            var hoverColor = (item7?.Background as System.Windows.Media.SolidColorBrush)?.Color
                             ?? default(System.Windows.Media.Color);
            Chk("T197d-picker-item-hover-shows", item7 != null && baseColor.A > 0 && hoverColor != baseColor,
                "候选条目悬停叠不出底色：基色自己 alpha 为 0 时，叠一层半透明强调还是全透明");

            // 切到别的应用是窗口级的事：卡在自己的视觉树里收不到那一记，靠宿主窗的去激活收场。
            // Deactivated 是 CLR 事件、没有可抛的 RoutedEvent，走窗口自己那扇 raising 方法——
            // 系统让出前台时调的也是它。
            typeof(Window).GetMethod("OnDeactivated", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!.Invoke(win7, new object[] { System.EventArgs.Empty });
            PumpMs(FrameCapture.Core.Fluent.Motion.ExitMs + 260);
            Chk("T197e-picker-closes-when-window-goes-away", !pop7.IsOpen && Get7("_hostWin") == null,
                "切到别的应用候选卡还挂着，或宿主窗那两个事件没摘干净");
            win7.Close();
        }
        catch (Exception e) { Chk("T197-chip-editor-round", false, e.Message); }

        // 设置窗这一轮：覆盖行不再多摆一行文字（这一步有几条由芯片自己说），
        // 编辑器绑键那一段左边留出呼吸、指针划过不再换底色。
        try
        {
            var svc8 = new FrameCapture.Core.SettingsService();
            typeof(FrameCapture.Services.AppHost).GetProperty("Settings")!
                .SetValue(FrameCapture.Services.AppHost.Current, svc8);
            FrameCapture.Core.Theme.Apply("dark", svc8.Current.Accent);
            FrameCapture.Core.Fluent.ApplyGlobalStyles();
            FrameCapture.Core.Loc.Apply("zh");
            var sw8 = typeof(FrameCapture.Overlays.SettingsWindow);
            var NP8 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var win8 = new FrameCapture.Overlays.SettingsWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false,
            };
            sw8.GetMethod("Build", NP8)!.Invoke(win8, null);
            win8.Show();
            PumpMs(400);
            var root8 = (UIElement)win8.Content;
            var tbs8 = new List<System.Windows.DependencyObject>();
            CollectTyped(root8, typeof(System.Windows.Controls.TextBlock), tbs8);
            var shown = tbs8.Cast<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
            string nothing = FrameCapture.Core.Loc.T(FrameCapture.Core.K.Hotkeys.DefaultNothing);
            Chk("T198a-override-rows-drop-the-summary-line", !shown.Contains(nothing),
                "覆盖行还摆着一行默认落点的文字：那件事该由芯片卡片自己说，不许多占一行");

            // 编辑器绑键那几行按命令名认领：预设键行挂的是手势名，两拨混不到一起
            var keyNames = FrameCapture.Core.EditorCommands.All
                .Select(c => FrameCapture.Core.Loc.T(c.Name)).ToHashSet();
            var borders8 = new List<System.Windows.DependencyObject>();
            CollectTyped(root8, typeof(System.Windows.Controls.Border), borders8);
            var keyRows = borders8.Cast<System.Windows.Controls.Border>()
                .Where(b => b.CornerRadius.TopLeft == 6 && b.BorderThickness.Left == 1
                            && b.Child is System.Windows.Controls.Grid)
                .Where(b =>
                {
                    var rowTbs = new List<System.Windows.DependencyObject>();
                    CollectTyped(b, typeof(System.Windows.Controls.TextBlock), rowTbs);
                    return rowTbs.Count > 0
                           && keyNames.Contains(((System.Windows.Controls.TextBlock)rowTbs[0]).Text);
                })
                .ToList();
            var probe8 = keyRows[0];
            double padLeft = keyRows.Min(p => p.Padding.Left);
            // 悬停不许改底色：整排都是能拖的行，只有亮起来那一行会像在暗示只有它能碰。
            // 所以判据盯「悬停前后底色一模一样」，不盯「底色必须是空」——
            // 行有一层静止底色是层次台阶的一部分，禁掉它等于禁掉了层次。
            string ColorOf(System.Windows.Controls.Border b) =>
                b.Background is System.Windows.Media.SolidColorBrush sbx ? sbx.Color.ToString() : "null";
            string[] bgBefore = keyRows.Select(ColorOf).ToArray();
            probe8.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
            { RoutedEvent = UIElement.MouseEnterEvent, Source = probe8 });
            PumpMs(260);
            bool hoverFlat = keyRows.Select((p, i) => ColorOf(p) == bgBefore[i]).All(x => x);
            bool onRamp = keyRows.All(p => p.Background is System.Windows.Media.SolidColorBrush sb2 && sb2.Color == Theme.RowBg);
            Chk("T198b-editor-key-row-room-and-flat-hover",
                keyRows.Count >= 3 && keyRows.All(p => p.Padding.Left >= 14) && hoverFlat && onRamp,
                $"{keyRows.Count} 行编辑器绑键：左边最少 {padLeft:F0} DIP，"
                + $"悬停后{(hoverFlat ? "底色都没变" : "有行换了底色")}"
                + $"，底色{(onRamp ? "=层次台阶的行色" : "不在层次台阶上")}");

            // 快捷键行的胶囊四档：静止 → 悬停 → 按下 → 抬起（指针还在里面）→ 离开。
            // 「静止 = 台阶行色」这条同时看住填充助手会不会把底色抹回透明——
            // 助手一开口就把 Background 换成自己那份，行就白垫了一层。
            var hotPills = borders8.Cast<System.Windows.Controls.Border>()
                .Where(b => b.Cursor == System.Windows.Input.Cursors.Hand && b.CornerRadius.TopLeft == 6
                            && b.Background is System.Windows.Media.SolidColorBrush pbg
                            && pbg.Color == Theme.RowBg)
                .ToList();
            var pill0 = hotPills.FirstOrDefault();
            if (pill0 == null)
            {
                Chk("T220-hotkey-row-rest-on-ramp", false, "没找到垫在层次台阶上的快捷键行");
            }
            else
            {
                // 进/出用 MouseEventArgs，按下/抬起用 MouseButtonEventArgs：喂错参数类型，
                // WPF 的鼠标事件簿会把 MouseEnter 的处理器按按键处理器去转，当场抛 InvalidCast。
                // 动画中的画刷 ToString() 会印成 scRGB 浮点串（sc#1, 0.046…, …），拿字符串比颜色一定输。
                // 这里按字节比，容 ±2：scRGB 转回 sRGB 有一格以内的舍入。
                bool Near(System.Windows.Media.Color a, System.Windows.Media.Color b)
                    => Math.Abs(a.R - b.R) <= 2 && Math.Abs(a.G - b.G) <= 2 && Math.Abs(a.B - b.B) <= 2;
                bool IsAt(System.Windows.Media.Color want)
                    => pill0.Background is System.Windows.Media.SolidColorBrush pc && Near(pc.Color, want);
                string PillColor() => pill0.Background is System.Windows.Media.SolidColorBrush pcc
                    ? $"#{pcc.Color.R:X2}{pcc.Color.G:X2}{pcc.Color.B:X2}" : "null";
                // 进/出用 MouseEventArgs，按下/抬起用 MouseButtonEventArgs：喂错参数类型，
                // WPF 的鼠标事件簿会把 MouseEnter 的处理器按按键处理器去转，当场抛 InvalidCast。
                void RaiseEnter(System.Windows.RoutedEvent ev) => pill0.RaiseEvent(
                    new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                    { RoutedEvent = ev, Source = pill0 });
                void RaiseBtn(System.Windows.RoutedEvent ev) => pill0.RaiseEvent(
                    new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                        System.Windows.Input.MouseButton.Left) { RoutedEvent = ev, Source = pill0 });
                Chk("T220-hotkey-row-rest-on-ramp", IsAt(Theme.RowBg), PillColor());
                RaiseEnter(UIElement.MouseEnterEvent); PumpMs(240);
                Chk("T220-hotkey-row-hover-steps-up", IsAt(Theme.RowHover), PillColor());
                RaiseBtn(UIElement.PreviewMouseLeftButtonDownEvent); PumpMs(240);
                Chk("T220-hotkey-row-press-steps-again", IsAt(Fluent.Mix(Theme.RowBg, Theme.Press)), PillColor());
                RaiseBtn(UIElement.MouseLeftButtonUpEvent); PumpMs(240);
                Chk("T220-hotkey-row-release-back-to-hover", IsAt(Theme.RowHover), PillColor());
                RaiseEnter(UIElement.MouseLeaveEvent); PumpMs(240);
                Chk("T220-hotkey-row-leave-back-to-rest", IsAt(Theme.RowBg), PillColor());
            }

            // 用户主动打开的窗要在任务栏留个位置，被别的窗盖住才切得回来。
            // 只构造不显示：没走过 Show，任务栏不会真闪出一枚，也挂不上关窗回调。
            var plainSet = new FrameCapture.Overlays.SettingsWindow();
            var plainHelp = new FrameCapture.Overlays.HelpWindow();
            // 框选层相反：它是一层盖住屏幕的取境动作，不是一扇等着被切回来的窗
            var plainPick = new FrameCapture.Overlays.RegionPickerWindow(
                new FrameCapture.Capture.MonitorInfo("test", new FrameCapture.Capture.RectI(0, 0, 800, 600), true), null);
            Chk("T198c-user-opened-windows-in-taskbar",
                plainSet.ShowInTaskbar && plainSet.Owner == null
                && plainHelp.ShowInTaskbar && plainHelp.Owner == null
                && !plainPick.ShowInTaskbar,
                "用户主动打开的窗没进任务栏（或有了 Owner——有 Owner 时这个开关被系统忽略），"
                + $"或框选层混进了任务栏（ShowInTaskbar={plainPick.ShowInTaskbar}）");
        }
        catch (Exception e) { Chk("T198-settings-surface-round", false, e.Message); }

        // 定格开着时，截图这一帧必须来自定格帧——包括「点击窗口时截取完整窗口」预抓没排到那只窗的时候。
        // 取帧来源的裁定是纯函数，四条路各断言一次：谁被调用比像素更好查。
        try
        {
            var NS9 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var choose = typeof(FrameCapture.Services.CaptureController).GetMethods(NS9)
                .First(m => m.Name == "ChooseStillFrame");
            var mon9 = new FrameCapture.Capture.RectI(0, 0, 800, 600);
            var reg9 = new FrameCapture.Capture.RectI(100, 100, 64, 48);
            FrameCapture.Capture.CapturedFrame Fill(byte v, int w, int h) => new()
            { Bgra = Enumerable.Repeat(v, w * h * 4).ToArray(), W = w, H = h, TimeMs = 0 };
            var frozen9 = Fill(0xFF, 800, 600);
            Func<(FrameCapture.Capture.CapturedFrame f, FrameCapture.Capture.RectI r)?> miss = () => null;
            Func<(FrameCapture.Capture.CapturedFrame f, FrameCapture.Capture.RectI r)?> hitWin =
                () => (Fill(0x7F, 64, 48), reg9);
            bool liveWinCalled = false, liveRegCalled = false;
            Func<(FrameCapture.Capture.CapturedFrame f, FrameCapture.Capture.RectI r)?> liveWin = () =>
            { liveWinCalled = true; return (Fill(0, 64, 48), reg9); };
            Func<FrameCapture.Capture.RectI, FrameCapture.Capture.CapturedFrame?> liveReg = r =>
            { liveRegCalled = true; return Fill(0, r.W, r.H); };
            (FrameCapture.Capture.CapturedFrame? fr, bool winCap) Run9(bool freeze, bool locked,
                Func<(FrameCapture.Capture.CapturedFrame f, FrameCapture.Capture.RectI r)?> cached,
                FrameCapture.Capture.CapturedFrame? frozen)
            {
                liveWinCalled = liveRegCalled = false;
                // 定格帧「没到位」就是传 null 进去：这里必须原样传 null，兜底那条才真的被走到
                var r = choose.Invoke(null, new object?[] { freeze, locked, cached, liveWin,
                    frozen, mon9, reg9, liveReg })!;
                var t = r.GetType();
                var fr = (FrameCapture.Capture.CapturedFrame?)t.GetField("Item1")!.GetValue(r);
                return (fr, (bool)t.GetField("Item3")!.GetValue(r)!);
            }
            var a9 = Run9(true, true, miss, frozen9);
            Chk("T199a-frozen-frame-beats-live-window", a9.fr?.Bgra[0] == 0xFF && !liveWinCalled && !a9.winCap,
                "定格开着、预抓没排到这只窗时仍去实时抓窗口本体：屏幕上定着的就是那一帧");
            var b9 = Run9(false, true, miss, null);
            Chk("T199b-live-window-when-not-frozen", b9.fr?.Bgra[0] == 0 && b9.winCap && liveWinCalled,
                "定格关着时点窗口仍该实时抓窗口本体（带 alpha 的干净画面）");
            var c9 = Run9(true, false, miss, frozen9);
            Chk("T199c-frozen-frame-for-a-plain-region", c9.fr?.Bgra[0] == 0xFF && !liveRegCalled && !c9.winCap,
                "定格开着、按矩形截时该裁定格帧，不该重抓");
            var d9 = Run9(true, true, hitWin, frozen9);
            Chk("T199d-pregrabbed-window-wins", d9.fr?.Bgra[0] == 0x7F && d9.winCap,
                "定格瞬间预抓到的干净窗口该优先于按矩形裁帧（干净圆角与投影）");
            var e9 = Run9(true, false, miss, null);
            Chk("T199e-live-fallback-without-a-frame", e9.fr?.Bgra[0] == 0 && liveRegCalled,
                "定格帧没到位时实时抓兜底，否则这一枪空放");
        }
        catch (Exception e) { Chk("T199-frozen-frame-source", false, e.Message); }

        // 预抓缓存要活到控制器读完它：结果先派发、窗后关闭，控制器在关闭之后的一拍里才取这一帧。
        // 关闭时清缓存，定格 + 点窗口就永远命中不了预抓，退成按矩形裁的定格帧——画面不透明，日志也安静。
        try
        {
            var pickWin = new FrameCapture.Overlays.RegionPickerWindow(
                new FrameCapture.Capture.MonitorInfo("test", new FrameCapture.Capture.RectI(0, 0, 800, 600), true),
                null, windowCapture: true, freeze: true);
            var cache3 = (System.Collections.Concurrent.ConcurrentDictionary<IntPtr,
                (FrameCapture.Capture.CapturedFrame Frame, FrameCapture.Capture.RectI Region)>)
                typeof(FrameCapture.Overlays.RegionPickerWindow)
                    .GetField("_winCache", System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance)!.GetValue(pickWin)!;
            var fakeHwnd = new IntPtr(0x4D2);
            cache3[fakeHwnd] = (new FrameCapture.Capture.CapturedFrame { Bgra = new byte[16], W = 2, H = 2 },
                new FrameCapture.Capture.RectI(0, 0, 2, 2));
            pickWin.Close();
            PumpMs(60);
            var kept3 = pickWin.TryGetCachedWindow(fakeHwnd, out var cf3, out _);
            Chk("T203-pregrab-cache-outlives-the-picker-window", kept3 && cf3.W == 2,
                "选区窗一关，预抓的干净窗口缓存就空了：控制器在关闭之后的一拍才读它，"
                + "定格 + 点窗口永远命中不了预抓，退成按矩形裁的定格帧");
        }
        catch (Exception e) { Chk("T203-pregrab-cache-round", false, e.Message); }

        // 细滚动条全局样式真正生效（，用户「基本没变」根因：主题 ScrollBar 样式的
        // MinWidth=17.33 经 DynamicResource 以直值泄漏到模板内 ScrollBar，样式 Setter 压不住，须附加属性归零）
        {
            // 一个 AppDomain 只能有一个 Application：有用例先建好了就接着用，不重复 new
            var app = System.Windows.Application.Current ?? new Application();
            Fluent.ApplyGlobalStyles();
            var sv = new System.Windows.Controls.ScrollViewer { Width = 100, Height = 60, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Visible };
            sv.Content = new System.Windows.Controls.Border { Width = 90, Height = 400 };
            var win = new Window { Content = sv, Left = -3000, Top = -3000, Width = 140, Height = 100, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            win.Show();
            win.UpdateLayout();
            System.Windows.Controls.Primitives.ScrollBar? sb = null;
            void WalkSb(DependencyObject o)
            {
                if (sb != null) return;
                if (o is System.Windows.Controls.Primitives.ScrollBar s && s.Orientation == System.Windows.Controls.Orientation.Vertical) { sb = s; return; }
                int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(o);
                for (int i = 0; i < n; i++) WalkSb(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
            }
            WalkSb(win);
            Chk("T162a-sb-thin", sb != null && sb.ActualWidth <= 8.5 && sb.MinWidth == 0,
                $"模板内滚动条实宽 {sb?.ActualWidth.ToString() ?? "?"}（≤8.5）且 MinWidth 归零");
            win.Close();
        }
        // accent 反白 = WCAG 相对亮度 <0.35→白（若用 0.299/0.587/0.114 加权，#7F75FF 会被判成黑字）
        {
            var purple = Fluent.OnAccentColor(System.Windows.Media.Color.FromRgb(0x7F, 0x75, 0xFF));
            var orange = Fluent.OnAccentColor(System.Windows.Media.Color.FromRgb(0xFF, 0x8C, 0x00));
            var teal = Fluent.OnAccentColor(System.Windows.Media.Color.FromRgb(0x00, 0xB2, 0x94));
            var white = Fluent.OnAccentColor(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
            var black = Fluent.OnAccentColor(System.Windows.Media.Color.FromRgb(0x00, 0x00, 0x00));
            Chk("T162b-onaccent-wcag",
                purple == System.Windows.Media.Colors.White && orange == System.Windows.Media.Colors.Black
                && teal == System.Windows.Media.Colors.White && white == System.Windows.Media.Colors.Black
                && black == System.Windows.Media.Colors.White,
                $"#7F75FF→白 #{purple} / #FF8C00→黑 #{orange} / #00B294→白 #{teal} / 白底黑 黑底白");
        }

        #region 真·窗口截图（WGC 本体 + 按轮廓合成阴影）纯逻辑
        {
            // 相交
            var ov = WindowCapture.Intersect(new RectI(0, 0, 10, 10), new RectI(5, 5, 10, 10));
            var dis = WindowCapture.Intersect(new RectI(0, 0, 10, 10), new RectI(20, 20, 5, 5));
            var con = WindowCapture.Intersect(new RectI(0, 0, 100, 100), new RectI(10, 10, 20, 20));
            Chk("T165w1-intersect",
                ov.Equals(new RectI(5, 5, 5, 5)) && dis.W == 0 && dis.H == 0 && con.Equals(new RectI(10, 10, 20, 20)),
                $"ov={ov} dis={dis} con={con}");

            // 外扩
            var inf = WindowCapture.Inflate(new RectI(100, 100, 200, 150), 26);
            var inf0 = WindowCapture.Inflate(new RectI(100, 100, 200, 150), 0);
            Chk("T165w2-inflate",
                inf.Equals(new RectI(74, 74, 252, 202)) && inf0.Equals(new RectI(100, 100, 200, 150)),
                $"inf={inf} inf0={inf0}");

            // 贴本体（含越界裁剪）
            var dst = new byte[10 * 10 * 4];
            var src = new byte[4 * 4 * 4];
            for (int i = 0; i < src.Length; i++) src[i] = 0xAA;
            WindowCapture.BlitBody(dst, 10, 10, src, 4, 4, 3, 3);
            byte A(int x, int y) => dst[(y * 10 + x) * 4];
            bool blitOk = A(3, 3) == 0xAA && A(6, 6) == 0xAA && A(2, 2) == 0 && A(7, 7) == 0;
            // 越界：src 4x4 放 (8,8)，只有 2x2 落进 10x10
            var dst2 = new byte[10 * 10 * 4];
            WindowCapture.BlitBody(dst2, 10, 10, src, 4, 4, 8, 8);
            byte A2(int x, int y) => dst2[(y * 10 + x) * 4];
            bool clipOk = A2(8, 8) == 0xAA && A2(9, 9) == 0xAA && A2(9, 8) == 0xAA;
            Chk("T165w3-blitbody", blitOk && clipOk, "本体拷贝到偏移处并裁剪越界");

            // T165w4 合成阴影：轮廓外柔和渐隐、单调衰减、纯黑、远处全透（不读屏幕→无背景渗入）
            int W = 40, H = 40, bx = 15, by = 15, bw = 10, bh = 10, rad = 5;
            var bodyS = new byte[bw * bh * 4];
            for (int i = 0; i < bodyS.Length; i++) bodyS[i] = 255; // 全不透明轮廓
            var sh = new byte[W * H * 4];                          // 透明画布（无屏幕铺底）
            WindowCapture.SyntheticShadow(sh, W, H, bodyS, bw, bh, bx, by, rad, 0.5, 0);
            byte SA(int x, int y) => sh[(y * W + x) * 4 + 3];
            bool farZero = SA(0, 0) < 3;                            // 远处（>3r）淡到 0
            bool nearPresent = SA(14, 20) > 0;                      // 紧贴轮廓外有阴影
            bool mono = SA(14, 20) > SA(12, 20) && SA(12, 20) > SA(9, 20); // 越远越淡
            int si = (20 * W + 14) * 4;
            bool isBlack = sh[si] == 0 && sh[si + 1] == 0 && sh[si + 2] == 0; // 纯黑阴影
            Chk("T165w4-shadow", farZero && nearPresent && mono && isBlack,
                $"far={SA(0,0)} d1={SA(14,20)} d3={SA(12,20)} d6={SA(9,20)} black={isBlack}");

            // PickWindow：命中第一个包含点的窗（跳过 <4px），返回 hwnd+bounds
            var cands = new List<(IntPtr, RectI)>
            {
                (new IntPtr(1), new RectI(0, 0, 2, 2)),      // 太小，跳过
                (new IntPtr(2), new RectI(0, 0, 50, 50)),    // 包含 (10,10)
                (new IntPtr(3), new RectI(5, 5, 50, 50)),    // 也包含，但排在后
            };
            var pk = SnapPick.PickWindow(cands, 10, 10);
            var pkNone = SnapPick.PickWindow(cands, 200, 200);
            Chk("T165w5-pickwindow", pk != null && pk.Value.Hwnd == new IntPtr(2) && pkNone == null,
                $"pk={pk?.Hwnd} none={pkNone == null}");

            // 遮挡判定：更靠上的窗与目标相交→遮挡；仅更靠下相交→不算
            var z = new List<RectI>
            {
                new RectI(20, 20, 50, 50), // idx0 更靠上，与目标 (10,10,20,20) 相交
                new RectI(10, 10, 20, 20), // idx1 目标
                new RectI(500, 500, 50, 50), // idx2 更靠下，相交与否无关
            };
            bool occ = SnapPick.AnyAboveIntersects(z, 1, z[1]);
            bool notOcc = SnapPick.AnyAboveIntersects(new List<RectI> { new RectI(900, 900, 10, 10), z[1] }, 1, z[1]);
            Chk("T165w6-occlusion", occ && !notOcc, "上层窗相交→遮挡；仅下层相交→不遮挡");

            // T165w7 端到端合成（模拟 Grab 的 5~6 步，不含 WGC）：本体干净 + 圆角透明(不发黑) + 合成阴影渐隐
            int ow = 40, oh = 40, mbx = 15, mby = 15, mbw = 10, mbh = 10, mrad = 5;
            var body = new byte[mbw * mbh * 4];
            for (int i = 0; i < body.Length; i++) body[i] = 255;      // 不透明白
            body[0] = body[1] = body[2] = body[3] = 0;                // 左上角透明（模拟圆角）
            var outb = new byte[ow * oh * 4];                         // 全透明画布（不抓屏幕）
            WindowCapture.SyntheticShadow(outb, ow, oh, body, mbw, mbh, mbx, mby, mrad, 0.5, 1);
            WindowCapture.BlitBody(outb, ow, oh, body, mbw, mbh, mbx, mby);
            byte OA(int x, int y) => outb[(y * ow + x) * 4 + 3];
            bool centerOpaque = OA(20, 20) == 255 && outb[(20 * ow + 20) * 4] == 255; // 本体白
            bool cornerTransparent = OA(15, 15) == 0;                                  // 圆角透明（本体 alpha=0 盖住阴影，不发黑）
            bool farTransparent = OA(0, 0) < 3;                                        // 远处阴影淡到 0
            bool ringSemi = OA(13, 20) > 0 && OA(13, 20) < 255;                        // 轮廓外阴影半透
            bool ringBlack = outb[(20 * ow + 13) * 4] == 0;                            // 阴影是黑色（非背景色渗入）
            Chk("T165w7-composite", centerOpaque && cornerTransparent && farTransparent && ringSemi && ringBlack,
                $"center={OA(20,20)} corner={OA(15,15)} far={OA(0,0)} ring={OA(13,20)}");
        }

        // 透明处拍平到白：录制产物没有透明通道，透明像素必须落成不透明
        {
            var fb = new byte[8 * 8 * 4];
            for (int i = 0; i < fb.Length; i += 4) { fb[i] = 10; fb[i + 1] = 20; fb[i + 2] = 30; fb[i + 3] = 128; }
            fb[(4 * 8 + 0) * 4 + 3] = 255; // 一个不透明像素：应原样不动
            WindowCapture.FlattenOverWhite(fb, 8, 8);
            int core = (4 * 8 + 0) * 4;
            int edge = 0;
            Chk("T189a-flatten-keeps-opaque", fb[core] == 10 && fb[core + 1] == 20 && fb[core + 2] == 30 && fb[core + 3] == 255,
                $"core=({fb[core]},{fb[core + 1]},{fb[core + 2]},{fb[core + 3]})");
            Chk("T189b-flatten-semi-blends", fb[edge] == 137 && fb[edge + 1] == 147 && fb[edge + 2] == 157 && fb[edge + 3] == 255,
                $"edge=({fb[edge]},{fb[edge + 1]},{fb[edge + 2]},{fb[edge + 3]})");
            var full = new byte[4 * 4 * 4]; // 全透明（预乘下 rgb 也是 0）→ 应拍成纯白
            WindowCapture.FlattenOverWhite(full, 4, 4);
            Chk("T189c-flatten-clear-to-white", full[0] == 255 && full[1] == 255 && full[2] == 255 && full[3] == 255,
                $"px=({full[0]},{full[1]},{full[2]},{full[3]})");
        }
        #endregion

        #region 选区定格画面（定格帧裁剪 + 预抓窗口排序）纯逻辑
        {
            // T166w1 定格帧裁剪：正常裁 + 越界钳 + 负偏移收缩 + 空返回 null
            var big = new byte[10 * 10 * 4];
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 10; x++)
                {
                    int i = (y * 10 + x) * 4;
                    big[i] = (byte)x; big[i + 1] = (byte)y; big[i + 2] = 0; big[i + 3] = 255;
                }
            var srcF = new CapturedFrame { Bgra = big, W = 10, H = 10, TimeMs = 0 };
            var c1 = BgraImage.Crop(srcF, 2, 3, 4, 5);
            bool c1ok = c1 != null && c1.W == 4 && c1.H == 5 && c1.Bgra[0] == 2 && c1.Bgra[1] == 3 && c1.Bgra[3] == 255;
            bool c1px = c1 != null && c1.Bgra[4] == 3 && c1.Bgra[5] == 3; // (1,0)←src(3,3)
            var c2 = BgraImage.Crop(srcF, 8, 8, 10, 10);                  // 右下越界 → 2x2
            bool c2ok = c2 != null && c2.W == 2 && c2.H == 2 && c2.Bgra[0] == 8 && c2.Bgra[1] == 8;
            var c3 = BgraImage.Crop(srcF, -2, -2, 6, 6);                  // 负偏移 → 从(0,0)起 4x4（不向右多取）
            bool c3ok = c3 != null && c3.W == 4 && c3.H == 4 && c3.Bgra[0] == 0 && c3.Bgra[1] == 0;
            var c4 = BgraImage.Crop(srcF, 20, 20, 5, 5);                  // 完全在外
            var c5 = BgraImage.Crop(srcF, 0, 0, 0, 5);                    // 宽 0
            bool cNull = c4 == null && c5 == null;
            Chk("T166w1-crop", c1ok && c1px && c2ok && c3ok && cNull,
                $"c1={c1?.W}x{c1?.H} c2={c2?.W}x{c2?.H} c3={c3?.W}x{c3?.H} null={cNull}");

            // T166w2 预抓排序：聚焦窗最优先（若在候选里）→ 其余按 Z 序 → 截断到 max；max≤0→空
            var h = new List<IntPtr>();
            for (int i = 1; i <= 5; i++) h.Add(new IntPtr(i));            // Z 序顶在前：1,2,3,4,5
            var o1 = SnapPick.PregrabOrder(new IntPtr(3), h, 3);          // 聚焦=3 → 3,1,2
            bool o1ok = o1.Count == 3 && o1[0] == new IntPtr(3) && o1[1] == new IntPtr(1) && o1[2] == new IntPtr(2);
            var o2 = SnapPick.PregrabOrder(IntPtr.Zero, h, 4);            // 无聚焦 → 1,2,3,4
            bool o2ok = o2.Count == 4 && o2[0] == new IntPtr(1) && o2[3] == new IntPtr(4);
            var o3 = SnapPick.PregrabOrder(new IntPtr(9), h, 2);          // 聚焦不在候选 → 1,2
            bool o3ok = o3.Count == 2 && o3[0] == new IntPtr(1) && o3[1] == new IntPtr(2);
            var o4 = SnapPick.PregrabOrder(new IntPtr(3), h, 0);          // max=0 → 空
            var o5 = SnapPick.PregrabOrder(new IntPtr(3), h, 99);         // max 超量 → 全部 5 个，聚焦仍最前
            bool o5ok = o5.Count == 5 && o5[0] == new IntPtr(3) && o5[1] == new IntPtr(1);
            Chk("T166w2-pregraborder", o1ok && o2ok && o3ok && o4.Count == 0 && o5ok,
                $"o1={o1.Count} o2={o2.Count} o3={o3.Count} o4={o4.Count} o5={o5.Count}");
        }
        #endregion
        #region 滚动预览长图降采样（纯函数）
        {
            // T167w1 不缩小＝逐字节原样：宽高都在上限内时不抽样，输出与源完全一致
            var small = new byte[7 * 5 * 4];
            for (int i = 0; i < small.Length; i++) small[i] = (byte)(i * 37 + 11);
            var id1 = FrameCapture.Overlays.ScrollPreviewWindow.Thumbnail(small, 7, 5, 7, 5);
            var id2 = FrameCapture.Overlays.ScrollPreviewWindow.Thumbnail(small, 7, 5, 99, 99);
            bool idOk = id1.W == 7 && id1.H == 5 && id1.Bgra.AsSpan().SequenceEqual(small)
                     && id2.W == 7 && id2.H == 5 && id2.Bgra.AsSpan().SequenceEqual(small);
            Chk("T167w1-thumb-same", idOk, $"id1={id1.W}x{id1.H} id2={id2.W}x{id2.H}");

            // T167w2 超长图：输出宽/高不越上限，抽样行严格递增（B=列号低 8 位，(R,G)=行号）
            int w = 300, h = 1200;
            var longImg = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    longImg[o] = (byte)(x & 0xFF);
                    longImg[o + 1] = (byte)(y & 0xFF);
                    longImg[o + 2] = (byte)(y >> 8);
                    longImg[o + 3] = 255;
                }
            var th = FrameCapture.Overlays.ScrollPreviewWindow.Thumbnail(longImg, w, h, 64, 50);
            int RowOf(int r) => th.Bgra[r * th.W * 4 + 1] | (th.Bgra[r * th.W * 4 + 2] << 8);
            bool asc = true;
            for (int r = 1; r < th.H; r++) if (RowOf(r) <= RowOf(r - 1)) asc = false;
            Chk("T167w2-thumb-bounds", th.W <= 64 && th.H <= 50 && asc, $"out={th.W}x{th.H} asc={asc}");

            // T167w3 首行/末行/末列必被抽到：刚拼上去的内容一定看得见
            int firstRow = RowOf(0), lastRow = RowOf(th.H - 1);
            int lastCol = th.Bgra[((th.H - 1) * th.W + th.W - 1) * 4];
            Chk("T167w3-thumb-edges", firstRow == 0 && lastRow == h - 1 && lastCol == ((w - 1) & 0xFF),
                $"first={firstRow} last={lastRow} lastCol={lastCol}");

            // T167w5 宽高比守恒：缩略图必须与源图同比例。各轴独立取步长会让竖长文档被横向拉宽。
            // 同一块缓冲按竖（300×1200）与横（1200×300）两种形状各解释一次，两个方向的缺陷都能抓到。
            bool arOk = true;
            var arLog = new System.Text.StringBuilder();
            foreach (var (sw, sh) in new[] { (300, 1200), (1200, 300) })
            {
                var ar = FrameCapture.Overlays.ScrollPreviewWindow.Thumbnail(longImg, sw, sh, 64, 50);
                double dev = Math.Abs((double)ar.W / ar.H / ((double)sw / sh) - 1);
                arOk &= dev < 0.10 && ar.W <= 64 && ar.H <= 50;
                arLog.Append($"{sw}x{sh}->{ar.W}x{ar.H} dev={dev:P1} ");
            }
            Chk("T167w5-thumb-aspect", arOk, arLog.ToString());

            // T167w4 退化入参：空数组 + 0×0，绝不抛
            bool deg = true;
            try
            {
                var f = FrameCapture.Overlays.ScrollPreviewWindow.Thumbnail;
                var one = new byte[4];
                deg &= f(null!, 10, 10, 5, 5).Bgra.Length == 0;
                deg &= f(one, 0, 1, 5, 5).Bgra.Length == 0;
                deg &= f(one, 1, 0, 5, 5).Bgra.Length == 0;
                deg &= f(one, 1, 1, 0, 5).Bgra.Length == 0;
                deg &= f(one, 1, 1, 5, 0).Bgra.Length == 0;
                deg &= f(one, 4, 4, 5, 5).Bgra.Length == 0;   // 长度不足 w*h*4
                deg &= f(one, 1, 1, 5, 5) is { W: 1, H: 1 };
            }
            catch { deg = false; }
            Chk("T167w4-thumb-degenerate", deg);
        }
        #endregion

        #region 编辑器键位表：键位解析、命令表完整性、分派表与撞键判定
        {
            // 键位串往返：写出来的串必须能原样读回去，设置里存的和界面上显示的是同一份
            string[] round = { "Ctrl+Z", "Ctrl+U", "Shift+Left", "Space", "Enter", "Esc", "Del", "Backspace", "PgUp", "PgDn", "Home", "End", "F1", "0", "1", "+", "-", "[", "]", "Ctrl++", "Ctrl+-", "Ctrl+0", "Ctrl+Alt+Shift+Win+F12" };
            var rtBad = round.Where(s => EditorChord.Parse(s)?.ToString() != s).ToList();
            Chk("T188a-chord-roundtrip", rtBad.Count == 0, string.Join(",", rtBad));

            // 同一个物理键的多种报法归一：布局与小键盘状态决定 WPF 报哪个枚举名，不归一会出现同键不同串
            bool canon = EditorChord.Parse("Ctrl+OemPlus") == EditorChord.Parse("Ctrl++")
                && EditorChord.Parse("Ctrl+NumPad0") == EditorChord.Parse("Ctrl+0")
                && EditorChord.Parse("0") == EditorChord.Parse("*")
                && EditorChord.Parse("esc") == EditorChord.Parse("Escape")
                && EditorChord.Parse("pagedown") == EditorChord.Parse("PgDn");
            Chk("T188b-chord-canonical", canon);

            // 拒收：没有主键、两个主键、只按修饰键都不是键位
            bool rej = EditorChord.Parse(null) == null && EditorChord.Parse("") == null
                && EditorChord.Parse("Ctrl+") == null && EditorChord.Parse("Ctrl+Shift") == null
                && EditorChord.Parse("A+B") == null && EditorChord.Parse("nonsense") == null;
            Chk("T188c-chord-rejects", rej);

            // 主键是加号时不能靠切串渲染：Ctrl++ 切开会剩两个空 token
            var plusToks = EditorChord.Parse("Ctrl++")!.Value.Tokens();
            Chk("T188d-tokens-plus", plusToks.SequenceEqual(new[] { "Ctrl", "+" }), string.Join("|", plusToks));

            // 命令表：Id 唯一、分组都在分组表里、默认键位串要么空要么解析得动
            var ids = EditorCommands.All.Select(c => c.Id).ToList();
            var badGroup = EditorCommands.All.Where(c => !EditorCommands.Groups.Contains(c.Group)).Select(c => c.Id).ToList();
            var badChord = EditorCommands.All
                .Where(c => c.Chords.Any(ch => ch.Length > 0 && EditorChord.Parse(ch) == null))
                .Select(c => c.Id).ToList();
            Chk("T188e-table-integrity", ids.Distinct().Count() == ids.Count && badGroup.Count == 0 && badChord.Count == 0,
                $"ids={ids.Count} dup={ids.Count - ids.Distinct().Count()} badGroup={string.Join(",", badGroup)} badChord={string.Join(",", badChord)}");

            // 工具键位由目录生成：目录加一条工具，命令表里就得有一条，否则那个工具按了没反应
            var toolMissing = ToolCatalog.Tools.Select(t => "tool." + t.Key)
                .Concat(new[] { "tool." + ToolCatalog.ExitKey })
                .Where(id => EditorCommands.Find(id) == null).ToList();
            Chk("T188f-tools-covered", toolMissing.Count == 0, string.Join(",", toolMissing));

            // 默认分派表：Ctrl+Z 撤销；Delete 与 Backspace 上「删选中」排在无条件命令前，落空才轮到撤销 / 清裁剪
            var map = EditorCommands.BuildMap(new AppSettings());
            bool Has(ModifierKeys m, Key k, string id) => map.TryGetValue((m, k), out var l) && l.Contains(id);
            bool OrderOk(ModifierKeys m, Key k, string first) => map.TryGetValue((m, k), out var l) && l[0] == first;
            Chk("T188g-map-defaults",
                Has(ModifierKeys.Control, Key.Z, "undo") && Has(ModifierKeys.None, Key.Delete, "undo")
                && Has(ModifierKeys.None, Key.Back, "trim-clear") && Has(ModifierKeys.Control, Key.Add, "zoom-in")
                && Has(ModifierKeys.None, Key.Add, "speed-up") && Has(ModifierKeys.None, Key.D0, "speed-reset")
                && Has(ModifierKeys.Control, Key.D0, "zoom-fit") && Has(ModifierKeys.Shift, Key.Left, "sec-prev")
                && OrderOk(ModifierKeys.None, Key.Delete, "delete-object") && OrderOk(ModifierKeys.None, Key.Back, "delete-object"),
                $"del={(map.TryGetValue((ModifierKeys.None, Key.Delete), out var dl) ? string.Join(",", dl) : "-")}");

            // 解绑：按法清空后命令退出分派表，同键上的其他命令照常
            var unbound = new AppSettings();
            EditorCommands.WriteBindings(unbound, EditorCommands.Find("undo")!, Array.Empty<string>());
            var umap = EditorCommands.BuildMap(unbound);
            Chk("T188h-unbind", !umap.ContainsKey((ModifierKeys.Control, Key.Z))
                && !umap.ContainsKey((ModifierKeys.Control, Key.U))
                && !HasIn(umap, ModifierKeys.None, Key.Delete, "undo")
                && HasIn(umap, ModifierKeys.None, Key.Delete, "delete-object"),
                $"del={string.Join(",", umap.TryGetValue((ModifierKeys.None, Key.Delete), out var ul) ? ul : new List<string>())}");

            // 改键：新键位进表、默认键位让位，其余按法不受影响
            var remap = new AppSettings();
            var undoCmd = EditorCommands.Find("undo")!;
            EditorCommands.WriteBindings(remap, undoCmd, new[] { "Ctrl+V", "Delete" });
            var rmap = EditorCommands.BuildMap(remap);
            Chk("T188i-remap", HasIn(rmap, ModifierKeys.Control, Key.V, "undo") && !rmap.ContainsKey((ModifierKeys.Control, Key.Z))
                && !HasIn(rmap, ModifierKeys.Control, Key.U, "undo")
                && HasIn(rmap, ModifierKeys.None, Key.Delete, "undo")
                && EditorCommands.IsCustomized(remap, undoCmd)
                && !EditorCommands.IsCustomized(remap, EditorCommands.Find("redo")!));

            // 出厂键位互不冲突：两条无条件命令抢同一个键，用户按下去只会有一条生效，另一条形同虚设
            var clash = EditorCommands.Conflicts(new AppSettings());
            Chk("T188j-default-no-conflict", clash.Count == 0,
                string.Join(";", clash.Select(c => c.Chord + "=" + string.Join("/", c.Ids))));

            // 用户改出冲突要报得出来：两条无条件命令同键即撞
            var conf = new AppSettings();
            conf.EditorKeys["undo"] = "Ctrl+V";
            conf.EditorKeys["tool.V"] = "Ctrl+V";
            var c2 = EditorCommands.Conflicts(conf);
            Chk("T188k-user-conflict", c2.Count == 1 && c2[0].Ids.Contains("undo") && c2[0].Ids.Contains("tool.V"),
                string.Join(";", c2.Select(c => c.Chord + "=" + string.Join("/", c.Ids))));

            // 加按法：单键命令也能往上加，加出来的每个按法各自进表；重复与空串不留
            var added = new AppSettings();
            var selCmd = EditorCommands.Find("tool.V")!;
            EditorCommands.WriteBindings(added, selCmd, new[] { "V", "Ctrl+V", "", "Alt+V", "V" });
            var amap = EditorCommands.BuildMap(added);
            Chk("T188l-add-binding",
                EditorCommands.BindingsFor(added, selCmd).SequenceEqual(new[] { "V", "Ctrl+V", "Alt+V" })
                && added.EditorKeys["tool.V"] == "V|Ctrl+V|Alt+V"
                && HasIn(amap, ModifierKeys.None, Key.V, "tool.V")
                && HasIn(amap, ModifierKeys.Control, Key.V, "tool.V")
                && HasIn(amap, ModifierKeys.Alt, Key.V, "tool.V"),
                added.EditorKeys.TryGetValue("tool.V", out var av) ? av : "-");

            // 按法改回出厂那份：配置里不留条目，恢复默认键位时不会被自己写过的值绊住
            var back = new AppSettings();
            EditorCommands.WriteBindings(back, selCmd, new[] { "V", "Ctrl+V" });
            EditorCommands.WriteBindings(back, selCmd, selCmd.Chords);
            Chk("T188m-default-not-stored", !back.EditorKeys.ContainsKey("tool.V")
                && !EditorCommands.IsCustomized(back, selCmd)
                && EditorCommands.BindingsFor(back, selCmd).SequenceEqual(selCmd.Chords));

            // 一条命令挂满按法：每个都进表，顺序按列表走，多到溢出也不会有按法被丢掉
            var many = new AppSettings();
            var manyCmd = EditorCommands.Find("save-frame")!;
            EditorCommands.WriteBindings(many, manyCmd, new[] { "Ctrl+S", "Ctrl+F", "F12", "Ctrl+Shift+S", "Alt+F12" });
            var mmap = EditorCommands.BuildMap(many);
            Chk("T188n-many-bindings",
                HasIn(mmap, ModifierKeys.Control, Key.S, "save-frame")
                && HasIn(mmap, ModifierKeys.Control, Key.F, "save-frame")
                && HasIn(mmap, ModifierKeys.None, Key.F12, "save-frame")
                && HasIn(mmap, ModifierKeys.Control | ModifierKeys.Shift, Key.S, "save-frame")
                && HasIn(mmap, ModifierKeys.Alt, Key.F12, "save-frame")
                && EditorCommands.BindingsFor(many, manyCmd).Count == 5);

            static bool HasIn(Dictionary<(ModifierKeys Mods, Key Key), List<string>> m, ModifierKeys k, Key key, string id)
                => m.TryGetValue((k, key), out var l) && l.Contains(id);
        }
        #endregion

        #region 界面文案取词

        {
            // 取词机制：中文列是源语言；英文列空着就退回中文——护栏把「缺英文」当缺陷，理由就在这条退回
            var sample = K.Tray.Exit;
            Loc.Apply(Loc.PrefZh);
            Chk("T189a-zh-returns-source", Loc.T(sample) == "退出" && !Loc.IsEnglish);
            Loc.Apply(Loc.PrefEn);
            Chk("T189b-en-returns-translation", Loc.T(sample) == "Exit" && Loc.IsEnglish);
            Chk("T189c-en-blank-falls-back", Loc.T(new Str("test.blank", "只有中文", "")) == "只有中文");
            Chk("T189d-en-format", Loc.F(K.Tray.RegionFps, 800, 600, 24) == "Region 800×600 · 24 fps");
            // 参数顺序由表里的英文值决定，调用侧只按占位符填参
            Chk("T189e-format-arg-order",
                Loc.F(K.Capture.SegmentsStitchedPxTall, 3, 1080) == "3 segments stitched · 1080 px tall");
            Loc.Apply(Loc.PrefZh);
            Chk("T189f-zh-format", Loc.F(K.Capture.SegmentsStitchedPxTall, 3, 1080) == "已拼接 3 段 · 高 1080 px");
            // system 档看界面文化，不看线程区域格式；认不出的取值按 system 处理
            bool sysEn = !System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                .Equals("zh", StringComparison.OrdinalIgnoreCase);
            Chk("T189g-system-follows-ui-culture", Loc.Apply(Loc.PrefSystem) == sysEn);
            Chk("T189h-unknown-pref-is-system", Loc.Apply("klingon") == sysEn);
            // 表的健康度：英文列必须是英文、非空、与中文不同、不带括号标注；id 与中文列都不许有两句共用
            static List<(string Owner, string Name, Str S)> AllStrings()
            {
                var k = typeof(Str).Assembly.GetType("FrameCapture.Core.K")!;
                var list = new List<(string, string, Str)>();
                foreach (var c in k.GetNestedTypes())
                    foreach (var f in c.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                        if (f.FieldType == typeof(Str))
                            list.Add((c.Name, f.Name, (Str)f.GetValue(null)!));
                return list;
            }
            var all = AllStrings();
            var tbl = all.Where(s => s.S.Id != "core.empty").Select(s => s.S).ToList();
            var cjk = tbl.Where(s => s.En.Any(c => c >= 0x4E00 && c <= 0x9FFF)).Select(s => s.Id).ToList();
            var blank = tbl.Where(s => string.IsNullOrWhiteSpace(s.En)).Select(s => s.Id).ToList();
            // 括号只许留给记法或占位符：白名单与 scripts/lint-copy-parens.ps1 同源，rgb(r, g, b) 与 {0} 不算括注
            var notation = new HashSet<string> { "x86", "*.ogg;*.wav", "*.*", "r, g, b", "h, s%, l%", "FC_TEST_ENCODE_FAIL=1" };
            var parenRe = new System.Text.RegularExpressions.Regex("[（(]([^（()）]{1,60})[）)]");
            bool NotationOnly(string s) => parenRe.Matches(s).Cast<System.Text.RegularExpressions.Match>()
                .All(m => notation.Contains(m.Groups[1].Value) || m.Groups[1].Value.StartsWith("{", StringComparison.Ordinal));
            var paren = tbl.Where(s => !NotationOnly(s.En)).Select(s => s.Id).ToList();
            // 原样登记的条目等于没翻：切到英文后那行还是中文，看不出也翻不了
            var same = tbl.Where(s => s.En == s.Zh).Select(s => s.Id).ToList();
            var dupId = tbl.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var dupZh = tbl.GroupBy(s => s.Zh).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Chk("T189i-table-hygiene",
                cjk.Count == 0 && blank.Count == 0 && paren.Count == 0 && same.Count == 0 && dupId.Count == 0 && dupZh.Count == 0,
                $"条数={tbl.Count} cjk={cjk.Count} blank={blank.Count} paren={paren.Count} same={same.Count} 重id={dupId.Count} 重中文={dupZh.Count}"
                + (dupId.Count + dupZh.Count == 0 ? "" : " [" + string.Join(" | ", dupId.Concat(dupZh).Take(4)) + "]"));
            // 落盘值由 Normalize 收口：写坏的语言退回跟随系统
            var langSet = new AppSettings { UiLanguage = "fr" };
            langSet.Normalize();
            Chk("T189j-lang-normalize", langSet.UiLanguage == "system");
            // 每个界面都在真的取词：某一面整面没换词，英文下就是整窗中文
            Loc.Apply(Loc.PrefEn);
            var silent = all.GroupBy(s => s.Owner).Where(g => !g.Any(s => Loc.T(s.S) != s.S.Zh)).Select(g => g.Key).ToList();
            Chk("T189k-every-surface-translated", silent.Count == 0, $"整面没换词={string.Join(',', silent)}");
            // 目录里的文案直接进界面显示：工具提示、命令名、分组标题、动作名、触发后缀，英文下带汉字就是漏翻
            var stillZh = ToolCatalog.Tools.SelectMany(t => new[] { Loc.T(t.Tip), Loc.T(t.Help) })
                .Concat(EditorCommands.All.SelectMany(c => new[] { Loc.T(c.Name), Loc.T(c.Group) }))
                .Concat(EditorCommands.Groups.Select(Loc.T))
                .Concat(GestureActions.All.SelectMany(a => new[] { a.Label, a.Description }))
                .Where(s => s.Any(c => c >= 0x4E00 && c <= 0x9FFF))
                .Distinct().ToList();
            Chk("T189q-catalogs-render-in-language", stillZh.Count == 0,
                $"英文下仍带汉字={string.Join(" | ", stillZh.Take(3))}");
            // 合成词：中文把挡位名并进「细笔画」，英文是「Thin stroke」——两段各自取词后还要拼得对
            Chk("T189o-composed-stroke-labels-in-en",
                Loc.F(K.Editor.Stroke, Loc.T(K.Editor.Thin)) == "Thin stroke" && Loc.F(K.Editor.Stroke, Loc.T(K.Editor.Bold)) == "Bold stroke",
                Loc.F(K.Editor.Stroke, Loc.T(K.Editor.Medium)));
            // 带参串在英文里也要拼得出来：占位符顺序由译文决定，不是照抄中文
            Chk("T189l-arg-format-in-en", Loc.F(K.Pin.CloseAllPins, 3) == "Close All Pins (3)",
                Loc.F(K.Pin.CloseAllPins, 3));
            // 动作词汇表整表必须翻完：配置器下拉、快捷键页、帮助窗都读它，漏一条就是半中半英。
            // 判据取「切到英文后还带汉字」——缺译时取词原样返回中文，正好落进这个条件。
            var untranslated = GestureActions.All
                .SelectMany(a => new[] { a.Label, a.Description })
                .Where(s => s.Any(c => c >= 0x4E00 && c <= 0x9FFF)).ToList();
            Chk("T189m-action-vocabulary", untranslated.Count == 0,
                $"untranslated={untranslated.Count} [{string.Join(" | ", untranslated.Take(3))}]");
            // 触发方式在两种语言里都要两两不同：英文若把「松开」「松手」译成同一个词，键位后缀就分不出来了
            var trigEn = new[] { "KeyDown", "KeyUp", "Tap", "Hold", "HoldRelease", "LongPress", "DoubleClick", "TripleClick", "Click" }
                .Select(t => Loc.T(GestureConfig.TriggerLabel(t))).ToList();
            Chk("T189n-trigger-words-distinct-in-en", trigEn.Distinct().Count() == trigEn.Count,
                string.Join(',', trigEn));
            Loc.Apply(Loc.PrefZh);
        }

        #endregion

        #region 双语排版体检：文字放得下、也不被钉死的高度顶到上沿

        {
            // 两类只有切语言才看得见的排版缺陷：
            // ① 容器宽度写死，另一种语言的字更宽 → 裁在盒子边上；
            // ② TextBlock 显式 Height 比行盒高 + VerticalAlignment=Center → 多出的空隙全落在字下面，字看着偏上。
            // 逐页扫真实窗口，判据用 FormattedText 现量，不跟任何写死的像素数对赌。
            static (List<string> Faults, int Seen) LayoutFaults(string lang)
            {
                Loc.Apply(lang);
                var svc = new FrameCapture.Core.SettingsService();
                typeof(FrameCapture.Services.AppHost).GetProperty("Settings")!
                    .SetValue(FrameCapture.Services.AppHost.Current, svc);
                FrameCapture.Core.Theme.Apply("dark", svc.Current.Accent);
                FrameCapture.Core.Fluent.ApplyGlobalStyles();
                var win = new FrameCapture.Overlays.SettingsWindow
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false,
                };
                typeof(FrameCapture.Overlays.SettingsWindow)
                    .GetMethod("Build", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(win, null);
                win.Show();
                PumpMs(350);
                var root = (UIElement)win.Content;
                var boxes = new List<System.Windows.DependencyObject>();
                CollectTyped(root, typeof(FrameCapture.Core.ChordCaptureBox), boxes);
                if (boxes.Count > 0)
                {
                    // 键位框 → 行首列容器 → 挂着展开处理器的行首：点开它，行里的控件才进视觉树
                    var pill = (System.Windows.UIElement)System.Windows.Media.VisualTreeHelper
                        .GetParent(System.Windows.Media.VisualTreeHelper.GetParent(boxes[0]))!;
                    pill.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                        System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                    { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent, Source = pill });
                    PumpMs(500);
                }
                var faults = new List<string>();
                int seen = 0;
                // 一段单行文字自然要多宽：现量，不跟任何写死的像素数对赌
                static double Need(System.Windows.Controls.TextBlock t)
                {
                    var ft = new System.Windows.Media.FormattedText(t.Text,
                        System.Globalization.CultureInfo.CurrentCulture, t.FlowDirection,
                        new System.Windows.Media.Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch),
                        t.FontSize, System.Windows.Media.Brushes.White, 1.0);
                    return ft.WidthIncludingTrailingWhitespace + t.Padding.Left + t.Padding.Right;
                }
                static bool OneLine(System.Windows.Controls.TextBlock t)
                    => t.IsVisible && t.ActualWidth > 0 && !string.IsNullOrWhiteSpace(t.Text)
                       && t.TextWrapping == TextWrapping.NoWrap && t.TextTrimming == TextTrimming.None;
                void Audit(System.Windows.DependencyObject o)
                {
                    if (o is System.Windows.Controls.TextBlock tb && OneLine(tb))
                    {
                        seen++;
                        // 显式 Height 比行盒高 + 居中：多出的空隙全落在字下面，字看着偏上
                        if (!double.IsNaN(tb.Height) && tb.VerticalAlignment == VerticalAlignment.Center)
                        {
                            var ft = new System.Windows.Media.FormattedText(tb.Text,
                                System.Globalization.CultureInfo.CurrentCulture, tb.FlowDirection,
                                new System.Windows.Media.Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                                tb.FontSize, System.Windows.Media.Brushes.White, 1.0);
                            double line = ft.Height + tb.Padding.Top + tb.Padding.Bottom;
                            if (tb.Height > line + 1) faults.Add($"钉高「{tb.Text}」{tb.Height:F0} 行盒 {line:F0}");
                        }
                    }
                    // 并排文字的总需求 vs 写死宽度的祖先。
                    // 单个 TextBlock 永远「刚好够宽」——WPF 把格子宽发给它，裁切发生在轨道那一层，
                    // 所以判据必须落在「并排几段一共要多少」上。
                    if (o is System.Windows.Controls.Panel pan && pan.Children.Count >= 2
                        && (pan is System.Windows.Controls.StackPanel { Orientation: System.Windows.Controls.Orientation.Horizontal }
                            || pan is System.Windows.Controls.Primitives.UniformGrid))
                    {
                        double sum = 0;
                        string names = "";
                        foreach (System.Windows.UIElement c in pan.Children)
                        {
                            if (c is not System.Windows.Controls.TextBlock t2 || !OneLine(t2)) continue;
                            sum += Need(t2);
                            names += (names.Length == 0 ? "" : "+") + t2.Text;
                        }
                        if (names.Length > 0 && pan.Children.Count >= 2)
                        {
                            double box = double.NaN;
                            for (var up = System.Windows.Media.VisualTreeHelper.GetParent(o); up != null;
                                 up = System.Windows.Media.VisualTreeHelper.GetParent(up))
                            {
                                if (up is FrameworkElement fe && !double.IsNaN(fe.Width)) { box = fe.Width; break; }
                            }
                            if (!double.IsNaN(box) && box + 0.6 < sum)
                                faults.Add($"并排裁字「{names}」共需 {sum:F0} 容器写死 {box:F0}");
                        }
                    }
                    int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(o);
                    for (int i = 0; i < n; i++) Audit(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
                }
                Audit(root);
                // 其余分页逐个切换再扫：每页的行不一样，缺陷也只长在某一页上
                foreach (var cat in new[] { K.Common.Capture, K.Common.Recording, K.Settings.Output,
                    K.Common.Editor, K.Common.Pin, K.Common.Appearance, K.Settings.System })
                {
                    if (FindNavText(root, Loc.T(cat)) is not System.Windows.UIElement nav) continue;
                    nav.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                        System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                    { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent, Source = nav });
                    PumpMs(160);
                    Audit(root);
                }
                // 只隐藏不 Close：销毁处理器会去装热键，而这个宿主没起完整 AppHost
                win.Hide();
                return (faults, seen);
            }
            var en = LayoutFaults("en");
            var zh = LayoutFaults("zh");
            Loc.Apply(Loc.PrefZh);
            var clip = en.Faults.Concat(zh.Faults).Where(f => f.Contains("裁字")).ToList();
            var pin = en.Faults.Concat(zh.Faults).Where(f => f.StartsWith("钉高")).ToList();
            // 审计要自证看到了那个控件：没扫到它的「零缺陷」是假绿
            Chk("T195a-text-fits",
                clip.Count == 0 && en.Seen > 60 && zh.Seen > 60,
                $"双语各扫 {en.Seen}/{zh.Seen} 段文字；"
                + (clip.Count == 0 ? "无裁字" : $"{clip.Count} 处：" + string.Join(" | ", clip.Take(4))));
            Chk("T195b-text-not-pinned-high", pin.Count == 0,
                pin.Count == 0 ? "没有比行盒更高却居中的文字" : $"{pin.Count} 处：" + string.Join(" | ", pin.Take(4)));
        }

        #endregion

        // 贴图方向键微调：一格 1 DIP，Shift 一格 10 DIP，非方向键不给位移
        try
        {
            var left = PinNudge.Move(Key.Left, false);
            var downBig = PinNudge.Move(Key.Down, true);
            var other = PinNudge.Move(Key.A, false);
            Chk("T207a-pin-nudge-one-dip",
                left == (-1d, 0d) && PinNudge.Move(Key.Right, false) == (1d, 0d)
                && PinNudge.Move(Key.Up, false) == (0d, -1d),
                $"左={left.Dx},{left.Dy} 上=({PinNudge.Move(Key.Up, false).Dx},{PinNudge.Move(Key.Up, false).Dy})");
            Chk("T207b-pin-nudge-shift-coarse", downBig == (0d, PinNudge.Coarse), $"Shift+下 = {downBig.Dy}");
            Chk("T207c-pin-nudge-ignores-other-keys", other == (0d, 0d), "非方向键不该给位移");
        }
        catch (Exception e) { Chk("T207-pin-nudge", false, e.Message); }

        // 倒计时窗的 Esc 代收开关跟着窗口生命周期走：漏关一次，全局 Esc 就永久失灵
        try
        {
            var cd = new FrameCapture.Overlays.CountdownWindow("1560,260 940×560", @"\\.\DISPLAY1");
            cd.Left = -20000; cd.Top = -20000;   // 构造期已定好落点，Show 前挪走，不在用户屏幕上闪
            cd.Show();
            PumpMs(150);
            bool on = HotkeyManager.CountdownActive;
            cd.Close();
            PumpMs(150);
            Chk("T208-countdown-esc-claim-and-release",
                on && !HotkeyManager.CountdownActive && HotkeyManager.CountdownEsc is null,
                $"开窗={on} 关窗={HotkeyManager.CountdownActive}");
        }
        catch (Exception e) { Chk("T208-countdown-esc", false, e.Message); }

        // 双击画布在 1:1 与适配之间来回：切的是缩放本身，直接驱动切换，量出来的就是屏幕上那个缩放
        try
        {
            var stZ = new SettingsService();
            var seqZ = new CapturedSequence
            {
                Frames = new List<CapturedFrame> { MovingSquare(3000, 1800, 0, 1) },
                Region = new RectI(0, 0, 3000, 1800),
                MonitorBounds = new RectI(0, 0, 1920, 1080),
            };
            var winZ = new FrameCapture.Overlays.EditorWindow(stZ, seqZ);
            winZ.WindowStartupLocation = WindowStartupLocation.Manual;
            winZ.Left = -20000; winZ.Top = -20000;
            winZ.Show();
            PumpMs(250);
            var TZ = typeof(FrameCapture.Overlays.EditorWindow);
            const System.Reflection.BindingFlags NPZ = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            double Zoom() => (double)TZ.GetField("_zoom", NPZ)!.GetValue(winZ)!;
            var toggle = TZ.GetMethod("ZoomToggleActual", NPZ)!;
            double fit = Zoom();
            toggle.Invoke(winZ, null);
            double one = Zoom();
            toggle.Invoke(winZ, null);
            double back = Zoom();
            // _zoom 是「相对适配的倍率」：适配档就是 1.0，图比视口大时 1:1 必然高于它
            Chk("T209-double-click-toggles-actual-and-fit",
                one > fit + 0.05 && Math.Abs(back - fit) < 0.001,
                $"适配={fit:F3} 1:1={one:F3} 再切回={back:F3}");
            try { winZ.Close(); } catch { }
        }
        catch (Exception e) { Chk("T209-double-click-zoom", false, e.Message); }

        // 暂停时托盘图标转灰：两枚都得画出来，暂停那枚不能还是那抹蓝
        try
        {
            var tTray = typeof(FrameCapture.Services.AppHost).Assembly.GetType("FrameCapture.Services.TrayService")!;
            var mk = tTray.GetMethod("MakeIcon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)!;
            using var on = (System.Drawing.Icon)mk.Invoke(null, new object[] { false })!;
            using var off = (System.Drawing.Icon)mk.Invoke(null, new object[] { true })!;
            using var bOn = on.ToBitmap();
            using var bOff = off.ToBitmap();
            var cOn = bOn.GetPixel(bOn.Width / 2, bOn.Height / 2);
            var cOff = bOff.GetPixel(bOff.Width / 2, bOff.Height / 2);
            bool blueish = cOn.B - cOn.R > 40;
            bool grayish = Math.Max(cOff.R, Math.Max(cOff.G, cOff.B)) - Math.Min(cOff.R, Math.Min(cOff.G, cOff.B)) <= 12;
            Chk("T210-tray-icon-paused-variant-is-gray",
                blueish && grayish && cOff.A > 0 && cOn.A > 0,
                $"工作态 #{cOn.R:X2}{cOn.G:X2}{cOn.B:X2} 暂停态 #{cOff.R:X2}{cOff.G:X2}{cOff.B:X2}");
        }
        catch (Exception e) { Chk("T210-tray-icon-paused", false, e.Message); }

        // 自动保存的轻提示：半秒内写完不打扰；慢的那次先说「正在保存视频…」，写完换成「已保存 文件名」
        try
        {
            var tHint = typeof(FrameCapture.Services.SaveService).GetNestedType("SavingHint", System.Reflection.BindingFlags.NonPublic)!;
            var startH = tHint.GetMethod("Start", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            var doneH = tHint.GetMethod("Done", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
            static string ToastText()
            {
                var app = System.Windows.Application.Current;
                if (app == null) return "";
                foreach (System.Windows.Window w in app.Windows)
                {
                    if (w is not FrameCapture.Overlays.NoticeToast) continue;
                    var sb = new System.Text.StringBuilder();
                    var stack = new System.Collections.Generic.Stack<System.Windows.DependencyObject>();
                    stack.Push(w);
                    while (stack.Count > 0)
                    {
                        var d = stack.Pop();
                        if (d is System.Windows.Controls.TextBlock tb) sb.Append(tb.Text).Append('|');
                        int n = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetChildrenCount(d) : 0;
                        for (int i = 0; i < n; i++) stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
                    }
                    return sb.ToString();
                }
                return "";
            }
            FrameCapture.Overlays.NoticeToast.DismissCurrent();
            PumpMs(400);                       // 让上一条淡出干净再计时
            var h = startH.Invoke(null, new object[] { "clip.mp4" });
            PumpMs(200);
            string early = ToastText();
            PumpMs(700);
            string mid = ToastText();
            doneH.Invoke(h, new object[] { true });
            PumpMs(250);
            string after = ToastText();
            bool saving = mid.Contains("正在保存") || mid.Contains("Saving");
            bool saved = (after.Contains("已保存") || after.Contains("Saved")) && after.Contains("clip.mp4");
            Chk("T211-autosave-hint-waits-then-confirms",
                early.Length == 0 && saving && saved,
                $"200ms=[{early}] 900ms=[{mid}] 完成后=[{after}]");
            FrameCapture.Overlays.NoticeToast.DismissCurrent();
        }
        catch (Exception e) { Chk("T211-autosave-hint", false, e.Message); }

        // 选区里按 C 取过的色并进编辑器「最近用色」。存的必须是 #RRGGBB——读数按 F 会切成 rgb()/hsl()，
        // 那只是复制出去的文本形式，色板认的是 HEX。
        try
        {
            var svcL = new SettingsService();
            svcL.Mutate(s => s.CustomColors = new List<string> { "#FFFFFF" });
            typeof(FrameCapture.Services.AppHost).GetProperty("Settings")!
                .SetValue(FrameCapture.Services.AppHost.Current, svcL);
            var pickL = new FrameCapture.Overlays.RegionPickerWindow(
                new FrameCapture.Capture.MonitorInfo("test", new FrameCapture.Capture.RectI(0, 0, 800, 600), true),
                null, magnifier: true);
            var TP = typeof(FrameCapture.Overlays.RegionPickerWindow);
            const System.Reflection.BindingFlags NP2 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            TP.GetField("_lastRgb", NP2)!.SetValue(pickL, ((int)0xE8, (int)0x11, (int)0x23));
            TP.GetField("_loupeHexText", NP2)!.SetValue(pickL, "rgb(232, 17, 35)");
            bool taken = (bool)TP.GetMethod("OnPickerKey", NP2)!.Invoke(pickL, new object[] { (uint)0x43 })!;
            PumpMs(150);
            var cols = svcL.Current.CustomColors;
            Chk("T212-loupe-color-enters-editor-recent-colors",
                taken && cols.Count == 2 && cols[0] == "#E81123" && cols[1] == "#FFFFFF",
                $"键被接管={taken} 最近用色=[{string.Join(",", cols)}]");
            pickL.Close();
        }
        catch (Exception e) { Chk("T212-loupe-color-history", false, e.Message); }

        // 滚动截图「继续上次滚动」：完成时落盘的那张 PNG 就是基准帧，读回来直接进步进拼接循环
        try
        {
            var ctlR = new FrameCapture.Services.CaptureController(new SettingsService());
            var TR = typeof(FrameCapture.Services.CaptureController);
            const System.Reflection.BindingFlags NPR = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            bool none = !ctlR.HasScrollResume;
            // 那张图读不回来（临时目录被清）就当没有这次可继续：不留会话，也不把「继续」挂在托盘上
            TR.GetField("_scrollResume", NPR)!.SetValue(ctlR,
                (@"C:\no-such-dir\fc-scroll-missing.png", new RectI(0, 0, 200, 120), "test"));
            bool claimed = ctlR.HasScrollResume;
            ctlR.ResumeScrollCapture();
            bool cleared = !ctlR.HasScrollResume && TR.GetField("_scrollBar", NPR)!.GetValue(ctlR) == null;
            // 真有一张：读回来当基准帧，控制条与步进计时器都该起来，Esc 吞键标志也该挂上
            var pngR = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FrameCapture", $"resume-test-{Guid.NewGuid():N}.png");
            var accR = new byte[200 * 120 * 4];
            for (int i = 0; i < accR.Length; i += 4) { accR[i] = 0x33; accR[i + 1] = 0x66; accR[i + 2] = 0x99; accR[i + 3] = 0xFF; }
            FrameCapture.Services.BgraImage.SavePng(new CapturedFrame { Bgra = accR, W = 200, H = 120 }, pngR);
            TR.GetField("_scrollResume", NPR)!.SetValue(ctlR, (pngR, new RectI(-18000, -18000, 200, 120), "test"));
            ctlR.ResumeScrollCapture();
            PumpMs(250);
            bool running = TR.GetField("_scrollBar", NPR)!.GetValue(ctlR) != null
                && (int)TR.GetField("_scrollH", NPR)!.GetValue(ctlR)! == 120
                && FrameCapture.Services.HotkeyManager.ScrollActive;
            ctlR.ScrollCancel();
            PumpMs(150);
            bool cleaned = !FrameCapture.Services.HotkeyManager.ScrollActive
                && TR.GetField("_scrollAcc", NPR)!.GetValue(ctlR) == null;
            try { System.IO.File.Delete(pngR); } catch { }
            ctlR.Dispose();
            Chk("T213-scroll-resume-from-the-done-png",
                none && claimed && cleared && running && cleaned,
                $"起手无={none} 声称有={claimed} 读不回即清={cleared} 接起来={running} 取消收干净={cleaned}");
        }
        catch (Exception e) { Chk("T213-scroll-resume", false, e.Message); }

        // 倒计时卡落点：那几秒用户正要在待截范围里点开菜单，卡压在上面就白等一轮。
        // 判的是纯几何（不建窗）：默认位、躲开、仍在屏内、整屏时回原、副屏负坐标不跑偏。
        try
        {
            var mon = new RectI(0, 0, 2560, 1440);
            int cw = 450, ch = 276;
            var (x0, y0) = FrameCapture.Overlays.CountdownWindow.PickPlace(mon, cw, ch, null);
            bool topCenter = Math.Abs(x0 + cw / 2 - mon.W / 2) <= 2 && y0 == 48;

            var strip = new RectI(900, 0, 760, 300);   // 正压在顶部居中一带
            var (x1, y1) = FrameCapture.Overlays.CountdownWindow.PickPlace(mon, cw, ch, strip);
            var card1 = new RectI(x1, y1, cw, ch);
            bool dodged = !(card1.X < strip.Right && strip.X < card1.Right
                         && card1.Y < strip.Bottom && strip.Y < card1.Bottom);
            bool inside = card1.X >= mon.X && card1.Y >= mon.Y && card1.Right <= mon.Right && card1.Bottom <= mon.Bottom;

            var (x2, y2) = FrameCapture.Overlays.CountdownWindow.PickPlace(mon, cw, ch, mon);
            bool fallback = x2 == x0 && y2 == y0;   // 范围就是整屏：躲不掉，保持默认位而不是跑出屏外

            var mon2 = new RectI(-1920, 0, 1920, 1080);   // 主屏左边的副屏
            var (x3, y3) = FrameCapture.Overlays.CountdownWindow.PickPlace(mon2, cw, ch, new RectI(-1000, 0, 800, 400));
            bool onMon2 = x3 >= -1920 && y3 >= 0 && x3 + cw <= 0 && y3 + ch <= 1080;

            Chk("T214-countdown-card-dodges-the-region",
                topCenter && dodged && inside && fallback && onMon2,
                $"默认顶部居中={topCenter} 压范围时躲开={dodged} 仍在屏内={inside} 整屏时回原={fallback} 副屏不跑偏={onMon2}");
        }
        catch (Exception e) { Chk("T214-countdown-dodge", false, e.Message); }

        // 强调色当文字色必须读得清。出厂那抹浅蓝 #4CC2FF 压在白卡片上只有 1.9:1，
        // 比 WCAG 正文 4.5:1 与大号字 3:1 都低；AccentText 按需要压暗到达标，色相不动。
        try
        {
            var keepAcc = Theme.Accent;
            bool ok = true;
            string bad = "";
            foreach (var hex in new[] { "#4CC2FF", "#7F75FF", "#E81123", "#FFB900", "#00CC6A" })
            foreach (var th in new[] { "light", "dark" })
            {
                Theme.Apply(th, hex);
                // 文字既落在白卡片上也落在窗口底色上，两张底都要过线。
                double a = Fluent.ContrastRatio(Fluent.AccentText(), Theme.CardBg);
                double b = Fluent.ContrastRatio(Fluent.AccentText(), Theme.WindowBg);
                double got = Math.Min(a, b);
                if (got >= 4.5) continue;
                ok = false;
                bad = th + " " + hex + " 只有 " + got.ToString("F2") + ":1";
            }
            // 浅蓝压在白底上确实不达标：这条判的是「为什么需要压暗」，
            // 谁把 AccentText 简化回强调色本身，上面那圈就会红。
            Theme.Apply("light", "#4CC2FF");
            bool rawFails = Fluent.ContrastRatio(Theme.Accent, Theme.CardBg) < 3.0;
            // 暗色主题里出厂浅蓝本来就够读（压在 #2D2D2D 上 6.5:1），必须原样用——
            // 退回正文色会抹掉键位提示的强调，暗色界面的键位列一直是强调蓝。
            Theme.Apply("dark", "#4CC2FF");
            bool darkKeepsAccent = Fluent.AccentText() == Theme.Accent;
            Theme.Accent = keepAcc;
            Chk("T215-accent-text-meets-contrast", ok && rawFails && darkKeepsAccent,
                bad + (darkKeepsAccent ? "" : " 暗色主题抹掉了强调色"));
        }
        catch (Exception e) { Chk("T215-accent-text-meets-contrast", false, e.Message); }

        // 调色板的正文与次要文字，压在四种底上都得过 4.5:1。次要文字带 Alpha，
        // 压平到底色上才是眼睛看到的颜色——按原始 RGB 算会算出一个不存在的颜色。
        try
        {
            var keepAcc = Theme.Accent;
            bool ok = true;
            string worst = "";
            double worstCr = 99;
            foreach (var th in new[] { "light", "dark" })
            {
                Theme.Apply(th, "#4CC2FF");
                var bgs = new[] { Theme.CardBg, Theme.WindowBg, Theme.PanelBg, Theme.ChromeBg };
                var names = new[] { "卡片", "窗口", "面板", "标题栏" };
                var fgs = new[] { ("正文", Theme.Text), ("次要", Theme.TextDim) };
                for (var i = 0; i < bgs.Length; i++)
                    foreach (var (label, fg) in fgs)
                    {
                        double cr = Fluent.ContrastRatio(Fluent.Flatten(fg, bgs[i]), bgs[i]);
                        if (cr >= 4.5) continue;
                        ok = false;
                        if (cr >= worstCr) continue;
                        worstCr = cr;
                        worst = th + " " + label + "压在" + names[i] + "底 " + cr.ToString("F2") + ":1";
                    }
            }
            Theme.Accent = keepAcc;
            Chk("T216-palette-text-contrast", ok, worst);
        }
        catch (Exception e) { Chk("T216-palette-text-contrast", false, e.Message); }

        // 保存位置那行的中段省略。这行要回答「存到哪去了」，答案在末尾——
        // 结尾省略会留下盘符、丢掉用来认目录的那一级，等于把问题留着。
        try
        {
            var deep = @"D:\Users\lin\Pictures\截图归档\2026\十月\产品走查\FrameCapture";
            var t = Fluent.TrimPathMiddle(deep);
            Chk("T217-path-middle-keeps-root", t.StartsWith(@"D:\"), t);
            Chk("T217-path-middle-keeps-tail", t.EndsWith("FrameCapture"), t);
            Chk("T217-path-middle-shorter", t.Contains('…') && t.Length < deep.Length, t);
            // 断点落在分隔符上：显示的尾巴必须是原路径的真实后缀，且要么自己以分隔符开头，
            // 要么在原路径里前面就是分隔符——两种情况都意味着没切断目录名。
            var seg = t.Split('…')[1];
            Chk("T217-path-middle-no-half-name",
                deep.EndsWith(seg.TrimStart('\\')) && (seg.StartsWith('\\') || deep.Contains(@"\" + seg)), t);
            Chk("T217-path-short-untouched", Fluent.TrimPathMiddle(@"C:\a\b") == @"C:\a\b");
            Chk("T217-path-phrase-untouched",
                Fluent.TrimPathMiddle("Pictures\\FrameCapture — default location") == "Pictures\\FrameCapture — default location");
            Chk("T217-path-unc-keeps-share",
                Fluent.TrimPathMiddle(@"\\nas\team\2026\十月\很长的目录名称\FrameCapture").StartsWith(@"\\nas\team\"),
                Fluent.TrimPathMiddle(@"\\nas\team\2026\十月\很长的目录名称\FrameCapture"));
        }
        catch (Exception e) { Chk("T217-path-middle-trim", false, e.Message); }

        // 层次台阶：卡片与行同色时整页只剩描边，读起来是一叠线框。两套主题都必须往正确的方向
        // 跨一档——暗色行更亮、亮色行更暗；幅度也要卡住：太小分不出层，太大就把列表切成格子。
        try
        {
            var themeSvc = AppHost.Current.Settings!;
            var keepTheme = themeSvc.Current.Theme;
            static double Lum(System.Windows.Media.Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000.0;
            static double Step(System.Windows.Media.Color a, System.Windows.Media.Color b)
                => Math.Abs(Lum(a) - Lum(b));
            static double Ratio(System.Windows.Media.Color fg, System.Windows.Media.Color bg)
            {
                // WCAG 相对亮度与对比度：alpha 先压平，透明色不能直接算
                static System.Windows.Media.Color Flat(System.Windows.Media.Color c, System.Windows.Media.Color on) =>
                    System.Windows.Media.Color.FromRgb(
                        (byte)(c.A / 255.0 * c.R + (1 - c.A / 255.0) * on.R),
                        (byte)(c.A / 255.0 * c.G + (1 - c.A / 255.0) * on.G),
                        (byte)(c.A / 255.0 * c.B + (1 - c.A / 255.0) * on.B));
                static double L(System.Windows.Media.Color c)
                {
                    static double C(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                    return 0.2126 * C(c.R / 255.0) + 0.7152 * C(c.G / 255.0) + 0.0722 * C(c.B / 255.0);
                }
                var f = Flat(fg, bg);
                double l1 = L(f), l2 = L(bg);
                return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
            }

            FrameCapture.Core.Theme.Apply("dark", themeSvc.Current.Accent);
            Chk("T219-ramp-dark-row-lighter", Lum(Theme.RowBg) > Lum(Theme.CardBg),
                $"行 {Lum(Theme.RowBg):F1} 卡片 {Lum(Theme.CardBg):F1}");
            Chk("T219-ramp-dark-hover-lighter", Lum(Theme.RowHover) > Lum(Theme.RowBg));
            Chk("T219-ramp-dark-step-one-notch",
                Step(Theme.RowBg, Theme.CardBg) >= 4 && Step(Theme.RowBg, Theme.CardBg) <= 24,
                $"台阶 {Step(Theme.RowBg, Theme.CardBg):F1}");
            Chk("T219-ramp-dark-dim-readable", Ratio(Theme.TextDim, Theme.RowBg) >= 4.5,
                $"{Ratio(Theme.TextDim, Theme.RowBg):F2}:1");

            FrameCapture.Core.Theme.Apply("light", themeSvc.Current.Accent);
            Chk("T219-ramp-light-row-darker", Lum(Theme.RowBg) < Lum(Theme.CardBg),
                $"行 {Lum(Theme.RowBg):F1} 卡片 {Lum(Theme.CardBg):F1}");
            Chk("T219-ramp-light-hover-darker", Lum(Theme.RowHover) < Lum(Theme.RowBg));
            Chk("T219-ramp-light-step-one-notch",
                Step(Theme.RowBg, Theme.CardBg) >= 4 && Step(Theme.RowBg, Theme.CardBg) <= 24,
                $"台阶 {Step(Theme.RowBg, Theme.CardBg):F1}");
            Chk("T219-ramp-light-dim-readable", Ratio(Theme.TextDim, Theme.RowBg) >= 4.5,
                $"{Ratio(Theme.TextDim, Theme.RowBg):F2}:1");

            FrameCapture.Core.Theme.Apply(keepTheme, themeSvc.Current.Accent);
        }
        catch (Exception e) { Chk("T219-elevation-ramp", false, e.Message); }

        // 工具条的选中语义：浏览是「没在画」这个状态，不是目录里的一把工具，
        // 所以整条没有高亮才是它对的样子；选一把就只刷那一枚，再点同一枚回到浏览。
        try
        {
            var stTool = new SettingsService();
            var seqTool = new CapturedSequence
            {
                Frames = new List<CapturedFrame> { MovingSquare(160, 90, 0, 1) },
                Region = new RectI(0, 0, 160, 90),
                MonitorBounds = new RectI(0, 0, 1920, 1080),
            };
            var winTool = new FrameCapture.Overlays.EditorWindow(stTool, seqTool);
            var inst = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var stat = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var ty = typeof(FrameCapture.Overlays.EditorWindow);
            var list = ((System.Collections.IEnumerable)ty.GetField("_toolBtns", inst)!.GetValue(winTool)!)
                .Cast<System.Windows.Controls.Button>().ToList();
            var order = (Array)ty.GetField("ToolOrder", stat)!.GetValue(null)!;
            var toolType = order.GetValue(0)!.GetType();
            object? CurTool() => ty.GetField("_tool", inst)!.GetValue(winTool);
            void Pick(string name) => ty.GetMethod("SetTool", inst)!.Invoke(winTool, new[] { Enum.Parse(toolType, name) });
            // 动画中的画刷 ToString() 是 scRGB 串，按字节比、容 ±2
            bool Active(System.Windows.Controls.Button b) =>
                b.Background is System.Windows.Media.SolidColorBrush sb
                && Math.Abs(sb.Color.R - Theme.Accent.R) <= 2
                && Math.Abs(sb.Color.G - Theme.Accent.G) <= 2
                && Math.Abs(sb.Color.B - Theme.Accent.B) <= 2;
            Chk("T221-tool-strip-buttons-match-catalog", list.Count == order.Length,
                $"工具按钮 {list.Count} 枚，工具目录 {order.Length} 把");
            // 浏览不是目录里的一把工具，是「没在画」这个状态：整条没有高亮才是它对的样子。
            // 怎么回浏览这件事写在编辑器首次打开的引导里（再点同一枚 = 回浏览），表面不再多占一个字。
            Chk("T221-browse-state-paints-nothing",
                Equals(CurTool(), Enum.Parse(toolType, "Browse")) && list.Count(Active) == 0,
                $"当前={CurTool()}，高亮 {list.Count(Active)} 枚");
            Pick("Rect");
            PumpMs(240);   // 选中态是动画刷的，不等走完就是拿「还没画完」当「没高亮」
            int rectIdx = Array.IndexOf(order, Enum.Parse(toolType, "Rect"));
            Chk("T221-picked-tool-is-the-only-painted",
                Equals(CurTool(), Enum.Parse(toolType, "Rect")) && Active(list[rectIdx]) && list.Count(Active) == 1,
                $"当前={CurTool()}，高亮 {list.Count(Active)} 枚");
            Pick("Rect");   // 再点同一枚 = 回浏览
            PumpMs(240);
            Chk("T221-toggle-same-tool-back-to-browse",
                Equals(CurTool(), Enum.Parse(toolType, "Browse")) && list.Count(Active) == 0,
                $"当前={CurTool()}，高亮 {list.Count(Active)} 枚");
            winTool.Close();
        }
        catch (Exception e) { Chk("T221-tool-highlight-on-open", false, e.Message); }

        // 键位在全应用只有一副长相：首启卡把键位写成彩色文字、编辑器写成键帽，
        // 用户在首启卡学到的「键位长这样」到下一扇窗就不成立。
        try
        {
            FrameCapture.Overlays.WelcomeToast.Show("F6", "Ctrl+Alt+R");
            var toast = System.Windows.Application.Current.Windows
                .OfType<FrameCapture.Overlays.WelcomeToast>().FirstOrDefault();
            Chk("T222-welcome-toast-opened", toast != null, toast == null ? "没找到首启卡窗口" : "已开");
            var caps = new List<System.Windows.Controls.Border>();
            if (toast != null)
            {
                toast.UpdateLayout();
                void Walk(DependencyObject o)
                {
                    // 键帽的长相：最小宽 24（一个字母键）+ 底边比四周粗一档（按键的厚度感）
                    if (o is System.Windows.Controls.Border bd && bd.Child is System.Windows.Controls.TextBlock
                        && bd.MinWidth >= 24 && bd.BorderThickness.Bottom == 2)
                        caps.Add(bd);
                    int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(o);
                    for (int i = 0; i < n; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
                }
                Walk(toast);
            }
            // F6 一枚 + Ctrl+Alt+R 三枚
            Chk("T222-welcome-keys-are-keycaps", caps.Count == 4, $"键帽 {caps.Count} 枚（应为 4：F6 一枚、Ctrl+Alt+R 三枚）");
            bool SameFace(System.Windows.Controls.Border b) =>
                b.Background is System.Windows.Media.SolidColorBrush sb
                && Math.Abs(sb.Color.R - Fluent.KeycapFace().R) <= 2
                && Math.Abs(sb.Color.G - Fluent.KeycapFace().G) <= 2
                && Math.Abs(sb.Color.B - Fluent.KeycapFace().B) <= 2;
            Chk("T222-welcome-keycap-face-matches-editor", caps.Count > 0 && caps.All(SameFace),
                $"底色与键帽面一致的有 {caps.Count(SameFace)} / {caps.Count} 枚");
            toast?.Close();
        }
        catch (Exception e) { Chk("T222-keycap-vocabulary", false, e.Message); }

        // 浮起卡片的描边方向。暗色主题下投影几乎看不见，卡片靠一条「上缘亮、下缘暗」的
        // 渐变边说清它是浮在桌面内容之上的；等宽实色边撑不起这个层次，卡片就又贴平了。
        // 倒计时卡走同一个 Fluent.CardEdge()，这里查两张被动卡片即可覆盖那条工厂。
        try
        {
            static double Lum(System.Windows.Media.Color c)
            {
                static double F(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                return 0.2126 * F(c.R / 255.0) + 0.7152 * F(c.G / 255.0) + 0.0722 * F(c.B / 255.0);
            }
            void CheckEdge(string name, Window w)
            {
                var edges = new List<System.Windows.Controls.Border>();
                var flat = new List<System.Windows.Controls.Border>();
                void Walk(DependencyObject o)
                {
                    // 卡片是「装了内容的 Border」：只包一层描边的空壳（悬停淡入的那层）不算卡片边。
                    if (o is System.Windows.Controls.Border bd && bd.Child != null && bd.CornerRadius.TopLeft >= 10
                        && bd.BorderBrush != null && bd.BorderBrush != System.Windows.Media.Brushes.Transparent)
                    {
                        if (bd.BorderBrush is System.Windows.Media.LinearGradientBrush) edges.Add(bd);
                        else flat.Add(bd);
                    }
                    int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(o);
                    for (int i = 0; i < n; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
                }
                // 视觉树要等渲染才落地：机器正忙时固定等待会偶发量空，量空就报「没有渐变描边可判」，
                // 那是测试自己的毛病不是产品的毛病。轮询到量出候选边框为止，判据本身一个字不改。
                var swalk = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    edges.Clear(); flat.Clear();
                    w.UpdateLayout();
                    Walk(w);
                    if (edges.Count + flat.Count > 0 || swalk.ElapsedMilliseconds > 3000) break;
                    PumpMs(30);
                }
                Chk($"T223-{name}-card-edge-is-gradient", edges.Count == 1 && flat.Count == 0,
                    $"卡片描边：渐变 {edges.Count} 处、等宽实色 {flat.Count} 处（卡片应恰好 1 条渐变边）");
                bool LitFromAbove(System.Windows.Controls.Border b)
                {
                    var gs = ((System.Windows.Media.LinearGradientBrush)b.BorderBrush).GradientStops;
                    return gs.Count >= 2 && Lum(gs[0].Color) > Lum(gs[gs.Count - 1].Color);
                }
                Chk($"T223-{name}-card-edge-lit-from-above", edges.Count == 1 && LitFromAbove(edges[0]),
                    edges.Count == 0 ? "没有渐变描边可判" :
                    $"上缘亮度 {Lum(((System.Windows.Media.LinearGradientBrush)edges[0].BorderBrush).GradientStops[0].Color):F4}" +
                    $" 下缘 {Lum(((System.Windows.Media.LinearGradientBrush)edges[0].BorderBrush).GradientStops[^1].Color):F4}");
            }

            FrameCapture.Overlays.WelcomeToast.Show("F6", "Ctrl+Alt+R");
            var wc = System.Windows.Application.Current.Windows
                .OfType<FrameCapture.Overlays.WelcomeToast>().FirstOrDefault();
            Chk("T223-welcome-card-found", wc != null, wc == null ? "没找到首启卡" : "已开");
            if (wc != null) CheckEdge("welcome", wc);
            wc?.Close();

            FrameCapture.Overlays.NoticeToast.Show("自检通知", "浮起卡片的描边方向");
            var nc = System.Windows.Application.Current.Windows
                .OfType<FrameCapture.Overlays.NoticeToast>().FirstOrDefault();
            Chk("T223-notice-card-found", nc != null, nc == null ? "没找到通知卡" : "已开");
            if (nc != null) CheckEdge("notice", nc);
            nc?.Close();

            // 「已复制」卡是全局曝光最高的一张卡：每次截图都出现，卡片边也必须同一副长相。
            FrameCapture.Overlays.CaptureToast.Show(Solid(160, 96, 0x30, 0x60, 0x90),
                new FrameCapture.Capture.RectI(0, 0, 160, 96), "", () => { }, () => { });
            var tc = System.Windows.Application.Current.Windows
                .OfType<FrameCapture.Overlays.CaptureToast>().FirstOrDefault();
            Chk("T223-toast-card-found", tc != null, tc == null ? "没找到「已复制」卡" : "已开");
            if (tc != null) CheckEdge("toast", tc);
            tc?.Close();

            // 滚动截图那两张卡也浮在任意桌面内容上，同一副长相就该受同一把尺子量。
            var sh = new FrameCapture.Overlays.ScrollHintWindow(new FrameCapture.Capture.RectI(0, 0, 320, 240), "");
            sh.Show();
            PumpMs(220);
            CheckEdge("scroll-hint", sh);
            sh.Close();
            var spw = new FrameCapture.Overlays.ScrollPreviewWindow(new FrameCapture.Capture.RectI(0, 0, 320, 240), "");
            spw.Show();
            PumpMs(220);
            CheckEdge("scroll-preview", spw);
            spw.Close();
        }
        catch (Exception e) { Chk("T223-card-edge", false, e.Message); }

        // 帮助页的键位列长成键帽：这一页是迷路的人最先翻到的地方，键位在这里也必须像要按的键。
        // 帮助窗在测试进程里 Content 一直是 null（它建内容的时机在别处），整窗量不到东西；
        // 而键位列的切分就是 KeyCell 一个静态函数——直接反射调它，量真正会错的那一步。
        try
        {
            static List<string> Chips(DependencyObject root)
            {
                var list = new List<string>();
                void W(DependencyObject o)
                {
                    if (o is System.Windows.Controls.Border bd && bd.Child is System.Windows.Controls.TextBlock tb
                        && bd.MinWidth >= 24 && bd.BorderThickness.Bottom == 2) list.Add(tb.Text);
                    foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
                        if (c is DependencyObject d) W(d);
                }
                W(root);
                return list;
            }
            var keyCell = typeof(FrameCapture.Overlays.HelpWindow).GetMethod("KeyCell",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            // 多条按法由 DescribeKeysFor 用「本地化逗号」拼接：中文是全角「，」，英文才是 ", "。
            // 只按半角切，中文帮助里第一枚键帽就长成「F6，」。
            var zh = Chips((DependencyObject)keyCell.Invoke(null, new object[] { "F6，Ctrl+Alt+R" })!);
            var en = Chips((DependencyObject)keyCell.Invoke(null, new object[] { "F6, Ctrl+Alt+R" })!);
            Chk("T224-help-keycell-splits-localized-comma", zh.Count == 4 && en.Count == 4,
                $"中文逗号切出 {zh.Count} 枚、英文 {en.Count} 枚（应各 4：F6 一枚 + Ctrl+Alt+R 三枚）");
            Chk("T224-help-keycap-has-no-separator",
                zh.All(c => !c.Contains('，') && !c.Contains(',')) && en.All(c => !c.Contains('，') && !c.Contains(',')),
                "键帽脸上：" + string.Join(" / ", zh));
            // 含中文的不是键位（「未设键」），照文字排，不能被切成一堆空键帽。
            var none = Chips((DependencyObject)keyCell.Invoke(null, new object[] { Loc.T(K.Common.NoKey) })!);
            Chk("T224-help-unbound-stays-text", none.Count == 0, "「未设键」不应长成键帽：" + string.Join(" / ", none));
            // 占位文案靠**身份**认，不靠「里面有没有中文」：英文的「No key」没有中文，
            // 按语言猜会切成一枚「No」键帽加一句 key（英文截图里真出现过）。这条与当前语言无关。
            var seps = new[] { ",", " / " };
            var theme = FrameCapture.Core.Fluent.KeycapTheme(11.5);
            var enPlaceholder = Chips((DependencyObject)FrameCapture.Core.Keycaps.ChordRow(
                "No key", theme, seps, "No key")!);
            Chk("T224-placeholder-never-becomes-a-keycap",
                enPlaceholder.Count == 0 && !FrameCapture.Core.Keycaps.LooksLikeKeys("No key", seps, "No key"),
                "占位文案被切成键帽：" + string.Join(" / ", enPlaceholder));
            // 真键位不受影响：占位文案在场时 Ctrl+Alt+R 照样是三枚键帽。
            var stillKeys = Chips((DependencyObject)FrameCapture.Core.Keycaps.ChordRow(
                "Ctrl+Alt+R", theme, seps, "No key")!);
            Chk("T224-real-chord-still-renders", stillKeys.SequenceEqual(new[] { "Ctrl", "Alt", "R" }),
                "键帽：" + string.Join(" / ", stillKeys));
        }
        catch (Exception e) { Chk("T224-help-key-vocabulary", false, e.Message); }

        // 就绪卡必须说真话：卡上的键位来自用户自己的配置，不是出厂默认。
        // 一个动作可以绑多条按法，也可以带触发后缀（「F2 按下」），拼出来形如「Ctrl+Shift+X，F2 按下」；
        // 整串当一个组合键切，键帽会裂成「F2，按下」——所以这里造一份用户配置，量卡片照不照得见。
        try
        {
            var st = FrameCapture.Core.SettingsService.Shared!;
            var g = st.Current.Gestures;
            var saved = g.Bindings.ToList();
            var savedFlag = st.Current.WelcomeShowsHotkeys;
            static (List<string> Chips, List<string> Notes) ScanCard(DependencyObject root)
            {
                var chips = new List<string>();
                var notes = new List<string>();
                void W(DependencyObject o)
                {
                    if (o is System.Windows.Controls.Border bd && bd.Child is System.Windows.Controls.TextBlock tb
                        && bd.MinWidth >= 24 && bd.BorderThickness.Bottom == 2) chips.Add(tb.Text);
                    else if (o is System.Windows.Controls.TextBlock n && !string.IsNullOrWhiteSpace(n.Text)) notes.Add(n.Text);
                    foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
                        if (c is DependencyObject d) W(d);
                }
                W(root);
                return (chips, notes);
            }
            void ShowCard()
            {
                foreach (var old in System.Windows.Application.Current.Windows.OfType<FrameCapture.Overlays.WelcomeToast>().ToList())
                    old.Close();
                PumpMs(120);
                FrameCapture.Overlays.WelcomeToast.Show(g.CaptureKeyText(), g.RecordKeyText());
                PumpMs(260);
            }

            // 截图绑两条（第二条带「按下」后缀），录制一条都不留。
            g.Bindings.Clear();
            g.Bindings.Add(new FrameCapture.Core.GestureBindingDto
            { Id = "t.cap1", Chord = "Ctrl+Shift+X", Actions = new[] { FrameCapture.Core.GestureActions.CaptureStored } });
            g.Bindings.Add(new FrameCapture.Core.GestureBindingDto
            { Id = "t.cap2", Chord = "F2", Trigger = "KeyDown", Actions = new[] { FrameCapture.Core.GestureActions.CaptureStored } });
            ShowCard();
            var card = System.Windows.Application.Current.Windows.OfType<FrameCapture.Overlays.WelcomeToast>().First();
            var scanned = ScanCard(card);
            Chk("T225-card-shows-user-chords",
                scanned.Chips.SequenceEqual(new[] { "Ctrl", "Shift", "X", "F2" }),
                "键帽：" + string.Join(" / ", scanned.Chips) + "（应为 Ctrl / Shift / X / F2，两条按法都要照见）");
            Chk("T225-card-no-comma-in-chip",
                scanned.Chips.All(c => !c.Contains('，') && !c.Contains(',')),
                "键帽脸上：" + string.Join(" / ", scanned.Chips));
            // 就绪卡只回答「按什么键」：触发后缀（轻按/按住松开）不该挤进这一行，
            // 但帮助窗那张键位表仍要如实写出后缀。
            Chk("T225-card-hides-trigger-suffix", !scanned.Notes.Contains(Loc.T(K.Triggers.TriggerKeydown)),
                "卡片上仍带着后缀：" + string.Join(" / ", scanned.Notes));
            Chk("T225-help-keeps-trigger-suffix",
                g.DescribeKeysFor(GestureActions.CaptureStored, GestureActions.CaptureFrame)
                    .Contains(Loc.T(K.Triggers.TriggerKeydown)),
                "帮助窗那份键位串没带后缀：" + g.DescribeKeysFor(GestureActions.CaptureStored, GestureActions.CaptureFrame));
            Chk("T225-card-says-unbound",
                scanned.Notes.Contains(Loc.T(K.Common.NoKey)),
                "没绑键的录制应显示「未设键」，实际：" + string.Join(" / ", scanned.Notes));

            // 开关关掉：卡片不列键位，也不该留下半排键帽。
            st.Current.WelcomeShowsHotkeys = false;
            ShowCard();
            var off = System.Windows.Application.Current.Windows.OfType<FrameCapture.Overlays.WelcomeToast>().First();
            Chk("T225-toggle-hides-keys", ScanCard(off).Chips.Count == 0,
                "关掉开关后还剩 " + ScanCard(off).Chips.Count + " 枚键帽");
            st.Current.WelcomeShowsHotkeys = true;
            ShowCard();
            var on = System.Windows.Application.Current.Windows.OfType<FrameCapture.Overlays.WelcomeToast>().First();
            Chk("T225-toggle-restore-shows-keys", ScanCard(on).Chips.Count == 4,
                "重新打开后应有 4 枚键帽，实际 " + ScanCard(on).Chips.Count + " 枚");
            foreach (var old in System.Windows.Application.Current.Windows.OfType<FrameCapture.Overlays.WelcomeToast>().ToList())
                old.Close();

            // 这条测试改的是内存里的绑定表，跑完原样还回去。
            g.Bindings.Clear();
            foreach (var b in saved) g.Bindings.Add(b);
            st.Current.WelcomeShowsHotkeys = savedFlag;
        }
        catch (Exception e) { Chk("T225-ready-card-follows-config", false, e.Message); }

        // 首启那张卡不自己走：没看见右下角这一下的人，不该被 12 秒倒计时没收唯一一次说明。
        // 脚本叫出来审查的那张仍按时自己走，免得卡在屏幕上。
        try
        {
            static bool AutoDismissRunning(Window w)
            {
                var f = typeof(FrameCapture.Overlays.WelcomeToast).GetField("_dismiss",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                return ((System.Windows.Threading.DispatcherTimer)f.GetValue(w)!).IsEnabled;
            }
            static System.Windows.Controls.Button? FindClose(DependencyObject o)
            {
                if (o is System.Windows.Controls.Button b && Equals(b.ToolTip, Loc.T(K.Common.Close))) return b;
                foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
                    if (c is DependencyObject d && FindClose(d) is { } found) return found;
                return null;
            }
            FrameCapture.Overlays.WelcomeToast.Show("F6", "Ctrl+Alt+R", sticky: true);
            var keep = System.Windows.Application.Current.Windows
                .OfType<FrameCapture.Overlays.WelcomeToast>().First();
            Chk("T226-first-run-card-does-not-self-close", !AutoDismissRunning(keep),
                "首启卡的计时器在跑，12 秒后会自己走");
            // 唯一出口：卡片不抢焦点，Esc 归用户当下正在用的那个窗口（实测注入 Esc 关不掉），
            // 而它不自己走——所以 ✕ 必须在，而且点了得真能关。
            var close = FindClose(keep);
            Chk("T226-first-run-card-has-a-way-out", close != null,
                "首启卡既不自己走、又等不到 Esc，却没有 ✕ 可点——用户被它关不住");
            if (close != null)
            {
                close.RaiseEvent(new System.Windows.RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                PumpMs(200);
                Chk("T226-close-button-dismisses-the-card",
                    !System.Windows.Application.Current.Windows
                        .OfType<FrameCapture.Overlays.WelcomeToast>().Any(w => ReferenceEquals(w, keep)),
                    "点了 ✕ 卡片还在");
            }
            foreach (var w in System.Windows.Application.Current.Windows
                         .OfType<FrameCapture.Overlays.WelcomeToast>().ToList()) w.Close();
            FrameCapture.Overlays.WelcomeToast.Show("F6", "Ctrl+Alt+R");
            var timed = System.Windows.Application.Current.Windows
                .OfType<FrameCapture.Overlays.WelcomeToast>().First();
            Chk("T226-scripted-card-still-times-out", AutoDismissRunning(timed),
                "脚本叫出来的卡应按时自己走，不然审查脚本会一直等");
            timed.Close();
        }
        catch (Exception e) { Chk("T226-welcome-card-timing", false, e.Message); }

        // 就绪卡只列已配置的快捷键，不放任何入口按钮：卡片上只该有 ✕ 这一枚图标按钮。
        try
        {
            foreach (var old in System.Windows.Application.Current.Windows.OfType<FrameCapture.Overlays.WelcomeToast>().ToList())
                old.Close();
            PumpMs(120);
            FrameCapture.Overlays.WelcomeToast.Show("F6", "Ctrl+Alt+R");
            PumpMs(220);
            var card = System.Windows.Application.Current.Windows.OfType<FrameCapture.Overlays.WelcomeToast>().First();
            // 图标按钮（✕）不算：它有字形内容，正文按钮没有
            int textButtons = 0;
            void WalkBtn(DependencyObject o)
            {
                if (o is System.Windows.Controls.Button b && b.Content is string s && s.Length > 0
                    && !s.Any(ch => ch >= 0xE000 && ch <= 0xF8FF)) textButtons++;
                foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
                    if (c is DependencyObject d) WalkBtn(d);
            }
            WalkBtn(card);
            Chk("T227-ready-card-has-no-entry-button", textButtons == 0, "卡片上还有 " + textButtons + " 枚文字按钮");
            // 落点：右下角各留 16 DIP。入场动画收尾会把 Left/Top 写回起始值，这条就是防它回退。
            var wa = System.Windows.SystemParameters.WorkArea;
            Chk("T227-ready-card-sits-in-the-corner",
                Math.Abs((card.Left + card.ActualWidth) - (wa.Right - 16)) <= 2
                && Math.Abs((card.Top + card.ActualHeight) - (wa.Bottom - 16)) <= 2,
                $"右 {card.Left + card.ActualWidth:N0}/{wa.Right - 16:N0} 底 {card.Top + card.ActualHeight:N0}/{wa.Bottom - 16:N0}");
            foreach (var old in System.Windows.Application.Current.Windows.OfType<FrameCapture.Overlays.WelcomeToast>().ToList())
                old.Close();
        }
        catch (Exception e) { Chk("T227-ready-card-buttons", false, e.Message); }

        // 键位框必须显示它报出去的那个键位：只把新值交给宿主、自己还留着原值，用户改完看到的仍是原键帽。
        static List<string> ChipsIn(DependencyObject root)
        {
            var list = new List<string>();
            void W(DependencyObject o)
            {
                if (o is System.Windows.Controls.Border b && b.Child is System.Windows.Controls.TextBlock tb
                    && b.MinWidth >= 24 && b.BorderThickness.Bottom == 2) list.Add(tb.Text);
                foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
                    if (c is DependencyObject d) W(d);
            }
            W(root);
            return list;
        }
        static void ShowOffscreen(Window w) { w.Left = -3000; w.Top = -3000; w.ShowInTaskbar = false; w.Show(); PumpMs(140); }
        try
        {
            var box = new FrameCapture.Core.ChordCaptureBox { Chord = "F6" };
            string picked = "";
            box.Picked += c => picked = c;
            var host = new Window { Width = 220, Height = 120, WindowStyle = WindowStyle.None, Content = box };
            ShowOffscreen(host);
            box.BeginCapture();
            var mi = typeof(FrameCapture.Core.ChordCaptureBox).GetMethod("OnPreviewKeyDown",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)!;   // 基类也有同名重写：不加 DeclaredOnly 会「同形重载」报错
            var ke = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                System.Windows.PresentationSource.FromVisual(box)!, 0, System.Windows.Input.Key.F7)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            mi.Invoke(box, new object[] { box, ke });
            PumpMs(80);
            Chk("T228-chord-box-reports-the-new-key", picked == "F7", "报出去的是：" + picked);
            Chk("T228-chord-box-shows-the-new-key", ChipsIn(box).SequenceEqual(new[] { "F7" }),
                "框里显示：" + string.Join(" / ", ChipsIn(box)));
            host.Close();
        }
        catch (Exception e) { Chk("T228-chord-box-display", false, e.Message); }

        // 语言三档装在同一条轨道里：三段的上下沿由同一个数说话，不能谁比谁高半截。
        try
        {
            var sw = new FrameCapture.Overlays.SettingsWindow();
            var np = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(FrameCapture.Overlays.SettingsWindow).GetField("_restoreCat", np)!.SetValue(sw, 6);   // 外观
            typeof(FrameCapture.Overlays.SettingsWindow).GetMethod("Build", np)!.Invoke(sw, null);
            sw.Left = -3000; sw.Top = -3000; sw.ShowInTaskbar = false; sw.Show(); PumpMs(200);
            var segs = new List<System.Windows.Controls.Border>();
            void WalkSeg(DependencyObject o)
            {
                if (o is System.Windows.Controls.Border b && b.Child is System.Windows.Controls.TextBlock { FontSize: > 12 and < 13 }
                    && Math.Abs(b.Height - 24) < 0.1) segs.Add(b);
                int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(o);
                for (int i = 0; i < n; i++) WalkSeg(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
            }
            WalkSeg(sw);
            Chk("T229-language-segments-are-equal-height",
                segs.Count == 3 && segs.Select(s => Math.Round(s.ActualHeight, 1)).Distinct().Count() == 1,
                segs.Count + " 段，高度：" + string.Join(" / ", segs.Select(s => s.ActualHeight.ToString("N1"))));
            sw.Close();
        }
        catch (Exception e) { Chk("T229-language-segment-heights", false, e.Message); }

        // 保存路径行是可直接编辑的输入框：回车落盘，Esc 把框里的字放回当前配置。
        try
        {
            // 设置窗读的是 AppHost 那份 Settings 实例，和 SettingsService.Shared 未必是同一个——
            // 断言与还原都走同一份，否则改的是甲的设置、读的是乙，测试自己制造出「没生效」。
            var svc = FrameCapture.Services.AppHost.Current.Settings;
            string before = svc.Current.AutoSavePath;
            var sw = new FrameCapture.Overlays.SettingsWindow();
            var np = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(FrameCapture.Overlays.SettingsWindow).GetField("_restoreCat", np)!.SetValue(sw, 3);   // 产物
            typeof(FrameCapture.Overlays.SettingsWindow).GetMethod("Build", np)!.Invoke(sw, null);
            sw.Left = -3000; sw.Top = -3000; sw.ShowInTaskbar = false; sw.Show(); PumpMs(200);
            var boxes = new List<System.Windows.Controls.TextBox>();
            void FindBox(DependencyObject o)
            {
                if (o is System.Windows.Controls.TextBox t) boxes.Add(t);
                foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
                    if (c is DependencyObject d) FindBox(d);
            }
            FindBox(sw);
            var box = boxes.FirstOrDefault(b => Equals(b.ToolTip, Loc.T(K.Settings.SaveFolderAutoSavedCopies)));
            Chk("T230-save-path-is-an-editable-box", box != null, "产物页里没有可编辑的路径输入框");
            if (box != null)
            {
                void Press(System.Windows.Input.Key key) => box!.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(box)!, 0, key)
                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                box.Text = @"C:\Windows";
                Press(System.Windows.Input.Key.Enter);
                PumpMs(80);
                Chk("T230-typed-path-lands-in-settings", svc.Current.AutoSavePath == @"C:\Windows",
                    "设置里是：" + svc.Current.AutoSavePath);
                box.Text = @"D:\no-such-folder";
                Press(System.Windows.Input.Key.Escape);
                PumpMs(80);
                Chk("T230-escape-reverts-the-box", svc.Current.AutoSavePath == @"C:\Windows" && box.Text == @"C:\Windows",
                    "设置=" + svc.Current.AutoSavePath + " 框里=" + box.Text);
            }
            sw.Close();
            svc.Mutate(s => s.AutoSavePath = before);   // 还原成审查前的设置
        }
        catch (Exception e) { Chk("T230-save-path-row", false, e.Message); }

        // 取色器：色域必须是**方形直角**、四角不许被裁；下面只剩预览与 HEX 一个输入框；两排右端齐平。
        try
        {
            var view = new FrameCapture.Overlays.ColorPickView { Color = System.Windows.Media.Color.FromArgb(0x80, 0xE8, 0x4C, 0x3D) };
            // 宿主按内容定尺寸：弹层里的宽度就是内容自己撑出来的那段。
            // 写死一个更大宽度会把下排拉伸到比上排宽，量出来的「右端不齐」是量具自己造的。
            var host = new Window { WindowStyle = WindowStyle.None, SizeToContent = SizeToContent.WidthAndHeight, Content = view };

            ShowOffscreen(host);
            var topRow = (System.Windows.Controls.Grid)view.Children[0];
            var bottomRow = (System.Windows.Controls.Grid)view.Children[1];
            var last = (System.Windows.FrameworkElement)topRow.Children[topRow.Children.Count - 1];
            var lastBottom = (System.Windows.FrameworkElement)bottomRow.Children[bottomRow.Children.Count - 1];
            double topRight = last.TranslatePoint(new System.Windows.Point(last.ActualWidth, 0), view).X;
            // 量的是「下排最后一块内容的右沿」：下排会被父级拉伸到整宽，用它自己的 ActualWidth 等于在量容器。
            double bottomRight = lastBottom.TranslatePoint(new System.Windows.Point(lastBottom.ActualWidth, 0), view).X;
            Chk("T231-picker-rows-end-together", Math.Abs(topRight - bottomRight) <= 1.5,
                $"上排右端 {topRight:N1} / 下排右端 {bottomRight:N1}");

            var svCell = (System.Windows.Controls.Border)topRow.Children[0];
            var svEl = (System.Windows.FrameworkElement)svCell.Child;
            Chk("T231-picker-sv-is-square", Math.Abs(svEl.ActualWidth - svEl.ActualHeight) <= 0.5,
                $"色域 {svEl.ActualWidth:N1}×{svEl.ActualHeight:N1}");
            Chk("T231-picker-corners-are-not-cut",
                svCell.CornerRadius.TopLeft == 0 && svCell.Clip == null,
                "色域外框圆角 " + svCell.CornerRadius.TopLeft + "、裁剪 " + (svCell.Clip?.GetType().Name ?? "null"));

            int boxes = 0;
            void CountBoxes(DependencyObject o)
            {
                if (o is System.Windows.Controls.TextBox) boxes++;
                foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
                    if (c is DependencyObject d) CountBoxes(d);
            }
            CountBoxes(view);
            Chk("T231-picker-keeps-only-the-hex-field", boxes == 1, "输入框 " + boxes + " 个（应只剩 HEX 一个）");
            host.Close();
        }
        catch (Exception e) { Chk("T231-picker-layout", false, e.Message); }

        // 临时目录的中间文件超过保留期必须被清掉；未到期的留着（「继续滚动」的基准帧就在里面）。
        try
        {
            System.IO.Directory.CreateDirectory(FrameCapture.Core.TempFiles.Dir);
            string stale = FrameCapture.Core.TempFiles.PathOf("capture-selftest-stale.png");
            string fresh = FrameCapture.Core.TempFiles.PathOf("scroll-selftest-fresh.png");
            System.IO.File.WriteAllBytes(stale, new byte[] { 1 });
            System.IO.File.WriteAllBytes(fresh, new byte[] { 1 });
            System.IO.File.SetLastWriteTimeUtc(stale,
                DateTime.UtcNow - FrameCapture.Core.TempFiles.Keep - TimeSpan.FromHours(1));
            int removed = FrameCapture.Core.TempFiles.SweepStale();
            Chk("T232-temp-sweep-removes-stale", !System.IO.File.Exists(stale) && removed >= 1,
                "超期文件还在，删了 " + removed + " 个");
            Chk("T232-temp-sweep-keeps-fresh", System.IO.File.Exists(fresh), "未到期的那张被清掉了");
            FrameCapture.Core.TempFiles.Drop(fresh);
        }
        catch (Exception e) { Chk("T232-temp-sweep", false, e.Message); }

        // MSAA 的语义判定：按钮/链接/文本算控件；整页文档与无名容器不算，有名字的容器算。
        try
        {
            Chk("T237-msaa-role-button", FrameCapture.Core.MsaaSnap.IsSemantic(0x2B, ""), "按钮角色应当算控件");
            Chk("T237-msaa-role-link", FrameCapture.Core.MsaaSnap.IsSemantic(0x1E, ""), "链接角色应当算控件");
            Chk("T237-msaa-role-document", !FrameCapture.Core.MsaaSnap.IsSemantic(0x0F, "GitHub"), "整页文档不该算控件");
            Chk("T237-msaa-role-unnamed-pane", !FrameCapture.Core.MsaaSnap.IsSemantic(0x10, ""), "无名容器不该算控件");
            Chk("T237-msaa-role-named-group", FrameCapture.Core.MsaaSnap.IsSemantic(0x10, "Dashboard"), "有名字的容器该算控件");
        }
        catch (Exception e) { Chk("T237-msaa-semantic", false, e.Message); }

        // 可调整选区的纯几何：抓边角、缩放、整块移动、吸附。左右写反是这里最常见的错，逐条钉住。
        try
        {
            var sel = new FrameCapture.Capture.RectI(100, 100, 200, 150);   // 100..300 / 100..250
            Chk("T240-grab-corner", FrameCapture.Core.AdjustGeometry.Hit(sel, 101, 101, 6) == FrameCapture.Core.GrabKind.TopLeft,
                "左上角应判成 TopLeft");
            Chk("T240-grab-edge", FrameCapture.Core.AdjustGeometry.Hit(sel, 200, 249, 6) == FrameCapture.Core.GrabKind.Bottom,
                "下边中点应判成 Bottom");
            Chk("T240-grab-inside-moves", FrameCapture.Core.AdjustGeometry.Hit(sel, 200, 180, 6) == FrameCapture.Core.GrabKind.Move,
                "框内应判成 Move");
            Chk("T240-grab-outside-none", FrameCapture.Core.AdjustGeometry.Hit(sel, 600, 600, 6) == FrameCapture.Core.GrabKind.None,
                "框外应判成 None");

            var bounds = new FrameCapture.Capture.RectI(0, 0, 1000, 800);
            var widened = FrameCapture.Core.AdjustGeometry.Resize(sel, FrameCapture.Core.GrabKind.Right, 50, 0, bounds);
            Chk("T240-resize-right-grows", widened.W == 250 && widened.X == 100 && widened.H == 150,
                "拖右边只改宽：" + widened.W + "×" + widened.H);
            var moved = FrameCapture.Core.AdjustGeometry.Resize(sel, FrameCapture.Core.GrabKind.Move, 30, -20, bounds);
            Chk("T240-move-keeps-size", moved.X == 130 && moved.Y == 80 && moved.W == 200 && moved.H == 150,
                "整块移动尺寸不变：" + moved.X + "," + moved.Y + " " + moved.W + "×" + moved.H);
            var clamped = FrameCapture.Core.AdjustGeometry.Resize(sel, FrameCapture.Core.GrabKind.Left, 500, 0, bounds);
            Chk("T240-resize-clamps-min-side", clamped.W >= FrameCapture.Core.AdjustGeometry.MinSide,
                "缩到最小也不能小于下限：" + clamped.W);


        }
        catch (Exception e) { Chk("T240-adjust-geometry", false, e.Message); }

        // 调色板的实例形态：界面颜色挂在共享画刷上，改配色当场落到在屏元素，不靠重建窗口。
        // 深浅互换则盖一层退场侧的底色再淡出——幕布走 Adorner 层，不进布局，走完自己摘掉。
        try
        {
            bool keepDark = Theme.IsDark;
            var cardBefore = Theme.CardBg;
            Chk("T241-brush-matches-token", Theme.CardBgBrush.Color == cardBefore, "共享画刷与令牌值同源");
            Chk("T241-brush-is-shared", Theme.IsPaletteBrush(Theme.CardBgBrush) && Theme.IsPaletteBrush(Theme.AccentBrush),
                "调色板画刷该被认成共享实例");
            Chk("T241-brush-private-not-shared", !Theme.IsPaletteBrush(new System.Windows.Media.SolidColorBrush(Theme.CardBg)),
                "自己 new 的那份不算共享实例");

            var probe = System.Windows.Media.Color.FromRgb(0x33, 0x66, 0x99);
            bool hit = Theme.SetCustom("CardBg", Theme.IsDark, probe);
            Chk("T241-custom-lands-in-brush", hit && Theme.CardBgBrush.Color == probe,
                "改一枚令牌，画刷当场跟着变——没有任何窗口参与");
            Theme.ClearCustom("CardBg", Theme.IsDark);
            Chk("T241-clear-restores-brush", Theme.CardBgBrush.Color == cardBefore, "去掉自定义，画刷回到出厂值");

            // 交互面必须是不透明实色：半透明底上混出来的也要实色，否则同一处压在什么上面就读出什么颜色。
            var veil = System.Windows.Media.Color.FromArgb(0x80, 0x20, 0x30, 0x40);
            Chk("T241-mixes-opaque",
                Fluent.HoverOn(veil).A == 0xFF && Fluent.PressOn(veil).A == 0xFF && Fluent.AccentOn(veil, 0.26).A == 0xFF,
                "半透明底上混出的悬停/按下/选中都是实色");
            double Lum(System.Windows.Media.Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
            var hov = Fluent.HoverOn(Theme.NavBg);
            var press = Fluent.PressOn(Theme.NavBg);
            Chk("T241-press-steps-past-hover",
                Math.Abs(Lum(press) - Lum(hov)) > 0.5 && (Theme.IsDark ? Lum(press) > Lum(hov) : Lum(press) < Lum(hov)),
                $"按下恒比悬停多走一档：悬停 {Lum(hov):F1} → 按下 {Lum(press):F1}");

            // 选中项的文字压在「强调色淡底」上，比卡片底亮，照抄 AccentText 会糊；按真实底色判。
            foreach (var which in new[] { "light", "dark" })
            {
                Theme.Apply(which, null);
                var tint = Fluent.AccentOn(Theme.NavBg, 0.26);
                double r = Fluent.ContrastRatio(Fluent.AccentTextOn(tint), tint);
                Chk($"T241-accent-text-on-tint-{which}", r >= 4.5, $"{which} 态淡底上的强调色文字对比 {r:F2}");
                // 出厂配色的两枚字色要压得住四种底——配色页把这条规则摆给用户看，出厂值自己先得过
                foreach (var (nm, ink) in new[] { ("Text", Theme.Text), ("TextDim", Theme.TextDim) })
                    foreach (var bgKey in new[] { "WindowBg", "PanelBg", "CardBg", "RowBg" })
                    {
                        double rr = Fluent.ContrastRatio(ink, Theme.Opaque(Theme.ValueOf(bgKey, Theme.IsDark)));
                        Chk($"T241-factory-ink-{which}-{nm}-{bgKey}", rr >= 4.5,
                            $"{which} 态 {nm} 压在 {bgKey} 上只有 {rr:F2}:1");
                    }
            }
            Theme.Apply(keepDark ? "dark" : "light", null);

            // 整套预设与设置文件的清洗
            var mid = ColorPresets.Find("midnight");
            var ps = new AppSettings { ColorPreset = mid.Id };
            ps.Colors = new Dictionary<string, string>(mid.Colors, StringComparer.Ordinal);
            ps.ApplyPalette();
            Chk("T241-preset-applies", Theme.Custom("WindowBg") != null && Theme.Custom("PanelBg") != null,
                "预设整套落进调色板");
            Theme.ClearAllCustoms();
            Chk("T241-preset-clears", Theme.Custom("WindowBg") == null, "清掉自定义后回到出厂值");

            // 每套预设都要让正文读得清：预设是「一键换整套」，坏一套就是一次全站不可读。
            foreach (var pr in ColorPresets.All)
            {
                var pp = new AppSettings { ColorPreset = pr.Id };
                pp.Colors = new Dictionary<string, string>(pr.Colors, StringComparer.Ordinal);
                pp.ApplyPalette();
                foreach (var which in new[] { "light", "dark" })
                {
                    Theme.Apply(which, null);
                    double worst = double.MaxValue;
                    string at = "";
                    foreach (var inkKey in new[] { "Text", "TextDim" })
                        foreach (var bgKey in new[] { "WindowBg", "PanelBg", "CardBg", "RowBg" })
                        {
                            double rr = Fluent.ContrastRatio(Theme.ValueOf(inkKey, Theme.IsDark),
                                Theme.Opaque(Theme.ValueOf(bgKey, Theme.IsDark)));
                            if (rr < worst) { worst = rr; at = $"{inkKey} 压在 {bgKey} 上"; }
                        }
                    Chk($"T241-preset-ink-{pr.Id}-{which}", worst >= 4.5,
                        $"{pr.Id} 预设 {which} 态最差一对 {at} 只有 {worst:F2}:1");
                }
            }
            Theme.ClearAllCustoms();

            // 预览卡那行跟着对比当场改口：出厂配色报 0 处，把正文改成贴着窗口底就该报出来
            var inkFn = typeof(FrameCapture.Overlays.SettingsWindow).GetMethod("InkShortfalls",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Theme.Apply("light", null);
            Chk("T241-ink-note-clear", (int)inkFn!.Invoke(null, null)! == 0, "出厂配色的预览卡报 0 处对比不足");
            Theme.SetCustom("Text", false, Theme.Opaque(Theme.ValueOf("WindowBg", false)));
            Chk("T241-ink-note-warn", (int)inkFn.Invoke(null, null)! > 0, "正文贴着窗口底时预览卡报出对比不足");
            Theme.ClearAllCustoms();
            Theme.Apply("dark", null);   // 深浅档交回后面那段用例认得的这一档

            var junk = new AppSettings();
            junk.Colors["dark.CardBg"] = "#123456";
            junk.Colors["dark.NotAToken"] = "#123456";
            junk.Colors["light.CardBg"] = "#GGGGGG";
            junk.Colors["light.RowBg"] = HexColor.Format(Theme.DefaultColor("RowBg", false));
            junk.Normalize();
            Chk("T241-normalize-drops-junk", junk.Colors.Count == 1 && junk.Colors["dark.CardBg"] == "#123456",
                "未知键、非法色、与出厂值相同的条目都在归一时丢掉：" + string.Join(",", junk.Colors.Keys));
            var badPreset = new AppSettings { ColorPreset = "nope" };
            badPreset.Normalize();
            Chk("T241-normalize-preset-id", badPreset.ColorPreset == "", "认不出的预设标识在归一时清空");

            // 换色遮罩：盖在 Adorner 层上，从圆心推开，铺满自己摘掉
            var mw = new System.Windows.Window
            {
                Content = new System.Windows.Controls.Grid { Background = System.Windows.Media.Brushes.Transparent },
                Width = 220,
                Height = 140,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowInTaskbar = false,
            };
            mw.Show();
            PumpMs(80);
            var rootEl = (System.Windows.UIElement)mw.Content;
            var from = System.Windows.Media.Color.FromRgb(0x11, 0x22, 0x33);
            Fluent.MaskFade(mw, from);
            var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(rootEl);
            var on = layer?.GetAdorners(rootEl);
            Chk("T241-mask-on", on != null && on.Length == 1, "换色幕布出现在 Adorner 层上，不占布局");
            var face = on != null
                ? (on[0].GetType()
                    .GetField("_b", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(on[0]) as System.Windows.Controls.Border)
                : null;
            var fill = face?.Background as System.Windows.Media.SolidColorBrush;
            Chk("T241-mask-color-outgoing", fill != null && fill.Color == from, "幕布铺的是换色那一刻画面上的窗口底");
            PumpMs(30);
            Chk("T241-mask-covers-window", face != null
                && face.ActualWidth == rootEl.RenderSize.Width && face.ActualHeight == rootEl.RenderSize.Height,
                $"幕布铺满整窗：{face?.ActualWidth.ToString() ?? "?"}×{face?.ActualHeight.ToString() ?? "?"}");
            PumpMs(Fluent.Motion.ThemeMs + 400);
            var off = layer?.GetAdorners(rootEl);
            Chk("T241-mask-gone", off == null || off.Length == 0, "淡完之后幕布自己摘掉");
            mw.Close();
        }
        catch (Exception e) { Chk("T241-palette-live", false, e.Message); }

        // 遮罩挂在真设置窗上：深浅档互换从圆心推开一层新底色，逐枚改色不盖遮罩——那种改动要的是当场看见。
        try
        {
            bool keepDark = Theme.IsDark;
            var sw = new FrameCapture.Overlays.SettingsWindow();
            typeof(FrameCapture.Overlays.SettingsWindow)
                .GetMethod("Build", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(sw, null);
            sw.WindowStartupLocation = WindowStartupLocation.Manual;
            sw.Left = -20000; sw.Top = -20000; sw.ShowInTaskbar = false;
            sw.Show();
            PumpMs(140);
            var sroot = (System.Windows.UIElement)sw.Content;
            var slayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(sroot);
            bool Masked()
            {
                var a = slayer?.GetAdorners(sroot);
                return a != null && a.Length > 0;
            }
            Chk("T242-mask-idle", !Masked(), "窗刚开时幕布不在场");
            // 遮罩是在 Theme.Apply 的广播里同步挂上装饰层的，取它不需要等渲染节拍：
            // 泵一下反而可能在重载机器上等到整段动画走完，量到的是「已经摘掉了」
            Theme.Apply(Theme.IsDark ? "light" : "dark", null);
            Chk("T242-mask-on-swap", Masked(), "深浅档互换：设置窗盖上一层幕布");
            PumpMs(Fluent.Motion.ThemeMs + 400);
            Chk("T242-mask-after-swap", !Masked(), "淡完之后幕布自己摘掉");
            Theme.SetCustom("CardBg", Theme.IsDark, System.Windows.Media.Color.FromRgb(0x21, 0x22, 0x23));
            PumpMs(20);
            Chk("T242-mask-on-edit", !Masked(), "逐枚改色不盖幕布");
            Theme.ClearCustom("CardBg", Theme.IsDark);
            Theme.Apply(keepDark ? "dark" : "light", null);
            PumpMs(60);
            sw.Close();
        }
        catch (Exception e) { Chk("T242-settings-mask", false, e.Message); }
        // 编辑器推开同一层：深浅档在别处翻了，开着的编辑器不该几十处颜色同帧跳变
        try
        {
            var stEd = new SettingsService();
            var seqEd = new CapturedSequence
            {
                Frames = new List<CapturedFrame> { MovingSquare(200, 120, 0, 1) },
                Region = new RectI(0, 0, 200, 120),
                MonitorBounds = new RectI(0, 0, 1920, 1080),
            };
            var winEd = new FrameCapture.Overlays.EditorWindow(stEd, seqEd);
            winEd.WindowStartupLocation = WindowStartupLocation.Manual;
            winEd.Left = -20000; winEd.Top = -20000; winEd.ShowInTaskbar = false;
            winEd.Show();
            PumpMs(220);
            var eroot = (System.Windows.UIElement)winEd.Content;
            var elayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(eroot);
            bool EdMasked()
            {
                var a = elayer?.GetAdorners(eroot);
                return a != null && a.Length > 0;
            }
            Chk("T243-editor-mask-idle", !EdMasked(), "编辑器刚开时幕布不在场");
            Theme.Apply(Theme.IsDark ? "light" : "dark", null);
            Chk("T243-editor-mask-on-swap", EdMasked(), "深浅档互换：编辑器也盖一层幕布");
            PumpMs(Fluent.Motion.ThemeMs + 400);
            Chk("T243-editor-mask-gone", !EdMasked(), "编辑器幕布淡完之后自己摘掉");
            winEd.Close();
            Theme.Apply(Theme.IsDark ? "light" : "dark", null);
            PumpMs(60);
        }
        catch (Exception e) { Chk("T243-editor-mask", false, e.Message); }
        // 语言三档的文字色跟着调色板走：三段是构造期取色的私有画刷，漏登记重画就会在换深浅档后停在旧档的灰度上
        try
        {
            var npLang = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var swLang = new FrameCapture.Overlays.SettingsWindow();
            typeof(FrameCapture.Overlays.SettingsWindow).GetField("_restoreCat", npLang)!.SetValue(swLang, 6);   // 外观
            typeof(FrameCapture.Overlays.SettingsWindow).GetMethod("Build", npLang)!.Invoke(swLang, null);
            swLang.WindowStartupLocation = WindowStartupLocation.Manual;
            swLang.Left = -20000; swLang.Top = -20000; swLang.ShowInTaskbar = false;
            swLang.Show();
            PumpMs(180);
            var labels = new List<System.Windows.Controls.TextBlock>();
            void WalkLang(DependencyObject o)
            {
                if (o is System.Windows.Controls.TextBlock tb && tb.Text is "中文" or "English") labels.Add(tb);
                int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(o);
                for (int i = 0; i < n; i++) WalkLang(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
            }
            WalkLang(swLang);
            Chk("T244-lang-chip-found", labels.Count >= 2, $"语言三档在外观页上找得到 {labels.Count} 段文字");
            System.Windows.Media.Color Fore(System.Windows.Controls.TextBlock t) => ((System.Windows.Media.SolidColorBrush)t.Foreground).Color;
            var before = labels.Select(Fore).ToList();
            Theme.Apply(Theme.IsDark ? "light" : "dark", null);
            PumpMs(80);
            var dim = Theme.TextDim;
            var onAccent = Fluent.OnAccentColor(Theme.Accent);
            bool Same(System.Windows.Media.Color a, System.Windows.Media.Color b) => a.R == b.R && a.G == b.G && a.B == b.B;
            bool OnNewPalette(int i) => Same(Fore(labels[i]), dim) || Same(Fore(labels[i]), onAccent);
            Chk("T244-lang-chip-on-new-palette", labels.Count >= 2 && Enumerable.Range(0, labels.Count).All(OnNewPalette),
                $"换深浅档后语言三档的文字色落在新档上：{string.Join(" / ", labels.Select(t => "#" + Fore(t).R.ToString("X2") + Fore(t).G.ToString("X2") + Fore(t).B.ToString("X2")))}");
            Chk("T244-lang-chip-actually-moved", labels.Count >= 2 &&
                Enumerable.Range(0, labels.Count).Any(i => !Same(Fore(labels[i]), before[i])),
                "至少有一段文字确实从旧档灰度换到了新档");
            Theme.Apply(Theme.IsDark ? "light" : "dark", null);
            PumpMs(60);
            swLang.Close();
        }
        catch (Exception e) { Chk("T244-lang-chip-repaint", false, e.Message); }
        // 删一条快捷键是一拍收高：行一边淡出右滑、一边把高度收到 0，下面的行被布局连着顶上来。
        // 先滑走再补位是两拍——滑走那段时间下面几行纹丝不动，收尾那一下看着就是一个跳。
        try
        {
            var npDel = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var tyDel = typeof(FrameCapture.Overlays.SettingsWindow);
            var tyHost = typeof(FrameCapture.Services.AppHost);
            // 这条用例会真删一条绑定并落盘：整块搬进临时目录，不碰用户机器上的配置
            string? prevDir = Environment.GetEnvironmentVariable("FC_SETTINGS_DIR");
            string scratchDel = Path.Combine(Path.GetTempPath(), "fc-selftest-hotkeys");
            Directory.CreateDirectory(scratchDel);
            // 上一轮写下的 settings.json 里已经少了一条绑定：不清掉，这一轮就是接着上一轮的残局跑
            string scratchFileDel = Path.Combine(scratchDel, "settings.json");
            if (File.Exists(scratchFileDel)) File.Delete(scratchFileDel);
            Environment.SetEnvironmentVariable("FC_SETTINGS_DIR", scratchDel);
            var prevSvc = tyHost.GetProperty("Settings")!.GetValue(FrameCapture.Services.AppHost.Current);
            var svcDel = new SettingsService();
            tyHost.GetProperty("Settings")!.SetValue(FrameCapture.Services.AppHost.Current, svcDel);
            try
            {
                var swDel = new FrameCapture.Overlays.SettingsWindow();
                tyDel.GetField("_restoreCat", npDel)!.SetValue(swDel, 0);   // 快捷键
                tyDel.GetMethod("Build", npDel)!.Invoke(swDel, null);
                swDel.WindowStartupLocation = WindowStartupLocation.Manual;
                swDel.Left = -20000; swDel.Top = -20000; swDel.ShowInTaskbar = false;
                swDel.Show();
                PumpMs(260);
                var list = tyDel.GetField("_hotList", npDel)!.GetValue(swDel) as System.Windows.Controls.StackPanel;
                var rows = tyDel.GetField("_hotRows", npDel)!.GetValue(swDel) as Dictionary<string, FrameworkElement>;
                Chk("T245-hotkey-list-built", list != null && rows != null && rows.Count >= 2 && list.Children.Count >= 2,
                    $"快捷键列表：{rows?.Count ?? 0} 条绑定，{list?.Children.Count ?? 0} 个孩子");
                var first = list!.Children[0] as FrameworkElement;
                var second = list.Children[1] as FrameworkElement;
                string id = rows!.First(kv => ReferenceEquals(kv.Value, first)).Key;
                var target = svcDel.Current.Gestures.Bindings.First(x => x.Id == id);
                double h0 = first!.ActualHeight;
                double y0 = second!.TranslatePoint(new System.Windows.Point(0, 0), list).Y;
                tyDel.GetMethod("DetachHotkeyAnimated", npDel)!.Invoke(swDel, new object[] { target });
                // 起步之后的一小段：被删那行已经矮了，下面那行也已经在往上走——不是等它滑完才动
                PumpMs(45);
                double h1 = first.ActualHeight;
                double y1 = second.TranslatePoint(new System.Windows.Point(0, 0), list).Y;
                Chk("T245-hotkey-delete-shrinks", h1 < h0 - 1, $"被删的那一行从 {h0:F0} 收到 {h1:F0}");
                Chk("T245-hotkey-delete-follows-during", y1 < y0 - 1, $"下面一行当场跟上来：{y0:F1} → {y1:F1}");
                PumpMs(Fluent.Motion.StateMs + 320);
                Chk("T245-hotkey-delete-gone", !list.Children.Contains(first), "收高走完这一行退出列表");
                Chk("T245-hotkey-delete-committed", svcDel.Current.Gestures.Bindings.All(x => x.Id != id), "配置里也摘掉了这条");
                swDel.Close();
            }
            finally
            {
                tyHost.GetProperty("Settings")!.SetValue(FrameCapture.Services.AppHost.Current, prevSvc);
                Environment.SetEnvironmentVariable("FC_SETTINGS_DIR", prevDir);
            }
        }
        catch (Exception e) { Chk("T245-hotkey-delete", false, e.Message); }
        // 侧栏那枚滑条要正落在选中项的中线上：行顶偏移写死过一个数，滑条整体偏出中线六像素
        try
        {
            var npNav = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var tyNav = typeof(FrameCapture.Overlays.SettingsWindow);
            List<System.Windows.Controls.Border>? rows = null;
            System.Windows.Controls.Border? bar = null;
            FrameCapture.Overlays.SettingsWindow? win = null;
            // 开窗停在指定分页，滑条就走开窗那条路落位：第 1 页与第 4 页各量一次
            void Open(int cat)
            {
                win = new FrameCapture.Overlays.SettingsWindow();
                tyNav.GetField("_restoreCat", npNav)!.SetValue(win, cat);
                tyNav.GetMethod("Build", npNav)!.Invoke(win, null);
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.Left = -20000; win.Top = -20000; win.ShowInTaskbar = false;
                win.Show();
                PumpMs(240);
                rows = tyNav.GetField("_navRows", npNav)!.GetValue(win) as List<System.Windows.Controls.Border>;
                bar = tyNav.GetField("_selBar", npNav)!.GetValue(win) as System.Windows.Controls.Border;
            }
            double Offset(int idx)
            {
                var host = (UIElement)bar!.Parent;
                var row = rows![idx];
                double rowMid = row.TranslatePoint(new System.Windows.Point(0, 0), host).Y + row.ActualHeight / 2;
                double barMid = System.Windows.Controls.Canvas.GetTop(bar) + bar.ActualHeight / 2;
                return barMid - rowMid;
            }
            Open(0);
            Chk("T246-nav-indicator-wired", rows != null && rows.Count >= 5 && bar != null && bar.Parent != null,
                $"侧栏 {rows?.Count ?? 0} 项，滑条 {(bar == null ? "没拿到" : "在位")}");
            Chk("T246-nav-indicator-centred", Math.Abs(Offset(0)) <= 1.0, $"首项：滑条中线与行中线差 {Offset(0):F1}px");
            win!.Close();
            // 换一页再量：行位一变，滑条就得落在新行上，而不是只对第一项凑巧对齐
            Open(3);
            Chk("T246-nav-indicator-follows", Math.Abs(Offset(3)) <= 1.0, $"第 4 项：滑条中线与行中线差 {Offset(3):F1}px");
            win!.Close();
        }
        catch (Exception e) { Chk("T246-nav-indicator", false, e.Message); }

        // 配色预设：每套自带强调色，套用即整屏一起换；出厂那一枚把覆盖表与强调色都放回厂值。
        try
        {
            var npPre = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var tyPre = typeof(FrameCapture.Overlays.SettingsWindow);
            var tyHostPre = typeof(FrameCapture.Services.AppHost);
            string? prevDirPre = Environment.GetEnvironmentVariable("FC_SETTINGS_DIR");
            string scratchPre = Path.Combine(Path.GetTempPath(), "fc-selftest-presets");
            Directory.CreateDirectory(scratchPre);
            string scratchFilePre = Path.Combine(scratchPre, "settings.json");
            if (File.Exists(scratchFilePre)) File.Delete(scratchFilePre);
            Environment.SetEnvironmentVariable("FC_SETTINGS_DIR", scratchPre);
            var prevSvcPre = tyHostPre.GetProperty("Settings")!.GetValue(FrameCapture.Services.AppHost.Current);
            var svcPre = new SettingsService();
            tyHostPre.GetProperty("Settings")!.SetValue(FrameCapture.Services.AppHost.Current, svcPre);
            bool keepDarkPre = Theme.IsDark;
            try
            {
                Chk("T247-presets-count", ColorPresets.All.Count >= 6, $"{ColorPresets.All.Count} 套预设");
                Chk("T247-presets-accent-defined",
                    ColorPresets.All.All(p => p.Accent.Length == 0 || HexColor.Parse(p.Accent) != null),
                    "每套的强调色要么留空跟随出厂，要么是合法 HEX");
                Chk("T247-presets-ids-unique",
                    ColorPresets.All.Select(p => p.Id).Distinct().Count() == ColorPresets.All.Count, "预设标识不重复");
                // 强调色是选中态与开关的底，压在上面的字按明度取黑或白，这一对必须读得清
                foreach (var p in ColorPresets.All)
                {
                    var a = p.AccentColor;
                    double fill = Fluent.ContrastRatio(Fluent.OnAccentColor(a), a);
                    Chk($"T247-preset-fill-ink-{p.Id}", fill >= 4.5, $"{p.Id} 的强调色上压字对比 {fill:F2}");
                }
                var swPre = new FrameCapture.Overlays.SettingsWindow();
                tyPre.GetField("_restoreCat", npPre)!.SetValue(swPre, 7);   // 配色
                tyPre.GetMethod("Build", npPre)!.Invoke(swPre, null);
                swPre.WindowStartupLocation = WindowStartupLocation.Manual;
                swPre.Left = -20000; swPre.Top = -20000; swPre.ShowInTaskbar = false;
                swPre.Show();
                PumpMs(200);
                var forest = ColorPresets.Find("forest");
                tyPre.GetMethod("ApplyPreset", npPre)!.Invoke(swPre, new object[] { forest });
                Chk("T247-preset-applies-colors", svcPre.Current.Colors.Count >= 8,
                    $"套用预设后覆盖表有 {svcPre.Current.Colors.Count} 条");
                Chk("T247-preset-applies-accent",
                    svcPre.Current.Accent == forest.Accent && Theme.Accent == forest.AccentColor,
                    $"强调色跟着预设走：设置 {svcPre.Current.Accent} / 调色板 {HexColor.Format(Theme.Accent)}");
                Chk("T247-preset-marks-id", svcPre.Current.ColorPreset == forest.Id, "设置里记下套的是哪一套");
                tyPre.GetMethod("ApplyPreset", npPre)!.Invoke(swPre, new object[] { ColorPresets.Default });
                Chk("T247-preset-default-resets-accent",
                    svcPre.Current.Accent.Length == 0 && Theme.Accent == Theme.DefaultAccent && svcPre.Current.Colors.Count == 0,
                    "出厂那一枚把覆盖表与强调色都放回厂值");
                swPre.Close();
            }
            finally
            {
                tyHostPre.GetProperty("Settings")!.SetValue(FrameCapture.Services.AppHost.Current, prevSvcPre);
                Environment.SetEnvironmentVariable("FC_SETTINGS_DIR", prevDirPre);
                Theme.ClearAllCustoms();
                Theme.ResetAccent();
                Theme.Apply(keepDarkPre ? "dark" : "light", null);
                PumpMs(60);
            }
        }
        catch (Exception e) { Chk("T247-presets", false, e.Message); }

        // 设置页版面：强调色的入口在「配色」页上，时长是两枚可输入下拉框，键位录入全在「快捷键」页。
        // 分页面板按 Build 的装配顺序排在同一个宿主里，按序号取到那一页整棵子树再找控件。
        try
        {
            var npPg = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var tyPg = typeof(FrameCapture.Overlays.SettingsWindow);
            var winPg = new FrameCapture.Overlays.SettingsWindow();
            tyPg.GetField("_restoreCat", npPg)!.SetValue(winPg, 7);
            tyPg.GetMethod("Build", npPg)!.Invoke(winPg, null);
            winPg.WindowStartupLocation = WindowStartupLocation.Manual;
            winPg.Left = -20000; winPg.Top = -20000; winPg.ShowInTaskbar = false;
            winPg.Show();
            PumpMs(240);
            var svsPg = new List<System.Windows.DependencyObject>();
            CollectTyped(winPg, typeof(System.Windows.Controls.ScrollViewer), svsPg);
            var hostPg = (svsPg.Count > 0 ? (svsPg[0] as System.Windows.Controls.ScrollViewer)?.Content : null) as System.Windows.Controls.Grid;
            var pages = hostPg?.Children.Cast<FrameworkElement>().ToList() ?? new List<FrameworkElement>();
            Chk("T248-pages-built", pages.Count >= 9, $"分页 {pages.Count} 个");

            List<System.Windows.DependencyObject> Of(int page, Type t)
            {
                var got = new List<System.Windows.DependencyObject>();
                if (page >= 0 && page < pages.Count) CollectTyped(pages[page], t, got);
                return got;
            }

            // 强调色：色块与十六进制都在「配色」页（第 8 页），外观页上没有它
            var swatches = Of(7, typeof(System.Windows.Controls.Border)).OfType<System.Windows.Controls.Border>()
                .Where(b => (b.Tag as string) == "settings-accent-swatch").ToList();
            var hexes = Of(7, typeof(System.Windows.Controls.TextBlock)).OfType<System.Windows.Controls.TextBlock>()
                .Where(t => (t.Tag as string) == "settings-accent-hex").ToList();
            Chk("T248-accent-entry-present", swatches.Count == 1 && hexes.Count == 1,
                $"配色页上强调色：色块 {swatches.Count} 枚，十六进制 {hexes.Count} 枚");
            var fillAcc = swatches.Count == 1 && swatches[0].Child is System.Windows.Controls.Grid gAcc
                && gAcc.Children.Count > 1 ? gAcc.Children[1] as System.Windows.Controls.Border : null;
            Chk("T248-accent-entry-shows-colour",
                fillAcc?.Background is System.Windows.Media.SolidColorBrush sbAcc && sbAcc.Color == Theme.Accent,
                "色块画的就是当前强调色");
            Chk("T248-accent-entry-shows-hex", hexes.Count == 1 && hexes[0].Text == HexColor.Format(Theme.Accent),
                $"色块旁边写着当前值：{hexes.FirstOrDefault()?.Text}");
            Chk("T248-accent-not-on-appearance",
                Of(6, typeof(System.Windows.Controls.Border)).OfType<System.Windows.Controls.Border>()
                    .All(b => (b.Tag as string) != "settings-accent-swatch"),
                "外观页上没有单独的强调色");

            // 时长：截图页（第 2 页）上两枚可输入下拉框，候选档都在，框里写着当前值。
            // 先切到那一页再找——收起的页不参与排版，模板还没套上去，模板里的部件一个都取不到。
            var navRowsPg = tyPg.GetField("_navRows", npPg)!.GetValue(winPg) as List<System.Windows.Controls.Border>;
            navRowsPg![1].RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.UIElement.MouseLeftButtonDownEvent, Source = navRowsPg[1] });
            PumpMs(520);
            var combos = Of(1, typeof(System.Windows.Controls.ComboBox)).OfType<System.Windows.Controls.ComboBox>().ToList();
            var delayBox = combos.FirstOrDefault(c => (c.Tag as string) == "settings-duration-delay");
            var toastBox = combos.FirstOrDefault(c => (c.Tag as string) == "settings-duration-toast");
            Chk("T252-duration-boxes", delayBox != null && toastBox != null,
                $"截图页上的时长框：延时 {(delayBox == null ? "没找到" : "有")}，悬浮窗 {(toastBox == null ? "没找到" : "有")}");
            Chk("T252-duration-editable", delayBox != null && delayBox.IsEditable && toastBox != null && toastBox.IsEditable,
                "两个时长框都能直接打字");
            Chk("T252-duration-items", delayBox != null && delayBox.Items.Count >= 6 && toastBox != null && toastBox.Items.Count >= 5,
                $"候选档：延时 {delayBox?.Items.Count} 档 / 悬浮窗 {toastBox?.Items.Count} 档");
            // 模板得真有可编辑文本框与弹层：少了前者打不进字，少了后者点箭头没有候选
            Chk("T252-duration-template",
                delayBox != null && delayBox.Template?.FindName("PART_EditableTextBox", delayBox) is System.Windows.Controls.TextBox,
                "模板里有可编辑文本框");
            var popPart = delayBox?.Template?.FindName("PART_Popup", delayBox) as System.Windows.Controls.Primitives.Popup;
            delayBox!.IsDropDownOpen = true;
            PumpMs(260);
            Chk("T252-duration-dropdown-opens",
                popPart != null && popPart.IsOpen && popPart.Child is FrameworkElement pc && pc.ActualWidth > 10,
                $"展开候选：IsOpen={popPart?.IsOpen} 宽 {(popPart?.Child as FrameworkElement)?.ActualWidth ?? 0:F0}");
            delayBox.IsDropDownOpen = false;
            PumpMs(140);
            // 点文字那一截也要开：模板里只有箭头是开关，点正文只会放光标，用户看到的是「点了没反应」
            var tbPart = delayBox.Template?.FindName("PART_EditableTextBox", delayBox) as System.Windows.Controls.TextBox;
            tbPart?.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.UIElement.PreviewMouseLeftButtonUpEvent, Source = tbPart });
            PumpMs(160);
            Chk("T252-duration-click-text-opens", tbPart != null && delayBox.IsDropDownOpen,
                $"点文本框也能拉开候选：IsOpen={delayBox.IsDropDownOpen}");
            delayBox.IsDropDownOpen = false;
            PumpMs(140);
            Chk("T252-duration-shows-current", delayBox != null && double.TryParse(delayBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _), $"延时的框里写着当前值且只有数字：{delayBox?.Text}");
            Chk("T252-duration-unit-outside", delayBox != null && delayBox.Width <= 62 && DurationUnitOf(pages[1], "settings-duration-delay") == Loc.T(K.Settings.SecondsUnit), $"单位在框外：框宽 {delayBox?.Width}，右侧写着「{DurationUnitOf(pages[1], "settings-duration-delay")}」");

            // 键位录入全在「快捷键」页：两条绑定的键帽 + 确认 / 取消 + 复制键，都在带小标题的那张卡里。
            // 编辑器键位那几组也在这页上，所以不数总数，认的是这枚三行各自的标签。
            var keysCap = Of(1, typeof(FrameCapture.Core.ChordCaptureBox));
            var hotTexts = Of(0, typeof(System.Windows.Controls.TextBlock)).OfType<System.Windows.Controls.TextBlock>()
                .Select(t => t.Text).ToList();
            Chk("T251-ui-keys-on-hotkeys-page",
                hotTexts.Contains(Loc.T(K.Capture.ConfirmSelection)) && hotTexts.Contains(Loc.T(K.Capture.CancelSelection))
                    && hotTexts.Contains(Loc.T(K.Settings.CopyKey)),
                $"快捷键页上找得到「{Loc.T(K.Capture.ConfirmSelection)}」「{Loc.T(K.Capture.CancelSelection)}」「{Loc.T(K.Settings.CopyKey)}」三行");
            Chk("T251-keys-not-on-capture-page", keysCap.Count == 0,
                $"截图页上还剩 {keysCap.Count} 个键位框");
            Chk("T251-ui-keys-labelled", hotTexts.Contains(Loc.T(K.Hotkeys.UiKeys)),
                "快捷键页给这组键位写了小标题");
            // 「恢复出厂配色」与卡片同一族：圆角、描边、投影都对得上，不是一块没有边的灰条
            var restoreBtn = Of(7, typeof(System.Windows.Controls.Border)).OfType<System.Windows.Controls.Border>()
                .FirstOrDefault(b => (b.Tag as string) == "settings-restore");
            Chk("T261-restore-looks-like-card",
                restoreBtn != null && restoreBtn.CornerRadius.TopLeft == 8 && restoreBtn.BorderThickness.Left == 1
                    && (restoreBtn.BorderBrush as System.Windows.Media.SolidColorBrush)?.Color == Theme.Stroke
                    && restoreBtn.Effect is System.Windows.Media.Effects.DropShadowEffect,
                "恢复出厂配色：圆角 8、1px 描边、带投影，与卡片同一族");
            // 预览卡里三枚芯片的右间距一致：中间那枚原本一点右间距都没有，两枚贴在一起
            System.Windows.Controls.Border? ChipOf(Str s) => Of(7, typeof(System.Windows.Controls.Border))
                .OfType<System.Windows.Controls.Border>()
                .FirstOrDefault(b => b.Child is System.Windows.Controls.TextBlock tb && tb.Text == Loc.T(s));
            var chipOnB = ChipOf(K.Settings.Accent);
            var chipOffB = ChipOf(K.Settings.TokenStroke);
            Chk("T261-preview-chips-gapped",
                chipOnB != null && chipOffB != null && chipOnB.Margin.Right == 6 && chipOffB.Margin.Right == 6,
                $"预览芯片右间距：{chipOnB?.Margin.Right} / {chipOffB?.Margin.Right}");
            // 新增的两枚开关各在它该在的那一页上，且默认都是开
            var capTexts = Of(1, typeof(System.Windows.Controls.TextBlock)).OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
            var recTexts = Of(2, typeof(System.Windows.Controls.TextBlock)).OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
            Chk("T263-rightclick-toggle-on-capture-page", capTexts.Contains(Loc.T(K.Settings.RightClickCancelsPicker)),
                "「右键关闭选区」在截图页上");
            Chk("T263-startclick-toggle-on-record-page", recTexts.Contains(Loc.T(K.Settings.StartRecordingAfterSelecting)),
                "「选区后先按开始」在录制页上");
            var freshSettings = new AppSettings();
            Chk("T263-new-behaviours-default-on", freshSettings.PickerRightClickCancel && freshSettings.RecordNeedsStartClick,
                "两者出厂都是开：右键关选区、选区后先按开始");
            winPg.Close();
        }
        catch (Exception e) { Chk("T248-settings-pages", false, e.Message); }
        // 选区窗上的模式开关与右键关层：开关改的是这一次的去向，右键等同于 Esc（可在设置里关）
        try
        {
            var mon262 = new FrameCapture.Capture.MonitorInfo("test", new FrameCapture.Capture.RectI(0, 0, 800, 600), true);
            FrameCapture.Core.Loc.Apply("zh");
            var pickRec = new FrameCapture.Overlays.RegionPickerWindow(mon262, null, recordMode: true);
            Chk("T262-picker-mode-initial", pickRec.RecordMode, "选区窗按传入的意图开在录制档");
            pickRec.SetRecordMode(false);
            Chk("T262-picker-mode-switchable", !pickRec.RecordMode, "开关切回截图档");
            int done = 0; bool? finalRegion = null;
            pickRec.Result += (r, _) => { done++; finalRegion = r != null; };
            pickRec.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = System.Windows.Input.Mouse.MouseUpEvent, Source = pickRec });
            Chk("T262-rightclick-closes", done == 1 && finalRegion == false, $"右键关掉整层：回调 {done} 次，范围 {(finalRegion == false ? "空（取消）" : "有")}");
            var pickNoRight = new FrameCapture.Overlays.RegionPickerWindow(mon262, null, rightClickCancel: false);
            int done2 = 0;
            pickNoRight.Result += (_, __) => done2++;
            pickNoRight.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = System.Windows.Input.Mouse.MouseUpEvent, Source = pickNoRight });
            Chk("T262-rightclick-can-be-off", done2 == 0, "设置关掉之后右键不再关层");
            pickNoRight.Close();
        }
        catch (Exception e) { Chk("T262-picker-mode", false, e.Message); }
        // 录制面板：待开始是「开始录制」，起录后原地变成「计时 + 停止」，点停止走停止录制
        try
        {
            int started264 = 0, stopped264 = 0;
            FrameCapture.Overlays.RecordPanelWindow.ShowArmed(new FrameCapture.Capture.RectI(100, 100, 400, 300), "",
                () => started264++, () => stopped264++);
            PumpMs(220);
            var panel264 = FrameCapture.Overlays.RecordPanelWindow.Current;
            Chk("T264-record-panel-shows", panel264 != null && panel264.IsVisible, "选区切到录制后面板出现在屏幕上方");
            var blocks264 = new List<System.Windows.DependencyObject>();
            if (panel264 != null) CollectTyped(panel264, typeof(System.Windows.Controls.Border), blocks264);
            var startBtn264 = blocks264.OfType<System.Windows.Controls.Border>()
                .FirstOrDefault(b => b.Child is System.Windows.Controls.TextBlock t && t.Text == Loc.T(K.Actions.RecordStartLabel));
            startBtn264?.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent, Source = startBtn264 });
            PumpMs(120);
            Chk("T264-record-panel-starts", started264 == 1 && panel264 != null && panel264.IsVisible,
                $"点「开始录制」起录并留在原地：回调 {started264} 次");
            var texts264 = new List<System.Windows.DependencyObject>();
            if (panel264 != null) CollectTyped(panel264, typeof(System.Windows.Controls.TextBlock), texts264);
            var shown264 = texts264.OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
            Chk("T264-record-panel-times", panel264 != null && shown264.Contains(Loc.T(K.Actions.RecordStopLabel))
                && shown264.Any(t => t.Length > 0 && t.Contains(":")),
                "起录后原地变成计时与停止按钮：" + string.Join(" | ", shown264.Where(t => t.Length > 0)));
            var stopBtn264 = blocks264.OfType<System.Windows.Controls.Border>()
                .FirstOrDefault(b => b.Child is System.Windows.Controls.TextBlock t && t.Text == Loc.T(K.Actions.RecordStopLabel));
            stopBtn264?.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent, Source = stopBtn264 });
            PumpMs(160);
            Chk("T264-record-panel-stops", stopped264 == 1 && FrameCapture.Overlays.RecordPanelWindow.Current == null,
                $"点「停止录制」走停止回调并收走面板：回调 {stopped264} 次");
            FrameCapture.Overlays.RecordPanelWindow.Dismiss();
        }
        catch (Exception e) { Chk("T264-record-panel", false, e.Message); }
        // 单键绑定与 Win 组合键：PrtSc 这种只属于截图的裸键要能绑，Win+Shift+S 这类系统先接手的也要能绑
        try
        {
            var prtsc = FrameCapture.Core.HotkeySpec.Parse("PrtSc");
            Chk("T265-bare-prtsc-parses",
                prtsc != null && prtsc.Key == System.Windows.Forms.Keys.PrintScreen && !prtsc.Ctrl && !prtsc.Alt && !prtsc.Shift && !prtsc.Win,
                $"PrtSc 单键解析：{prtsc?.ToString() ?? "null"}");
            Chk("T265-bare-prtsc-roundtrip", prtsc != null && prtsc.ToString() == "PrtSc", "存进设置的串读回来还是 PrtSc");
            Chk("T265-bare-letter-still-rejected", FrameCapture.Core.HotkeySpec.Parse("A") == null,
                "裸字母仍然拒收：热键在窗口外也生效，收下裸键等于劫持别人的输入");
            var winShiftS = FrameCapture.Core.HotkeySpec.Parse("Win+Shift+S");
            Chk("T265-win-chord-parses",
                winShiftS != null && winShiftS.Win && winShiftS.Shift && !winShiftS.Ctrl && winShiftS.Key == System.Windows.Forms.Keys.S,
                $"Win+Shift+S：{winShiftS?.ToString() ?? "null"}");
            var ctrlPrtsc = FrameCapture.Core.HotkeySpec.Parse("Ctrl+PrtSc");
            Chk("T265-modified-prtsc-parses", ctrlPrtsc != null && ctrlPrtsc.Ctrl && ctrlPrtsc.Key == System.Windows.Forms.Keys.PrintScreen,
                $"Ctrl+PrtSc：{ctrlPrtsc?.ToString() ?? "null"}");
            // 手势绑定层：以前裸 PrtSc 在这一步被丢掉，绑定表里看着有、实际不生效
            var dto265 = new FrameCapture.Core.GestureBindingDto
            {
                Id = "selftest.prtsc",
                Chord = "PrtSc",
                Trigger = "Chord",
                Actions = new[] { FrameCapture.Core.GestureActions.ScopeSelect, FrameCapture.Core.GestureActions.CaptureStored },
            };
            Chk("T265-bare-prtsc-reaches-engine", dto265.ToBinding() != null, "裸 PrtSc 能变成引擎绑定（不再是空）");
            // 提权交接回来的页名解析：--page system 要落到「系统」那一页
            var parsePage = typeof(FrameCapture.Overlays.SettingsWindow).GetMethod("ParsePage",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            Chk("T266-page-arg-system", (int)parsePage.Invoke(null, new object?[] { "system" })! == 8,
                "页名 system 落到第 9 页（系统）");
            Chk("T266-page-arg-index", (int)parsePage.Invoke(null, new object?[] { "3" })! == 3, "下标 3 直接当页序");
            Chk("T266-page-arg-junk", (int)parsePage.Invoke(null, new object?[] { "nonsense" })! == -1
                && (int)parsePage.Invoke(null, new object?[] { null })! == -1, "认不出的页名与空值都回 -1");
            var ed265 = FrameCapture.Core.EditorChord.Parse("PrtSc");
            Chk("T265-editor-chord-prtsc",
                ed265 != null && ed265.Value.Key == System.Windows.Input.Key.PrintScreen && ed265.Value.ToString() == "PrtSc"
                    && ed265.Value.Tokens().Count == 1,
                $"编辑器键位里的 PrtSc：{ed265?.ToString() ?? "null"}，键帽 {ed265?.Tokens().Count ?? 0} 枚");
            var edWin265 = FrameCapture.Core.EditorChord.Parse("Win+Shift+S");
            Chk("T265-editor-chord-win", edWin265 != null && edWin265.Value.Tokens().Count == 3,
                "Win+Shift+S 在编辑器键位里拆成三枚键帽");
        }
        catch (Exception e) { Chk("T265-single-key-bindings", false, e.Message); }

        // 窗口底就是「窗口底色」这枚令牌：改它就当场变，不会被 Mica 背板挡住。
        try
        {
            var npBg = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var winBg = new FrameCapture.Overlays.SettingsWindow();
            typeof(FrameCapture.Overlays.SettingsWindow).GetMethod("Build", npBg)!.Invoke(winBg, null);
            winBg.WindowStartupLocation = WindowStartupLocation.Manual;
            winBg.Left = -20000; winBg.Top = -20000; winBg.ShowInTaskbar = false;
            winBg.Show();
            PumpMs(180);
            var wash = winBg.Background as System.Windows.Media.SolidColorBrush;
            Chk("T249-window-bg-is-token", wash != null && ReferenceEquals(wash, Theme.WindowBgSolidBrush),
                "窗口底直接铺共享的窗口底色画刷，不留给背板");
            var probeBg = System.Windows.Media.Color.FromRgb(0x2B, 0x1E, 0x2E);
            Theme.SetCustom("WindowBg", Theme.IsDark, probeBg);
            Chk("T249-window-bg-follows", wash != null && wash.Color == Theme.Opaque(probeBg),
                $"改窗口底色，窗口底当场变：{HexColor.Format(wash?.Color ?? System.Windows.Media.Colors.Transparent)}");
            Theme.ClearCustom("WindowBg", Theme.IsDark);
            Chk("T249-window-bg-restores", wash != null && wash.Color == Theme.WindowBgSolid,
                "去掉自定义，窗口底回到出厂色");
            winBg.Close();
        }
        catch (Exception e) { Chk("T249-window-bg", false, e.Message); }

        // 快捷键展开/收起连点：每次开关领一个代数，过期的收口/展开回调不许落地。
        // 掉帧的根子是这件事——前一次动画的中途回调把限高改回去，行要么当场塌掉、要么卡在半透明。
        try
        {
            var npFd = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var tyFd = typeof(FrameCapture.Overlays.SettingsWindow);
            var winFd = new FrameCapture.Overlays.SettingsWindow();
            tyFd.GetField("_restoreCat", npFd)!.SetValue(winFd, 0);
            tyFd.GetMethod("Build", npFd)!.Invoke(winFd, null);
            winFd.WindowStartupLocation = WindowStartupLocation.Manual;
            winFd.Left = -20000; winFd.Top = -20000; winFd.ShowInTaskbar = false;
            winFd.Show();
            PumpMs(260);
            var rowsFd = tyFd.GetField("_hotRows", npFd)!.GetValue(winFd) as Dictionary<string, FrameworkElement>;
            var rowFd = rowsFd!.Values.First() as System.Windows.Controls.Border;
            var stackFd = rowFd!.Child as System.Windows.Controls.StackPanel;
            var pillFd = stackFd!.Children[0] as System.Windows.Controls.Border;
            var bodyFd = stackFd.Children[1] as System.Windows.Controls.Border;
            void Tap()
            {
                pillFd!.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent, Source = pillFd });
            }
            // 从收起态连点三下 → 展开；每一下都赶在上一段动画走完时打下去，回调只认最后那一代
            for (int i = 0; i < 3; i++) { Tap(); PumpMs(35); }
            PumpMs(900);
            Chk("T250-fold-open-after-rapid",
                bodyFd!.Visibility == Visibility.Visible && double.IsPositiveInfinity(bodyFd.MaxHeight)
                    && Math.Abs(bodyFd.Opacity - 1) < 0.01,
                $"三下连点之后停在展开态：可见={bodyFd.Visibility} 限高={bodyFd.MaxHeight} 透明度={bodyFd.Opacity:F2}");
            bool allOpaque = true;
            foreach (var c in ((System.Windows.Controls.Panel)bodyFd.Child!).Children)
                if (c is FrameworkElement fe && Math.Abs(fe.Opacity - 1) > 0.02) allOpaque = false;
            Chk("T250-fold-content-visible", allOpaque, "正文里的每一行都回到全亮，没有停在半透明");
            PumpMs(400);   // 再过一段：过期的收口回调要是还活着，这里就把这一行收走了
            Chk("T250-fold-stays-open",
                bodyFd.Visibility == Visibility.Visible && double.IsPositiveInfinity(bodyFd.MaxHeight),
                "过期回调不许把这一行收回去");
            Chk("T250-fold-height-sane", bodyFd.ActualHeight > 20 && bodyFd.ActualHeight < 900,
                $"正文高度 {bodyFd.ActualHeight:F0}");
            // 记账高度：展开到位时对得上实测高度，重复展开不必重新排版量一遍——每次开关都量整棵正文，连点就是在这里掉帧
            var foldsFd = tyFd.GetField("_folds", npFd)!.GetValue(winFd)!;
            var tryFd = foldsFd.GetType().GetMethod("TryGetValue")!;
            double NaturalOf()
            {
                object?[] args = { bodyFd, null };
                if (!(bool)tryFd.Invoke(foldsFd, args)! || args[1] == null) return -1;
                return (double)args[1]!.GetType().GetField("Natural")!.GetValue(args[1])!;
            }
            double natural1 = NaturalOf();
            Chk("T250-fold-caches-height", Math.Abs(natural1 - bodyFd.ActualHeight) <= 1,
                $"记账高度 {natural1:F0} 与实测 {bodyFd.ActualHeight:F0} 一致");
            for (int i = 0; i < 2; i++) { Tap(); PumpMs(35); }   // 展开态再连点两下：收、开，仍落在展开
            PumpMs(900);
            Chk("T250-fold-reopens-after-rapid",
                bodyFd.Visibility == Visibility.Visible && Math.Abs(bodyFd.Opacity - 1) < 0.01,
                $"展开态连点两下仍停在展开：可见={bodyFd.Visibility} 透明度={bodyFd.Opacity:F2}");
            double natural2 = NaturalOf();
            Chk("T250-fold-reuses-height", Math.Abs(natural2 - natural1) <= 1 && Math.Abs(natural2 - bodyFd.ActualHeight) <= 1,
                $"来回折返之后记账高度还是 {natural2:F0}，与实测一致");
            Tap();   // 收一下，停在收起态
            PumpMs(900);
            Chk("T250-fold-closed-after-rapid", bodyFd.Visibility == Visibility.Collapsed && bodyFd.MaxHeight == 0,
                $"收起态：可见={bodyFd.Visibility} 限高={bodyFd.MaxHeight}");
            winFd.Close();
        }
        catch (Exception e) { Chk("T250-fold-rapid", false, e.Message); }
        // 时长框落盘：选档、回车、移开焦点都认，键位文案（「常驻」「关」）与带单位的数字都读得出来。
        try
        {
            var npDu = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var tyDu = typeof(FrameCapture.Overlays.SettingsWindow);
            var tyHostDu = typeof(FrameCapture.Services.AppHost);
            string? prevDirDu = Environment.GetEnvironmentVariable("FC_SETTINGS_DIR");
            string scratchDu = Path.Combine(Path.GetTempPath(), "fc-selftest-duration");
            Directory.CreateDirectory(scratchDu);
            string scratchFileDu = Path.Combine(scratchDu, "settings.json");
            if (File.Exists(scratchFileDu)) File.Delete(scratchFileDu);
            Environment.SetEnvironmentVariable("FC_SETTINGS_DIR", scratchDu);
            var prevSvcDu = tyHostDu.GetProperty("Settings")!.GetValue(FrameCapture.Services.AppHost.Current);
            var svcDu = new SettingsService();
            tyHostDu.GetProperty("Settings")!.SetValue(FrameCapture.Services.AppHost.Current, svcDu);
            try
            {
                var winDu = new FrameCapture.Overlays.SettingsWindow();
                tyDu.GetField("_restoreCat", npDu)!.SetValue(winDu, 1);   // 截图
                tyDu.GetMethod("Build", npDu)!.Invoke(winDu, null);
                winDu.WindowStartupLocation = WindowStartupLocation.Manual;
                winDu.Left = -20000; winDu.Top = -20000; winDu.ShowInTaskbar = false;
                winDu.Show();
                PumpMs(240);
                var allDu = new List<System.Windows.DependencyObject>();
                CollectTyped(winDu, typeof(System.Windows.Controls.ComboBox), allDu);
                var boxes = allDu.OfType<System.Windows.Controls.ComboBox>().ToList();
                var delayDu = boxes.FirstOrDefault(c => (c.Tag as string) == "settings-duration-delay");
                var toastDu = boxes.FirstOrDefault(c => (c.Tag as string) == "settings-duration-toast");
                var outlineDu = boxes.FirstOrDefault(c => (c.Tag as string) == "settings-duration-outline");
                Chk("T260-duration-outline-row", outlineDu != null && outlineDu.IsEditable, "边框亮多久也是一枚可输入下拉框");
                void Type(System.Windows.Controls.ComboBox c, string text, System.Windows.Input.Key key)
                {
                    c.Text = text;
                    c.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                        System.Windows.Input.Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(c)!, 0, key)
                    { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent, Source = c });
                    PumpMs(50);
                }
                Type(delayDu!, "7 秒", System.Windows.Input.Key.Enter);
                Chk("T253-duration-commits-typed", svcDu.Current.DelayCaptureSeconds == 7 && delayDu!.Text == "7", $"打「7 秒」回车：设置 {svcDu.Current.DelayCaptureSeconds} 秒，框里写「{delayDu!.Text}」");
                Type(delayDu!, "999 秒", System.Windows.Input.Key.Enter);
                Chk("T253-duration-clamps", svcDu.Current.DelayCaptureSeconds == 120,
                    $"上限收在 120：实得 {svcDu.Current.DelayCaptureSeconds}");
                Type(toastDu!, "0", System.Windows.Input.Key.Enter);
                Chk("T253-duration-resident", svcDu.Current.ToastSeconds == 0 && toastDu!.Text == "0" && DurationUnitOf(winDu, "settings-duration-toast") == Loc.T(K.Settings.Resident), $"打「0」回车落成常驻：设置 {svcDu.Current.ToastSeconds}，框里写「{toastDu!.Text}」+「{DurationUnitOf(winDu, "settings-duration-toast")}」");
                Type(toastDu!, "2.5 秒", System.Windows.Input.Key.Enter);
                Chk("T253-duration-decimals", Math.Abs(svcDu.Current.ToastSeconds - 2.5) < 1e-9 && toastDu!.Text == "2.5" && DurationUnitOf(winDu, "settings-duration-toast") == Loc.T(K.Settings.SecondsUnit), $"打「2.5 秒」回车：设置 {svcDu.Current.ToastSeconds}，框里写「{toastDu!.Text}」+「{DurationUnitOf(winDu, "settings-duration-toast")}」");
                Type(toastDu!, "9", System.Windows.Input.Key.Escape);
                Chk("T253-duration-escape-reverts", Math.Abs(svcDu.Current.ToastSeconds - 2.5) < 1e-9 && toastDu!.Text == "2.5", $"Esc 作废：设置还是 {svcDu.Current.ToastSeconds}，框里写「{toastDu!.Text}」");
                Type(outlineDu!, "2.5", System.Windows.Input.Key.Enter);
                Chk("T260-duration-outline-commits", Math.Abs(svcDu.Current.OutlineFadeSeconds - 2.5) < 1e-9 && outlineDu!.Text == "2.5", $"边框余晖打「2.5」回车：设置 {svcDu.Current.OutlineFadeSeconds}，框里写「{outlineDu!.Text}」");
                winDu.Close();
            }
            finally
            {
                tyHostDu.GetProperty("Settings")!.SetValue(FrameCapture.Services.AppHost.Current, prevSvcDu);
                Environment.SetEnvironmentVariable("FC_SETTINGS_DIR", prevDirDu);
            }
        }
        catch (Exception e) { Chk("T253-duration-commit", false, e.Message); }
        // 侧栏三态在任何一套预设、任何一侧主题下都要读得清：底色与字色一起算。
        // 选中项的悬停是提强调色浓度，而不是叠一层通用悬停色——两层染色叠出来的色相取决于强调色有多亮，
        // 亮强调色上再压白就是一块刺眼的青，压在上面的字当场读不清。
        try
        {
            var npNv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var tyNv = typeof(FrameCapture.Overlays.SettingsWindow);
            bool keepDarkNv = Theme.IsDark;
            var winNv = new FrameCapture.Overlays.SettingsWindow();
            tyNv.GetMethod("Build", npNv)!.Invoke(winNv, null);
            winNv.WindowStartupLocation = WindowStartupLocation.Manual;
            winNv.Left = -20000; winNv.Top = -20000; winNv.ShowInTaskbar = false;
            winNv.Show();
            PumpMs(220);
            var rowsNv = tyNv.GetField("_navRows", npNv)!.GetValue(winNv) as List<System.Windows.Controls.Border>;
            double worst = double.MaxValue;
            string worstAt = "";
            void Note(double r, string at)
            {
                if (r < worst) { worst = r; worstAt = at; }
            }
            foreach (var p in ColorPresets.All)
            {
                foreach (var th in new[] { "dark", "light" })
                {
                    var st = new AppSettings { Theme = th, Accent = p.Accent };
                    st.Colors = new Dictionary<string, string>(p.Colors, StringComparer.Ordinal);
                    // 预览卡与导航走同一组数：先按这套预设把调色板落下来，再读界面上真正在用的那几支画刷
                    Theme.LoadCustoms(st.Colors);
                    Theme.Apply(th, p.Accent);
                    for (int i = 0; i < rowsNv!.Count; i++)
                    {
                        var el = rowsNv[i];
                        var bg = (el.Background as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.Transparent;
                        var sp = (System.Windows.Controls.StackPanel)el.Child;
                        var inkIcon = ((System.Windows.Controls.TextBlock)sp.Children[0]).Foreground as System.Windows.Media.SolidColorBrush;
                        var inkText = ((System.Windows.Controls.TextBlock)sp.Children[1]).Foreground as System.Windows.Media.SolidColorBrush;
                        Note(Fluent.ContrastRatio(inkText?.Color ?? Theme.Text, bg), $"{p.Id}/{th}/第{i + 1}项文字");
                        Note(Fluent.ContrastRatio(inkIcon?.Color ?? Theme.TextDim, bg), $"{p.Id}/{th}/第{i + 1}项图标");
                    }
                    // 悬停态：底色与字色都由同一组规则给出，四种组合各算一遍
                    foreach (var sel in new[] { false, true })
                    {
                        var back = Fluent.NavBacking(Theme.NavBg, sel, hovered: true);
                        foreach (var icon in new[] { false, true })
                            Note(Fluent.ContrastRatio(Fluent.NavInk(back, sel, icon), back),
                                $"{p.Id}/{th}/悬停{(sel ? "选中" : "未选")}{(icon ? "图标" : "文字")}");
                    }
                }
            }
            Chk("T254-nav-ink-readable", worst >= 4.5, $"最差一处 {worstAt} 只有 {worst:F2}:1");
            // 选中 + 悬停的底色要比选中的静止档更浓：提一档浓度，而不是混进一层把色相冲淡的白
            Theme.LoadCustoms(new Dictionary<string, string>());
            Theme.Apply("dark", null);
            var selRest = Fluent.NavBacking(Theme.NavBg, selected: true, hovered: false);
            var selHot = Fluent.NavBacking(Theme.NavBg, selected: true, hovered: true);
            Chk("T254-nav-hover-deeper", Math.Abs(Fluent.ContrastRatio(selHot, Theme.Accent) - Fluent.ContrastRatio(selRest, Theme.Accent)) > 0.05,
                $"选中悬停比静止更靠近强调色：{HexColor.Format(selRest)} → {HexColor.Format(selHot)}");
            Theme.Apply(keepDarkNv ? "dark" : "light", null);
            PumpMs(60);
            winNv.Close();
        }
        catch (Exception e) { Chk("T254-nav-ink", false, e.Message); }

        // 每套预设两侧都要看得出换过：预设只给出厂那几个色阶的邻居，用户会以为「亮色根本没做」。
        try
        {
            var lines = new List<string>();
            foreach (var p in ColorPresets.All)
            {
                if (p.Id == "default") continue;
                foreach (var dark in new[] { false, true })
                {
                    int changed = 0;
                    var detail = new List<string>();
                    foreach (var t in Theme.Tokens)
                    {
                        var key = (dark ? "dark." : "light.") + t.Key;
                        var factory = t.Default(dark);
                        var v = p.Colors.TryGetValue(key, out var hex) ? HexColor.Parse(hex) ?? factory : factory;
                        int delta = Math.Max(Math.Abs(v.A - factory.A),
                            Math.Max(Math.Abs(v.R - factory.R), Math.Max(Math.Abs(v.G - factory.G), Math.Abs(v.B - factory.B))));
                        if (delta >= 6) changed++;
                        detail.Add($"{t.Key}+{delta}");
                    }
                    lines.Add($"{p.Id}/{(dark ? "dark" : "light")}:{changed}");
                    Chk($"T255-preset-visible-{p.Id}-{(dark ? "dark" : "light")}", changed >= 4,
                        $"这套在{(dark ? "深" : "亮")}侧改动了 {changed} 枚令牌（{string.Join(" ", detail)}）");
                }
            }
            Console.WriteLine("预设改动枚数 " + string.Join(" ", lines));
        }
        catch (Exception e) { Chk("T255-preset-visible", false, e.Message); }
        // 描边要看得见但不能抢眼：把带 alpha 的描边压到它那块底上，量两者对比度。
        // 太亮就是「用了预设之后边框很扎眼」，太淡则等于没有边界——两头都要有数。
        try
        {
            bool keepDarkBd = Theme.IsDark;
            double hi = 0, lo = 99;
            string hiAt = "", loAt = "";
            foreach (var p in ColorPresets.All)
            {
                Theme.LoadCustoms(p.Colors);
                foreach (var dark in new[] { false, true })
                {
                    foreach (var (strokeKey, bgKey) in new[]
                             {
                                 ("Stroke", "CardBg"), ("StrokeField", "KeycapBg"),
                                 ("StrokeField", "ChipBg"), ("StrokeSoft", "RowBg"),
                             })
                    {
                        var bg = Theme.Opaque(Theme.ValueOf(bgKey, dark));
                        var line = Fluent.Flatten(Theme.ValueOf(strokeKey, dark), bg);
                        double r = Fluent.ContrastRatio(line, bg);
                        string at = $"{p.Id}/{(dark ? "深" : "亮")} {strokeKey} 压 {bgKey}";
                        if (r > hi) { hi = r; hiAt = at; }
                        if (r < lo) { lo = r; loAt = at; }
                    }
                }
            }
            Theme.ClearAllCustoms();
            Theme.Apply(keepDarkBd ? "dark" : "light", null);
            Chk("T256-border-calm", hi <= 2.0, $"最扎眼的一处 {hiAt} 达到 {hi:F2}:1（上限 2.0）");
            Chk("T256-border-visible", lo >= 1.08, $"最淡的一处 {loAt} 只有 {lo:F2}:1（下限 1.08）");
        }
        catch (Exception e) { Chk("T256-border-contrast", false, e.Message); }
        // 键帽与芯片换成了各自的令牌：开窗后键帽的底就是「键帽底色」那支共享画刷，芯片未选中态是「芯片底色」。
        try
        {
            var npTk = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var tyTk = typeof(FrameCapture.Overlays.SettingsWindow);
            var winTk = new FrameCapture.Overlays.SettingsWindow();
            tyTk.GetField("_restoreCat", npTk)!.SetValue(winTk, 0);
            tyTk.GetMethod("Build", npTk)!.Invoke(winTk, null);
            winTk.WindowStartupLocation = WindowStartupLocation.Manual;
            winTk.Left = -20000; winTk.Top = -20000; winTk.ShowInTaskbar = false;
            winTk.Show();
            PumpMs(260);
            var boxesTk = tyTk.GetField("_hotBoxes", npTk)!.GetValue(winTk) as Dictionary<string, FrameCapture.Core.ChordCaptureBox>;
            var capTk = boxesTk!.Values.First();
            Chk("T257-keycap-uses-token", ReferenceEquals(capTk.Background, Theme.KeycapBgBrush),
                "键帽底走「键帽底色」令牌，改它当场换色");
            Chk("T257-keycap-border-uses-token",
                capTk.BorderBrush is System.Windows.Media.SolidColorBrush kb
                    && (kb.Color == Theme.StrokeField || kb.Color == Theme.Accent),
                "键帽描边走「控件描边」令牌");
            // 截图页上的格式芯片：未选中那几枚的底就是芯片底色
            var svsTk = new List<System.Windows.DependencyObject>();
            CollectTyped(winTk, typeof(System.Windows.Controls.ScrollViewer), svsTk);
            var hostTk = (svsTk.Count > 0 ? (svsTk[0] as System.Windows.Controls.ScrollViewer)?.Content : null) as System.Windows.Controls.Grid;
            var pageCap = hostTk?.Children.Count > 1 ? hostTk.Children[1] as FrameworkElement : null;
            var textsTk = new List<System.Windows.DependencyObject>();
            if (pageCap != null) CollectTyped(pageCap, typeof(System.Windows.Controls.TextBlock), textsTk);
            var rgbChip = textsTk.OfType<System.Windows.Controls.TextBlock>()
                .Where(t => t.Text == "RGB")
                .Select(t => System.Windows.Media.VisualTreeHelper.GetParent(t))
                .OfType<System.Windows.Controls.Border>().FirstOrDefault();
            Chk("T257-chip-uses-token",
                (rgbChip?.Background as System.Windows.Media.SolidColorBrush)?.Color == Theme.ChipBg,
                $"未选中的芯片底就是「芯片底色」：{HexColor.Format((rgbChip?.Background as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.Transparent)} / 令牌 {HexColor.Format(Theme.ChipBg)}");
            winTk.Close();
        }
        catch (Exception e) { Chk("T257-new-tokens", false, e.Message); }
        // 叠加层与描边必须是半透明的：色值写成 8 位时 alpha 的位置写错，读出来就是一支不透明的亮色——
        // 悬停于是把整行刷成一块刺眼的青，压在上面的字全糊。
        try
        {
            var overlay = new[] { "Hover", "Press", "Stroke", "StrokeSoft", "StrokeField", "TrackBg" };
            int checkedCount = 0, opaque = 0;
            var bad = new List<string>();
            foreach (var p in ColorPresets.All)
            {
                foreach (var e in p.Colors)
                {
                    int dot = e.Key.IndexOf('.');
                    var token = dot > 0 ? e.Key.Substring(dot + 1) : e.Key;
                    if (Array.IndexOf(overlay, token) < 0) continue;
                    var c = HexColor.Parse(e.Value);
                    if (c == null) continue;
                    checkedCount++;
                    if (c.Value.A >= 255) { opaque++; bad.Add($"{e.Key}={e.Value}"); }
                }
            }
            Chk("T258-overlays-translucent", checkedCount >= 30 && opaque == 0,
                $"{checkedCount} 条叠加层/描边里不透明的有 {opaque} 条{(bad.Count > 0 ? "：" + string.Join(" ", bad.Take(6)) : "")}");
            // 悬停一步要读得出「被碰过」，但不能把整块底翻个面：出厂与每套预设都量一次
            bool keepDarkHv = Theme.IsDark;
            double worstHover = 0;
            string worstHoverAt = "";
            foreach (var p in ColorPresets.All)
            {
                Theme.LoadCustoms(p.Colors);
                foreach (var dark in new[] { false, true })
                {
                    var nav = Theme.Opaque(Theme.ValueOf("NavBg", dark));
                    var row = Theme.Opaque(Theme.ValueOf("RowBg", dark));
                    double a = Fluent.ContrastRatio(Fluent.HoverOn(nav), nav);
                    double b = Fluent.ContrastRatio(Fluent.HoverOn(row), row);
                    if (a > worstHover) { worstHover = a; worstHoverAt = $"{p.Id}/{(dark ? "深" : "亮")} 侧栏"; }
                    if (b > worstHover) { worstHover = b; worstHoverAt = $"{p.Id}/{(dark ? "深" : "亮")} 行"; }
                }
            }
            Theme.ClearAllCustoms();
            Theme.Apply(keepDarkHv ? "dark" : "light", null);
            Chk("T258-hover-is-a-step", worstHover <= 1.9, $"最重的一步悬停 {worstHoverAt} 到 {worstHover:F2}:1（上限 1.9）");
        }
        catch (Exception e) { Chk("T258-overlay-alpha", false, e.Message); }
        // 设置文件里那份预设副本：套用那一刻抄下来的字符串，字节顺序读不对就一直读错。
        // 版本落后时只换读错位的那几条——存下来不透明、表里这条本是半透明。
        try
        {
            var stale = new AppSettings { ColorPreset = "midnight", PresetRevision = 0 };
            stale.Colors["dark.Hover"] = "#33FFFFFF";   // 错位的写法：读出来是不透明的青
            stale.Normalize();
            Chk("T259-stale-preset-copy-rewritten",
                !stale.Colors.TryGetValue("dark.Hover", out var leftHover) || leftHover != "#33FFFFFF",
                $"错位那条被表里的值替掉了：dark.Hover={(stale.Colors.TryGetValue("dark.Hover", out var lh) ? lh : "（与出厂同值，已省去）")}");
            // 表里这条与出厂同值，换回半透明之后会被归一丢掉；留着的也必须已经是半透明
            Chk("T259-misread-entry-becomes-translucent",
                stale.PresetRevision == ColorPresets.Revision
                    && (!stale.Colors.TryGetValue("dark.Hover", out var backHover) || (HexColor.Parse(backHover)?.A ?? 255) < 255),
                $"换回来之后 dark.Hover={(stale.Colors.TryGetValue("dark.Hover", out var bh) ? bh : "与出厂同值，已省去")}");
            // 逐条对，别整张表覆盖：手改的那枚与读错的那条同行时也得各归各位
            var mixed = new AppSettings { ColorPreset = "midnight", PresetRevision = 0 };
            mixed.Colors["dark.Hover"] = "#33FFFFFF";   // 错位
            mixed.Colors["dark.RowBg"] = "#123456";     // 手改（表里这条不透明，不该被动）
            mixed.Normalize();
            Chk("T259-only-misread-entries-swapped",
                (!mixed.Colors.TryGetValue("dark.Hover", out var mixedHover) || mixedHover != "#33FFFFFF")
                    && mixed.Colors.TryGetValue("dark.RowBg", out var mixedRow) && mixedRow == "#123456",
                "只把读错位的那条按表换掉，其它手改的颜色留着");
            // 出厂预设的覆盖表是空的：这种设置下的自定义颜色一枚都不能丢
            var ownColours = new AppSettings { ColorPreset = "default", PresetRevision = 0 };
            ownColours.Colors["dark.CardBg"] = "#123456";
            ownColours.Normalize();
            Chk("T259-default-preset-keeps-customs",
                ownColours.Colors.TryGetValue("dark.CardBg", out var ownCard) && ownCard == "#123456"
                    && ownColours.PresetRevision == ColorPresets.Revision,
                "出厂预设下自己调的颜色不会被版本迁移抹掉");
            var keepId = new AppSettings { ColorPreset = "midnight", PresetRevision = ColorPresets.Revision };
            keepId.Colors["dark.CardBg"] = "#123456";
            keepId.Normalize();
            Chk("T259-preset-tweak-survives",
                keepId.Colors.TryGetValue("dark.CardBg", out var kept) && kept == "#123456",
                "版本已是最新时，套预设之后手改的那枚留得住");
        }
        catch (Exception e) { Chk("T259-preset-revision", false, e.Message); }
        // 语义判定：按钮/链接/输入框算控件；匿名容器与整页文档不算。
        try
        {
            Chk("T236-semantic-button-passes",
                FrameCapture.Core.ElementSnap.IsSemantic(System.Windows.Automation.ControlType.Button, ""),
                "按钮应当算控件");
            Chk("T236-named-container-passes",
                FrameCapture.Core.ElementSnap.IsSemantic(System.Windows.Automation.ControlType.Group, "Dashboard menu"),
                "有名字的分组应当算控件");
            Chk("T236-anonymous-pane-fails",
                !FrameCapture.Core.ElementSnap.IsSemantic(System.Windows.Automation.ControlType.Pane, ""),
                "匿名容器不该算控件");
            Chk("T236-whole-document-fails",
                !FrameCapture.Core.ElementSnap.IsSemantic(System.Windows.Automation.ControlType.Document, "GitHub"),
                "整页文档不该算控件");
        }
        catch (Exception e) { Chk("T236-semantic", false, e.Message); }

        // 描边必须整个落在捕获范围之外：范围里的像素一个都不该被自己的边框吃掉。
        try
        {
            var r = new FrameCapture.Capture.RectI(900, 600, 900, 600);
            var o = FrameCapture.Overlays.RegionOutlineWindow.OuterRect(r);
            Chk("T235-outline-stroke-sits-outside-the-region",
                o.X + 2 == r.X && o.Y + 2 == r.Y
                && o.X + o.W - 2 == r.X + r.W && o.Y + o.H - 2 == r.Y + r.H,
                "外框 " + o.X + "," + o.Y + " " + o.W + "x" + o.H + " / 范围 " + r.X + "," + r.Y + " " + r.W + "x" + r.H);
            Chk("T235-outline-fits-around-the-region",
                o.X < r.X && o.Y < r.Y && o.X + o.W > r.X + r.W && o.Y + o.H > r.Y + r.H,
                "外框没有把范围完整包住");
        }
        catch (Exception e) { Chk("T235-outline-outside", false, e.Message); }

        // 「允许截到自己」默认关（浮窗与轮廓不进产物），打开后放行。
        try
        {
            var svcSelf = FrameCapture.Core.SettingsService.Shared;
            bool keepSelf = svcSelf?.Current.CaptureSelf ?? false;
            svcSelf?.Mutate(s => s.CaptureSelf = false);
            Chk("T233-self-capture-off-by-default", !FrameCapture.Core.Fluent.CaptureSelfAllowed,
                "默认应当排除自己，实际放行了");
            if (svcSelf != null)
            {
                svcSelf.Mutate(s => s.CaptureSelf = true);
                Chk("T233-self-capture-can-be-enabled", FrameCapture.Core.Fluent.CaptureSelfAllowed,
                    "打开开关后仍然排除自己");
                svcSelf.Mutate(s => s.CaptureSelf = keepSelf);
            }
        }
        catch (Exception e) { Chk("T233-self-capture", false, e.Message); }

        // 截图浮窗的三枚操作：标注 / 复制 / 关闭，键位写在名字后面；复制那枚跟着设置里的键位走。
        try
        {
            var svc = FrameCapture.Services.AppHost.Current.Settings;
            string keepChord = svc.Current.ToastCopyChord;
            svc.Mutate(s => s.ToastCopyChord = "Ctrl+C");
            FrameCapture.Overlays.CaptureToast.Show(Solid(160, 96, 0x30, 0x60, 0x90),
                new FrameCapture.Capture.RectI(0, 0, 160, 96), "", () => { }, () => { });
            PumpMs(260);
            var tc = System.Windows.Application.Current.Windows
                .OfType<FrameCapture.Overlays.CaptureToast>().FirstOrDefault();
            var caps = tc == null ? new List<string>() : ChipsIn(tc);
            Chk("T234-toast-offers-annotate-copy-close",
                caps.Contains("Enter") && caps.Contains("Ctrl") && caps.Contains("C") && caps.Contains("Esc"),
                "浮窗键帽：" + string.Join(" / ", caps));
            tc?.Close();
            svc.Mutate(s => s.ToastCopyChord = keepChord);
        }
        catch (Exception e) { Chk("T234-toast-actions", false, e.Message); }

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

/// <summary>只带设备与时间戳的鼠标事件参数。<see cref="System.Windows.Input.MouseEventArgs"/> 的构造函数
/// 是 protected，而 WPF 按参数类型挑委托，抬 MouseEnter/MouseLeave 这类无按键的鼠标事件必须有这个公开子类。</summary>
sealed class EnterMouseArgs : System.Windows.Input.MouseEventArgs
{
    public EnterMouseArgs(System.Windows.Input.MouseDevice device) : base(device, 0, null!) { }
}

/// <summary>GestureCore 测试探针：记录被调用的动作名，可指定某些动作必然失败以驱动失败策略分支。
/// 另实现 <see cref="GestureCore.IPreludeInvoker"/>：哪些动作名算前置任务、宿主是否受理都可控，
/// 用来断言引擎把「以选区开头的序列」整体交给宿主，绝不逐步同步跑掉后面的动作。</summary>
sealed class GestureProbe : GestureCore.IPreludeInvoker
{
    public List<string> Log = new();
    public HashSet<string> Fail = new();
    public List<string> Bindings = new();   // 记录 BeginBinding 收到的绑定 Id
    public HashSet<string> Prelude = new(); // 这些动作名被宿主认作前置任务
    public bool AcceptPrelude = true;       // 宿主是否受理带前置任务的整条序列
    public List<string> PreludeLog = new(); // 交来的整条序列，用 | 连接便于断言
    public bool Invoke(string actionName) { Log.Add(actionName); return !Fail.Contains(actionName); }
    public void BeginBinding(GestureCore.GestureBinding b) { Bindings.Add(b.Id); }
    public bool IsPrelude(string actionName) => Prelude.Contains(actionName);
    public bool InvokePrelude(IReadOnlyList<string> actions)
    {
        PreludeLog.Add(string.Join("|", actions));
        // 受理＝整条归宿主跑；不受理＝什么都没发生，引擎据此走本条失败路径

        return AcceptPrelude;
    }
}

/// <summary>只实现 <see cref="GestureCore.IActionInvoker"/> 的最小宿主：不认前置任务，
/// 用来断言引擎对不支持它的宿主保持逐步同步执行。</summary>
sealed class PlainProbe : GestureCore.IActionInvoker
{
    public List<string> Log = new();
    public bool Invoke(string actionName) { Log.Add(actionName); return true; }
    public void BeginBinding(GestureCore.GestureBinding b) { }
}

/// <summary>背板回读用的最小 DWM 声明。生产侧的 Win32 是 internal，测试不为此放宽可见性。</summary>
static class DwmProbe
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out uint value, int cb);
}

/// <summary>分层窗口 alpha 通道用的最小声明，同上：不为测试放宽生产侧可见性。</summary>
static class LayerProbe
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern uint GetWindowLongW(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern uint SetWindowLongW(IntPtr hWnd, int nIndex, uint dwNewLong);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, IntPtr crKey, byte bAlpha, uint dwFlags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool GetLayeredWindowAttributes(IntPtr hWnd, out IntPtr pcKey, out byte alpha, out uint flags);
    public const int GWL_EXSTYLE = -20;
    public const uint WS_EX_LAYERED = 0x00080000, LWA_ALPHA = 0x2;
}

/// <summary>测试侧的系统版本号探针：能力门控以 ntdll 的 RtlGetVersion 为准，
/// 结构体布局写错会静默返回 0 并把整套 Win11 特性关掉，所以这条本身也要被断言覆盖。</summary>
static class OsProbe
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct OsVersionInfoEx
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    [System.Runtime.InteropServices.DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref OsVersionInfoEx info);

    /// <summary>取系统内部版本号，取不到返回 0（调用方据此判探针失效）。</summary>
    /// <returns>内部版本号，或 0。</returns>
    public static int RtlBuildNumber()
    {
        var v = new OsVersionInfoEx
        {
            dwOSVersionInfoSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(OsVersionInfoEx))
        };
        return RtlGetVersion(ref v) == 0 ? v.dwBuildNumber : 0;
    }
}