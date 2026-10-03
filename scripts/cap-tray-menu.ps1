# D8 取证：托盘右键菜单双主题像素档案（UIAUDIT.md D8）
# 依据：托盘菜单不是系统原生渲染——TrayService.ShowMenu 用 WPF ContextMenu + Fluent.MenuResources()
#       自绘并随主题着色（见 src/FrameCapture/Services/TrayService.cs）。像素取证完全可行。
# 机制（全部在自动化边界内）：对自家 NotifyIcon 隐藏窗 PostMessage WM_RBUTTONUP → MouseUp 右键 →
#       ShowMenu 在光标处弹出；随后按弹窗窗口矩形裁切桌面 BitBlt 存 PNG。绝不使用 OS 级鼠标自动化。
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
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int idx);
    [DllImport("gdi32.dll")] public static extern IntPtr BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint op);
    const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, SRCCOPY = 0xCC0020;
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
    static long gArea; static RECT gBest;
    public static bool FindMenuPopup(uint pid)
    {
        gArea = 0;
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
            if (area > gArea) { gArea = area; gBest = r; any = true; }
            return true;
        }, IntPtr.Zero);
        return any;
    }

    public static void PostRightClick(IntPtr hwnd)
    {
        PostMessageW(hwnd, WM_RBUTTONDOWN, IntPtr.Zero, IntPtr.Zero);
        PostMessageW(hwnd, WM_RBUTTONUP, IntPtr.Zero, IntPtr.Zero);
    }

    public static void CropScreen(int x, int y, int w, int h, string outPath)
    {
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            IntPtr sdc = GetDC(IntPtr.Zero);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                BitBlt(hdc, 0, 0, w, h, sdc, x, y, SRCCOPY);
                g.ReleaseHdc(hdc);
            }
            ReleaseDC(IntPtr.Zero, sdc);
            bmp.Save(outPath, ImageFormat.Png);
        }
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
Start-Sleep -Milliseconds 700   # 菜单弹出 + 渲染

if (-not [FcTray.T]::FindMenuPopup([uint32]$p.Id)) {
    Write-Host "TRAY-CAP FAIL: 菜单弹窗未出现——检查 FC_THEME 实例是否正常启动"; exit 1
}
$rc = [FcTray.T]::gBest
$pad = 28
$swPix = [FcTray.T]::GetSystemMetrics(0); $shPix = [FcTray.T]::GetSystemMetrics(1)
$x = [Math]::Max(0, $rc.Left - $pad); $y = [Math]::Max(0, $rc.Top - $pad)
$rr = [Math]::Min($swPix, $rc.Right + $pad); $bb = [Math]::Min($shPix, $rc.Bottom + $pad)
$w = $rr - $x; $h = $bb - $y
New-Item -ItemType Directory -Force -Path (Split-Path $OutPng) | Out-Null
[FcTray.T]::CropScreen($x, $y, $w, $h, $OutPng)
Write-Host "TRAY-CAP PASS saved $OutPng ${w}x${h} @($x,$y) menu=$($rc.Left),$($rc.Top),$($rc.Right),$($rc.Bottom)"
exit 0
