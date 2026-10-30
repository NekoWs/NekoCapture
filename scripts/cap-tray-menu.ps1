# 托盘右键菜单双主题像素档案。
# 依据：托盘菜单不是系统原生渲染——TrayService.ShowMenu 用 WPF ContextMenu + Fluent.MenuResources()
#       自绘并随主题着色（见 src/FrameCapture/Services/TrayService.cs）。像素取证完全可行。
# 机制（全部在自动化边界内）：对自家 NotifyIcon 隐藏窗 PostMessage 托盘回调消息
#       （.NET(8+) WinForms NotifyIcon.WndProc：msg=WM_USER+1024=0x800，鼠标事件码在 lParam——
#        见 dotnet/winforms NotifyIcon.cs WM_TRAYMOUSEMESSAGE；旧文档的 WM_USER+102 是 .NET Framework 时代的值）
#       → MouseUp(Right) → ShowMenu 在光标处弹出 Fluent 菜单；随后对弹窗 HWND 调 PrintWindow 存 PNG。
#       绝不使用 OS 级鼠标自动化。
# 为什么不能用桌面 BitBlt：菜单窗是 WPF 分层窗（AllowsTransparency），屏幕 DC 的 BitBlt 不含分层窗内容，
#       截回来是一整块桌面背景，看起来像"菜单是空白的"。PrintWindow 带 PW_RENDERFULLCONTENT 才拿到内容。
# 为什么要折算矩形：宿主进程对 DPI 无感知时 GetWindowRect 返回按缩放折算过的虚拟坐标，
#       按它开位图就截掉右下——窄文案（中文）看着完整，宽文案（英文）每项都被切一刀。
# 用法: pwsh -NoProfile -File scripts/cap-tray-menu.ps1 -Theme dark|light [-OutPng test-out/uiaudit/d8-tray-menu-dark.png]
# 前置：本脚本会杀掉运行中的 FrameCapture 并以 FC_THEME=<Theme> 重启一个（测试态）；结束后实例仍在运行。
param(
    [Parameter(Mandatory)][ValidateSet('dark', 'light')][string]$Theme,
    [string]$OutPng
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
if (-not $OutPng) { $OutPng = Join-Path $root "test-out/uiaudit/d8-tray-menu-$Theme.png" }

try { Add-Type -AssemblyName System.Drawing.Common } catch { Add-Type -AssemblyName System.Drawing }
$src = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace FcTray
{
public class T
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
    const uint WM_USER = 0x0400, WM_TRAYMOUSEMESSAGE = WM_USER + 1024 /* 0x0800 */,
               WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    // 自家进程所有不可见的 WinForms 类顶层窗：NotifyIcon 隐藏窗必然在其中（其余窗会忽略右键消息，无害）
    public static IntPtr[] FindNotifyIconCandidates(uint pid)
    {
        IntPtr[] hits = new IntPtr[8];
        int n = 0;
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || IsWindowVisible(h)) return true;
            var cls = new StringBuilder(256); GetClassNameW(h, cls, 256);
            if (cls.ToString().StartsWith("WindowsForms10.Window"))
            {
                if (n == hits.Length) { var bigger = new IntPtr[hits.Length * 2]; Array.Copy(hits, bigger, hits.Length); hits = bigger; }
                hits[n++] = h;
            }
            return true;
        }, IntPtr.Zero);
        var exact = new IntPtr[n];
        Array.Copy(hits, exact, n);
        return exact;
    }

    // 自家进程可见、无标题、尺寸像菜单的顶层窗（WPF Popup 宿主窗），取面积最大者
    public static long gArea; public static RECT gBest; public static IntPtr gHwnd;
    public static bool FindMenuPopup(uint pid)
    {
        gArea = 0; gHwnd = IntPtr.Zero;
        bool any = false;
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var t = new StringBuilder(8); GetWindowTextW(h, t, 8);
            if (t.Length != 0) return true;
            RECT r;
            if (!GetWindowRect(h, out r)) return true;
            int w = r.Right - r.Left, hh = r.Bottom - r.Top;
            if (w < 60 || hh < 60) return true; // 排除 1x1 锚点窗与 0 尺寸隐藏窗
            long area = (long)w * hh;
            if (area > gArea) { gArea = area; gBest = r; gHwnd = h; any = true; }
            return true;
        }, IntPtr.Zero);
        return any;
    }

    public static void PostRightClick(IntPtr hwnd)
    {
        // 托盘回调：事件码放 lParam（.NET 8 NotifyIcon 按 LParamInternal 分发）
        PostMessageW(hwnd, WM_TRAYMOUSEMESSAGE, IntPtr.Zero, (IntPtr)WM_RBUTTONDOWN);
        PostMessageW(hwnd, WM_TRAYMOUSEMESSAGE, IntPtr.Zero, (IntPtr)WM_RBUTTONUP);
    }

    // 弹窗的真实物理矩形：进程无 DPI 感知时 GetWindowRect 给的是折算过的虚拟坐标，直接开位图会截掉右下
    public static RECT PhysicalRect(IntPtr h)
    {
        RECT r; GetWindowRect(h, out r);
        if (GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) != 0) return r;
        uint dpi = GetDpiForWindow(h);
        if (dpi == 0 || dpi == 96) return r;
        double k = dpi / 96.0;
        RECT o = r;
        o.Left = (int)Math.Round(r.Left * k); o.Top = (int)Math.Round(r.Top * k);
        o.Right = (int)Math.Round(r.Right * k); o.Bottom = (int)Math.Round(r.Bottom * k);
        return o;
    }

    // PW_RENDERFULLCONTENT = 2：WPF 经 DirectComposition 呈现，不带这个标志 PrintWindow 画不出内容
    public static bool Grab(IntPtr hwnd, string outPath)
    {
        RECT r = PhysicalRect(hwnd);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w < 8 || h < 8) return false;
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                bool ok = PrintWindow(hwnd, hdc, 2);
                g.ReleaseHdc(hdc);
                if (!ok) return false;
            }
            bmp.Save(outPath, ImageFormat.Png);
        }
        return true;
    }
}
}
'@
$pwshDir = Split-Path ([System.Drawing.Image]).Assembly.Location
$refs = @(
    ([object]).Assembly.Location,                                              # System.Private.CoreLib（含 DllImport 等元数据）
    ([System.Collections.Generic.List[object]]).Assembly.Location,             # System.Collections
    ([System.Drawing.Bitmap]).Assembly.Location,                               # System.Drawing.Common
    # .NET 10+ 把 Windows 图形实现拆进 System.Private.Windows.*：编译期必须显式引用
    ([System.Reflection.Assembly]::LoadFrom((Join-Path $pwshDir 'System.Private.Windows.GdiPlus.dll'))).Location,
    ([System.Reflection.Assembly]::LoadFrom((Join-Path $pwshDir 'System.Private.Windows.Core.dll'))).Location
) | Sort-Object -Unique
Add-Type -TypeDefinition $src -ReferencedAssemblies $refs

Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 0.6
$env:FC_THEME = $Theme
$p = Start-Process $exe -PassThru
Remove-Item Env:\FC_THEME -ErrorAction SilentlyContinue
Start-Sleep 1.8   # 等托盘图标建好

