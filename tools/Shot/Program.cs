// 自动化助手
//   Shot.exe shot <out.png>
//   Shot.exe title <text>                → 找窗口，打印 hwnd（找不到打 0）
//   Shot.exe commit <text> [vk]          → 找窗口+置前台+发送键（默认 Enter），重试 10×300ms
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing;
using System.Drawing.Imaging;

[DllImport("user32.dll")] static extern bool SetProcessDPIAware();
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindowW(string? cls, string title);
[DllImport("user32.dll")] static extern bool SetForegroundWindow(nint h);
[DllImport("user32.dll")] static extern bool BringWindowToTop(nint h);
[DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
[DllImport("user32.dll")] static extern bool AttachThreadInput(int a, int b, bool attach);
[DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, nint extra);
[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint h, out int pid);
[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] static extern uint MapVirtualKeyW(uint vk, uint mapType);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostMessageW(nint h, uint msg, uint wParam, uint lParam);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);
[DllImport("user32.dll")] static extern bool GetWindowRect(nint h, out RECT r);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(nint h, System.Text.StringBuilder buf, int max);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(nint h, System.Text.StringBuilder buf, int max);
[DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nint extra);
const uint MOUSEEVENTF_LEFTDOWN = 0x02, MOUSEEVENTF_LEFTUP = 0x04;

