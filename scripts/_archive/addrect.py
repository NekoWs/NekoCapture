p='tools/Shot/Program.cs'
s=open(p,encoding='utf-8').read()
add='''    case "rect": // rect <标题> —— 打印窗口矩形（物理像素）
    {
        nint h = FindWindowW(null, args[1]);
        if (h == 0) { Console.WriteLine("window-not-found"); return; }
        GetWindowRect(h, out RECT r);
        Console.WriteLine($"rect {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}");
        break;
    }
    case "hk":'''
assert 'case "hk":' in s and '"rect"' not in s
s=s.replace('case "hk":',add,1)
if 'GetWindowRect' not in s:
    s=s.replace('[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindowExW',
'''[StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] static extern bool GetWindowRect(nint h, out RECT r);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindowExW''')
open(p,'w',encoding='utf-8').write(s)
print('rect added')
