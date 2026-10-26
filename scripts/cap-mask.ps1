# 换色遮罩取证：真进程里把主题从一档翻到另一档（默认暗→亮，-From light -To dark 反过来），
# 连拍一串帧，逐帧量「窗口中心」与「远角」两块的平均亮度。
# 深浅互换时设置窗从圆心推开一层：中心先翻、远角后翻，两条曲线之间拉开一段距离。
# 整片淡变的话两块同时翻，两条曲线会贴在一起；没有遮罩则两条都是直接跳档，中间不留过渡值。
# 逐帧用本进程截屏（每帧十几毫秒）：另起进程去截，一帧的开销比整段动画还长，采不到边界走过的那几帧。
# 用法：pwsh -NoProfile -File scripts/cap-mask.ps1 [-From dark] [-To light]
param(
    [string]$OutDir = 'test-out/mask',
    [string]$SettingsDir = 'test-out/mask-settings',
    [string]$From = 'dark',
    [string]$To = 'light',
    [int]$Frames = 60,
    [int]$GapMs = 12,
    [int]$KeepEvery = 6
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $root $SettingsDir
$out = Join-Path $root $OutDir
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
New-Item -ItemType Directory -Force -Path $out | Out-Null
Remove-Item (Join-Path $out '*.png') -Force -ErrorAction SilentlyContinue

$exe = Join-Path $root 'src\FrameCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\FrameCapture.exe'
if (-not (Test-Path $exe)) { throw "找不到 $exe" }
$cap = Join-Path $root 'tools\UICap\bin\Release\net8.0-windows\UICap.exe'
if (-not (Test-Path $cap)) { $cap = Join-Path $root 'tools\UICap\bin\Debug\net8.0-windows\UICap.exe' }

# 起点用用户那份设置，整份搬进 scratch，绝不写回用户目录
$user = Join-Path $env:APPDATA 'FrameCapture\settings.json'
$scratchFile = Join-Path $scratch 'settings.json'
if (Test-Path $user) { Copy-Item $user $scratchFile -Force }
$set = Get-Content $scratchFile -Raw | ConvertFrom-Json
$set.Theme = $From
$set | ConvertTo-Json -Depth 40 | Set-Content $scratchFile -Encoding utf8

if (-not ('RaiseWin' -as [type])) {
    Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class RaiseWin {
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string a,string b);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint f);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 public static bool Go(string title) {
   var h = FindWindow(null, title);
   if (h == IntPtr.Zero) return false;
   SetForegroundWindow(h);
   return SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002);   // TOPMOST | NOMOVE | NOSIZE
 }
 public static int[] Rect(string title) {
   var h = FindWindow(null, title);
   if (h == IntPtr.Zero) return null;
   R r; if (!GetWindowRect(h, out r)) return null;
   return new int[] { r.L, r.T, r.Rt - r.L, r.B - r.T };
 }
}
'@
}
# 先声明 DPI 感知：不声明时 pwsh 拿到的是缩放过的坐标，量到的窗口比实际小一圈，截到的画面也偏了
[void][RaiseWin]::SetProcessDPIAware()
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# 本进程截一块屏幕：省掉每帧起进程的开销，才采得到边界走过的那几帧
function Grab([int]$x, [int]$y, [int]$w, [int]$h) {
    $b = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($x, $y, 0, 0, [System.Drawing.Size]::new($w, $h))
    $g.Dispose()
    return $b
}
function Get-Luma([System.Drawing.Bitmap]$bmp, [int]$x0, [int]$y0, [int]$x1, [int]$y1) {
    $sum = 0.0; $n = 0
    for ($y = $y0; $y -lt [Math]::Min($y1, $bmp.Height); $y += 3) {
        for ($x = $x0; $x -lt [Math]::Min($x1, $bmp.Width); $x += 3) {
            $c = $bmp.GetPixel($x, $y)
            $sum += 0.2126 * $c.R + 0.7152 * $c.G + 0.0722 * $c.B
            $n++
        }
    }
    return [math]::Round($sum / [Math]::Max(1, $n), 1)
}
# 第一帧越过两档中点的帧号：这块面「什么时候翻过去」看的是中点，不是终点——
# 中心那块里有正文和卡片，亮度是混合值，用终点当门槛会把几帧的先后抹平。
# 翻的方向由两端的亮度大小定：亮翻暗时序列往下走，门槛要往下找。
function Get-Arrival($series, [double]$from, [double]$to) {
    $mid = ($from + $to) / 2
    $up = $to -gt $from
    for ($i = 0; $i -lt $series.Count; $i++) {
        if ($up) { if ($series[$i] -ge $mid) { return $i } }
        else { if ($series[$i] -le $mid) { return $i } }
    }
    return 999
}

Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 350
$env:FC_SETTINGS_DIR = $scratch
$env:FC_THEME = $From
$env:FC_SETTINGS_PAGE = 'appearance'
$p = Start-Process $exe -ArgumentList '--settings' -PassThru
try {
    $found = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 250
        & $cap win 'FrameCapture Settings' (Join-Path $out 'wait.png') 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $found = $true; break }
    }
    if (-not $found) { throw '没等到设置窗' }
    Start-Sleep -Milliseconds 900
    [void][RaiseWin]::Go('FrameCapture Settings')
    Start-Sleep -Milliseconds 250
    $rc = [RaiseWin]::Rect('FrameCapture Settings')
    if (-not $rc) { throw '量不到设置窗' }

    $base = Grab $rc[0] $rc[1] $rc[2] $rc[3]
    # 圆心在窗口正中：圆先盖住中心，最后才扫到角
    $cx0 = [int]($rc[2] / 2) - 70; $cx1 = [int]($rc[2] / 2) + 70
    $cy0 = [int]($rc[3] / 2) - 45; $cy1 = [int]($rc[3] / 2) + 45
    $nx0 = 8; $nx1 = 96; $ny0 = $rc[3] - 96; $ny1 = $rc[3] - 8
    $startC = Get-Luma $base $cx0 $cy0 $cx1 $cy1
    $startN = Get-Luma $base $nx0 $ny0 $nx1 $ny1
    $base.Save((Join-Path $out ("mask-000-{0}.png" -f $From)))
    $base.Dispose()

    # 翻到另一档：进程按 settings.json 的外部改动热重载（500ms 防抖），遮罩在应用新调色板那一刻从圆心推开
    $set.Theme = $To
    $set | ConvertTo-Json -Depth 40 | Set-Content $scratchFile -Encoding utf8

    $centre = @($startC); $corner = @($startN)
    for ($i = 1; $i -le $Frames; $i++) {
        Start-Sleep -Milliseconds $GapMs
        $f = Grab $rc[0] $rc[1] $rc[2] $rc[3]
        $centre += Get-Luma $f $cx0 $cy0 $cx1 $cy1
        $corner += Get-Luma $f $nx0 $ny0 $nx1 $ny1
        if ($i % $KeepEvery -eq 0) { $f.Save((Join-Path $out ("mask-{0:d3}.png" -f $i))) }
        $f.Dispose()
    }
    $endC = $centre[-1]; $endN = $corner[-1]
    Write-Host ("{0} → {1}：中心 {2} → {3}    远角 {4} → {5}" -f $From, $To, $startC, $endC, $startN, $endN)
    Write-Host ("中心序列：" + ($centre -join ' '))
    Write-Host ("远角序列：" + ($corner -join ' '))
    $arrC = Get-Arrival $centre $startC $endC
    $arrN = Get-Arrival $corner $startN $endN
    Write-Host ("中心越过中点在第 {0} 帧，远角在第 {1} 帧（早 {2} 帧）" -f $arrC, $arrN, ($arrN - $arrC))
    if (($arrN - $arrC) -ge 3) { Write-Host 'RIPPLE VISIBLE' } else { Write-Host 'NO RIPPLE' }
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