SetProcessDPIAware();
uint Vk(string s) => s.StartsWith("0x") ? Convert.ToUInt32(s, 16) : uint.Parse(s);
string mode = args.Length > 0 ? args[0] : "shot";
switch (mode)
{
    case "shot":
    {
        string outPath = args.Length > 1 ? args[1] : @"D:\Backup\Capture\shot.png";
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        bmp.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"shot {outPath} {bounds.Width}x{bounds.Height}");
        break;
    }
    case "title":
    {
        nint h = FindWindowW(null, args[1]);
        Console.WriteLine($"hwnd={(long)h}");
        break;
    }
    case "activate": // activate <标题> —— 仅置前台（焦点安全库用），不发送任何键
    {
        nint h = FindWindowW(null, args[1]);
        if (h == 0) { Console.WriteLine("window-not-found"); return; }
        // 前台锁：后台进程 SetForegroundWindow 会被拒。AttachThreadInput 把本线程输入队列
        // 挂到当前前台窗线程上，借它的"前台权"完成置前台（不发送任何键，不碰用户窗口状态）
        nint fg = GetForegroundWindow();
        int curTid = (int)GetCurrentThreadId();
        int fgTid = (int)GetWindowThreadProcessId(fg, out _);
        int selfTid = (int)GetWindowThreadProcessId(h, out _);
        bool a1 = AttachThreadInput(curTid, fgTid, true);
        bool a2 = AttachThreadInput(curTid, selfTid, true);
        bool ok = SetForegroundWindow(h);
        BringWindowToTop(h);
        if (a1) AttachThreadInput(curTid, fgTid, false);
        if (a2) AttachThreadInput(curTid, selfTid, false);
        await Task.Delay(150);
        int pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        Console.WriteLine($"activate {(long)h} setfg={ok} nowpid={pid}");
        break;
    }
    case "commit":
    {
        byte vk = args.Length > 2 ? (byte)Vk(args[2]) : (byte)13;
        for (int i = 0; i < 10; i++)
        {
            nint h = FindWindowW(null, args[1]);
            if (h != 0)
            {
                bool fg = SetForegroundWindow(h);
                int pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                await Task.Delay(400);
                keybd_event(vk, 0, 0, 0);
                await Task.Delay(60);
                keybd_event(vk, 0, 2, 0);
                Console.WriteLine($"sent vk={vk} hwnd={(long)h} setfg={fg} nowfgpid={pid}");
                return;
            }
            await Task.Delay(300);
        }
        Console.WriteLine("window-not-found");
        break;
    }
    case "keyw": // keyw <title> <vk> → PostMessage 直达窗口，绕过输入法
    {
        nint h = FindWindowW(null, args[1]);
        if (h == 0) { Console.WriteLine("window-not-found"); return; }
        uint vk = Vk(args[2]);
        uint scan = MapVirtualKeyW(vk, 0);
        const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
        PostMessageW(h, WM_KEYDOWN, vk, scan | 1 << 24);
        await Task.Delay(60);
        PostMessageW(h, WM_KEYUP, vk, scan | (1u << 24) | (1u << 30) | (1u << 31));
        Console.WriteLine($"keyw {vk} posted to {(long)h}");
        break;
    }
    case "ctrlw": // ctrlw <title> <vk> → PostMessage Ctrl+<vk>（Ctrl down/up 包裹）
    {
        nint h = FindWindowW(null, args[1]);
        if (h == 0) { Console.WriteLine("window-not-found"); return; }
        uint vk = Vk(args[2]);
        uint scan = MapVirtualKeyW(vk, 0);
        uint cscan = MapVirtualKeyW(0x11, 0); // VK_CONTROL
        const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_MOD = 0x0020; // lParam bit5=上下文码占位
        PostMessageW(h, WM_KEYDOWN, 0x11, cscan | 1 << 24);
        await Task.Delay(40);
        PostMessageW(h, WM_KEYDOWN, vk, scan | 1 << 24);
        await Task.Delay(60);
        PostMessageW(h, WM_KEYUP, vk, scan | (1u << 24) | (1u << 30) | (1u << 31));
        await Task.Delay(40);
        PostMessageW(h, WM_KEYUP, 0x11, cscan | (1u << 24) | (1u << 30) | (1u << 31));
        Console.WriteLine($"ctrlw {vk} posted to {(long)h}");
        break;
    }
    case "key": // key <vk> → 发给当前前台
    {
        byte vk = (byte)Vk(args[1]);
        keybd_event(vk, 0, 0, 0);
        await Task.Delay(60);
        keybd_event(vk, 0, 2, 0);
        Console.WriteLine($"key {vk} sent");
        break;
    }
    case "dbl": // dbl <vk> [holdMs=60] [gapMs=120] —— 单进程内连按两下（跨进程启动间隔会超出双击窗，必须进程内）
    {
        byte vk = args[1].StartsWith("0x") ? Convert.ToByte(args[1], 16) : byte.Parse(args[1]);
        int hold = args.Length > 2 ? int.Parse(args[2]) : 60;
        int gap = args.Length > 3 ? int.Parse(args[3]) : 120;
        uint scan = MapVirtualKeyW(vk, 0);
        for (int i = 0; i < 2; i++)
        {
            keybd_event(vk, (byte)scan, 0, 0);
            await Task.Delay(hold);
            keybd_event(vk, (byte)scan, 2, 0);
            if (i == 0) await Task.Delay(gap);
        }
        Console.WriteLine($"dbl vk={vk} hold={hold} gap={gap}");
        break;
    }
    case "rclick": // rclick x y —— 右键单击（绝对物理坐标）
    {
        int x = int.Parse(args[1]), y = int.Parse(args[2]);
        int ax = x * 65535 / (System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width - 1);
        int ay = y * 65535 / (System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height - 1);
        mouse_event(0x01 | 0x8000, (uint)ax, (uint)ay, 0, 0);
        await Task.Delay(60);
        mouse_event(0x08, 0, 0, 0, 0); // RIGHTDOWN
        await Task.Delay(60);
        mouse_event(0x10, 0, 0, 0, 0); // RIGHTUP
        Console.WriteLine($"rclick ({x},{y})");
        break;
    }
    case "mouse": // mouse x1 y1 x2 y2 [holdMs]
    {
        int x1 = int.Parse(args[1]), y1 = int.Parse(args[2]);
        int x2 = int.Parse(args[3]), y2 = int.Parse(args[4]);
        int hold = args.Length > 5 ? int.Parse(args[5]) : 350;
        // 绝对坐标走 mouse_event（与真实鼠标同管线，OS 拖拽循环不吃指针加速失真；SetCursorPos 会让拖窗位移超调）
        void AbsMove(int x, int y)
        {
            int ax = x * 65535 / (System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width - 1);
            int ay = y * 65535 / (System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height - 1);
            mouse_event(0x01 | 0x8000, (uint)ax, (uint)ay, 0, 0); // MOVE|ABSOLUTE
        }
        AbsMove(x1, y1);
        await Task.Delay(80);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        // 分步移动模拟拖拽
        for (int i = 1; i <= 8; i++)
        {
            AbsMove(x1 + (x2 - x1) * i / 8, y1 + (y2 - y1) * i / 8);
            await Task.Delay(hold / 8);
        }
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        Console.WriteLine($"mouse ({x1},{y1})->({x2},{y2})");
        break;
    }
    case "postkey": // postkey <标题> <vk> —— PostMessage 直达窗口，绕过输入法
    {
        var hwnd = FindWindowW(null, args[2]);
        if (hwnd == 0) { Console.WriteLine("window-not-found"); return; }
        const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
        PostMessageW(hwnd, WM_KEYDOWN, uint.Parse(args[3]), 0);
        await Task.Delay(40);
        PostMessageW(hwnd, WM_KEYUP, uint.Parse(args[3]), 0);
        Console.WriteLine($"postkey {args[3]} -> {hwnd}");
        break;
    }
            case "rect": // rect <标题> —— 打印窗口矩形（物理像素）
    {
        nint h = FindWindowW(null, args[1]);
        if (h == 0) { Console.WriteLine("window-not-found"); return; }
        GetWindowRect(h, out RECT r);
        Console.WriteLine($"rect {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}");
        break;
    }
    case "fg": // fg —— 打印当前前台窗标题与类名
    {
        nint fgh = GetForegroundWindow();
        var sb2 = new System.Text.StringBuilder(256);
        GetWindowTextW(fgh, sb2, sb2.Capacity);
        var cls2 = new System.Text.StringBuilder(256);
        GetClassNameW(fgh, cls2, cls2.Capacity);
        Console.WriteLine($"FG hwnd={fgh} title=[{sb2}] class=[{cls2}]");
        break;
    }
    case "swallow": // swallow —— 吞键安全自检：本窗文本框 + 自注入 Ctrl+Shift+G，输出文本是否被污染
    {
        var sform = new System.Windows.Forms.Form
        {
            Text = "FC-Swallow",
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
            Location = new Point(420, 420),
        };
        var stb = new System.Windows.Forms.TextBox { Text = "ABCDEF", Width = 240 };
        sform.Controls.Add(stb);
        int stage = 0;
        var stm = new System.Windows.Forms.Timer { Interval = 900 };
        stm.Tick += (_, _) =>
        {
            if (stage == 0)
            {
                stage = 1; // 900ms：按下组合键
                keybd_event(0x11, 0, 0, 0);
                keybd_event(0x10, 0, 0, 0);
                keybd_event(0x47, 0, 0, 0);
                return;
            }
            if (stage == 1) // 1800ms：松开（必须留在 UI 线程同步注入，async 续体会丢键）
            {
                stage = 2;
                stm.Interval = 1400;
                keybd_event(0x47, 0, 2, 0);
                keybd_event(0x10, 0, 2, 0);
                keybd_event(0x11, 0, 2, 0);
                return;
            }
            stm.Stop(); // 3200ms：判定
            Console.WriteLine($"SWALLOW text=[{stb.Text}]");
            sform.Close();
        };
        stm.Start();
        System.Windows.Forms.Application.Run(sform);
        break;
    }
    case "probe": // probe <holdMs> —— 焦点探针：激活带文本框的窗，退出时打印前台归属与文本（验证全局热键不漏字符）
    {
        int holdMs2 = int.Parse(args[1]);
        var form = new System.Windows.Forms.Form
        {
            Text = "FC-Probe",
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
            Location = new Point(600, 600),
            TopMost = true,
        };
        var tb = new System.Windows.Forms.TextBox { Text = "ABCDEF", Width = 240 };
        form.Controls.Add(tb);
        form.Shown += (_, _) => SetForegroundWindow(form.Handle);
        var tmr = new System.Windows.Forms.Timer { Interval = holdMs2 };
        tmr.Tick += (_, _) =>
        {
            tmr.Stop();
            Console.WriteLine($"PROBE fg={GetForegroundWindow() == form.Handle} text=[{tb.Text}]");
            form.Close();
        };
        tmr.Start();
        System.Windows.Forms.Application.Run(form);
        break;
    }
    case "hk": // hk <vk> <holdMs> —— Ctrl+Shift+<vk> 组合按住
    {
        int vk = (int)Vk(args[1]);
        int hold = args.Length > 2 ? int.Parse(args[2]) : 1200;
        keybd_event(0x11, 0, 0, 0);
        keybd_event(0x10, 0, 0, 0);
        keybd_event((byte)vk, 0, 0, 0);
        await Task.Delay(hold);
        keybd_event((byte)vk, 0, 2, 0);
        keybd_event(0x10, 0, 2, 0);
        keybd_event(0x11, 0, 2, 0);
        Console.WriteLine($"hk ctrl+shift+{vk} hold={hold}");
        break;
    }
    case "hkm": // hkm <vk> <ctrl> <shift> <alt> <win> <holdMs> —— 自定义修饰键组合按住（标志 0/1）
    {
        int vk = (int)Vk(args[1]);
        bool ctrl = args[2] == "1", shift = args[3] == "1", alt = args[4] == "1", win = args[5] == "1";
        int hold = args.Length > 6 ? int.Parse(args[6]) : 1200;
        var seq = new List<(byte vk2, uint up)>();
        if (ctrl) seq.Add((0x11, 0));
        if (shift) seq.Add((0x10, 0));
        if (alt) seq.Add((0x12, 0));
        if (win) seq.Add((0x5B, 0));
        seq.Add(((byte)vk, 0));
        foreach (var (vk2, up) in seq) { keybd_event(vk2, 0, up, 0); await Task.Delay(30); }
        await Task.Delay(hold);
        var downSeq = seq.ToList(); downSeq.Reverse();
        foreach (var (vk2, _) in downSeq) { keybd_event(vk2, 0, 2, 0); await Task.Delay(20); }
        Console.WriteLine($"hkm c{ctrl}s{shift}a{alt}w{win}+{vk} hold={hold}");
        break;
    }
    case "dlgsave": // dlgsave <对话框标题> <完整路径> —— 标准另存为：填路径+点保存
    {
        nint dlg = FindDlg(args[1]);
        if (dlg == 0) { Console.WriteLine("dlg-not-found"); return; }
        nint edit = FindWindowExW(dlg, 0, "Edit", null);
        if (edit == 0) edit = FindWindowExW(dlg, 0, "ComboBoxEx32", null);
        if (edit != 0) PostMessageW(edit, 0x000C /*WM_SETTEXT*/, 0, (uint)System.Runtime.InteropServices.Marshal.StringToHGlobalUni(args[2]));
        await Task.Delay(200);
        nint btn = FindWindowExW(dlg, 0, "Button", null); // 第一个按钮=保存
        while (btn != 0)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowTextW(btn, sb, sb.Capacity);
            if (sb.ToString().Contains("保存") || sb.ToString().ToLower().Contains("save")) break;
            btn = FindWindowExW(dlg, btn, "Button", null);
        }
        if (btn != 0) PostMessageW(btn, 0x00F5 /*BM_CLICK*/, 0, 0);
        Console.WriteLine($"dlgsave edit={(long)edit} btn={(long)btn}");
        break;
    }
    static nint FindDlg(string title)
    {
        nint h = FindWindowW("#32770", title);
        if (h != 0) return h;
        // 中文本地化标题不确定：扫描全部 #32770
        nint prev = 0;
        while ((prev = FindWindowExW(0, prev, "#32770", null)) != 0)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowTextW(prev, sb, sb.Capacity);
            if (sb.ToString().Contains(title) || sb.ToString().ToLower().Contains("save")) return prev;
        }
        return 0;
    }
    case "hotkey": // hotkey <holdMs>  Ctrl+Shift+G
    {
        int hold = args.Length > 1 ? int.Parse(args[1]) : 1500;
        keybd_event(0x11, 0, 0, 0);
        keybd_event(0x10, 0, 0, 0);
        keybd_event(0x47, 0, 0, 0);
        await Task.Delay(hold);
        keybd_event(0x47, 0, 2, 0);
        keybd_event(0x10, 0, 2, 0);
        keybd_event(0x11, 0, 2, 0);
        Console.WriteLine($"hotkey done hold={hold}");
        break;
    }
}

[StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
