p='tools/Shot/Program.cs'
s=open(p,encoding='utf-8').read()
add='''    case "hk": // hk <vk> <holdMs> —— Ctrl+Shift+<vk> 组合按住
    {
        int vk = int.Parse(args[1]);
        int hold = args.Length > 2 ? int.Parse(args[2]) : 1200;
        keybd_event(0x11, 0, 0, 0);
        keybd_event(0x10, 0, 0, 0);
        keybd_event((uint)vk, 0, 0, 0);
        await Task.Delay(hold);
        keybd_event((uint)vk, 0, 2, 0);
        keybd_event(0x10, 0, 2, 0);
        keybd_event(0x11, 0, 2, 0);
        Console.WriteLine($"hk ctrl+shift+{vk} hold={hold}");
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
    case "hotkey":'''
assert 'case "hotkey":' in s and '"dlgsave"' not in s
s=s.replace('case "hotkey":',add,1)
s=s.replace('[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostMessageW(nint h, uint msg, uint wParam, uint lParam);',
'[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostMessageW(nint h, uint msg, uint wParam, uint lParam);\n[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);\n[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(nint h, System.Text.StringBuilder buf, int max);')
open(p,'w',encoding='utf-8').write(s)
print('modes added')