$hits = [FcTray.T]::FindNotifyIconCandidates([uint32]$p.Id)
if ($hits.Count -eq 0) { Write-Host "TRAY-CAP FAIL: 找不到 NotifyIcon 隐藏窗 (pid=$($p.Id))"; exit 1 }
$cur = New-Object FcTray.T+POINT
[void][FcTray.T]::GetCursorPos([ref]$cur)
foreach ($hw in $hits) { [FcTray.T]::PostRightClick($hw) }
Write-Host "posted WM_RBUTTONDOWN/UP -> $($hits.Count) hidden WinForms hwnd(s) cursor=($($cur.X),$($cur.Y))"
# 菜单弹出后逐次尝试：分层窗的内容要等 WPF 画完第一帧才拿得到，入场动画也在跑
New-Item -ItemType Directory -Force -Path (Split-Path $OutPng) | Out-Null
$saved = $false
foreach ($delay in 250, 500, 900, 1500) {
    Start-Sleep -Milliseconds $delay
    if (-not [FcTray.T]::FindMenuPopup([uint32]$p.Id)) { continue }
    if ([FcTray.T]::Grab([FcTray.T]::gHwnd, $OutPng)) {
        $r = [FcTray.T]::PhysicalRect([FcTray.T]::gHwnd)
        Write-Host "TRAY-CAP PASS saved $OutPng $($r.Right-$r.Left)x$($r.Bottom-$r.Top) after ${delay}ms"
        $saved = $true; break
    }
}
if (-not $saved) { Write-Host "TRAY-CAP FAIL: 菜单弹窗未出现或 PrintWindow 未取到内容"; exit 1 }
exit 0
