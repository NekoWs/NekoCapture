# 编辑器也推开同一层换色遮罩的取证：真进程里开着一张样图，把主题从暗翻到亮，本进程逐帧截屏。
# 编辑器中间是样图与画布四周那圈固定中性，换色不动它们；会跟着换色的是底下那条工具条与两侧面板。
# 于是两块取样都落在工具条上：一块贴着横向中点（离圆心近），一块在左下角（离圆心远）。
# 圆从窗口中心推开 → 中点那块先翻、左下角后翻；整片淡变或没有遮罩时两块一起翻。
# 用法：pwsh -NoProfile -File scripts/cap-mask-editor.ps1 [-From dark] [-To light]
param(
    [string]$OutDir = 'test-out/mask-editor',
    [string]$SettingsDir = 'test-out/mask-editor-settings',
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

$user = Join-Path $env:APPDATA 'FrameCapture\settings.json'
$scratchFile = Join-Path $scratch 'settings.json'
if (Test-Path $user) { Copy-Item $user $scratchFile -Force }
$set = Get-Content $scratchFile -Raw | ConvertFrom-Json
$set.Theme = $From
$set | ConvertTo-Json -Depth 40 | Set-Content $scratchFile -Encoding utf8

Add-Type -AssemblyName System.Drawing
# 一张有明暗两半的样图：压在图上的界面在两侧各被量一次
$sample = Join-Path $out 'sample.png'
$bmp = [System.Drawing.Bitmap]::new(520, 320)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.FillRectangle([System.Drawing.Brushes]::White, 0, 0, 260, 320)
$g.FillRectangle([System.Drawing.Brushes]::Black, 260, 0, 260, 320)
$g.FillRectangle([System.Drawing.Brushes]::Tomato, 40, 60, 180, 90)
$g.FillRectangle([System.Drawing.Brushes]::SteelBlue, 300, 170, 180, 90)
$g.Dispose()
$bmp.Save($sample, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

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
[void][RaiseWin]::SetProcessDPIAware()

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
# 第一帧越过两档中点的帧号；翻的方向由两端亮度大小定
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
$p = Start-Process $exe -ArgumentList @('--editor', $sample) -PassThru
try {
    $found = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 250
        & $cap win 'FrameCapture Editor' (Join-Path $out 'wait.png') 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $found = $true; break }
    }
    if (-not $found) { throw '没等到编辑器' }
    Start-Sleep -Milliseconds 1200
    [void][RaiseWin]::Go('FrameCapture Editor')
    Start-Sleep -Milliseconds 250
    $rc = [RaiseWin]::Rect('FrameCapture Editor')
    if (-not $rc) { throw '量不到编辑器' }

    $base = Grab $rc[0] $rc[1] $rc[2] $rc[3]
    # 两块都压在底部工具条上：中点那块离圆心近，左下角那块离圆心远
    $mx0 = [int]($rc[2] / 2) - 70; $mx1 = [int]($rc[2] / 2) + 70
    $my0 = $rc[3] - 46; $my1 = $rc[3] - 8
    $nx0 = 8; $nx1 = 96
    $startM = Get-Luma $base $mx0 $my0 $mx1 $my1
    $startN = Get-Luma $base $nx0 $my0 $nx1 $my1
    $base.Save((Join-Path $out ("mask-000-{0}.png" -f $From)))
    $base.Dispose()

    $set.Theme = $To
    $set | ConvertTo-Json -Depth 40 | Set-Content $scratchFile -Encoding utf8

    $mid = @($startM); $corner = @($startN)
    for ($i = 1; $i -le $Frames; $i++) {
        Start-Sleep -Milliseconds $GapMs
        $f = Grab $rc[0] $rc[1] $rc[2] $rc[3]
        $mid += Get-Luma $f $mx0 $my0 $mx1 $my1
        $corner += Get-Luma $f $nx0 $my0 $nx1 $my1
        if ($i % $KeepEvery -eq 0) { $f.Save((Join-Path $out ("mask-{0:d3}.png" -f $i))) }
        $f.Dispose()
    }
    $endM = $mid[-1]; $endN = $corner[-1]
    Write-Host ("{0} → {1}：工具条中点 {2} → {3}    左下角 {4} → {5}" -f $From, $To, $startM, $endM, $startN, $endN)
    Write-Host ("中点序列：" + ($mid -join ' '))
    Write-Host ("左下角序列：" + ($corner -join ' '))
    $arrM = Get-Arrival $mid $startM $endM
    $arrN = Get-Arrival $corner $startN $endN
    Write-Host ("中点越过中点在第 {0} 帧，左下角在第 {1} 帧（早 {2} 帧）" -f $arrM, $arrN, ($arrN - $arrM))
    if (($arrN - $arrM) -ge 3) { Write-Host 'RIPPLE VISIBLE' } else { Write-Host 'NO RIPPLE' }
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
