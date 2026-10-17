// 版面与双语审计：逐界面离屏建窗，把两类靠肉眼漏掉的缺陷一次扫出来——
//   裁字：英文比中文长，按中文排的版面在英文下把字挤掉（或压成省略号）；
//   漏翻：英文界面里还印着中文（动态拼出来的文案最容易漏，静态扫描只看源码里的字面串）。
// 用法：SelfTest.exe uxaudit <zh|en> <界面名>，一行一条 UXA|界面|类型|细节，由 scripts/ux-audit.ps1 汇总。

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    /// <summary>审计模式入口：按语言与界面名建一个窗口，扫完就关。</summary>
    /// <param name="lang">语言覆盖：<c>zh</c> 或 <c>en</c>。</param>
    /// <param name="surface">界面名，见 <see cref="Surfaces"/>。</param>
    /// <returns>发现的缺陷条数（供驱动脚本汇总，非进程失败）。</returns>
    internal static int UxAudit(string lang, string surface)
    {
        var app = new Application();
        var rows = new List<string>();
        // 建窗与关窗路径抛出来的异常按发现记录：关不掉的窗、销毁回调里炸的 NRE，用户都碰得到，
        // 让它们把进程带走反而什么都看不到。
        app.DispatcherUnhandledException += (_, e) =>
        {
            rows.Add($"UXA|{surface}|抛异常|{e.Exception.GetType().Name}: {Cut(e.Exception.Message, 70)}");
            e.Handled = true;
        };
        var svc = new SettingsService();
        typeof(AppHost).GetProperty("Settings")!
            .SetValue(AppHost.Current, svc);
        // 版面审计与配色无关，钉住暗色让两次运行完全可比
        Theme.Apply("dark", svc.Current.Accent);
        Loc.Apply(lang == "en" ? Loc.PrefEn : Loc.PrefZh);
        Fluent.ApplyGlobalStyles();
        // 用户自己写下的字（绑定名这类）不算漏翻：那是他打的字，语言切换不动它。
        // 版面照着他的长名字压——名字越长越容易挤，那部分照查。
        _userData = svc.Current.Gestures.Bindings
            .Where(b => !string.IsNullOrWhiteSpace(b.Name))
            .Select(b => b.Name.Trim())
            .ToHashSet();

        try
        {
            switch (surface)
            {
                case "settings": BuildSettingsAudit(app, rows, "settings", -1); break;
                case "settings-open": BuildSettingsAudit(app, rows, "settings-open", 0); break;
                case "settings-adv": BuildSettingsAudit(app, rows, "settings-adv", 0, advanced: true); break;
                case "settings-clash": BuildSettingsAudit(app, rows, "settings-clash", 0, clash: true); break;
            // 拖到最小尺寸那一档：MinWidth/MinHeight 是用户能压到的极限，长说明文字在这一档最容易挤。
            case "settings-narrow": BuildSettingsAudit(app, rows, "settings-narrow", 0, forceW: 680, forceH: 460); break;
            // 深层目录 + 最小宽度：路径行是最容易被长内容打穿的一处，默认那条短路径量不到
            case "settings-longpath": BuildSettingsAudit(app, rows, "settings-longpath", -1, forceW: 680, forceH: 460,
                savePath: @"D:\Users\lin\Pictures\截图归档\2026\十月\产品走查与录屏\FrameCapture"); break;
                case "help": BuildHelpAudit(rows); break;
                case "editor": BuildEditorAudit(app, rows, 3); break;
                case "editor-still": BuildEditorAudit(app, rows, 1); break;
                case "pin": BuildPinAudit(app, rows); break;
            case "pin-menu": BuildPinMenuAudit(app, rows); break;
                case "ocr": BuildOcrAudit(app, rows, message: false); break;
                case "ocr-msg": BuildOcrAudit(app, rows, message: true); break;
                case "notice": BuildNoticeAudit(app, rows); break;
                case "welcome": BuildWelcomeAudit(app, rows); break;
                case "toast": BuildToastAudit(app, rows); break;
                case "countdown": BuildCountdownAudit(app, rows); break;
                case "scroll": BuildScrollAudit(app, rows); break;
                case "filmstrip": BuildFilmStripAudit(app, rows); break;
        case "picker": BuildPickerAudit(rows, loupe: false); break;
        case "picker-loupe": BuildPickerAudit(rows, loupe: true); break;
                default: Console.WriteLine($"UXA|{surface}|未知界面|"); return 0;
            }
        }
        catch (Exception ex)
        {
            rows.Add($"UXA|{surface}|建窗失败|{ex.GetType().Name}: {Cut(ex.Message, 90)}");
        }
        foreach (var r in rows) Console.WriteLine(r);
        // 扫到的文字条数：0 条发现 + 0 条文字 = 探针没看到东西，不是界面干净
        Console.WriteLine($"UXS|{surface}|{_scanned} 条文字");
        return rows.Count;
    }

    /// <summary>可审计的界面名，驱动脚本按这份清单跑满矩阵。</summary>
    internal static readonly string[] Surfaces =
    {
        "settings", "settings-open", "settings-adv", "settings-clash", "settings-narrow", "settings-longpath",
        "help", "editor", "editor-still", "pin", "pin-menu", "ocr", "ocr-msg",
        "notice", "welcome", "toast", "countdown", "scroll", "filmstrip",
        "picker", "picker-loupe",
    };

    // ---- 逐界面建窗 ----

    /// <summary>设置窗：折叠态、展开第 0 条、高级形态、撞车描边四种形态各扫一遍。</summary>
    static void BuildSettingsAudit(Application app, List<string> rows, string surface, int openAt, bool advanced = false, bool clash = false,
        double? forceW = null, double? forceH = null, string? savePath = null)
    {
        var svc = AppHost.Current.Settings!.Current;
        if (savePath != null) svc.AutoSavePath = savePath;
        if (clash && svc.Gestures.Bindings.Count > 0)
        {
            var first = svc.Gestures.Bindings[0];
            svc.Gestures.Bindings.Add(new GestureBindingDto
            {
                Id = "uxa.clash", Chord = first.Chord, Trigger = first.Trigger,
                Actions = new[] { "ocr.selection" },
            });
        }
        var w = new SettingsWindow();
        if (advanced)
            typeof(SettingsWindow).GetField("_hotAdvanced",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(w, true);
        typeof(SettingsWindow).GetMethod("Build",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(w, null);
        ShowOffscreen(w);
        var root = (UIElement)w.Content;
        LayoutTo(root, forceW ?? w.Width, forceH ?? w.Height);
        // 快捷键页的展开行要点开才有内容：折叠态与展开态是两套版面
        if (openAt >= 0)
        {
            var boxes = new List<DependencyObject>();
            CollectTyped(root, typeof(ChordCaptureBox), boxes);
            if (openAt < boxes.Count)
            {
                var row = VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(boxes[openAt]));
                if (row is UIElement re)
                {
                    re.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                        System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0,
                        System.Windows.Input.Key.Enter)
                    { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent, Source = re });
                    PumpMs(300);
                }
            }
        }
        LayoutTo(root, w.Width, w.Height);
        ScanTree(rows, surface, root);
        w.Close();
    }

    /// <summary>帮助窗：九步逐步建窗扫描，每一步的版式都不一样。</summary>
    static void BuildHelpAudit(List<string> rows)
    {
        var f = typeof(HelpWindow).GetField("_lastStep",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        for (int step = 0; step < 9; step++)
        {
            f.SetValue(null, step);
            var w = HelpWindow.CreateForCapture();
            var root = (UIElement)w.Content;
            LayoutTo(root, w.Width, w.Height);
            ScanTree(rows, $"help{step}", root);
        }
    }

    /// <summary>编辑器：多帧（带播放条）与单帧（只有绘制条）两套版面。</summary>
    static void BuildEditorAudit(Application app, List<string> rows, int frameCount)
    {
        var svc = AppHost.Current.Settings!;
        const int fw = 900, fh = 520;
        var frames = new List<CapturedFrame>();
        for (int i = 0; i < frameCount; i++)
            frames.Add(new CapturedFrame { Bgra = MockScreen(fw, fh), W = fw, H = fh, TimeMs = i * 33.3 });
        var seq = new CapturedSequence
        {
            Frames = frames,
            Region = new RectI(0, 0, fw, fh),
            MonitorBounds = new RectI(0, 0, 1920, 1080),
            Fps = 30,
        };
        var w = new EditorWindow(svc, seq);
        ShowOffscreen(w);
        PumpMs(250);
        ScanTree(rows, "editor", (UIElement)w.Content);
        w.Close();
    }

    /// <summary>贴图：含右键菜单之外的常驻版面（贴图本体 + 浮层提示条）。</summary>
    static void BuildPinAudit(Application app, List<string> rows)
    {
        const int pw = 640, ph = 400;
        var frame = new CapturedFrame { Bgra = MockScreen(pw, ph), W = pw, H = ph, TimeMs = 0 };
        var w = new PinWindow(frame, new RectI(240, 180, pw, ph), false);
        ShowOffscreen(w);
        PumpMs(250);
        ScanTree(rows, "pin", (UIElement)w.Content);
        w.Close();
    }

    /// <summary>贴图右键菜单：菜单开在独立 Popup 里，主窗视觉树与桌面截图都扫不到它，只能单独叫出来量。</summary>
    static void BuildPinMenuAudit(Application app, List<string> rows)
    {
        const int pw = 480, ph = 300;
        var frame = new CapturedFrame { Bgra = MockScreen(pw, ph), W = pw, H = ph, TimeMs = 0 };
        var w = new PinWindow(frame, new RectI(200, 160, pw, ph), false);
        ShowOffscreen(w);
        PumpMs(200);
        var menu = w.BuildMenu();
        menu.PlacementTarget = w;
        // 不叫 Popup，直接量菜单本体：审计的窗口摆在屏幕外，Popup 一开就被虚拟桌面边界夹住，
        // 每个条目的键位列都被裁掉 10px——那是取证装置在裁字，不是产品在裁字。
        // 按无限宽度 Measure/Arrange 出来的才是菜单自己想要的尺寸。
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();
        PumpMs(150);
        ScanTree(rows, "pin-menu", menu);
        // 扫不到字就是探针自己在说谎：菜单没 realize 时它一条也不会报，界面却根本没被量过。
        int texts = CountTexts(menu);
        if (texts < 6) rows.Add($"UXA|pin-menu|空扫|菜单里只量到 {texts} 条文字，这一项没真正跑到");
        w.Close();
    }

    /// <summary>数这棵子树里有多少条可见文字，用来判断某个界面是不是被空扫了。</summary>
    static int CountTexts(DependencyObject root)
    {
        int n = 0;
        var c = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < c; i++)
        {
            var ch = VisualTreeHelper.GetChild(root, i);
            if (ch is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text) && tb.Visibility == Visibility.Visible) n++;
            n += CountTexts(ch);
        }
        return n;
    }

    /// <summary>OCR 结果窗的两种形态：可编辑结果与单条消息。</summary>
    static void BuildOcrAudit(Application app, List<string> rows, bool message)
    {
        if (message) OcrResultWindow.ShowMessage(Loc.T(K.Capture.NoOcrLanguagePack));
        else OcrResultWindow.Show("识别结果示例\nLine of english text for width probing\n第三行混排 text");
        var w = LastWindow(app);
        if (w == null) { rows.Add("UXA|ocr|建窗失败|没拿到窗口"); return; }
        PumpMs(250);
        ScanTree(rows, message ? "ocr-msg" : "ocr", (UIElement)w.Content);
        w.Close();
    }

    /// <summary>通用提示浮层。</summary>
    static void BuildNoticeAudit(Application app, List<string> rows)
    {
        // 样例取启动气泡那一条：它是这层最长的真实文案，也是唯一带两处键位的。
        // 键位串里有 +，宽度一紧 WPF 就在 + 上找断点，把 Ctrl+Alt+R 劈成两截——
        // 折行本身不算缺陷，键位被劈开才算，所以这里连副行高度一起看。
        NoticeToast.Show(Loc.T(K.Tray.FramecaptureIsRunning),
            Loc.F(K.Tray.WaitingInTheTrayCapture, "Ctrl+Alt+A", "Ctrl+Alt+R"));
        var w = LastWindow(app);
        if (w == null) { rows.Add("UXA|notice|建窗失败|没拿到窗口"); return; }
        PumpMs(200);
        ScanTree(rows, "notice", (UIElement)w.Content);
        var detail = FindByFontSize(w, 11);
        if (detail != null && detail.ActualHeight > detail.FontSize * 1.6)
            rows.Add($"UXA|notice|折行|副行 {detail.ActualHeight:F0}px 两行高，键位会被 + 劈开：{detail.Text}");
        w.Close();
    }

    /// <summary>按字号找那一条文字：版面审查里字号就是角色的区分（正文 13、副行 11）。</summary>
    static TextBlock? FindByFontSize(DependencyObject root, double size)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is TextBlock tb && Math.Abs(tb.FontSize - size) < 0.1) return tb;
            var hit = FindByFontSize(c, size);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>首启欢迎卡：新用户看到的第一屏。</summary>
    static void BuildWelcomeAudit(Application app, List<string> rows)
    {
        WelcomeToast.Show("Ctrl+Shift+G", "Ctrl+Shift+R");
        var w = LastWindow(app);
        if (w == null) { rows.Add("UXA|welcome|建窗失败|没拿到窗口"); return; }
        PumpMs(200);
        ScanTree(rows, "welcome", (UIElement)w.Content);
        w.Close();
    }

    /// <summary>截图后的确认浮窗。</summary>
    static void BuildToastAudit(Application app, List<string> rows)
    {
        var frame = new CapturedFrame { Bgra = MockScreen(480, 300), W = 480, H = 300, TimeMs = 0 };
        CaptureToast.Show(frame, new RectI(100, 80, 480, 300), @"\\.\DISPLAY1", () => { });
        var w = LastWindow(app);
        if (w == null) { rows.Add("UXA|toast|建窗失败|没拿到窗口"); return; }
        PumpMs(200);
        ScanTree(rows, "toast", (UIElement)w.Content);
        w.Close();
    }

    /// <summary>延时截图倒计时浮窗。</summary>
    static void BuildCountdownAudit(Application app, List<string> rows)
    {
        var w = new CountdownWindow("1560,260 940×560", @"\\.\DISPLAY1");
        ShowOffscreen(w);
        PumpMs(250);
        ScanTree(rows, "countdown", (UIElement)w.Content);
        w.Close();
    }

    /// <summary>滚动截图的三层浮层：取景层、预览卡、控制条。</summary>
    static void BuildScrollAudit(Application app, List<string> rows)
    {
        var reg = new RectI(600, 200, 900, 700);
        var hint = new ScrollHintWindow(reg, @"\\.\DISPLAY1");
        ShowOffscreen(hint);
        var stage = new ScrollStageWindow(reg, @"\\.\DISPLAY1");
        ShowOffscreen(stage);
        var prev = new ScrollPreviewWindow(reg, @"\\.\DISPLAY1");
        ShowOffscreen(prev);
        PumpMs(250);
        ScanTree(rows, "scroll-hint", (UIElement)hint.Content);
        ScanTree(rows, "scroll-preview", (UIElement)prev.Content);
        hint.Close(); stage.Close(); prev.Close();
    }

    /// <summary>选区浮层：工具条只在有选区时出现，所以带一个已存在的选区进去建窗。</summary>
    /// <param name="rows">发现汇总，逐条追加。</param>
    /// <param name="loupe">true = 放大镜形态（取色读数、色值格式那一叠），false = 普通框选形态。</param>
    static void BuildPickerAudit(List<string> rows, bool loupe)
    {
        var mon = new MonitorInfo(@"\\.\DISPLAY1", new RectI(0, 0, 1600, 900), true);
        var w = new RegionPickerWindow(mon, new RectI(320, 200, 560, 380), magnifier: loupe);
        ShowOffscreen(w);
        PumpMs(250);
        ScanTree(rows, loupe ? "picker-loupe" : "picker", (UIElement)w.Content);
        w.Close();
    }

    /// <summary>胶片条参数对话框。</summary>
    static void BuildFilmStripAudit(Application app, List<string> rows)
    {
        var owner = new Window { Width = 200, Height = 120, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        owner.Show();
        // 预览回调返回 null = 这一档画不出来，对话框留空；审计只要版面，不要缩略图
        var w = new FilmStripDialog(owner, 240, 12, _ => default);
        w.Show();
        PumpMs(250);
        ScanTree(rows, "filmstrip", (UIElement)w.Content);
        w.Close(); owner.Close();
    }

    // ---- 扫描 ----

    /// <summary>取最近显示的那个窗口：浮层类的 Show 工厂不返回实例，只能从应用的窗口表里拿。</summary>
    static Window? LastWindow(Application app)
    {
        Window? last = null;
        foreach (Window w in app.Windows) last = w;
        return last;
    }

    /// <summary>把窗口挪到屏幕外再显示：布局按真实尺寸算，屏幕上看不到。</summary>
    static void ShowOffscreen(Window w)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Show();
        w.Left = -20000; w.Top = -20000;
        w.Opacity = 1;
        PumpMs(120);
    }

    static void LayoutTo(UIElement root, double w, double h)
    {
        if (double.IsNaN(w) || double.IsNaN(h)) { root.UpdateLayout(); return; }
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
    }

    /// <summary>
    /// 同一菜单里的条目大小写必须一致：本项目菜单走 Title Case（Set Capture Region、Run As Administrator）。
    /// 混排最伤观感——「Recognize Text」旁边站着「Refresh content」。
    /// 单词条目（Copy / Close）两种写法同形，不参与判定；占位符、括号与标点先剥掉。
    /// </summary>
    static void CheckMenuCase(List<string> rows, string surface, DependencyObject root)
    {
        var items = new List<DependencyObject>();
        CollectTyped(root, typeof(MenuItem), items);
        var title = new List<string>();
        var sentence = new List<string>();
        foreach (var o in items)
        {
            if (o is not MenuItem mi || mi.Header is not string h) continue;
            var clean = System.Text.RegularExpressions.Regex
                .Replace(h, @"\{[0-9]+\}|\(.*?\)|[^\w\s'-]", " ").Trim();
            var words = clean.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || !words.All(w => Regex_Latin(w[0]))) continue;
            if (words.All(w => char.IsUpper(w[0]))) title.Add(h);
            else if (char.IsUpper(words[0][0]) && words.Skip(1).All(w => char.IsLower(w[0]))) sentence.Add(h);
        }
        if (title.Count > 0 && sentence.Count > 0)
            rows.Add($"UXA|{surface}|大小写|同菜单混排：Title Case「{Cut(title[0], 26)}」与句首大写「{Cut(sentence[0], 26)}」");
    }

    /// <summary>只有拉丁字母才谈得上大小写。</summary>
    static bool Regex_Latin(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    /// <summary>
    /// 命中区尺寸：手型光标标出来的就是「给人点的」，短边不足 28 逻辑像素很容易点偏。
    /// `Cursor` 在 WPF 里是继承属性，可点行里的每个文字都会带上手型——只判最外层那一个，
    /// 内层继承来的不算，否则一条快捷键行能报十几条假阳性。
    /// </summary>
    static void CheckHitTargets(List<string> rows, string surface, DependencyObject root)
    {
        var els = new List<DependencyObject>();
        CollectTyped(root, typeof(FrameworkElement), els);
        foreach (var o in els)
        {
            if (o is not FrameworkElement fe) continue;
            if (fe.Cursor != System.Windows.Input.Cursors.Hand) continue;
            if (HandAncestor(fe, root)) continue;
            double w = fe.ActualWidth, h = fe.ActualHeight;
            if (w <= 0 || h <= 0) continue;
            // 24 逻辑像素是 WCAG 2.5.8 的目标尺寸下限，也是 Windows 11 触控的最小档；
            // 再往上抬就成了自造标准，会把 27.6 这种本来合格的浮窗按钮报成缺陷。
            if (Math.Min(w, h) >= 24) continue;
            rows.Add($"UXA|{surface}|命中区|{At(fe, root)} {fe.GetType().Name}「{Cut(FirstText(fe), 22)}」{w:F0}×{h:F0}");
        }
    }

    /// <summary>取这棵子树里第一条文字，让命中区的报告说得出它是哪个按钮。</summary>
    /// <param name="root">命中区根元素。</param>
    /// <returns>第一条非空文字，没有则空串。</returns>
    static string FirstText(DependencyObject root)
    {
        var list = new List<DependencyObject>();
        CollectTyped(root, typeof(TextBlock), list);
        foreach (var o in list)
            if (o is TextBlock t && !string.IsNullOrWhiteSpace(t.Text)) return t.Text.Trim();
        return "";
    }

    /// <summary>往上找有没有同样带手型光标的祖先：命中区是祖先，这个元素只是继承来的。</summary>
    /// <param name="fe">待判元素。</param>
    /// <param name="root">本次扫描的根，到根为止。</param>
    /// <returns>祖先里出现过手型光标则为真。</returns>
    static bool HandAncestor(FrameworkElement fe, DependencyObject root)
    {
        var p = VisualTreeHelper.GetParent(fe);
        while (p != null && !ReferenceEquals(p, root))
        {
            if (p is FrameworkElement pe && pe.Cursor == System.Windows.Input.Cursors.Hand) return true;
            p = VisualTreeHelper.GetParent(p);
        }
        return false;
    }

    /// <summary>扫一棵已布局好的子树，报出挤掉的文字与英文界面里的中文。</summary>
    static void ScanTree(List<string> rows, string surface, DependencyObject root)
    {
        var found = new List<DependencyObject>();
        CollectTyped(root, typeof(TextBlock), found);
        _scanned += found.Count;
        CheckMenuCase(rows, surface, root);
        CheckHitTargets(rows, surface, root);
        foreach (var o in found)
        {
            if (o is not TextBlock tb) continue;
            string text = tb.Text ?? "";
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (Loc.IsEnglish && HasCjk(text) && !AllowedCjkInEnglish(text) && !LooksLikePath(text) && !_userData.Contains(text.Trim()))
                rows.Add($"UXA|{surface}|漏翻|{At(tb, root)} {Cut(text, 60)}");
            // 图标字形按 em 框量，比它占的格子宽是常态；键帽单字符居中溢出三五个像素也不伤人。
            // 两类都不报，否则一次审计几十条假阳性，真的那条就淹了。
            if (text.Trim().Length < 2 || AllGlyph(text)) continue;

            double avail = tb.ActualWidth, got = tb.ActualHeight;
            if (avail <= 0 || got <= 0) continue;
            if (tb.TextWrapping == TextWrapping.NoWrap)
            {
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double need = tb.DesiredSize.Width;
                // 只有「真的会被切掉」或「被压成省略号」才算缺陷：WPF 默认不裁剪，
                // 排得比想要的窄只是往右边空处溢出，看着一点问题没有（设置窗左侧导航就是这样误报的）。
                bool ellipsized = tb.TextTrimming != TextTrimming.None;
                if (need - avail >= 4 && (ellipsized || ClippedAncestor(tb)) && !InScroller(tb, horizontal: true))
                    rows.Add($"UXA|{surface}|{(ellipsized ? "省略号" : "裁字")}|{At(tb, root)} {avail:F0}→{need:F0} {Cut(text, 46)}");
            }
            else
            {
                tb.Measure(new Size(avail, double.PositiveInfinity));
                double need = tb.DesiredSize.Height;
                // 少掉一整行（≥6px）且祖先会裁剪才算：差三五个像素是行距取整，不是把字吃掉
                if (need - got >= 6 && ClippedAncestor(tb) && !InScroller(tb, horizontal: false))
                    rows.Add($"UXA|{surface}|裁行|{At(tb, root)} 高{got:F0}→{need:F0} {Cut(text, 46)}");
            }
        }
        OverlapPass(rows, surface, root, found);
        (root as UIElement)?.UpdateLayout();
    }

    /// <summary>两段文字画在同一块地方：写死宽度的标签溢出后压在邻居身上，邻居再把它盖掉。</summary>
    /// <remarks>
    /// 这类缺陷「裁字」那条查不出来：WPF 不裁，DesiredSize 也被自己那格的 Width 夹住，量不出溢出——
    /// 唯一诚实的判据是「文字真正要多宽」和「邻居从哪儿开始」。
    /// 欢迎卡的「截图」列写死 40px，英文 Capture 的尾字母正是这么被键位吃掉的。
    /// </remarks>
    /// <param name="rows">发现汇总，逐条追加。</param>
    /// <param name="surface">界面名，进报告前缀。</param>
    /// <param name="root">这棵子树的根，坐标以它为原点。</param>
    /// <param name="found">已经收集好的 TextBlock 列表。</param>
    static void OverlapPass(List<string> rows, string surface, DependencyObject root, List<DependencyObject> found)
    {
        if (root is not Visual visRoot) return;
        var boxes = new List<(Rect R, string Text, TextBlock Tb)>();
        foreach (var o in found)
        {
            if (o is not TextBlock tb) continue;
            string text = tb.Text ?? "";
            if (string.IsNullOrWhiteSpace(text) || tb.ActualWidth <= 0 || tb.ActualHeight <= 0) continue;
            if (AllGlyph(text)) continue;   // 图标字形按 em 框量，本来就比格子宽，不算压到谁
            if (!tb.IsVisible) continue;
            var p = tb.TransformToVisual(visRoot).Transform(new Point(0, 0));
            boxes.Add((new Rect(p.X, p.Y, Math.Max(NaturalWidth(tb), tb.ActualWidth), tb.ActualHeight), text, tb));
        }
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i].R;
                var b = boxes[j].R;
                double ox = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
                double oy = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
                if (ox < 4 || oy < Math.Min(a.Height, b.Height) * 0.5) continue;
                // 同一段字在同一个位置出现两次：模板里 ContentPresenter 套 TextBlock 的常见形状，不是叠字
                if (boxes[i].Text == boxes[j].Text
                    && ox >= Math.Min(a.Width, b.Width) - 2 && oy >= Math.Min(a.Height, b.Height) - 2) continue;
                rows.Add($"UXA|{surface}|叠字|{At(boxes[i].Tb, root)} {Cut(boxes[i].Text, 22)} × {Cut(boxes[j].Text, 22)} 压住{ox:F0}px");
            }
    }

    /// <summary>这段文字不受外层格子限制时真正要多宽：照抄字体属性另起一个 TextBlock 量。</summary>
    /// <param name="tb">要量的文字块。</param>
    /// <returns>单行不换行时的自然宽度（DIP）。</returns>
    static double NaturalWidth(TextBlock tb)
    {
        var probe = new TextBlock
        {
            Text = tb.Text,
            FontSize = tb.FontSize,
            FontFamily = tb.FontFamily,
            FontWeight = tb.FontWeight,
            FontStyle = tb.FontStyle,
            FontStretch = tb.FontStretch,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    /// <summary>祖先里有没有会把溢出内容真切掉的东西：ClipToBounds、显式 Clip，或滚动容器。</summary>
    static bool ClippedAncestor(DependencyObject node)
    {
        if (node is FrameworkElement self && self.ClipToBounds) return true;
        for (var p = VisualTreeHelper.GetParent(node); p != null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ScrollViewer) return true;   // 滚动容器按视口裁，溢出看不见
            if (p is FrameworkElement fe && (fe.ClipToBounds || fe.Clip != null)) return true;
            if (p is Window) return false;        // 到窗口为止：窗外的溢出属于「画到窗外」，另说
        }
        return false;
    }

    /// <summary>本次进程扫到的文字条数，用来判断「0 条发现」是真干净还是根本没扫到东西。</summary>
    static int _scanned;

    /// <summary>用户自己写下的字：漏翻检查跳过，版面挤压照查。</summary>
    static HashSet<string> _userData = new();

    /// <summary>整串都是私有区图标字形（Segoe Fluent Icons）。</summary>
    static bool AllGlyph(string s)
    {
        foreach (char c in s.Trim())
            if (c < 0xE000 || c > 0xF8FF) return false;
        return true;
    }

    /// <summary>祖先里有能横向（或纵向）滚动的 ScrollViewer 时，超出是设计而非缺陷。</summary>
    static bool InScroller(DependencyObject node, bool horizontal)
    {
        for (var p = VisualTreeHelper.GetParent(node); p != null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ScrollViewer sv)
            {
                var v = horizontal ? sv.HorizontalScrollBarVisibility : sv.VerticalScrollBarVisibility;
                if (v != ScrollBarVisibility.Disabled) return true;
            }
        }
        return false;
    }

    /// <summary>含中日韩字符（含全角标点）即算中文残留。</summary>
    static bool HasCjk(string s)
    {
        foreach (char c in s)
            if (c >= 0x3000 && c <= 0x9FFF || c >= 0xFF00 && c <= 0xFFEF) return true;
        return false;
    }

    /// <summary>英文界面里合法保留的中文：语言名按本语言自称是约定，不是漏翻。</summary>
    static bool AllowedCjkInEnglish(string s) => s.Trim() == "中文";

    /// <summary>
    /// 带盘符根或 UNC 根的字是文件系统路径：那是用户机器上的目录名，不是待译文案。
    /// 保存位置那行显示的就是它，中文目录名在英文界面里出现是正常的。
    /// </summary>
    /// <param name="s">待判文字。</param>
    /// <returns>像路径则为真。</returns>
    static bool LooksLikePath(string s) =>
        System.Text.RegularExpressions.Regex.IsMatch(s, @"^\\\\[^\\]+\\|^[A-Za-z]:[\\/]");

    /// <summary>元素在窗口内的落点：把一条发现对回截图上的位置，不靠猜。</summary>
    static string At(Visual el, DependencyObject root)
    {
        try
        {
            var p = el.TransformToVisual(root as Visual).Transform(new Point(0, 0));
            return $"@{p.X:F0},{p.Y:F0}";
        }
        catch { return "@?"; }
    }

    static string Cut(string s, int n)
    {
        s = s.Replace("\r", " ").Replace("\n", " ⏎ ").Trim();
        return s.Length <= n ? s : s[..n] + "…";
    }
}
